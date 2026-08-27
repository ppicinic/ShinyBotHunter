using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The start menu is read rather than assumed, because its contents and cursor position both
    /// vary. These pin the arithmetic that decides how far to move, and -- more importantly -- that a
    /// menu it does not understand stops the flow instead of pressing A on whatever is highlighted.
    /// </summary>
    public class RsStartMenuServiceTests
    {
        private const int Save = 5;

        private static RsStartMenuService.MenuState Menu(int cursor, params byte[] actions)
        {
            byte[] padded = new byte[10];
            actions.CopyTo(padded, 0);
            return new RsStartMenuService.MenuState(cursor, actions.Length, padded);
        }

        [Fact]
        public void MovesDownWhenTheTargetIsBelowTheCursor()
        {
            // POKEDEX, POKEMON, BAG, POKENAV, PLAYER, SAVE
            var menu = Menu(0, 0, 1, 2, 3, 4, 5);

            Assert.Equal(5, RsStartMenuService.StepsTo(menu, Save));
        }

        [Fact]
        public void MovesUpWhenTheCursorIsPastTheTarget()
        {
            // The cursor remembers where it was left, so it can start below SAVE.
            var menu = Menu(6, 0, 1, 2, 3, 4, 5, 6, 7);

            Assert.Equal(-1, RsStartMenuService.StepsTo(menu, Save));
        }

        [Fact]
        public void StaysPutWhenAlreadyOnTheTarget()
        {
            Assert.Equal(0, RsStartMenuService.StepsTo(Menu(5, 0, 1, 2, 3, 4, 5), Save));
        }

        [Fact]
        public void EarlyGameMenuIsShorterAndStillFound()
        {
            // Before the Pokedex and party exist the menu is only BAG, PLAYER, SAVE, OPTION, EXIT --
            // which is exactly why a fixed number of Down presses cannot work.
            var menu = Menu(0, 2, 4, 5, 6, 7);

            Assert.Equal(2, RsStartMenuService.StepsTo(menu, Save));
        }

        [Fact]
        public void MenuWithoutSaveIsRefusedRatherThanGuessedAt()
        {
            var menu = Menu(0, 0, 1, 2, 3);

            var ex = Assert.Throws<InvalidOperationException>(() => RsStartMenuService.StepsTo(menu, Save));
            Assert.Contains("no entry with action id 5", ex.Message);
            // The message carries the menu, so a wrong id is diagnosable from the log alone.
            Assert.Contains("cursor 0", ex.Message);
        }

        [Fact]
        public void UnopenedMenuIsRefused()
        {
            // A count of zero means the menu is not up; pressing A then would act on the overworld.
            var ex = Assert.Throws<InvalidOperationException>(() => RsStartMenuService.StepsTo(Menu(0), Save));

            Assert.Contains("not a menu that is open", ex.Message);
        }

        [Fact]
        public void CursorOutsideTheMenuIsRefused()
        {
            // Signals a read that landed mid-write rather than a real position.
            var menu = Menu(9, 0, 1, 2, 3, 4, 5);

            Assert.Throws<InvalidOperationException>(() => RsStartMenuService.StepsTo(menu, Save));
        }

        [Theory]
        [InlineData(3, "T.Down")]
        [InlineData(-2, "T.Up")]
        public void CursorMacroPressesTheRightDirection(int steps, string expectedButton)
        {
            string macro = RsStartMenuService.BuildCursorMacro(steps);

            Assert.Equal(Math.Abs(steps), macro.Split(';').Count(p => p == expectedButton));
        }

        [Fact]
        public void NoMovementProducesNoMacro()
        {
            Assert.Equal(string.Empty, RsStartMenuService.BuildCursorMacro(0));
        }
    }
}
