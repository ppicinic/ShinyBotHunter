using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Service;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

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

            var file = Versions.ToDictionary(v => v, v => groups.Select(g => new Milestone(
                g.Select(e => e.Header.Id).ToArray(), g[0].Modality, g.Any(e => e.HasGrass), g[0].Slots[v], [])).ToList());
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
