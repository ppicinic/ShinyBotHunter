using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShinyBotHunter.Games;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Tests
{
    // The committed Data/HGSS/milestones.json.gz, as --generatemilestoneshgss wrote it from pokeheartgold at 8946577.
    // In the collection because MapService opens maps.bin without read sharing, as MapServiceTests says.
    [Collection("macro-registry")]
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
        public void Route30Grass_Anchor()
        {
            Assert.Equal(new Anchor(34, 547, 304, 0, 6), Find(RomVersion.HeartGold, "34-land").Anchor);
            Assert.Equal(new Anchor(34, 547, 304, 0, 6), Find(RomVersion.SoulSilver, "34-land").Anchor);
        }

        [Fact]
        public void Route32Grass_Anchor()
        {
            Assert.Equal(new Anchor(36, 458, 339, 0, 7), Find(RomVersion.HeartGold, "36-land").Anchor);
            Assert.Equal(new Anchor(36, 458, 339, 0, 7), Find(RomVersion.SoulSilver, "36-land").Anchor);
        }

        [Fact]
        public async Task UnionCave1F_AnchorIsReachedByThePathfinder()
        {
            var anchor = Find(RomVersion.HeartGold, "99-land").Anchor!;
            var maps = new MapService(DataDirectory);
            var pointers = new HgssPointerService();
            var pathfinding = new PathfindingService(maps, new GraphService(DataDirectory), pointers.TrainerX, pointers.TrainerY, pointers.Anchor, pointers.MapIdOffset);

            var toCenter = await pathfinding.FindClosestPokemonCenter(anchor.Map, anchor.X, anchor.Y, new Capability(), (byte)Direction.Down);
            Assert.NotNull(toCenter);
            var center = toCenter.Last().Node;
            var back = await pathfinding.FindNodePath(center.MapId, center.X, center.Y, anchor.Map, anchor.X, anchor.Y, new Capability(), (byte)Direction.Down, startInternalId: true);

            Assert.Equal(Constant.MAP_Free, maps.GetMap(anchor.Map)[anchor.Y, anchor.X]);
            Assert.NotNull(back);
        }

        [Fact]
        public void LandAnchors_LieOnTheirKind()
        {
            var versions = Load();
            var maps = new MapService(DataDirectory);

            foreach (var milestone in versions[RomVersion.HeartGold].Where(m => m.Modality == Modality.Land && m.Anchor is not null))
            {
                var anchor = milestone.Anchor!;
                Assert.Contains(anchor.Map, milestone.Maps);
                byte kind = milestone.HasGrass ? Constant.MAP_Grass : Constant.MAP_Free;
                var map = maps.GetMap(anchor.Map);
                for (int dx = -anchor.XRange; dx <= anchor.XRange; dx++)
                {
                    for (int dy = -anchor.YRange; dy <= anchor.YRange; dy++)
                    {
                        Assert.True(map[anchor.Y + dy, anchor.X + dx] == kind, $"{milestone.Id} ({anchor.X + dx}, {anchor.Y + dy})");
                    }
                }
            }
            Assert.Equal(versions[RomVersion.HeartGold].Select(m => m.Anchor), versions[RomVersion.SoulSilver].Select(m => m.Anchor));
        }

        [Fact]
        public void HeadbuttStands_BesideTheirTreeFacingIt()
        {
            var versions = Load();
            var maps = new MapService(DataDirectory);

            foreach (var milestone in versions[RomVersion.HeartGold].Where(m => m.Modality == Modality.Headbutt))
            {
                var treeTiles = milestone.Trees.SelectMany(t => t.Tiles).Select(t => (t[0], t[1])).ToHashSet();
                var map = maps.GetMap(milestone.Maps[0]);
                foreach (var tree in milestone.Trees.Where(t => t.Anchor is not null))
                {
                    var stand = tree.Anchor!;
                    var (stepX, stepY) = tree.Facing switch { Direction.Up => (0, -1), Direction.Down => (0, 1), Direction.Left => (-1, 0), Direction.Right => (1, 0), _ => (0, 0) };
                    int x = stand.X + stepX, y = stand.Y + stepY;
                    string where = $"{milestone.Id} stand ({stand.X}, {stand.Y}) {tree.Facing}";
                    Assert.Equal(milestone.Maps[0], stand.Map);
                    Assert.True(tree.Tiles.Any(t => t[0] == x && t[1] == y), where);
                    Assert.True(map[y, x] == Constant.MAP_Blocked, where);
                    Assert.DoesNotContain((stand.X, stand.Y), treeTiles);
                }
            }
            // The tiles are arrays, which records compare by reference, so the versions' trees are compared as JSON.
            Assert.Equal(JsonConvert.SerializeObject(versions[RomVersion.HeartGold].Select(m => m.Trees)),
                JsonConvert.SerializeObject(versions[RomVersion.SoulSilver].Select(m => m.Trees)));
        }

        // No stand on water, a Cut tile or a Rock Smash tile: headbutt while surfing is not run, and an HM tile is no stand.
        [Fact]
        public void HeadbuttStands_OnFreeOrGrass()
        {
            var maps = new MapService(DataDirectory);

            foreach (var milestone in Load()[RomVersion.HeartGold].Where(m => m.Modality == Modality.Headbutt))
            {
                foreach (var stand in milestone.Trees.Select(t => t.Anchor).OfType<Anchor>())
                {
                    byte kind = maps.GetMap(stand.Map)[stand.Y, stand.X];
                    Assert.True(kind is Constant.MAP_Free or Constant.MAP_Grass, $"{milestone.Id} ({stand.X}, {stand.Y}) kind {kind}");
                }
            }
        }

        // Tree 22's cheapest stand was (471, 439), a Rock Smash tile reached only with every HM; it now has none.
        [Fact]
        public async Task HeadbuttStands_Route32_ReachedWithNoHm()
        {
            var route32 = Find(RomVersion.HeartGold, "36-headbutt");
            var maps = new MapService(DataDirectory);
            var pointers = new HgssPointerService();
            var pathfinding = new PathfindingService(maps, new GraphService(DataDirectory), pointers.TrainerX, pointers.TrainerY, pointers.Anchor, pointers.MapIdOffset);

            foreach (var stand in route32.Trees.Select(t => t.Anchor).OfType<Anchor>())
            {
                var toCenter = await pathfinding.FindClosestPokemonCenter(stand.Map, stand.X, stand.Y, new Capability(), (byte)Direction.Down);
                Assert.NotNull(toCenter);
                var center = toCenter.Last().Node;
                var back = await pathfinding.FindNodePath(center.MapId, center.X, center.Y, stand.Map, stand.X, stand.Y, new Capability(), (byte)Direction.Down, startInternalId: true);
                Assert.NotNull(back);
            }
            Assert.Null(route32.Trees[22].Anchor);
            Assert.Equal(29, route32.Trees.Count(t => t.Anchor is not null));
        }

        [Fact]
        public void HeadbuttStands_Counts()
        {
            var headbutt = Load()[RomVersion.HeartGold].Where(m => m.Modality == Modality.Headbutt).ToList();

            Assert.Equal(686, headbutt.Sum(m => m.Trees.Count(t => t.Anchor is not null)));
            Assert.Equal(872, headbutt.Sum(m => m.Trees.Length));
            Assert.Equal(["9-headbutt", "47-headbutt", "49-headbutt", "93-headbutt", "460-headbutt"],
                headbutt.Where(m => m.Trees.All(t => t.Anchor is null)).Select(m => m.Id));
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
