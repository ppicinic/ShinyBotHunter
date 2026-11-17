using Newtonsoft.Json.Linq;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Tests
{
    public class RsMapProcessorTests
    {
        private static readonly string Fixture = Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "pokeruby");

        // The spike prototype's (rsmaps.py) Route 101, the data the emulator walked.
        private static readonly string[] Route101Kinds =
        [
            "8080808080808080000000008080808080808080",
            "8080808080808080000000008080808080808080",
            "8080030303000000000000000003030380808080",
            "8080030303030000000000000303030380808080",
            "0303030303030000000000000303030303038080",
            "0303030303000000000000000303030303038080",
            "8080030300008007070707808003030303000000",
            "8080070707078000000000808080800000000000",
            "8080000000000000000000808080800080000000",
            "8080808000800000000000808003030000000000",
            "8080808000000000000000030303030300000000",
            "8080808000120000000000030303030303000000",
            "8080808000000000000000030303030303000000",
            "8080121100000014071212070303030303000000",
            "0311111103000012000000008080030300008080",
            "0303030303031400000000008080030300008080",
            "8080030303031400000000008080808080808080",
            "8080030303001400000000008080808080808080",
            "8080808080808080808014148080808080808080",
            "8080808080808080808014148080808080808080",
        ];

        private static readonly string[] Route101Elevation =
        [
            "00000000333300000000",
            "00000000333300000000",
            "00333333333333330000",
            "03333333333333330000",
            "33333333333333333300",
            "33333333333333333300",
            "00333300000003333333",
            "00000003333000033333",
            "00333333333000033333",
            "00003033333003333333",
            "00003333333333333333",
            "00003333333333333333",
            "00003333333333333333",
            "00333333000033333333",
            "33333333333300333300",
            "33333333333300333300",
            "00333333333300000000",
            "00333333333300000000",
            "00000000003300000000",
            "00000000003300000000",
        ];

        private static RsMapProcessor Processor() => new(Fixture, Path.Combine(Path.GetTempPath(), "unused"));

        private static string[] KindRows(Map map) => map.Tiles.Select(row => Convert.ToHexString(row)).ToArray();

        private static string[] ElevationRows(Map map) => map.Elevation!.Select(row => string.Concat(row.Select(e => e.ToString("X")))).ToArray();

        [Fact]
        public void Route101_KindPlane_IsTheSpikes()
        {
            var processor = Processor();

            Map map = processor.BuildMap(processor.ReadMap("Route101"));

            Assert.Equal(Route101Kinds, KindRows(map));
        }

        [Fact]
        public void Route101_ElevationPlane_IsTheSpikes()
        {
            var processor = Processor();

            Map map = processor.BuildMap(processor.ReadMap("Route101"));

            Assert.Equal(Route101Elevation, ElevationRows(map));
        }

        [Fact]
        public void Route101_SideFile_ObjectsAndTriggers()
        {
            var processor = Processor();

            Map map = processor.BuildMap(processor.ReadMap("Route101"));

            Assert.Equal(
                [new MapObject(9, 13, 0x2D0), new MapObject(7, 14, 0x2BC), new MapObject(10, 13, 0x2EE), new MapObject(5, 11, 0x381), new MapObject(2, 13, 0x3DF)],
                map.Events.Objects);
            Assert.Equal(Constant.MAP_Blocked, map[8, 16]);
            Assert.Equal(
                [
                    new MapTrigger(10, 19, 0x4060, 1), new MapTrigger(11, 19, 0x4060, 1), new MapTrigger(10, 18, 0x4060, 2), new MapTrigger(11, 18, 0x4060, 2),
                    new MapTrigger(6, 16, 0x4060, 2), new MapTrigger(6, 15, 0x4060, 2), new MapTrigger(6, 17, 0x4060, 2), new MapTrigger(7, 13, 0x4060, 2),
                ],
                map.Events.Triggers);
            Assert.Equal([new MapConnection(Direction.Up, 0, 0)], map.Events.Connections);
        }

        [Fact]
        public void Trigger_WithVarZero_IsKeptWithNoVar()
        {
            var processor = Processor();
            JObject json = processor.ReadMap("Route101");
            ((JArray)json["coord_events"]!).Add(JObject.FromObject(new { type = "trigger", x = 4, y = 8, elevation = 3, var = "0", var_value = "0", script = "Test" }));

            Map map = processor.BuildMap(json);

            Assert.Equal(Constant.MAP_ScriptTrigger, map[8, 4]);
            Assert.Contains(new MapTrigger(4, 8, null, 0), map.Events.Triggers);
        }

        [Fact]
        public void Route102_Trainers_BlockTheirTilesAndChangeNothingElse()
        {
            var processor = Processor();
            JObject json = processor.ReadMap("Route102");
            Map withTrainers = processor.BuildMap(json);
            foreach (var trainer in json["object_events"]!.Where(o => (string)o["trainer_type"]! == "TRAINER_TYPE_NORMAL").ToList())
            {
                trainer.Remove();
            }

            Map without = processor.BuildMap(json);

            var differing = new List<(int X, int Y)>();
            for (int y = 0; y < withTrainers.Height; y++)
            {
                for (int x = 0; x < withTrainers.Width; x++)
                {
                    if (withTrainers[y, x] != without[y, x])
                    {
                        differing.Add((x, y));
                        Assert.Equal(Constant.MAP_Blocked, withTrainers[y, x]);
                    }
                }
            }
            Assert.Equal(new HashSet<(int, int)> { (33, 14), (25, 15), (8, 7), (19, 4) }, differing.ToHashSet());
            Assert.Equal(withTrainers.Elevation, without.Elevation);
        }

        [Fact]
        public void Oldale_And_PokemonCenter_Warps()
        {
            var processor = Processor();

            Map oldale = processor.BuildMap(processor.ReadMap("OldaleTown"));
            Map center = processor.BuildMap(processor.ReadMap("OldaleTown_PokemonCenter_1F"));

            Assert.Equal(Constant.MAP_Portal1, oldale[16, 6]);
            Assert.Equal([new MapWarp(6, 16, 256, 7, 8, Direction.None)], oldale.Events.Warps);
            Assert.Equal(Constant.MAP_Portal1, center[8, 6]);
            Assert.Equal(Constant.MAP_Portal1, center[8, 7]);
            Assert.Equal([new MapWarp(7, 8, 0, 6, 16, Direction.Down), new MapWarp(6, 8, 0, 6, 16, Direction.Down)], center.Events.Warps);
            Assert.Equal([new MapConnection(Direction.Down, 0, 1), new MapConnection(Direction.Left, 0, 2)], oldale.Events.Connections);
            Assert.Contains(new MapTrigger(0, 10, 0x4051, 0), oldale.Events.Triggers);
        }

        [Fact]
        public void Warp_LandingOffItsDestination_IsDropped()
        {
            // A copy of the fixture with the Pokemon Center's warp 0, Oldale's door's landing, moved off the 14-wide Center.
            string copy = Path.Combine(Path.GetTempPath(), "rsfixture-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (string file in Directory.GetFiles(Fixture, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(copy, Path.GetRelativePath(Fixture, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
                string centerFile = Path.Combine(copy, "data", "maps", "OldaleTown_PokemonCenter_1F", "map.json");
                JObject centerJson = JObject.Parse(File.ReadAllText(centerFile));
                centerJson["warp_events"]![0]!["x"] = 20;
                File.WriteAllText(centerFile, centerJson.ToString());
                var processor = new RsMapProcessor(copy, Path.Combine(Path.GetTempPath(), "unused"));

                Map oldale = processor.BuildMap(processor.ReadMap("OldaleTown"));
                Map center = processor.BuildMap(processor.ReadMap("OldaleTown_PokemonCenter_1F"));

                Assert.Empty(oldale.Events.Warps);
                Assert.NotEqual(Constant.MAP_Portal1, oldale[16, 6]);
                Assert.Equal([new MapWarp(6, 8, 0, 6, 16, Direction.Down)], center.Events.Warps);
            }
            finally
            {
                Directory.Delete(copy, recursive: true);
            }
        }

        [Fact]
        public void ObjectsByGraphic_TreeRockBoulder()
        {
            var processor = Processor();
            JObject json = processor.ReadMap("Route101");
            var objects = (JArray)json["object_events"]!;
            objects.Add(JObject.FromObject(new { graphics_id = "OBJ_EVENT_GFX_CUTTABLE_TREE", x = 2, y = 8, flag = "0" }));
            objects.Add(JObject.FromObject(new { graphics_id = "OBJ_EVENT_GFX_BREAKABLE_ROCK", x = 3, y = 8, flag = "0" }));
            objects.Add(JObject.FromObject(new { graphics_id = "OBJ_EVENT_GFX_PUSHABLE_BOULDER", x = 4, y = 8, flag = "0" }));

            Map map = processor.BuildMap(json);

            Assert.Equal(Constant.MAP_Cut, map[8, 2]);
            Assert.Equal(Constant.MAP_RockSmash, map[8, 3]);
            Assert.Equal(Constant.MAP_Free, map[8, 4]);
            var boulder = Assert.Single(map.Boulders);
            Assert.Equal((4, 8), (boulder.X, boulder.Y));
        }

        [Theory]
        [InlineData(0x02, 0, Constant.MAP_Grass)]
        [InlineData(0x15, 0, Constant.MAP_Surf)]
        [InlineData(0x16, 0, Constant.MAP_Free)]
        [InlineData(0x17, 0, Constant.MAP_Free)]
        [InlineData(0x13, 0, Constant.MAP_Waterfall)]
        [InlineData(0x38, 0, Constant.MAP_RightLedge)]
        [InlineData(0x39, 0, Constant.MAP_LeftLedge)]
        [InlineData(0x3B, 0, Constant.MAP_DownLedge)]
        [InlineData(0x38, 1, Constant.MAP_RightLedge)]
        [InlineData(0x39, 1, Constant.MAP_LeftLedge)]
        [InlineData(0x3B, 1, Constant.MAP_DownLedge)]
        [InlineData(0x20, 0, Constant.MAP_Ice)]
        [InlineData(0x00, 1, Constant.MAP_Blocked)]
        public void KindOf_FollowsTheBehaviourTable(byte behaviour, int collision, byte kind)
        {
            Assert.Equal(kind, RsMapProcessor.KindOf(behaviour, collision));
        }

        [Fact]
        public void PickDiveTile_FirstDiveableTileThatCanEmerge()
        {
            // Row 0 is land; row 1 is deep water (MB_SEMI_DEEP_WATER) at x 1 and 2.
            var surface = new (byte Behaviour, int Collision, byte Elevation)[2, 3];
            surface[1, 1] = (0x11, 0, 1);
            surface[1, 2] = (0x11, 0, 1);
            var underwater = new (byte Behaviour, int Collision, byte Elevation)[2, 3];
            underwater[1, 1] = (0x19, 0, 0); // MB_NO_SURFACING
            underwater[1, 2] = (0x22, 0, 0); // MB_SEAWEED

            var tile = RsMapProcessor.PickDiveTile("Surface", surface, "Underwater", underwater);

            Assert.Equal((2, 1), tile);
        }

        [Fact]
        public void Generate_RoundTripsThroughMapService()
        {
            string output = Path.Combine(Path.GetTempPath(), "rsmaps-" + Guid.NewGuid().ToString("N"));
            try
            {
                var processor = new RsMapProcessor(Fixture, output);
                Dictionary<int, Map> built = processor.Build();

                processor.Generate();
                var service = new MapService(output, withElevationAndEvents: true);

                Assert.Equal([0, 1, 2, 256], built.Keys.Order());
                foreach (var (id, expected) in built)
                {
                    Map actual = service.GetMap(id);
                    Assert.Equal(expected.Tiles, actual.Tiles);
                    Assert.Equal(expected.Elevation, actual.Elevation);
                    Assert.Equal(expected.Events.Objects, actual.Events.Objects);
                    Assert.Equal(expected.Events.Triggers, actual.Events.Triggers);
                    Assert.Equal(expected.Events.Warps, actual.Events.Warps);
                    Assert.Equal(expected.Events.Connections, actual.Events.Connections);
                    Assert.Equal(expected.Events.Dive, actual.Events.Dive);
                    Assert.Equal(expected.Boulders.Select(b => (b.X, b.Y)), actual.Boulders.Select(b => (b.X, b.Y)));
                }
                Assert.Equal("OldaleTown_PokemonCenter_1F", service.GetMapLookup(256).InternalName);
            }
            finally
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }
}
