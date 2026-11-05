namespace ShinyBotHunter.BotEvents.Factories
{
    public enum EncounterType
    {
        Spin,
        Run,
        Headbutt
    }
    public record EncounterParams(EncounterType type, int yRange = 0, int xRange = 0);
}
