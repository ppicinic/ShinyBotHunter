using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
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

        /// <summary>
        /// Opens the start menu, reads where SAVE actually is, and moves exactly that far. See
        /// <see cref="RsStartMenuService"/> for why this cannot be a fixed key sequence.
        /// </summary>
        public async Task SaveViaStartMenuAsync(IBot bot, RsPointerService pointers, int saveMenuActionId, string saveConfirmMacro, int menuWaitTimeoutMs, int pollIntervalMs, CancellationToken cancellationToken)
        {
            bot.RunMacroString("T.Start");
            await bot.WaitForMacroAsync(cancellationToken);

            MenuState menu = await WaitForStartMenuAsync(bot, pointers, menuWaitTimeoutMs, pollIntervalMs, cancellationToken);
            int target = menu.IndexOf(saveMenuActionId);
            // StepsTo carries the validation and its diagnostic message; the movement itself is done
            // one verified step at a time below.
            RsStartMenuService.StepsTo(menu, saveMenuActionId);
            Console.WriteLine($"[StarterHunt] Start menu: {menu.Describe()}; SAVE is at position {target}.");

            await MoveCursorToAsync(bot, pointers, target, pollIntervalMs, cancellationToken);

            bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.RS, saveConfirmMacro));
            await bot.WaitForMacroAsync(cancellationToken);
        }

        /// <summary>
        /// Walks the start-menu cursor to a position one verified press at a time.
        ///
        /// Firing the whole run of presses blind is how a save landed on the trainer card: the maths
        /// said three downs, one press was swallowed by the menu, and A went to whatever was two rows
        /// down. The cursor is readable, so each press is checked before the next -- a swallowed
        /// input costs a retry instead of selecting the wrong entry.
        /// </summary>
        private async Task MoveCursorToAsync(IBot bot, RsPointerService pointers, int target, int pollIntervalMs, CancellationToken cancellationToken)
        {
            const int MaxPresses = 24;
            for (int presses = 0; presses < MaxPresses; presses++)
            {
                MenuState menu = await ReadAsync(bot, pointers);
                if (menu.CursorPos == target)
                {
                    return;
                }

                bot.RunMacroString(menu.CursorPos < target ? "T.Down;W.15" : "T.Up;W.15");
                await bot.WaitForMacroAsync(cancellationToken);
                await Task.Delay(pollIntervalMs, cancellationToken);
            }

            MenuState final = await ReadAsync(bot, pointers);
            throw new InvalidOperationException(
                $"Start-menu cursor would not settle on position {target} after {MaxPresses} presses " +
                $"({final.Describe()}). Refusing to press A on whatever is highlighted.");
        }

        /// <summary>
        /// Polls until the start menu reports a sane state, rather than waiting a fixed time for it to
        /// open. How long that takes is not worth guessing at: too short and the menu is read before it
        /// is populated, which looks identical to a menu that does not contain SAVE.
        /// </summary>
        private async Task<MenuState> WaitForStartMenuAsync(IBot bot, RsPointerService pointers, int menuWaitTimeoutMs, int pollIntervalMs, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(menuWaitTimeoutMs);
            MenuState menu = await ReadAsync(bot, pointers);

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (menu.Count > 0 && menu.Count <= 10 && menu.CursorPos < menu.Count)
                {
                    return menu;
                }
                await Task.Delay(pollIntervalMs, cancellationToken);
                menu = await ReadAsync(bot, pointers);
            }

            // Returned rather than thrown so StepsTo produces the failure, with the menu contents in
            // its message.
            return menu;
        }
    }
}
