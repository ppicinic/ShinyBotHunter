using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The shipped Wishmaker config is coherent, and validation rejects what would fail a cycle later.
    /// </summary>
    [Collection("macro-registry")]
    public class WishmakerJirachiConfigTests
    {
        public WishmakerJirachiConfigTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        [Fact]
        public void ShippedConfigLoadsAndValidates()
        {
            WishmakerJirachiConfig config = WishmakerJirachiConfig.Load();

            Assert.Equal(26760, config.DsuPort);
        }

        [Fact]
        public void EveryMacroTheShippedConfigNamesExists()
        {
            WishmakerJirachiConfig config = WishmakerJirachiConfig.Load();

            foreach (string macro in new[] { config.ContinueMacro, config.SaveMacro })
            {
                string compiled = MacroRegistry.Instance.GetMacro(GameFlavor.RS, macro);
                Assert.False(string.IsNullOrWhiteSpace(compiled), $"Config names '{macro}', which compiled to nothing.");
            }
        }

        [Fact]
        public void MinPlayNotBelowMaxIsRejected()
        {
            WishmakerJirachiConfig config = new() { MinPlayMs = 5000, MaxPlayMs = 5000 };

            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("MinPlayMs", ex.Message);
        }

        [Fact]
        public void NonPositiveTimingIsRejected()
        {
            var zeroed = new (string Name, Action<WishmakerJirachiConfig> Set)[]
            {
                (nameof(WishmakerJirachiConfig.MinPlayMs), c => c.MinPlayMs = 0),
                (nameof(WishmakerJirachiConfig.MaxPlayMs), c => c.MaxPlayMs = 0),
                (nameof(WishmakerJirachiConfig.LaunchReadyTimeoutMs), c => c.LaunchReadyTimeoutMs = 0),
                (nameof(WishmakerJirachiConfig.SaveTimeoutMs), c => c.SaveTimeoutMs = 0),
                (nameof(WishmakerJirachiConfig.DiscMenuTimeoutMs), c => c.DiscMenuTimeoutMs = 0),
                (nameof(WishmakerJirachiConfig.DiscPromptTimeoutMs), c => c.DiscPromptTimeoutMs = 0),
                (nameof(WishmakerJirachiConfig.TransferTimeoutMs), c => c.TransferTimeoutMs = 0),
                (nameof(WishmakerJirachiConfig.DsuPort), c => c.DsuPort = 0),
            };

            foreach (var (name, set) in zeroed)
            {
                WishmakerJirachiConfig config = new();
                set(config);

                var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
                Assert.Contains(name, ex.Message);
            }
        }

        [Fact]
        public void MissingMacroNameIsRejected()
        {
            WishmakerJirachiConfig config = new() { SaveMacro = "" };

            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("SaveMacro", ex.Message);
        }

        [Fact]
        public void DiscTimeoutShorterThanItsIntervalIsRejected()
        {
            // Either would derive a press count of 0, and the disc would fail on its first check.
            WishmakerJirachiConfig menu = new() { DiscMenuTimeoutMs = 1999 };
            WishmakerJirachiConfig prompt = new() { DiscPromptTimeoutMs = 1499 };

            Assert.Contains("DiscMenuTimeoutMs", Assert.Throws<InvalidOperationException>(() => menu.Validate()).Message);
            Assert.Contains("DiscPromptTimeoutMs", Assert.Throws<InvalidOperationException>(() => prompt.Validate()).Message);
        }
    }
}
