using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The shipped Primo egg config is coherent, and validation rejects what would fail a cycle later.
    /// </summary>
    [Collection("macro-registry")]
    public class PrimoEggConfigTests
    {
        public PrimoEggConfigTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        [Fact]
        public void ShippedConfigLoadsAndValidates()
        {
            PrimoEggConfig config = PrimoEggConfig.Load(PrimoEggConfig.DefaultPath);

            Assert.Equal(10, config.Egg);
            Assert.Equal(218, config.EggSpecies);
            Assert.Equal((158, 12, 12, Direction.Up), (config.StartMapId, config.StartX, config.StartY, config.StartFacingDirection));
            Assert.Equal(
                new (char, short)[] { ('y', 19), ('x', 8), ('y', 272), ('x', 488), ('y', 264), ('x', 469), ('y', 258) },
                config.Legs);
            Assert.False(config.IsVerticalCorridor);
            Assert.Equal((458, 500), (config.HatchLow, config.HatchHigh));
            Assert.Equal(30000, config.HatchStaleMs);
        }

        [Fact]
        public void EveryMacroTheShippedConfigNamesExists()
        {
            PrimoEggConfig config = PrimoEggConfig.Load(PrimoEggConfig.DefaultPath);

            foreach (string macro in new[] { config.SoftResetMacro, config.ContinueMacro, config.SaveMacro })
            {
                string compiled = MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, macro);
                Assert.False(string.IsNullOrWhiteSpace(compiled), $"Config names '{macro}', which compiled to nothing.");
            }
        }

        [Fact]
        public void BadValuesAreRejected()
        {
            var cases = new (string Key, Action<PrimoEggConfig> Set)[]
            {
                ("startFacing", c => c.StartFacing = "None"),
                ("hatchAxis", c => c.HatchAxis = "z"),
                ("hatchLow", c => c.HatchLow = c.HatchHigh),
                ("routeToLine", c => c.RouteToLine = []),
                ("routeToLine", c => c.RouteToLine = ["z:3"]),
                ("routeToLine", c => c.RouteToLine = ["x:"]),
                ("routeToLine", c => c.RouteToLine = ["y:abc"]),
                ("egg", c => c.Egg = 11),
                ("hatchStaleMs", c => c.HatchStaleMs = 0),
            };

            foreach (var (key, set) in cases)
            {
                PrimoEggConfig config = PrimoEggConfig.Load(PrimoEggConfig.DefaultPath);
                set(config);

                var ex = Assert.Throws<InvalidOperationException>(() => config.Validate("PrimoEgg.json"));
                Assert.Contains($"PrimoEgg.json: {key}", ex.Message);
            }
        }

        [Fact]
        public void MissingFileIsFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(() => PrimoEggConfig.Load("./Configs/Events/NoSuchPrimoEgg.json"));
        }
    }
}
