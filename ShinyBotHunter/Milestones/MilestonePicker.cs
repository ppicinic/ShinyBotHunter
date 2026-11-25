using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    // One entry of the bot control's milestone picker: a generated milestone, or a custom phase with Modality null and its name as Id.
    // Anchor is where its hunt starts on this file (GameFile.AnchorFor), null when hunted where the bot stands; AnchorSet is true
    // when the file stores a replacement.
    public record PickerMilestone(string Id, string Name, Modality? Modality, bool Huntable, bool Complete, bool Archived, string? Order,
        Anchor? Anchor, bool AnchorSet);

    public static class MilestonePicker
    {
        // FR-15 to FR-18: every generated milestone of the file's version (each has at least one row, since every slot species is
        // one), and every custom phase assigned to the file or to no file and not linked on it (FR-44); labelled entries first by
        // label, then by name.
        public static List<PickerMilestone> List(IReadOnlyList<Milestone> generated, IEnumerable<PhaseInfo> phases, GameFile file,
            IReadOnlySet<string> assignedAnywhere, IReadOnlyList<CaughtPokemon> caught)
        {
            var fileCaught = caught.Where(c => c.GameFileName == file.Id).ToList();
            var custom = phases.Where(p => (file.Assigned.Contains(p.Name) || !assignedAnywhere.Contains(p.Name)) && file.Links.All(l => l.Phase != p.Name))
                .Select(p => Entry(file, p.Name, p.Name, null, null, true, OrchestratorEvent.IsPhaseComplete(p, fileCaught)));
            var milestones = generated.Select(m => Entry(file, m.Id, m.Name, m.Modality, m, m.Huntable, OrchestratorEvent.IsGeneratedComplete(m, file, caught)));
            return custom.Concat(milestones)
                .OrderBy(e => e.Order is null)
                .ThenBy(e => e.Order, Natural)
                .ThenBy(e => e.Name, Natural)
                .ToList();
        }

        private static PickerMilestone Entry(GameFile file, string id, string name, Modality? modality, Milestone? milestone, bool huntable, bool complete)
            => new(id, name, modality, huntable, complete, file.Archived.Contains(id), file.Order.FirstOrDefault(l => l.Milestone == id)?.Label,
                file.AnchorFor(id, milestone)?.Anchor, file.Anchors.Any(a => a.Milestone == id));

        private static readonly NaturalComparer Natural = new();

        // Digit runs compare as numbers, so "1-4" comes before "1-10"; other characters compare ignoring case.
        private sealed class NaturalComparer : IComparer<string?>
        {
            public int Compare(string? a, string? b)
            {
                if (a is null || b is null)
                {
                    return (a is null).CompareTo(b is null);
                }
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
                    {
                        int startA = i, startB = j;
                        while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                        while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                        string numberA = a[startA..i].TrimStart('0'), numberB = b[startB..j].TrimStart('0');
                        int byNumber = numberA.Length != numberB.Length ? numberA.Length.CompareTo(numberB.Length) : string.CompareOrdinal(numberA, numberB);
                        if (byNumber != 0) return byNumber;
                    }
                    else
                    {
                        int byChar = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                        if (byChar != 0) return byChar;
                        i++;
                        j++;
                    }
                }
                int byRest = (a.Length - i).CompareTo(b.Length - j);
                return byRest != 0 ? byRest : string.CompareOrdinal(a, b);
            }
        }
    }
}
