using System.Text.Json;
using System.Text.Json.Serialization;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Checks the shipped top-level action schema.
    ///
    /// The dashboard renders whatever this file declares, so a condition naming a parameter that
    /// does not exist -- or a mode value that no dropdown ever emits -- does not fail anywhere. The
    /// field simply never appears, and the action looks broken for reasons nothing reports.
    /// </summary>
    public class SpecialEventsSchemaTests
    {
        private static readonly EventArgumentOptions[] Actions = Load();

        private static EventArgumentOptions[] Load()
        {
            string path = Path.Combine(RepoPaths.AppProject, "Configs", "SpecialEvents", "SpecialEvents.json");
            // The same options BotService.Init uses, so a config it would refuse fails here first.
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            return JsonSerializer.Deserialize<EventArgumentOptions[]>(File.ReadAllText(path), options)!;
        }

        private static IEnumerable<string> DeclaredParameters(EventArgumentOptions action)
        {
            foreach (string p in action.RequiredParameters ?? []) yield return p;
            foreach (string p in action.OptionalParameters ?? []) yield return p;
            foreach (var cp in action.ConditionalParameters) yield return cp.Name;
        }

        [Fact]
        public void BothStarterHuntsLiveUnderOneAction()
        {
            string[] hunts = Actions.Where(a => a.Name.Contains("Starter Hunt")).Select(a => a.Name).ToArray();
            Assert.Equal(["Shiny Starter Hunt"], hunts);
        }

        [Fact]
        public void TheStarterHuntAsksWhichGameFirst()
        {
            EventArgumentOptions hunt = Actions.Single(a => a.Name == "Shiny Starter Hunt");

            Assert.Contains("Mode", hunt.RequiredParameters ?? []);
            // Everything else belongs to the RS hunt, so HGSS mode shows the picker and nothing else.
            Assert.Equal(["Mode"], hunt.RequiredParameters);
            Assert.NotNull(hunt.LegCondition);
        }

        [Fact]
        public void EveryConditionNamesAParameterTheActionDeclares()
        {
            foreach (EventArgumentOptions action in Actions)
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
            IEnumerable<Condition> conditions = Actions.SelectMany(a =>
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
            foreach (EventArgumentOptions action in Actions.Where(a => a.LegCondition is not null))
            {
                Assert.NotNull(action.LegParameters);
            }
        }
    }
}
