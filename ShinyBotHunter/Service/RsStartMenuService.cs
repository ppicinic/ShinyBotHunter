using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Drives the Ruby/Sapphire start menu by reading it rather than assuming it.
    ///
    /// A fixed key sequence does not work here. The menu grows as things are unlocked -- POKéDEX and
    /// POKéMON only appear once obtained -- and the cursor remembers where it was left, so "press Down
    /// three times" saves from a different entry depending on when it runs. Pressing SAVE blind would
    /// eventually open the OPTION menu instead, and the hunt would carry on believing it had saved.
    ///
    /// The game keeps the answer in memory: the entry list in display order, how many are valid, and
    /// where the cursor is. Reading those and moving exactly as far as needed is also what a player
    /// does -- they can see the same menu on screen.
    /// </summary>
    public class RsStartMenuService
    {
        /// <summary>The actions array is fixed-size regardless of how many entries are in use.</summary>
        private const int ActionsLength = 10;

        public record MenuState(int CursorPos, int Count, byte[] Actions)
        {
            /// <summary>Position of an action in display order, or -1 when the menu does not offer it.</summary>
            public int IndexOf(int actionId) => Array.IndexOf(Actions, (byte)actionId, 0, Math.Min(Count, Actions.Length));

            public string Describe() => $"cursor {CursorPos}, {Count} entries [{string.Join(", ", Actions.Take(Count))}]";
        }

        public async Task<MenuState> ReadAsync(IMemoryClient memory, RsPointerService addresses)
        {
            byte cursor = await memory.ReadByteAsync(addresses.StartMenuCursorPos);
            byte count = await memory.ReadByteAsync(addresses.StartMenuActionCount);
            byte[] actions = await memory.ReadBytesAsync(addresses.StartMenuActions, ActionsLength);
            return new MenuState(cursor, count, actions);
        }

        /// <summary>
        /// How many times to press Down to land on <paramref name="actionId"/>, given the menu as it
        /// currently is. Negative means press Up.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The menu does not contain that action, or its state is not readable. Thrown with the menu
        /// contents attached, because the alternative is pressing A on whatever happens to be
        /// highlighted -- which in this menu could mean opening the bag or, worse, the option screen.
        /// </exception>
        public static int StepsTo(MenuState menu, int actionId)
        {
            if (menu.Count <= 0 || menu.Count > ActionsLength)
            {
                throw new InvalidOperationException(
                    $"Start menu reports {menu.Count} entries, which is not a menu that is open and populated. " +
                    "Either it has not been opened yet, or the read landed mid-write.");
            }

            int target = menu.IndexOf(actionId);
            if (target < 0)
            {
                throw new InvalidOperationException(
                    $"Start menu has no entry with action id {actionId} ({menu.Describe()}). " +
                    "Either the menu is not the one expected, or SaveMenuActionId is wrong for this game.");
            }

            if (menu.CursorPos < 0 || menu.CursorPos >= menu.Count)
            {
                throw new InvalidOperationException(
                    $"Start menu cursor is at {menu.CursorPos}, outside its {menu.Count} entries ({menu.Describe()}).");
            }

            return target - menu.CursorPos;
        }

        /// <summary>
        /// A macro that walks the cursor from where it is to the target entry. Empty when it is
        /// already there, since sending nothing is cheaper than sending a no-op.
        /// </summary>
        public static string BuildCursorMacro(int steps, int framesBetween = 8)
        {
            if (steps == 0) return string.Empty;

            string button = steps > 0 ? "Down" : "Up";
            int count = Math.Abs(steps);
            var parts = new List<string>(count * 2);
            for (int i = 0; i < count; i++)
            {
                parts.Add($"T.{button}");
                parts.Add($"W.{framesBetween}");
            }
            return string.Join(";", parts);
        }
    }
}
