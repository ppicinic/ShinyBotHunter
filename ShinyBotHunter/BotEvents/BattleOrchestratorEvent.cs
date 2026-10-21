﻿using Microsoft.Extensions.DependencyInjection;
using Discord;
using Microsoft.Extensions.Options;
using Mono.TextTemplating;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Services;
using ShinyBotHunter.Util;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using YamlDotNet.Core;

namespace ShinyBotHunter.BotEvents
{
    public class BattleOrchestratorEvent : AbstractBotEvent<GameState>
    {
        private static readonly Dictionary<int, string> MoveShorthands = new Dictionary<int, string>()
        {
            { 0, "MoveOne" },
            { 1, "MoveTwo" },
            { 2, "MoveThree" },
            { 3, "MoveFour" },
        };

        // Switch-train macros, keyed by the 1-based party slot of the battler we switch to.
        private static readonly Dictionary<int, string> SwitchSlotMacros = new Dictionary<int, string>()
        {
            { 1, "SwitchToPartySlotOne" },
            { 2, "SwitchToPartySlotTwo" },
            { 3, "SwitchToPartySlotThree" },
            { 4, "SwitchToPartySlotFour" },
            { 5, "SwitchToPartySlotFive" },
            { 6, "SwitchToPartySlotSix" },
        };

        // Settle, advance the opening text, sit through the evolution animation, dismiss the result.
        //
        // The long wait has to outlast the animation, which varies by species -- it was about a
        // second short for the slowest ones, so the final tap landed while the animation was still
        // playing, got swallowed, and left the result screen up with nothing to dismiss it. Padded by
        // 120 frames. Overshooting is safe: the result screen waits for a button, so a late tap still
        // lands, while an early one is simply lost.
        private const string EvolutionMacro = "W.45;T.A;W.1245;T.A;W.15";

        // Settles through the level-up fanfare, then pages past the stat panel with two A presses.
        //
        // The leading wait carries ~1s of deliberate margin. It is measured from whenever this macro
        // starts, and that moment moved earlier when the battle-state read got faster: ProcessLevelUp's
        // own read shortened, and WaitForLevelUpOrField now polls on a much finer grain, so the level
        // field is seen updating before the panel accepts input. The margin is safe to overshoot --
        // the stat panel waits for a button, so waiting too long only costs time, while waiting too
        // little drops the press and desyncs every macro after it.
        private const string LevelUpAdvanceMacro = "W.325;T.A;W.5;T.A";

        // Advances dialogue from right after the level-up fanfare to the point where the
        // move-learn decision (or auto-learn text) is ready to act on.
        private const string LevelUpPrePromptMacro = "W.340";

        // Macros driving the "do you want to learn a new move?" flow, post level-up.
        private const string DoNotLearnMacro = "S.DoNotLearn;W.225;S.DoNotLearnComplete;W.5";
        private const string LearnMoveMacroTemplate = "S.NewMove;W.165;S.New{0};W.20;S.Forget;W.5";
        private const string FreeSlotMacro = "T.A;W.30;T.A;W.15";

        // Clones of the above two for the post-evolution move-learn prompt. Kept as separate
        // constants (rather than reused) because the evolution flow's screen sequence does not
        // necessarily match the post-level-up one -- tune these independently.
        private const string EvolutionLearnMoveInit = "T.A;W.150;T.A;W.150;";
        private const string EvolutionLearnMoveMacroTemplate = $"{EvolutionLearnMoveInit}S.125.75.5;W.240;S.Evo{{0}}.5;W.15;S.60.165.5;W.320;T.A;W.150;T.A;W.90;T.A;W.150;T.A;W.15";
        private const string EvolutionDoNotLearnMacro = $"{EvolutionLearnMoveInit}S.125.125.5;W.240;S.125.75.5;W.180;T.A;W.15";

        // The post-evolution free-slot path: with an open move slot the game learns the move outright
        // and only shows "<Pokemon> learned <Move>!", so this just settles and advances that text.
        // Deliberately not built on EvolutionLearnMoveInit -- there is no prompt to walk into here.
        // The level-up equivalent is FreeSlotMacro.
        private const string EvolutionFreeSlotMacro = "W.160;T.A;W.15";

        private IPokemonParser parser;
        private PokemonPersistenceService persistenceService;
        private PokemonMetadataService metadataService;
        private ApplicationSettings appSettings;
        private DiscordBotService discordBotService;
        private PokemonStateReader reader;
        private SmogonDamageService damageService;
        private ShinyAlertService shinyAlertService;

        // How long to wait for the battler to actually be out before calling the switch failed. Has to
        // outlast the switch animation plus the opponent's reply.
        private const int SwitchConfirmTimeoutMs = 4000;

        private const int MaxSwitchAttempts = 3;

        // Attempts spent on the current battle's switch. One instance serves one bot's battles in
        // sequence, so a field is enough; reset per battle in PokemonDataPending.
        private int switchAttempts = 0;
        public BattleOrchestratorEvent([FromKeyedServices(GameFlavor.HGSS)] IPokemonParser parser, PokemonPersistenceService persistenceService, IOptions<ApplicationSettings> appSettings, DiscordBotService discordBotService, PokemonStateReader reader,
                                        PokemonMetadataService metadataService, SmogonDamageService damageService, ShinyAlertService shinyAlertService, IServiceProvider provider)
        {
            this.parser = parser;
            this.persistenceService = persistenceService;
            this.appSettings = appSettings.Value;
            this.discordBotService = discordBotService;
            this.reader = reader;
            this.metadataService = metadataService;
            this.damageService = damageService;
            this.shinyAlertService = shinyAlertService;
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            while (state.State.Peek() != EventState.BattleComplete && state.State.Peek() != EventState.Waiting && !cancellationToken.IsCancellationRequested)
            {
                EventState topState = state.State.Peek();
                if (topState == EventState.BattleStart)
                {
                    state.State.Pop();
                    state.State.Push(EventState.PokemonDataPending);
                }
                else if (topState == EventState.PokemonDataPending)
                {
                    await PokemonDataPending(state);
                }
                else if (topState == EventState.DecideAction)
                {
                    DecideAction(state);
                }
                else if (topState == EventState.Fight)
                {
                    await SelectMove(state);
                }
                else if (topState == EventState.SwitchPokemon)
                {
                    await SwitchPokemon(state);
                }
                else if (topState == EventState.WaitForMovesEnd)
                {
                    await WaitForMoveEnd(state);
                }
                else if (topState == EventState.OurPokemonFainted || topState == EventState.BothPokemonFainted)
                {
                    // TODO Implement losing our pokemon - for now we just defer to user to regain control
                    state.State.Pop();
                    state.State.Push(EventState.Waiting);
                }
                else if (topState == EventState.EnemyFainted)
                {
                    await WaitForLevelUpOrField(state);
                }
                else if (topState == EventState.LevelUp)
                {
                    await ProcessLevelUp(state);
                }
                else if (topState == EventState.EvolutionInit)
                {
                    await EvolutionInit(state);
                }
                else if(topState == EventState.Flee)
                {
                    await Flee(state);
                }
            }
            state.BattleState = null;
            return state;
        }

        private async Task Flee(GameState state)
        {
            state.Bot.RunMacroString("W.10;S.Run.3;W.5;");
            await state.Bot.WaitForMacroAsync();
            state.SwapState(EventState.WaitForMovesEnd);
        }

        private async Task EvolutionInit(GameState state)
        {
            Console.WriteLine("start evolution");
            List<WrappedEvolutionMetadata> evolutions = new List<WrappedEvolutionMetadata>();
            // Order the evolutions and wrap the necessary data. also validate dead pokemon (not currently needed, but maybe needed in the future.
            foreach(var pokemon in state.BattleState!.PartyPokemon)
            {
                if(pokemon.HP > 0 && state.PendingEvolutions.ContainsKey((pokemon.PID, pokemon.Species)))
                {
                    evolutions.Add(new WrappedEvolutionMetadata()
                    {
                        EvolutionDetails = state.PendingEvolutions[(pokemon.PID, pokemon.Species)],
                        Pokemon = pokemon,
                    });
                }
            }
            foreach(var evo in evolutions)
            {
                state.Bot.RunMacroString(EvolutionMacro);
                await state.Bot.WaitForMacroAsync();

                // Evolution changes species/stats but not level or known moves, so evo.Pokemon
                // (captured before the evolution animation ran) still accurately reflects the
                // mon's level and moveset for the first move check. We deliberately do not read
                // fresh state here: if a move is about to be learned, the "trying to learn a
                // move" menu may already be up, and reads during that menu crash.
                var metadata = metadataService.GetPokemonMetadata(evo.EvolutionDetails.EvolveToId);
                var newMoves = metadata.LevelUpMoves[state.GameFlavor]
                    .Where(x => x.Level == evo.Pokemon.Level || x.Level == 0).ToList();
                newMoves.RemoveAll(m => evo.Pokemon.Moves.Any(k => k.Id == m.Id));
                newMoves.Sort();

                await ProcessEvolutionMoveLearning(state, evo.Pokemon, newMoves);

                // Refresh via the real party read, not the battle-state one -- the enemy is
                // long gone by this point in the flow, so there's nothing for the battle
                // reader's enemy pointer chain to resolve to. Safe here: any move-learn
                // decision has already fully resolved back to a normal screen by this point.
                // Party only: nothing below touches the boxes, and the box dump would drag a 72KB
                // read into the evolution flow.
                var freshParty = await reader.GetPartyState(state.Bot);
                state.BattleState!.PartyPokemon = freshParty.PartyPokemon.Take(freshParty.PartySize).Select(x => x!).ToList();
            }
            Console.WriteLine("end evolution");
            state.PendingEvolutions = new Dictionary<(uint, ushort), EvolutionMetadata>();
            state.SwapState(EventState.EnemyFainted);
        }

        private async Task ProcessLevelUp(GameState state)
        {
            var newState = await reader.GetPokemonBattleState(state.Bot);
            Console.WriteLine($"[LevelUp] fresh read: {(newState is null ? "null" : $"party={newState.PartyPokemon.Count}")}, prev party={state.BattleState!.PartyPokemon.Count}");
            var levelUp = GetLevelUp(state.BattleState!, newState);
            state.BattleState = newState;
            state.Bot.RunMacroString(LevelUpAdvanceMacro);
            await state.Bot.WaitForMacroAsync();
            if (levelUp is not null)
            {
                Console.WriteLine($"[LevelUp] {levelUp.Name} reached level {levelUp.Level}");
                var metadata = metadataService.GetPokemonMetadata(levelUp.Species);
                var newMoves = metadata.LevelUpMoves[state.GameFlavor].Where(x => x.Level == levelUp.Level).ToList();
                // A move the mon already knows produces no prompt at all in game, so it must never
                // reach the learn flow -- the macros there would run against dialogue that isn't up.
                newMoves.RemoveAll(m => levelUp.Moves.Any(k => k.Id == m.Id));
                newMoves.Sort();
                Console.WriteLine($"[LevelUp] moves to learn: {(newMoves.Count == 0 ? "none" : string.Join(", ", newMoves.Select(m => m.Name)))}");
                levelUp = await ProcessMoveLearning(state, levelUp, newMoves);
                Console.WriteLine("[LevelUp] move learning done, checking evolutions");
                ProcessEvolutions(levelUp, metadata, state, state.BattleState!);
            }
            state.SwapState(EventState.EnemyFainted);
        }

        private async Task<PokemonData> ProcessMoveLearning(GameState state, PokemonData pokemon, List<PokemonLevelUpMoveMetadata> newMoves)
        {
            // Reads are safe in the in-battle learn-move flow (unlike the evolution flow), so
            // every iteration starts from a fresh read. ApplyLearnedMove additionally records
            // each learn on the PokemonData itself -- memory may not reflect the final learn
            // until its dialogue finishes, and pokemon is the same object referenced by
            // state.BattleState.PartyPokemon, so EvolutionInit's snapshot sees it either way.
            foreach(var newMove in newMoves)
            {
                // Fresh read via the battle-state reader -- during battle the game works on the
                // battle copy of the party; the field party (GetPokemonState) is not synced
                // until the battle fully ends, so reading it here returns pre-battle data.
                var freshState = await reader.GetPokemonBattleState(state.Bot);
                if (freshState is not null)
                {
                    state.BattleState = freshState;
                    pokemon = state.BattleState.PartyPokemon.Where(x => x.PID == pokemon.PID).First();
                }
                // ProcessLevelUp already filtered these, but against a snapshot taken before the
                // level-up dialogue was advanced -- if the game auto-learned this move into a free
                // slot during that dialogue, only the fresh read above knows. Skipping is the whole
                // fix: an already-known move shows no prompt, so the macros below would desync us.
                if (pokemon.Moves.Any(m => m.Id == newMove.Id))
                {
                    Console.WriteLine($"{pokemon.Name} already knows {newMove.Name}, skipping learn flow");
                    continue;
                }
                bool hasFourMoves = true;
                foreach(var knownMove in pokemon.Moves)
                {
                    if(knownMove.Id == 0)
                    {
                        hasFourMoves = false;
                    }
                }
                if (hasFourMoves)
                {
                    // replace move.
                    state.Bot.RunMacroString(LevelUpPrePromptMacro);
                    await state.Bot.WaitForMacroAsync();
                    int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                    if(moveId == -1)
                    {
                        state.Bot.RunMacroString($"W.5;{DoNotLearnMacro}");
                        await state.Bot.WaitForMacroAsync();
                    }
                    else
                    {
                        string moveStr = MoveShorthands[moveId];
                        state.Bot.RunMacroString($"W.5;{string.Format(LearnMoveMacroTemplate, moveStr)}");
                        await state.Bot.WaitForMacroAsync();
                        ApplyLearnedMove(pokemon, moveId, newMove);
                    }
                }
                else
                {
                    // The mon has an open move slot, so the game auto-learns the move and just
                    // needs the "Pokemon learned Move!" text advanced.
                    Console.WriteLine($"{pokemon.Name} learned {newMove.Name} in an open move slot");
                    state.Bot.RunMacroString($"{LevelUpPrePromptMacro};{FreeSlotMacro}");
                    await state.Bot.WaitForMacroAsync();
                    ApplyLearnedMove(pokemon, FirstOpenMoveSlot(pokemon), newMove);
                }
            }
            return pokemon;
        }

        private async Task<PokemonData> ProcessEvolutionMoveLearning(GameState state, PokemonData pokemon, List<PokemonLevelUpMoveMetadata> newMoves)
        {
            // No memory reads in here: we're inside the evolution menus, where party state is
            // not readable. ApplyLearnedMove keeps pokemon.Moves accurate across multiple moves
            // so each Discord decision shows the current moveset.
            foreach(var newMove in newMoves)
            {
                // No fresh read is possible here, so this leans on the same ApplyLearnedMove cache
                // the loop already depends on: an already-known move produces no prompt in game, and
                // running the learn macros against dialogue that isn't up would desync us.
                if (pokemon.Moves.Any(m => m.Id == newMove.Id))
                {
                    Console.WriteLine($"{pokemon.Name} already knows {newMove.Name}, skipping learn flow");
                    continue;
                }
                bool hasFourMoves = true;
                foreach(var knownMove in pokemon.Moves)
                {
                    if(knownMove.Id == 0)
                    {
                        hasFourMoves = false;
                    }
                }
                if (!hasFourMoves)
                {
                    // An open slot means the game just learns the move -- there is no "forget which?"
                    // prompt at all. Asking Discord here answered a question nobody was asked, and the
                    // replace macros below would then run against dialogue that isn't on screen. It
                    // also crashed outright: the prompt builds a button per move slot, and an empty
                    // slot has no move metadata to name it.
                    Console.WriteLine($"{pokemon.Name} learned {newMove.Name} in an open move slot");
                    state.Bot.RunMacroString(EvolutionFreeSlotMacro);
                    await state.Bot.WaitForMacroAsync();
                    ApplyLearnedMove(pokemon, FirstOpenMoveSlot(pokemon), newMove);
                    continue;
                }
                int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                if(moveId == -1)
                {
                    state.Bot.RunMacroString($"W.5;{EvolutionDoNotLearnMacro}");
                    await state.Bot.WaitForMacroAsync();
                }
                else
                {
                    string moveStr = MoveShorthands[moveId];
                    state.Bot.RunMacroString($"W.5;{string.Format(EvolutionLearnMoveMacroTemplate, moveStr)}");
                    await state.Bot.WaitForMacroAsync();
                    ApplyLearnedMove(pokemon, moveId, newMove);
                }
            }
            return pokemon;
        }

        // We can't read memory to confirm a learn (dialogue still playing / evolution screen
        // up), but we know the decision's outcome exactly, so record it on our in-memory
        // PokemonData directly.
        private static void ApplyLearnedMove(PokemonData pokemon, int slot, PokemonLevelUpMoveMetadata newMove)
        {
            if (slot == 0)
            {
                pokemon.Move1 = (ushort)newMove.Id;
            }
            else if (slot == 1)
            {
                pokemon.Move2 = (ushort)newMove.Id;
            }
            else if (slot == 2)
            {
                pokemon.Move3 = (ushort)newMove.Id;
            }
            else if (slot == 3)
            {
                pokemon.Move4 = (ushort)newMove.Id;
            }
        }

        private static int FirstOpenMoveSlot(PokemonData pokemon)
        {
            var moves = pokemon.Moves;
            for (int i = 0; i < moves.Length; i++)
            {
                if (moves[i].Id == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        private void ProcessEvolutions(PokemonData levelUp, PokemonMetadata metadata, GameState state, PokemonBattleState battleState)
        {
            // The parser only creates this key on pokemon that are somebody's pre-evolution, so a
            // fully-evolved mon legitimately has no entry at all. Absent means "no evolutions".
            if (!metadata.EvolutionDetails.TryGetValue(state.GameFlavor, out var evolutionDetails))
            {
                return;
            }
            foreach(var evoDetails in evolutionDetails)
            {
                if(WillEvolve(levelUp, evoDetails, battleState))
                {
                    if(state.PendingEvolutions.ContainsKey((levelUp.PID, levelUp.Species)))
                    {
                        state.PendingEvolutions[(levelUp.PID, levelUp.Species)] = evoDetails;
                    }
                    else
                    {
                        state.PendingEvolutions.Add((levelUp.PID, levelUp.Species), evoDetails);
                    }
                }
            }
        }

        private bool WillEvolve(PokemonData levelUp, EvolutionMetadata evolutionMetadata, PokemonBattleState battleState)
        {
            if(evolutionMetadata.MinLevel is not null && levelUp.Level < evolutionMetadata.MinLevel)
            {
                return false;
            }
            if(evolutionMetadata.HeldItemId is not null && levelUp.HeldItem != evolutionMetadata.HeldItemId)
            {
                return false;
            }
            if(evolutionMetadata.TyrogueValue is not null)
            {
                if(evolutionMetadata.TyrogueValue == -1 && levelUp.Attack < levelUp.Defense)
                {
                    return true;
                }
                else if(evolutionMetadata.TyrogueValue == 0 && levelUp.Attack == levelUp.Defense)
                {
                    return true;
                }
                else if(evolutionMetadata.TyrogueValue == 1 && levelUp.Attack > levelUp.Defense)
                {
                    return true;
                }
            }
            DateTime date = DateTime.Now;
            bool isNight = date.Hour >= 20 || date.Hour < 4;
            if (evolutionMetadata.IsDay is not null && evolutionMetadata.IsDay.Value && isNight)
            {
                return false;
            }
            if(evolutionMetadata.IsNight is not null && evolutionMetadata.IsNight.Value && !isNight)
            {
                return false;
            }
            if(evolutionMetadata.PartySpeciesId is not null && battleState.PartyPokemon.Where(x => x.Species == evolutionMetadata.PartySpeciesId.Value).Count() == 0)
            {
                return false;
            }
            if(evolutionMetadata.KnownMoveId is not null && levelUp.Moves.Where(x => x.Id == evolutionMetadata.KnownMoveId.Value).Count() == 0)
            {
                return false;
            }
            if(evolutionMetadata.Gender is not null && levelUp.Gender != evolutionMetadata.Gender.Value)
            {
                return false;
            }
            if(evolutionMetadata.MinHappiness is not null && levelUp.Happiness < evolutionMetadata.MinHappiness)
            {
                return false;
            }
            if(evolutionMetadata.MinBeauty is not null && levelUp.Beauty < evolutionMetadata.MinBeauty)
            // TODO remove - block auto making a level up for the conditions we haven't checked yet.
            if(evolutionMetadata.MinLevel is null)
            {
                return false;
            }
            return true;
        }

        private async Task WaitForLevelUpOrField(GameState state)
        {
            bool endWait = false;
            

            Console.WriteLine("hit");
            while (!endWait)
            {
                await Task.Delay(Constant.PollIntervalMs);
                try
                {
                    if(state.PendingEvolutions.Count() > 0 && await state.Bot.GetBattleState() == 14)
                    {
                        state.SwapState(EventState.EvolutionInit);
                        endWait = true;
                    }
                    else if (!IsBattle(await state.Bot.GetBattleValue()))
                    {
                        state.SwapState(EventState.BattleComplete);
                        endWait = true;
                    }
                    else if (await reader.GetPokemonBattleState(state.Bot) is var battleState
                                && battleState is not null
                                && battleState.ActivePokemon.HP == 0)
                    {
                        state.SwapState(EventState.BothPokemonFainted);
                        endWait = true;
                    }
                    else if (GetLevelUp(state.BattleState!, battleState) is var levelUp && levelUp is not null)
                    {
                        state.SwapState(EventState.LevelUp);
                        endWait = true;
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    throw;
                }
            }
            Console.WriteLine(state.State.Peek().ToString());
        }

        private bool IsBattle(byte battleValue)
        {
            return battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0;
        }

        private PokemonData? GetLevelUp(PokemonBattleState oldState, PokemonBattleState? newState)
        {
            if(newState is null)
            {
                return null;
            }
            for(int i = 0; i < oldState.PartyPokemon.Count(); i++)
            {
                if (newState.PartyPokemon[i].Level > oldState.PartyPokemon[i].Level)
                {
                    return newState.PartyPokemon[i];
                }
            }
            return null;
        }

        private async Task WaitForMoveEnd(GameState state)
        {
            using var cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;

            // Poll each end-of-move condition concurrently and act on whichever trips first.
            // The cheap battle-end check no longer waits behind the heavy battle-state read.
            var tasks = new List<Task<(EventState next, PokemonBattleState? bs)?>>
            {
                PollBattleEnded(ct),
                PollMenuUp(ct),
                PollFaints(ct),
            };

            (EventState next, PokemonBattleState? bs)? outcome = null;
            while (outcome is null && tasks.Count > 0)
            {
                var finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);
                outcome = await finished; // non-null: this task detected its condition
            }

            cts.Cancel(); // stop the losing polls

            if (outcome is null)
                return;

            state.SwapState(outcome.Value.next);
            if (outcome.Value.bs is not null)
                state.BattleState = outcome.Value.bs;

            // ── Local poll tasks ────────────────────────────────────────────────────
            // Battle ended (e.g. a successful escape). One cheap byte read, polled at full rate.
            async Task<(EventState, PokemonBattleState?)?> PollBattleEnded(CancellationToken token)
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (!IsBattle(await state.Bot.GetBattleValue()))
                            return (EventState.BattleComplete, null);
                        await Task.Delay(Constant.PollIntervalMs, token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested) { }
                return null;
            }

            // Command menu is back up → decide the next action. Guarded on a valid battle state
            // so a stale post-battle read can't push DecideAction (the bug-2b invariant).
            async Task<(EventState, PokemonBattleState?)?> PollMenuUp(CancellationToken token)
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (await state.Bot.GetBattleMenuState() == 1)
                        {
                            var battleState = await reader.GetPokemonBattleState(state.Bot);
                            if (battleState is not null)
                                return (EventState.DecideAction, battleState);
                        }
                        await Task.Delay(Constant.PollIntervalMs, token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested) { }
                return null;
            }

            // A combatant fainted. Both checks share one battle-state read.
            async Task<(EventState, PokemonBattleState?)?> PollFaints(CancellationToken token)
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var battleState = await reader.GetPokemonBattleState(state.Bot);
                        if (battleState is not null)
                        {
                            if (battleState.ActivePokemon.HP == 0)
                                return (EventState.OurPokemonFainted, null);
                            if (battleState.EnemyPokemon.HP == 0)
                                return (EventState.EnemyFainted, null);
                        }
                        await Task.Delay(Constant.PollIntervalMs, token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested) { }
                return null;
            }
        }

        private void DecideAction(GameState state)
        {
            var lambda = (GameState s) =>
            {
                if(s.BattleMode == BattleMode.Flee)
                {
                    return EventState.Flee;
                }
                // In switch training slot 1 is the trainee and never fights, so the pokemon that has to
                // still be standing is the battler we switch to -- not the lead.
                int checkSlot = s.TrainType == TrainType.Switch ? s.SwitchSlot!.Value - 1 : 0;
                if (checkSlot >= state.BattleState!.PartyPokemon.Count() || state.BattleState.PartyPokemon[checkSlot].HP == 0)
                {
                    return EventState.Flee;
                }
                else if (s.TrainType == TrainType.Switch && !s.SwitchPerformed)
                {
                    return EventState.SwitchPokemon;
                }
                else if(BattleReadiness.IsTooHurtToFight(state.BattleState.ActivePokemon.HP, state.BattleState.ActivePokemon.MaxHP) || TotalDamagePP(state.BattleState.ActivePokemon) == 0) // if we can't kill or close to dying flee.
                {
                    return EventState.Flee;
                }
                else
                {
                    return EventState.Fight;
                }
            };
            EventState nextState = lambda(state);
            state.State.Pop();
            state.State.Push(nextState);
        }

        private int TotalDamagePP(BattlePokemon activePokemon)
        {
            var moves = activePokemon.Moves;
            int total = 0;
            foreach(var move in moves)
            {
                if(move.Id > 0 && metadataService.GetPokemonMoveMetadata(move.Id).IsDamageMove)
                {
                    total += move.PP;
                }
            }
            return total;
        }

        private async Task SelectMove(GameState state)
        {
            int move = damageService.PickBestMove(state.BattleState!);
            if(move == -1)
            {
                state.State.Pop();
                state.State.Push(EventState.Flee);
            }
            else
            {
                string moveStr = MoveShorthands[move];
                state.Bot.RunMacroString($"W.5;S.{moveStr};W.10;S.{moveStr}");
                await state.Bot.WaitForMacroAsync();
                state.State.Pop();
                state.State.Push(EventState.WaitForMovesEnd);
            }

        }

        /// <summary>
        /// Switch training: the trainee leads so it banks participation EXP, then we immediately swap to
        /// the configured battler. Switching burns our turn, so we hand off to WaitForMovesEnd and the
        /// next DecideAction fights normally with SwitchPerformed set.
        ///
        /// The switch is confirmed by PID before it counts as done. Marking it done blind used to mean
        /// a missed tap left the trainee out and the next DecideAction fought with it -- no EXP for the
        /// battler and nothing in the log to say so. A mistimed macro now retries and, if it keeps
        /// failing, flees loudly instead of quietly training the wrong pokemon.
        /// </summary>
        private async Task SwitchPokemon(GameState state)
        {
            int battlerIndex = state.SwitchSlot!.Value - 1;
            uint expectedPid = state.BattleState!.PartyPokemon[battlerIndex].PID;
            string battlerName = state.BattleState.PartyPokemon[battlerIndex].Name;

            string macro = MacroRegistry.Instance.GetMacro(state.GameFlavor, SwitchSlotMacros[state.SwitchSlot!.Value]);
            state.Bot.RunMacroString(macro);
            await state.Bot.WaitForMacroAsync();

            SwitchOutcome outcome = await WaitForSwitchAsync(state, expectedPid);
            if (outcome == SwitchOutcome.Landed)
            {
                switchAttempts = 0;
                state.SwitchPerformed = true;
                state.State.Pop();
                state.State.Push(EventState.WaitForMovesEnd);
                return;
            }

            if (outcome == SwitchOutcome.BattleOver)
            {
                // Escaped or wiped mid-switch; nothing left to switch into.
                Console.WriteLine("[Battle] Battle ended while switching.");
                state.State.Pop();
                state.State.Push(EventState.BattleComplete);
                return;
            }

            switchAttempts++;
            if (switchAttempts < MaxSwitchAttempts)
            {
                Console.WriteLine($"[Battle] Switch to slot {state.SwitchSlot!.Value} ({battlerName}) did not land, retrying ({switchAttempts}/{MaxSwitchAttempts}).");
                // Stay in SwitchPokemon so the macro runs again from the command menu.
                return;
            }

            // Fighting on would put EXP into the battler's place with the trainee, which is the exact
            // opposite of what switch training is for -- so give up on the encounter instead.
            Console.WriteLine($"[Battle] Switch to slot {state.SwitchSlot!.Value} ({battlerName}) failed {switchAttempts} times; fleeing. Check the party-screen tap coordinates and the SwitchPokemonInit settle.");
            switchAttempts = 0;
            state.State.Pop();
            state.State.Push(EventState.Flee);
        }

        private enum SwitchOutcome
        {
            Landed,
            NotLanded,
            BattleOver,
        }

        /// <summary>
        /// Polls until the battler is the active pokemon. The switch animation and the opponent's reply
        /// run on after the macro ends, so a single read straight afterwards would often still show the
        /// trainee and be mistaken for a failed switch.
        /// </summary>
        private async Task<SwitchOutcome> WaitForSwitchAsync(GameState state, uint expectedPid)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < SwitchConfirmTimeoutMs)
            {
                if (!IsBattle(await state.Bot.GetBattleValue()))
                {
                    return SwitchOutcome.BattleOver;
                }
                var battleState = await reader.GetPokemonBattleState(state.Bot);
                if (battleState is not null && battleState.ActivePokemon.PID == expectedPid)
                {
                    state.BattleState = battleState;
                    return SwitchOutcome.Landed;
                }
                await Task.Delay(Constant.PollIntervalMs);
            }
            return SwitchOutcome.NotLanded;
        }

        private async Task PokemonDataPending(GameState state)
        {
            IBot bot = state.Bot;
            uint anchor = await bot.ReadUIntAsync(0x21D4158);
            uint foeAnchorPtr = anchor + 0x6930;
            uint foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
            uint foeCountPtr = foeAnchor + 0xC14;
            var foeCount = await bot.ReadByteAsync(foeCountPtr);
            while (foeCount == 0)
            {
                await Task.Delay(Constant.PollIntervalMs);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                foeAnchorPtr = anchor + 0x6930;
                foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
                foeCountPtr = foeAnchor + 0xC14;
                foeCount = await bot.ReadByteAsync(foeCountPtr);
            }

            PokemonData? pokemon = pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            while (pokemon is null || pokemon.IsCorrupt)
            {
                await Task.Delay(Constant.PollIntervalMs);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                foeAnchor = await bot.ReadUIntAsync(anchor + 0x6930);
                pokemon = pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            }


            // tap B until battle menu

            // this code is repeated but it's unique cause we need to progress through the first pokemon.
            bot.RunRepeatMacro("T.B;W.5");
            anchor = await bot.ReadUIntAsync(0x21D4158);
            var battleMenuStatePtr = anchor + 0x230EC;
            byte battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            while (battleMenuState != 1)
            {
                await Task.Delay(Constant.PollIntervalMs);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                battleMenuStatePtr = anchor + 0x230EC;
                battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            }
            bot.ClearMacro();
            try
            {
                await bot.WaitForMacroAsync();
                bot.RunMacroString("W.5"); // TODO tune this. the menu flag is available before we can actually press so we want to wait by frame not real time.
                await bot.WaitForMacroAsync();
                state.BattleState = await reader.GetPokemonBattleState(bot);
                state.PendingEvolutions = new Dictionary<(uint, ushort), EvolutionMetadata>();
                // New battle -- the trainee is back out front, so we owe another switch.
                state.SwitchPerformed = false;
                switchAttempts = 0;
                if (state.BattleState is null)
                {
                    // Same reasoning as the unreadable-pokemon case below: without a battle state
                    // there is nothing to decide an action from, and dereferencing it would fault the
                    // whole event.
                    Console.WriteLine("[Battle] Battle state unreadable at battle start; fleeing.");
                    state.State.Pop();
                    state.State.Push(EventState.Flee);
                    return;
                }
                if (pokemon is null)
                {
                    // corrupted state. this should never happen but if it does we just flee cause we can't recover.
                    Console.WriteLine("read pokemon too fast");
                    state.State.Pop();
                    state.State.Push(EventState.Flee);
                }
                else
                {
                    Console.WriteLine($"{pokemon.Name}, isShiny: {pokemon.IsShiny}");
                    var persistPokemon = new PersistPokemonData()
                    {
                        Game = state.GameFlavor,
                        Phase = state.Milestone,
                        PokemonData = pokemon,
                        DateTime = DateTime.UtcNow,
                        GameFileName = bot.GameFileName,
                    };
                    await persistenceService.AddEncounterAsync(persistPokemon);
                    if (await state.Target.IsTargetAsync(pokemon))
                    {
                        state.TargetPokemon = persistPokemon;
                        await shinyAlertService.NotifyTargetFoundAsync(persistPokemon);
                        state.State.Pop();
                        state.State.Push(EventState.Waiting);
                    }
                    else
                    {
                        state.State.Pop();
                        state.State.Push(EventState.DecideAction);
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
        }

    }
}
