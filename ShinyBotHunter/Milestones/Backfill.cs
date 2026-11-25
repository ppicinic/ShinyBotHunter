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
    }
}
