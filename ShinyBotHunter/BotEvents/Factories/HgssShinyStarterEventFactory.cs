﻿using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents.Factories
{
    public class HgssShinyStarterEventFactory
    {
        private PokemonPersistenceService encounterDao;

        public HgssShinyStarterEventFactory(PokemonPersistenceService encounterDao)
        {
            this.encounterDao = encounterDao;
        }

        public HgssShinyStarterEvent CreateEvent(IBot bot, GameFlavor flavor)
        {
            return new HgssShinyStarterEvent(bot.Profile.Parser, encounterDao, bot, flavor);
        }
    }
}
