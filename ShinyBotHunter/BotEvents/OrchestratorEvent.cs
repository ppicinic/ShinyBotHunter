using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    public class OrchestratorEvent : AbstractBotEvent<EventState>
    {
        private bool gameStateSet = false;
        public GameState GameState
        {
            get; set
            {
                if (!gameStateSet)
                {
                    gameStateSet = true;
                    field = value;
                }
            }
        } = new GameState();

        // The catch check is the one read worth waiting a long time for -- see
        // ReadStateForCatchCheckAsync.
        private const int CatchReadAttempts = 5;
        private const int CatchReadRetryDelayMs = 1000;

        private EncounterParams encounterParams;
        private IBotEvent<GameState> battleEvent;
        private NavigatorEvent navigatorEvent;
        private PokemonStateReader reader;
        private PokemonMetadataService metadataService;
        private PokemonPersistenceService persistenceService;
        private PhaseInfoService phaseInfoService;

        public OrchestratorEvent(IServiceProvider provider, EncounterParams encounterParams)
        {
            this.encounterParams = encounterParams;
            battleEvent = provider.GetRequiredService<BattleOrchestratorEvent>();
            navigatorEvent = provider.GetRequiredService<NavigatorEvent>();
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.metadataService = provider.GetRequiredService<PokemonMetadataService>();
            this.persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            this.phaseInfoService = provider.GetRequiredService<PhaseInfoService>();
        }

        public override async Task<EventState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            GameState = state;
            // Resume can sit on top of an unfinished state, not just on an idle stack: an encounter
            // during a heal trip or the walk home leaves the navigation state underneath. Popping
            // back to it lets that leg carry on instead of spinning on a state nothing handles.
            if(state.State.Count > 0 && state.State.Peek() == EventState.Resume)
            {
                if (await CheckForCatchAsync(state))
                {
                    state.State.Pop();
                    if (state.State.Count == 0)
                    {
                        state.State.Push(EventState.DecideOverworldAction);
                    }
                }
                else
                {
                    // Back to Waiting rather than onwards: the event stays alive on the dashboard and
                    // another Resume can record the catch.
                    state.SwapState(EventState.Waiting);
                }
            }
            if(state.State.Count > 0 && state.State.Peek() == EventState.Init)
            {
                if(state.StartX == -1 && state.StartY == -1 && state.StartMapId == -1)
                {
                    await CaptureStartAsync(state, state.Bot.Overworld, cancellationToken);
                }
                // Every Init, not just the first: a chained leg re-enters here with the start tile
                // already captured and still needs its milestone state read fresh.
                await UpdateMilestoneState(state);
                state.SwapState(EventState.DecideOverworldAction);
            }
            while(state.State.Count != 0 && state.State.Peek() != EventState.Complete && state.State.Peek() != EventState.Waiting && !cancellationToken.IsCancellationRequested)
            {
                EventState topState = state.State.Peek();
                Console.WriteLine($"[Orchestrator] State: {topState}");
                if(topState == EventState.DecideOverworldAction)
                {
                    await DecideOverworldAction(state);
                }
                else if(topState == EventState.Encountering)
                {
                    await BuildEncounter(state.Bot, encounterParams).ExecuteAsync(state, cancellationToken);
                }
                else if(topState == EventState.BattleStart)
                {
                    await battleEvent.ExecuteWithResultAsync(state, cancellationToken);
                }
                else if(topState == EventState.BattleComplete)
                {
                    // Pop BattleComplete and let the previous state resume.
                    // If the stack empties (normal encounter path), restart at DecideOverworldAction.
                    state.State.Pop();
                    if (state.State.Count == 0)
                    {
                        state.State.Push(EventState.DecideOverworldAction);
                    }
                }
                else if(topState == EventState.GoToPokemonCenter
                     || topState == EventState.HealAtPokemonCenter
                     || topState == EventState.ReturnToStart)
                {
                    await navigatorEvent.ExecuteWithResultAsync(state, cancellationToken);
                }
            }
            return state.State.Count == 0 ? EventState.NotStarted : state.State.Peek();
        }

        // Dispatch on the configured type. The events report different result types, so they only
        // meet at the non-generic IBotEvent -- which is fine, the result is discarded.
        internal static IBotEvent BuildEncounter(IBot bot, EncounterParams encounterParams) => encounterParams.type switch
        {
            EncounterType.Spin => new SpinEncounterEvent(bot),
            EncounterType.Headbutt => new HeadbuttEncounterEvent(bot),
            _ => new RunEncounterEvent(bot, encounterParams),
        };

        internal static async Task CaptureStartAsync(GameState state, IOverworldReader overworld, CancellationToken cancellationToken)
        {
            state.StartMapId = await overworld.GetMapIdAsync(cancellationToken);
            (state.StartX, state.StartY) = await overworld.GetPositionAsync(cancellationToken);
            state.StartFacing = await overworld.GetFacingAsync(cancellationToken);
            Console.WriteLine($"[Orchestrator] Start tile captured: map={state.StartMapId} ({state.StartX},{state.StartY}) facing={state.StartFacing}");
        }

        // BotService is absent when the orchestrator is driven from the Test harness rather than the
        // dashboard, so nothing to notify.
        private static async Task RefreshDashboardAsync(GameState state)
        {
            if (state.BotService is not null)
            {
                await state.BotService.RefreshDashboardState();
            }
        }

        /// <summary>
        /// Returns false when the check could not be carried out at all, so the caller can leave the
        /// run parked rather than moving on and losing the catch.
        /// </summary>
        private async Task<bool> CheckForCatchAsync(GameState state)
        {
            if (state.TargetPokemon is null)
            {
                // Waiting wasn't a target sighting -- a wipe parks here too, and there is nothing
                // to look for in the party.
                await UpdateMilestoneState(state);
                return true;
            }
            // Boxes are required here: a caught pokemon may have gone straight to the PC, so this is
            // the one read that must not be narrowed to the party.
            PokemonState? pokemonState = await ReadStateForCatchCheckAsync(state);
            if (pokemonState is null)
            {
                // TargetPokemon is deliberately left set so a later resume can still record it.
                return false;
            }
            bool found = pokemonState.Search(state.TargetPokemon!.PokemonData);

            Console.WriteLine($"Found pokemon: {found}");
            if (found)
            {
                state.Bot.RunMacroString(MacroRegistry.Instance.GetMacro(state.GameFlavor, "SaveGame"));
                await state.Bot.WaitForMacroAsync();
                await persistenceService.AddCaughtPokemonAsync(state.TargetPokemon!);
                await Task.Delay(1000);
                await state.Bot.FlushSave();
            }
            state.TargetPokemon = null;
            await UpdateMilestoneState(state);
            return true;
        }

        /// <summary>
        /// Reads party and boxes for the catch check, retrying when the emulator can't produce a
        /// validated party.
        ///
        /// This read failing must never fault the event. The pokemon is already caught in game at this
        /// point, so a dropped run means it is never written to persistence and the milestone never
        /// completes -- state that has to be repaired by hand. Returning null instead parks the run so
        /// the check can be retried.
        /// </summary>
        private async Task<PokemonState?> ReadStateForCatchCheckAsync(GameState state)
        {
            for (int attempt = 1; attempt <= CatchReadAttempts; attempt++)
            {
                try
                {
                    return await reader.GetPokemonState(state.Bot);
                }
                catch (PartyReadUnavailableException ex)
                {
                    Console.WriteLine($"[Orchestrator] Catch check read failed (attempt {attempt}/{CatchReadAttempts}): {ex.Message}");
                    await Task.Delay(CatchReadRetryDelayMs);
                }
            }
            Console.WriteLine("[Orchestrator] Could not read party and boxes to confirm the catch. The pokemon is caught in game but not yet recorded -- staying in Waiting so Resume can try again.");
            return null;
        }

        private async Task UpdateMilestoneState(GameState state)
        {
            if (!state.Target.UsesMilestone)
            {
                // No milestone gates this run, so skip the caught-pokemon query entirely.
                // MilestoneComplete is not read for these targets.
                return;
            }
            var phaseInfo = phaseInfoService.GetPhaseInfo(state.GameFlavor, state.Milestone);
            var caughtPokemon = (await persistenceService.GetCaughtPokemonAsync(state.GameFlavor)).Where(x => x.GameFileName == state.Bot.GameFileName).ToList();
            bool complete = true;
            if (phaseInfo is not null)
            {
                foreach (var targetPokemon in phaseInfo.Pokemon)
                {
                    if (targetPokemon.RequireSpecificForm)
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.FormCode == targetPokemon.FormCode).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                    else
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.Name == targetPokemon.Name).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                }
            }
            state.MilestoneComplete = complete;
        }

        private async Task DecideOverworldAction(GameState state)
        {
            // Party only. This runs before every encounter and reads nothing but the lead and the
            // battler, so the box dump GetPokemonState performs -- 72KB and ~540 decrypts -- would be
            // thrown away every time.
            var pokeState = await reader.GetPartyState(state.Bot);
            var lead = pokeState.PartyPokemon[0]!;
            bool training = state.TrainType != TrainType.None;

            // No battle is in flight here, so a switch that aborted mid-encounter can't leak into the next one.
            state.SwitchPerformed = false;

            if (training && !state.TrainingComplete)
            {
                // The trainee always leads, in both standard and switch training.
                if (lead.Level >= state.TargetLevel!.Value)
                {
                    state.TrainingComplete = true;
                    state.OriginalBattleMode = BattleMode.Flee;
                }
            }

            // A target without a milestone (NoTarget) has nothing to complete, so the training
            // requirement alone ends the event -- BotService rejects that combination without one.
            bool milestoneSatisfied = !state.Target.UsesMilestone || state.MilestoneComplete;
            if (milestoneSatisfied && (!training || state.TrainingComplete))
            {
                // Walk home before finishing so whatever runs next starts from a known tile instead
                // of wherever the last encounter happened to leave us.
                state.CompleteAfterReturn = true;
                state.BattleMode = BattleMode.Flee;
                state.SwapState(EventState.ReturnToStart);
                await RefreshDashboardAsync(state);
                return;
            }

            if (state.OriginalBattleMode == BattleMode.Flee || CanFightNextEncounter(state, pokeState, lead))
            {
                state.BattleMode = state.OriginalBattleMode;
                state.SwapState(EventState.Encountering);
            }
            else
            {
                state.BattleMode = BattleMode.Flee;
                state.SwapState(EventState.GoToPokemonCenter);
            }
        }

        /// <summary>
        /// In switch training the lead only has to survive long enough to be sent out -- the battler in
        /// the configured slot is the one that needs HP and PP. A fainted lead is still fatal though:
        /// the game would send out someone else and the trainee would stop banking EXP entirely.
        /// </summary>
        private bool CanFightNextEncounter(GameState state, PokemonState pokeState, PokemonData lead)
        {
            if (state.TrainType != TrainType.Switch)
            {
                return CanBattle(lead);
            }
            int battlerIndex = state.SwitchSlot!.Value - 1;
            var battler = battlerIndex < pokeState.PartySize ? pokeState.PartyPokemon[battlerIndex] : null;
            if (battler is null)
            {
                // The configured slot is empty -- nothing to switch to, so treat it like a spent party.
                return false;
            }
            return lead.HP > 0 && CanBattle(battler);
        }

        private bool CanBattle(PokemonData pokemon)
        {
            // Same boundary the battle loop flees on -- see BattleReadiness.
            if(BattleReadiness.IsTooHurtToFight(pokemon.HP, pokemon.MaxHP))
            {
                return false;
            }
            int moveCount = 0;
            foreach(var move in pokemon.Moves)
            {
                if(move.Id > 0 && metadataService.GetPokemonMoveMetadata(move.Id).IsDamageMove) // TODO check for bad moves like False Swipe or recoil
                {
                    moveCount += move.PP;
                }
            }
            return moveCount >= 3;
        }
    }
}
