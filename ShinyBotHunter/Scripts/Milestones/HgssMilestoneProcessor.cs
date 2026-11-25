using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Service;
using ShinyBotHunter.Util;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ShinyBotHunter.Scripts.Milestones
{
    // Writes Data/HGSS/milestones.json.gz from a pret/pokeheartgold checkout: per version, one milestone per map and
    // modality, where maps of one location (mapsec) whose slots for a modality are identical in both versions are one
    // milestone. Headbutt never merges: a tree's type is its position in its own map's list. Weights and the slot
    // rules are from src/field/encounter_check.c at 8946577; the swarm species are never read.
    public class HgssMilestoneProcessor
    {
        private static readonly int[] LandWeights = [20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1];
        private static readonly int[] SurfWeights = [60, 30, 5, 4, 1];
        private static readonly int[] RodWeights = [40, 30, 15, 10, 5];
        private static readonly int[] RockSmashWeights = [80, 20];
        private static readonly int[] HeadbuttWeights = [50, 15, 15, 10, 5, 5];
        // The Safari Zone entrance and the Bug-Catching Contest, the spec's non-goals.
        private static readonly HashSet<int> Dropped = [357, 487];
        private static readonly RomVersion[] Versions = [RomVersion.HeartGold, RomVersion.SoulSilver];
        private const int CellSize = 32;

        // Bank is the encounter table's name in gs_enc_data.json, null for ENCDATA_NA. Overworld: on matrix 0.
        private record Header(int Id, string Internal, string? Bank, string Mapsec, bool Overworld);

        // One map's milestone before merging.
        private record Entry(Header Header, Modality Modality, bool HasGrass, Dictionary<RomVersion, Slot[]> Slots, HeadbuttTree[] Trees);

        private readonly string decompDir;
        private readonly string outputDir;
        private readonly Dictionary<string, int> species;

        public HgssMilestoneProcessor(string decompDir, string outputDir)
        {
            this.decompDir = decompDir;
            this.outputDir = outputDir;
            species = File.ReadLines(Path.Combine(decompDir, "include", "constants", "species.h"))
                .Select(line => Regex.Match(line, @"^#define\s+(SPECIES_\w+)\s+(\d+)"))
                .Where(match => match.Success)
                .ToDictionary(match => match.Groups[1].Value, match => int.Parse(match.Groups[2].Value));
        }

        public void Generate()
        {
            var encounters = JObject.Parse(File.ReadAllText(Path.Combine(decompDir, "files", "fielddata", "encountdata", "gs_enc_data.json")))["encounters"]!
                .Cast<JObject>().ToDictionary(e => (string)e["map"]!);
            var headbutt = JObject.Parse(File.ReadAllText(Path.Combine(decompDir, "files", "arc", "headbutt.json")))["tables"]!
                .Cast<JObject>().ToDictionary(t => (string)t["Map"]!);
            var maps = new MapService(outputDir);
            var owners = ReadOverworldOwners();

            var entries = new List<Entry>();
            foreach (var header in ReadHeaders().Where(h => !Dropped.Contains(h.Id)))
            {
                if (header.Bank is not null)
                {
                    var table = encounters[header.Bank];
                    if ((int)table["land"]!["rate"]! > 0)
                    {
                        entries.Add(Make(header, Modality.Land, HasGrass(maps, owners, header), v => LandSlots(table, v)));
                    }
                    if ((int)table["surf"]!["rate"]! > 0)
                    {
                        entries.Add(Make(header, Modality.Surf, false, v => RangeSlots(table["surf"]!, SurfWeights, v)));
                    }
                    foreach (var (key, modality, nightIndex) in new[] { ("old_rod", Modality.OldRod, -1), ("good_rod", Modality.GoodRod, 3), ("super_rod", Modality.SuperRod, 1) })
                    {
                        var rod = table["fishing"]![key]!;
                        if ((int)rod["rate"]! > 0)
                        {
                            entries.Add(Make(header, modality, false, v => RodSlots(table, rod, nightIndex, v)));
                        }
                    }
                    if ((int)table["rock_smash"]!["rate"]! > 0)
                    {
                        entries.Add(Make(header, Modality.RockSmash, false, v => RangeSlots(table["rock_smash"]!, RockSmashWeights, v)));
                    }
                }
                var trees = headbutt[header.Internal];
                if (new[] { "CommonMons", "RareMons", "SecretMons" }.Any(key => trees[key]!.HasValues))
                {
                    entries.Add(Make(header, Modality.Headbutt, false, v => HeadbuttSlots(trees, v)) with { Trees = Trees(trees) });
                }
            }

            // Entries are in map id order, so each group's first entry is its lowest map.
            var groups = new List<List<Entry>>();
            foreach (var entry in entries)
            {
                var group = entry.Modality == Modality.Headbutt ? null : groups.FirstOrDefault(g =>
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
                g.Select(e => e.Header.Id).ToArray(), g[0].Modality, g.Any(e => e.HasGrass), g[0].Slots[v], g[0].Trees)).ToList());
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
                string grass = group.Select(e => e.HasGrass).Distinct().Count() > 1 ? ", grass on some maps only" : "";
                Console.WriteLine($"  {first.Header.Id}-{first.Modality.ToString().ToLowerInvariant()}: {string.Join(", ", group.Select(e => $"{e.Header.Id} {e.Header.Internal}"))}"
                    + $" ({group.Count} of {inLocation} maps of {first.Header.Mapsec} with {first.Modality}{grass})");
            }
            var caves = groups.Select(g => g[0])
                .Where(e => e.Modality == Modality.Land && !e.HasGrass && (e.Header.Overworld || e.Header.Mapsec.Contains("FOREST")))
                .Select(e => $"{e.Header.Id} {e.Header.Internal}");
            Console.WriteLine($"Land without grass on an overworld or forest map: {string.Join(", ", caves)}");
            Console.WriteLine($"milestones.json.gz: {raw.Length} bytes raw, {compressed.Length} gzipped");
        }

        private static Entry Make(Header header, Modality modality, bool hasGrass, Func<RomVersion, Slot[]> slots)
            => new(header, modality, hasGrass, Versions.ToDictionary(v => v, slots), []);

        // Every header in src/data/map_headers.h, with its id and internal name from include/constants/maps.h.
        private List<Header> ReadHeaders()
        {
            var ids = new Dictionary<string, (int Id, string Internal)>();
            foreach (string line in File.ReadLines(Path.Combine(decompDir, "include", "constants", "maps.h")))
            {
                var match = Regex.Match(line, @"^#define\s+(MAP_\w+)\s+(\d+)\s*//\s*MAP_(\w+)");
                if (match.Success)
                {
                    ids.Add(match.Groups[1].Value, (int.Parse(match.Groups[2].Value), match.Groups[3].Value));
                }
            }
            var headers = new List<Header>();
            string? constant = null;
            var fields = new Dictionary<string, string>();
            foreach (string line in File.ReadLines(Path.Combine(decompDir, "src", "data", "map_headers.h")).Append("    [END] ="))
            {
                var start = Regex.Match(line, @"^\s*\[(\w+)\]\s*=");
                if (start.Success)
                {
                    if (constant is not null)
                    {
                        var (id, name) = ids[constant];
                        string bank = fields["wildEncounterBank"];
                        headers.Add(new Header(id, name, bank == "ENCDATA_NA" ? null : bank["ENCDATA_".Length..], fields["mapsec"],
                            fields["matrixId"] == "NARC_map_matrix_map_matrix_0000_EVERYWHERE_bin"));
                    }
                    constant = start.Groups[1].Value;
                    fields.Clear();
                    continue;
                }
                var field = Regex.Match(line, @"^\s*\.(\w+)\s*=\s*([^,]+),");
                if (field.Success)
                {
                    fields[field.Groups[1].Value] = field.Groups[2].Value.Trim();
                }
            }
            return headers.OrderBy(h => h.Id).ToList();
        }

        // The header layer of matrix 0: width, height, has-headers, has-altitudes, name length, name, then a u16 header
        // id per 32 x 32 cell, row by row. The bot's overworld tile grid uses the same coordinates.
        private ushort[,] ReadOverworldOwners()
        {
            using var reader = new BinaryReader(File.OpenRead(Path.Combine(decompDir, "files", "fielddata", "mapmatrix", "map_matrix", "map_matrix_0000_EVERYWHERE.bin")));
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            reader.ReadBytes(2);
            reader.ReadBytes(reader.ReadByte());
            var owners = new ushort[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    owners[y, x] = reader.ReadUInt16();
                }
            }
            return owners;
        }

        // Any grass tile among the tiles the header owns: its cells of the overworld, or the whole grid of its bot map.
        private static bool HasGrass(MapService maps, ushort[,] owners, Header header)
        {
            var map = maps.GetMap(header.Id);
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (map[y, x] == Constant.MAP_Grass && (!header.Overworld || owners[y / CellSize, x / CellSize] == header.Id))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // A value is a scalar or one per version: {HEARTGOLD, SOULSILVER} in the wild tables, {gold, silver} in headbutt.
        private static JToken Pick(JToken value, RomVersion version)
        {
            if (value is JObject pair && (pair["HEARTGOLD"] ?? pair["gold"]) is JToken gold)
            {
                return version == RomVersion.HeartGold ? gold : (pair["SOULSILVER"] ?? pair["silver"])!;
            }
            return value;
        }

        private int Species(JToken value, RomVersion version) => species[(string)Pick(value, version)!];

        // 12 slots per time of day with one level each, then the two sounds over slots 2 to 5 as [s0, s0, s1, s1].
        private Slot[] LandSlots(JObject table, RomVersion version)
        {
            var mons = table["land"]!["mons"]!;
            int Level(int index) => (int)Pick(mons[index]!["level"]!, version);
            var slots = new List<Slot>();
            foreach (var (key, condition) in new[] { ("morn", SlotCondition.Morning), ("day", SlotCondition.Day), ("nite", SlotCondition.Night) })
            {
                for (int i = 0; i < LandWeights.Length; i++)
                {
                    slots.Add(new Slot(i, Species(mons[i]!["species"]![key]!, version), Level(i), Level(i), LandWeights[i], condition));
                }
            }
            foreach (var (key, condition) in new[] { ("hoenn", SlotCondition.HoennSound), ("sinnoh", SlotCondition.SinnohSound) })
            {
                for (int i = 2; i <= 5; i++)
                {
                    slots.Add(new Slot(i, Species(table[key]![(i - 2) / 2]!, version), Level(i), Level(i), LandWeights[i], condition));
                }
            }
            return slots.ToArray();
        }

        private Slot[] RangeSlots(JToken modality, int[] weights, RomVersion version)
        {
            var mons = modality["mons"]!;
            return weights.Select((weight, i) =>
            {
                var level = Pick(mons[i]!["level"]!, version);
                return new Slot(i, Species(mons[i]!["species"]!, version), (int)Pick(level["min"]!, version), (int)Pick(level["max"]!, version), weight, SlotCondition.None);
            }).ToArray();
        }

        // At night the night-fishing species replaces the Good Rod's slot 3 and the Super Rod's slot 1.
        private Slot[] RodSlots(JObject table, JToken rod, int nightIndex, RomVersion version)
        {
            var slots = RangeSlots(rod, RodWeights, version);
            if (nightIndex < 0)
            {
                return slots;
            }
            var replaced = slots[nightIndex];
            return [.. slots, replaced with { Species = Species(table["nightFish"]!, version), Condition = SlotCondition.NightFishing }];
        }

        private Slot[] HeadbuttSlots(JObject trees, RomVersion version)
        {
            var slots = new List<Slot>();
            foreach (var (key, condition) in new[] { ("CommonMons", SlotCondition.CommonTree), ("RareMons", SlotCondition.RareTree), ("SecretMons", SlotCondition.SecretTree) })
            {
                var mons = trees[key]!;
                for (int i = 0; i < mons.Count(); i++)
                {
                    slots.Add(new Slot(i, Species(mons[i]!["species"]!, version), (int)mons[i]!["minLevel"]!, (int)mons[i]!["maxLevel"]!, HeadbuttWeights[i], condition));
                }
            }
            return slots.ToArray();
        }

        // The map's trees in list order, then its secret trees.
        private static HeadbuttTree[] Trees(JObject trees)
        {
            HeadbuttTree Tree(JToken tiles, bool secret) => new(secret, tiles.Select(t => new[] { (int)t["x"]!, (int)t["y"]! }).ToArray());
            return [.. trees["Trees"]!.Select(t => Tree(t, false)), .. trees["SecretTrees"]!.Select(t => Tree(t, true))];
        }
    }
}
