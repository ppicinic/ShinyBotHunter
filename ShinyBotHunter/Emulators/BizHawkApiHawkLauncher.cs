using Microsoft.Extensions.Options;
using ShinyBotHunter.Context;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// Starts one EmuHawk with the ApiHawk external tool and places its window.
    ///
    ///   - Passes <c>--open-ext-tool-dll=BotServer</c> to load the external tool DLL.
    ///   - Passes the ports in the <c>BIZHAWK_COMMAND_PORT</c> and <c>BIZHAWK_MEMORY_PORT</c>
    ///     environment variables.
    ///   - Waits for the main EmuHawk window.
    ///   - Exposes <see cref="MemoryPort"/> and <see cref="CommandPort"/> so
    ///     <see cref="Bot.BotFactory"/> can pass them to the clients.
    /// </summary>
    public class BizHawkApiHawkLauncher : ILauncher
    {
        // Flags for SetWindowPos
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOZORDER   = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private readonly int    _placement;
        private readonly string _game;
        private readonly int    _width;
        private readonly int    _height;
        private readonly int    _widthOffset;
        private readonly int    _rows;
        private readonly int    _maxEmulators;
        private readonly string _romDirectory;
        private readonly string _bizHawkDirectory;

        private Process _process;
        private bool    _processStarted;
        private IntPtr  _hwId;

        private readonly BizHawkReaderWriterLock _locker;

        /// <summary>TCP port for memory reads/writes.</summary>
        public int MemoryPort  { get; }

        /// <summary>TCP port for button commands.</summary>
        public int CommandPort { get; }

        public string GameFileName => _game;

        public BizHawkApiHawkLauncher(GameContext context, IServiceProvider provider)
        {
            var appSettings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;

            _placement        = context.DisplayPosition;
            _game             = context.GameName;
            _width            = context.Width;
            _height           = context.Height;
            _widthOffset      = context.WidthOffset;
            _rows             = context.Rows;
            _maxEmulators     = appSettings.MaxEmulatorsFor(context.ConsoleType);
            _romDirectory     = appSettings.ROMBaseDirectory;
            _bizHawkDirectory = appSettings.BizHawkBaseDirectory;
            _locker           = provider.GetRequiredService<BizHawkReaderWriterLock>();

            // Ports as derived by BotFactory.BuildGameContext
            MemoryPort  = context.MemoryPort;
            CommandPort = context.CommandPort;

            string soundConfig = context.SoundOn
                ? $" --config={_bizHawkDirectory}config_sound_on.ini"
                : "";

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName         = $"{_bizHawkDirectory}EmuHawk.exe",
                    UseShellExecute  = false,
                    WorkingDirectory = _bizHawkDirectory,
                    // No core is named here: BizHawk selects melonDS vs mGBA from the file extension.
                    Arguments        = $"\"{_romDirectory}{_game}{context.RomExtension}\""
                                     + $" --open-ext-tool-dll=BotServer"
                                     + soundConfig
                }
            };

            // Pass the ports to BotServer via env vars
            _process.StartInfo.EnvironmentVariables["BIZHAWK_COMMAND_PORT"] = CommandPort.ToString();
            _process.StartInfo.EnvironmentVariables["BIZHAWK_MEMORY_PORT"]  = MemoryPort.ToString();
            // No save-slot variable: the fork now names SaveRAM after the ROM file rather than the
            // gamedb title, so each copy writes "<copy>.SaveRAM" of its own accord. That replaced a
            // per-instance directory driven by %BIZHAWK_SAVE_SLOT%, which existed only because every
            // copy of one ROM resolved to a single save file and overwrote the others.
        }

        // ── ILauncher ─────────────────────────────────────────────────────────────

        public async Task LaunchAsync()
        {
            await using (var locker = await _locker.GetReaderLockAsync())
            {
                _process.Start();
                _processStarted = true;
                IntPtr main = await WaitForMainWindowAsync(_process);
                _hwId = main;
                await RepositionAsync(_placement);
            }
        }

        public Task RepositionAsync(int x, int y)
        {
            SetWindowPos(_hwId, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
            return Task.CompletedTask;
        }

        public Task RepositionAsync(int placeId)
        {
            var (x, y) = EmulatorGrid.PositionFor(placeId, _maxEmulators, _rows, _width, _height, _widthOffset);
            return RepositionAsync(x, y);
        }

        // ── Window helpers ────────────────────────────────────────────────────────

        // 60s, not 30: this is the first budget a large wave blows. Twenty EmuHawks starting inside
        // a couple of minutes contend for the GUI and the disk, and an emulator that took longer than
        // half a minute to paint its window failed here -- visibly, as the one that never moved to
        // its position -- while the connection budget after it still had four times the headroom.
        private static async Task<IntPtr> WaitForMainWindowAsync(Process process, int timeoutMs = 60_000)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    IntPtr found = IntPtr.Zero;

                    EnumThreadWindows((uint)thread.Id, (hWnd, _) =>
                    {
                        if (!IsWindowVisible(hWnd)) return true;
                        string title = GetWindowTextSafe(hWnd);
                        if (!string.IsNullOrWhiteSpace(title) &&
                            !title.Contains("Bot Server", StringComparison.OrdinalIgnoreCase))
                        {
                            found = hWnd;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (found != IntPtr.Zero)
                        return found;
                }

                await Task.Delay(100);
            }

            throw new TimeoutException("Timed out waiting for EmuHawk main window.");
        }

        // ── IAsyncDisposable ──────────────────────────────────────────────────────

        public async ValueTask DisposeAsync()
        {
            if (!_processStarted) return;

            await using (var locker = await _locker.GetWriterLockAsync())
            {
                if (_process.MainWindowHandle == _hwId)
                {
                    Console.WriteLine("Emulator is main window. Closing via CloseMainWindow()");
                    _process.CloseMainWindow();
                }
                else
                {
                    Console.WriteLine("Emulator is not main window. Closing via SendMessage()");
                    SendMessage(_hwId, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                var exitTask = _process.WaitForExitAsync();
                if (await Task.WhenAny(exitTask, Task.Delay(2 * 60 * 1000)) != exitTask)
                {
                    try { _process.Kill(entireProcessTree: true); } catch { }
                    await Task.WhenAny(_process.WaitForExitAsync(), Task.Delay(60_000));
                }

                await Task.Delay(2000);
            }
        }

        // ── Win32 ─────────────────────────────────────────────────────────────────

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static string GetWindowTextSafe(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    }
}
