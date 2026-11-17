using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Util;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ShinyBotHunter.Scripts.Maps
{
    // Writes Data/RS from a pret/pokeruby checkout: per map a kind plane and an elevation plane (maps.bin), the side
    // data (events.json.gz), the boulders and the id lookup. Map id = group index * 256 + position in the group.
    public class RsMapProcessor
    {
        // Behaviours, from include/constants/metatile_behaviors.h at 5784633.
        private const byte MB_TALL_GRASS = 0x02;
        private const byte MB_LONG_GRASS = 0x03;
        private const byte MB_LONG_GRASS_SOUTH_EDGE = 0x09;
        private const byte MB_INDOOR_ENCOUNTER = 0x0B;
        private const byte MB_MT_PYRE_HOLE = 0x0F;
        private const byte MB_WATERFALL = 0x13;
        private const byte MB_STAIRS_OUTSIDE_ABANDONED_SHIP = 0x1B;
        private const byte MB_SHOAL_CAVE_ENTRANCE = 0x1C;
        private const byte MB_ICE = 0x20;
        private const byte MB_ASHGRASS = 0x24;
        private const byte MB_THIN_ICE = 0x26;
        private const byte MB_CRACKED_ICE = 0x27;
        private const byte MB_LAVARIDGE_GYM_B1F_WARP = 0x29;
        private const byte MB_JUMP_EAST = 0x38;
        private const byte MB_JUMP_WEST = 0x39;
        private const byte MB_JUMP_SOUTH = 0x3B;
        private const byte MB_SLIDE_EAST = 0x44;
        private const byte MB_SLIDE_SOUTH = 0x47;
        private const byte MB_NON_ANIMATED_DOOR = 0x60;
        private const byte MB_LADDER = 0x61;
        private const byte MB_UNUSED_EAST_ARROW_WARP = 0x62;
        private const byte MB_WEST_ARROW_WARP = 0x63;
        private const byte MB_NORTH_ARROW_WARP = 0x64;
        private const byte MB_SOUTH_ARROW_WARP = 0x65;
        private const byte MB_AQUA_HIDEOUT_WARP = 0x67;
        private const byte MB_LAVARIDGE_GYM_1F_WARP = 0x68;
        private const byte MB_ANIMATED_DOOR = 0x69;
        private const byte MB_UP_ESCALATOR = 0x6A;
        private const byte MB_DOWN_ESCALATOR = 0x6B;
        private const byte MB_WATER_DOOR = 0x6C;
        private const byte MB_WATER_SOUTH_ARROW_WARP = 0x6D;
        private const byte MB_UNUSED_DEEP_SOUTH_WARP = 0x6E;
        private const byte MB_PETALBURG_GYM_DOOR = 0x8D;
        // MB_MUDDY_SLOPE to MB_HORIZONTAL_RAIL: slopes, cracked floor and rails.
        private const byte MB_MUDDY_SLOPE = 0xD0;
        private const byte MB_HORIZONTAL_RAIL = 0xD6;

        private static readonly HashSet<byte> Grass = [MB_TALL_GRASS, MB_LONG_GRASS, MB_LONG_GRASS_SOUTH_EDGE, MB_INDOOR_ENCOUNTER, MB_ASHGRASS];
        // The behaviours with the surfable bit in sTileBitAttributes (src/metatile_behavior.c).
        private static readonly HashSet<byte> Surfable = [0x10, 0x11, 0x12, 0x14, 0x15, 0x19, 0x22, 0x2A, 0x50, 0x51, 0x52, 0x53, 0x6C, 0x6D, 0x6F];
        // MetatileBehavior_IsDiveable and MetatileBehavior_IsNotSurfacable.
        private static readonly HashSet<byte> Diveable = [0x11, 0x12, 0x14];
        private static readonly HashSet<byte> NotSurfacable = [0x19, 0x2A];

        private readonly string checkoutDir;
        private readonly string outputDir;
        // Every map.json, by map id, and each map's MAP_* constant to its id.
        private readonly SortedDictionary<int, JObject> mapsById = new();
        private readonly Dictionary<string, int> idByConstant = new();
        private readonly Dictionary<string, JObject> layouts;
        // gTileset_X to the path of its metatile_attributes.bin, and the files read so far.
        private readonly Dictionary<string, string> attributeFileByTileset;
        private readonly Dictionary<string, byte[]> attributes = new();
        private readonly Dictionary<string, string> flagDefines;
        private readonly Dictionary<string, string> varDefines;

        public RsMapProcessor(string checkoutDir, string outputDir)
        {
            this.checkoutDir = checkoutDir;
            this.outputDir = outputDir;
            var groups = JObject.Parse(File.ReadAllText(Path.Combine(checkoutDir, "data", "maps", "map_groups.json")));
            int groupIndex = 0;
            foreach (var group in groups["group_order"]!)
            {
                int position = 0;
                foreach (var name in groups[(string)group!]!)
                {
                    int id = groupIndex * 256 + position;
                    var map = ReadMap((string)name!);
                    mapsById.Add(id, map);
                    idByConstant.Add((string)map["id"]!, id);
                    position++;
                }
                groupIndex++;
            }
            layouts = JObject.Parse(File.ReadAllText(Path.Combine(checkoutDir, "data", "layouts", "layouts.json")))["layouts"]!
                .Cast<JObject>().ToDictionary(layout => (string)layout["id"]!);
            var tilesetAttributes = LabelValues(Path.Combine(checkoutDir, "data", "tilesets", "headers.inc"), new Regex(@"(gMetatileAttributes_\w+)"));
            var attributeFiles = LabelValues(Path.Combine(checkoutDir, "data", "tilesets", "metatiles.inc"), new Regex(@"\.incbin ""([^""]+)"""));
            attributeFileByTileset = tilesetAttributes.ToDictionary(pair => pair.Key, pair => attributeFiles[pair.Value]);
            flagDefines = Defines(Path.Combine(checkoutDir, "include", "constants", "flags.h"));
            varDefines = Defines(Path.Combine(checkoutDir, "include", "constants", "vars.h"));
        }

        internal JObject ReadMap(string name)
            => JObject.Parse(File.ReadAllText(Path.Combine(checkoutDir, "data", "maps", name, "map.json")));

        internal static byte KindOf(byte behaviour, int collision)
        {
            if (behaviour == MB_JUMP_EAST) return Constant.MAP_RightLedge;
            if (behaviour == MB_JUMP_WEST) return Constant.MAP_LeftLedge;
            if (behaviour == MB_JUMP_SOUTH) return Constant.MAP_DownLedge;
            if (collision != 0) return Constant.MAP_Blocked;
            if (Grass.Contains(behaviour)) return Constant.MAP_Grass;
            if (behaviour == MB_WATERFALL) return Constant.MAP_Waterfall;
            if (Surfable.Contains(behaviour)) return Constant.MAP_Surf;
            if (behaviour is MB_ICE or MB_THIN_ICE or MB_CRACKED_ICE or (>= MB_SLIDE_EAST and <= MB_SLIDE_SOUTH) or (>= MB_MUDDY_SLOPE and <= MB_HORIZONTAL_RAIL))
            {
                return Constant.MAP_Ice;
            }
            return Constant.MAP_Free;
        }

        // How the game fires a warp event on a tile of this behaviour (src/field_control_avatar.c): null when it does
        // not; None for a step onto the tile (TryStartWarpEventScript, IsWarpMetatileBehavior), and for an animated
        // door also a press Up into it from the tile below (map_warp_consider_2_to_inside); otherwise the direction to
        // press while standing on the tile (mapheader_run_first_tag2_script_list_match_conditionally, IsArrowWarpMetatileBehavior).
        internal static Direction? WarpArrow(byte behaviour) => behaviour switch
        {
            MB_ANIMATED_DOOR or MB_LADDER or MB_UP_ESCALATOR or MB_DOWN_ESCALATOR or MB_NON_ANIMATED_DOOR or MB_UNUSED_DEEP_SOUTH_WARP
                or MB_WATER_DOOR or MB_LAVARIDGE_GYM_B1F_WARP or MB_LAVARIDGE_GYM_1F_WARP or MB_AQUA_HIDEOUT_WARP or MB_MT_PYRE_HOLE => Direction.None,
            MB_NORTH_ARROW_WARP or MB_STAIRS_OUTSIDE_ABANDONED_SHIP => Direction.Up,
            MB_SOUTH_ARROW_WARP or MB_WATER_SOUTH_ARROW_WARP or MB_SHOAL_CAVE_ENTRANCE => Direction.Down,
            MB_WEST_ARROW_WARP => Direction.Left,
            MB_UNUSED_EAST_ARROW_WARP => Direction.Right,
            _ => null,
        };

        // Where the player stands when control returns, from the destination warp event's tile, by the behaviour of
        // the tile the warp fired on and of the tile it lands on. Escalators walk East (sub_8086A2C, sub_8086ED4) and
        // the Lavaridge B1F warp jumps East (mapldr_080851BC, sub_8087548); the Lavaridge 1F warp and the Mt. Pyre hole
        // fall in place (sub_8086748) and the Aqua Hideout warp appears in place (sub_8080B78). Every other warp goes
        // through mapldr_default (src/field_fadetransition.c, sub_8080AE4): off a door one tile Down (sub_8080B9C), off
        // a non-animated door one tile in the facing direction (task_map_chg_seq_0807E20C), which
        // GetAdjustedInitialDirection (src/overworld.c) makes North for 0x6E and South for the others.
        internal static (int Dx, int Dy) ArrivalStep(byte fired, byte landing) => fired switch
        {
            MB_UP_ESCALATOR or MB_DOWN_ESCALATOR or MB_LAVARIDGE_GYM_B1F_WARP => (1, 0),
            MB_LAVARIDGE_GYM_1F_WARP or MB_AQUA_HIDEOUT_WARP or MB_MT_PYRE_HOLE => (0, 0),
            _ => landing switch
            {
                MB_ANIMATED_DOOR or MB_PETALBURG_GYM_DOOR or MB_NON_ANIMATED_DOOR or MB_WATER_DOOR => (0, 1),
                MB_UNUSED_DEEP_SOUTH_WARP => (0, -1),
                _ => (0, 0),
            },
        };

        // The first tile in reading order that is diveable on the surface and can be surfaced from underwater.
        internal static (int X, int Y) PickDiveTile(string surfaceName, (byte Behaviour, int Collision, byte Elevation)[,] surface,
            string underwaterName, (byte Behaviour, int Collision, byte Elevation)[,] underwater)
        {
            for (int y = 0; y < surface.GetLength(0); y++)
            {
                for (int x = 0; x < surface.GetLength(1); x++)
                {
                    if (Diveable.Contains(surface[y, x].Behaviour) && surface[y, x].Collision == 0
                        && underwater[y, x].Collision == 0 && !NotSurfacable.Contains(underwater[y, x].Behaviour))
                    {
                        return (x, y);
                    }
                }
            }
            throw new InvalidOperationException($"No Dive access tile between {surfaceName} and {underwaterName}.");
        }

        internal Map BuildMap(JObject json)
        {
            var cells = DecodeCells(json);
            int height = cells.GetLength(0);
            int width = cells.GetLength(1);
            var map = new Map(height, width) { Elevation = new byte[height][] };
            for (int y = 0; y < height; y++)
            {
                map.Elevation[y] = new byte[width];
                for (int x = 0; x < width; x++)
                {
                    map[y, x] = KindOf(cells[y, x].Behaviour, cells[y, x].Collision);
                    map.Elevation[y][x] = cells[y, x].Elevation;
                }
            }

            var boulders = new List<BoulderData>();
            foreach (var o in Entries(json, "object_events"))
            {
                int x = (int)o["x"]!;
                int y = (int)o["y"]!;
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    continue;
                }
                switch ((string)o["graphics_id"]!)
                {
                    case "OBJ_EVENT_GFX_CUTTABLE_TREE":
                        map[y, x] = Constant.MAP_Cut;
                        continue;
                    case "OBJ_EVENT_GFX_BREAKABLE_ROCK":
                        map[y, x] = Constant.MAP_RockSmash;
                        continue;
                    case "OBJ_EVENT_GFX_PUSHABLE_BOULDER":
                        boulders.Add(new BoulderData { X = x, Y = y });
                        continue;
                }
                string flag = (string)o["flag"]!;
                if (flag != "0")
                {
                    map[y, x] = Constant.MAP_FlaggedNpc;
                    map.Events.Objects.Add(new MapObject(x, y, Resolve(flagDefines, flag)));
                }
                else
                {
                    map[y, x] = Constant.MAP_Blocked;
                }
                string movement = (string)o["movement_type"]!;
                int rangeX = (int)o["movement_range_x"]!;
                int rangeY = (int)o["movement_range_y"]!;
                if ((movement.StartsWith("MOVEMENT_TYPE_WANDER") || movement.StartsWith("MOVEMENT_TYPE_WALK")) && (rangeX != 0 || rangeY != 0))
                {
                    for (int yy = Math.Max(0, y - rangeY); yy <= Math.Min(height - 1, y + rangeY); yy++)
                    {
                        for (int xx = Math.Max(0, x - rangeX); xx <= Math.Min(width - 1, x + rangeX); xx++)
                        {
                            if (map[yy, xx] is Constant.MAP_Free or Constant.MAP_Grass)
                            {
                                map[yy, xx] = Constant.MAP_WalkingTrainer;
                            }
                        }
                    }
                }
            }
            map.Boulders = boulders.ToArray();

            foreach (var c in Entries(json, "coord_events"))
            {
                int x = (int)c["x"]!;
                int y = (int)c["y"]!;
                if ((string)c["type"]! != "trigger" || map[y, x] is not (Constant.MAP_Free or Constant.MAP_Grass or Constant.MAP_WalkingTrainer))
                {
                    continue;
                }
                string variable = (string)c["var"]!;
                map[y, x] = Constant.MAP_ScriptTrigger;
                map.Events.Triggers.Add(new MapTrigger(x, y, variable == "0" ? null : Resolve(varDefines, variable), ParseNumber((string)c["var_value"]!)));
            }

            foreach (var w in Entries(json, "warp_events"))
            {
                if (WhyDropped(json, w, cells) is not null)
                {
                    continue;
                }
                int x = (int)w["x"]!;
                int y = (int)w["y"]!;
                int destination = idByConstant[(string)w["dest_map"]!];
                var to = Landing(destination, w);
                int toX = (int)to["x"]!;
                int toY = (int)to["y"]!;
                var (dx, dy) = ArrivalStep(cells[y, x].Behaviour, DecodeCells(mapsById[destination])[toY, toX].Behaviour);
                map[y, x] = Constant.MAP_Portal1;
                map.Events.Warps.Add(new MapWarp(x, y, destination, toX + dx, toY + dy, WarpArrow(cells[y, x].Behaviour)!.Value));
            }

            foreach (var c in Entries(json, "connections"))
            {
                Direction? direction = (string)c["direction"]! switch
                {
                    "up" => Direction.Up,
                    "down" => Direction.Down,
                    "left" => Direction.Left,
                    "right" => Direction.Right,
                    _ => null, // dive and emerge become the Dive pair in Build
                };
                if (direction is not null && idByConstant.TryGetValue((string)c["map"]!, out int neighbour))
                {
                    map.Events.Connections.Add(new MapConnection(direction.Value, (int)c["offset"]!, neighbour));
                }
            }
            return map;
        }

        internal Dictionary<int, Map> Build()
        {
            var maps = mapsById.ToDictionary(pair => pair.Key, pair => BuildMap(pair.Value));
            foreach (var (id, json) in mapsById)
            {
                foreach (var c in Entries(json, "connections"))
                {
                    if ((string)c["direction"]! != "dive" || !idByConstant.TryGetValue((string)c["map"]!, out int underwater))
                    {
                        continue;
                    }
                    var (x, y) = PickDiveTile((string)json["name"]!, DecodeCells(json), (string)mapsById[underwater]["name"]!, DecodeCells(mapsById[underwater]));
                    maps[id].Events.Dive.Add(new MapDive(x, y, underwater));
                    maps[underwater].Events.Dive.Add(new MapDive(x, y, id));
                }
            }
            return maps;
        }

        public void Generate()
        {
            var stopwatch = Stopwatch.StartNew();
            var maps = Build();
            Directory.CreateDirectory(outputDir);

            var raw = new MemoryStream();
            using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(maps.Count);
                foreach (var (id, map) in maps)
                {
                    writer.Write(id);
                    writer.Write(map.Height);
                    writer.Write(map.Width);
                    foreach (var row in map.Tiles)
                    {
                        writer.Write(row);
                    }
                    foreach (var row in map.Elevation!)
                    {
                        writer.Write(row);
                    }
                }
            }
            byte[] mapsBin = Gzip(raw.ToArray());
            File.WriteAllBytes(Path.Combine(outputDir, "maps.bin"), mapsBin);
            byte[] events = Gzip(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(maps.ToDictionary(pair => pair.Key, pair => pair.Value.Events))));
            File.WriteAllBytes(Path.Combine(outputDir, "events.json.gz"), events);
            string boulders = JsonConvert.SerializeObject(maps.Where(pair => pair.Value.Boulders.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value.Boulders));
            File.WriteAllText(Path.Combine(outputDir, "boulders.json"), boulders);
            string lookup = JsonConvert.SerializeObject(mapsById.ToDictionary(pair => pair.Key, pair => new MapLookup { BotMapId = pair.Key, InternalName = (string)pair.Value["name"]! }));
            File.WriteAllText(Path.Combine(outputDir, "mapreverselookup.json"), lookup);

            var all = maps.Values.Select(map => map.Events).ToList();
            int warps = all.Sum(e => e.Warps.Count);
            var dropped = mapsById.Values.SelectMany(json =>
                {
                    var cells = DecodeCells(json);
                    return Entries(json, "warp_events").Select(w => WhyDropped(json, w, cells));
                })
                .OfType<string>().GroupBy(reason => reason).ToList();
            Console.WriteLine($"Maps: {maps.Count} built, {maps.Values.Sum(map => map.Height * map.Width)} tiles");
            Console.WriteLine($"maps.bin: {raw.Length} bytes raw, {mapsBin.Length} gzipped");
            Console.WriteLine($"Flagged objects: {all.Sum(e => e.Objects.Count)}, {all.Sum(e => e.Objects.Count(o => o.Flag is null))} unresolved");
            Console.WriteLine($"Triggers: {all.Sum(e => e.Triggers.Count)}, {all.Sum(e => e.Triggers.Count(t => t.Var is null))} unresolved");
            Console.WriteLine($"Warps: {warps}, {dropped.Sum(group => group.Count())} dropped ({string.Join(", ", dropped.Select(group => $"{group.Count()} {group.Key}"))})");
            Console.WriteLine($"Connections: {all.Sum(e => e.Connections.Count)}");
            // Each pair is listed on both maps; print it once, from the map with the lower id.
            var divePairs = maps.SelectMany(pair => pair.Value.Events.Dive.Where(d => d.Map > pair.Key)
                .Select(d => $"{mapsById[pair.Key]["name"]} and {mapsById[d.Map]["name"]} at ({d.X}, {d.Y})")).ToList();
            Console.WriteLine($"Dive pairs: {divePairs.Count}: {string.Join("; ", divePairs)}");
            Console.WriteLine($"Boulders: {maps.Values.Sum(map => map.Boulders.Length)} on {maps.Values.Count(map => map.Boulders.Length > 0)} maps");
            Console.WriteLine($"events.json.gz: {events.Length} bytes, boulders.json: {boulders.Length} bytes, mapreverselookup.json: {lookup.Length} bytes");
            Console.WriteLine($"Data/RS: {mapsBin.Length + events.Length + boulders.Length + lookup.Length} bytes in all");
            Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F1} s");
        }

        // Null for a warp the side file keeps; otherwise why it is dropped. Warps to MAP_DYNAMIC (secret bases, the
        // truck, the elevator) have no destination, and a few warps sit, or land, outside their map's layout. A warp
        // event on a tile the game does not warp from (WarpArrow) is only ever a landing, for a script or another warp.
        private string? WhyDropped(JObject json, JToken warp, (byte Behaviour, int Collision, byte Elevation)[,] cells)
        {
            if (!idByConstant.TryGetValue((string)warp["dest_map"]!, out int destination))
            {
                return "to an unknown map";
            }
            int x = (int)warp["x"]!;
            int y = (int)warp["y"]!;
            if (!OnLayout(json, x, y))
            {
                return "off their own map";
            }
            var to = Landing(destination, warp);
            if (!OnLayout(mapsById[destination], (int)to["x"]!, (int)to["y"]!))
            {
                return "landing off their destination";
            }
            return WarpArrow(cells[y, x].Behaviour) is null ? "on a tile the game does not warp from" : null;
        }

        // The destination map's warp event that the warp lands on.
        private JToken Landing(int destination, JToken warp)
            => mapsById[destination]["warp_events"]![ParseNumber((string)warp["dest_warp_id"]!)]!;

        private bool OnLayout(JObject json, int x, int y)
        {
            var layout = layouts[(string)json["layout"]!];
            return x >= 0 && x < (int)layout["width"]! && y >= 0 && y < (int)layout["height"]!;
        }

        // Per tile of the map's layout: the behaviour from the tileset attributes, and the collision and elevation bits.
        private (byte Behaviour, int Collision, byte Elevation)[,] DecodeCells(JObject json)
        {
            var layout = layouts[(string)json["layout"]!];
            int width = (int)layout["width"]!;
            int height = (int)layout["height"]!;
            byte[] blocks = File.ReadAllBytes(Path.Combine(checkoutDir, (string)layout["blockdata_filepath"]!));
            byte[] primary = Attributes((string)layout["primary_tileset"]!);
            byte[] secondary = Attributes((string)layout["secondary_tileset"]!);
            var cells = new (byte, int, byte)[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort cell = BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan((y * width + x) * 2));
                    int metatile = cell & 0x3FF;
                    byte behaviour;
                    if (metatile < 0x200)
                    {
                        if (metatile * 2 >= primary.Length)
                        {
                            throw new InvalidOperationException($"{json["name"]}: metatile {metatile} at ({x}, {y}) is past the primary attribute table.");
                        }
                        behaviour = primary[metatile * 2];
                    }
                    else
                    {
                        // Past the end of the secondary table reads 0, as the spike did.
                        int index = (metatile - 0x200) * 2;
                        behaviour = index < secondary.Length ? secondary[index] : (byte)0;
                    }
                    cells[y, x] = (behaviour, (cell >> 10) & 3, (byte)(cell >> 12));
                }
            }
            return cells;
        }

        private byte[] Attributes(string tileset)
        {
            if (!attributes.TryGetValue(tileset, out var table))
            {
                table = File.ReadAllBytes(Path.Combine(checkoutDir, attributeFileByTileset[tileset]));
                attributes.Add(tileset, table);
            }
            return table;
        }

        private static IEnumerable<JToken> Entries(JObject json, string key) => (IEnumerable<JToken>?)(json[key] as JArray) ?? [];

        // For each "Label::" in an assembler file, the first capture of the pattern in the lines that follow it.
        private static Dictionary<string, string> LabelValues(string path, Regex pattern)
        {
            var values = new Dictionary<string, string>();
            string? label = null;
            foreach (string line in File.ReadLines(path))
            {
                var labelMatch = Regex.Match(line, @"^(\w+)::");
                if (labelMatch.Success)
                {
                    label = labelMatch.Groups[1].Value;
                    continue;
                }
                var match = pattern.Match(line);
                if (label is not null && match.Success && !values.ContainsKey(label))
                {
                    values.Add(label, match.Groups[1].Value);
                }
            }
            return values;
        }

        // "#define NAME value" lines, comments stripped.
        private static Dictionary<string, string> Defines(string path)
        {
            var defines = new Dictionary<string, string>();
            foreach (string line in File.ReadLines(path))
            {
                var match = Regex.Match(line, @"^#define\s+(\w+)\s+(.+?)\s*(//.*)?$");
                if (match.Success)
                {
                    defines[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }
            return defines;
        }

        // A number, another name, or (NAME + number), followed recursively; anything else does not resolve.
        private static int? Resolve(Dictionary<string, string> defines, string name)
        {
            if (!defines.TryGetValue(name, out string? value))
            {
                return null;
            }
            if (Regex.IsMatch(value, @"^(0x[0-9A-Fa-f]+|\d+)$"))
            {
                return ParseNumber(value);
            }
            if (Regex.IsMatch(value, @"^\w+$"))
            {
                return Resolve(defines, value);
            }
            var sum = Regex.Match(value, @"^\(\s*(\w+)\s*\+\s*(0x[0-9A-Fa-f]+|\d+)\s*\)$");
            return sum.Success ? Resolve(defines, sum.Groups[1].Value) + ParseNumber(sum.Groups[2].Value) : null;
        }

        private static int ParseNumber(string value)
            => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(value, 16) : int.Parse(value);

        private static byte[] Gzip(byte[] raw)
        {
            var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(raw);
            }
            return compressed.ToArray();
        }
    }
}
