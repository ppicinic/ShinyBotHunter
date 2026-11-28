using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Route = ShinyBotHunter.Pathfinding.Route;

namespace ShinyBotHunter.Scripts.Milestones
{
    // Writes Data/RS/milestones.json.gz from a pret/pokeruby checkout: per version, one milestone per map and modality,
    // where maps of one region map section whose slots for a modality are identical in both versions are one milestone.
    // Weights are from src/wild_encounter.c at 5784633. Feebas and mass outbreaks are not table slots and are not read.
    public class RsMilestoneProcessor
    {
        private static readonly int[] LandWeights = [20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1];
        private static readonly int[] SurfWeights = [60, 30, 5, 4, 1];
        // fishing_mons holds the three rods in one table: Old Rod at 0-1, Good Rod at 2-4, Super Rod at 5-9.
        private static readonly int[] FishingWeights = [70, 30, 60, 20, 20, 40, 40, 15, 4, 1];
        // The four Safari Zone maps, the spec's non-goal.
        private static readonly HashSet<int> Dropped = [6656, 6657, 6658, 6659];
        private static readonly RomVersion[] Versions = [RomVersion.Ruby, RomVersion.Sapphire];

        private record Header(int Id, string Internal, string Mapsec, string MapType);

        // One map's milestone before merging.
        private record Entry(Header Header, Modality Modality, bool HasGrass, Dictionary<RomVersion, Slot[]> Slots);

        private readonly string decompDir;
        private readonly string outputDir;
        private readonly Dictionary<string, int> nationalDex;

        public RsMilestoneProcessor(string decompDir, string outputDir)
        {
            this.decompDir = decompDir;
            this.outputDir = outputDir;
            nationalDex = File.ReadLines(Path.Combine(decompDir, "include", "constants", "species.h"))
                .Select(line => Regex.Match(line, @"^#define\s+NATIONAL_DEX_(\w+)\s+(\d+)"))
                .Where(match => match.Success)
                .ToDictionary(match => match.Groups[1].Value, match => int.Parse(match.Groups[2].Value));
        }

        public void Generate()
        {
            var headers = ReadHeaders();
            // Each map has one entry per version, its base_label ending in _Ruby or _Sapphire.
            var tables = JObject.Parse(File.ReadAllText(Path.Combine(decompDir, "src", "data", "wild_encounters.json")))["wild_encounter_groups"]![0]!["encounters"]!
                .Cast<JObject>()
                .GroupBy(e => (string)e["map"]!)
                .ToDictionary(g => g.Key, g => g.ToDictionary(e => ((string)e["base_label"]!).EndsWith("_Ruby") ? RomVersion.Ruby : RomVersion.Sapphire));

            var entries = new List<Entry>();
            foreach (var (header, table) in tables.Select(t => (Header: headers[t.Key], Table: t.Value)).Where(t => !Dropped.Contains(t.Header.Id)).OrderBy(t => t.Header.Id))
            {
                if (table[RomVersion.Ruby]["land_mons"] is not null)
                {
                    // Underground and indoor maps are "cave"; every other type is "grass".
                    bool hasGrass = header.MapType is not ("MAP_TYPE_UNDERGROUND" or "MAP_TYPE_INDOOR");
                    entries.Add(Make(header, Modality.Land, hasGrass, v => Slots(table[v]["land_mons"]!, LandWeights, 0, LandWeights.Length)));
                }
                if (table[RomVersion.Ruby]["water_mons"] is not null)
                {
                    entries.Add(Make(header, Modality.Surf, false, v => Slots(table[v]["water_mons"]!, SurfWeights, 0, SurfWeights.Length)));
                }
                if (table[RomVersion.Ruby]["rock_smash_mons"] is not null)
                {
                    entries.Add(Make(header, Modality.RockSmash, false, v => Slots(table[v]["rock_smash_mons"]!, SurfWeights, 0, SurfWeights.Length)));
                }
                if (table[RomVersion.Ruby]["fishing_mons"] is not null)
                {
                    foreach (var (modality, start, count) in new[] { (Modality.OldRod, 0, 2), (Modality.GoodRod, 2, 3), (Modality.SuperRod, 5, 5) })
                    {
                        entries.Add(Make(header, modality, false, v => Slots(table[v]["fishing_mons"]!, FishingWeights, start, count)));
                    }
                }
            }

            // Entries are in map id order, so each group's first entry is its lowest map.
            var groups = new List<List<Entry>>();
            foreach (var entry in entries)
            {
                var group = groups.FirstOrDefault(g =>
                    g[0].Modality == entry.Modality && g[0].Header.Mapsec == entry.Header.Mapsec
                    && Versions.All(v => g[0].Slots[v].SequenceEqual(entry.Slots[v])));
                if (group is null)
                {
                    groups.Add([entry]);
                }
                else
                {
                    group.Add(entry);
                }
            }
            groups = groups.OrderBy(g => g[0].Header.Id).ThenBy(g => g[0].Modality).ToList();

            Console.WriteLine("Anchors:");
            var total = Stopwatch.StartNew();
            var maps = new MapService(outputDir, withElevationAndEvents: true);
            var search = new RouteSearch(maps, new RsPointerService(), GameFlavor.RS);
            var anchors = new Dictionary<List<Entry>, Anchor?>();
            foreach (var group in groups.Where(g => g[0].Modality == Modality.Land))
            {
                anchors[group] = FindAnchor(group, maps, search);
            }
            var unanchored = anchors.Where(a => a.Value is null).Select(a => $"{a.Key[0].Header.Id}-land {a.Key[0].Header.Internal}").ToList();
            Console.WriteLine($"Anchors: {anchors.Count - unanchored.Count} spin, {unanchored.Count} none, in {total.Elapsed.TotalSeconds:F1} s");
            Console.WriteLine($"No anchor: {string.Join(", ", unanchored)}");

            var file = Versions.ToDictionary(v => v, v => groups.Select(g => new Milestone(
                g.Select(e => e.Header.Id).ToArray(), g[0].Modality, g.Any(e => e.HasGrass), g[0].Slots[v], [], anchors.GetValueOrDefault(g))).ToList());
            byte[] raw = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(file, new StringEnumConverter()));
            var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(raw);
            }
            Directory.CreateDirectory(outputDir);
            File.WriteAllBytes(Path.Combine(outputDir, "milestones.json.gz"), compressed.ToArray());

            foreach (var (version, milestones) in file)
            {
                var counts = milestones.GroupBy(m => m.Modality).Select(m => $"{m.Key} {m.Count()}");
                Console.WriteLine($"{version}: {milestones.Count} milestones ({string.Join(", ", counts)})");
            }
            Console.WriteLine("Merged:");
            foreach (var group in groups.Where(g => g.Count > 1))
            {
                var first = group[0];
                int inLocation = entries.Count(e => e.Modality == first.Modality && e.Header.Mapsec == first.Header.Mapsec);
                Console.WriteLine($"  {first.Header.Id}-{first.Modality.ToString().ToLowerInvariant()}: {string.Join(", ", group.Select(e => $"{e.Header.Id} {e.Header.Internal}"))}"
                    + $" ({group.Count} of {inLocation} maps of {first.Header.Mapsec} with {first.Modality})");
            }
            Console.WriteLine("Land:");
            foreach (var group in groups.Where(g => g[0].Modality == Modality.Land))
            {
                Console.WriteLine($"  {group[0].Header.Id}-land {group[0].Header.Internal}: {string.Join(", ", group.Select(e => e.Header.MapType).Distinct())}, {(group[0].HasGrass ? "grass" : "cave")}");
            }
            Console.WriteLine($"milestones.json.gz: {raw.Length} bytes raw, {compressed.Length} gzipped");
        }

        // A land milestone's anchor (FR-19a): a spin on the land-encounter tile of its maps, out of every trainer's sight,
        // that is cheapest on foot to a Pokémon Center's counter and is reached back from it, ties to the lowest map and
        // then row-major. Searched with zeroed flags and variables and no HM: the RS navigator walks no HM route. Signs
        // are not excluded: on RS a sign needs an A press, and the spin presses none.
        private Anchor? FindAnchor(List<Entry> group, MapService maps, RouteSearch search)
        {
            var watch = Stopwatch.StartNew();
            byte[] flags = new byte[0x120], vars = new byte[0x200];
            var reached = new List<(int Map, int X, int Y, Route Route)>();
            foreach (var entry in group)
            {
                var map = maps.GetMap(entry.Header.Id, true);
                var sight = TrainerSight(JObject.Parse(File.ReadAllText(Path.Combine(decompDir, "data", "maps", entry.Header.Internal, "map.json"))));
                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        if (map[y, x] != Constant.MAP_Grass || sight.Contains((x, y)))
                        {
                            continue;
                        }
                        var route = search.Find(entry.Header.Id, x, y, Elevation(maps, entry.Header.Id, x, y), IsCounter(maps), flags, vars, new Capability());
                        if (route is not null)
                        {
                            reached.Add((entry.Header.Id, x, y, route));
                        }
                    }
                }
            }

            // OrderBy is stable, so equal costs keep the candidate order.
            foreach (var (mapId, x, y, route) in reached.OrderBy(r => r.Route.Cost))
            {
                var counter = route.Steps[^1];
                if (search.Find(counter.Map, counter.X, counter.Y, Elevation(maps, counter.Map, counter.X, counter.Y), (m, gx, gy) => m == mapId && gx == x && gy == y, flags, vars, new Capability()) is null)
                {
                    continue;
                }
                string crossed = route.LiveTriggers.Count == 0 ? "none" : string.Join(", ", route.LiveTriggers.Select(t => $"{maps.GetMapLookup(t.Map).InternalName} ({t.X}, {t.Y})"));
                Console.WriteLine($"  {group[0].Header.Id}-land {group[0].Header.Internal}: {maps.GetMapLookup(mapId).InternalName} ({x}, {y}), cost {route.Cost}, "
                    + $"to {maps.GetMapLookup(counter.Map).InternalName}, live triggers crossed: {crossed}; {watch.ElapsedMilliseconds} ms");
                return new Anchor(mapId, x, y, 0, 0);
            }
            Console.WriteLine($"  {group[0].Header.Id}-land {group[0].Header.Internal}: no anchor ({reached.Count} tiles reach a Center); {watch.ElapsedMilliseconds} ms");
            return null;
        }

        // The tile's elevation as the navigator starts a search from it: 15, any elevation, is 0.
        private static int Elevation(MapService maps, int map, int x, int y)
        {
            byte elevation = maps.GetMap(map, true).ElevationAt(y, x);
            return elevation == 15 ? 0 : elevation;
        }

        // The navigator's goal: the tile below a Pokémon Center's counter.
        private static Func<int, int, int, bool> IsCounter(MapService maps)
            => (map, x, y) => x == 7 && y == 4 && maps.GetMapLookup(map).InternalName.EndsWith("_PokemonCenter_1F");

        // The tiles some trainer of the map's object_events sees (src/trainer_see.c): a normal trainer facing one way
        // (MOVEMENT_TYPE_FACE_UP, _DOWN, _LEFT or _RIGHT) along its facing, every other trainer, buried ones too, in all
        // four directions from every tile of its movement range, since it turns or walks. trainer_sight_or_berry_tree_id
        // tiles in a straight line. Beaten trainers count and walls do not block sight.
        internal static HashSet<(int X, int Y)> TrainerSight(JObject map)
        {
            var tiles = new HashSet<(int X, int Y)>();
            foreach (var trainer in map["object_events"]!.Where(o => (string)o["trainer_type"]! != "TRAINER_TYPE_NONE"))
            {
                int x = (int)trainer["x"]!, y = (int)trainer["y"]!, sight = int.Parse((string)trainer["trainer_sight_or_berry_tree_id"]!);
                (int X, int Y)? facing = ((string)trainer["trainer_type"]!, (string)trainer["movement_type"]!) switch
                {
                    ("TRAINER_TYPE_NORMAL", "MOVEMENT_TYPE_FACE_UP") => (0, -1),
                    ("TRAINER_TYPE_NORMAL", "MOVEMENT_TYPE_FACE_DOWN") => (0, 1),
                    ("TRAINER_TYPE_NORMAL", "MOVEMENT_TYPE_FACE_LEFT") => (-1, 0),
                    ("TRAINER_TYPE_NORMAL", "MOVEMENT_TYPE_FACE_RIGHT") => (1, 0),
                    _ => null,
                };
                (int X, int Y)[] directions = facing is { } f ? [f] : [(0, -1), (0, 1), (-1, 0), (1, 0)];
                int rangeX = facing is null ? (int)trainer["movement_range_x"]! : 0, rangeY = facing is null ? (int)trainer["movement_range_y"]! : 0;
                for (int dy = -rangeY; dy <= rangeY; dy++)
                {
                    for (int dx = -rangeX; dx <= rangeX; dx++)
                    {
                        foreach (var (stepX, stepY) in directions)
                        {
                            for (int step = 1; step <= sight; step++)
                            {
                                tiles.Add((x + dx + stepX * step, y + dy + stepY * step));
                            }
                        }
                    }
                }
            }
            return tiles;
        }

        private static Entry Make(Header header, Modality modality, bool hasGrass, Func<RomVersion, Slot[]> slots)
            => new(header, modality, hasGrass, Versions.ToDictionary(v => v, slots));

        // Every map, keyed by its MAP_* constant. The id is the group's index in group_order x 256 plus the position in
        // the group, as RsMapProcessor numbers the committed map data.
        private Dictionary<string, Header> ReadHeaders()
        {
            var groups = JObject.Parse(File.ReadAllText(Path.Combine(decompDir, "data", "maps", "map_groups.json")));
            var headers = new Dictionary<string, Header>();
            int groupIndex = 0;
            foreach (var group in groups["group_order"]!)
            {
                int position = 0;
                foreach (var name in groups[(string)group!]!)
                {
                    var map = JObject.Parse(File.ReadAllText(Path.Combine(decompDir, "data", "maps", (string)name!, "map.json")));
                    headers.Add((string)map["id"]!, new Header(groupIndex * 256 + position, (string)name!, (string)map["region_map_section"]!, (string)map["map_type"]!));
                    position++;
                }
                groupIndex++;
            }
            return headers;
        }

        // The slots at [start, start + count) of a table, each indexed by its place in the game's table.
        private Slot[] Slots(JToken table, int[] weights, int start, int count)
        {
            var mons = table["mons"]!;
            return Enumerable.Range(start, count).Select(i => new Slot(i,
                nationalDex[((string)mons[i]!["species"]!)["SPECIES_".Length..]],
                (int)mons[i]!["min_level"]!, (int)mons[i]!["max_level"]!, weights[i], SlotCondition.None)).ToArray();
        }
    }
}
