using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Hgss
{
    public static class HgssProfile
    {
        private static readonly HgssPointerService Table = new();

        private static IPointerService Addresses(string? gameCode, int revision) => revision == 0
            ? Table
            : throw new NotSupportedException($"HGSS revision {revision}: no address table. Hunts run on revision 0.");

        public const string DataDirectory = ".\\data\\HGSS";

        // ABILITY_PICKUP, include/constants/abilities.h.
        private const byte AbilityPickup = 53;

        public static GameProfile Create(PokemonMetadataService metadata)
            => Create(metadata, new MapService(DataDirectory), new GraphService(DataDirectory));

        internal static GameProfile Create(PokemonMetadataService metadata, MapService maps, GraphService graph) => new(
            GameFlavor.HGSS,
            new GameCapabilities(
                HasOverworldNavigation: true,
                HasWildEncounters: true,
                HasTraining: true,
                HasEggs: true,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: true,
                HasWishmakerJirachi: false,
                HasChainedEncounter: true,
                HasStaticEncounter: true,
                HasHeadbuttTrees: true,
                HasWildRunChecks: false),
            revisions: [0],
            Addresses,
            new HgssPokemonParser(metadata),
            new HgssBattlePokemonParser(),
            createOverworldReader: (memory, table) => new HgssOverworldReader(memory, (HgssPointerService)table),
            maps,
            graph,
            new PathfindingService(maps, graph, Table.TrainerX, Table.TrainerY, Table.Anchor, Table.MapIdOffset),
            createStateReader: (memory, table) => new HgssFieldSystemStateReader(memory, (HgssPointerService)table),
            createBattleEvent: provider => provider.GetRequiredService<BattleOrchestratorEvent>(),
            saveGame: async (provider, bot) =>
            {
                bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "SaveGame"));
                await bot.WaitForMacroAsync();
            },
            createNavigatorEvent: provider => provider.GetRequiredService<NavigatorEvent>(),
            isPickupMember: member => member.Ability == AbilityPickup,
            // Loaded at the decision, as the Primo config is at its start, so a value can be corrected without a build.
            createPickupVisit: (provider, bot) => new PickupTakeEvent(bot, provider.GetRequiredService<PokemonStateReader>(),
                PickupConfig.Load(PickupConfig.DefaultPath), provider.GetRequiredService<PickupTally>()));
    }
}
