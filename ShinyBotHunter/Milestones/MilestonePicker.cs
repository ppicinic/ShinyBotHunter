using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Games;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    // One entry of the bot control's milestone picker: a saved milestone, with its location's modality on this file's version, or
    // null with no location. Anchor is where its hunt starts on this file (GameFile.AnchorFor), null when hunted where the bot
    // stands; AnchorSet is true when the file stores a replacement under its name.
    public record PickerMilestone(string Name, Modality? Modality, bool Huntable, bool Complete, Anchor? Anchor, bool AnchorSet);

    public static class MilestonePicker
    {
        // FR-15: the saved milestones ticked for the file, or for no file, that are not archived, in natural name order. A null
        // trainerId gives a headbutt milestone no generated anchor.
        public static List<PickerMilestone> List(IEnumerable<PhaseInfo> phases, IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? milestones,
            RomVersion? version, GameFile file, IReadOnlyList<CaughtPokemon> caught, uint? trainerId)
        {
            var fileCaught = caught.Where(c => c.GameFileName == file.Id).ToList();
            return phases.Where(p => !p.Archived && (p.GameFiles.Count == 0 || p.GameFiles.Contains(file.Id)))
                .Select(p => Entry(p, milestones, version, file, fileCaught, trainerId))
                .OrderBy(e => e.Name, Natural)
                .ToList();
        }

        private static PickerMilestone Entry(PhaseInfo phase, IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? milestones, RomVersion? version,
            GameFile file, List<CaughtPokemon> fileCaught, uint? trainerId)
        {
            Milestone? located = PhaseTarget.FindMilestone(milestones, version, phase.Location);
            var rows = MilestoneRows.Counted(phase, milestones, version);
            return new(phase.Name, located?.Modality, located?.Huntable ?? true, OrchestratorEvent.IsPhaseComplete(rows, fileCaught),
                file.AnchorFor(phase.Name, located, trainerId, MilestoneRows.Wanted(rows, fileCaught))?.Anchor, file.Anchors.Any(a => a.Milestone == phase.Name));
        }

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
