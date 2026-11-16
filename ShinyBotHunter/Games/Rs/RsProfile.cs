using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Rs
{
    public static class RsProfile
    {
        private static readonly RsPointerService Table = new();

        private static IPointerService Addresses(int revision) => revision is 1 or 2
            ? Table
            : throw new NotSupportedException($"RS revision {revision}: no address table. Hunts run on revisions 1 and 2.");

        public static GameProfile Create(PokemonMetadataService metadata) => new(
            GameFlavor.RS,
            new GameCapabilities(
                HasOverworldNavigation: false,
                HasWildEncounters: true,
                HasTraining: false,
                HasEggs: false,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: true,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: true),
            revisions: [1, 2],
            Addresses,
            new Gen3PokemonParser(metadata),
            battleParser: null,
            createOverworldReader: (memory, table) => new RsOverworldReader(memory, (RsPointerService)table),
            createStateReader: (memory, table) => new RsStateReader(memory, (RsPointerService)table),
            createBattleEvent: provider => new RsBattleEvent(provider.GetRequiredService<PokemonPersistenceService>(), provider.GetRequiredService<ShinyAlertService>(), provider.GetRequiredService<LegalityService>()),
            saveGame: (provider, bot) =>
            {
                // The starter hunt's save step and its values (FR-13); loaded per save, as the hunt loads it per start.
                StarterHuntConfig config = StarterHuntConfig.Load();
                return provider.GetRequiredService<RsStartMenuService>().SaveViaStartMenuAsync(bot, Table,
                    config.SaveMenuActionId, config.SaveConfirmMacro, config.BattleWaitTimeoutMs, config.BattlePollIntervalMs, CancellationToken.None);
            });
    }
}
