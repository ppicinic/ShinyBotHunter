using System.Net.Sockets;
using System.Text;

namespace ShinyBotHunter.Emulators
{

    public class BizHawkClient : IDisposable
    {
        private readonly TcpClient _cmdTcp;
        private readonly NetworkStream _cmdStream;
        private readonly StreamWriter _cmdWriter;
        private readonly SemaphoreSlim _cmdLock = new(1, 1);

        private readonly TcpClient _memTcp;
        private readonly NetworkStream _memStream;
        private readonly StreamWriter _memWriter;
        private readonly StreamReader _memReader;
        private readonly SemaphoreSlim _memLock = new(1, 1);

        // Fires when a macro completes
        public event Action? OnMacroDone;

        private readonly CancellationTokenSource _cts = new();

        public BizHawkClient(string host = "127.0.0.1", int cmdPort = 12345, int memPort = 12346)
        {
            // Command channel
            _cmdTcp = new TcpClient(host, cmdPort);
            _cmdStream = _cmdTcp.GetStream();
            _cmdWriter = new StreamWriter(_cmdStream, Encoding.UTF8) { AutoFlush = true };

            // Memory channel
            _memTcp = new TcpClient(host, memPort);
            _memStream = _memTcp.GetStream();
            _memWriter = new StreamWriter(_memStream, Encoding.UTF8) { AutoFlush = true };
            _memReader = new StreamReader(_memStream, Encoding.UTF8);

            // Listen for async events from Lua (macro_done etc.)
            _ = ListenForEventsAsync(_cts.Token);
        }

        // ── Input commands ────────────────────────────────────────────────────────

        public void Tap(string button)
            => SendCommand($"tap_{button}");

        public void Hold(string button)
            => SendCommand($"hold_{button}");

        public void HoldFrames(string button, int frames)
            => SendCommand($"hold_{button}_{frames}");

        public void Release()
            => SendCommand("release");

        public void Release(string button)
            => SendCommand($"release_{button}");

        // ── Macros ────────────────────────────────────────────────────────────────

        public void StartMacro(string name, params string[] args)
        {
            string argStr = args.Length > 0 ? "_" + string.Join("_", args) : "";
            SendCommand($"macro_{name}{argStr}");
        }

        public async Task WaitForMacroAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource();
            OnMacroDone += Complete;
            await tcs.Task.WaitAsync(ct);
            OnMacroDone -= Complete;
            void Complete() => tcs.TrySetResult();
        }

        // ── Memory reads ──────────────────────────────────────────────────────────

        public async Task<byte> ReadByteAsync(uint address) => byte.Parse(await MemRequestAsync($"rb:{address}"));
        public async Task<ushort> ReadUShortAsync(uint address) => ushort.Parse(await MemRequestAsync($"rw:{address}"));
        public async Task<uint> ReadUIntAsync(uint address) => uint.Parse(await MemRequestAsync($"rd:{address}"));
        public async Task WriteByteAsync(uint address, byte value) => await MemRequestAsync($"wb:{address}:{value}");

        // ── Internal ──────────────────────────────────────────────────────────────

        private void SendCommand(string cmd)
        {
            _cmdLock.Wait();
            try { _cmdWriter.WriteLine(cmd); }
            finally { _cmdLock.Release(); }
        }

        private async Task<string> MemRequestAsync(string request)
        {
            await _memLock.WaitAsync();
            try
            {
                await _memWriter.WriteLineAsync(request);
                return await _memReader.ReadLineAsync() ?? "";
            }
            finally { _memLock.Release(); }
        }

        private async Task ListenForEventsAsync(CancellationToken ct)
        {
            var reader = new StreamReader(_cmdStream, Encoding.UTF8);
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (line == "macro_done") OnMacroDone?.Invoke();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cmdWriter.Dispose(); _cmdStream.Dispose(); _cmdTcp.Dispose();
            _memWriter.Dispose(); _memReader.Dispose(); _memStream.Dispose(); _memTcp.Dispose();
        }
    }
}
