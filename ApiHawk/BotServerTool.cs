using System;
using System.Windows.Forms;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// BizHawk 2.9.1 external tool — combined memory + command server over TCP.
    ///
    /// Drop-in C# replacement for the Lua MemoryServer.lua + CommandServer.lua scripts.
    /// Uses the same TCP protocol and the same port environment variables:
    ///   BIZHAWK_MEMORY_PORT  — memory server port  (default 50001)
    ///   BIZHAWK_COMMAND_PORT — command server port (default 50000)
    ///
    /// Loaded by EmuHawk via:
    ///   --open-ext-tool-dll=BotServer
    /// </summary>
    [ExternalTool("Bot Server", Description = "TCP memory + command server for ShinyBotHunter")]
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
            int memPort = GetPort("BIZHAWK_MEMORY_PORT",  50001);
            int cmdPort = GetPort("BIZHAWK_COMMAND_PORT", 50000);

            _memoryServer = new MemoryServer(memPort)
            {
                MemoryApi = _memoryApi
            };

            _commandServer = new CommandServer(cmdPort)
            {
                JoypadApi = _joypadApi,
                ClientApi = _clientApi
            };

            _memoryServer.Start();
            _commandServer.Start();

            UpdateStatus($"Listening — memory: {memPort} | command: {cmdPort}");
            Console.WriteLine($"[BotServerTool] Started. Memory port: {memPort}, Command port: {cmdPort}");
        }

        private void StopServers()
        {
            _memoryServer?.Dispose();
            _memoryServer = null;
            _commandServer?.Dispose();
            _commandServer = null;
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
