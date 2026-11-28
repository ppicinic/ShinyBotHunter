using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Games;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    public static class MilestoneRows
    {
        // The saved milestone's rows that count on a file of this version: at its location, a row whose species only another
        // version has there is left out (default 5). With no location, an unscanned file or no generated data, every row counts.
        public static List<PhasePokemonInfo> Counted(PhaseInfo phase, IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? milestones, RomVersion? version)
        {
            if (PhaseTarget.FindMilestone(milestones, version, phase.Location) is not { } here)
            {
                return phase.Pokemon;
            }
            var anyVersion = milestones!.Values.SelectMany(v => v).Where(m => m.Id == here.Id).SelectMany(m => m.Slots).Select(s => s.Species).ToHashSet();
            return phase.Pokemon.Where(r => here.Slots.Any(s => s.Species == r.SpeciesId) || !anyVersion.Contains(r.SpeciesId)).ToList();
        }

        // A row is short while the file has fewer shiny catches of it than its amount; fileCaught is the file's catches.
        public static bool Short(PhasePokemonInfo row, IEnumerable<CaughtPokemon> fileCaught)
            => fileCaught.Count(x => x.Pokemon.IsShiny && (row.RequireSpecificForm ? x.Pokemon.FormCode == row.FormCode : x.Pokemon.Name == row.Name)) < row.Amount;

        // The species of the rows still short, which pick the file's headbutt tree.
        public static HashSet<int> Wanted(IEnumerable<PhasePokemonInfo> rows, IEnumerable<CaughtPokemon> fileCaught)
            => rows.Where(r => Short(r, fileCaught)).Select(r => r.SpeciesId).ToHashSet();

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
            SlotCondition cond = seen?.Inputs.Hour is int h ? Plan.TimeOfDay(h) : SlotCondition.None;
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
