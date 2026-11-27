using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class HuntPlanTests
    {
        private static readonly HashSet<string> None = [];

        private static HuntPlan Plan(params PlanRule[] rules) => new(GameFlavor.HGSS, "Johto", "36-land", [.. rules]);

        private static PlanRule Rule(string milestone, SlotCondition[] times, DayOfWeek[] weekdays) => new([.. times], [.. weekdays], milestone);

        [Theory]
        [InlineData(3, SlotCondition.Night)]
        [InlineData(4, SlotCondition.Morning)]
        [InlineData(9, SlotCondition.Morning)]
        [InlineData(10, SlotCondition.Day)]
        [InlineData(19, SlotCondition.Day)]
        [InlineData(20, SlotCondition.Night)]
        [InlineData(23, SlotCondition.Night)]
        [InlineData(0, SlotCondition.Night)]
        public void TimeOfDay_Boundaries(int hour, SlotCondition expected)
        {
            Assert.Equal(expected, HuntPlan.TimeOfDay(hour));
        }

        [Fact]
        public void Pick_FirstMatchingRuleWins()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []), Rule("33-land", [SlotCondition.Night], []));

            Assert.Equal("34-land", plan.Pick(DayOfWeek.Monday, 21, None));
        }

        [Fact]
        public void Pick_NoRuleMatches_IsTheDefault()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []));

            Assert.Equal("36-land", plan.Pick(DayOfWeek.Monday, 12, None));
        }

        [Fact]
        public void Pick_Weekday_MatchesOnlyItsDay()
        {
            var plan = Plan(Rule("Union Cave", [], [DayOfWeek.Friday]));

            Assert.Equal("Union Cave", plan.Pick(DayOfWeek.Friday, 12, None));
            Assert.Equal("36-land", plan.Pick(DayOfWeek.Thursday, 12, None));
        }

        [Fact]
        public void Pick_EmptySets_MatchAny()
        {
            var plan = Plan(Rule("34-land", [], []));

            Assert.Equal("34-land", plan.Pick(DayOfWeek.Sunday, 0, None));
            Assert.Equal("34-land", plan.Pick(DayOfWeek.Wednesday, 12, None));
        }

        [Fact]
        public void Pick_CompleteRule_IsSkipped()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []), Rule("33-land", [SlotCondition.Night], []));

            Assert.Equal("33-land", plan.Pick(DayOfWeek.Monday, 21, new HashSet<string> { "34-land" }));
            Assert.Equal("36-land", plan.Pick(DayOfWeek.Monday, 21, new HashSet<string> { "34-land", "33-land" }));
        }

        [Fact]
        public void Pick_EveryMilestoneComplete_IsNull()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []));

            Assert.Null(plan.Pick(DayOfWeek.Monday, 21, new HashSet<string> { "36-land", "34-land" }));
            Assert.Null(plan.Pick(DayOfWeek.Monday, 12, new HashSet<string> { "36-land", "34-land" }));
        }

        [Fact]
        public void Pick_DefaultCompleteNoRuleMatches_IsFirstOpenRule()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []), Rule("Union Cave", [], [DayOfWeek.Friday]),
                Rule("33-land", [SlotCondition.Night], []));

            Assert.Equal("34-land", plan.Pick(DayOfWeek.Monday, 12, new HashSet<string> { "36-land" }));
            Assert.Equal("Union Cave", plan.Pick(DayOfWeek.Monday, 12, new HashSet<string> { "36-land", "34-land" }));
        }

        [Fact]
        public void Pick_DefaultComplete_MatchingOpenRuleBeatsStandIn()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []), Rule("Union Cave", [], [DayOfWeek.Friday]));

            Assert.Equal("Union Cave", plan.Pick(DayOfWeek.Friday, 12, new HashSet<string> { "36-land" }));
            Assert.Equal("34-land", plan.Pick(DayOfWeek.Monday, 12, new HashSet<string> { "36-land" }));
        }

        [Fact]
        public void Pick_DefaultComplete_MatchingOpenRule_StillPicked()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []));

            Assert.Equal("34-land", plan.Pick(DayOfWeek.Monday, 21, new HashSet<string> { "36-land" }));
        }

        [Fact]
        public void Milestones_DefaultFirst_Distinct()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []), Rule("36-land", [SlotCondition.Morning], []),
                Rule("34-land", [], [DayOfWeek.Friday]));

            Assert.Equal(["36-land", "34-land"], plan.Milestones);
        }

        private static NewEventArguments Wild(string milestone) => new()
        {
            EventName = "Wild Encounter", PhaseName = milestone, TargetType = "ShinyTarget", EncounterType = EncounterType.Run,
        };

        private static NewEventArguments WaitStep() => new() { EventName = "Wait" };

        private static PlanStep Step(SlotCondition[] times, DayOfWeek[] weekdays, NewEventArguments arguments) => new([.. times], [.. weekdays], arguments);

        private static Plan Steps(NewEventArguments @default, params PlanStep[] rules) => new(GameFlavor.HGSS, "Johto nights", [.. rules], @default, false);

        [Fact]
        public void PlanPick_FirstMatchingRuleWins()
        {
            var first = Step([SlotCondition.Night], [], Wild("1-4 Route 30"));
            var plan = Steps(Wild("2-1 Route 32"), first, Step([SlotCondition.Night], [], Wild("1-5 Route 31")));

            Assert.Same(first.Arguments, plan.Pick(DayOfWeek.Monday, 21));
        }

        [Fact]
        public void PlanPick_NoRuleMatches_IsTheDefault()
        {
            var plan = Steps(Wild("2-1 Route 32"), Step([SlotCondition.Night], [], Wild("1-4 Route 30")));

            Assert.Same(plan.Default, plan.Pick(DayOfWeek.Monday, 12));
        }

        [Fact]
        public void PlanPick_Weekday_MatchesOnlyItsDay()
        {
            var friday = Step([], [DayOfWeek.Friday], Wild("Union Cave"));
            var plan = Steps(Wild("2-1 Route 32"), friday);

            Assert.Same(friday.Arguments, plan.Pick(DayOfWeek.Friday, 12));
            Assert.Same(plan.Default, plan.Pick(DayOfWeek.Thursday, 12));
        }

        [Fact]
        public void PlanPick_EmptyLists_MatchAny()
        {
            var any = Step([], [], Wild("1-4 Route 30"));
            var plan = Steps(Wild("2-1 Route 32"), any);

            Assert.Same(any.Arguments, plan.Pick(DayOfWeek.Sunday, 0));
            Assert.Same(any.Arguments, plan.Pick(DayOfWeek.Wednesday, 12));
        }

        [Theory]
        [InlineData(3, false)]
        [InlineData(4, true)]
        [InlineData(9, true)]
        [InlineData(10, false)]
        public void PlanPick_TimeBoundaries(int hour, bool matches)
        {
            var morning = Step([SlotCondition.Morning], [], Wild("1-4 Route 30"));
            var plan = Steps(Wild("2-1 Route 32"), morning);

            Assert.Same(matches ? morning.Arguments : plan.Default, plan.Pick(DayOfWeek.Monday, hour));
        }

        [Fact]
        public void Plan_BsonRoundTrip()
        {
            var run = Wild("1-4 Route 30");
            run.xRange = 2;
            run.TrainType = TrainType.Standard;
            run.Pickup = true;
            var plan = new Plan(GameFlavor.HGSS, "Johto nights",
                [Step([SlotCondition.Night], [], run), Step([SlotCondition.Morning], [DayOfWeek.Friday, DayOfWeek.Saturday], WaitStep())],
                Wild("2-1 Route 32"), true);
            var document = plan.ToBsonDocument();
            document.InsertAt(0, new BsonElement("_id", ObjectId.GenerateNewId()));

            var read = BsonSerializer.Deserialize<Plan>(document);

            Assert.Equal((plan.Game, plan.Name, plan.EndWhenComplete), (read.Game, read.Name, read.EndWhenComplete));
            Assert.Equal(2, read.Rules.Count);
            Assert.Equal([SlotCondition.Night], read.Rules[0].Times);
            Assert.Empty(read.Rules[0].Weekdays);
            NewEventArguments readRun = read.Rules[0].Arguments;
            Assert.Equal(("Wild Encounter", "1-4 Route 30", "ShinyTarget", EncounterType.Run), (readRun.EventName, readRun.PhaseName, readRun.TargetType, readRun.EncounterType));
            Assert.Equal((2, (int?)null, TrainType.Standard, true), (readRun.xRange, readRun.yRange, readRun.TrainType, readRun.Pickup));
            Assert.Equal([SlotCondition.Morning], read.Rules[1].Times);
            Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday], read.Rules[1].Weekdays);
            Assert.Equal("Wait", read.Rules[1].Arguments.EventName);
            Assert.Null(read.Rules[1].Arguments.PhaseName);
            Assert.Equal(("Wild Encounter", "2-1 Route 32"), (read.Default.EventName, read.Default.PhaseName));
        }

        [Fact]
        public void Plan_BsonRead_IgnoresAnArgumentFieldNoLongerDefined()
        {
            var document = Steps(Wild("2-1 Route 32"), Step([], [], WaitStep())).ToBsonDocument();
            document.InsertAt(0, new BsonElement("_id", ObjectId.GenerateNewId()));
            document["Default"].AsBsonDocument.Add("Removed", "gone");
            document["Rules"][0]["Arguments"].AsBsonDocument.Add("Removed", 3);

            var read = BsonSerializer.Deserialize<Plan>(document);

            Assert.Equal("2-1 Route 32", read.Default.PhaseName);
            Assert.Equal("Wait", read.Rules[0].Arguments.EventName);
        }

        private static readonly GameCapabilities Hgss = HgssProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()), new GraphService()).Capabilities;

        private static string? Error(Plan plan) => PhaseMetadataController.PlanError(plan, Hgss);

        private static string? RuleError(NewEventArguments rule) => Error(Steps(WaitStep(), Step([], [], rule)));

        [Fact]
        public void PlanError_ValidPlan_IsNull()
        {
            var run = Wild("1-4 Route 30");
            run.xRange = 2;
            run.yRange = 1;
            var spinTrained = Wild("1-5 Route 31");
            spinTrained.EncounterType = EncounterType.Spin;
            spinTrained.TrainType = TrainType.Standard;
            spinTrained.TargetLevel = 101;
            spinTrained.Pickup = true;
            var headbuttSwitch = Wild("2-2 Route 33");
            headbuttSwitch.EncounterType = EncounterType.Headbutt;
            headbuttSwitch.TargetType = "PhaseTarget";
            headbuttSwitch.TrainType = TrainType.Switch;
            headbuttSwitch.SwitchSlot = 2;
            var headbuttUntrained = Wild("2-2 Route 33");
            headbuttUntrained.EncounterType = EncounterType.Headbutt;
            headbuttUntrained.TrainType = TrainType.None;

            Assert.Null(Error(Steps(Wild("2-1 Route 32"), Step([SlotCondition.Night], [], run), Step([], [DayOfWeek.Friday], spinTrained),
                Step([SlotCondition.Morning], [], headbuttSwitch), Step([], [], headbuttUntrained), Step([SlotCondition.Day], [], WaitStep()))));
            Assert.Null(Error(Steps(WaitStep(), Step([SlotCondition.Night], [], run))));
        }

        [Theory]
        [InlineData("Chained Encounter")]
        [InlineData("HGSS Primo Egg")]
        [InlineData("HGSS Togepi Egg")]
        [InlineData("HGSS Static Encounter")]
        [InlineData("Launch Bot")]
        public void PlanError_StepNotYet_Refused(string action)
        {
            Assert.Equal($"Rule 1: {action} cannot be a plan step yet; a step is Wild Encounter or Wait.", RuleError(new NewEventArguments { EventName = action }));
        }

        [Fact]
        public void PlanError_UnknownAction_Refused()
        {
            Assert.Equal("Rule 1: Fly is not an action.", RuleError(new NewEventArguments { EventName = "Fly" }));
        }

        [Fact]
        public void PlanError_NoAction_Refused()
        {
            Assert.Equal("Rule 1 has no action.", RuleError(new NewEventArguments { EventName = " " }));
            Assert.Equal("The default has no action.", Error(Steps(null!)));
        }

        [Fact]
        public void PlanError_UndeclaredParameter_Refused()
        {
            var withPlan = Wild("1-4 Route 30");
            withPlan.HuntPlan = "Johto nights";

            Assert.Equal("Rule 1: HuntPlan is not a parameter of Wild Encounter.", RuleError(withPlan));
            Assert.Equal("Rule 1: PhaseName is not a parameter of Wait.", RuleError(new NewEventArguments { EventName = "Wait", PhaseName = "1-4 Route 30" }));
        }

        [Fact]
        public void PlanError_ConditionUnmet_Refused()
        {
            var pickupUntrained = Wild("1-4 Route 30");
            pickupUntrained.TrainType = TrainType.None;
            pickupUntrained.Pickup = true;
            var spinRange = Wild("1-4 Route 30");
            spinRange.EncounterType = EncounterType.Spin;
            spinRange.xRange = 2;

            Assert.Equal("Rule 1: Pickup applies only when TrainType is not None.", RuleError(pickupUntrained));
            Assert.Equal("Rule 1: xRange applies only when EncounterType is Run.", RuleError(spinRange));
        }

        [Fact]
        public void PlanError_MissingRequired_Refused()
        {
            var noType = Wild("1-5 Route 31");
            noType.EncounterType = null;
            var noMilestone = Wild(" ");

            Assert.Equal("Rule 2: Wild Encounter needs EncounterType.", Error(Steps(WaitStep(), Step([], [], Wild("1-4 Route 30")), Step([], [], noType))));
            Assert.Equal("The default: Wild Encounter needs PhaseName.", Error(Steps(noMilestone)));
        }

        [Fact]
        public void PlanError_NoClock_Refused()
        {
            var rs = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>())).Capabilities;

            Assert.Equal("RS has no in-game clock, so it has no plans.", PhaseMetadataController.PlanError(Steps(WaitStep()) with { Game = GameFlavor.RS }, rs));
        }

        // E1: the old Wild Encounter parameter is refused after the clock check, before anything is read.
        [Fact]
        public async Task WildEncounterWithAPlan_IsRefused()
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            var service = new BotService(settings, new FakeBotFactory(settings, Task.CompletedTask, null), null!, null!, null!, null!);
            await service.ExecuteNewBotOrSpecialEvent(new NewEventArguments { EventName = "Launch Bot", GameName = "fake.nds" });
            var args = Wild("1-4 Route 30");
            args.HuntPlan = "Johto nights";

            var result = await new BotController(service).StartAction(0, args);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal("A Wild Encounter no longer takes a plan; start the Plan action with it.", content.Content);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }

        // The in-use check reads only the entry's event, bot and name, so the event never runs.
        private sealed class IdleEvent : IBotEvent
        {
            public Task ExecuteAsync(GameState state, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private static KeyValuePair<int, BotAndOrEvent> Hunt(int pos, string? eventName, bool hasEvent = true, GameProfile? profile = null)
            => new(pos, new BotAndOrEvent(new FakeBot(Task.CompletedTask, profile)) { BotPosition = pos, Event = hasEvent ? new IdleEvent() : null, EventName = eventName });

        [Fact]
        public void PlanInUse_RunningHunt_RefusedWithTheSentence()
        {
            var running = Hunt(2, BotService.PlanEventName("Johto"));
            running.Value.EventTask = new TaskCompletionSource().Task;

            Assert.Equal("Johto is in use by the hunt on bot #2; cancel that hunt first.",
                BotService.PlanInUseRefusal([Hunt(0, null, hasEvent: false), running], GameFlavor.HGSS, "Johto"));
        }

        [Fact]
        public void PlanInUse_ParkedHunt_Refused()
        {
            var parked = Hunt(2, BotService.PlanEventName("Johto"));
            parked.Value.GameState = new GameState();
            parked.Value.GameState.State.Push(EventState.Waiting);

            Assert.Equal("Johto is in use by the hunt on bot #2; cancel that hunt first.",
                BotService.PlanInUseRefusal([parked], GameFlavor.HGSS, "Johto"));
        }

        [Fact]
        public void PlanInUse_FinishedOrOtherPlanOrOtherGame_IsNull()
        {
            var rs = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()));

            // A finished hunt keeps its name but not its event.
            Assert.Null(BotService.PlanInUseRefusal([Hunt(0, BotService.PlanEventName("Johto"), hasEvent: false)], GameFlavor.HGSS, "Johto"));
            Assert.Null(BotService.PlanInUseRefusal([Hunt(0, BotService.PlanEventName("Kanto"))], GameFlavor.HGSS, "Johto"));
            Assert.Null(BotService.PlanInUseRefusal([Hunt(0, "Johto")], GameFlavor.HGSS, "Johto"));
            Assert.Null(BotService.PlanInUseRefusal([Hunt(0, BotService.PlanEventName("Johto"), profile: rs)], GameFlavor.HGSS, "Johto"));
        }

        [Fact]
        public async Task RenamePlan_BlankName_Refused()
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            // No provider: the name is refused before the gate and before Mongo.
            var service = new BotService(settings, new FakeBotFactory(settings, Task.CompletedTask, null), null!, null!, null!, null!);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RenamePlanAsync(GameFlavor.HGSS, "Johto", " "));

            Assert.Equal("The plan needs a name.", ex.Message);
        }

        [Fact]
        public void PlanEventName_IsWhatStartNames()
        {
            Assert.Equal("Plan Johto", BotService.PlanEventName("Johto"));
        }
    }
}
