using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

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

        // As Overworld.WaitForReadyAsync: the snapshot may stay unchanged this long before Resume parks again.
        private static readonly TimeSpan TrainerResumeStale = TimeSpan.FromSeconds(10);

        internal const string SlowTransitionLine = "A first Pokémon at or below the wild level gets the long battle transition.";

        private EncounterParams encounterParams;
        private IServiceProvider provider;
        // Made at the first BattleStart from the bot profile; one orchestrator runs one bot loop in sequence, no lock.
        private IBotEvent<GameState>? battleEvent;
        // Made at the first navigation state from the bot profile; one orchestrator runs one bot loop in sequence, no lock.
        private IBotEvent<GameState>? navigatorEvent;
        private PokemonStateReader reader;
        private PokemonMetadataService metadataService;
        private PokemonPersistenceService persistenceService;
        private PhaseInfoService phaseInfoService;
        private PickupTally pickupTally;
        // The hunt's anchor, or null to hunt where the bot stands (FR-23).
        private readonly MilestoneAnchor? anchor;
        // True from the Init that walks to the anchor until that walk ends; one orchestrator runs one bot loop in sequence, no lock.
        private bool walkingToAnchor;
        // The saved plan the hunt moves by (FR-33), or null; its own fields say how they are protected.
        private readonly HuntPlanRun? plan;

        public OrchestratorEvent(IServiceProvider provider, EncounterParams encounterParams, MilestoneAnchor? anchor = null, HuntPlanRun? plan = null)
        {
            this.encounterParams = encounterParams;
            this.anchor = anchor;
            this.plan = plan;
            this.provider = provider;
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.metadataService = provider.GetRequiredService<PokemonMetadataService>();
            this.persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            this.phaseInfoService = provider.GetRequiredService<PhaseInfoService>();
            this.pickupTally = provider.GetRequiredService<PickupTally>();
        }

        public override async Task<EventState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            GameState = state;
            // Resume can sit on top of an unfinished state, not just on an idle stack: an encounter
            // during a heal trip or the walk home leaves the navigation state underneath. Popping
            // back to it lets that leg carry on instead of spinning on a state nothing handles.
            if(state.State.Count > 0 && state.State.Peek() == EventState.Resume)
            {
                await ResumeAsync(state, () => CheckForCatchAsync(state, reader, provider, persistenceService.AddCaughtPokemonAsync, UpdateMilestoneState),
                    TrainerResumeStale, cancellationToken);
            }
            if(state.State.Count > 0 && state.State.Peek() == EventState.Init)
            {
                if(state.StartX == -1 && state.StartY == -1 && state.StartMapId == -1)
                {
                    await CaptureStartAsync(state, state.Bot.Overworld, cancellationToken);
                    walkingToAnchor = UseAnchor(state, anchor);
                }
                // Every Init, not just the first: a chained leg re-enters here with the start tile
                // already captured and still needs its milestone state read fresh.
                await UpdateMilestoneState(state);
                state.SwapState(walkingToAnchor ? EventState.ReturnToStart : EventState.DecideOverworldAction);
            }
            try
            {
                while(state.State.Count != 0 && state.State.Peek() != EventState.Complete && state.State.Peek() != EventState.Waiting && !cancellationToken.IsCancellationRequested)
                {
                    EventState topState = state.State.Peek();
                    Console.WriteLine($"[Orchestrator] State: {topState}");
                    if(topState == EventState.DecideOverworldAction)
                    {
                        // With a plan, the step's answer sets walkingToAnchor: true from the walk it starts until that walk ends.
                        await DecideOverworldAction(state, reader, metadataService.GetPokemonMoveMetadata, pickupTally, cancellationToken,
                            plan is null ? null : async s => walkingToAnchor = await plan.StepAsync(s,
                                m => provider.GetRequiredService<AdminService>().FollowHuntAsync(s.GameFlavor, s.Bot.GameFileName, m), UpdateMilestoneState, cancellationToken));
                        if (state.RatesSeen is { } seen)
                        {
                            phaseInfoService.RecordLive(state.Bot.GameFileName, seen);
                        }
                    }
                    else if(topState == EventState.Encountering)
                    {
                        await BuildEncounter(state.Bot, plan?.Stops[state.Milestone].Leg.EncounterParams ?? encounterParams).ExecuteAsync(state, cancellationToken);
                    }
                    else if(topState == EventState.BattleStart)
                    {
                        await (battleEvent ??= state.Bot.Profile.CreateBattleEvent!(provider)).ExecuteWithResultAsync(state, cancellationToken);
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
                        await (navigatorEvent ??= state.Bot.Profile.CreateNavigatorEvent!(provider)).ExecuteWithResultAsync(state, cancellationToken);
                        if (walkingToAnchor)
                        {
                            walkingToAnchor = plan?.Pending is { } next
                                ? AnchorWalkGoesOn(state, plan.Stops[next].Anchor.Anchor, plan.Stops[next].Name)
                                : AnchorWalkGoesOn(state, anchor!.Anchor, PhaseTarget.FindMilestone(provider.GetRequiredService<GameProfiles>().Get(state.GameFlavor).Milestones,
                                    provider.GetRequiredService<RomInfoService>().GetByHandle(state.Bot.GameFileName)?.Version, state.Milestone)?.Name ?? state.Milestone);
                            if (plan is not null && state.State.Peek() == EventState.DecideOverworldAction)
                            {
                                plan.WalkEnded = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                phaseInfoService.EndLive(state.Bot.GameFileName);
            }
            return state.State.Count == 0 ? EventState.NotStarted : state.State.Peek();
        }

        // Resume: after a trainer stop, the free field first; then the catch check, then on to the state underneath. A
        // separate method so a test can drive it, with fieldFreeStale short.
        internal static async Task ResumeAsync(GameState state, Func<Task<bool>> checkForCatch, TimeSpan fieldFreeStale, CancellationToken ct)
        {
            if (state.StoppedForTrainer)
            {
                // The speech or the battle may still be on: nothing is pressed or started until the field is free (FR-10).
                try
                {
                    await GameStateWait.UntilAsync(state.Bot.State, "the free field after the trainer stop",
                        s => s.Screen == GameScreen.FieldFree, fieldFreeStale, ct);
                }
                catch (GameStateTimeoutException ex)
                {
                    Console.WriteLine($"[Orchestrator] Resume: {ex.Message}");
                    state.SwapState(EventState.Waiting);
                    return;
                }
                state.StoppedForTrainer = false;
            }
            if (await checkForCatch())
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

        // Dispatch on the configured type. The events report different result types, so they only
        // meet at the non-generic IBotEvent -- which is fine, the result is discarded.
        internal static IBotEvent BuildEncounter(IBot bot, EncounterParams encounterParams) => encounterParams.type switch
        {
            EncounterType.Spin => new SpinEncounterEvent(bot) { LockedHandOff = bot.Profile.SeekLockHandOff },
            EncounterType.Headbutt => new HeadbuttEncounterEvent(bot),
            _ => new RunEncounterEvent(bot, encounterParams) { LockedHandOff = bot.Profile.SeekLockHandOff },
        };

        // FR-23: the anchor becomes the start tile, so the walk there, the heal trips and the end-of-hunt return all go to it
        // with the navigators' existing walk. The walk flees, as a heal trip does; DecideOverworldAction restores the battle mode.
        internal static bool UseAnchor(GameState state, MilestoneAnchor? anchor)
        {
            if (anchor is null)
            {
                return false;
            }
            state.StartMapId = anchor.Anchor.Map;
            state.StartX = anchor.Anchor.X;
            state.StartY = anchor.Anchor.Y;
            if (anchor.Facing != Direction.None)
            {
                state.StartFacing = anchor.Facing;
            }
            state.BattleMode = BattleMode.Flee;
            return true;
        }

        // Whether the walk to the anchor goes on after the navigator ran: still walking, or in a battle on the way. HGSS's
        // navigator completes in place when it finds no path, but a hunt that never reached its anchor is not complete (FR-23),
        // so that walk parks with the sentence after the navigator's own line with where the player stood.
        internal static bool AnchorWalkGoesOn(GameState state, Anchor anchor, string milestone)
        {
            EventState top = state.State.Peek();
            if (top is EventState.ReturnToStart or EventState.BattleStart)
            {
                return true;
            }
            if (top == EventState.Complete)
            {
                Console.WriteLine($"[Orchestrator] {milestone}: no path to the anchor ({anchor.X}, {anchor.Y}).");
                state.SwapState(EventState.Waiting);
            }
            return false;
        }

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
        internal static async Task<bool> CheckForCatchAsync(GameState state, PokemonStateReader reader, IServiceProvider provider,
            Func<PersistPokemonData, Task> addCaughtPokemon, Func<GameState, Task> updateMilestoneState)
        {
            if (state.TargetPokemon is null)
            {
                // Waiting wasn't a target sighting -- a wipe parks here too, and there is nothing
                // to look for in the party.
                await updateMilestoneState(state);
                return true;
            }
            // The boxes are read too: a caught pokemon may have gone straight to the PC.
            PokemonState? pokemonState = await ReadStateForCatchCheckAsync(state, reader);
            if (pokemonState is null)
            {
                // TargetPokemon is deliberately left set so a later resume can still record it.
                return false;
            }
            bool found = pokemonState.Search(state.TargetPokemon!.PokemonData);

            Console.WriteLine($"Found pokemon: {found}");
            if (found)
            {
                await state.Bot.Profile.SaveGame!(provider, state.Bot);
                await addCaughtPokemon(state.TargetPokemon!);
                await Task.Delay(1000);
                await state.Bot.FlushSave();
            }
            state.TargetPokemon = null;
            await updateMilestoneState(state);
            return true;
        }

        /// <summary>
        /// Reads the party and the boxes for the catch check, retrying when
        /// the emulator can't produce a validated party.
        ///
        /// This read failing must never fault the event. The pokemon is already caught in game at this
        /// point, so a dropped run means it is never written to persistence and the milestone never
        /// completes -- state that has to be repaired by hand. Returning null instead parks the run so
        /// the check can be retried.
        /// </summary>
        private static async Task<PokemonState?> ReadStateForCatchCheckAsync(GameState state, PokemonStateReader reader)
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
            if (plan is null)
            {
                state.MilestoneComplete = await IsCompleteAsync(state, state.Milestone);
                return;
            }
            // FR-34: the plan skips a complete stop; read here, at Init, the catch check and the plan's switch of milestone, never
            // on a decision that keeps its milestone.
            var complete = new HashSet<string>();
            foreach (string stop in plan.Stops.Keys)
            {
                if (await IsCompleteAsync(state, stop))
                {
                    complete.Add(stop);
                }
            }
            plan.Complete = complete;
            state.MilestoneComplete = complete.Contains(state.Milestone);
        }

        private async Task<bool> IsCompleteAsync(GameState state, string milestone)
        {
            var phaseInfo = phaseInfoService.GetPhaseInfo(state.GameFlavor, milestone);
            if (phaseInfo is null)
            {
                // A generated milestone is complete by its rows whatever the target (FR-14).
                var generated = PhaseTarget.FindMilestone(provider.GetRequiredService<GameProfiles>().Get(state.GameFlavor).Milestones,
                    provider.GetRequiredService<RomInfoService>().GetByHandle(state.Bot.GameFileName)?.Version, milestone);
                return IsGeneratedComplete(generated, await provider.GetRequiredService<MongoGameFileDao>().LoadAsync(state.Bot.GameFileName),
                    await persistenceService.GetCaughtPokemonAsync(state.GameFlavor));
            }
            var caughtPokemon = (await persistenceService.GetCaughtPokemonAsync(state.GameFlavor)).Where(x => x.GameFileName == state.Bot.GameFileName).ToList();
            return IsPhaseComplete(phaseInfo, caughtPokemon);
        }

        // FR-14: complete when none of the milestone's rows is a target. A name that is neither a phase nor a milestone of the
        // file's version is complete, as it was before generated milestones.
        internal static bool IsGeneratedComplete(Milestone? milestone, GameFile gameFile, IEnumerable<CaughtPokemon> caughtPokemon)
            => milestone is null || MilestoneRows.Build(milestone, gameFile, caughtPokemon).All(r => !r.IsTarget);

        // Today's phase rule, moved unchanged (FR-42); caughtPokemon is the file's catches.
        internal static bool IsPhaseComplete(PhaseInfo phaseInfo, IEnumerable<CaughtPokemon> caughtPokemon)
        {
            bool complete = true;
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
            return complete;
        }

        internal static async Task FinishAsync(GameState state)
        {
            if (state.Bot.Profile.Capabilities.HasOverworldNavigation)
            {
                // Walk home before finishing so whatever runs next starts from a known tile instead
                // of wherever the last encounter happened to leave us.
                state.CompleteAfterReturn = true;
                state.BattleMode = BattleMode.Flee;
                state.SwapState(EventState.ReturnToStart);
                await RefreshDashboardAsync(state);
                return;
            }
            // No navigation to walk home with, so the hunt completes in place.
            state.BattleMode = BattleMode.Flee;
            state.SwapState(EventState.Complete);
            await RefreshDashboardAsync(state);
        }

        /// <summary>Logs the wild Pokemon and asks the target. A target is kept for the catch check, alerted, and parks the
        /// stack in Waiting; on false the caller moves on.</summary>
        internal static async Task<bool> LogEncounterAndAskTargetAsync(GameState state, PokemonData pokemon,
            Func<PersistPokemonData, Task> addEncounter, Func<PersistPokemonData, Task> notifyTargetFound, bool legal = true)
        {
            var persistPokemon = new PersistPokemonData()
            {
                Game = state.GameFlavor,
                Phase = state.Milestone,
                PokemonData = pokemon,
                DateTime = DateTime.UtcNow,
                GameFileName = state.Bot.GameFileName,
                LegalityFailed = !legal,
            };
            await addEncounter(persistPokemon);
            // The first wild Pokémon at or above the lead's level is the first time the lead is not above every wild level seen.
            if (state.SpeedLines is { } lines && pokemon.Level >= state.LeadLevel && !lines.Contains(SlowTransitionLine))
            {
                state.SpeedLines = [.. lines, SlowTransitionLine];
                Console.WriteLine($"[Orchestrator] {SlowTransitionLine}");
                await RefreshDashboardAsync(state);
            }
            if (legal && await state.Target.IsTargetAsync(pokemon))
            {
                state.TargetPokemon = persistPokemon;
                await notifyTargetFound(persistPokemon);
                state.State.Pop();
                state.State.Push(EventState.Waiting);
                return true;
            }
            return false;
        }

        // Static with its services passed in, so a test can drive the decisions without the services that open a database.
        internal static async Task DecideOverworldAction(GameState state, PokemonStateReader reader, Func<int, PokemonMoveMetadata> moveMetadata,
            PickupTally pickupTally, CancellationToken cancellationToken, Func<GameState, Task<bool>>? followPlan = null)
        {
            // Party only. This runs before every encounter and reads nothing but the lead and the
            // battler, so the box dump GetPokemonState performs -- 72KB and ~540 decrypts -- would be
            // thrown away every time.
            var pokeState = await reader.GetPartyState(state.Bot);
            var lead = pokeState.PartyPokemon[0]!;
            // FR-26 on HGSS, in one request beside the party read; RS's counter is B3r2's (#556).
            if (state.Bot.Addresses is HgssPointerService)
            {
                (int weekday, int hour, int? repelSteps) = await ((HgssFieldSystemStateReader)state.Bot.State).ReadClockAndRepelAsync(cancellationToken);
                state.RatesSeen = RateInputs.Of(weekday, hour, repelSteps, pokeState);
            }
            // FR-33: a saved plan's step, on the clock just read; true when it took the stack to walk or to park.
            if (followPlan is not null && await followPlan(state))
            {
                return;
            }
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
                await FinishAsync(state);
                return;
            }
            if (state.OriginalBattleMode == BattleMode.Flee || CanFightNextEncounter(state, pokeState, lead, moveMetadata))
            {
                if (state.Pickup)
                {
                    // Loaded at the decision, as the Primo config is at its start, so a value can be corrected without a build.
                    PickupConfig config = PickupConfig.Load(PickupConfig.DefaultPath);
                    // The visit is awaited inline; a stop has already parked the stack in Waiting.
                    if (!await new PickupTakeEvent(state.Bot, reader, config, pickupTally).ExecuteWithResultAsync(state, cancellationToken))
                    {
                        return;
                    }
                }
                state.BattleMode = state.OriginalBattleMode;
                state.SwapState(EventState.Encountering);
            }
            else
            {
                Console.WriteLine($"[Orchestrator] {HealTripLine(state, pokeState, lead, moveMetadata)}");
                state.BattleMode = BattleMode.Flee;
                state.SwapState(EventState.GoToPokemonCenter);
            }
        }

        /// <summary>
        /// In switch training the lead only has to survive long enough to be sent out -- the battler in
        /// the configured slot is the one that needs HP and PP. A fainted lead is still fatal though:
        /// the game would send out someone else and the trainee would stop banking EXP entirely.
        /// </summary>
        internal static bool CanFightNextEncounter(GameState state, PokemonState pokeState, PokemonData lead, Func<int, PokemonMoveMetadata> moveMetadata)
        {
            if (state.TrainType != TrainType.Switch)
            {
                return CanBattle(lead, moveMetadata);
            }
            var battler = Battler(state, pokeState);
            if (battler is null)
            {
                // The configured slot is empty -- nothing to switch to, so treat it like a spent party.
                return false;
            }
            return lead.HP > 0 && CanBattle(battler, moveMetadata);
        }

        // Why the heal trip was taken: the member the check above read (the lead, or a living lead's battler) with its HP and
        // damaging PP, or the empty slot.
        internal static string HealTripLine(GameState state, PokemonState pokeState, PokemonData lead, Func<int, PokemonMoveMetadata> moveMetadata)
        {
            PokemonData? member = state.TrainType == TrainType.Switch && lead.HP > 0 ? Battler(state, pokeState) : lead;
            return member is null
                ? $"heal trip: switch slot {state.SwitchSlot} is empty"
                : $"heal trip: {member.Name} HP {member.HP}/{member.MaxHP}, damaging PP {DamagingPp(member, moveMetadata)}";
        }

        private static PokemonData? Battler(GameState state, PokemonState pokeState)
        {
            int battlerIndex = state.SwitchSlot!.Value - 1;
            return battlerIndex < pokeState.PartySize ? pokeState.PartyPokemon[battlerIndex] : null;
        }

        private static bool CanBattle(PokemonData pokemon, Func<int, PokemonMoveMetadata> moveMetadata)
        {
            // Same boundary the battle loop flees on -- see BattleReadiness.
            if(BattleReadiness.IsTooHurtToFight(pokemon.HP, pokemon.MaxHP))
            {
                return false;
            }
            return DamagingPp(pokemon, moveMetadata) >= 3;
        }

        private static int DamagingPp(PokemonData pokemon, Func<int, PokemonMoveMetadata> moveMetadata)
        {
            int moveCount = 0;
            foreach(var move in pokemon.Moves)
            {
                if(move.Id > 0 && moveMetadata(move.Id).IsDamageMove) // TODO check for bad moves like False Swipe or recoil
                {
                    moveCount += move.PP;
                }
            }
            return moveCount;
        }
    }
}
