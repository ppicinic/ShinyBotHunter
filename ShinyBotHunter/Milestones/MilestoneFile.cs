using Newtonsoft.Json;
using ShinyBotHunter.Service;
using System.IO.Compression;

namespace ShinyBotHunter.Milestones
{
    // Reads a flavor's generated milestones.json.gz and names each milestone from map-names.json.
    public static class MilestoneFile
    {
        private record MapNames(Dictionary<int, string> Maps, Dictionary<string, string> Groups);

        public static IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Load(string directory)
        {
            using FileStream file = new FileStream(Path.Combine(directory, "milestones.json.gz"), FileMode.Open, FileAccess.Read);
            using GZipStream stream = new GZipStream(file, CompressionMode.Decompress);
            using StreamReader reader = new StreamReader(stream);
            var versions = JsonConvert.DeserializeObject<Dictionary<RomVersion, Milestone[]>>(reader.ReadToEnd())!;
            var names = JsonConvert.DeserializeObject<MapNames>(File.ReadAllText(Path.Combine(directory, "map-names.json")))!;

            return versions.ToDictionary(
                v => v.Key,
                v => (IReadOnlyList<Milestone>)v.Value.Select(m => m with { Name = $"{Place(m, names)} · {Word(m)}" }).ToList());
        }

        // A merged milestone is named only from Groups, so a missing group entry is an error, not its first floor's name.
        private static string Place(Milestone milestone, MapNames names)
        {
            if (names.Groups.TryGetValue(milestone.Id, out var group))
                return group;
            if (milestone.Maps.Length == 1 && names.Maps.TryGetValue(milestone.Maps[0], out var map))
                return map;
            throw new InvalidDataException($"map-names.json has no name for milestone {milestone.Id}");
        }

        internal static string Word(Milestone milestone) => milestone.Modality switch
        {
            Modality.Land => milestone.HasGrass ? "grass" : "cave",
            Modality.Surf => "surf",
            Modality.OldRod => "old rod",
            Modality.GoodRod => "good rod",
            Modality.SuperRod => "super rod",
            Modality.RockSmash => "rock smash",
            Modality.Headbutt => "headbutt",
            _ => throw new ArgumentOutOfRangeException(nameof(milestone), milestone.Modality, null)
        };
    }
}
