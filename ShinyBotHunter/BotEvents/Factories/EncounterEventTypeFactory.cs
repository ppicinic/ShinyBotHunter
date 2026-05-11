using ShinyBotHunter.Bot;

namespace ShinyBotHunter.BotEvents.Factories
{
    public enum EncounterType
    {
        Spin,
        Run
    }
    public record EncounterParams(EncounterType type, int yRange = 0, int xRange = 0);
    public class EncounterEventTypeFactory
    {
        public IBotEvent<bool> GetEncounterEvent(IBot bot, EncounterParams param) 
        {
            switch (param.type)
            {
                case EncounterType.Spin:
                    return new SpinEncounterEvent(bot);
                case EncounterType.Run:
                    return new RunEncounterEvent(bot, param);
                default:
                    throw new ArgumentException("No suitable encounter available");
            }
        }
    }
}
