using Newtonsoft.Json.Linq;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    // The committed Data/HGSS/milestones.json.gz, as --generatemilestoneshgss wrote it from pokeheartgold at 8946577.
    public class HgssMilestoneDataTests
    {
        private const int Caterpie = 10, Metapod = 11, Weedle = 13, Kakuna = 14, Pidgey = 16, Rattata = 19, Poliwag = 60;
        private const int Hoothoot = 163, Ledyba = 165, Spinarak = 167, Linoone = 264, Ralts = 280, Whismur = 293, Bidoof = 399, Buizel = 418;

        private static readonly string DataDirectory = Path.Combine(RepoPaths.AppProject, "Data", "HGSS");

        private static IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Load() => MilestoneFile.Load(DataDirectory);

        private static Milestone Find(RomVersion version, string id) => Load()[version].Single(m => m.Id == id);

        // Weight per species over the slots of one condition, as the game's slot roll gives it.
        private static Dictionary<int, int> Shares(Milestone milestone, SlotCondition condition)
            => milestone.Slots.Where(s => s.Condition == condition).GroupBy(s => s.Species).ToDictionary(g => g.Key, g => g.Sum(s => s.Weight));

        [Fact]
        public void Route30Grass_HeartGold()
        {
            var route30 = Find(RomVersion.HeartGold, "34-land");

            Assert.Equal(new Dictionary<int, int> { [Caterpie] = 50, [Pidgey] = 40, [Metapod] = 10 }, Shares(route30, SlotCondition.Morning));
            Assert.Equal(new Dictionary<int, int> { [Pidgey] = 50, [Caterpie] = 35, [Metapod] = 15 }, Shares(route30, SlotCondition.Day));
            Assert.Equal(new Dictionary<int, int> { [Rattata] = 40, [Spinarak] = 30, [Hoothoot] = 30 }, Shares(route30, SlotCondition.Night));
            Assert.Equal("Route 30 · grass", route30.Name);
        }

        [Fact]
        public void Route30Grass_SoulSilver()
        {
            var route30 = Find(RomVersion.SoulSilver, "34-land");

            Assert.Equal(new Dictionary<int, int> { [Weedle] = 50, [Ledyba] = 30, [Kakuna] = 10, [Pidgey] = 10 }, Shares(route30, SlotCondition.Morning));
            Assert.Equal(new Dictionary<int, int> { [Pidgey] = 50, [Weedle] = 35, [Kakuna] = 15 }, Shares(route30, SlotCondition.Day));
            Assert.Equal(new Dictionary<int, int> { [Hoothoot] = 60, [Rattata] = 40 }, Shares(route30, SlotCondition.Night));
        }

        [Fact]
        public void Counts_PerVersion()
        {
            var versions = Load();

            foreach (var version in new[] { RomVersion.HeartGold, RomVersion.SoulSilver })
            {
                var counts = versions[version].GroupBy(m => m.Modality).ToDictionary(g => g.Key, g => g.Count());
                Assert.Equal(new Dictionary<Modality, int>
                {
                    [Modality.Land] = 90, [Modality.Surf] = 63, [Modality.OldRod] = 58, [Modality.GoodRod] = 59,
                    [Modality.SuperRod] = 59, [Modality.RockSmash] = 9, [Modality.Headbutt] = 60
                }, counts);
            }
            Assert.Equal(versions[RomVersion.HeartGold].Select(m => m.Id), versions[RomVersion.SoulSilver].Select(m => m.Id));
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
            int[] bellTower = [332, 333, 334, 335, 336, 337, 338, 339, 341];

            var land = Load()[RomVersion.HeartGold].Where(m => m.Modality == Modality.Land).ToList();

            var tower = land.Single(m => m.Id == "332-land");
            Assert.Equal(bellTower, tower.Maps);
            Assert.Equal("Bell Tower · cave", tower.Name);
            Assert.Single(land, m => m.Maps.Intersect(bellTower).Any());
        }

        [Fact]
        public void SlotConditions()
        {
            var route30 = Find(RomVersion.HeartGold, "34-land");
            Assert.Equal(12, route30.Slots.Count(s => s.Condition == SlotCondition.Morning));
            Assert.Equal(12, route30.Slots.Count(s => s.Condition == SlotCondition.Day));
            Assert.Equal(12, route30.Slots.Count(s => s.Condition == SlotCondition.Night));
            var hoenn = route30.Slots.Where(s => s.Condition == SlotCondition.HoennSound).ToList();
            Assert.Equal([2, 3, 4, 5], hoenn.Select(s => s.Index));
            Assert.Equal([Whismur, Whismur, Linoone, Linoone], hoenn.Select(s => s.Species));
            Assert.Equal([Buizel, Buizel, Bidoof, Bidoof], route30.Slots.Where(s => s.Condition == SlotCondition.SinnohSound).Select(s => s.Species));

            var goodRod = Find(RomVersion.HeartGold, "34-goodrod").Slots.Single(s => s.Condition == SlotCondition.NightFishing);
            Assert.Equal((3, Poliwag), (goodRod.Index, goodRod.Species));
            Assert.Equal(1, Find(RomVersion.HeartGold, "34-superrod").Slots.Single(s => s.Condition == SlotCondition.NightFishing).Index);
            Assert.All(Find(RomVersion.HeartGold, "34-oldrod").Slots, s => Assert.Equal(SlotCondition.None, s.Condition));
            Assert.All(Find(RomVersion.HeartGold, "34-surf").Slots, s => Assert.Equal(SlotCondition.None, s.Condition));

            var azalea = Find(RomVersion.HeartGold, "74-headbutt");
            Assert.Contains(azalea.Slots, s => s.Condition == SlotCondition.CommonTree);
            Assert.Contains(azalea.Slots, s => s.Condition == SlotCondition.RareTree);
            Assert.Equal(15, azalea.Trees.Length);
            Assert.Equal([[426, 465], [427, 465]], azalea.Trees[0].Tiles);
        }

        [Fact]
        public void NoSwarmSpecies()
        {
            var route34 = Load()[RomVersion.HeartGold].Where(m => m.Maps.Contains(38)).ToList();

            Assert.NotEmpty(route34);
            Assert.DoesNotContain(route34.SelectMany(m => m.Slots), s => s.Species == Ralts);
        }

        [Fact]
        public void Weights_SumTo100()
        {
            SlotCondition[] fullTables = [SlotCondition.None, SlotCondition.Morning, SlotCondition.Day, SlotCondition.Night,
                SlotCondition.CommonTree, SlotCondition.RareTree, SlotCondition.SecretTree];

            foreach (var milestone in Load().Values.SelectMany(m => m))
            {
                foreach (var table in milestone.Slots.Where(s => fullTables.Contains(s.Condition)).GroupBy(s => s.Condition))
                {
                    Assert.True(table.Sum(s => s.Weight) == 100, $"{milestone.Id} {table.Key}");
                }
            }
        }
    }
}
