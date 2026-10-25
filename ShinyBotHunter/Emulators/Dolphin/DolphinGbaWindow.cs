using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ShinyBotHunter.Emulators.Dolphin
{
    // Resets Dolphin's integrated GBA on port 2 through its window's context menu, with posted
    // messages only: no focus, no coordinates, no synthesized input. The menu order is Connected,
    // Load ROM, Unload ROM, a disabled e-Reader entry that keyboard navigation skips, Reset, so
    // Down four times and Return. Success is observed by the caller (wait-for-power clears, the
    // .sav changes), not here.
    public sealed class DolphinGbaWindow(int processId)
    {
        private const int PollMs = 100;
        // Between posted key messages, so the Qt popup handles each one before the next.
        private const int KeyGapMs = 100;

        public async Task ResetAsync(TimeSpan timeout, CancellationToken ct)
        {
            IntPtr gba = await WaitForWindowAsync("the GBA2 window", w => w.Title.StartsWith("GBA2", StringComparison.Ordinal), timeout, ct);

            // lParam -1: a keyboard-invoked context menu, placed by the window, not at a point.
            Post(gba, WM_CONTEXTMENU, gba, -1);

            IntPtr popup = await WaitForWindowAsync("the GBA2 context menu (a Qt popup window)", w => w.ClassName.Contains("Popup", StringComparison.Ordinal), timeout, ct);

            for (int i = 0; i < 4; i++)
            {
                Post(popup, WM_KEYDOWN, VK_DOWN, DownKeyLParam);
                await Task.Delay(KeyGapMs, ct);
                Post(popup, WM_KEYUP, VK_DOWN, DownKeyLParam | KeyUpFlags);
                await Task.Delay(KeyGapMs, ct);
            }
            // Return triggers Reset on its key-down and the menu closes, so it gets no key-up.
            Post(popup, WM_KEYDOWN, VK_RETURN, ReturnKeyLParam);
        }

        private static void Post(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (!PostMessage(window, message, wParam, lParam))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"PostMessage 0x{message:X4} to the GBA2 window or its menu failed");
        }

        private record struct Window(IntPtr Handle, string Title, string ClassName);

        private async Task<IntPtr> WaitForWindowAsync(string waitingFor, Func<Window, bool> match, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            List<Window> seen = VisibleWindows();
            while (true)
            {
                foreach (Window w in seen)
                    if (match(w)) return w.Handle;
                if (DateTime.UtcNow >= deadline) break;
                await Task.Delay(PollMs, ct);
                seen = VisibleWindows();
            }

            string windows = string.Join(", ", seen.Select(w => $"'{w.Title}' ({w.ClassName})"));
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0.#} s waiting for {waitingFor} of Dolphin process {processId}; its visible windows: [{windows}].");
        }

        private List<Window> VisibleWindows()
        {
            List<Window> windows = [];
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == processId && IsWindowVisible(hWnd))
                {
                    StringBuilder title = new(256);
                    StringBuilder className = new(256);
                    GetWindowText(hWnd, title, title.Capacity);
                    GetClassName(hWnd, className, className.Capacity);
                    windows.Add(new Window(hWnd, title.ToString(), className.ToString()));
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        // ── Win32 ─────────────────────────────────────────────────────────────────

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_CONTEXTMENU = 0x007B;
        private const int VK_RETURN = 0x0D;
        private const int VK_DOWN = 0x28;
        // Repeat count 1 and the scan code in bits 16..23; Down is an extended key (bit 24).
        private const int DownKeyLParam = 1 | (0x50 << 16) | (1 << 24);
        private const int ReturnKeyLParam = 1 | (0x1C << 16);
        // Previous key state and transition state, as a real key-up carries them.
        private const int KeyUpFlags = unchecked((int)0xC000_0000);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int count);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
