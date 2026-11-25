using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Milestones
{
    // One line of a plan (FR-31): at these times of day (Morning, Day or Night) on these weekdays, hunt Milestone, a generated
    // milestone id or a custom phase name. An empty Times or Weekdays matches any value.
    public record PlanRule(List<SlotCondition> Times, List<DayOfWeek> Weekdays, string Milestone);

    // A saved plan (FR-37a): one document per game and name in HuntPlans, shared between that game's files. Mongo adds the _id.
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

        // The first matching rule whose milestone is not complete wins, then the default if it is not complete; null when
        // neither is left, which ends the hunt (FR-34).
        public string? Pick(DayOfWeek weekday, int hour, IReadOnlySet<string> complete)
        {
            var time = TimeOfDay(hour);
            var rule = Rules.FirstOrDefault(r => (r.Times.Count == 0 || r.Times.Contains(time))
                && (r.Weekdays.Count == 0 || r.Weekdays.Contains(weekday))
                && !complete.Contains(r.Milestone));
            return rule?.Milestone ?? (complete.Contains(Default) ? null : Default);
        }
    }
}
