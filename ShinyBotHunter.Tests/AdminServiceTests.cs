using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using ShinyBotHunter.Controllers;
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
                PickupGameFiles = ["HG-Pickup-Held"],
            };

            JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(config, options))!;

            Assert.Equal("HGSS", (string?)json["gamePhases"]![0]!["game"]);
            Assert.Equal("Johto Starters", (string?)json["gamePhases"]![0]!["phase"]);
            Assert.Equal("HeartGold", (string?)json["gamePhases"]![0]!["gameFileName"]);
            Assert.Equal(10, (int?)json["recentEncounterCount"]);
            JsonNode appearance = json["appearance"]!;
            Assert.Equal("07:00", (string?)appearance["dayStart"]);
            Assert.Equal("19:00", (string?)appearance["nightStart"]);
            Assert.True((bool?)appearance["animations"]);
            Assert.Equal("hgss-drift", (string?)appearance["default"]!["theme"]);
            Assert.Equal("hgss-dusk", (string?)appearance["default"]!["night"]);
            JsonObject window = appearance["windows"]!["phase:RS:Hoenn%20Starters:Ruby"]!.AsObject();
            Assert.Equal("ruby", (string?)window["theme"]);
            Assert.False(window.ContainsKey("night"));
            Assert.Equal("HG-Pickup-Held", (string?)json["pickupGameFiles"]![0]);
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
                    Animations = false,
                    Default = new ThemeChoice { Theme = "hgss-drift", Night = "hgss-dusk" },
                    Windows =
                    {
                        ["recent"] = new ThemeChoice { Theme = "midnight" },
                        ["phase:RS:Hoenn%20Starters:Ruby.v1"] = new ThemeChoice { Theme = "ruby", Night = "sapphire" },
                    },
                },
                PickupGameFiles = ["A", "B"],
            };

            BsonDocument document = MongoDashboardConfigDao.ToDocument(config);
            Assert.Equal("dashboard", document["_id"].AsString);
            DashboardConfig loaded = MongoDashboardConfigDao.FromDocument(document);

            Assert.Equal(config.GamePhases, loaded.GamePhases);
            Assert.Equal(25, loaded.RecentEncounterCount);
            AssertSameAppearance(config.Appearance, loaded.Appearance);
            Assert.Equal(["A", "B"], loaded.PickupGameFiles);
        }

        [Fact]
        public void SavedDocumentWithoutAnimationsLoadsAsOn()
        {
            BsonDocument document = MongoDashboardConfigDao.ToDocument(new DashboardConfig([]) { Appearance = new Appearance { Animations = false } });
            document["Appearance"].AsBsonDocument.Remove("Animations");

            DashboardConfig loaded = MongoDashboardConfigDao.FromDocument(document);

            Assert.True(loaded.Appearance.Animations);
        }

        [Fact]
        public void SavedDocumentWithoutPickupGameFilesLoadsAsEmpty()
        {
            BsonDocument document = MongoDashboardConfigDao.ToDocument(new DashboardConfig([]) { PickupGameFiles = ["HG-Test"] });
            document.Remove("PickupGameFiles");

            DashboardConfig loaded = MongoDashboardConfigDao.FromDocument(document);

            Assert.Empty(loaded.PickupGameFiles);
        }

        [Fact]
        public async Task EveryChangeSavesTheWholeConfig()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> saves = [];
            AdminService service = Service(pushes, saves);
            var appearance = new Appearance { Default = new ThemeChoice { Theme = "ruby" } };

            await service.SubscribeGamePhaseAsync("HGSS", "Johto Starters", "HeartGold");
            await service.UpdateRecentEncounterCount(25);
            await service.SetAppearanceAsync(appearance);
            await service.UnsubscribeGamePhaseAsync("RS", "Hoenn Starters", "Ruby");
            await service.UnsubscribeGamePhaseAsync("HGSS", "Johto Starters", "HeartGold");

            GamePhase johto = new(GameFlavor.HGSS, "Johto Starters", "HeartGold");
            Assert.Equal(4, saves.Count);
            Assert.Equal(4, pushes.Count);
            Assert.Equal([johto], saves[0].GamePhases);
            Assert.Equal(10, saves[0].RecentEncounterCount);
            Assert.Equal("midnight", saves[0].Appearance.Default.Theme);
            Assert.Equal([johto], saves[1].GamePhases);
            Assert.Equal(25, saves[1].RecentEncounterCount);
            Assert.Equal("midnight", saves[1].Appearance.Default.Theme);
            Assert.Equal([johto], saves[2].GamePhases);
            Assert.Equal(25, saves[2].RecentEncounterCount);
            Assert.Same(appearance, saves[2].Appearance);
            Assert.Empty(saves[3].GamePhases);
            Assert.Equal(25, saves[3].RecentEncounterCount);
            Assert.Same(appearance, saves[3].Appearance);
        }

        [Fact]
        public async Task PickupGameFilesAddOnceAndRemove()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> saves = [];
            AdminService service = Service(pushes, saves);

            await service.AddPickupGameFileAsync("A");
            await service.AddPickupGameFileAsync("A");
            await service.AddPickupGameFileAsync("B");
            await service.RemovePickupGameFileAsync("A");
            await service.RemovePickupGameFileAsync("A");

            Assert.Equal(3, saves.Count);
            Assert.Equal(3, pushes.Count);
            Assert.Equal(["A"], saves[0].PickupGameFiles);
            Assert.Equal(["A", "B"], saves[1].PickupGameFiles);
            Assert.Equal(["B"], saves[2].PickupGameFiles);
            foreach (DashboardConfig saved in saves)
            {
                Assert.Empty(saved.GamePhases);
                Assert.Equal(10, saved.RecentEncounterCount);
                Assert.Equal("midnight", saved.Appearance.Default.Theme);
                Assert.Empty(saved.Appearance.Windows);
            }
        }

        [Fact]
        public async Task AddingAnUnknownGameFileIsA400()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> saves = [];
            string dir = Path.Combine(Path.GetTempPath(), "sbh-admin-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                AdminController controller = Controller(Service(pushes, saves), dir);

                IActionResult result = await controller.AddPickupGameFile("Nope");

                Assert.IsType<BadRequestObjectResult>(result);
                Assert.Empty(saves);
                Assert.Empty(pushes);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task AddingAKnownGameFileSavesItsHandle()
        {
            List<DashboardConfig> saves = [];
            string dir = Path.Combine(Path.GetTempPath(), "sbh-admin-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                byte[] rom = new byte[0x200];
                "POKEMON TEST"u8.ToArray().CopyTo(rom, 0xA0);
                "AXVE"u8.ToArray().CopyTo(rom, 0xAC);
                "01"u8.ToArray().CopyTo(rom, 0xB0);
                rom[0xB2] = 0x96;
                File.WriteAllBytes(Path.Combine(dir, "HG-Test.gba"), rom);
                AdminController controller = Controller(Service([], saves), dir);

                IActionResult result = await controller.AddPickupGameFile("hg-test");

                Assert.IsType<OkResult>(result);
                Assert.Equal(["HG-Test"], Assert.Single(saves).PickupGameFiles);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ConnectPushDoesNotSave()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> saves = [];
            AdminService service = Service(pushes, saves);

            await service.UpdateDashboardsAsync();

            Assert.Single(pushes);
            Assert.Empty(saves);
        }

        [Fact]
        public async Task SavesNeverLandOutOfOrder()
        {
            List<DashboardConfig> saves = [];
            TaskCompletionSource firstSaveReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
            AdminService service = new(
                _ => Task.CompletedTask,
                () => Task.FromResult<DashboardConfig?>(null),
                async config =>
                {
                    bool isFirst;
                    lock (saves)
                    {
                        saves.Add(config);
                        isFirst = saves.Count == 1;
                    }
                    if (isFirst)
                    {
                        await firstSaveReleased.Task;
                    }
                },
                NullLogger.Instance);

            Task first = service.SubscribeGamePhaseAsync("HGSS", "Johto Starters", "HeartGold");
            Task second = service.UpdateRecentEncounterCount(25);
            await Task.Delay(50);
            Assert.Single(saves);
            firstSaveReleased.SetResult();
            await Task.WhenAll(first, second);

            Assert.Equal(2, saves.Count);
            Assert.Equal(10, saves[0].RecentEncounterCount);
            Assert.Single(saves[1].GamePhases);
            Assert.Equal(25, saves[1].RecentEncounterCount);
        }

        [Fact]
        public async Task DefaultsBeforeAnyChange()
        {
            List<DashboardConfig> pushes = [];
            AdminService service = Service(pushes, []);

            await service.InitAsync();
            await service.UpdateDashboardsAsync();

            DashboardConfig pushed = Assert.Single(pushes);
            Assert.Empty(pushed.GamePhases);
            Assert.Equal(10, pushed.RecentEncounterCount);
            Assert.Equal("midnight", pushed.Appearance.Default.Theme);
            Assert.Null(pushed.Appearance.Default.Night);
            Assert.Equal("07:00", pushed.Appearance.DayStart);
            Assert.Equal("19:00", pushed.Appearance.NightStart);
            Assert.True(pushed.Appearance.Animations);
            Assert.Empty(pushed.Appearance.Windows);
            Assert.Empty(pushed.PickupGameFiles);
        }

        [Fact]
        public async Task InitRestoresTheSavedConfig()
        {
            GamePhase johto = new(GameFlavor.HGSS, "Johto Starters", "HeartGold");
            GamePhase hoenn = new(GameFlavor.RS, "Hoenn Starters", "Ruby");
            var saved = new DashboardConfig([johto, hoenn])
            {
                RecentEncounterCount = 25,
                Appearance = new Appearance
                {
                    DayStart = "06:30",
                    NightStart = "20:00",
                    Animations = false,
                    Default = new ThemeChoice { Theme = "hgss-drift", Night = "hgss-dusk" },
                    Windows =
                    {
                        ["admin"] = new ThemeChoice { Theme = "christmas" },
                        ["pickup:Ruby"] = new ThemeChoice { Theme = "ruby", Night = "sapphire" },
                    },
                },
                PickupGameFiles = ["HG-Test", "Ruby"],
            };
            List<DashboardConfig> pushes = [];
            AdminService service = Service(pushes, [], saved);

            await service.InitAsync();
            await service.UpdateDashboardsAsync();

            DashboardConfig pushed = Assert.Single(pushes);
            Assert.Equal([johto, hoenn], pushed.GamePhases.OrderBy(p => p.game));
            Assert.Equal(25, pushed.RecentEncounterCount);
            AssertSameAppearance(saved.Appearance, pushed.Appearance);
            Assert.Equal(["HG-Test", "Ruby"], pushed.PickupGameFiles);
        }

        [Fact]
        public async Task UnknownThemeIdFallsBackToMidnight()
        {
            var saved = new DashboardConfig([])
            {
                Appearance = new Appearance
                {
                    Default = new ThemeChoice { Theme = "emerald", Night = "hgss-dusk" },
                    Windows = { ["recent"] = new ThemeChoice { Theme = "ruby", Night = "bogus" } },
                },
            };
            List<DashboardConfig> pushes = [];
            AdminService service = Service(pushes, [], saved);

            await service.InitAsync();
            await service.UpdateDashboardsAsync();

            Appearance appearance = Assert.Single(pushes).Appearance;
            Assert.Equal("midnight", appearance.Default.Theme);
            Assert.Equal("hgss-dusk", appearance.Default.Night);
            Assert.Equal("ruby", appearance.Windows["recent"].Theme);
            Assert.Equal("midnight", appearance.Windows["recent"].Night);
        }

        [Fact]
        public async Task MalformedTimeFallsBackToTheDefault()
        {
            List<DashboardConfig> pushes = [];
            AdminService badDay = Service(pushes, [], new DashboardConfig([]) { Appearance = new Appearance { DayStart = "7pm", NightStart = "20:00" } });
            AdminService badNight = Service(pushes, [], new DashboardConfig([]) { Appearance = new Appearance { DayStart = "06:00", NightStart = "25:00" } });

            await badDay.InitAsync();
            await badDay.UpdateDashboardsAsync();
            await badNight.InitAsync();
            await badNight.UpdateDashboardsAsync();

            Assert.Equal("07:00", pushes[0].Appearance.DayStart);
            Assert.Equal("20:00", pushes[0].Appearance.NightStart);
            Assert.Equal("06:00", pushes[1].Appearance.DayStart);
            Assert.Equal("19:00", pushes[1].Appearance.NightStart);
        }

        [Fact]
        public async Task OverrideForAGoneWindowStaysSaved()
        {
            var saved = new DashboardConfig([])
            {
                Appearance = new Appearance { Windows = { ["pickup:Gone"] = new ThemeChoice { Theme = "ruby" } } },
            };
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> saves = [];
            AdminService service = Service(pushes, saves, saved);

            await service.InitAsync();
            await service.SubscribeGamePhaseAsync("HGSS", "Johto Starters", "HeartGold");

            Assert.Equal("ruby", Assert.Single(pushes).Appearance.Windows["pickup:Gone"].Theme);
            Assert.Equal("ruby", Assert.Single(saves).Appearance.Windows["pickup:Gone"].Theme);
        }

        private static AdminService Service(List<DashboardConfig> pushes, List<DashboardConfig> saves, DashboardConfig? saved = null)
            => new(
                config => { pushes.Add(config); return Task.CompletedTask; },
                () => Task.FromResult(saved),
                config => { saves.Add(config); return Task.CompletedTask; },
                NullLogger.Instance);

        // PhaseInfoService is null: the Pickup endpoints do not use it, and a real one builds a MongoClient.
        private static AdminController Controller(AdminService service, string romDir)
        {
            var roms = new RomInfoService(Options.Create(new ApplicationSettings { ROMBaseDirectory = romDir + Path.DirectorySeparatorChar }), NullLogger<RomInfoService>.Instance);
            roms.InitAsync().GetAwaiter().GetResult();
            return new AdminController(service, null!, roms, Options.Create(new ApplicationSettings()));
        }

        private static void AssertSameAppearance(Appearance expected, Appearance actual)
        {
            Assert.Equal(expected.DayStart, actual.DayStart);
            Assert.Equal(expected.NightStart, actual.NightStart);
            Assert.Equal(expected.Animations, actual.Animations);
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
