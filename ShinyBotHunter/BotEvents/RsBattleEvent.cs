using System.Diagnostics;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    // The Ruby and Sapphire battle of a wild encounter, from BattleStart to BattleComplete or Waiting (docs/specs/rs-wild-encounter.md
    // FR-9 to FR-11, FR-14; docs/research/rs-wild-encounter.md "Presses", "Tuned for speed", "A failed escape"): A at each
    // message, the read at the first action menu, then RUN. In Battle mode it fights instead, first switching to the
    // battler when training Switch, and answers the level-up box and the new-move prompt, then the evolution scene's texts
    // and prompt (docs/specs/rs-navigation-and-training.md FR-13 to FR-17, FR-18c), and after a faint with another alive,
    // No at "Use next POKéMON?", then if that flee fails the fastest alive sent out and RUN once (FR-18). Every press is sent from the snapshot its rule needs and confirmed by the byte it changes; anything
    // else stops the bot in Waiting for the operator.
    public class RsBattleEvent : AbstractBotEvent<GameState>
    {
        internal const string NotWild = SpinEncounterEvent.NotAWildBattle;
        internal const string Fainted = "The first Pokémon fainted.";
        internal const string Trapped = "Could not run from this battle.";
        internal const string Corrupt = "The wild Pokémon's record failed its checksum for 2 s.";
        internal const string RunFailed = "A Pokémon fainted, and running from the battle failed after the fastest Pokémon left was sent out.";
        internal const string NoneAlive = "A Pokémon fainted, and the party menu read showed none able to battle.";

        // The battle bit after the field leaves free; how long the snapshot may stay the same in battle; how long a record
        // that fails its checksum is read again; how long the cursor may take to read RUN before the missing press is sent.
        private static readonly TimeSpan BattleStartStale = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ChecksumWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan CursorWait = TimeSpan.FromSeconds(1);

        // Presses on RUN in one battle before it counts as a trap: the escape chance rises with each try.
        internal const int MaxRunPresses = 12;

        // Switch tries in one battle before it is fled, as HGSS's MaxSwitchAttempts. A try is one pass from the action menu
        // to the next with 0x02024A6A not on the battler's slot.
        private const int MaxSwitchTries = 3;

        // The two halves of BattleCursorToRun, each sent alone to a cursor the other one already moved; a test pins them to
        // the registry text.
        internal const string Right = "H.Right.2";
        internal const string Down = "H.Down.2";

        // The other single presses the training rules send, each a 2-frame hold as the two above.
        internal const string Up = "H.Up.2";
        internal const string Left = "H.Left.2";
        internal const string B = "H.B.2";

        // gBattleTypeFlags: wild, trainer, Safari Zone.
        private const ushort WildBattle = 0x0004;
        private const ushort TrainerBattle = 0x0008;
        private const ushort SafariBattle = 0x0080;

        // gMain.inBattle's bit in the InBattle byte; the script status with no script running (research "A trainer's intro
        // speech"); a trainer's flag is 0x500 + its id.
        private const byte InBattleBit = 0x02;
        private const byte NoScript = 2;
        private const int TrainerFlagBase = 0x500;

        // The action cursor on FIGHT, on POKéMON and on RUN.
        private const int FightCursor = 0;
        private const int PokemonCursor = 2;
        private const int RunCursor = 3;

        // A party record's current HP: pokeruby struct Pokemon.hp, Gen3PokemonParser's HpOffset.
        private const int LeadHpOffset = 0x56;

        private readonly PokemonPersistenceService persistence;
        private readonly ShinyAlertService alerts;
        private readonly LegalityService legality;
        private readonly PokemonMetadataService metadata;
        private readonly Func<PokemonBattleState, int> pickMove;
        private readonly Func<PokemonData, PokemonLevelUpMoveMetadata, Task<int>> decideMove;

        // pickMove and decideMove reach the damage calculator and Discord only when called, so a flee-only hunt builds
        // neither.
        public RsBattleEvent(PokemonPersistenceService persistence, ShinyAlertService alerts, LegalityService legality,
            PokemonMetadataService metadata, Func<PokemonBattleState, int> pickMove,
            Func<PokemonData, PokemonLevelUpMoveMetadata, Task<int>> decideMove)
        {
            this.persistence = persistence;
            this.alerts = alerts;
            this.legality = legality;
            this.metadata = metadata;
            this.pickMove = pickMove;
            this.decideMove = decideMove;
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // The seek hands over a lock that has lasted SeekLockHandOff, so a lock that is not a trainer waits for the battle
            // bit only for what is left of 15 s from the lock (FR-9). A battle that trains reads through its own reader with
            // the training reads, built per battle.
            RsPointerService addresses = (RsPointerService)state.Bot.Addresses;
            IGameStateReader? trainingReader = state.BattleMode == BattleMode.Battle ? new RsStateReader(state.Bot, addresses, trainingReads: true) : null;
            await RunAsync(state, addresses, trainingReader, persistence.AddEncounterAsync, p => alerts.NotifyTargetFoundAsync(p),
                s => alerts.NotifyStopAsync(s),
                raw => legality.Analyze(raw, GameFlavor.RS), pickMove, metadata.GetPokemonMoveMetadata, decideMove,
                BattleStartStale, BattleStartStale - state.Bot.Profile.SeekLockHandOff!.Value,
                Stale, ChecksumWindow, CursorWait, cancellationToken);
            return state;
        }

        // The battle. Null when it is over (BattleComplete) or parked on a target (Waiting), otherwise the sentence it stopped
        // with (Waiting). trainingReader is null in Flee mode. The delegates are the encounter log, the two alerts (a target,
        // and the trainer and run-failed stops), PKHeX, the damage picker, the move collection and the new-move question. The
        // times are parameters so the tests run short: afterHandOff is the battle bit's wait once a lock the seek handed over
        // is not a trainer.
        internal static async Task<string?> RunAsync(GameState state, RsPointerService addresses, IGameStateReader? trainingReader,
            Func<PersistPokemonData, Task> addEncounter, Func<PersistPokemonData, Task> notifyTargetFound,
            Func<string, Task> notifyStop,
            Func<byte[], LegalityResult> analyze, Func<PokemonBattleState, int> pickMove, Func<int, PokemonMoveMetadata> moveMetadata,
            Func<PokemonData, PokemonLevelUpMoveMetadata, Task<int>> decideMove,
            TimeSpan battleStart, TimeSpan afterHandOff, TimeSpan stale, TimeSpan checksumWindow,
            TimeSpan cursorWait, CancellationToken ct)
        {
            IBot bot = state.Bot;
            // The entry, the trainer read, the hand-off and the battle type read go through the flee reader: its field
            // snapshot does not change while text prints, so it cannot restart their stale clocks (N5 review).
            IGameStateReader reader = bot.State;
            GameSnapshot s;
            try
            {
                s = await GameStateWait.UntilAsync(reader, "the battle bit after the field left free",
                    x => x.Screen is not (GameScreen.Transitioning or GameScreen.FieldFree), battleStart, ct);
                if (s.Screen == GameScreen.FieldTask)
                {
                    // A trainer's battle type is set only after a press at the speech, so the trainer is read from the lock.
                    if (await ReadTrainerChallengeAsync(bot, addresses, ct) is { } trainer)
                    {
                        return await TrainerStopAsync(state, trainer.Group, trainer.Number, $"trainer {trainer.Trainer}, {s}", notifyStop);
                    }
                    s = await GameStateWait.UntilAsync(reader, "the battle bit after the field left free",
                        x => x.Screen is not (GameScreen.Transitioning or GameScreen.FieldFree or GameScreen.FieldTask), afterHandOff, ct);
                }
            }
            catch (GameStateTimeoutException ex)
            {
                return Stop(state, NotWild, ex.Last.ToString());
            }
            Stopwatch battleTime = Stopwatch.StartNew();
            ChainBlockResult[] entry = await bot.ReadChainBlocksAsync(
                [new(addresses.BattleTypeFlags, [], 2), new(addresses.VBlankCounter, [], 4)], ct: ct);
            ushort flags = BitConverter.ToUInt16(entry[0].Bytes, 0);
            uint startFrame = BitConverter.ToUInt32(entry[1].Bytes, 0);
            if ((flags & TrainerBattle) != 0)
            {
                // A press at the speech, the operator's, came before the hand-off. A trainer stop's own battle never gets
                // here: Resume after it waits for the free field.
                ChainBlockResult[] at = await bot.ReadChainBlocksAsync([new(addresses.PlayerLocation, [], 2)], ct: ct);
                return await TrainerStopAsync(state, at[0].Bytes[0], at[0].Bytes[1], $"battle type 0x{flags:X4}, {s}", notifyStop);
            }
            if ((flags & WildBattle) == 0 || (flags & SafariBattle) != 0)
            {
                return Stop(state, NotWild, $"battle type 0x{flags:X4}, {s}");
            }
            // A wild battle: one that trains reads the rest of it through the training reader.
            reader = trainingReader ?? reader;

            // Battle mode fights until its rule says to flee; from then on, and in Flee mode, the menu runs RUN.
            bool training = state.BattleMode == BattleMode.Battle;
            bool fleeing = !training;
            bool seenMenu = false;
            int runPresses = 0;
            int presses = 0;
            // Switch training (FR-17): the battler's 0-based party slot, switched to until 0x02024A6A reads it at an action
            // menu. One RunAsync is one battle, so GameState.SwitchPerformed is not used.
            int? switchSlot = training && state.TrainType == TrainType.Switch ? state.SwitchSlot!.Value - 1 : null;
            bool switched = switchSlot is null;
            int switchTries = 0;
            // A faint with another alive (FR-18; research "A battler faints: the readings"): sentOut once the fastest alive
            // was sent out from the menu the failed flee forced, and runTriesBefore read at the one RUN after it.
            bool sentOut = false;
            int? runTriesBefore = null;
            try
            {
                while (true)
                {
                    // Before this battle's first menu the outcome can still be the last battle's: it stays until the next
                    // battle starts.
                    // The level-up box, the move prompt, the evolution scene, "Use next POKéMON?" and the forced party menu
                    // come only from the training reader. A forget-a-move screen met here, outside a Yes, is not waited for:
                    // it ends in the stale stop that names it.
                    // In the scene only its text waits and its yes/no are taken (FR-16): its animation, its first text
                    // (data[0] 2) and the window's holds at 4 and 10 get nothing.
                    s = await GameStateWait.UntilAsync(reader,
                        "a battle message, the action menu, an evolution text wait or yes/no, Use next POKéMON?, the party menu after a faint, or the free field",
                        x => x.Screen is GameScreen.BattleMessage or GameScreen.BattleMenu or GameScreen.FieldFree or GameScreen.BattleExpPrompt
                                or GameScreen.BattleMoveLearnPrompt or GameScreen.BattleScript
                            || (x.Screen == GameScreen.Evolution && ((x.TextPrinter is int w && RsStateReader.ButtonWaits.Contains(w)) || x.Question is not null))
                            || (x.Screen == GameScreen.OtherApp && x.TaskFunction == addresses.BattlePartyMenuTask)
                            || (seenMenu && x.Outcome == BattleOutcome.Lost), stale, ct);
                    Stopwatch since = Stopwatch.StartNew();
                    if (seenMenu && s.Outcome == BattleOutcome.Lost)
                    {
                        return Stop(state, Fainted, s.ToString());
                    }
                    if (s.Screen == GameScreen.FieldFree)
                    {
                        ChainBlockResult[] end = await bot.ReadChainBlocksAsync([new(addresses.VBlankCounter, [], 4)], ct: ct);
                        uint frames = BitConverter.ToUInt32(end[0].Bytes, 0) - startFrame;
                        Console.WriteLine($"[RsBattle] battle over: {frames} frames from the battle bit, {battleTime.ElapsedMilliseconds} ms, "
                            + $"{presses} presses, {runPresses} on RUN");
                        if (training)
                        {
                            // The party as the battle left it: what was learned, and later what evolved (FR-16).
                            ChainBlockResult[] party = await bot.ReadChainBlocksAsync(
                                [new(addresses.Anchor, [], 1), new(addresses.BattlePartyPokemon, [], 6 * Gen3PokemonParser.PartyBlockSize)], ct: ct);
                            for (int slot = 0; slot < party[0].Bytes[0]; slot++)
                            {
                                PokemonData p = bot.Profile.Parser.Parse(party[1].Bytes[(slot * Gen3PokemonParser.PartyBlockSize)..((slot + 1) * Gen3PokemonParser.PartyBlockSize)])!;
                                Console.WriteLine($"[RsBattle] party after the battle: slot {slot} {p.Name} ({p.Species}) Lv {p.Level} moves "
                                    + string.Join(",", p.Moves.Select(m => m.Id)));
                            }
                        }
                        state.SwapState(EventState.BattleComplete);
                        return null;
                    }
                    if (s.Screen == GameScreen.BattleScript)
                    {
                        // "Use next POKéMON?" (F1, F2): B is No, the game's own flee, confirmed by the script leaving the
                        // question. "Got away safely!" ends the battle and the orchestrator heals; "Can't escape!" opens the
                        // party menu. From here the action menu runs RUN, in Standard and Switch alike.
                        Console.WriteLine($"[RsBattle] Use next POKéMON? for slot {s.PartyMonIndex}: fleeing");
                        (_, int no) = await PressConfirmedAsync(bot, reader, B, $"B (No) at Use next POKéMON? for slot {s.PartyMonIndex}",
                            "the script leaving Use next POKéMON?", s, x => x.Screen != GameScreen.BattleScript, cursorWait, stale, ct);
                        presses += no;
                        fleeing = true;
                        continue;
                    }
                    if (s.Screen == GameScreen.OtherApp && s.TaskFunction == addresses.BattlePartyMenuTask)
                    {
                        // The party menu after "Can't escape!" (F4): only that leads here, since SwitchAsync takes the menu
                        // it opens. The fastest alive is sent out (F5: "Go!" clears by itself, with no free attack), and
                        // the next action menu runs RUN once.
                        (int sent, string? noneAlive) = await SendOutFastestAsync(bot, reader, addresses, since, cursorWait, stale, ct);
                        presses += sent;
                        if (noneAlive is not null)
                        {
                            // Waiting and one Discord message, as the run-failed stop; Resume waits for the free field.
                            string sentence = Stop(state, NoneAlive, noneAlive);
                            state.StoppedForTrainer = true;
                            await notifyStop(sentence);
                            return sentence;
                        }
                        sentOut = true;
                        continue;
                    }
                    if (s.Screen == GameScreen.BattleMessage && runTriesBefore is int before && s.Outcome != BattleOutcome.Fled)
                    {
                        // The first message after the one RUN that followed the send-out, with the outcome not 4: "Can't
                        // escape!" (F6). Nothing more is pressed; the operator finishes the battle.
                        byte after = (await bot.ReadChainBlocksAsync([new(addresses.RunTries, [], 1)], ct: ct))[0].Bytes[0];
                        string sentence = Stop(state, RunFailed, $"runTries {before} -> {after}{(after > before ? "" : " (did not rise)")}, {s}");
                        // Resume waits for the free field first, as after a trainer stop.
                        state.StoppedForTrainer = true;
                        await notifyStop(sentence);
                        return sentence;
                    }
                    // The scene prints in the battle window, so its text waits are passed as a battle message's (FR-16).
                    if (s.Screen == GameScreen.BattleMessage || (s.Screen == GameScreen.Evolution && s.Question is null))
                    {
                        int? window = s.TextPrinter;
                        string scene = s.Screen == GameScreen.Evolution ? $" in the evolution scene of slot {s.PartyMonIndex} at data[0] {s.Prompt}" : "";
                        presses += await PressAsync(bot, Macro("BattleAdvance"), ct);
                        GameSnapshot advanced = await GameStateWait.UntilAsync(reader, $"the battle window to leave {window}",
                            x => x.TextPrinter != window, stale, ct);
                        Console.WriteLine($"[RsBattle] BattleAdvance{scene} at window {window}, {since.ElapsedMilliseconds} ms: {advanced}");
                        continue;
                    }
                    if (s.Screen == GameScreen.BattleExpPrompt)
                    {
                        // The level-up box, one per level and getter (FR-14): page 1 is confirmed by page 2, page 2 by the
                        // counter leaving it (3, or 1 for the next box). The getter is named only from the box's own snapshot.
                        bool firstPage = s.Prompt == 1;
                        (_, int sent) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"),
                            $"level-up box page {s.Prompt} for slot {s.PartyMonIndex}: BattleConfirm", firstPage ? "page 2" : "the box leaving page 2", s,
                            firstPage ? x => x.Screen == GameScreen.BattleExpPrompt && x.Prompt == 2 : x => !(x.Screen == GameScreen.BattleExpPrompt && x.Prompt == 2),
                            cursorWait, stale, ct);
                        presses += sent;
                        continue;
                    }
                    if (s.Screen is GameScreen.BattleMoveLearnPrompt or GameScreen.Evolution)
                    {
                        // A new move with four known (FR-15), in battle or at the evolution scene's own yes/no, where the
                        // learner is the evolving slot (data[12]) and B is sent only as the No (FR-16). "Stop learning?"
                        // reached any way gets its Yes.
                        Console.WriteLine($"[RsBattle] move prompt at {s}");
                        presses +=s.Question == RsStateReader.StopLearningQuestion
                            ? await StopLearningAsync(bot, reader, s, cursorWait, stale, ct)
                            : await AnswerMovePromptAsync(bot, reader, addresses, s,
                                await AskMoveAsync(bot, addresses, s.PartyMonIndex!.Value, moveMetadata, decideMove, ct), cursorWait, stale, ct);
                        continue;
                    }

                    // The action menu. Before the switch the menu read takes the battler's record; after it, or once fleeing,
                    // the active one's.
                    if (!switched && s.PartyMonIndex == switchSlot)
                    {
                        switched = true;
                        Console.WriteLine($"[RsBattle] switch confirmed: 0x02024A6A reads {s.PartyMonIndex} at the action menu, try {switchTries}");
                    }
                    else if (!switched && !fleeing && switchTries > 0)
                    {
                        Console.WriteLine($"[RsBattle] switch try {switchTries} did not land: 0x02024A6A reads {s.PartyMonIndex}");
                    }
                    bool toSwitch = !switched && !fleeing;
                    int? battler = !training ? null : toSwitch ? switchSlot : s.PartyMonIndex;
                    ChainBlockResult[] menu = await ReadMenuAsync(bot, addresses, battler, ct);
                    if (!seenMenu)
                    {
                        PokemonData wild = state.Bot.Profile.Parser.Parse(menu[0].Bytes)!;
                        Stopwatch corrupt = Stopwatch.StartNew();
                        while (wild.IsCorrupt)
                        {
                            if (corrupt.Elapsed >= checksumWindow)
                            {
                                return Stop(state, Corrupt, s.ToString());
                            }
                            await Task.Delay(Constant.PollIntervalMs, ct);
                            menu = await ReadMenuAsync(bot, addresses, battler, ct);
                            wild = state.Bot.Profile.Parser.Parse(menu[0].Bytes)!;
                        }
                        Console.WriteLine($"[RsBattle] {wild.Name} species {wild.Species} Lv {wild.Level} PV {wild.PID:X8} "
                            + $"shiny value {wild.ShinyValue} shiny {wild.IsShiny}");
                        // A GBA emulator can build a shiny no cartridge could (docs/decisions/global.md, "Every Gen 3 shiny is
                        // checked with PKHeX"): a shiny that fails is logged as not shiny, is not a target, and is fled or fought
                        // like any other (FR-18c).
                        bool legal = true;
                        if (wild.IsShiny)
                        {
                            LegalityResult verdict = analyze(menu[0].Bytes);
                            legal = verdict.IsLegal;
                            Console.WriteLine(legal
                                ? $"[RsBattle] legality OK   {wild.Name} PV {wild.PID:X8}"
                                : $"[RsBattle] legality FAIL {wild.Name} PV {wild.PID:X8}: logged as not shiny, {(fleeing ? "running" : "fighting")}{Environment.NewLine}{verdict.Report}");
                        }
                        if (await OrchestratorEvent.LogEncounterAndAskTargetAsync(state, wild, addEncounter, notifyTargetFound, legal))
                        {
                            Console.WriteLine("[RsBattle] a target: waiting at the action menu, nothing pressed");
                            return null;
                        }
                        seenMenu = true;
                    }
                    ushort leadHp = BitConverter.ToUInt16(menu[1].Bytes, training ? LeadHpOffset : 0);
                    if (toSwitch)
                    {
                        // HGSS's order (BattleOrchestratorEvent.DecideAction): a fainted battler or the last failed try
                        // flees; otherwise one more try. The checks below apply once switched.
                        string? flee = leadHp == 0 ? $"the battler in slot {switchSlot} has fainted"
                            : switchTries == MaxSwitchTries ? $"the switch to slot {switchSlot} failed {MaxSwitchTries} times"
                            : null;
                        if (flee is null)
                        {
                            switchTries++;
                            presses += await SwitchAsync(bot, reader, addresses, s, switchSlot!.Value, switchTries, cursorWait, stale, ct);
                            continue;
                        }
                        Console.WriteLine($"[RsBattle] fleeing: {flee}");
                        fleeing = true;
                    }
                    else if (leadHp == 0)
                    {
                        return Stop(state, Fainted, s.ToString());
                    }
                    if (!fleeing)
                    {
                        // HGSS's order (BattleOrchestratorEvent.DecideAction): too hurt or no damaging PP flees, then the
                        // damage picker, whose -1 flees too. The party record carries the live HP and PP.
                        PokemonData fighter = state.Bot.Profile.Parser.Parse(menu[1].Bytes)!;
                        PokemonData enemy = state.Bot.Profile.Parser.Parse(menu[0].Bytes)!;
                        string? flee = BattleReadiness.IsTooHurtToFight(fighter.HP, fighter.MaxHP) ? $"lead HP {fighter.HP}/{fighter.MaxHP} is too low"
                            : !fighter.Moves.Any(m => m.Id > 0 && m.PP > 0 && moveMetadata(m.Id).IsDamageMove) ? "no damaging PP"
                            : null;
                        int slot = -1;
                        if (flee is null)
                        {
                            slot = pickMove(new PokemonBattleState { ActivePokemon = ToBattlePokemon(fighter), EnemyPokemon = ToBattlePokemon(enemy) });
                            flee = slot == -1 ? "the picker found no move" : null;
                        }
                        if (flee is null)
                        {
                            Move move = fighter.Moves[slot];
                            string chosen = $"[RsBattle] move {slot} {moveMetadata(move.Id).Name} chosen by the damage picker, lead HP "
                                + $"{fighter.HP}/{fighter.MaxHP} PP {move.PP}, wild HP {enemy.HP}";
                            presses += await FightAsync(bot, reader, slot, chosen, cursorWait, stale, ct);
                            continue;
                        }
                        Console.WriteLine($"[RsBattle] fleeing: {flee}");
                        fleeing = true;
                    }
                    if (runPresses == MaxRunPresses)
                    {
                        return Stop(state, Trapped, s.ToString());
                    }
                    if (runPresses > 0)
                    {
                        Console.WriteLine($"[RsBattle] escape failed (try {runPresses} of {MaxRunPresses}), cursor {s.Prompt}, lead HP {leadHp}");
                    }

                    if (s.Prompt != RunCursor)
                    {
                        int? from = s.Prompt;
                        presses += await PressAsync(bot, Macro("BattleCursorToRun"), ct);
                        // A cursor read a frame early is not a dropped press: the missing press goes only once the cursor has
                        // stayed short of RUN for cursorWait.
                        Stopwatch sinceCursor = Stopwatch.StartNew();
                        s = await GameStateWait.UntilAsync(reader, "the cursor on RUN",
                            x => x.Prompt == RunCursor || sinceCursor.Elapsed >= cursorWait, stale, ct);
                        if (s.Prompt != RunCursor)
                        {
                            // Off the menu nothing is pressed; the wait below stops on the snapshot.
                            string? missing = s.Prompt switch
                            {
                                0 => Macro("BattleCursorToRun"),
                                1 => Down,
                                2 => Right,
                                _ => null,
                            };
                            Console.WriteLine($"[RsBattle] cursor {s.Prompt} after BattleCursorToRun; sending {missing ?? "nothing"}");
                            if (missing is not null)
                            {
                                presses += await PressAsync(bot, missing, ct);
                            }
                            s = await GameStateWait.UntilAsync(reader, "the cursor on RUN after the missing press", x => x.Prompt == RunCursor, stale, ct);
                        }
                        Console.WriteLine($"[RsBattle] BattleCursorToRun from cursor {from}, {since.ElapsedMilliseconds} ms: {s}");
                        since.Restart();
                    }

                    // The cursor is on the snapshot only at the action menu, so s is the menu with the cursor on RUN. After a
                    // send-out, runTries before this RUN goes into the run-failed stop (F6).
                    if (sentOut)
                    {
                        runTriesBefore = (await bot.ReadChainBlocksAsync([new(addresses.RunTries, [], 1)], ct: ct))[0].Bytes[0];
                    }
                    presses += await PressAsync(bot, Macro("BattleConfirm"), ct);
                    runPresses++;
                    GameSnapshot left = await GameStateWait.UntilAsync(reader, "the screen to leave the action menu",
                        x => x.Screen != GameScreen.BattleMenu, stale, ct);
                    Console.WriteLine($"[RsBattle] BattleConfirm on RUN (try {runPresses}), {since.ElapsedMilliseconds} ms: {left}");
                }
            }
            catch (GameStateTimeoutException ex)
            {
                // The message names the snapshot that did not change.
                return Stop(state, ex.Message, null);
            }
        }

        // The wild Pokémon's record and, on one frame, the lead's HP; in Battle mode the battler's whole party record instead.
        private static Task<ChainBlockResult[]> ReadMenuAsync(IBot bot, RsPointerService addresses, int? battler, CancellationToken ct)
            => bot.ReadChainBlocksAsync(
                [
                    new(addresses.EnemyParty, [], Gen3PokemonParser.PartyBlockSize),
                    battler is int slot
                        ? new(PartyRecord(addresses, slot), [], Gen3PokemonParser.PartyBlockSize)
                        : new(addresses.BattlePartyPokemon + LeadHpOffset, [], 2),
                ], ct: ct);

        // The send-out after a faint (the owner's rule of 2026-11-17), over the menu's rows and the party slot each shows: the
        // row of the highest Speed among the members alive and not eggs, the lowest party slot on a tie; null with none.
        internal static int? FastestAlive(IReadOnlyList<PokemonData> rows, IReadOnlyList<int> slots)
            => Enumerable.Range(0, rows.Count).Where(r => rows[r].HP > 0 && !rows[r].IsEgg)
                .OrderByDescending(r => rows[r].Speed).ThenBy(r => slots[r]).Cast<int?>().FirstOrDefault();

        private static uint PartyRecord(RsPointerService addresses, int slot)
            => (uint)(addresses.BattlePartyPokemon + slot * Gen3PokemonParser.PartyBlockSize);

        private static BattlePokemon ToBattlePokemon(PokemonData p) => new()
        {
            Pokemon = p, Species = p.Species, PID = p.PID, Level = p.Level, HP = p.HP, MaxHP = p.MaxHP,
            Move1PP = p.Move1PP, Move2PP = p.Move2PP, Move3PP = p.Move3PP, Move4PP = p.Move4PP,
        };

        // FIGHT, the move cursor to the slot, then the move: each press gated on its screen and confirmed by the byte it
        // changes (the controller function, then gMoveSelectionCursor). The cursor clears its bits first (Left, Up), then
        // sets them (Right, Down), so it never crosses an empty slot: the game refuses Right or Down onto a slot past the
        // move count, and moves fill from slot 0.
        private static async Task<int> FightAsync(IBot bot, IGameStateReader reader, int slot, string chosen, TimeSpan cursorWait,
            TimeSpan stale, CancellationToken ct)
        {
            GameSnapshot at = await GameStateWait.UntilAsync(reader, "the action cursor on FIGHT",
                x => x.Screen == GameScreen.BattleMenu && x.Prompt == FightCursor, stale, ct);
            (GameSnapshot list, int presses) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), "BattleConfirm on FIGHT",
                "the move list", at, x => x.Screen == GameScreen.BattleMoveList, cursorWait, stale, ct);
            Console.WriteLine(chosen);
            while (list.Prompt != slot)
            {
                int from = list.Prompt!.Value;
                (string macro, string name, int next) = MoveCursorStep(from, slot);
                (list, int sent) = await PressConfirmedAsync(bot, reader, macro, $"{name} on the move list {from} -> {next}", $"cursor {next}",
                    list, x => x.Screen == GameScreen.BattleMoveList && x.Prompt == next, cursorWait, stale, ct);
                presses += sent;
            }
            (_, int confirmed) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), $"BattleConfirm on move {slot}",
                "the screen leaving the move list", list, x => x.Screen != GameScreen.BattleMoveList, cursorWait, stale, ct);
            return presses + confirmed;
        }

        // One switch try from the action menu at, to the battler's 0-based slot (FR-17; research "The in-battle switch, read
        // frame by frame"). Every press is gated on its snapshot and confirmed by the byte it changes: the action cursor to
        // POKéMON, A (the screen leaving the action menu), then the party list's walk to the slot and SHIFT (PartyWalkAsync).
        private static async Task<int> SwitchAsync(IBot bot, IGameStateReader reader, RsPointerService addresses, GameSnapshot at,
            int slot, int attempt, TimeSpan cursorWait, TimeSpan stale, CancellationToken ct)
        {
            int presses = 0;
            GameSnapshot menu = at;
            while (menu.Prompt != PokemonCursor)
            {
                int from = menu.Prompt!.Value;
                (string macro, string name, int next) = MoveCursorStep(from, PokemonCursor);
                (menu, int sent) = await PressConfirmedAsync(bot, reader, macro, $"{name} on the action menu {from} -> {next}", $"cursor {next}",
                    menu, x => x.Screen == GameScreen.BattleMenu && x.Prompt == next, cursorWait, stale, ct);
                presses += sent;
            }
            Stopwatch since = Stopwatch.StartNew();
            (_, int pokemon) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), $"BattleConfirm on POKéMON (switch try {attempt} of {MaxSwitchTries})",
                "the screen leaving the action menu", menu, x => x.Screen != GameScreen.BattleMenu, cursorWait, stale, ct);
            // On the list only once it takes keys. The party is not read while the menu is up, so the slot's row comes from
            // gBattlePartyCurrentOrder, read once (F7); in the voluntary menu the order is still the identity (round six, B).
            GameSnapshot list = await GameStateWait.UntilAsync(reader, $"the party list to take keys (the switch to slot {slot})",
                x => PartyListTakesKeys(x, addresses), stale, ct);
            Console.WriteLine($"[RsBattle] switch try {attempt} of {MaxSwitchTries} to slot {slot}: the party list takes keys, "
                + $"{since.ElapsedMilliseconds} ms after POKéMON: {list}");
            byte[] order = (await bot.ReadChainBlocksAsync([new(addresses.BattlePartyCurrentOrder, [], 4)], ct: ct))[0].Bytes;
            int row = Enumerable.Range(0, 6).First(p => SlotOnRow(order, p) == slot);
            LogOrder(order, slot, row);
            return presses + pokemon + await PartyWalkAsync(bot, reader, addresses, list, slot, row, "the switch", "SHIFT", cursorWait, stale, ct);
        }

        // The menu a faint forces after "Can't escape!" (F4, F5). Once the list takes keys, the order and the party in one
        // read: while a party task is up the game keeps gPlayerParty in the menu's row order, record r being the Pokémon on
        // row r, which is party slot order[r] (research "A battler faints: the readings", round six, B), so the fastest alive
        // is chosen as a row. Every row read is logged. Returns the presses, or with nobody alive in that read the rows
        // read, and nothing is pressed: the emulator produced that once, cause not found (round six, "Not read").
        private static async Task<(int Presses, string? NoneAlive)> SendOutFastestAsync(IBot bot, IGameStateReader reader, RsPointerService addresses,
            Stopwatch since, TimeSpan cursorWait, TimeSpan stale, CancellationToken ct)
        {
            GameSnapshot list = await GameStateWait.UntilAsync(reader, "the party list to take keys (the send-out)",
                x => PartyListTakesKeys(x, addresses), stale, ct);
            Console.WriteLine($"[RsBattle] send-out of the fastest alive: the party list takes keys, {since.ElapsedMilliseconds} ms after the party menu: {list}");
            ChainBlockResult[] read = await bot.ReadChainBlocksAsync(
            [
                new(addresses.BattlePartyCurrentOrder, [], 4), new(addresses.Anchor, [], 1),
                new(addresses.BattlePartyPokemon, [], 6 * Gen3PokemonParser.PartyBlockSize),
            ], ct: ct);
            byte[] order = read[0].Bytes;
            PokemonData[] rows = [.. Enumerable.Range(0, read[1].Bytes[0]).Select(r => bot.Profile.Parser.Parse(
                read[2].Bytes[(r * Gen3PokemonParser.PartyBlockSize)..((r + 1) * Gen3PokemonParser.PartyBlockSize)])!)];
            int[] slots = [.. Enumerable.Range(0, rows.Length).Select(r => SlotOnRow(order, r))];
            string table = string.Join("; ", Enumerable.Range(0, rows.Length)
                .Select(r => $"row {r} slot {slots[r]} {rows[r].Name} HP {rows[r].HP} Speed {rows[r].Speed}"));
            Console.WriteLine($"[RsBattle] the party menu after a faint, by row: {table}");
            if (FastestAlive(rows, slots) is not int row)
            {
                return (0, table);
            }
            LogOrder(order, slots[row], row);
            return (await PartyWalkAsync(bot, reader, addresses, list, slots[row], row, "the send-out", "SEND OUT", cursorWait, stale, ct), null);
        }

        // The battle party list taking keys: the list's task, callback 2 CB2_PartyMenuMain and the fade off.
        private static bool PartyListTakesKeys(GameSnapshot x, RsPointerService addresses)
            => x.Screen == GameScreen.OtherApp && x.AppState == 0 && x.TaskFunction == addresses.BattlePartyMenuTask;

        // Row p of the battle party menu shows the party slot in nibble p of gBattlePartyCurrentOrder: byte p / 2, the high
        // nibble for even p; the menu's case byte follows the three bytes.
        private static int SlotOnRow(byte[] order, int row) => (order[row / 2] >> (row % 2 == 0 ? 4 : 0)) & 0xF;

        private static void LogOrder(byte[] order, int slot, int row)
            => Console.WriteLine($"[RsBattle] party menu order {order[0]:X2} {order[1]:X2} {order[2]:X2}, case byte {order[3]}: slot {slot} is on row {row}");

        // The battle party list's walk from list, taking keys, to the row of the party slot, and its popup's first item:
        // SHIFT in a switch, SEND OUT in the menu a faint forces (research "A battler faints: the readings", F4, F5). Down
        // until the cursor is on the row (each confirmed by the cursor leaving its value; Down alone reaches any row, last
        // -> 7 -> 0), A (the popup's task function), then A on the item with the popup up and the fade off (the function
        // leaving the popup). Then nothing is pressed and no party record is read until the next screen the main loop acts
        // on: "Come back", "Go!" and the wild Pokémon's move go by themselves, and for about 22 frames after SHIFT the party
        // records are swapped. A refused item ends in this wait's stale stop. what names the walk in its waits.
        private static async Task<int> PartyWalkAsync(IBot bot, IGameStateReader reader, RsPointerService addresses, GameSnapshot list,
            int slot, int row, string what, string item, TimeSpan cursorWait, TimeSpan stale, CancellationToken ct)
        {
            bool TakesKeys(GameSnapshot x) => x.Screen == GameScreen.OtherApp && x.AppState == 0;
            int presses = 0;
            while (list.PartyMonIndex != row)
            {
                int from = list.PartyMonIndex!.Value;
                (list, int down) = await PressConfirmedAsync(bot, reader, Down, $"Down on the party list from {from}", $"the party cursor leaving {from}",
                    list, x => TakesKeys(x) && x.TaskFunction == addresses.BattlePartyMenuTask && x.PartyMonIndex != from, cursorWait, stale, ct);
                presses += down;
            }
            (GameSnapshot popup, int slotA) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), $"BattleConfirm on party slot {slot}",
                $"the {item} submenu", list, x => TakesKeys(x) && x.TaskFunction == addresses.BattlePartyPopupTask, cursorWait, stale, ct);
            (_, int chosen) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), $"BattleConfirm on {item} at {popup}", "the submenu closing",
                popup, x => !(x.Screen == GameScreen.OtherApp && x.TaskFunction == addresses.BattlePartyPopupTask), cursorWait, stale, ct);

            Stopwatch afterItem = Stopwatch.StartNew();
            GameSnapshot back = await GameStateWait.UntilAsync(reader, $"the next action menu after {item} ({what} to slot {slot})",
                x => x.Screen != GameScreen.BattleTurn, stale, ct);
            Console.WriteLine($"[RsBattle] after {item}, {afterItem.ElapsedMilliseconds} ms: {back}");
            return presses + slotA + chosen;
        }

        // The move offered and the learner's record, read only while the prompt is up (gMoveToLearn goes stale after it),
        // then the question HGSS's LevelUpFlow asks: a slot to forget, or -1. Nothing is pressed while it waits; it has no
        // time bound and ends on the answer or on Cancel.
        private static async Task<int> AskMoveAsync(IBot bot, RsPointerService addresses, int learnerSlot, Func<int, PokemonMoveMetadata> moveMetadata,
            Func<PokemonData, PokemonLevelUpMoveMetadata, Task<int>> decideMove, CancellationToken ct)
        {
            ChainBlockResult[] read = await bot.ReadChainBlocksAsync(
                [new(addresses.MoveToLearn, [], 2), new(PartyRecord(addresses, learnerSlot), [], Gen3PokemonParser.PartyBlockSize)], ct: ct);
            ushort moveId = BitConverter.ToUInt16(read[0].Bytes, 0);
            PokemonData learner = bot.Profile.Parser.Parse(read[1].Bytes)!;
            string offered = $"slot {learnerSlot} {learner.Name} learning {moveMetadata(moveId).Name} ({moveId})";
            Console.WriteLine($"[RsBattle] asking Discord: {offered}; nothing is pressed until the answer");
            int slot = await decideMove(learner, new PokemonLevelUpMoveMetadata { Id = moveId, Name = moveMetadata(moveId).Name, Level = learner.Level })
                .WaitAsync(ct);
            Console.WriteLine($"[RsBattle] Delete a move? {offered}: "
                + (slot == -1 ? "No" : $"Yes over slot {slot} {moveMetadata(learner.Moves[slot].Id).Name}"));
            return slot;
        }

        // The answer at "Delete a move?" (Question 5), at the snapshot at. Yes: A, confirmed by the prompt leaving; then on
        // the forget-a-move screen every press only while the summary's input task runs and the fade is off (a key sent
        // earlier is dropped): Down until the cursor is on the slot, each confirmed by the cursor, then A, confirmed by the
        // destroy task, then the screen closing. No: B, confirmed by the prompt leaving, then Yes at "Stop learning?".
        private static async Task<int> AnswerMovePromptAsync(IBot bot, IGameStateReader reader, RsPointerService addresses, GameSnapshot at,
            int slot, TimeSpan cursorWait, TimeSpan stale, CancellationToken ct)
        {
            if (slot == -1)
            {
                (_, int no) = await PressConfirmedAsync(bot, reader, B, "B at Delete a move?", "the prompt leaving Delete a move?", at,
                    x => x.Question != RsStateReader.DeleteMoveQuestion, cursorWait, stale, ct);
                GameSnapshot stop = await GameStateWait.UntilAsync(reader, "Stop learning?", x => x.Question == RsStateReader.StopLearningQuestion, stale, ct);
                return no + await StopLearningAsync(bot, reader, stop, cursorWait, stale, ct);
            }
            (_, int presses) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), "BattleConfirm (Yes) at Delete a move?",
                "the prompt leaving Delete a move?", at, x => x.Question != RsStateReader.DeleteMoveQuestion, cursorWait, stale, ct);
            while (true)
            {
                GameSnapshot screen = await GameStateWait.UntilAsync(reader, "the forget-a-move screen taking input",
                    x => x.Screen == GameScreen.OtherApp && x.TaskFunction == addresses.SummaryMoveInputTask && x.AppState == 0, stale, ct);
                int from = screen.Prompt!.Value;
                if (from == slot)
                {
                    (_, int a) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), $"BattleConfirm on forget slot {slot}",
                        "the summary task closing", screen, x => x.TaskFunction == addresses.SummaryDestroyTask || x.Screen != GameScreen.OtherApp,
                        cursorWait, stale, ct);
                    GameSnapshot back = await GameStateWait.UntilAsync(reader, "the forget-a-move screen to close",
                        x => x.Screen != GameScreen.OtherApp, stale, ct);
                    Console.WriteLine($"[RsBattle] forget-a-move screen closed: {back}");
                    return presses + a;
                }
                (_, int down) = await PressConfirmedAsync(bot, reader, Down, $"Down on the forget-a-move screen {from} -> {from + 1}",
                    "the cursor", screen, x => x.Screen == GameScreen.OtherApp && x.Prompt == from + 1, cursorWait, stale, ct);
                presses += down;
            }
        }

        // Yes at "Stop learning?", whose cursor the game resets to Yes; confirmed by the prompt leaving.
        private static async Task<int> StopLearningAsync(IBot bot, IGameStateReader reader, GameSnapshot at, TimeSpan cursorWait,
            TimeSpan stale, CancellationToken ct)
        {
            (_, int presses) = await PressConfirmedAsync(bot, reader, Macro("BattleConfirm"), "BattleConfirm (Yes) at Stop learning?",
                "the prompt leaving Stop learning?", at, x => x.Question != RsStateReader.StopLearningQuestion, cursorWait, stale, ct);
            return presses;
        }

        // The move cursor's next press toward the slot: bit 0 is the column, bit 1 the row.
        private static (string Macro, string Name, int Next) MoveCursorStep(int from, int to)
            => (from & 1) > (to & 1) ? (Left, "Left", from & ~1)
            : (from & 2) > (to & 2) ? (Up, "Up", from & ~2)
            : (from & 1) < (to & 1) ? (Right, "Right", from | 1)
            : (Down, "Down", from | 2);

        // Sends the macro at the snapshot it was gated on and waits for confirmed (AGENTS rule 16). Each confirming byte
        // changes on the frame the game reads the key, so a snapshot still equal to at, same screen and cursor, cursorWait
        // after the press proves the key was dropped: it is sent once more, never a third time. A snapshot that changed but
        // is not confirmed yet gets no second press. Then the wait for confirmed is bounded by stale. One log line under
        // log's prefix, naming what confirmed the press; the Pickup visit (RsPickupTakeEvent) shares it.
        internal static async Task<(GameSnapshot Confirmed, int Presses)> PressConfirmedAsync(IBot bot, IGameStateReader reader,
            string macro, string what, string by, GameSnapshot at, Func<GameSnapshot, bool> confirmed, TimeSpan cursorWait,
            TimeSpan stale, CancellationToken ct, string log = "RsBattle")
        {
            int presses = await PressAsync(bot, macro, ct);
            Stopwatch since = Stopwatch.StartNew();
            bool moved = false;
            GameSnapshot s = await GameStateWait.UntilAsync(reader, $"{by} after {what}",
                x => confirmed(x) || (moved |= x != at) || since.Elapsed >= cursorWait, stale, ct);
            string dropped = "";
            if (!confirmed(s) && !moved)
            {
                presses += await PressAsync(bot, macro, ct);
                dropped = "; dropped, sent again";
            }
            if (!confirmed(s))
            {
                s = await GameStateWait.UntilAsync(reader, $"{by} after {what}", confirmed, stale, ct);
            }
            Console.WriteLine($"[{log}] {what}, {since.ElapsedMilliseconds} ms: confirmed by {by} ({s}){dropped}");
            return (s, presses);
        }

        private static string Macro(string name) => MacroRegistry.Instance.GetMacro(GameFlavor.RS, name);

        // Sends the macro and waits for it; returns its presses, one a step (BattleCursorToRun is Right and Down).
        private static async Task<int> PressAsync(IBot bot, string macro, CancellationToken ct)
        {
            bot.RunMacroString(macro);
            await bot.WaitForMacroAsync(ct);
            return macro.Split(';').Length;
        }

        // An unbeaten trainer's challenge, on one frame (docs/research/rs-navigation-and-training.md, "A trainer's intro
        // speech", the rule): the field locked, no battle, a script running or waiting, a trainer id, and that trainer's flag
        // clear. The flags are read whole so the flag the id names is from the same frame. Null when it is not one.
        internal static async Task<(byte Group, byte Number, ushort Trainer)?> ReadTrainerChallengeAsync(IBot bot, RsPointerService addresses, CancellationToken ct)
        {
            ChainBlockResult[] read = await bot.ReadChainBlocksAsync(
            [
                new(addresses.FieldControlsLock, [], 1),
                new(addresses.InBattle, [], 1),
                new(addresses.ScriptContextStatus, [], 1),
                new(addresses.TrainerBattleOpponent, [], 2),
                new(addresses.EventFlags, [], addresses.EventFlagBytes),
                new(addresses.PlayerLocation, [], 2),
            ], ct: ct);
            ushort trainer = BitConverter.ToUInt16(read[3].Bytes, 0);
            int flag = TrainerFlagBase + trainer;
            // An id whose flag lies past the flags read (1024 and up) is not a trainer the game has a flag for.
            bool challenge = read[0].Bytes[0] != 0
                && (read[1].Bytes[0] & InBattleBit) == 0
                && read[2].Bytes[0] != NoScript
                && trainer != 0
                && (flag >> 3) < read[4].Bytes.Length
                && (read[4].Bytes[flag >> 3] & (1 << (flag & 7))) == 0;
            if (!challenge)
            {
                Console.WriteLine($"[RsBattle] lock handed over: not a trainer (lock {read[0].Bytes[0]}, battle bit {(read[1].Bytes[0] & InBattleBit) >> 1}, "
                    + $"script status {read[2].Bytes[0]}, opponent {trainer}, flag {flag} "
                    + $"{((flag >> 3) >= read[4].Bytes.Length ? "past the flags" : (read[4].Bytes[flag >> 3] & (1 << (flag & 7))) != 0 ? "set" : "clear")})");
            }
            return challenge ? (read[5].Bytes[0], read[5].Bytes[1], trainer) : null;
        }

        // The trainer stop's sentence, naming the map from the profile's maps.
        internal static string TrainerSentence(MapService maps, byte group, byte number) =>
            $"A trainer battle started on {maps.GetMapLookup((group << 8) | number).InternalName}.";

        // Parks the bot with the trainer's sentence, marks the stop as a trainer's for Resume, then sends the sentence.
        private static async Task<string> TrainerStopAsync(GameState state, byte group, byte number, string seen, Func<string, Task> notifyStop)
        {
            string sentence = Stop(state, TrainerSentence(state.Bot.Profile.Maps!, group, number), seen);
            state.StoppedForTrainer = true;
            await notifyStop(sentence);
            return sentence;
        }

        // Logs the sentence with what was read and parks the bot for the operator.
        private static string Stop(GameState state, string sentence, string? seen)
        {
            Console.WriteLine(seen is null ? $"[RsBattle] stopped: {sentence}" : $"[RsBattle] stopped: {sentence} {seen}");
            state.SwapState(EventState.Waiting);
            return sentence;
        }
    }
}
