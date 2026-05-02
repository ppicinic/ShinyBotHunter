using ShinyBotHunter.Bot;

namespace ShinyBotHunter.BotEvents.Factories
{
    public enum EncounterType
    {
        Spin
    }
    public class EncounterEventTypeFactory
    {
        public IBotEvent<bool> GetEncounterEvent(IBot bot, EncounterType type) 
        {
            switch (type)
            {
                case EncounterType.Spin:
                    return new SpinEncounterEvent(bot);
                default:
                    throw new ArgumentException("No suitable encounter available");
            }
        }
    }
}
