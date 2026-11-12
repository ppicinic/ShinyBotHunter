using System.Text.Json.Nodes;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    // The shipped static encounter config is coherent, and validation rejects what would fail a cycle later (FR-10).
    [Collection("macro-registry")]
    public class StaticEncounterConfigTests
    {
        public StaticEncounterConfigTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        [Fact]
        public void ShippedConfigLoadsAndValidates()
        {
            StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);

            Assert.Equal(["Dialga (Sinjoh Ruins)", "Palkia (Sinjoh Ruins)", "Giratina (Sinjoh Ruins)"], config.Encounters.Select(e => e.Name));
            Assert.Equal([483, 484, 487], config.Encounters.Select(e => e.Species));
            Assert.All(config.Encounters, e => Assert.Equal(522, e.MapId));
            Assert.Equal([(12, 15), (20, 15), (16, 8)], config.Encounters.Select(e => (e.Spot[0], e.Spot[1])));
            Assert.All(config.Encounters, e =>
            {
                Assert.Equal([44, 77], e.PokemonTouch);
                Assert.Equal([195, 34], e.SlotTouch);
                Assert.Equal([195, 41], e.SummaryTouch);
            });
            Assert.Equal(200000, config.CutsceneStaleMs);
        }

        [Fact]
        public void EveryMacroTheShippedConfigNamesExists()
        {
            StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);

            foreach (string macro in new[] { config.SoftResetMacro, config.ContinueMacro })
            {
                string compiled = MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, macro);
                Assert.False(string.IsNullOrWhiteSpace(compiled), $"Config names '{macro}', which compiled to nothing.");
            }
        }

        [Fact]
        public void TheResetMacroHoldsTheFourButtonsEightFrames()
        {
            StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);

            Assert.Equal("H.Start.8:H.Select.8:H.L.8:H.R.8", MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, config.SoftResetMacro));
        }

        [Fact]
        public void BadValuesAreRejected()
        {
            var cases = new (string Key, Action<StaticEncounterConfig> Set)[]
            {
                ("spot", c => c.Encounters[0].Spot = []),
                ("encounters", c => c.Encounters.Clear()),
                ("slotTouch", c => c.Encounters[1].SlotTouch = [195, 34, 1]),
                ("name", c => c.Encounters[2].Name = c.Encounters[0].Name),
                ("species", c => c.Encounters[0].Species = 0),
                ("cutsceneStaleMs", c => c.CutsceneStaleMs = 0),
            };

            foreach (var (key, set) in cases)
            {
                StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);
                set(config);

                var ex = Assert.Throws<InvalidOperationException>(() => config.Validate("StaticEncounter.json"));
                Assert.StartsWith($"StaticEncounter.json: {key}", ex.Message);
            }
        }

        [Fact]
        public void UnknownFieldIsRejected()
        {
            JsonObject json = JsonNode.Parse(File.ReadAllText(StaticEncounterConfig.DefaultPath),
                documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsObject();
            json["walkTiles"] = new JsonArray();
            string path = Path.Combine(Path.GetTempPath(), $"StaticEncounter-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json.ToJsonString());
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => StaticEncounterConfig.Load(path));

                Assert.StartsWith(path, ex.Message);
                Assert.Contains("walkTiles", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MissingFileIsFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(() => StaticEncounterConfig.Load("./Configs/Events/NoSuchStaticEncounter.json"));
        }

        [Fact]
        public void FindUnknownNameListsTheChoices()
        {
            StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);

            var ex = Assert.Throws<ArgumentException>(() => config.Find("Mew"));

            Assert.Equal("'Mew' is not a static encounter. Choose one of: Dialga (Sinjoh Ruins), Palkia (Sinjoh Ruins), Giratina (Sinjoh Ruins).", ex.Message);
        }
    }
}
