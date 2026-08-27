using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The config is validated when the event is built rather than when a value is first used, so a
    /// bad entry refuses the start instead of surfacing an hour into a hunt. These check that the
    /// shipped file is coherent and that validation actually rejects things.
    /// </summary>
    [Collection("macro-registry")]
    public class StarterHuntConfigTests
    {
        public StarterHuntConfigTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        [Fact]
        public void ShippedConfigLoadsAndValidates()
        {
            StarterHuntConfig config = StarterHuntConfig.Load();

            Assert.NotEmpty(config.StarterMacros);
        }

        [Fact]
        public void EveryMacroTheShippedConfigNamesExists()
        {
            // The whole point of naming macros in config is that they can be renamed; this is what
            // catches a rename that was not carried across.
            StarterHuntConfig config = StarterHuntConfig.Load();

            var named = new List<string>
            {
                config.ContinueMacro, config.CreateSaveMacro, config.SoftResetMacro, config.BattleAdvanceMacro,
            };
            named.AddRange(config.StarterMacros.Values);

            foreach (string macro in named)
            {
                string compiled = MacroRegistry.Instance.GetMacro(GameFlavor.RS, macro);
                Assert.False(string.IsNullOrWhiteSpace(compiled), $"Config names '{macro}', which compiled to nothing.");
            }
        }

        [Fact]
        public void ShippedConfigCoversAllThreeStarters()
        {
            StarterHuntConfig config = StarterHuntConfig.Load();

            Assert.Equal(3, config.StarterMacros.Count);
            foreach (string starter in new[] { "Treecko", "Torchic", "Mudkip" })
            {
                Assert.False(string.IsNullOrWhiteSpace(config.MacroForStarter(starter)));
            }
        }

        [Fact]
        public void UnknownStarterSaysWhatIsConfigured()
        {
            StarterHuntConfig config = StarterHuntConfig.Load();

            var ex = Assert.Throws<InvalidOperationException>(() => config.MacroForStarter("Bulbasaur"));
            Assert.Contains("Treecko", ex.Message);
        }

        [Fact]
        public void MissingMacroNameIsRejected()
        {
            StarterHuntConfig config = new() { StarterMacros = { ["Treecko"] = "SelectStarterTreecko" }, ContinueMacro = "" };

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Fact]
        public void NoStartersIsRejected()
        {
            StarterHuntConfig config = new();

            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("StarterMacros", ex.Message);
        }

        [Fact]
        public void PollIntervalLongerThanTheTimeoutIsRejected()
        {
            // Would never poll, so the hunt would report the battle as never starting every time.
            StarterHuntConfig config = new()
            {
                StarterMacros = { ["Treecko"] = "SelectStarterTreecko" },
                BattleWaitTimeoutMs = 1000,
                BattlePollIntervalMs = 5000,
            };

            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("never be polled", ex.Message);
        }

        [Fact]
        public void NonPositiveTimingIsRejected()
        {
            StarterHuntConfig config = new()
            {
                StarterMacros = { ["Treecko"] = "SelectStarterTreecko" },
                PartyRetryFrames = 0,
            };

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }
    }
}
