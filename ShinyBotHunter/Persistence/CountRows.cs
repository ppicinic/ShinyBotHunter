namespace ShinyBotHunter.Persistence
{
    // One row per distinct (phase, ROM copy, form, species) of a game; every count in the cache tree is a sum of these.
    public record CountRow(string Milestone, string GameFileName, string FormCode, int Species, long Encounters, long ShinyEncounters);

    // Encounters since the last shiny. GameFileName null is the phase across all ROM copies.
    public record ProgressRow(string Milestone, string? GameFileName, int SinceLastShiny);
}
