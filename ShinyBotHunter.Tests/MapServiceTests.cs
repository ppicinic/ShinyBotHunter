using ShinyBotHunter.Pathfinding;
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
    }
}
