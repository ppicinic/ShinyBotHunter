using Newtonsoft.Json.Linq;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Tests
{
    // The committed Data/RS/milestones.json.gz, as --generatemilestoners wrote it from pokeruby at 5784633.
    // In the collection because MapService opens maps.bin without read sharing, and RouteSearch asks the registry for
    // RunHold, so the working directory is the app project (as in RouteSearchTests).
    [Collection("macro-registry")]
    public class RsMilestoneDataTests
    {
        private const int Poochyena = 261, Zigzagoon = 263, Wurmple = 265, Lotad = 270, Lombre = 271, Seedot = 273, Nuzleaf = 274;
        private const int Surskit = 283, Swablu = 333, Zangoose = 335, Seviper = 336, Feebas = 349;

        private static readonly string DataDirectory = Path.Combine(RepoPaths.AppProject, "Data", "RS");
        private static readonly RomVersion[] Versions = [RomVersion.Ruby, RomVersion.Sapphire];

        private static IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Load() => MilestoneFile.Load(DataDirectory);

        private static Milestone Find(RomVersion version, string id) => Load()[version].Single(m => m.Id == id);

        private static Dictionary<int, int> Shares(Milestone milestone)
            => milestone.Slots.GroupBy(s => s.Species).ToDictionary(g => g.Key, g => g.Sum(s => s.Weight));

        public RsMilestoneDataTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        [Fact]
        public void Route101Grass_BothVersions()
        {
            foreach (var version in Versions)
            {
                var route101 = Find(version, "16-land");

                Assert.Equal(new Dictionary<int, int> { [Wurmple] = 45, [Zigzagoon] = 45, [Poochyena] = 10 }, Shares(route101));
                Assert.Equal("Route 101 · grass", route101.Name);
            }
        }

        // The spike's anchors (FR-19a), each the cheapest land tile to a Pokémon Center and back.
        [Theory]
        [InlineData("16-land", 16, 4, 2)]
        [InlineData("17-land", 17, 4, 7)]
        [InlineData("18-land", 18, 12, 15)]
        [InlineData("19-land", 19, 2, 11)]
        [InlineData("25-land", 25, 14, 97)]
        public void SpikeAnchors_AreReproduced(string id, int map, int x, int y)
        {
            foreach (var version in Versions)
            {
                Assert.Equal(new Anchor(map, x, y, 0, 0), Find(version, id).Anchor);
            }
        }

        // Route 111's only land-encounter tiles are its desert, MB_UNUSED_DEEP_SAND.
        [Fact]
        public void Route111_AnchorOnItsDesert()
        {
            var maps = new MapService(DataDirectory, withElevationAndEvents: true);

            var anchor = Find(RomVersion.Ruby, "26-land").Anchor!;

            Assert.Equal(new Anchor(26, 19, 38, 0, 0), anchor);
            Assert.Equal(Constant.MAP_Grass, maps.GetMap(26, true)[38, 19]);
        }

        // Cave floor is MB_UNUSED_CAVE.
        [Fact]
        public void GraniteCave1F_AnchorOnItsFloor()
        {
            var maps = new MapService(DataDirectory, withElevationAndEvents: true);

            var milestone = Find(RomVersion.Ruby, "6151-land");

            Assert.Equal("Granite Cave 1F · cave", milestone.Name);
            Assert.Equal(new Anchor(6151, 37, 11, 0, 0), milestone.Anchor);
            Assert.Equal(Constant.MAP_Grass, maps.GetMap(6151, true)[11, 37]);
        }

        [Fact]
        public void LandAnchors_AreSpinsOnTheirMapsReachingACenterAndBack()
        {
            var versions = Load();
            var maps = new MapService(DataDirectory, withElevationAndEvents: true);
            var search = new RouteSearch(maps, new RsPointerService(), GameFlavor.RS);
            byte[] flags = new byte[0x120], vars = new byte[0x200];
            int Elevation(int map, int x, int y) => maps.GetMap(map, true).ElevationAt(y, x) is var e && e == 15 ? 0 : e;

            foreach (var milestone in versions[RomVersion.Ruby].Where(m => m.Modality == Modality.Land && m.Anchor is not null))
            {
                var anchor = milestone.Anchor!;
                Assert.Contains(anchor.Map, milestone.Maps);
                Assert.Equal((0, 0), (anchor.XRange, anchor.YRange));
                Assert.Equal(Constant.MAP_Grass, maps.GetMap(anchor.Map, true)[anchor.Y, anchor.X]);

                var toCenter = search.Find(anchor.Map, anchor.X, anchor.Y, Elevation(anchor.Map, anchor.X, anchor.Y),
                    (m, x, y) => x == 7 && y == 4 && maps.GetMapLookup(m).InternalName.EndsWith("_PokemonCenter_1F"), flags, vars, new Capability());
                Assert.NotNull(toCenter);
                var counter = toCenter.Steps[^1];
                Assert.NotNull(search.Find(counter.Map, counter.X, counter.Y, Elevation(counter.Map, counter.X, counter.Y),
                    (m, x, y) => m == anchor.Map && x == anchor.X && y == anchor.Y, flags, vars, new Capability()));
            }
            Assert.Equal(versions[RomVersion.Ruby].Select(m => m.Anchor), versions[RomVersion.Sapphire].Select(m => m.Anchor));
        }

        [Fact]
        public void Route114Land_DiffersByVersion()
        {
            Assert.Equal(new Dictionary<int, int> { [Swablu] = 40, [Seedot] = 30, [Zangoose] = 19, [Nuzleaf] = 10, [Surskit] = 1 },
                Shares(Find(RomVersion.Ruby, "29-land")));
            Assert.Equal(new Dictionary<int, int> { [Swablu] = 40, [Lotad] = 30, [Seviper] = 19, [Lombre] = 10, [Surskit] = 1 },
                Shares(Find(RomVersion.Sapphire, "29-land")));
        }

        [Fact]
        public void Counts_PerVersion()
        {
            var versions = Load();

            foreach (var version in Versions)
            {
                var counts = versions[version].GroupBy(m => m.Modality).ToDictionary(g => g.Key, g => g.Count());
                Assert.Equal(new Dictionary<Modality, int>
                {
                    [Modality.Land] = 46, [Modality.Surf] = 46, [Modality.OldRod] = 43, [Modality.GoodRod] = 43,
                    [Modality.SuperRod] = 44, [Modality.RockSmash] = 4
                }, counts);
            }
            Assert.Equal(versions[RomVersion.Ruby].Select(m => m.Id), versions[RomVersion.Sapphire].Select(m => m.Id));
        }

        [Fact]
        public void Slots_HaveRangeAndNoCondition()
        {
            foreach (var milestone in Load().Values.SelectMany(m => m))
            {
                Assert.Empty(milestone.Trees);
                Assert.All(milestone.Slots, s =>
                {
                    Assert.InRange(s.MinLevel, 1, s.MaxLevel);
                    Assert.InRange(s.MaxLevel, s.MinLevel, 100);
                    Assert.Equal(SlotCondition.None, s.Condition);
                });
            }
        }

        [Fact]
        public void Weights_AreTheGames()
        {
            var expected = new Dictionary<Modality, (int Index, int Weight)[]>
            {
                [Modality.Land] = [(0, 20), (1, 20), (2, 10), (3, 10), (4, 10), (5, 10), (6, 5), (7, 5), (8, 4), (9, 4), (10, 1), (11, 1)],
                [Modality.Surf] = [(0, 60), (1, 30), (2, 5), (3, 4), (4, 1)],
                [Modality.RockSmash] = [(0, 60), (1, 30), (2, 5), (3, 4), (4, 1)],
                [Modality.OldRod] = [(0, 70), (1, 30)],
                [Modality.GoodRod] = [(2, 60), (3, 20), (4, 20)],
                [Modality.SuperRod] = [(5, 40), (6, 40), (7, 15), (8, 4), (9, 1)],
            };

            foreach (var milestone in Load().Values.SelectMany(m => m))
            {
                Assert.Equal(expected[milestone.Modality], milestone.Slots.Select(s => (s.Index, s.Weight)));
            }
        }

        [Fact]
        public void MapIds_AreInTheCommittedMapData()
        {
            var lookup = JObject.Parse(File.ReadAllText(Path.Combine(DataDirectory, "mapreverselookup.json")));
            var ids = Load().Values.SelectMany(m => m).SelectMany(m => m.Maps).Distinct().ToList();

            Assert.All(ids, id => Assert.Equal(id, (int)lookup[id.ToString()]!["BotMapId"]!));
            Assert.Equal("Route101", (string)lookup["16"]!["InternalName"]!);
            Assert.Equal("MeteorFalls_1F_1R", (string)lookup["6144"]!["InternalName"]!);
            Assert.DoesNotContain(ids, id => id >= 6656 && id <= 6659);
        }

        [Fact]
        public void Names_UniquePerVersion_AndEveryMapNamed()
        {
            var versions = Load();
            var named = JObject.Parse(File.ReadAllText(Path.Combine(DataDirectory, "map-names.json")))["Maps"]!
                .Cast<JProperty>().Select(p => int.Parse(p.Name)).ToHashSet();

            foreach (var milestones in versions.Values)
            {
                Assert.Empty(milestones.GroupBy(m => m.Name).Where(g => g.Count() > 1).Select(g => g.Key));
                Assert.Empty(milestones.SelectMany(m => m.Maps).Except(named));
            }
        }

        [Fact]
        public void SharedTable_IsOneMilestone()
        {
            int[] seafloor = [6172, 6173, 6174, 6175, 6176, 6177, 6178, 6179];

            var land = Load()[RomVersion.Ruby].Where(m => m.Modality == Modality.Land).ToList();

            var cavern = land.Single(m => m.Id == "6172-land");
            Assert.Equal(seafloor, cavern.Maps);
            Assert.Equal("Seafloor Cavern · cave", cavern.Name);
            Assert.Single(land, m => m.Maps.Intersect(seafloor).Any());
        }

        [Fact]
        public void LandWord_FromTheMapType()
        {
            Assert.Equal("Route 111 · grass", Find(RomVersion.Ruby, "26-land").Name);
            Assert.Equal("New Mauville Inside · cave", Find(RomVersion.Ruby, "6197-land").Name);
            Assert.Equal("Mt. Pyre 1F, 2F and 3F · cave", Find(RomVersion.Ruby, "6159-land").Name);
        }

        [Fact]
        public void NoFeebas()
        {
            var route119Rods = Load().Values.SelectMany(m => m)
                .Where(m => m.Maps.Contains(34) && m.Modality is Modality.OldRod or Modality.GoodRod or Modality.SuperRod).ToList();

            Assert.Equal(6, route119Rods.Count);
            Assert.DoesNotContain(route119Rods.SelectMany(m => m.Slots), s => s.Species == Feebas);
        }
    }
}
