using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    public class GameProfileTests
    {
        // The DAO is only read by InitAsync, so a null one is never touched by these tests.
        private static PokemonMetadataService Metadata() => new(null!);

        [Fact]
        public void HgssProfileIsConsistent()
        {
            MapService maps = new(new Dictionary<int, Map>());
            GraphService graph = new();

            GameProfile profile = HgssProfile.Create(Metadata(), maps, graph);

            Assert.Equal(GameFlavor.HGSS, profile.Flavor);
            Assert.Equal(new GameCapabilities(
                HasOverworldNavigation: true,
                HasWildEncounters: true,
                HasEggs: true,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: true,
                HasWishmakerJirachi: false), profile.Capabilities);
            Assert.NotNull(profile.BattleParser);
            Assert.NotNull(profile.CreateOverworldReader);
            Assert.NotNull(profile.CreateStateReader);
            Assert.Equal(new BoxLayout(-0x148D8, 18, 30, 4096), profile.Addresses(0).BoxLayout);
            Assert.Same(maps, profile.Maps);
            Assert.Same(graph, profile.Graph);
            Assert.NotNull(profile.Pathfinding);
        }

        [Fact]
        public void RsProfileIsConsistent()
        {
            GameProfile profile = RsProfile.Create(Metadata());

            Assert.Equal(GameFlavor.RS, profile.Flavor);
            Assert.Equal(new GameCapabilities(
                HasOverworldNavigation: false,
                HasWildEncounters: false,
                HasEggs: false,
                CanReadBoxes: false,
                HasSoftResetStarterHunt: true,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: true), profile.Capabilities);
            Assert.Null(profile.BattleParser);
            Assert.Null(profile.CreateOverworldReader);
            Assert.Null(profile.CreateStateReader);
            Assert.Same(profile.Addresses(1), profile.Addresses(2));
            Assert.IsType<RsPointerService>(profile.Addresses(1));
            Assert.Null(profile.Addresses(1).BoxLayout);
            Assert.Null(profile.Maps);
            Assert.Null(profile.Graph);
            Assert.Null(profile.Pathfinding);
        }

        [Fact]
        public void UnknownRevisionThrows()
        {
            GameProfile hgss = HgssProfile.Create(Metadata(), new MapService(new Dictionary<int, Map>()), new GraphService());
            GameProfile rs = RsProfile.Create(Metadata());

            var hgssError = Assert.Throws<NotSupportedException>(() => hgss.Addresses(1));
            var rsError = Assert.Throws<NotSupportedException>(() => rs.Addresses(0));

            Assert.Contains("HGSS revision 1", hgssError.Message);
            Assert.Contains("RS revision 0", rsError.Message);
            Assert.IsType<HgssPointerService>(hgss.Addresses(0));
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public void DeclaredCapabilityWithoutItsPartThrows(bool hasWildEncounters, bool canReadBoxes, bool hasOverworldNavigation)
        {
            GameCapabilities capabilities = new(
                HasOverworldNavigation: hasOverworldNavigation,
                HasWildEncounters: hasWildEncounters,
                HasEggs: false,
                CanReadBoxes: canReadBoxes,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: false);

            // No battle parser, a table whose BoxLayout is null, and no overworld reader: each case declares one missing part.
            Assert.Throws<ArgumentException>(() => new GameProfile(
                GameFlavor.RS,
                capabilities,
                [1],
                _ => new RsPointerService(),
                new Gen3PokemonParser(Metadata()),
                battleParser: null));
        }

        [Fact]
        public void WildEncountersWithoutStateReaderThrows()
        {
            HgssPointerService table = new();
            MapService maps = new(new Dictionary<int, Map>());
            GraphService graph = new();

            var error = Assert.Throws<ArgumentException>(() => new GameProfile(
                GameFlavor.HGSS,
                new GameCapabilities(
                    HasOverworldNavigation: true,
                    HasWildEncounters: true,
                    HasEggs: false,
                    CanReadBoxes: false,
                    HasSoftResetStarterHunt: false,
                    HasShinyStarterHunt: false,
                    HasWishmakerJirachi: false),
                [0],
                _ => table,
                new HgssPokemonParser(Metadata()),
                new HgssBattlePokemonParser(),
                createOverworldReader: (memory, t) => new HgssOverworldReader(memory, (HgssPointerService)t),
                maps,
                graph,
                new PathfindingService(maps, graph, table.TrainerX, table.TrainerY, table.Anchor, table.MapIdOffset)));

            Assert.Contains("no state reader", error.Message);
        }

        [Fact]
        public void WildEncountersWithoutOverworldNavigationThrows()
        {
            var error = Assert.Throws<ArgumentException>(() => new GameProfile(
                GameFlavor.HGSS,
                new GameCapabilities(
                    HasOverworldNavigation: false,
                    HasWildEncounters: true,
                    HasEggs: false,
                    CanReadBoxes: false,
                    HasSoftResetStarterHunt: false,
                    HasShinyStarterHunt: false,
                    HasWishmakerJirachi: false),
                [0],
                _ => new HgssPointerService(),
                new HgssPokemonParser(Metadata()),
                new HgssBattlePokemonParser(),
                createStateReader: (memory, t) => new HgssFieldSystemStateReader(memory, (HgssPointerService)t)));

            Assert.Contains("not HasOverworldNavigation", error.Message);
        }
    }
}
