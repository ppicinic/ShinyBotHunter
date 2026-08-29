namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// Where an emulator window goes on screen.
    ///
    /// Display slots are handed out as a flat index, and the grid turns that index into a pixel
    /// position. Both launchers share this so the two cannot drift apart, and so the arithmetic can
    /// be tested without starting an emulator.
    /// </summary>
    public static class EmulatorGrid
    {
        /// <summary>
        /// Columns in the grid: the emulator budget spread across the configured number of rows.
        /// Rows taller than the budget would leave no columns at all, so the result is never zero.
        /// </summary>
        public static int ColumnsFor(int maxEmulators, int rows)
        {
            if (rows <= 0)
                throw new ArgumentOutOfRangeException(nameof(rows), rows, "An emulator grid needs at least one row.");

            return Math.Max(1, maxEmulators / rows);
        }

        /// <summary>
        /// Top-left corner for a display slot. The six-pixel pull is the window border, which the
        /// window rectangle includes but the visible edge does not.
        /// </summary>
        public static (int X, int Y) PositionFor(int placeId, int maxEmulators, int rows, int width, int height, int widthOffset)
        {
            int columns = ColumnsFor(maxEmulators, rows);
            return (placeId % columns * width - 6 + widthOffset,
                    placeId / columns * height);
        }
    }
}
