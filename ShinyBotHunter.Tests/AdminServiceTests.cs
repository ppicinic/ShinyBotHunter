using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Service;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Tests
{
    public class AdminServiceTests
    {
        [Fact]
        public void PushedConfigHasTheSpecJsonNames()
        {
            JsonSerializerOptions options = new JsonHubProtocolOptions().PayloadSerializerOptions;
            options.Converters.Add(new JsonStringEnumConverter());
            var config = new DashboardConfig([new GamePhase(GameFlavor.HGSS, "Johto Starters", "HeartGold")])
            {
                RecentEncounterCount = 10,
                Appearance = new Appearance
                {
                    Default = new ThemeChoice { Theme = "hgss-drift", Night = "hgss-dusk" },
                    Windows = { ["phase:RS:Hoenn%20Starters:Ruby"] = new ThemeChoice { Theme = "ruby" } },
                },
            };

            JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(config, options))!;

            Assert.Equal("HGSS", (string?)json["gamePhases"]![0]!["game"]);
            Assert.Equal("Johto Starters", (string?)json["gamePhases"]![0]!["phase"]);
            Assert.Equal("HeartGold", (string?)json["gamePhases"]![0]!["gameFileName"]);
            Assert.Equal(10, (int?)json["recentEncounterCount"]);
            JsonNode appearance = json["appearance"]!;
            Assert.Equal("07:00", (string?)appearance["dayStart"]);
            Assert.Equal("19:00", (string?)appearance["nightStart"]);
            Assert.Equal("hgss-drift", (string?)appearance["default"]!["theme"]);
            Assert.Equal("hgss-dusk", (string?)appearance["default"]!["night"]);
            JsonObject window = appearance["windows"]!["phase:RS:Hoenn%20Starters:Ruby"]!.AsObject();
            Assert.Equal("ruby", (string?)window["theme"]);
            Assert.False(window.ContainsKey("night"));
        }

        [Fact]
        public void SavedDocumentRoundTripsThroughBson()
        {
            var config = new DashboardConfig([
                new GamePhase(GameFlavor.HGSS, "Johto Starters", "HeartGold"),
                new GamePhase(GameFlavor.RS, "Hoenn Starters", null),
            ])
            {
                RecentEncounterCount = 25,
                Appearance = new Appearance
                {
                    DayStart = "06:30",
                    NightStart = "20:00",
                    Default = new ThemeChoice { Theme = "hgss-drift", Night = "hgss-dusk" },
                    Windows =
                    {
                        ["recent"] = new ThemeChoice { Theme = "midnight" },
                        ["phase:RS:Hoenn%20Starters:Ruby.v1"] = new ThemeChoice { Theme = "ruby", Night = "sapphire" },
                    },
                },
            };

            BsonDocument document = MongoDashboardConfigDao.ToDocument(config);
            Assert.Equal("dashboard", document["_id"].AsString);
            DashboardConfig loaded = MongoDashboardConfigDao.FromDocument(document);

            Assert.Equal(config.GamePhases, loaded.GamePhases);
            Assert.Equal(25, loaded.RecentEncounterCount);
            AssertSameAppearance(config.Appearance, loaded.Appearance);
        }

        private static void AssertSameAppearance(Appearance expected, Appearance actual)
        {
            Assert.Equal(expected.DayStart, actual.DayStart);
            Assert.Equal(expected.NightStart, actual.NightStart);
            AssertSameChoice(expected.Default, actual.Default);
            Assert.Equal(expected.Windows.Keys.Order(), actual.Windows.Keys.Order());
            foreach ((string name, ThemeChoice choice) in expected.Windows)
            {
                AssertSameChoice(choice, actual.Windows[name]);
            }
        }

        private static void AssertSameChoice(ThemeChoice expected, ThemeChoice actual)
        {
            Assert.Equal(expected.Theme, actual.Theme);
            Assert.Equal(expected.Night, actual.Night);
        }
    }
}
