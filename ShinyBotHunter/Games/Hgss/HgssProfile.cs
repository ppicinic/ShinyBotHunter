using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Games.Hgss
{
    public static class HgssProfile
    {
        private static readonly HgssPointerService Table = new();

        private static IPointerService Addresses(string? gameCode, int revision) => revision == 0
            ? Table
            : throw new NotSupportedException($"HGSS revision {revision}: no address table. Hunts run on revision 0.");

        public const string DataDirectory = ".\\data\\HGSS";

        public static GameProfile Create(PokemonMetadataService metadata)
            => Create(metadata, new MapService(DataDirectory), new GraphService(DataDirectory), MilestoneFile.Load(DataDirectory));

        internal static GameProfile Create(PokemonMetadataService metadata, MapService maps, GraphService graph,
            IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? milestones = null) => new(
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
                HasPickup: true,
                HasWildRunChecks: false,
                HasHuntPlans: true),
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
            // A land milestone with grass is hunted in its grass; one without is a cave, hunted on its floor.
            landTileKind: m => m.HasGrass ? Constant.MAP_Grass : Constant.MAP_Free,
            rates: HgssRates.Compute,
            milestones: milestones);
    }
}
