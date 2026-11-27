using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // A saved plan's run (R6b) through PlanEvent's seam: each step's hunt is a scripted function in place of an orchestrator,
    // which resolves services that open a database. The clock is the real HGSS read over the fake bot's chain blocks.
    public class HuntPlanRunTests
    {
        private static readonly HgssPointerService Table = new();
        private const string PlanName = "Johto nights";
        private const string Route30 = "1-4 Route 30", Route32 = "2-1 Route 32";

        // A Morning rule that flees on Route 30, and a default that trains to 30 with Pickup on Route 32; both have an anchor.
        private static readonly EventLeg MorningLeg = new(new ShinyTarget(), Route30, new EncounterParams(EncounterType.Run, 2, 0), TrainType.None, null, null);
        private static readonly EventLeg DefaultLeg = new(new ShinyTarget(), Route32, new EncounterParams(EncounterType.Run, 0, 2), TrainType.Standard, 30, null, Pickup: true);
        private static readonly MilestoneAnchor MorningAnchor = new(Route30, new Anchor(29, 10, 20, 0, 2), Direction.None);
        private static readonly MilestoneAnchor DefaultAnchor = new(Route32, new Anchor(36, 458, 339, 2, 0), Direction.Down);

        private static NewEventArguments Wild(string milestone) => new() { EventName = "Wild Encounter", PhaseName = milestone };

        // The plan and its steps as Start resolves them; the bodies are the plan's own, so the run tells steps apart by them.
        private static (Plan Plan, (NewEventArguments, string, EventLeg?, MilestoneAnchor?)[] Steps) MorningOn30(bool endWhenComplete = false)
        {
            NewEventArguments morning = Wild(Route30), @default = Wild(Route32);
            Plan plan = new(GameFlavor.HGSS, PlanName, [new PlanStep([SlotCondition.Morning], [], morning)], @default, endWhenComplete);
            return (plan, [(morning, Route30, MorningLeg, MorningAnchor), (@default, Route32, DefaultLeg, DefaultAnchor)]);
        }

        // Night is Wait; any other time is the default.
        private static (Plan Plan, (NewEventArguments, string, EventLeg?, MilestoneAnchor?)[] Steps) WaitAtNight()
        {
            NewEventArguments wait = new() { EventName = Plan.Wait }, @default = Wild(Route32);
            Plan plan = new(GameFlavor.HGSS, PlanName, [new PlanStep([SlotCondition.Night], [], wait)], @default, false);
            return (plan, [(wait, Plan.Wait, null, null), (@default, Route32, DefaultLeg, DefaultAnchor)]);
        }

        // The clock reads Thursday at the scripted hours in order, the last one repeating; each read is counted.
        private sealed class Clock(params int[] hours)
        {
            public List<int> Hours { get; } = [.. hours];
            public int Reads { get; private set; }
            public Action<int>? OnRead { get; set; }

            public ChainBlockResult[] Read(IReadOnlyList<ChainBlockRequest> blocks)
            {
                int hour = Hours[Math.Min(Reads, Hours.Count - 1)];
                Reads++;
                OnRead?.Invoke(Reads);
                return [new(ChainBlockStatus.Ok, 0, [.. BitConverter.GetBytes(4u), .. BitConverter.GetBytes((uint)hour)]), new(ChainBlockStatus.NullPointer, 0, [])];
            }
        }

        private static FakeBot BotWith(Clock clock, List<string> pressed)
        {
            FakeBot bot = new(Task.CompletedTask) { ChainBlocks = clock.Read, Pressed = pressed.Add };
            bot.State = new HgssFieldSystemStateReader(bot, Table);
            return bot;
        }

        // An AdminService from its test constructor with a follows-the-hunt card on the bot's file, so each follow is one push.
        private static async Task<(IServiceProvider Provider, List<DashboardConfig> Pushes)> ProviderAsync()
        {
            List<DashboardConfig> pushes = [];
            AdminService admin = new(c => { pushes.Add(c); return Task.CompletedTask; }, () => Task.FromResult<(DashboardConfig, Appearance?)?>(null),
                _ => Task.CompletedTask, () => Task.FromResult<Appearance?>(null), _ => Task.CompletedTask, NullLogger.Instance);
            await admin.SubscribeGamePhaseAsync("HGSS", "none yet", "fake.nds", followsHunt: true);
            pushes.Clear();
            return (new ServiceCollection().AddSingleton(admin).BuildServiceProvider(), pushes);
        }

        private static string[] Follows(List<DashboardConfig> pushes) => [.. pushes.Select(c => c.GamePhases.Single(p => p.followsHunt).phase)];

        // What a step's hunt saw when it first ran.
        private sealed record Entry(EventLeg Leg, MilestoneAnchor? Anchor, TrainType TrainType, int? TargetLevel, bool Pickup, bool TrainingComplete,
            BattleMode OriginalBattleMode, BattleMode BattleMode, int StartMapId, int StartX, int StartY, EventState[] Stack);

        private delegate EventState Step(EventLeg leg, Func<GameState, bool> endsHere, GameState state, int call);

        // The seam's hunt: each hunt made is recorded with what it saw on its first run; every run calls the script with the
        // hunt's leg, the plan's hook and that hunt's call number, and leaves the script's state on top, as a hunt returns.
        private static Func<EventLeg, MilestoneAnchor?, Func<GameState, bool>, Func<GameState, CancellationToken, Task<EventState>>> Hunt(List<Entry> made, Step step)
            => (leg, anchor, endsHere) =>
            {
                int calls = 0;
                return (s, _) =>
                {
                    if (calls == 0)
                    {
                        made.Add(new Entry(leg, anchor, s.TrainType, s.TargetLevel, s.Pickup, s.TrainingComplete, s.OriginalBattleMode, s.BattleMode,
                            s.StartMapId, s.StartX, s.StartY, [.. s.State]));
                    }
                    EventState result = step(leg, endsHere, s, ++calls);
                    s.SwapState(result);
                    return Task.FromResult(result);
                };
            };

        // A decision of the hunt on the clock at this hour: the hook is asked as the real decision asks it, and a true ends the
        // hunt with Complete.
        private static EventState DecideAt(GameState state, Func<GameState, bool> endsHere, int hour, EventState otherwise, int weekday = 4)
        {
            state.RatesSeen = new RateInputs(weekday, hour, null, null, 0, 12, false);
            return endsHere(state) ? EventState.Complete : otherwise;
        }

        private static GameState StartState(FakeBot bot)
        {
            GameState state = new() { Bot = bot, Profile = bot.Profile };
            state.State.Push(EventState.Init);
            return state;
        }

        private static PlanEvent Event(IServiceProvider provider, (Plan Plan, (NewEventArguments, string, EventLeg?, MilestoneAnchor?)[] Steps) plan,
            Func<EventLeg, MilestoneAnchor?, Func<GameState, bool>, Func<GameState, CancellationToken, Task<EventState>>> hunt, TimeSpan? stale = null)
            => new(provider, plan.Plan, plan.Steps, hunt, TimeSpan.Zero, stale ?? TimeSpan.FromMinutes(65));

        [Fact]
        public async Task Start_RunsTheStepTheClockPicks()
        {
            var (provider, pushes) = await ProviderAsync();
            Clock clock = new(9);
            List<string> pressed = [];
            GameState state = StartState(BotWith(clock, pressed));
            List<Entry> made = [];
            PlanEvent plan = Event(provider, MorningOn30(), Hunt(made, (_, _, _, _) => EventState.Waiting));

            EventState result = await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Waiting, result);
            Entry hunt = Assert.Single(made);
            Assert.Same(MorningLeg, hunt.Leg);
            Assert.Same(MorningAnchor, hunt.Anchor);
            Assert.Equal((TrainType.None, false), (hunt.TrainType, hunt.Pickup));
            Assert.Equal((-1, -1, -1), (hunt.StartMapId, hunt.StartX, hunt.StartY));
            Assert.Equal([EventState.Init], hunt.Stack);
            Assert.Equal([Route30], Follows(pushes));
            Assert.Equal(Route30, plan.StepName);
            Assert.Equal(1, clock.Reads);
        }

        // Acceptance: the flee step's decision at 10:00 ends its hunt, and the default's hunt starts training with Pickup.
        [Fact]
        public async Task Switch_FleeToTrainWithPickup_AtTheBoundary()
        {
            var (provider, pushes) = await ProviderAsync();
            Clock clock = new(9, 10);
            GameState state = StartState(BotWith(clock, []));
            state.StartMapId = 29;
            List<Entry> made = [];
            bool? morningPickedAgain = null;
            PlanEvent plan = Event(provider, MorningOn30(), Hunt(made, (leg, endsHere, s, _) =>
            {
                if (leg == MorningLeg)
                {
                    return DecideAt(s, endsHere, 10, EventState.Waiting);
                }
                // Asked at 9h and parked: the Morning step was switched away from, not done, so the plan would pick it again.
                morningPickedAgain = DecideAt(s, endsHere, 9, EventState.Waiting) == EventState.Complete;
                return EventState.Waiting;
            }));

            await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal([MorningLeg, DefaultLeg], made.Select(m => m.Leg));
            Entry train = made[1];
            Assert.Equal((TrainType.Standard, 30, true), (train.TrainType, train.TargetLevel, train.Pickup));
            Assert.Equal((BattleMode.Battle, BattleMode.Battle), (train.OriginalBattleMode, train.BattleMode));
            Assert.Equal((-1, -1, -1), (train.StartMapId, train.StartX, train.StartY));
            Assert.Equal([EventState.Init], train.Stack);
            Assert.Equal([Route30, Route32], Follows(pushes));
            Assert.Equal(Route32, plan.StepName);
            Assert.True(morningPickedAgain);
        }

        [Fact]
        public async Task SameStep_AtTheDecision_DoesNotEnd()
        {
            var (provider, _) = await ProviderAsync();
            GameState state = StartState(BotWith(new Clock(9), []));
            List<Entry> made = [];
            EventState? decided = null;
            PlanEvent plan = Event(provider, MorningOn30(), Hunt(made, (_, endsHere, s, _) => (decided = DecideAt(s, endsHere, 9, EventState.Waiting)).Value));

            EventState result = await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Waiting, decided);
            Assert.Equal(EventState.Waiting, result);
            Assert.Single(made);
        }

        // Acceptance (default 8): a step whose hunt ended on its own is skipped for the rest of the run.
        [Fact]
        public async Task CompleteStep_IsSkipped()
        {
            var (provider, _) = await ProviderAsync();
            GameState state = StartState(BotWith(new Clock(9), []));
            List<Entry> made = [];
            EventState? defaultDecided = null;
            PlanEvent plan = Event(provider, MorningOn30(), Hunt(made, (leg, endsHere, s, _) => leg == MorningLeg
                ? EventState.Complete
                : (defaultDecided = DecideAt(s, endsHere, 9, EventState.Waiting)).Value));

            await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal([MorningLeg, DefaultLeg], made.Select(m => m.Leg));
            // Still Morning at 9h, and the default's decision does not end: the Morning step is not picked again.
            Assert.Equal(EventState.Waiting, defaultDecided);
        }

        // Default 8: returning to a training step is a new hunt with the step's own target level, not a done step.
        [Fact]
        public async Task TrainingStep_ReturnedTo_KeepsItsTargetLevel()
        {
            var (provider, _) = await ProviderAsync();
            GameState state = StartState(BotWith(new Clock(12, 4, 10), []));
            List<Entry> made = [];
            PlanEvent plan = Event(provider, MorningOn30(), Hunt(made, (leg, endsHere, s, call) =>
            {
                if (leg == MorningLeg)
                {
                    return DecideAt(s, endsHere, 10, EventState.Waiting);
                }
                if (made.Count(m => m.Leg == DefaultLeg) == 1)
                {
                    s.TrainingComplete = true;
                    return DecideAt(s, endsHere, 4, EventState.Waiting, weekday: 5);
                }
                return EventState.Waiting;
            }));

            await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal([DefaultLeg, MorningLeg, DefaultLeg], made.Select(m => m.Leg));
            Assert.Equal((30, false), (made[2].TargetLevel, made[2].TrainingComplete));
        }

        // Acceptance (no switch mid-battle): a hunt parked in Waiting is not ended though the clock has crossed the boundary;
        // Resume carries on the same hunt, and the switch comes only at its decision.
        [Fact]
        public async Task ClockCrossesTheBoundaryMidBattle_HuntIsNotEnded()
        {
            var (provider, pushes) = await ProviderAsync();
            Clock clock = new(9, 10);
            GameState state = StartState(BotWith(clock, []));
            List<Entry> made = [];
            int morningCalls = 0;
            PlanEvent plan = Event(provider, MorningOn30(), Hunt(made, (leg, endsHere, s, call) =>
            {
                if (leg != MorningLeg)
                {
                    return EventState.Waiting;
                }
                morningCalls = call;
                // The first run parks in a battle's trainer stop with the clock already at 10h; the resumed run reaches its decision.
                return call == 1 ? EventState.Waiting : DecideAt(s, endsHere, 10, EventState.Waiting);
            }));

            EventState parked = await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Waiting, parked);
            Assert.Single(made);
            Assert.Equal(1, clock.Reads);
            Assert.Equal([Route30], Follows(pushes));

            state.SwapState(EventState.Resume);
            await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(2, morningCalls);
            Assert.Equal([MorningLeg, DefaultLeg], made.Select(m => m.Leg));
        }

        // Rule 16: Wait presses nothing, and ends when the clock picks a step.
        [Fact]
        public async Task Wait_PressesNothing_EndsWhenTheClockPicksAStep()
        {
            var (provider, pushes) = await ProviderAsync();
            Clock clock = new(21, 21, 4);
            List<string> pressed = [];
            FakeBot bot = BotWith(clock, pressed);
            GameState state = StartState(bot);
            List<Entry> made = [];
            List<string> stepAtRead = [];
            PlanEvent plan = Event(provider, WaitAtNight(), Hunt(made, (_, _, _, _) => EventState.Waiting));
            clock.OnRead = _ => stepAtRead.Add(plan.StepName);

            await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(3, clock.Reads);
            Assert.Equal([DefaultLeg], made.Select(m => m.Leg));
            Assert.Equal(["", Plan.Wait, Plan.Wait], stepAtRead);
            Assert.Empty(bot.Macros);
            Assert.Empty(pressed);
            Assert.Equal([Route32], Follows(pushes));
        }

        [Fact]
        public async Task Wait_ClockStale_ParksWithTheSentence_ResumeReentersThePlan()
        {
            var (provider, _) = await ProviderAsync();
            Clock clock = new(21);
            GameState state = StartState(BotWith(clock, []));
            List<Entry> made = [];
            PlanEvent plan = Event(provider, WaitAtNight(), Hunt(made, (_, _, _, _) => EventState.Waiting), stale: TimeSpan.Zero);

            EventState parked = await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Waiting, parked);
            Assert.Equal([EventState.Waiting], state.State);
            Assert.Empty(made);

            clock.Hours[0] = 4;
            state.SwapState(EventState.Resume);
            await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Entry hunt = Assert.Single(made);
            Assert.Same(DefaultLeg, hunt.Leg);
            Assert.Equal([EventState.Init], hunt.Stack);
        }

        [Fact]
        public async Task Wait_Cancelled_Ends()
        {
            var (provider, _) = await ProviderAsync();
            using CancellationTokenSource cts = new();
            Clock clock = new(21) { OnRead = _ => cts.Cancel() };
            GameState state = StartState(BotWith(clock, []));
            List<Entry> made = [];
            PlanEvent plan = Event(provider, WaitAtNight(), Hunt(made, (_, _, _, _) => EventState.Waiting));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plan.ExecuteWithResultAsync(state, cts.Token));

            Assert.Empty(made);
        }

        // Default 9: with endWhenComplete, the plan ends once every shiny-hunt step's hunt has ended on its own.
        [Fact]
        public async Task EndWhenComplete_EveryShinyStepDone_EndsThePlan()
        {
            var (provider, _) = await ProviderAsync();
            GameState state = StartState(BotWith(new Clock(9), []));
            List<Entry> made = [];
            PlanEvent plan = Event(provider, MorningOn30(endWhenComplete: true), Hunt(made, (_, _, _, _) => EventState.Complete));

            EventState result = await plan.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Complete, result);
            Assert.Equal([MorningLeg, DefaultLeg], made.Select(m => m.Leg));
            Assert.Equal([EventState.Complete], state.State);
        }

        // Default 9: without it, nothing open is a Wait, which reads the clock again until cancelled.
        [Fact]
        public async Task EndWhenComplete_False_NothingOpen_Waits()
        {
            var (provider, _) = await ProviderAsync();
            using CancellationTokenSource cts = new();
            Clock clock = new(9) { OnRead = reads => { if (reads == 4) cts.Cancel(); } };
            GameState state = StartState(BotWith(clock, []));
            List<Entry> made = [];
            PlanEvent plan = Event(provider, MorningOn30(), Hunt(made, (_, _, _, _) => EventState.Complete));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plan.ExecuteWithResultAsync(state, cts.Token));

            Assert.Equal([MorningLeg, DefaultLeg], made.Select(m => m.Leg));
            Assert.Equal(4, clock.Reads);
            Assert.Equal(Plan.Wait, plan.StepName);
        }

        // Default 7: a step's Start refusal is re-thrown with the step's label and its own type, so it stays a 400. Plan Start
        // itself loads the plan from Mongo, so it is not driven here.
        [Fact]
        public async Task StartPlan_StepRefusal_NamesTheStepAndKeepsItsType()
        {
            var argument = await Assert.ThrowsAsync<ArgumentException>(() => BotService.RefusedAsStepAsync<int>("The default",
                () => throw new ArgumentException("No Pokémon in the party has Pickup.")));
            var invalid = await Assert.ThrowsAsync<InvalidOperationException>(() => BotService.RefusedAsStepAsync<int>("Rule 2",
                () => throw new InvalidOperationException("The anchor is not set.")));

            Assert.Equal("The default: No Pokémon in the party has Pickup.", argument.Message);
            Assert.Equal("Rule 2: The anchor is not set.", invalid.Message);
            Assert.Equal(7, await BotService.RefusedAsStepAsync("Rule 1", () => Task.FromResult(7)));
        }
    }
}
