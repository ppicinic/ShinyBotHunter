using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    // One line of a plan (FR-31): at these times of day (Morning, Day or Night) on these weekdays, hunt Milestone, a generated
    // milestone id or a custom phase name. An empty Times or Weekdays matches any value.
    public record PlanRule(List<SlotCondition> Times, List<DayOfWeek> Weekdays, string Milestone);

    // The plan shape before the rework, no longer stored: kept only for HuntPlanRun until R6b deletes it with that run.
    [BsonIgnoreExtraElements]
    public record HuntPlan(GameFlavor Game, string Name, string Default, List<PlanRule> Rules)
    {
        // Morning 4 to 10, day 10 to 20, night 20 to 4: HgssRates' time of day, with evening counted as day.
        public static SlotCondition TimeOfDay(int hour) => hour switch
        {
            >= 4 and < 10 => SlotCondition.Morning,
            >= 10 and < 20 => SlotCondition.Day,
            _ => SlotCondition.Night
        };

        // Every milestone the plan names, the default first.
        public IEnumerable<string> Milestones => Rules.Select(r => r.Milestone).Prepend(Default).Distinct();

        // The first matching rule whose milestone is not complete wins, then the default if it is not complete, then the first
        // rule in order whose milestone is not complete, whatever its times and weekdays; null only when every milestone is
        // complete, which ends the hunt (FR-34).
        public string? Pick(DayOfWeek weekday, int hour, IReadOnlySet<string> complete)
        {
            var time = TimeOfDay(hour);
            var rule = Rules.FirstOrDefault(r => (r.Times.Count == 0 || r.Times.Contains(time))
                && (r.Weekdays.Count == 0 || r.Weekdays.Contains(weekday))
                && !complete.Contains(r.Milestone));
            return rule?.Milestone ?? Milestones.FirstOrDefault(m => !complete.Contains(m));
        }
    }

    // One rule of a plan: at these times of day and weekdays (an empty list matches any), run this action's body.
    public record PlanStep(List<SlotCondition> Times, List<DayOfWeek> Weekdays, NewEventArguments Arguments);

    // A saved plan: one document per game and name in Plans, shared between that game's files. Mongo adds the _id. Each step's
    // body is the one Bot Control posts to bot/startAction for that action; EndWhenComplete is stored here and run by R6b.
    [BsonIgnoreExtraElements]
    public record Plan(GameFlavor Game, string Name, List<PlanStep> Rules, NewEventArguments Default, bool EndWhenComplete)
    {
        // A step that does nothing until the next rule matches. Not a registry entry, so Bot Control never lists it.
        public const string Wait = "Wait";

        // The actions a step may be; adding a type is adding its registry name here.
        public static readonly string[] StepActions = ["Wild Encounter", Wait];

        // The first rule whose times and weekdays match, else the default.
        public NewEventArguments Pick(DayOfWeek weekday, int hour)
        {
            var time = HuntPlan.TimeOfDay(hour);
            return Rules.FirstOrDefault(r => (r.Times.Count == 0 || r.Times.Contains(time)) && (r.Weekdays.Count == 0 || r.Weekdays.Contains(weekday)))
                ?.Arguments ?? Default;
        }
    }
}
