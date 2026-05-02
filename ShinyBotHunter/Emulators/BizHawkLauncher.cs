using Microsoft.Extensions.Options;
using ShinyBotHunter.Context;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ShinyBotHunter.Emulators
{
    public class BizHawkLauncher : ILauncher
    {

        // Flags for SetWindowPos
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_MINIMIZE = 6;

        private int memoryPort;
        private int commandPort;
        private string game;
        private Process process;
        private bool processStarted = false;
        private int placement;
        private IntPtr hwId;
        private int width;
        private int height;
        private int widthOffset;
        private string romDirectory;
        private string bizHawkDirectory;

        private int _maxEmulators = 14;
        private BizHawkReaderWriterLock _locker;
        public BizHawkLauncher(GameContext context, IServiceProvider provider)
        {
            ApplicationSettings applicationSettings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;
            this.placement = context.DisplayPosition;
            this.memoryPort = context.MemoryPort;
            this.commandPort = context.CommandPort;
            this.game = context.GameName;
            this.width = context.Width;
            this.height = context.Height;
            this.widthOffset = context.WidthOffset;
            this._maxEmulators = applicationSettings.MaxEmulators["DS"];
            this.romDirectory = applicationSettings.ROMBaseDirectory;
            this.bizHawkDirectory = applicationSettings.BizHawkBaseDirectory;
            _locker = provider.GetRequiredService<BizHawkReaderWriterLock>();

            string path = AppContext.BaseDirectory;
            string soundConfig = context.SoundOn ? $" --config={this.bizHawkDirectory}config_sound_on.ini" : "";
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = $"{this.bizHawkDirectory}EmuHawk.exe",
                    UseShellExecute = false,
                    Arguments = $"\"{this.romDirectory}{this.game}.nds\" \"--lua={path}\\Loader.lua\"{soundConfig}"
                }
            };
            process.StartInfo.Environment["BIZHAWK_COMMAND_PORT"] = this.commandPort.ToString();
            process.StartInfo.Environment["BIZHAWK_MEMORY_PORT"] = this.memoryPort.ToString();
        }

        public string GameFileName 
        {   
            get
            {
                return this.game;
            } 
        }

        private async Task WaitForWindows(Process process, int timeoutMs = 30000)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool hasMain = false;
                bool hasLua = false;

                foreach (ProcessThread thread in process.Threads)
                {
                    EnumThreadWindows((uint)thread.Id, (hWnd, lParam) =>
                    {
                        string title = GetWindowText(hWnd);

                        if (string.IsNullOrWhiteSpace(title))
                            return true;

                        if (title.Contains("Lua Console", StringComparison.OrdinalIgnoreCase))
                            hasLua = true;
                        else
                            hasMain = true;

                        return !(hasMain && hasLua);
                    }, IntPtr.Zero);

                    if (hasMain && hasLua)
                        return;
                }

                await Task.Delay(100);
            }
            Console.WriteLine("Timed out waiting for BizHawk windows.");
            throw new TimeoutException("Timed out waiting for BizHawk windows.");
        }

        public async Task LaunchAsync()
        {
            await using (var locker = await _locker.GetReaderLockAsync()) 
            {
                process.Start();
                processStarted = true;
                await WaitForWindows(process);
                (IntPtr main, IntPtr lua) = await CaptureWindowsAsync(process);
                hwId = main;
                ShowWindow(lua, SW_MINIMIZE);
                await RepositionAsync(this.placement);
            }
        }

        public Task RepositionAsync(int x, int y)
        {
            SetWindowPos(hwId, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
            return Task.CompletedTask;
        }

        public Task RepositionAsync(int placeId)
        {
            return RepositionAsync(placeId % (this._maxEmulators / 2) * this.width - 6 + this.widthOffset, placeId / (this._maxEmulators / 2) * this.height);
        }



        public static async Task<(IntPtr main, IntPtr lua)> CaptureWindowsAsync(Process process)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                var windows = new List<(IntPtr hWnd, string title)>();

                foreach (ProcessThread thread in process.Threads)
                {
                    EnumThreadWindows((uint)thread.Id, (hWnd, _) =>
                    {
                        if (!IsWindowVisible(hWnd))
                            return true;

                        string title = GetWindowTextSafe(hWnd);

                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            windows.Add((hWnd, title));
                        }

                        return true;
                    }, IntPtr.Zero);
                }

                if (windows.Count >= 2)
                {
                    IntPtr main = IntPtr.Zero;
                    IntPtr lua = IntPtr.Zero;

                    foreach (var (hWnd, title) in windows)
                    {
                        if (title.Contains("Lua Console", StringComparison.OrdinalIgnoreCase))
                            lua = hWnd;
                        else
                            main = hWnd;
                    }

                    if (main != IntPtr.Zero && lua != IntPtr.Zero)
                        return (main, lua);
                }

                await Task.Delay(100);
            }

            throw new Exception("Could not find both windows.");
        }

        public async Task ArrangeWindowsAsync(Process process, int x, int y)
        {
            // Wait until process has created windows

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);

                if (pid != process.Id)
                    return true;

                string title = GetWindowText(hWnd);

                if (string.IsNullOrWhiteSpace(title))
                    return true;

                // 🔻 Minimize Lua Console
                if (title.Contains("Lua Console", StringComparison.OrdinalIgnoreCase))
                {
                    ShowWindow(hWnd, SW_MINIMIZE);
                }
                else
                {
                    // 🔻 Assume main window → move it
                    SetWindowPos(
                        hWnd,
                        IntPtr.Zero,
                        x, y, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
                }

                return true;
            }, IntPtr.Zero);
        }


        private string GetWindowText(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0) return string.Empty;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            if (processStarted)
            {
                await using (var locker = await _locker.GetWriterLockAsync())
                {
                    if (process.MainWindowHandle == hwId)
                    {
                        Console.WriteLine("Emulator is main window. Closing via CloseMainWindow()");
                        process.CloseMainWindow();
                    }
                    else
                    {
                        Console.WriteLine("Emulator is not main window. Closing via SendMessage()");
                        SendMessage(hwId, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    var exitTask = process.WaitForExitAsync();
                    if(await Task.WhenAny(exitTask, Task.Delay(2 * 60 * 1000)) != exitTask)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch { }
                        await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(60000));
                    }
                    await Task.Delay(2000);
                }
            }
        }

        #region Win32

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(
            uint dwThreadId,
            EnumWindowsProc lpfn,
            IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private static string GetClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        private static string GetWindowTextSafe(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        #endregion
    }
}
