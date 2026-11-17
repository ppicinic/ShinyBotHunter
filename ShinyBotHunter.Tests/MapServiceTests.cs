using Newtonsoft.Json;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using System.IO.Compression;

namespace ShinyBotHunter.Tests
{
    // In the collection because MapService opens maps.bin without read sharing, as StaticEncounterRunTests says.
    [Collection("macro-registry")]
    public class MapServiceTests
    {
        [Fact]
        public void HgssMaps_LoadAsToday_WithElevationZero()
        {
            string directory = Path.Combine(RepoPaths.AppProject, "Data", "HGSS");
            var expected = new Dictionary<int, byte[][]>();
            using (var file = File.OpenRead(Path.Combine(directory, "maps.bin")))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip))
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int id = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int width = reader.ReadInt32();
                    var rows = new byte[height][];
                    for (int y = 0; y < height; y++)
                    {
                        rows[y] = reader.ReadBytes(width);
                    }
                    expected.Add(id, rows);
                }
            }

            var service = new MapService(directory);

            foreach (var (id, rows) in expected)
            {
                Map map = service.GetMap(id, internalId: true);
                Assert.Equal(rows, map.Tiles);
                Assert.Null(map.Elevation);
                Assert.Empty(map.Events.Objects);
                Assert.Empty(map.Events.Triggers);
                Assert.Empty(map.Events.Warps);
                Assert.Empty(map.Events.Connections);
                Assert.Empty(map.Events.Dive);
                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        Assert.Equal(0, map.ElevationAt(y, x));
                    }
                }
            }
        }

        [Fact]
        public void CommittedRsData_Route101_MatchesTheGenerator()
        {
            var processor = new RsMapProcessor(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "pokeruby"), Path.Combine(Path.GetTempPath(), "unused"));
            Map expected = processor.BuildMap(processor.ReadMap("Route101"));

            var service = new MapService(Path.Combine(RepoPaths.AppProject, "Data", "RS"), withElevationAndEvents: true);
            Map committed = service.GetMap(16);

            Assert.Equal("Route101", service.GetMapLookup(16).InternalName);
            Assert.Equal("OldaleTown", service.GetMapLookup(10).InternalName);
            Assert.Equal("LittlerootTown", service.GetMapLookup(9).InternalName);
            Assert.Equal(expected.Tiles, committed.Tiles);
            Assert.Equal(expected.Elevation, committed.Elevation);
            Assert.Equal(expected.Events.Objects, committed.Events.Objects);
            Assert.Equal(expected.Events.Triggers, committed.Events.Triggers);
            Assert.Empty(committed.Events.Warps);
            Assert.Empty(committed.Events.Dive);
            Assert.Empty(committed.Boulders);
            // The fixture has Oldale as id 0 and no Littleroot; the checkout has both.
            Assert.Equal([new MapConnection(Direction.Up, 0, 0)], expected.Events.Connections);
            Assert.Equal([new MapConnection(Direction.Up, 0, 10), new MapConnection(Direction.Down, 0, 9)], committed.Events.Connections);
            var lookup = JsonConvert.DeserializeObject<Dictionary<int, Pathfinding.MapLookup>>(File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Data", "RS", "mapreverselookup.json")))!;
            Assert.Equal(394, lookup.Count);
            Assert.All(lookup.Keys, id => Assert.Equal(id, lookup[id].BotMapId));
            Assert.All(lookup.Keys, id => service.GetMap(id));
        }
    }
}
