using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    public class CommandPreprocessorTests
    {
        [Fact]
        public void Tap_ExpandsToAHoldOfTheDefaultLength()
        {
            string result = new CommandPreprocessor().Process("T.A");

            Assert.Equal("H.A.3", result);
        }

        [Fact]
        public void RandomWait_ResolvesInsideItsRange()
        {
            CommandPreprocessor preprocessor = new();

            // Resolved per call, so sample enough to catch an off-by-one at either bound.
            for (int i = 0; i < 200; i++)
            {
                string result = preprocessor.Process("W.R[10,20]");
                int frames = int.Parse(result.Substring(2));
                Assert.InRange(frames, 10, 20);
            }
        }

        [Fact]
        public void TouchShorthand_ResolvesToCoordinatesOnDs()
        {
            string result = new CommandPreprocessor(ConsoleType.DS).Process("S.Pokedex");

            Assert.StartsWith("S.45.35.", result);
        }

        [Fact]
        public void TouchShorthand_IsRejectedOnGba()
        {
            CommandPreprocessor preprocessor = new(ConsoleType.GBA);

            // Silently tapping nothing would be worse: the macro would run to completion having
            // never pressed the thing it meant to.
            var ex = Assert.Throws<ArgumentException>(() => preprocessor.Process("S.Pokedex"));
            Assert.Contains("GBA", ex.Message);
        }

        [Fact]
        public void HexTokens_BecomeDecimal()
        {
            string result = new CommandPreprocessor().Process("M.Up.10.0x21DA6F4");

            // Computed rather than written out, so the test cannot disagree with itself.
            Assert.Contains(Convert.ToUInt32("21DA6F4", 16).ToString(), result);
            Assert.DoesNotContain("0x", result);
        }
    }
}
