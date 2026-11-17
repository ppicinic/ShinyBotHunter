using Newtonsoft.Json;
using ShinyBotHunter.Games;
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
        // Null when the container has no elevation plane (HGSS); every tile then reads elevation 0.
        public byte[][]? Elevation { get; set; }
        public MapEvents Events { get; set; } = new();
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

        public byte ElevationAt(int y, int x) => Elevation is null ? (byte)0 : Elevation[y][x];
    }

    // A map's script state and exits, from the side file (events.json.gz).
    public class MapEvents
    {
        public List<MapObject> Objects { get; set; } = [];
        public List<MapTrigger> Triggers { get; set; } = [];
        public List<MapWarp> Warps { get; set; } = [];
        public List<MapConnection> Connections { get; set; } = [];
        public List<MapDive> Dive { get; set; } = [];
    }

    // An object that stands while its flag is clear; a null Flag did not resolve and the object is always there.
    public record MapObject(int X, int Y, int? Flag);

    // A trigger that fires while its variable holds Value; a null Var did not resolve (or is "0") and the trigger is
    // always live. The game compares the variable with Value cut to a byte (VarGet(var) == (u8)value).
    public record MapTrigger(int X, int Y, int? Var, int Value);

    // ToX, ToY is the destination's tile (its warp event's tile), not the destination warp's index.
    // Arrow is the direction to press on the tile for an arrow warp, None for a door.
    public record MapWarp(int X, int Y, int Map, int ToX, int ToY, Direction Arrow);

    public record MapConnection(Direction Direction, int Offset, int Map);

    // The Dive access tile: the same (X, Y) on the surface map and on the underwater map, one entry on each.
    public record MapDive(int X, int Y, int Map);

    public class MapLookup
    {
        public int BotMapId { get; set; }
        public string InternalName { get; set; } = null!;
    }
    public class MapService
    {
        public MapService(string directory, bool withElevationAndEvents = false)
        {
            LoadMaps(directory, withElevationAndEvents);
        }
        internal MapService(Dictionary<int, Map> maps)
        {
            Maps = maps;
        }
        private Dictionary<int, MapLookup> mapLookup = new Dictionary<int, MapLookup>();
        private Dictionary<int, List<MapLookup>> botMapLookup = new Dictionary<int, List<MapLookup>>();
        private Dictionary<int, List<int>> botMapGameIds = new Dictionary<int, List<int>>();
        private Dictionary<int, Map> Maps { get; set; } = new Dictionary<int, Map>();
        public void LoadMaps(string directory, bool withElevationAndEvents)
        {
            Console.WriteLine("Load maps");
            using (FileStream compressedFileStream = new FileStream(Path.Combine(directory, "maps.bin"), FileMode.Open))
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
                    if (withElevationAndEvents)
                    {
                        map.Elevation = new byte[height][];
                        for (int y = 0; y < height; y++)
                        {
                            map.Elevation[y] = reader.ReadBytes(width);
                        }
                    }
                    Maps.Add(id, map);
                }
                Console.WriteLine($"Loaded {Maps.Keys.Count()} into memory. Total memory size: {byteCount} bytes.");
            }
            mapLookup = JsonConvert.DeserializeObject<Dictionary<int, MapLookup>>(File.ReadAllText(Path.Combine(directory, "mapreverselookup.json")))!;
            foreach(var (key, lookup) in mapLookup)
            {
                if (!botMapLookup.ContainsKey(lookup.BotMapId))
                {
                    botMapLookup.Add(lookup.BotMapId, new List<MapLookup>());
                    botMapGameIds.Add(lookup.BotMapId, new List<int>());
                }
                botMapLookup[lookup.BotMapId].Add(lookup);
                botMapGameIds[lookup.BotMapId].Add(key);
            }
            var boulders = JsonConvert.DeserializeObject<Dictionary<int, BoulderData[]>>(File.ReadAllText(Path.Combine(directory, "boulders.json")))!;
            foreach(var (boulderKey, boulderData) in boulders)
            {
                Maps[boulderKey].Boulders = boulderData;
            }
            if (withElevationAndEvents)
            {
                using FileStream eventsFile = new FileStream(Path.Combine(directory, "events.json.gz"), FileMode.Open, FileAccess.Read);
                using GZipStream eventsStream = new GZipStream(eventsFile, CompressionMode.Decompress);
                using StreamReader eventsReader = new StreamReader(eventsStream);
                var events = JsonConvert.DeserializeObject<Dictionary<int, MapEvents>>(eventsReader.ReadToEnd())!;
                foreach (var (eventsKey, mapEvents) in events)
                {
                    Maps[eventsKey].Events = mapEvents;
                }
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

        // All raw/hardware game map ids that share the given internal (bot) map id.
        // A single bot map id (e.g. the shared Pokemon Center or overworld layout) maps
        // to many game map ids — one per physical location that reuses that layout.
        public IReadOnlyList<int> GetGameMapIds(int botMapId)
        {
            return botMapGameIds.TryGetValue(botMapId, out var ids) ? ids : new List<int>();
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
