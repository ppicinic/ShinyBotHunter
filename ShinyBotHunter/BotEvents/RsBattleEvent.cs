using System.Diagnostics;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    // The Ruby and Sapphire battle of a wild encounter, from BattleStart to BattleComplete or Waiting (docs/specs/rs-wild-encounter.md
    // FR-9 to FR-11, FR-14; docs/research/rs-wild-encounter.md "Presses", "Tuned for speed", "A failed escape"): A at each
    // message, the read at the first action menu, then RUN. Every press is sent from the snapshot its rule needs and confirmed
    // by the byte it changes; anything else stops the bot in Waiting for the operator.
    public class RsBattleEvent : AbstractBotEvent<GameState>
    {
        internal const string NotWild = SpinEncounterEvent.NotAWildBattle;
        internal const string Fainted = "The first Pokémon fainted.";
        internal const string Trapped = "Could not run from this battle.";
        internal const string Corrupt = "The wild Pokémon's record failed its checksum for 2 s.";

        // The battle bit after the field leaves free; how long the snapshot may stay the same in battle; how long a record
        // that fails its checksum is read again; how long the cursor may take to read RUN before the missing press is sent.
        private static readonly TimeSpan BattleStartStale = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ChecksumWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan CursorWait = TimeSpan.FromSeconds(1);

        // Presses on RUN in one battle before it counts as a trap: the escape chance rises with each try.
        internal const int MaxRunPresses = 12;

        // The two halves of BattleCursorToRun, each sent alone to a cursor the other one already moved; a test pins them to
        // the registry text.
        internal const string Right = "H.Right.2";
        internal const string Down = "H.Down.2";

        // gBattleTypeFlags: wild, trainer, Safari Zone.
        private const ushort WildBattle = 0x0004;
        private const ushort TrainerBattle = 0x0008;
        private const ushort SafariBattle = 0x0080;

        // gMain.inBattle's bit in the InBattle byte; the script status with no script running (research "A trainer's intro
        // speech"); a trainer's flag is 0x500 + its id.
        private const byte InBattleBit = 0x02;
        private const byte NoScript = 2;
        private const int TrainerFlagBase = 0x500;

        // The action cursor on RUN.
        private const int RunCursor = 3;

        // A party record's current HP: pokeruby struct Pokemon.hp, Gen3PokemonParser's HpOffset.
        private const int LeadHpOffset = 0x56;

        private readonly PokemonPersistenceService persistence;
        private readonly ShinyAlertService alerts;
        private readonly LegalityService legality;

        public RsBattleEvent(PokemonPersistenceService persistence, ShinyAlertService alerts, LegalityService legality)
        {
            this.persistence = persistence;
            this.alerts = alerts;
            this.legality = legality;
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // The seek hands over a lock that has lasted SeekLockHandOff, so a lock that is not a trainer waits for the battle
            // bit only for what is left of 15 s from the lock (FR-9).
            await RunAsync(state, (RsPointerService)state.Bot.Addresses, persistence.AddEncounterAsync, p => alerts.NotifyTargetFoundAsync(p),
                s => alerts.NotifyTrainerBattleAsync(s),
                raw => legality.Analyze(raw, GameFlavor.RS), BattleStartStale, BattleStartStale - state.Bot.Profile.SeekLockHandOff!.Value,
                Stale, ChecksumWindow, CursorWait, cancellationToken);
            return state;
        }

        // The battle. Null when it is over (BattleComplete) or parked on a target (Waiting), otherwise the sentence it stopped
        // with (Waiting). The delegates are the encounter log and the two alerts. The times are parameters so the tests run
        // short: afterHandOff is the battle bit's wait once a lock the seek handed over is not a trainer.
        internal static async Task<string?> RunAsync(GameState state, RsPointerService addresses,
            Func<PersistPokemonData, Task> addEncounter, Func<PersistPokemonData, Task> notifyTargetFound,
            Func<string, Task> notifyTrainer,
            Func<byte[], LegalityResult> analyze, TimeSpan battleStart, TimeSpan afterHandOff, TimeSpan stale, TimeSpan checksumWindow,
            TimeSpan cursorWait, CancellationToken ct)
        {
            IBot bot = state.Bot;
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
                        return await TrainerStopAsync(state, trainer.Group, trainer.Number, $"trainer {trainer.Trainer}, {s}", notifyTrainer);
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
                return await TrainerStopAsync(state, at[0].Bytes[0], at[0].Bytes[1], $"battle type 0x{flags:X4}, {s}", notifyTrainer);
            }
            if ((flags & WildBattle) == 0 || (flags & SafariBattle) != 0)
            {
                return Stop(state, NotWild, $"battle type 0x{flags:X4}, {s}");
            }

            bool seenMenu = false;
            int runPresses = 0;
            int presses = 0;
            try
            {
                while (true)
                {
                    // Before this battle's first menu the outcome can still be the last battle's: it stays until the next
                    // battle starts.
                    s = await GameStateWait.UntilAsync(reader, "a battle message, the action menu or the free field",
                        x => x.Screen is GameScreen.BattleMessage or GameScreen.BattleMenu or GameScreen.FieldFree
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
                        state.SwapState(EventState.BattleComplete);
                        return null;
                    }
                    if (s.Screen == GameScreen.BattleMessage)
                    {
                        int? window = s.TextPrinter;
                        presses += await PressAsync(bot, Macro("BattleAdvance"), ct);
                        GameSnapshot advanced = await GameStateWait.UntilAsync(reader, $"the battle window to leave {window}",
                            x => x.TextPrinter != window, stale, ct);
                        Console.WriteLine($"[RsBattle] BattleAdvance at window {window}, {since.ElapsedMilliseconds} ms: {advanced}");
                        continue;
                    }

                    // The action menu.
                    ChainBlockResult[] menu = await ReadMenuAsync(bot, addresses, ct);
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
                            menu = await ReadMenuAsync(bot, addresses, ct);
                            wild = state.Bot.Profile.Parser.Parse(menu[0].Bytes)!;
                        }
                        Console.WriteLine($"[RsBattle] {wild.Name} species {wild.Species} Lv {wild.Level} PV {wild.PID:X8} "
                            + $"shiny value {wild.ShinyValue} shiny {wild.IsShiny}");
                        // A GBA emulator can build a shiny no cartridge could (docs/decisions/global.md, "Every Gen 3 shiny is
                        // checked with PKHeX"): a shiny that fails is logged as not shiny, is not a target, and is fled like any other.
                        bool legal = true;
                        if (wild.IsShiny)
                        {
                            LegalityResult verdict = analyze(menu[0].Bytes);
                            legal = verdict.IsLegal;
                            Console.WriteLine(legal
                                ? $"[RsBattle] legality OK   {wild.Name} PV {wild.PID:X8}"
                                : $"[RsBattle] legality FAIL {wild.Name} PV {wild.PID:X8}: logged as not shiny, running{Environment.NewLine}{verdict.Report}");
                        }
                        if (await OrchestratorEvent.LogEncounterAndAskTargetAsync(state, wild, addEncounter, notifyTargetFound, legal))
                        {
                            Console.WriteLine("[RsBattle] a target: waiting at the action menu, nothing pressed");
                            return null;
                        }
                        seenMenu = true;
                    }
                    ushort leadHp = BitConverter.ToUInt16(menu[1].Bytes, 0);
                    if (leadHp == 0)
                    {
                        return Stop(state, Fainted, s.ToString());
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

                    // The cursor is on the snapshot only at the action menu, so s is the menu with the cursor on RUN.
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

        // The wild Pokémon's record and the lead's HP, on one frame.
        private static Task<ChainBlockResult[]> ReadMenuAsync(IBot bot, RsPointerService addresses, CancellationToken ct)
            => bot.ReadChainBlocksAsync(
                [new(addresses.EnemyParty, [], Gen3PokemonParser.PartyBlockSize), new(addresses.BattlePartyPokemon + LeadHpOffset, [], 2)], ct: ct);

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
            bool challenge = read[0].Bytes[0] != 0
                && (read[1].Bytes[0] & InBattleBit) == 0
                && read[2].Bytes[0] != NoScript
                && trainer != 0
                && (read[4].Bytes[flag >> 3] & (1 << (flag & 7))) == 0;
            return challenge ? (read[5].Bytes[0], read[5].Bytes[1], trainer) : null;
        }

        // The trainer stop's sentence; the map is group.number until the navigator has names.
        internal static string TrainerSentence(byte group, byte number) => $"A trainer battle started on map {group}.{number}.";

        // Parks the bot with the trainer's sentence, marks the stop as a trainer's for Resume, then sends the sentence.
        private static async Task<string> TrainerStopAsync(GameState state, byte group, byte number, string seen, Func<string, Task> notifyTrainer)
        {
            string sentence = Stop(state, TrainerSentence(group, number), seen);
            state.StoppedForTrainer = true;
            await notifyTrainer(sentence);
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
