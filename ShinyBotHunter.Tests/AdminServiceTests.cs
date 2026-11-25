using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence;
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
            Assert.False((bool?)json["gamePhases"]![0]!["followsHunt"]);
            Assert.Equal(10, (int?)json["recentEncounterCount"]);
            JsonNode appearance = json["appearance"]!;
            Assert.Equal("07:00", (string?)appearance["dayStart"]);
            Assert.Equal("19:00", (string?)appearance["nightStart"]);
            JsonObject animations = appearance["animations"]!.AsObject();
            Assert.Equal(["gradients", "snow", "stars"], animations.Select(member => member.Key));
            Assert.All(animations, member => Assert.Equal("high", (string?)member.Value));
            Assert.Equal("hgss-drift", (string?)appearance["default"]!["theme"]);
            Assert.Equal("hgss-dusk", (string?)appearance["default"]!["night"]);
            JsonObject window = appearance["windows"]!["phase:RS:Hoenn%20Starters:Ruby"]!.AsObject();
            Assert.Equal("ruby", (string?)window["theme"]);
            Assert.False(window.ContainsKey("night"));
        }

        [Fact]
        public void SavedDocumentHasNoAppearance()
        {
            var config = new DashboardConfig([
                new GamePhase(GameFlavor.HGSS, "Johto Starters", "HeartGold"),
                new GamePhase(GameFlavor.RS, "Hoenn Starters", null),
            ])
            {
                RecentEncounterCount = 25,
                Appearance = new Appearance { Default = new ThemeChoice { Theme = "ruby" } },
            };

            BsonDocument document = MongoDashboardConfigDao.ToDocument(config);
            Assert.Equal("dashboard", document["_id"].AsString);
            Assert.True(document.Contains("GamePhases"));
            Assert.True(document.Contains("RecentEncounterCount"));
            Assert.False(document.Contains("Appearance"));
            (DashboardConfig loaded, Appearance? appearance) = MongoDashboardConfigDao.FromDocument(document);

            Assert.Equal(config.GamePhases, loaded.GamePhases);
            Assert.Equal(25, loaded.RecentEncounterCount);
            Assert.Null(appearance);
        }

        [Fact]
        public void OldDocumentWithAppearanceStillLoads()
        {
            var config = new DashboardConfig([new GamePhase(GameFlavor.HGSS, "Johto Starters", "HeartGold")])
            {
                RecentEncounterCount = 25,
                Appearance = new Appearance
                {
                    DayStart = "06:30",
                    NightStart = "20:00",
                    Animations = new AnimationChoice { Gradients = AnimationLevel.off, Snow = AnimationLevel.low, Stars = AnimationLevel.medium },
                    Default = new ThemeChoice { Theme = "hgss-drift", Night = "hgss-dusk" },
                    Windows = { ["phase:RS:Hoenn%20Starters:Ruby.v1"] = new ThemeChoice { Theme = "ruby", Night = "sapphire" } },
                },
            };
            BsonDocument document = config.ToBsonDocument();
            document.InsertAt(0, new BsonElement("_id", "dashboard"));
            Assert.Equal("low", document["Appearance"]["Animations"]["Snow"].AsString);

            (DashboardConfig loaded, Appearance? appearance) = MongoDashboardConfigDao.FromDocument(document);

            Assert.Equal(config.GamePhases, loaded.GamePhases);
            Assert.Equal(25, loaded.RecentEncounterCount);
            AssertSameAppearance(config.Appearance, appearance!);
        }

        [Fact]
        public void StoredSubscriptionWithoutFollowsHuntIsAFixedCard()
        {
            BsonDocument stored = new()
            {
                { "_id", "dashboard" },
                { "GamePhases", new BsonArray
                    {
                        new BsonDocument { { "game", (int)GameFlavor.HGSS }, { "phase", "Route 32" }, { "gameFileName", "HG-TimeMove" } },
                        new BsonDocument { { "game", (int)GameFlavor.HGSS }, { "phase", "Johto Starters" }, { "gameFileName", BsonNull.Value } },
                        new BsonDocument { { "game", (int)GameFlavor.RS }, { "phase", "Hoenn Starters" }, { "gameFileName", "Ruby" } },
                    }
                },
                { "RecentEncounterCount", 10 },
            };

            (DashboardConfig loaded, _) = MongoDashboardConfigDao.FromDocument(stored);

            Assert.Equal(3, loaded.GamePhases.Length);
            Assert.All(loaded.GamePhases, gp => Assert.False(gp.followsHunt));
            BsonDocument saved = MongoDashboardConfigDao.ToDocument(loaded);
            Assert.All(saved["GamePhases"].AsBsonArray, element => Assert.False(element.AsBsonDocument.Contains("followsHunt")));

            BsonDocument follows = MongoDashboardConfigDao.ToDocument(new DashboardConfig([new GamePhase(GameFlavor.HGSS, "Route 32", "HG-TimeMove") { followsHunt = true }]));
            Assert.True(follows["GamePhases"][0]["followsHunt"].AsBoolean);
        }

        [Fact]
        public async Task FollowHuntIsOnePush()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> sharedSaves = [];
            AdminService service = Service(pushes, sharedSaves, []);
            await service.SubscribeGamePhaseAsync("HGSS", "Route 32", "HG-TimeMove");
            await service.SubscribeGamePhaseAsync("HGSS", "Route 32", "HG-TimeMove", followsHunt: true);
            await service.SubscribeGamePhaseAsync("HGSS", "Route 32", "HG-Other", followsHunt: true);
            pushes.Clear();
            sharedSaves.Clear();

            await service.FollowHuntAsync(GameFlavor.HGSS, "HG-TimeMove", "Route 30 Night");

            GamePhase[] expected = [
                new(GameFlavor.HGSS, "Route 32", "HG-TimeMove"),
                new(GameFlavor.HGSS, "Route 30 Night", "HG-TimeMove") { followsHunt = true },
                new(GameFlavor.HGSS, "Route 32", "HG-Other") { followsHunt = true },
            ];
            Assert.Equal(expected.ToHashSet(), Assert.Single(pushes).GamePhases.ToHashSet());
            Assert.Equal(expected.ToHashSet(), Assert.Single(sharedSaves).GamePhases.ToHashSet());
        }

        [Fact]
        public async Task FollowHuntWithNoSuchCardOrTheSameMilestoneDoesNothing()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> sharedSaves = [];
            AdminService service = Service(pushes, sharedSaves, []);
            await service.SubscribeGamePhaseAsync("HGSS", "Route 32", "HG-Fixed");
            await service.SubscribeGamePhaseAsync("HGSS", "Route 32", "HG-TimeMove", followsHunt: true);
            pushes.Clear();
            sharedSaves.Clear();

            await service.FollowHuntAsync(GameFlavor.HGSS, "HG-Fixed", "Route 30 Night");
            await service.FollowHuntAsync(GameFlavor.HGSS, "HG-TimeMove", "Route 32");

            Assert.Empty(pushes);
            Assert.Empty(sharedSaves);
        }

        [Fact]
        public async Task ASecondFollowsHuntCardReplacesTheFirst()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> sharedSaves = [];
            AdminService service = Service(pushes, sharedSaves, []);
            await service.SubscribeGamePhaseAsync("HGSS", "Route 32", "HG-TimeMove");
            await service.SubscribeGamePhaseAsync("HGSS", "Route 32", "HG-TimeMove", followsHunt: true);

            await service.SubscribeGamePhaseAsync("HGSS", "Route 30 Night", "HG-TimeMove", followsHunt: true);

            GamePhase[] expected = [
                new(GameFlavor.HGSS, "Route 32", "HG-TimeMove"),
                new(GameFlavor.HGSS, "Route 30 Night", "HG-TimeMove") { followsHunt = true },
            ];
            Assert.Equal(expected.ToHashSet(), sharedSaves[^1].GamePhases.ToHashSet());
        }

        // The same options as the controllers in Startup.cs; the JsonException is what [ApiController] answers with a 400.
        [Theory]
        [InlineData("gradients")]
        [InlineData("snow")]
        [InlineData("stars")]
        public void UnknownAnimationLevelIsRejected(string kind)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter());

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Appearance>($$$"""{"animations":{"{{{kind}}}":"ultra"}}""", options));
        }

        [Fact]
        public async Task AppearanceFileRoundTrips()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "sub", "appearance.json");
            try
            {
                var saved = new Appearance
                {
                    DayStart = "06:30",
                    NightStart = "20:00",
                    Animations = new AnimationChoice { Gradients = AnimationLevel.off, Snow = AnimationLevel.low, Stars = AnimationLevel.medium },
                    Default = new ThemeChoice { Theme = "hgss-drift", Night = "hgss-dusk" },
                    Windows =
                    {
                        ["recent"] = new ThemeChoice { Theme = "midnight" },
                        ["phase:RS:Hoenn%20Starters:Ruby.v1"] = new ThemeChoice { Theme = "ruby", Night = "sapphire" },
                    },
                };
                var file = new AppearanceFile(path, NullLogger.Instance);

                await file.SaveAsync(saved);
                Appearance? loaded = await file.LoadAsync();

                Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
                AssertSameAppearance(saved, loaded!);
                JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
                Assert.Equal("06:30", (string?)json["dayStart"]);
                Assert.Equal("low", (string?)json["animations"]!["snow"]);
                Assert.Equal("hgss-dusk", (string?)json["default"]!["night"]);
                Assert.Equal("sapphire", (string?)json["windows"]!["phase:RS:Hoenn%20Starters:Ruby.v1"]!["night"]);
                Assert.False(json["windows"]!["recent"]!.AsObject().ContainsKey("night"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public async Task AppearanceFileWithoutAnAnimationMemberLoadsAsHigh()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "appearance.json");
            Directory.CreateDirectory(folder);
            try
            {
                var file = new AppearanceFile(path, NullLogger.Instance);

                File.WriteAllText(path, """{"animations":{"gradients":"off"}}""");
                Appearance oneMember = (await file.LoadAsync())!;
                File.WriteAllText(path, """{"dayStart":"06:30"}""");
                Appearance noAnimations = (await file.LoadAsync())!;

                Assert.Equal(AnimationLevel.off, oneMember.Animations.Gradients);
                Assert.Equal(AnimationLevel.high, oneMember.Animations.Snow);
                Assert.Equal(AnimationLevel.high, oneMember.Animations.Stars);
                Assert.Equal(AnimationLevel.high, noAnimations.Animations.Gradients);
                Assert.Equal(AnimationLevel.high, noAnimations.Animations.Snow);
                Assert.Equal(AnimationLevel.high, noAnimations.Animations.Stars);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public async Task MissingAppearanceFileLoadsNull()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "appearance.json");

            Appearance? loaded = await new AppearanceFile(path, NullLogger.Instance).LoadAsync();

            Assert.Null(loaded);
            Assert.False(Directory.Exists(folder));
        }

        [Theory]
        [InlineData("""{"dayStart":"06:30","defa""")]
        [InlineData("""{"dayStart":"06:30","default":null}""")]
        [InlineData("null")]
        [InlineData("""{"windows":{"x":null}}""")]
        public async Task UnreadableAppearanceFileLoadsDefaultsAndIsLeftAlone(string text)
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "appearance.json");
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(path, text);
                var logger = new RecordingLogger();

                Appearance loaded = (await new AppearanceFile(path, logger).LoadAsync())!;

                AssertSameAppearance(new Appearance(), loaded);
                Assert.Equal(text, File.ReadAllText(path));
                var entry = Assert.Single(logger.Entries);
                Assert.Equal(LogLevel.Warning, entry.Level);
                Assert.Contains(path, entry.Message);
                Assert.True(entry.Exception == null || entry.Exception is JsonException);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public async Task EachChangeSavesOnlyItsStore()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> sharedSaves = [];
            List<Appearance> appearanceSaves = [];
            AdminService service = Service(pushes, sharedSaves, appearanceSaves);
            var appearance = new Appearance { Default = new ThemeChoice { Theme = "ruby" } };

            await service.SubscribeGamePhaseAsync("HGSS", "Johto Starters", "HeartGold");
            await service.UpdateRecentEncounterCount(25);
            await service.SetAppearanceAsync(appearance);
            await service.UnsubscribeGamePhaseAsync("RS", "Hoenn Starters", "Ruby");
            await service.UnsubscribeGamePhaseAsync("HGSS", "Johto Starters", "HeartGold");

            GamePhase johto = new(GameFlavor.HGSS, "Johto Starters", "HeartGold");
            Assert.Equal(4, pushes.Count);
            Assert.Equal(3, sharedSaves.Count);
            Assert.Equal([johto], sharedSaves[0].GamePhases);
            Assert.Equal(10, sharedSaves[0].RecentEncounterCount);
            Assert.Equal([johto], sharedSaves[1].GamePhases);
            Assert.Equal(25, sharedSaves[1].RecentEncounterCount);
            Assert.Empty(sharedSaves[2].GamePhases);
            Assert.Equal(25, sharedSaves[2].RecentEncounterCount);
            Assert.Same(appearance, Assert.Single(appearanceSaves));
        }

        [Fact]
        public async Task ConnectPushDoesNotSave()
        {
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> sharedSaves = [];
            List<Appearance> appearanceSaves = [];
            AdminService service = Service(pushes, sharedSaves, appearanceSaves);

            await service.UpdateDashboardsAsync();

            Assert.Single(pushes);
            Assert.Empty(sharedSaves);
            Assert.Empty(appearanceSaves);
        }

        [Fact]
        public async Task SavesNeverLandOutOfOrder()
        {
            List<DashboardConfig> saves = [];
            TaskCompletionSource firstSaveReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
            AdminService service = new(
                _ => Task.CompletedTask,
                () => Task.FromResult<(DashboardConfig Config, Appearance? Appearance)?>(null),
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
                () => Task.FromResult<Appearance?>(null),
                _ => Task.CompletedTask,
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
            List<DashboardConfig> sharedSaves = [];
            List<Appearance> appearanceSaves = [];
            AdminService service = Service(pushes, sharedSaves, appearanceSaves);

            await service.InitAsync();
            await service.UpdateDashboardsAsync();

            DashboardConfig pushed = Assert.Single(pushes);
            Assert.Empty(pushed.GamePhases);
            Assert.Equal(10, pushed.RecentEncounterCount);
            Assert.Equal("midnight", pushed.Appearance.Default.Theme);
            Assert.Null(pushed.Appearance.Default.Night);
            Assert.Equal("07:00", pushed.Appearance.DayStart);
            Assert.Equal("19:00", pushed.Appearance.NightStart);
            Assert.Equal(AnimationLevel.high, pushed.Appearance.Animations.Gradients);
            Assert.Equal(AnimationLevel.high, pushed.Appearance.Animations.Snow);
            Assert.Equal(AnimationLevel.high, pushed.Appearance.Animations.Stars);
            Assert.Empty(pushed.Appearance.Windows);
            Assert.Empty(sharedSaves);
            Assert.Empty(appearanceSaves);
        }

        [Fact]
        public async Task InitRestoresTheSavedConfig()
        {
            GamePhase johto = new(GameFlavor.HGSS, "Johto Starters", "HeartGold");
            GamePhase hoenn = new(GameFlavor.RS, "Hoenn Starters", "Ruby");
            var shared = new DashboardConfig([johto, hoenn]) { RecentEncounterCount = 25 };
            var file = new Appearance
            {
                DayStart = "06:30",
                NightStart = "20:00",
                Animations = new AnimationChoice { Gradients = AnimationLevel.off, Snow = AnimationLevel.medium, Stars = AnimationLevel.low },
                Default = new ThemeChoice { Theme = "hgss-drift", Night = "hgss-dusk" },
                Windows =
                {
                    ["admin"] = new ThemeChoice { Theme = "christmas" },
                    ["pickup:Ruby"] = new ThemeChoice { Theme = "ruby", Night = "sapphire" },
                },
            };
            List<DashboardConfig> pushes = [];
            AdminService service = Service(pushes, [], [], (shared, null), file);

            await service.InitAsync();
            await service.UpdateDashboardsAsync();

            DashboardConfig pushed = Assert.Single(pushes);
            Assert.Equal([johto, hoenn], pushed.GamePhases.OrderBy(p => p.game));
            Assert.Equal(25, pushed.RecentEncounterCount);
            AssertSameAppearance(file, pushed.Appearance);
        }

        [Fact]
        public async Task UnknownThemeIdFallsBackToMidnight()
        {
            var file = new Appearance
            {
                Default = new ThemeChoice { Theme = "emerald", Night = "hgss-dusk" },
                Windows = { ["recent"] = new ThemeChoice { Theme = "ruby", Night = "bogus" } },
            };
            List<DashboardConfig> pushes = [];
            AdminService service = Service(pushes, [], [], file: file);

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
            AdminService badDay = Service(pushes, [], [], file: new Appearance { DayStart = "7pm", NightStart = "20:00" });
            AdminService badNight = Service(pushes, [], [], file: new Appearance { DayStart = "06:00", NightStart = "25:00" });

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
            var file = new Appearance { Windows = { ["pickup:Gone"] = new ThemeChoice { Theme = "ruby" } } };
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> sharedSaves = [];
            List<Appearance> appearanceSaves = [];
            AdminService service = Service(pushes, sharedSaves, appearanceSaves, file: file);

            await service.InitAsync();
            await service.SubscribeGamePhaseAsync("HGSS", "Johto Starters", "HeartGold");

            Assert.Equal("ruby", Assert.Single(pushes).Appearance.Windows["pickup:Gone"].Theme);
            Assert.Single(sharedSaves);
            Assert.Empty(appearanceSaves);
        }

        [Fact]
        public async Task DocumentAppearanceIsCopiedToTheFileOnce()
        {
            var old = new Appearance
            {
                DayStart = "06:30",
                Animations = new AnimationChoice { Snow = AnimationLevel.off },
                Default = new ThemeChoice { Theme = "sapphire" },
                Windows = { ["recent"] = new ThemeChoice { Theme = "ruby", Night = "sapphire" } },
            };
            var shared = new DashboardConfig([]) { RecentEncounterCount = 25 };
            List<DashboardConfig> pushes = [];
            List<DashboardConfig> sharedSaves = [];
            List<Appearance> appearanceSaves = [];
            AdminService first = Service(pushes, sharedSaves, appearanceSaves, (shared, old));

            await first.InitAsync();
            await first.UpdateDashboardsAsync();
            AdminService second = Service([], sharedSaves, appearanceSaves, (shared, old), appearanceSaves[0]);
            await second.InitAsync();

            AssertSameAppearance(old, Assert.Single(pushes).Appearance);
            AssertSameAppearance(old, Assert.Single(appearanceSaves));
            Assert.Empty(sharedSaves);
        }

        [Fact]
        public async Task FileWinsOverTheDocumentAppearance()
        {
            var shared = new DashboardConfig([]);
            var old = new Appearance { Default = new ThemeChoice { Theme = "sapphire" } };
            var file = new Appearance { Default = new ThemeChoice { Theme = "ruby" } };
            List<DashboardConfig> pushes = [];
            List<Appearance> appearanceSaves = [];
            AdminService service = Service(pushes, [], appearanceSaves, (shared, old), file);

            await service.InitAsync();
            await service.UpdateDashboardsAsync();

            Assert.Equal("ruby", Assert.Single(pushes).Appearance.Default.Theme);
            Assert.Empty(appearanceSaves);
        }

        [Fact]
        public async Task DocumentWithoutAppearanceAndNoFileGivesDefaults()
        {
            GamePhase johto = new(GameFlavor.HGSS, "Johto Starters", "HeartGold");
            var shared = new DashboardConfig([johto]);
            List<DashboardConfig> pushes = [];
            List<Appearance> appearanceSaves = [];
            AdminService service = Service(pushes, [], appearanceSaves, (shared, null));

            await service.InitAsync();
            await service.UpdateDashboardsAsync();

            DashboardConfig pushed = Assert.Single(pushes);
            Assert.Equal([johto], pushed.GamePhases);
            AssertSameAppearance(new Appearance(), pushed.Appearance);
            Assert.Empty(appearanceSaves);
        }

        private static AdminService Service(
            List<DashboardConfig> pushes,
            List<DashboardConfig> sharedSaves,
            List<Appearance> appearanceSaves,
            (DashboardConfig Config, Appearance? Appearance)? shared = null,
            Appearance? file = null)
            => new(
                config => { pushes.Add(config); return Task.CompletedTask; },
                () => Task.FromResult(shared),
                config => { sharedSaves.Add(config); return Task.CompletedTask; },
                () => Task.FromResult(file),
                appearance => { appearanceSaves.Add(appearance); return Task.CompletedTask; },
                NullLogger.Instance);

        private static void AssertSameAppearance(Appearance expected, Appearance actual)
        {
            Assert.Equal(expected.DayStart, actual.DayStart);
            Assert.Equal(expected.NightStart, actual.NightStart);
            Assert.Equal(expected.Animations.Gradients, actual.Animations.Gradients);
            Assert.Equal(expected.Animations.Snow, actual.Animations.Snow);
            Assert.Equal(expected.Animations.Stars, actual.Animations.Stars);
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

        private class RecordingLogger : ILogger
        {
            public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
