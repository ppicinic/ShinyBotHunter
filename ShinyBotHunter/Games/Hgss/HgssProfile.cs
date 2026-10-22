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

        private static IPointerService Addresses(int revision) => revision == 0
            ? Table
            : throw new NotSupportedException($"HGSS revision {revision}: no address table. Hunts run on revision 0.");

        public const string DataDirectory = ".\\data\\HGSS";

        public static GameProfile Create(PokemonMetadataService metadata)
            => Create(metadata, new MapService(DataDirectory), new GraphService(DataDirectory));

        internal static GameProfile Create(PokemonMetadataService metadata, MapService maps, GraphService graph) => new(
            GameFlavor.HGSS,
            new GameCapabilities(
                HasOverworldNavigation: true,
                HasWildEncounters: true,
                HasEggs: true,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: true),
            revisions: [0],
            Addresses,
            new HgssPokemonParser(metadata),
            new HgssBattlePokemonParser(),
            createOverworldReader: (memory, table) => new HgssOverworldReader(memory, (HgssPointerService)table),
            maps,
            graph,
            new PathfindingService(maps, graph, Table.TrainerX, Table.TrainerY, Table.Anchor, Table.MapIdOffset),
            createStateReader: (memory, table) => new HgssLegacyStateReader(memory, (HgssPointerService)table));
    }
}
