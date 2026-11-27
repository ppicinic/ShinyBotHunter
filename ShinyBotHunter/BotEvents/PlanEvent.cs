using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// A saved plan's run (R6b): the step the in-game clock picks runs as one whole hunt, a fresh orchestrator over the one
    /// shared GameState, re-pointed with ChainedOrchestratorEvent.ApplyLeg as a chain's leg is. At each decision of that hunt
    /// the plan is asked again on the clock the decision read; when another step is picked the hunt ends there and the next
    /// step starts at Init, which walks to its anchor. A Wait step presses nothing and polls the clock.
    ///
    /// Steps are told apart by reference (done uses ReferenceEqualityComparer), one entry per step even when two steps have
    /// the same body.
    /// </summary>
    public class PlanEvent : AbstractBotEvent<EventState>
    {
        private static readonly TimeSpan WaitPoll = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan WaitStale = TimeSpan.FromMinutes(65);

        private readonly IServiceProvider provider;
        private readonly Plan plan;
        private readonly IReadOnlyList<(NewEventArguments Body, string Name, EventLeg? Leg, MilestoneAnchor? Anchor)> steps;
        private readonly Func<EventLeg, MilestoneAnchor?, Func<GameState, bool>, Func<GameState, CancellationToken, Task<EventState>>> hunt;
        private readonly TimeSpan poll;
        private readonly TimeSpan stale;

        // current, run, switching and done are written and read only by this event's one loop, in sequence (the hook runs
        // inside the step's hunt, on that loop); Resume restarts the loop only after it has returned, so there is no lock.
        // The step being run.
        private (NewEventArguments Body, string Name, EventLeg? Leg, MilestoneAnchor? Anchor) current;
        // The current step's hunt, kept while it is parked so Resume carries on the same hunt; null between steps and in a Wait.
        private Func<GameState, CancellationToken, Task<EventState>>? run;
        // Set by the hook when it ends the hunt for another step.
        private bool switching;
        // The steps whose hunt ended on its own in this run; it only grows, so a complete step is skipped until the next Start.
        private readonly HashSet<NewEventArguments> done = new(ReferenceEqualityComparer.Instance);

        public PlanEvent(IServiceProvider provider, Plan plan, IReadOnlyList<(NewEventArguments Body, string Name, EventLeg? Leg, MilestoneAnchor? Anchor)> steps)
            : this(provider, plan, steps, (leg, anchor, endsHere) => new OrchestratorEvent(provider, leg.EncounterParams, anchor, endsHere).ExecuteWithResultAsync,
                WaitPoll, WaitStale)
        {
        }

        // The test seam for the database boundary: OrchestratorEvent's constructor resolves Mongo-backed services, so a test
        // gives the step's hunt as a function, and short Wait spans.
        internal PlanEvent(IServiceProvider provider, Plan plan, IReadOnlyList<(NewEventArguments Body, string Name, EventLeg? Leg, MilestoneAnchor? Anchor)> steps,
            Func<EventLeg, MilestoneAnchor?, Func<GameState, bool>, Func<GameState, CancellationToken, Task<EventState>>> hunt, TimeSpan poll, TimeSpan stale)
        {
            this.provider = provider;
            this.plan = plan;
            this.steps = steps;
            this.hunt = hunt;
            this.poll = poll;
            this.stale = stale;
        }

        // Bot Control's current step. Written by the loop, read by GetBotDashboardState on request threads; the reference write
        // is atomic, and a stale read shows the previous step for one refresh.
        public string StepName { get; private set; } = "";

        public override async Task<EventState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            if (run is null && state.State.Peek() == EventState.Resume)
            {
                // Parked by a stale Wait: Resume re-enters the plan, not a bare step.
                state.SwapState(EventState.Init);
            }
            while (!cancellationToken.IsCancellationRequested)
            {
                if (run is null && !await BeginPickedStepAsync(state, cancellationToken))
                {
                    return EventState.Waiting;
                }
                EventState last = await run!(state, cancellationToken);
                if (last != EventState.Complete)
                {
                    // Waiting (a target, a wipe, a trainer stop, a walk with no path) or cancelled: the step stays parked and
                    // run is kept, so Resume continues the same hunt.
                    return last;
                }
                run = null;
                if (!switching)
                {
                    done.Add(current.Body);
                    var shinySteps = steps.Where(s => s.Leg?.Target.UsesMilestone == true).ToList();
                    if (plan.EndWhenComplete && shinySteps.Count > 0 && shinySteps.All(s => done.Contains(s.Body)))
                    {
                        return EventState.Complete;
                    }
                }
            }
            return state.State.Peek();
        }

        // Reads the clock and begins the step it picks, waiting first while the pick is Wait or nothing is open; false when a
        // stale Wait parked the plan.
        private async Task<bool> BeginPickedStepAsync(GameState state, CancellationToken ct)
        {
            var (weekday, hour) = await ReadClockAsync(state, ct);
            NewEventArguments? pick = plan.Pick(weekday, hour, done);
            if (pick is null || pick.EventName == Plan.Wait)
            {
                (pick, weekday, hour) = await WaitAsync(state, weekday, hour, ct);
                if (pick is null)
                {
                    return false;
                }
            }
            current = steps.First(s => ReferenceEquals(s.Body, pick));
            EventLeg leg = current.Leg!;
            Console.WriteLine($"[Plan] {plan.Name}: {weekday} {hour}h picks {current.Name}");
            switching = false;
            ChainedOrchestratorEvent.ApplyLeg(state, leg);
            // The step's Init captures the start tile and walks to its anchor, or hunts where the bot stands without one.
            (state.StartMapId, state.StartX, state.StartY, state.StartFacing) = (-1, -1, -1, Direction.None);
            state.SwapState(EventState.Init);
            StepName = leg.PhaseName;
            await provider.GetRequiredService<AdminService>().FollowHuntAsync(state.Bot.GameFlavor, state.Bot.GameFileName, leg.PhaseName);
            await RefreshDashboardAsync(state);
            run = hunt(leg, current.Anchor, EndsHere);
            return true;
        }

        // The hook at the step's decision, on the clock that decision just read: true when another step (or Wait) is picked.
        private bool EndsHere(GameState state)
            => switching = !ReferenceEquals(plan.Pick((DayOfWeek)state.RatesSeen!.Weekday!.Value, state.RatesSeen.Hour!.Value, done), current.Body);

        // Presses nothing. Ends in the step the clock picks, in cancellation through the delay, or, when the clock has stayed
        // at one weekday and hour for the stale span, parked in Waiting with the sentence. The pick is null when parked.
        private async Task<(NewEventArguments? Pick, DayOfWeek Weekday, int Hour)> WaitAsync(GameState state, DayOfWeek weekday, int hour, CancellationToken ct)
        {
            StepName = Plan.Wait;
            await RefreshDashboardAsync(state);
            Console.WriteLine($"[Plan] {plan.Name}: {weekday} {hour}h picks {Plan.Wait}");
            (DayOfWeek Weekday, int Hour) seen = (weekday, hour);
            DateTime since = DateTime.UtcNow;
            while (true)
            {
                await Task.Delay(poll, ct);
                var now = await ReadClockAsync(state, ct);
                if (plan.Pick(now.Weekday, now.Hour, done) is { } pick && pick.EventName != Plan.Wait)
                {
                    return (pick, now.Weekday, now.Hour);
                }
                if (now != seen)
                {
                    (seen, since) = (now, DateTime.UtcNow);
                }
                else if (DateTime.UtcNow - since >= stale)
                {
                    Console.WriteLine($"[Plan] {plan.Name}: the in-game clock stayed at {seen.Weekday} {seen.Hour}h for {stale.TotalMinutes:0} minutes; waiting for Resume.");
                    state.SwapState(EventState.Waiting);
                    return (null, now.Weekday, now.Hour);
                }
            }
        }

        private static async Task<(DayOfWeek Weekday, int Hour)> ReadClockAsync(GameState state, CancellationToken ct)
        {
            var (weekday, hour, _) = await ((HgssFieldSystemStateReader)state.Bot.State).ReadClockAndRepelAsync(ct);
            return ((DayOfWeek)weekday, hour);
        }

        // BotService is absent when a test drives the plan.
        private static async Task RefreshDashboardAsync(GameState state)
        {
            if (state.BotService is not null)
            {
                await state.BotService.RefreshDashboardState();
            }
        }
    }
}
