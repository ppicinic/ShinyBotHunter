using Newtonsoft.Json.Linq;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    // The committed Data/RS/milestones.json.gz, as --generatemilestoners wrote it from pokeruby at 5784633.
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
