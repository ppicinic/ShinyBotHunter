using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    // FR-43: moving an existing phase onto the generated data, as a link the owner makes on one file.
    public static class Backfill
    {
        // The research doc's mapping of production's wild phases to generated milestones, with the owner's answers for Ruins of Alph
        // (the underground hall), Dark Cave (the Route 31 side) and Slowpoke Well (B1F). Each id has the same maps in HeartGold
        // and SoulSilver. A literal table, because the names are a closed set and a name rule gets three of nine places wrong.
        private static readonly Dictionary<string, string> Suggestions = new()
        {
            ["1-2 Route 29"] = "33-land",
            ["1-2c Route 29"] = "33-land",
            ["1-3 Route 46"] = "48-land",
            ["1-3b Route 46"] = "48-land",
            ["1-4 Route 30"] = "34-land",
            ["1-4b Route 30"] = "34-land",
            ["1-4c Route 30"] = "34-land",
            ["1-6 Dark Cave"] = "176-land",
            ["1-6b Dark Cave"] = "176-land",
            ["1-8 Route 32"] = "36-land",
            ["1-8b Route 32"] = "36-land",
            ["1-9 Sprout Tower"] = "155-land",
            ["1-10 Ruins of Alph"] = "315-land",
            ["2-2c Slowpoke Well"] = "177-land",
            ["2-3c Azalea Town Headbutt"] = "74-headbutt",
        };

        // The suggested milestone id for an existing phase, or null for a phase with none (training, starters, gifts).
        public static string? Suggestion(string phase) => Suggestions.GetValueOrDefault(phase);

        // FR-45: per species of the phase, in phase order, the phase's amounts as goal entries beside the goal's own for it.
        public static List<CopyOffer> Offer(PhaseInfo phase, GameFile file)
            => PhaseEntries(phase).GroupBy(e => e.Species)
                .Select(g => new CopyOffer(g.Key, g.ToList(), file.Entries.Where(e => e.Species == g.Key).ToList()))
                .ToList();

        // The goal with each chosen species' entries replaced by the phase's; every other entry is kept as it is.
        public static List<GoalEntry> ApplyCopy(IReadOnlyList<GoalEntry> goal, PhaseInfo phase, IReadOnlyCollection<int> chosen)
            => [.. goal.Where(e => !chosen.Contains(e.Species)), .. PhaseEntries(phase).Where(e => chosen.Contains(e.Species))];

        // A row that requires its form is a form entry, any other an any-form entry. A species with an any-form row leaves out its
        // form rows at 0 (production's Unown in "1-10 Ruins of Alph"), as today's rule lets the any-form row decide.
        private static IEnumerable<GoalEntry> PhaseEntries(PhaseInfo phase)
        {
            var entries = phase.Pokemon.Select(p => new GoalEntry(p.SpeciesId, p.RequireSpecificForm ? p.FormCode : null, p.Amount)).ToList();
            return entries.Where(e => e.FormCode is null || e.Amount > 0 || !entries.Any(a => a.Species == e.Species && a.FormCode is null));
        }
    }

    // FR-45's side-by-side view of one species: what the phase would copy and what the goal has now.
    public record CopyOffer(int Species, List<GoalEntry> Phase, List<GoalEntry> Goal);
}
