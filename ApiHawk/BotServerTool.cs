using System;
using System.Windows.Forms;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// BizHawk 2.9.1 external tool — combined memory + command server over HTTP.
    ///
    /// Uses HttpListener (bound to http://127.0.0.1:{port}/) and the same port
    /// environment variables as before:
    ///   BIZHAWK_MEMORY_PORT  — memory server port  (default 50001)
    ///   BIZHAWK_COMMAND_PORT — command server port (default 50000)
    ///
    /// Loaded by EmuHawk via:
    ///   --open-ext-tool-dll=BotServer
    /// </summary>
    [ExternalTool("Bot Server", Description = "HTTP memory + command server for ShinyBotHunter")]
    public sealed class BotServerTool : Form, IExternalToolForm
    {
        [RequiredApi]
        public IMemoryApi? _memoryApi { get; set; }

        [RequiredApi]
        public IJoypadApi? _joypadApi { get; set; }

        [RequiredApi]
        public IEmuClientApi? _clientApi { get; set; }

        private MemoryServer?  _memoryServer;
        private CommandServer? _commandServer;

        private Label _statusLabel = null!;

        public BotServerTool()
        {
            InitializeComponent();
        }

        // Prevent the tool window from ever being shown — it has no UI to display.
        protected override void SetVisibleCore(bool value)
        {
            // Ensure the handle is created (required for BizHawk to interact with the tool)
            // but never actually show the window.
            if (!IsHandleCreated)
                CreateHandle();
            base.SetVisibleCore(false);
        }

        // Start servers as soon as the window handle is created.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_memoryServer == null)
                StartServers();
        }

        // ── IExternalToolForm (marker interface — no additional members) ───────────

        // ── IToolForm ─────────────────────────────────────────────────────────────

        public bool IsActive  => !IsDisposed;
        public bool IsLoaded  => _memoryServer != null;

        public void UpdateValues(ToolFormUpdateType type)
        {
            // Use PreFrame to match the Lua scripts which use event.onframestart.
            // This ensures joypad inputs are applied before the frame is processed.
            if (type == ToolFormUpdateType.PreFrame)
            {
                _commandServer?.ServiceFrame();
            }
            // Memory reads/writes can happen at PostFrame (after emulation, before next frame)
            else if (type == ToolFormUpdateType.PostFrame)
            {
                _memoryServer?.ServiceFrame();
            }
        }

        public void Restart()
        {
            StopServers();
            StartServers();
        }

        public bool AskSaveChanges() => true;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void StartServers()
        {
            // Ports must sit below 49152, where Windows' ephemeral range begins, and outside any
            // administered http.sys exclusion. Both halves are load-bearing and pull against each
            // other. Inside the ephemeral range, any program that opens a connection can be handed
            // the port first -- pgAdmin and Spotify each took one -- and HttpListener.Start() then
            // fails with ERROR_SHARING_VIOLATION on a random emulator. But excluding a range to
            // protect it makes things worse, not better: an administered exclusion (also created by
            // Hyper-V / WSL2 / Docker / WinNAT, which is why 50000-50059 was already unusable) stops
            // http.sys registering a prefix there at all, so the emulators are locked out too.
            // Below the ephemeral floor no reservation is needed. The bot always sets these env vars
            // explicitly (ports 20000+), so these fallbacks only matter when loading the tool by hand.
            int memPort = GetPort("BIZHAWK_MEMORY_PORT",  20001);
            int cmdPort = GetPort("BIZHAWK_COMMAND_PORT", 20000);

            // Before anything that might log. EmuHawk gives this assembly no usable console, so the
            // file is the only place diagnostics survive.
            BotLog.Init(memPort);

            _memoryServer = new MemoryServer(memPort)
            {
                MemoryApi = _memoryApi
            };

            _commandServer = new CommandServer(cmdPort)
            {
                MemoryApi = _memoryApi,
                JoypadApi = _joypadApi,
                ClientApi = _clientApi
            };

            try
            {
                _memoryServer.Start();
                _commandServer.Start();
            }
            catch (System.Net.HttpListenerException ex)
            {
                string msg = $"[BotServerTool] Failed to bind HTTP ports (memory: {memPort}, command: {cmdPort}). "
                           + $"Win32 error {ex.ErrorCode}. If the port is 'in use by another process' but nothing "
                           + "is listening, it is likely inside a reserved http.sys exclusion range "
                           + "(check: netsh int ipv4 show excludedportrange protocol=tcp). Pick ports outside that range.";
                BotLog.Write(msg);
                UpdateStatus(msg);
                throw;
            }

            UpdateStatus($"Listening (HTTP) — memory: {memPort} | command: {cmdPort}");
            BotLog.Write($"[BotServerTool] Started. Memory port: {memPort}, Command port: {cmdPort}");
        }

        private void StopServers()
        {
            _memoryServer?.Dispose();
            _memoryServer = null;
            _commandServer?.Dispose();
            _commandServer = null;
            BotLog.Shutdown();
        }

        // ── Port from environment variables ───────────────────────────────────────

        private static int GetPort(string envVar, int defaultPort)
        {
            string? val = Environment.GetEnvironmentVariable(envVar);
            return int.TryParse(val, out int port) ? port : defaultPort;
        }

        // ── WinForms UI ───────────────────────────────────────────────────────────

        private void InitializeComponent()
        {
            _statusLabel = new Label
            {
                Text      = "Initialising...",
                Dock      = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Font      = new System.Drawing.Font("Segoe UI", 9f)
            };

            Controls.Add(_statusLabel);
            Text            = "Bot Server";
            Width           = 420;
            Height          = 60;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
        }

        private void UpdateStatus(string text)
        {
            if (InvokeRequired)
                Invoke(new Action(() => _statusLabel.Text = text));
            else
                _statusLabel.Text = text;
        }

        // ── Dispose ───────────────────────────────────────────────────────────────

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                StopServers();
            base.Dispose(disposing);
        }
    }
}
