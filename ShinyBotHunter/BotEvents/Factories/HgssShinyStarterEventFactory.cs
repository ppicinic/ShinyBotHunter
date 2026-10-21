﻿using Microsoft.Extensions.DependencyInjection;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents.Factories
{
    public class HgssShinyStarterEventFactory
    {
        private IPokemonParser parser;
        private PokemonPersistenceService encounterDao;

        public HgssShinyStarterEventFactory([FromKeyedServices(GameFlavor.HGSS)] IPokemonParser parser, PokemonPersistenceService encounterDao)
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
