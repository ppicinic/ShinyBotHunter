using System.Text.Json;
using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Pins how display slots become window positions.
    ///
    /// The row count used to be a hard-coded 2 in both launchers, which suits the DS window but
    /// spread the GBA budget of 36 across 18 columns -- most of them off the side of the screen. Now
    /// that it is configuration, the arithmetic is worth holding still, and the configuration is
    /// worth checking for the missing console key that would only surface as a failed launch.
    /// </summary>
    public class EmulatorGridTests
    {
        [Theory]
        [InlineData(8, 2, 4)]    // DS: four across, two deep
        [InlineData(36, 6, 6)]   // GBA: a six by six wall
        [InlineData(36, 2, 18)]  // the old behaviour, if anyone configures it back
        public void ColumnsAreTheBudgetSpreadAcrossTheRows(int maxEmulators, int rows, int expected)
        {
            Assert.Equal(expected, EmulatorGrid.ColumnsFor(maxEmulators, rows));
        }

        [Fact]
        public void MoreRowsThanEmulatorsStillLeavesAColumn()
        {
            // Integer division would say zero columns here, and every position would divide by it.
            Assert.Equal(1, EmulatorGrid.ColumnsFor(4, 6));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ARowlessGridIsRefused(int rows)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EmulatorGrid.ColumnsFor(36, rows));
        }

        [Fact]
        public void SlotsFillLeftToRightThenWrapToTheNextRow()
        {
            // GBA numbers: 36 emulators over 6 rows at 240x255.
            (int X, int Y) Place(int slot) => EmulatorGrid.PositionFor(slot, 36, 6, 240, 255, 0);

            Assert.Equal((-6, 0), Place(0));
            Assert.Equal((234, 0), Place(1));
            Assert.Equal((1194, 0), Place(5));    // last column of the first row
            Assert.Equal((-6, 255), Place(6));    // wraps down, back to the left edge
            Assert.Equal((1194, 1275), Place(35)); // bottom right corner
        }

        [Fact]
        public void TheWidthOffsetShiftsTheWholeGrid()
        {
            var (x, _) = EmulatorGrid.PositionFor(1, 8, 2, 257, 478, 100);
            Assert.Equal(257 - 6 + 100, x);
        }

        [Fact]
        public void EveryConsoleWithABudgetHasARowCount()
        {
            // BotFactory reads DisplayRows per console and throws on a missing key, which would be a
            // launch-time failure rather than a startup one.
            using JsonDocument settings = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoPaths.AppProject, "appsettings.json")),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            JsonElement app = settings.RootElement.GetProperty("ApplicationSettings");
            JsonElement rows = app.GetProperty("DisplayRows");

            foreach (JsonProperty console in app.GetProperty("MaxEmulators").EnumerateObject())
            {
                Assert.True(rows.TryGetProperty(console.Name, out JsonElement value),
                    $"appsettings configures MaxEmulators for '{console.Name}' but no DisplayRows entry.");
                Assert.True(value.GetInt32() > 0, $"DisplayRows for '{console.Name}' must be at least one row.");
            }
        }
    }
}
