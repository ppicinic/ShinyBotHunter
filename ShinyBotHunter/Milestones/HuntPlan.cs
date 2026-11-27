using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
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

        // Morning 4 to 10, day 10 to 20, night 20 to 4: HgssRates' time of day, with evening counted as day.
        public static SlotCondition TimeOfDay(int hour) => hour switch
        {
            >= 4 and < 10 => SlotCondition.Morning,
            >= 10 and < 20 => SlotCondition.Day,
            _ => SlotCondition.Night
        };

        // The first rule whose times and weekdays match and whose step is not done, else the default if it is not done, else
        // null. Done holds the steps whose hunt ended on its own in this run.
        public NewEventArguments? Pick(DayOfWeek weekday, int hour, IReadOnlySet<NewEventArguments> done)
        {
            var time = TimeOfDay(hour);
            return Rules.FirstOrDefault(r => (r.Times.Count == 0 || r.Times.Contains(time)) && (r.Weekdays.Count == 0 || r.Weekdays.Contains(weekday))
                && !done.Contains(r.Arguments))?.Arguments ?? (done.Contains(Default) ? null : Default);
        }
    }
}
