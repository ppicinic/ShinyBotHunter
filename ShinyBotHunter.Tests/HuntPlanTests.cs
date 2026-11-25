using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;

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
        public void Pick_DefaultCompleteAndNoOpenRule_IsNull()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []));

            Assert.Null(plan.Pick(DayOfWeek.Monday, 12, new HashSet<string> { "36-land" }));
            Assert.Null(plan.Pick(DayOfWeek.Monday, 21, new HashSet<string> { "36-land", "34-land" }));
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

        [Fact]
        public void HuntPlan_BsonRoundTrip()
        {
            var plan = Plan(Rule("34-land", [SlotCondition.Night], []), Rule("Union Cave", [], [DayOfWeek.Friday, DayOfWeek.Saturday]));
            var document = plan.ToBsonDocument();
            document.InsertAt(0, new BsonElement("_id", ObjectId.GenerateNewId()));

            var read = BsonSerializer.Deserialize<HuntPlan>(document);

            Assert.Equal(plan.Game, read.Game);
            Assert.Equal(plan.Name, read.Name);
            Assert.Equal(plan.Default, read.Default);
            Assert.Equal(2, read.Rules.Count);
            Assert.Equal(plan.Rules[0].Times, read.Rules[0].Times);
            Assert.Equal(plan.Rules[1].Weekdays, read.Rules[1].Weekdays);
            Assert.Equal("Union Cave", read.Rules[1].Milestone);
            Assert.False(document.Contains(nameof(HuntPlan.Milestones)));
        }

        [Fact]
        public void PlanError_RefusesAPlanWithoutItsMilestones()
        {
            Assert.Null(PhaseMetadataController.PlanError(Plan(Rule("34-land", [SlotCondition.Night], []))));
            Assert.Equal("The plan has no default milestone", PhaseMetadataController.PlanError(Plan() with { Default = " " }));
            Assert.Equal("Rule 2 names no milestone",
                PhaseMetadataController.PlanError(Plan(Rule("34-land", [SlotCondition.Night], []), Rule("", [], [DayOfWeek.Friday]))));
        }
    }
}
