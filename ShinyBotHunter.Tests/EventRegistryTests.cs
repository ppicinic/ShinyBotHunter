using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class EventRegistryTests
    {
        private static readonly NewEventArguments LaunchBot = new() { EventName = "Launch Bot", GameName = "fake.nds" };

        private static BotService Build(GameProfile? profile = null)
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            // The launch and idle paths touch neither the hub, the provider nor the ROM index.
            return new BotService(settings, new FakeBotFactory(settings, Task.CompletedTask, profile), null!, null!, null!, null!);
        }

        [Fact]
        public void NamesAreUnique()
        {
            var names = EventRegistry.All.Select(e => e.Name).ToList();

            Assert.Equal(names.Distinct(), names);
        }

        [Fact]
        public void EveryRequiresIsAMemberTestOverGameCapabilities()
        {
            var constructor = typeof(GameCapabilities).GetConstructors().Single();
            foreach (EventRegistration entry in EventRegistry.All)
            {
                if (entry.Requires is not { } need) continue;

                Assert.Equal(typeof(bool), typeof(GameCapabilities).GetProperty(need.Capability)?.PropertyType);
                var onlyThat = (GameCapabilities)constructor.Invoke(constructor.GetParameters().Select(p => (object)(p.Name == need.Capability)).ToArray());
                var allButThat = (GameCapabilities)constructor.Invoke(constructor.GetParameters().Select(p => (object)(p.Name != need.Capability)).ToArray());
                Assert.True(need.Has(onlyThat), $"'{entry.Name}': {need.Capability} alone does not satisfy its predicate.");
                Assert.False(need.Has(allButThat), $"'{entry.Name}': its predicate holds without {need.Capability}.");
            }
        }

        [Fact]
        public async Task IdleHgssBotSeesTodaysActions()
        {
            var service = Build();
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);

            var names = service.GetAvailableActions(0).Select(a => a.Name).ToList();

            Assert.Equal(["Wild Encounter", "Chained Encounter", "HGSS Togepi Egg"], names);
        }

        [Fact]
        public async Task RsBotSeesNoHgssActions()
        {
            var service = Build(RsProfile.Create(new PokemonMetadataService(null!)));
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);

            var names = service.GetAvailableActions(0).Select(a => a.Name).ToList();

            Assert.DoesNotContain("Wild Encounter", names);
            Assert.DoesNotContain("Chained Encounter", names);
            Assert.DoesNotContain("HGSS Togepi Egg", names);
            Assert.Empty(names);
        }

        [Fact]
        public async Task UnsupportedActionIs400()
        {
            var service = Build(RsProfile.Create(new PokemonMetadataService(null!)));
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);
            var args = new NewEventArguments { EventName = "Wild Encounter", PhaseName = "x", TargetType = "ShinyTarget", EncounterType = EncounterType.Run, xRange = 1 };

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteEvent(0, args));

            Assert.Contains("HasWildEncounters", ex.Message);
            // No GameState was assigned, so no event was built.
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }

        // A verbatim copy of Configs/SpecialEvents/SpecialEvents.json as it was when the registry
        // replaced it. The dashboard renders the top-level forms from these options, so they must
        // not drift from what it rendered before.
        private const string TodaysSpecialEvents = """
            [
              {
                "Name": "Launch Bot",
                "RequiredParameters": [ "GameName" ],
                "OptionalParameters": [ "SoundOn"]
              },
              {
                "Name": "Launch Bot + Continue",
                "RequiredParameters": [ "GameName" ],
                "OptionalParameters": [ "SoundOn"]
              },
              {
                // One entry for both starter hunts. HGSS keeps its own long-standing event and takes no
                // parameters; RS uses the generalized hunt, so everything below it appears only in RS mode.
                "Name": "Shiny Starter Hunt",
                "RequiredParameters": [ "Mode" ],
                "ConditionalParameters": [
                  {
                    "Name": "PhaseName",
                    "Required": true,
                    "Condition": { "ConditionType": "Eq", "Name": "Mode", "Value": "RS" }
                  },
                  {
                    "Name": "Emulators",
                    "Condition": { "ConditionType": "Eq", "Name": "Mode", "Value": "RS" }
                  }
                ],
                "LegCondition": { "ConditionType": "Eq", "Name": "Mode", "Value": "RS" },
                "LegParameters": {
                  "Name": "Starter",
                  "RequiredParameters": [ "GameName", "Starter" ],
                  "OptionalParameters": [ "RecordingName", "RequireFemale" ]
                }
              }
            ]
            """;

        [Fact]
        public void TopLevelOptionsEqualTodaysSpecialEvents()
        {
            // The options the deleted BotService.Init read the file with.
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            var today = JsonSerializer.Deserialize<EventArgumentOptions[]>(TodaysSpecialEvents, options)!;

            var actual = Build().GetTopLevelOptions();

            Assert.Equal(JsonSerializer.Serialize(today, options), JsonSerializer.Serialize(actual, options));
        }

        private static IEnumerable<string> DeclaredParameters(EventArgumentOptions action)
        {
            foreach (string p in action.RequiredParameters ?? []) yield return p;
            foreach (string p in action.OptionalParameters ?? []) yield return p;
            foreach (var cp in action.ConditionalParameters) yield return cp.Name;
        }

        [Fact]
        public void EveryConditionNamesAParameterTheActionDeclares()
        {
            foreach (EventArgumentOptions action in EventRegistry.All.Select(e => e.Options))
            {
                string[] declared = DeclaredParameters(action).ToArray();

                foreach (var cp in action.ConditionalParameters)
                {
                    Assert.True(declared.Contains(cp.Condition.Name),
                        $"'{action.Name}': the condition on '{cp.Name}' watches '{cp.Condition.Name}', which the action does not declare.");
                }

                if (action.LegCondition is not null)
                {
                    Assert.True(declared.Contains(action.LegCondition.Name),
                        $"'{action.Name}': the leg condition watches '{action.LegCondition.Name}', which the action does not declare.");
                }
            }
        }

        [Fact]
        public void EveryModeConditionComparesAgainstARealFlavor()
        {
            // The dashboard's mode dropdown emits GameFlavor names, and the server parses them back
            // into the enum. A condition comparing against anything else can never be met.
            IEnumerable<Condition> conditions = EventRegistry.All.Select(e => e.Options).SelectMany(a =>
                a.ConditionalParameters.Select(cp => cp.Condition).Concat(a.LegCondition is null ? [] : new[] { a.LegCondition }));

            foreach (Condition condition in conditions.Where(c => c.Name == "Mode"))
            {
                Assert.True(Enum.TryParse<GameFlavor>(condition.Value, ignoreCase: true, out _),
                    $"A mode condition compares against '{condition.Value}', which is not a GameFlavor.");
            }
        }

        [Fact]
        public void OnlyTheLegsThatHaveAConditionAreGated()
        {
            // Chained encounters predate modes and must keep offering their legs unconditionally.
            foreach (EventArgumentOptions action in EventRegistry.All.Select(e => e.Options).Where(a => a.LegCondition is not null))
            {
                Assert.NotNull(action.LegParameters);
            }
        }
    }
}
