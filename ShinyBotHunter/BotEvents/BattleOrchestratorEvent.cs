﻿using Discord;
using Microsoft.Extensions.Options;
using Mono.TextTemplating;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using YamlDotNet.Core;

namespace ShinyBotHunter.BotEvents
{
    public class BattleOrchestratorEvent : AbstractBotEvent<GameState>
    {
        internal static readonly Dictionary<int, string> MoveShorthands = new Dictionary<int, string>()
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

        private PokemonPersistenceService persistenceService;
        private PokemonMetadataService metadataService;
        private ApplicationSettings appSettings;
        private PokemonStateReader reader;
        private SmogonDamageService damageService;
        private ShinyAlertService shinyAlertService;
        private readonly LevelUpFlow levelUpFlow;

        // How long to wait for the battler to actually be out before calling the switch failed. Has to
        // outlast the switch animation plus the opponent's reply.
        private const int SwitchConfirmTimeoutMs = 4000;

        private const int MaxSwitchAttempts = 3;

        // Attempts spent on the current battle's switch. One instance serves one bot's battles in
        // sequence, so a field is enough; reset per battle in PokemonDataPending.
        private int switchAttempts = 0;
        public BattleOrchestratorEvent(PokemonPersistenceService persistenceService, IOptions<ApplicationSettings> appSettings, DiscordBotService discordBotService, PokemonStateReader reader,
                                        PokemonMetadataService metadataService, SmogonDamageService damageService, ShinyAlertService shinyAlertService, IServiceProvider provider)
        {
            this.persistenceService = persistenceService;
            this.appSettings = appSettings.Value;
            this.reader = reader;
            this.metadataService = metadataService;
            this.damageService = damageService;
            this.shinyAlertService = shinyAlertService;
            levelUpFlow = new LevelUpFlow(reader, metadataService, discordBotService);
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
                    await PokemonDataPending(state, cancellationToken);
                }
                else if (topState == EventState.DecideAction)
                {
                    DecideAction(state);
                }
                else if (topState == EventState.Fight)
                {
                    await SelectMove(state, cancellationToken);
                }
                else if (topState == EventState.SwitchPokemon)
                {
                    await SwitchPokemon(state, cancellationToken);
                }
                else if (topState == EventState.WaitForMovesEnd)
                {
                    await WaitForMoveEnd(state, cancellationToken);
                }
                else if (topState == EventState.OurPokemonFainted || topState == EventState.BothPokemonFainted)
                {
                    // TODO Implement losing our pokemon - for now we just defer to user to regain control
                    state.State.Pop();
                    state.State.Push(EventState.Waiting);
                }
                else if (topState == EventState.EnemyFainted)
                {
                    await levelUpFlow.WaitForLevelUpOrField(state, cancellationToken);
                }
                else if (topState == EventState.LevelUp)
                {
                    await levelUpFlow.ProcessLevelUp(state, cancellationToken);
                }
                else if (topState == EventState.EvolutionInit)
                {
                    await levelUpFlow.EvolutionInit(state, cancellationToken);
                }
                else if(topState == EventState.Flee)
                {
                    await Flee(state, cancellationToken);
                }
            }
            state.BattleState = null;
            return state;
        }

        private async Task Flee(GameState state, CancellationToken ct)
        {
            state.Bot.RunMacroString("W.10;S.Run.3;W.5;");
            await state.Bot.WaitForMacroAsync(ct);
            state.SwapState(EventState.WaitForMovesEnd);
        }

        private async Task WaitForMoveEnd(GameState state, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
            try
            {
                while (outcome is null && tasks.Count > 0)
                {
                    var finished = await Task.WhenAny(tasks);
                    tasks.Remove(finished);
                    outcome = await finished; // non-null: this task detected its condition
                }
            }
            finally
            {
                // Stop the losing polls and let them finish before the using disposes the source.
                cts.Cancel();
                await Task.WhenAll(tasks);
            }

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
                    await GameStateWait.UntilAsync(state.Bot.State, "the battle app gone",
                        s => s.Screen is GameScreen.FieldFree or GameScreen.FieldTask, stale: null, token);
                    return (EventState.BattleComplete, null);
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
                        if ((await state.Bot.State.ReadAsync(token)).Screen == GameScreen.BattleMenu)
                        {
                            var battleState = await reader.GetPokemonBattleState(state.Bot, ct: token);
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
                        var battleState = await reader.GetPokemonBattleState(state.Bot, ct: token);
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

        private async Task SelectMove(GameState state, CancellationToken ct)
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
                await state.Bot.WaitForMacroAsync(ct);
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
        private async Task SwitchPokemon(GameState state, CancellationToken ct)
        {
            int battlerIndex = state.SwitchSlot!.Value - 1;
            uint expectedPid = state.BattleState!.PartyPokemon[battlerIndex].PID;
            string battlerName = state.BattleState.PartyPokemon[battlerIndex].Name;

            string macro = MacroRegistry.Instance.GetMacro(state.GameFlavor, SwitchSlotMacros[state.SwitchSlot!.Value]);
            state.Bot.RunMacroString(macro);
            await state.Bot.WaitForMacroAsync(ct);

            SwitchOutcome outcome = await WaitForSwitchAsync(state, expectedPid, ct);
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
        private async Task<SwitchOutcome> WaitForSwitchAsync(GameState state, uint expectedPid, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < SwitchConfirmTimeoutMs)
            {
                if ((await state.Bot.State.ReadAsync(ct)).Screen is GameScreen.FieldFree or GameScreen.FieldTask)
                {
                    return SwitchOutcome.BattleOver;
                }
                var battleState = await reader.GetPokemonBattleState(state.Bot, ct: ct);
                if (battleState is not null && battleState.ActivePokemon.PID == expectedPid)
                {
                    state.BattleState = battleState;
                    return SwitchOutcome.Landed;
                }
                await Task.Delay(Constant.PollIntervalMs, ct);
            }
            return SwitchOutcome.NotLanded;
        }

        private async Task PokemonDataPending(GameState state, CancellationToken ct)
        {
            IBot bot = state.Bot;
            // The foe count and the foe block from app.setup (BattleSetup), both in one request. A
            // block that is not Ok (no battle app yet, or mid-write) is polled like a zero count.
            var hgss = (HgssPointerService)bot.Addresses;
            ChainBlockRequest[] foeBlocks =
            [
                new(bot.Addresses.Anchor, [hgss.AppSlotsOffset, hgss.SubAppOffset, hgss.AppSetupOffset, (int)bot.Addresses.EnemyCount], 1),
                new(bot.Addresses.Anchor, [hgss.AppSlotsOffset, hgss.SubAppOffset, hgss.AppSetupOffset, (int)bot.Addresses.Enemy], bot.Addresses.PartyPokemonSize),
            ];
            ChainBlockResult[] foe = await bot.ReadChainBlocksAsync(foeBlocks, ct: ct);
            while (foe[0].Status != ChainBlockStatus.Ok || foe[0].Bytes[0] == 0)
            {
                await Task.Delay(Constant.PollIntervalMs);
                foe = await bot.ReadChainBlocksAsync(foeBlocks, ct: ct);
            }

            PokemonData? pokemon = foe[1].Status == ChainBlockStatus.Ok ? bot.Profile.Parser.Parse(foe[1].Bytes) : null;
            while (pokemon is null || pokemon.IsCorrupt)
            {
                await Task.Delay(Constant.PollIntervalMs);
                foe = await bot.ReadChainBlocksAsync(foeBlocks, ct: ct);
                pokemon = foe[1].Status == ChainBlockStatus.Ok ? bot.Profile.Parser.Parse(foe[1].Bytes) : null;
            }


            // tap B until battle menu

            // this code is repeated but it's unique cause we need to progress through the first pokemon.
            bot.RunRepeatMacro("T.B;W.5");
            try
            {
                await GameStateWait.UntilAsync(bot.State, "the battle menu", s => s.Screen == GameScreen.BattleMenu, stale: null, ct);
            }
            finally
            {
                bot.ClearMacro();
            }
            try
            {
                await bot.WaitForMacroAsync(ct);
                bot.RunMacroString("W.5"); // TODO tune this. the menu flag is available before we can actually press so we want to wait by frame not real time.
                await bot.WaitForMacroAsync(ct);
                state.BattleState = await reader.GetPokemonBattleState(bot, ct: ct);
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
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
        }

    }
}
