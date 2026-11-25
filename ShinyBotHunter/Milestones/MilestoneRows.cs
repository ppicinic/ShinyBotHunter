using ShinyBotHunter.Games;
using ShinyBotHunter.Persistence.MongoDb;

namespace ShinyBotHunter.Milestones
{
    // One line of a milestone seen through a game file's goal (FR-10, FR-12). Caught is global per file (FR-9).
    public record MilestoneRow(int Species, string Key, string? FormCode, int Wanted, int Caught, bool Hidden)
    {
        public bool Complete => Wanted > 0 && Caught >= Wanted;

        public bool IsTarget => Wanted > 0 && Caught < Wanted && !Hidden;
    }

    public static class MilestoneRows
    {
        // Every slot species is a row, in slot order: one per form entry where the goal has form entries for it, otherwise one at
        // its any-form entry's amount, or at 0 when the goal has no entry.
        public static List<MilestoneRow> Build(Milestone milestone, GameFile gameFile, IEnumerable<CaughtPokemon> caught)
        {
            var rows = new List<MilestoneRow>();
            foreach (int species in milestone.Slots.Select(s => s.Species).Distinct())
            {
                var entries = gameFile.Entries.Where(e => e.Species == species).ToList();
                var forms = entries.Where(e => e.FormCode is not null).ToList();
                IEnumerable<GoalEntry> rowEntries = forms.Count > 0 ? forms : [entries.SingleOrDefault() ?? new GoalEntry(species, null, 0)];
                rows.AddRange(rowEntries.Select(e => new MilestoneRow(species, e.Key, e.FormCode, e.Amount, e.CaughtIn(caught, gameFile.Id),
                    gameFile.Hidden.Contains(new HiddenRow(milestone.Id, e.Key)))));
            }
            return rows;
        }

        // FR-27 and FR-28: each slot species' share now, the reason a share is zero, and the survive share. Live, the inputs the
        // decision last read; otherwise the last-seen time of day with no Repel and no lead. Null with no rate rule, on headbutt
        // (the tree the bot faces is not known), and when never seen on a milestone whose slots depend on the time of day.
        internal static (IReadOnlyDictionary<int, (double Share, string? Reason)> Species, double Survive)? Shares(Milestone m,
            Func<Milestone, SlotCondition, int?, byte, int, bool, (IReadOnlyDictionary<int, double> Shares, double Survive)>? rates,
            (RateInputs Inputs, bool Live)? seen)
        {
            SlotCondition[] timed = [SlotCondition.Morning, SlotCondition.Day, SlotCondition.Night, SlotCondition.NightFishing];
            if (rates is null || m.Modality == Modality.Headbutt || (seen is null && m.Slots.Any(s => timed.Contains(s.Condition))))
            {
                return null;
            }
            SlotCondition cond = seen is { } s ? HuntPlan.TimeOfDay(s.Inputs.Hour) : SlotCondition.None;
            RateInputs? live = seen is { Live: true } l ? l.Inputs : null;
            var (shares, survive) = live is null
                ? rates(m, cond, null, 0, 1, false)
                : rates(m, cond, live.RepelLevel, live.LeadAbility, live.LeadLevel, live.LeadIsEgg);
            // Blocked by Repel only where the rule, run without Repel, gives the species a share.
            var plain = live?.RepelLevel is null ? shares : rates(m, cond, null, live.LeadAbility, live.LeadLevel, live.LeadIsEgg).Shares;
            int[] nightFishing = m.Slots.Where(s => s.Condition == SlotCondition.NightFishing).Select(s => s.Index).ToArray();
            return (m.Slots.GroupBy(s => s.Species).ToDictionary(g => g.Key, g =>
            {
                double share = shares.GetValueOrDefault(g.Key);
                return (share, share > 0 ? null : plain.GetValueOrDefault(g.Key) > 0 ? "blocked by Repel" : Reason(g.ToList(), cond, nightFishing));
            }), survive);
        }

        // Why a species' share is zero, from its slots. At night a None slot whose index has a NightFishing slot is replaced by
        // it, as HgssRates takes the rods' slots, so such a slot counts for morning and day only.
        private static string? Reason(List<Slot> slots, SlotCondition cond, int[] nightFishing)
        {
            bool present = slots.Any(s => s.Condition == cond
                || (s.Condition == SlotCondition.None && !(cond == SlotCondition.Night && nightFishing.Contains(s.Index)))
                || (cond == SlotCondition.Night && s.Condition == SlotCondition.NightFishing));
            if (present)
            {
                return null;
            }
            var conditions = slots.Select(s => s.Condition).ToHashSet();
            var times = new[] { SlotCondition.Morning, SlotCondition.Day, SlotCondition.Night }
                .Where(t => conditions.Contains(t) || (t != SlotCondition.Night && conditions.Contains(SlotCondition.None))).ToList();
            if (times.Count > 0)
            {
                return $"{string.Join(" and ", times.Select(t => t.ToString().ToLowerInvariant()))} only";
            }
            return conditions.SetEquals([SlotCondition.HoennSound]) ? "needs Hoenn Sound"
                : conditions.SetEquals([SlotCondition.SinnohSound]) ? "needs Sinnoh Sound"
                : conditions.SetEquals([SlotCondition.NightFishing]) ? "night only"
                : null;
        }
    }
}
