using ShinyBotHunter.Context;

namespace ShinyBotHunter.Tests
{
    public class GameContextTests
    {
        [Fact]
        public void SaveSlotIsNamedAfterTheRomCopy()
        {
            // A save on disk should say which copy produced it, not which slot ran it.
            var context = new GameContext { SourceGameName = "Pokemon Sapphire", GameId = 3 };

            Assert.Equal("Pokemon Sapphire3", context.SaveSlotName);
        }

        [Fact]
        public void ASingleInstanceUsesTheRomNameUnchanged()
        {
            var context = new GameContext { SourceGameName = "Pokemon Sapphire" };

            Assert.Equal("Pokemon Sapphire", context.SaveSlotName);
        }

        [Fact]
        public void EachInstanceGetsADistinctSlot()
        {
            // Two instances sharing a slot is the bug this whole mechanism exists to prevent.
            var slots = Enumerable.Range(1, 4)
                .Select(i => new GameContext { SourceGameName = "Pokemon Sapphire", GameId = i }.SaveSlotName)
                .ToList();

            Assert.Equal(4, slots.Distinct().Count());
        }

        [Fact]
        public void CharactersThatCannotBeInADirectoryNameAreReplaced()
        {
            // The slot becomes a real directory, so an unusable name would fail at launch rather than
            // anywhere diagnosable.
            var context = new GameContext { SourceGameName = "Pokemon: Sapphire?" };

            Assert.DoesNotContain(':', context.SaveSlotName);
            Assert.DoesNotContain('?', context.SaveSlotName);
        }

        [Theory]
        [InlineData(ConsoleType.GBA, ".gba")]
        [InlineData(ConsoleType.DS, ".nds")]
        public void RomExtensionFollowsTheConsole(ConsoleType console, string expected)
        {
            var context = new GameContext { SourceGameName = "game", ConsoleType = console };

            Assert.Equal(expected, context.RomExtension);
        }
    }
}
