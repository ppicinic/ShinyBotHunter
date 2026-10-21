namespace ShinyBotHunter.BotEvents.Factories
{
    public enum EncounterType
    {
        Spin,
        Run
    }
    public record EncounterParams(EncounterType type, int yRange = 0, int xRange = 0);
}
