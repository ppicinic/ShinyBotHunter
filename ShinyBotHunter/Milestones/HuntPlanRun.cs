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

        // FR-33 and FR-33a, after the decision's clock read: true when the step took the stack, to walk or to park.
        internal async Task<bool> StepAsync(GameState state, Func<string, Task> follow, Func<GameState, Task> updateMilestone, CancellationToken ct)
        {
            string? pick = Plan.Pick((DayOfWeek)state.RatesSeen!.Weekday, state.RatesSeen.Hour, Complete);
            if (pick is null)
            {
                // FR-34: nothing is left, so the decision's finish check ends the hunt.
                state.MilestoneComplete = true;
                return false;
            }
            if (!Arrived && pick != state.Milestone)
            {
                await SwitchToAsync(state, pick, follow, updateMilestone);
            }
            if (pick == state.Milestone && Pending is null)
            {
                return false;
            }
            var (_, stopAnchor, name) = Stops[pick];
            Anchor anchor = stopAnchor.Anchor;
            int map = await state.Bot.Overworld.GetMapIdAsync(ct);
            var (x, y) = await state.Bot.Overworld.GetPositionAsync(ct);
            if (map == anchor.Map && x == anchor.X && y == anchor.Y)
            {
                // FR-36: the walk's encounters were logged under the milestone left; from here they are this one's.
                if (pick != state.Milestone)
                {
                    await SwitchToAsync(state, pick, follow, updateMilestone);
                }
                OrchestratorEvent.UseAnchor(state, stopAnchor);
                Pending = null;
                Arrived = true;
                WalkEnded = false;
                return false;
            }
            if (Pending == pick && WalkEnded)
            {
                Console.WriteLine($"[Orchestrator] {name}: the walk ended at map {map} ({x}, {y}), not on the anchor ({anchor.X}, {anchor.Y}).");
                state.SwapState(EventState.Waiting);
                WalkEnded = false;
                return true;
            }
            Pending = pick;
            WalkEnded = false;
            OrchestratorEvent.UseAnchor(state, stopAnchor);
            // A walk parked on the way left its route behind; this one is planned from where the player stands.
            state.NavigationPath = null;
            state.NavigationPathIndex = 0;
            state.SwapState(EventState.ReturnToStart);
            Console.WriteLine($"[Orchestrator] Plan: {(DayOfWeek)state.RatesSeen.Weekday} {state.RatesSeen.Hour}h names {name}; walking from {state.Milestone} to map {anchor.Map} ({anchor.X}, {anchor.Y})");
            return true;
        }

        private async Task SwitchToAsync(GameState state, string pick, Func<string, Task> follow, Func<GameState, Task> updateMilestone)
        {
            state.Milestone = pick;
            state.Target = Stops[pick].Leg.Target;
            await updateMilestone(state);
            await follow(pick);
            // Built from the start tile on first use, and this milestone's anchor is another tile.
            state.EncounterMacros.Remove("Run");
            Console.WriteLine($"[Orchestrator] Plan: now hunting {Stops[pick].Name}");
        }
    }
}
