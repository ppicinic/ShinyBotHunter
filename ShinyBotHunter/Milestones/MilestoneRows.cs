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
    }
}
