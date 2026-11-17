using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
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
    [Collection("macro-registry")]
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
                HasWildRunChecks: false), profile.Capabilities);
            Assert.NotNull(profile.BattleParser);
            Assert.NotNull(profile.CreateOverworldReader);
            Assert.NotNull(profile.CreateStateReader);
            Assert.Equal(new BoxLayout(-0x148D8, 18, 30, 4096), profile.Addresses(null, 0).BoxLayout);
            Assert.Same(maps, profile.Maps);
            Assert.Same(graph, profile.Graph);
            Assert.NotNull(profile.Pathfinding);
            Assert.NotNull(profile.CreateBattleEvent);
            Assert.NotNull(profile.SaveGame);
            NavigatorEvent navigator = new(new PokemonStateReader());
            Assert.Same(navigator, profile.CreateNavigatorEvent!(new ServiceCollection().AddSingleton(navigator).BuildServiceProvider()));
            Assert.Null(profile.TrainingRefusal);
        }

        [Fact]
        public void RsProfileIsConsistent()
        {
            MapService maps = new(new Dictionary<int, Map>());

            GameProfile profile = RsProfile.Create(Metadata(), maps);

            Assert.Equal(GameFlavor.RS, profile.Flavor);
            Assert.Equal(new GameCapabilities(
                HasOverworldNavigation: true,
                HasWildEncounters: true,
                HasTraining: true,
                HasEggs: false,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: true,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: true,
                HasChainedEncounter: false,
                HasStaticEncounter: false,
                HasHeadbuttTrees: false,
                HasPickup: false,
                HasWildRunChecks: true), profile.Capabilities);
            Assert.Null(profile.BattleParser);
            Assert.NotNull(profile.CreateOverworldReader);
            Assert.NotNull(profile.CreateStateReader);
            Assert.IsType<RsPointerService>(profile.Addresses("AXPE", 1));
            Assert.IsType<RsPointerService>(profile.Addresses("AXVE", 2));
            Assert.NotNull(profile.Addresses("AXPE", 1).BoxLayout);
            Assert.NotNull(profile.Addresses("AXVE", 2).BoxLayout);
            Assert.Same(maps, profile.Maps);
            Assert.Null(profile.Graph);
            Assert.Null(profile.Pathfinding);
            Assert.NotNull(profile.CreateBattleEvent);
            Assert.NotNull(profile.SaveGame);
            var provider = new ServiceCollection().AddSingleton<PokemonStateReader>().BuildServiceProvider();
            Assert.IsType<RsNavigatorEvent>(profile.CreateNavigatorEvent!(provider));
            Assert.Null(profile.TrainingRefusal!(RsPointerService.For("AXPE")));
            Assert.Null(profile.TrainingRefusal!(RsPointerService.For("AXVE")));
            Assert.Equal(RsProfile.NoTrainingAddresses, profile.TrainingRefusal!(RsPointerService.For(null)));
            Assert.Equal(RsProfile.NoTrainingAddresses, profile.TrainingRefusal!(RsPointerService.For("AXPJ")));
        }

        [Theory]
        [InlineData("AXPE")]
        [InlineData("AXVE")]
        public void Revision1_HasThePerGamePart(string gameCode)
        {
            GameProfile profile = RsProfile.Create(Metadata(), new MapService(new Dictionary<int, Map>()));

            IPointerService table = profile.Addresses(gameCode, 1);

            Assert.NotNull(((RsPointerService)table).DeleteMoveYesNo);
            Assert.NotNull(((RsPointerService)table).PartyMenuMainCallback2);
            Assert.Null(profile.TrainingRefusal!(table));
        }

        [Fact]
        public void UnknownRevisionThrows()
        {
            GameProfile hgss = HgssProfile.Create(Metadata(), new MapService(new Dictionary<int, Map>()), new GraphService());
            GameProfile rs = RsProfile.Create(Metadata(), new MapService(new Dictionary<int, Map>()));

            var hgssError = Assert.Throws<NotSupportedException>(() => hgss.Addresses(null, 1));
            var rsError = Assert.Throws<NotSupportedException>(() => rs.Addresses(null, 0));

            Assert.Contains("HGSS revision 1", hgssError.Message);
            Assert.Contains("RS revision 0", rsError.Message);
            Assert.IsType<HgssPointerService>(hgss.Addresses(null, 0));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void DeclaredCapabilityWithoutItsPartThrows(bool hasWildEncounters, bool hasOverworldNavigation)
        {
            GameCapabilities capabilities = new(
                HasOverworldNavigation: hasOverworldNavigation,
                HasWildEncounters: hasWildEncounters,
                HasTraining: false,
                HasEggs: false,
                CanReadBoxes: false,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: false);

            // No state reader and no overworld reader: each case declares one missing part.
            Assert.Throws<ArgumentException>(() => new GameProfile(
                GameFlavor.RS,
                capabilities,
                [1],
                (_, _) => new RsPointerService(),
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
                    HasTraining: false,
                    HasEggs: false,
                    CanReadBoxes: false,
                    HasSoftResetStarterHunt: false,
                    HasShinyStarterHunt: false,
                    HasWishmakerJirachi: false),
                [0],
                (_, _) => table,
                new HgssPokemonParser(Metadata()),
                new HgssBattlePokemonParser(),
                createOverworldReader: (memory, t) => new HgssOverworldReader(memory, (HgssPointerService)t),
                maps,
                graph,
                new PathfindingService(maps, graph, table.TrainerX, table.TrainerY, table.Anchor, table.MapIdOffset),
                createNavigatorEvent: _ => throw new NotImplementedException()));

            Assert.Contains("no state reader", error.Message);
        }

        [Fact]
        public void TrainingWithoutOverworldNavigationThrows()
        {
            var error = Assert.Throws<ArgumentException>(() => new GameProfile(
                GameFlavor.HGSS,
                new GameCapabilities(
                    HasOverworldNavigation: false,
                    HasWildEncounters: false,
                    HasTraining: true,
                    HasEggs: false,
                    CanReadBoxes: false,
                    HasSoftResetStarterHunt: false,
                    HasShinyStarterHunt: false,
                    HasWishmakerJirachi: false),
                [0],
                (_, _) => new HgssPointerService(),
                new HgssPokemonParser(Metadata()),
                new HgssBattlePokemonParser()));

            Assert.Contains("declares HasTraining but not HasOverworldNavigation", error.Message);
        }

        // The T3a review's gap: a chained leg that trains must reach a navigator part.
        [Fact]
        public void ChainedEncounterWithoutTrainingThrows()
        {
            var error = Assert.Throws<ArgumentException>(() => new GameProfile(
                GameFlavor.HGSS,
                new GameCapabilities(
                    HasOverworldNavigation: false,
                    HasWildEncounters: false,
                    HasTraining: false,
                    HasEggs: false,
                    CanReadBoxes: false,
                    HasSoftResetStarterHunt: false,
                    HasShinyStarterHunt: false,
                    HasWishmakerJirachi: false,
                    HasChainedEncounter: true),
                [0],
                (_, _) => new HgssPointerService(),
                new HgssPokemonParser(Metadata()),
                new HgssBattlePokemonParser()));

            Assert.Contains("declares HasChainedEncounter but not HasTraining", error.Message);
        }

        // Navigation and training as RS will declare them (FR-18b): maps, an overworld reader and a navigator part, and no
        // graph, pathfinding or battle parser.
        private static GameProfile RsShapedNavigatingProfile(bool navigator = true) => new(
            GameFlavor.RS,
            new GameCapabilities(
                HasOverworldNavigation: true,
                HasWildEncounters: false,
                HasTraining: true,
                HasEggs: false,
                CanReadBoxes: false,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: false),
            [1],
            (_, _) => new RsPointerService(),
            new Gen3PokemonParser(Metadata()),
            battleParser: null,
            createOverworldReader: (memory, t) => new RsOverworldReader(memory, (RsPointerService)t),
            maps: new MapService(new Dictionary<int, Map>()),
            createNavigatorEvent: navigator ? _ => throw new NotImplementedException() : null);

        [Fact]
        public void NavigationWithoutANavigatorEventThrows()
        {
            var error = Assert.Throws<ArgumentException>(() => RsShapedNavigatingProfile(navigator: false));

            Assert.Contains("declares HasOverworldNavigation but has no navigator event", error.Message);
        }

        [Fact]
        public void NavigationWithMapsAndANavigatorButNoGraphIsAccepted()
        {
            GameProfile profile = RsShapedNavigatingProfile();

            Assert.True(profile.Capabilities.HasOverworldNavigation);
            Assert.Null(profile.Graph);
            Assert.Null(profile.Pathfinding);
        }

        [Fact]
        public void TrainingWithoutBattleParserIsAccepted()
        {
            GameProfile profile = RsShapedNavigatingProfile();

            Assert.True(profile.Capabilities.HasTraining);
            Assert.Null(profile.BattleParser);
        }

        [Theory]
        [InlineData(false, "no graph")]
        [InlineData(true, "no pathfinding")]
        public void StaticEncounterWithoutPathfindingThrows(bool withGraph, string missing)
        {
            var error = Assert.Throws<ArgumentException>(() => new GameProfile(
                GameFlavor.HGSS,
                new GameCapabilities(
                    HasOverworldNavigation: false,
                    HasWildEncounters: false,
                    HasTraining: false,
                    HasEggs: false,
                    CanReadBoxes: false,
                    HasSoftResetStarterHunt: false,
                    HasShinyStarterHunt: false,
                    HasWishmakerJirachi: false,
                    HasStaticEncounter: true),
                [0],
                (_, _) => new HgssPointerService(),
                new HgssPokemonParser(Metadata()),
                new HgssBattlePokemonParser(),
                graph: withGraph ? new GraphService() : null));

            Assert.Contains($"declares HasStaticEncounter but has {missing}", error.Message);
        }

        // Wild encounters with every part they need, and no navigation or battle parser.
        private static GameProfile RsShapedWildProfile(bool overworldReader = true, bool battleEvent = true, bool save = true) => new(
            GameFlavor.RS,
            new GameCapabilities(
                HasOverworldNavigation: false,
                HasWildEncounters: true,
                HasTraining: false,
                HasEggs: false,
                CanReadBoxes: false,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: false),
            [1],
            (_, _) => new RsPointerService(),
            new Gen3PokemonParser(Metadata()),
            battleParser: null,
            createOverworldReader: overworldReader ? (memory, t) => new RsOverworldReader(memory, (RsPointerService)t) : null,
            createStateReader: (memory, t) => new RsStateReader(memory, (RsPointerService)t),
            createBattleEvent: battleEvent ? _ => throw new NotImplementedException() : null,
            saveGame: save ? (_, _) => Task.CompletedTask : null);

        [Fact]
        public void WildEncountersWithoutNavigationOrBattleParserIsAccepted()
        {
            GameProfile profile = RsShapedWildProfile();

            Assert.True(profile.Capabilities.HasWildEncounters);
            Assert.Null(profile.BattleParser);
            Assert.Null(profile.Pathfinding);
        }

        [Theory]
        [InlineData(false, true, true, "no overworld reader")]
        [InlineData(true, false, true, "no battle event")]
        [InlineData(true, true, false, "no save")]
        public void WildEncountersWithoutItsPartThrows(bool overworldReader, bool battleEvent, bool save, string missing)
        {
            var error = Assert.Throws<ArgumentException>(() => RsShapedWildProfile(overworldReader, battleEvent, save));

            Assert.Contains($"declares HasWildEncounters but has {missing}", error.Message);
        }

        [Fact]
        public void HgssBattleEventIsBattleOrchestratorEvent()
        {
            // The battle event's constructor only stores what it is given, so the services it never reaches here are null.
            BattleOrchestratorEvent battle = new(null!, Options.Create(new ApplicationSettings()), null!, new PokemonStateReader(), Metadata(), null!, null!, null!);
            var provider = new ServiceCollection().AddSingleton(battle).BuildServiceProvider();
            GameProfile profile = HgssProfile.Create(Metadata(), new MapService(new Dictionary<int, Map>()), new GraphService());

            Assert.Same(battle, profile.CreateBattleEvent!(provider));
        }

        [Fact]
        public async Task HgssSaveGamePressesTheSaveGameMacro()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
            List<string> pressed = [];
            FakeBot bot = new(Task.CompletedTask) { Pressed = pressed.Add };

            await bot.Profile.SaveGame!(new ServiceCollection().BuildServiceProvider(), bot);

            Assert.Equal([MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "SaveGame")], pressed);
        }
    }
}
