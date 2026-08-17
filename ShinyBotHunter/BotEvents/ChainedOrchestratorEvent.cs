using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// One leg of a chained run -- everything a single Wild Encounter needs, already validated by
    /// BotService. New per-leg options belong here rather than on GameState, which is shared by
    /// every leg in the chain.
    /// </summary>
    public record EventLeg(
        ITarget Target,
        string PhaseName,
        EncounterParams EncounterParams,
        TrainType TrainType,
        int? TargetLevel,
        int? SwitchSlot);

    /// <summary>
    /// Runs a sequence of orchestrator legs back to back over one shared GameState, so a run like
    /// "switch train to level 20, then lead train to level 30" needs no operator between the legs.
    ///
    /// Every leg ends by walking back to the start tile (see OrchestratorEvent's completion path), so
    /// the next leg starts from the same position -- which matters because RunEncounterEvent anchors
    /// its run corridor wherever the trainer is standing when the leg begins.
    ///
    /// A leg that parks in Waiting (target found, or a wipe) returns that state up to BotService,
    /// which keeps this instance as the bot's active event. Resume re-enters here and picks the same
    /// leg back up, then carries on through the rest of the chain.
    /// </summary>
    public class ChainedOrchestratorEvent : AbstractBotEvent<EventState>
    {
        public override EventState EventState => throw new NotImplementedException();

        private readonly IServiceProvider provider;
        private readonly List<EventLeg> legs;
        private readonly object legLock = new object();
        private int currentLegIndex = 0;
        private OrchestratorEvent? currentLegEvent = null;

        public ChainedOrchestratorEvent(IServiceProvider provider, IEnumerable<EventLeg> legs)
        {
            this.provider = provider;
            this.legs = legs.ToList();
            if (this.legs.Count == 0)
            {
                throw new ArgumentException("A chained event needs at least one leg.", nameof(legs));
            }
        }

        public int LegCount
        {
            get { lock (legLock) { return legs.Count; } }
        }

        public int CurrentLegNumber => currentLegIndex + 1;

        public override async Task<EventState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            EventState last = EventState.NotStarted;
            while (!cancellationToken.IsCancellationRequested)
            {
                EventLeg? leg = TryGetLeg(currentLegIndex);
                if (leg is null)
                {
                    break;
                }

                if (currentLegEvent is null)
                {
                    Console.WriteLine($"[Chain] Starting leg {CurrentLegNumber}/{LegCount}: phase={leg.PhaseName} train={leg.TrainType} targetLevel={leg.TargetLevel} switchSlot={leg.SwitchSlot}");
                    ApplyLeg(state, leg);
                    currentLegEvent = new OrchestratorEvent(provider, leg.EncounterParams);
                }

                last = await currentLegEvent.ExecuteWithResultAsync(state, cancellationToken);

                if (last != EventState.Complete)
                {
                    // Waiting (the operator has to resume) or an empty stack. Either way the chain
                    // stays parked on this leg -- currentLegEvent is kept so Resume continues it.
                    return last;
                }

                currentLegEvent = null;
                currentLegIndex++;
                Console.WriteLine($"[Chain] Leg {currentLegIndex}/{LegCount} complete.");
                if (TryGetLeg(currentLegIndex) is not null)
                {
                    // Complete -> Init hands the next orchestrator a stack it recognises.
                    state.SwapState(EventState.Init);
                }
                if (state.BotService is not null)
                {
                    await state.BotService.RefreshDashboardState();
                }
            }
            return last;
        }

        private EventLeg? TryGetLeg(int index)
        {
            lock (legLock)
            {
                return index < legs.Count ? legs[index] : null;
            }
        }

        /// <summary>
        /// Points the shared GameState at a leg. Resets everything a previous leg could have left
        /// dirty and deliberately preserves the start tile, bot, and pending evolutions, which belong
        /// to the chain as a whole rather than to one leg.
        /// </summary>
        private static void ApplyLeg(GameState state, EventLeg leg)
        {
            state.Target = leg.Target;
            state.Milestone = leg.PhaseName;
            state.TrainType = leg.TrainType;
            state.TargetLevel = leg.TrainType != TrainType.None ? leg.TargetLevel : null;
            state.SwitchSlot = leg.SwitchSlot;
            state.TrainingComplete = false;
            state.SwitchPerformed = false;
            state.OriginalBattleMode = leg.TrainType != TrainType.None ? BattleMode.Battle : BattleMode.Flee;
            state.BattleMode = state.OriginalBattleMode;
            state.MilestoneComplete = false;
            state.CompleteAfterReturn = false;
            state.TargetPokemon = null;
            state.NavigationPath = null;
            state.NavigationPathIndex = 0;
            // Built from the trainer's live position on first use, and this leg's ranges may differ.
            state.EncounterMacros.Remove("Run");
        }
    }
}
