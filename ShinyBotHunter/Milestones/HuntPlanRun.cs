using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;

namespace ShinyBotHunter.Milestones
{
    // A saved plan as one Wild Encounter resolved it at Start (FR-37a): each milestone's leg, anchor and name, and the move
    // in flight between them.
    public sealed class HuntPlanRun(HuntPlan plan, IReadOnlyDictionary<string, (EventLeg Leg, MilestoneAnchor Anchor, string Name)> stops)
    {
        public HuntPlan Plan { get; } = plan;
        public IReadOnlyDictionary<string, (EventLeg Leg, MilestoneAnchor Anchor, string Name)> Stops { get; } = stops;

        // Pending, Complete, Arrived and WalkEnded are written and read only by the orchestrator's one loop, in sequence; Resume
        // restarts that loop only after it has returned at Waiting, so there is no lock. Complete is replaced, never mutated.
        // The milestone a walk is owed to, null once the bot stands on the hunted milestone's anchor.
        public string? Pending { get; set; } = plan.Default;
        public IReadOnlySet<string> Complete { get; set; } = new HashSet<string>();
        // False until the first commit, so the first decision's pick becomes the milestone before any walk (binding answer 6).
        public bool Arrived { get; set; }
        // Set when the navigator finished a plan walk, so a walk that ended off the anchor parks instead of walking again.
        public bool WalkEnded { get; set; }
    }
}
