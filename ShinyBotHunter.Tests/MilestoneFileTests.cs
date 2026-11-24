using ShinyBotHunter.Milestones;
using ShinyBotHunter.Service;
using System.IO.Compression;

namespace ShinyBotHunter.Tests
{
    public class MilestoneFileTests
    {
        private const string Milestones = """
            { "HeartGold": [
                { "Maps": [34], "Modality": "Land", "HasGrass": true,
                  "Slots": [ { "Index": 0, "Species": 16, "MinLevel": 3, "MaxLevel": 3, "Weight": 20, "Condition": "Morning" } ],
                  "Trees": [] },
                { "Maps": [34], "Modality": "OldRod", "HasGrass": false,
                  "Slots": [ { "Index": 0, "Species": 129, "MinLevel": 10, "MaxLevel": 10, "Weight": 40, "Condition": "None" } ],
                  "Trees": [] },
                { "Maps": [176], "Modality": "Land", "HasGrass": false, "Slots": [], "Trees": [] },
                { "Maps": [332, 333, 341], "Modality": "Land", "HasGrass": false, "Slots": [], "Trees": [] }
            ] }
            """;

        private const string Names = """
            { "Maps": { "34": "Route 30", "176": "Dark Cave (Route 31 side)", "332": "Bell Tower 2F", "333": "Bell Tower 3F", "341": "Bell Tower 10F" },
              "Groups": { "332-land": "Bell Tower" } }
            """;

        private static string WriteFolder(string milestones, string names)
        {
            string directory = Path.Combine(Path.GetTempPath(), "milestones-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            using (var file = File.Create(Path.Combine(directory, "milestones.json.gz")))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var writer = new StreamWriter(gzip))
            {
                writer.Write(milestones);
            }
            File.WriteAllText(Path.Combine(directory, "map-names.json"), names);
            return directory;
        }

        [Fact]
        public void Load_NamesFromMapsAndGroups()
        {
            string directory = WriteFolder(Milestones, Names);
            try
            {
                var loaded = MilestoneFile.Load(directory);

                Assert.Equal(
                    ["Route 30 · grass", "Route 30 · old rod", "Dark Cave (Route 31 side) · cave", "Bell Tower · cave"],
                    loaded[RomVersion.HeartGold].Select(m => m.Name));
                Assert.Equal(new Slot(0, 16, 3, 3, 20, SlotCondition.Morning), loaded[RomVersion.HeartGold][0].Slots[0]);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Id_IsFirstMapAndModality_AndIgnoresName()
        {
            string renamed = Names.Replace("\"Route 30\"", "\"Route Thirty\"").Replace("\"Bell Tower\"", "\"Tin Tower\"");
            string first = WriteFolder(Milestones, Names);
            string second = WriteFolder(Milestones, renamed);
            try
            {
                var a = MilestoneFile.Load(first)[RomVersion.HeartGold];
                var b = MilestoneFile.Load(second)[RomVersion.HeartGold];

                Assert.Equal(["34-land", "34-oldrod", "176-land", "332-land"], a.Select(m => m.Id));
                Assert.Equal(a.Select(m => m.Id), b.Select(m => m.Id));
                Assert.Equal("Route Thirty · grass", b[0].Name);
                Assert.Equal("Tin Tower · cave", b[3].Name);
                Assert.NotEqual(a[0].Name, b[0].Name);
            }
            finally
            {
                Directory.Delete(first, recursive: true);
                Directory.Delete(second, recursive: true);
            }
        }

        [Fact]
        public void Load_MissingName_Throws()
        {
            string withoutGroup = Names.Replace("\"332-land\": \"Bell Tower\"", "");
            string directory = WriteFolder(Milestones, withoutGroup);
            try
            {
                var error = Assert.Throws<InvalidDataException>(() => MilestoneFile.Load(directory));

                Assert.Contains("332-land", error.Message);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
