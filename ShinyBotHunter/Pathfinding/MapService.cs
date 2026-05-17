using Newtonsoft.Json;
using ShinyBotHunter.Scripts.Maps;
using System.IO.Compression;

namespace ShinyBotHunter.Pathfinding
{
    public class Map
    {
        public byte[][] Tiles { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public BoulderData[] Boulders { get; set; } = [];
        public Map(int height, int width)
        {
            Height = height;
            Width = width;
            Tiles = new byte[Height][];
            for(int i = 0; i < Height; i++)
            {
                Tiles[i] = new byte[Width];
            }
        }

        public byte this[int y, int x]
        {
            get => Tiles[y][x];
            set => Tiles[y][x] = value;
        }

    }

    public class MapLookup
    {
        public int BotMapId { get; set; }
        public string InternalName { get; set; } = null!;
    }
    public class MapService
    {
        public static readonly MapService Instance = new MapService();

        private MapService()
        {
            LoadMaps();
        }
        private Dictionary<int, MapLookup> mapLookup = new Dictionary<int, MapLookup>();
        private Dictionary<int, List<MapLookup>> botMapLookup = new Dictionary<int, List<MapLookup>>();
        private Dictionary<int, Map> Maps { get; set; } = new Dictionary<int, Map>();
        public void LoadMaps()
        {
            Console.WriteLine("Load maps");
            using (FileStream compressedFileStream = new FileStream(".\\data\\HGSS\\maps.bin", FileMode.Open))
            using (GZipStream decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(decompressor))
            {
                int mapCount = reader.ReadInt32();
                long byteCount = 0;
                for (int i = 0; i < mapCount; i++)
                {
                    int id = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int width = reader.ReadInt32();
                    Map map = new Map(height, width);
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            map[y, x] = reader.ReadByte();
                            byteCount++;
                        }
                    }
                    Maps.Add(id, map);
                }
                Console.WriteLine($"Loaded {Maps.Keys.Count()} into memory. Total memory size: {byteCount} bytes.");
            }
            mapLookup = JsonConvert.DeserializeObject<Dictionary<int, MapLookup>>(File.ReadAllText(".\\data\\HGSS\\mapreverselookup.json"))!;
            foreach(var (key, lookup) in mapLookup)
            {
                if (!botMapLookup.ContainsKey(lookup.BotMapId))
                {
                    botMapLookup.Add(lookup.BotMapId, new List<MapLookup>());
                }
                botMapLookup[lookup.BotMapId].Add(lookup);
            }
            var boulders = JsonConvert.DeserializeObject<Dictionary<int, BoulderData[]>>(File.ReadAllText(".\\data\\HGSS\\boulders.json"))!;
            foreach(var (boulderKey, boulderData) in boulders)
            {
                Maps[boulderKey].Boulders = boulderData;
            }
        }

        public List<MapLookup> GetMapLookups(int id, bool internalId = false)
        {
            if (internalId)
            {
                return botMapLookup[id];
            }
            else 
            { 
                return new List<MapLookup>() { mapLookup[id] };
            }
        }
        public MapLookup GetMapLookup(int id)
        {
            return mapLookup[id];
        }
        public Map GetMap(int id, bool internalId = false)
        {
            if (internalId)
            {
                return Maps[id];
            }
            else
            {
                return Maps[mapLookup[id].BotMapId];
            }
        }
    }

    
}
