using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.InfluxDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents.Factories
{
    public class HgssShinyStarterEventFactory
    {
        private HgssPokemonParser parser;
        private PokemonPersistenceService encounterDao;

        public HgssShinyStarterEventFactory(HgssPokemonParser parser, PokemonPersistenceService encounterDao)
        {
            this.parser = parser;
            this.encounterDao = encounterDao;
        }

        public HgssShinyStarterEvent CreateEvent(IBot bot, GameFlavor flavor)
        {
            return new HgssShinyStarterEvent(parser, encounterDao, bot, flavor);
        }
    }
}
