using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;
using System.Text;
using System.Text.RegularExpressions;

namespace ShinyBotHunter.Emulators
{
    public class BizHawkCommandClient : BizHawkClientBase, ICommandClient
    {
        private StreamWriter? _writer;
        private StreamReader? _reader;

        public event Action? OnMacroDone;
        public event Action? OnFlushDone;

        private readonly CancellationTokenSource _cts = new();
        private readonly CommandPreprocessor _preprocessor;

        public BizHawkCommandClient(string host = "127.0.0.1", int port = 50000)
            : base("BizHawkCommandClient", host, port) {
            _preprocessor = new CommandPreprocessor();
        }

        public BizHawkCommandClient(GameContext context)
            : base("BizHawkCommandClient", "127.0.0.1", context.CommandPort)
        {
            _preprocessor = new CommandPreprocessor();
        }

        public new void Connect()
        {
            base.Connect();
            _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
            _reader = new StreamReader(_stream, Encoding.UTF8);
            _ = ListenForEventsAsync(_cts.Token);
        }

        protected override void Disconnect()
        {
            _writer?.WriteLine("disconnect");
            _writer?.Flush();
            Console.WriteLine("disposed");
        }

        // ── Input ─────────────────────────────────────────────────────────────────

        public void Tap(string button)
            => _writer!.WriteLine($"hold_{button}_{_preprocessor.DefaultTapFrames}");

        public void Hold(string button)
            => _writer!.WriteLine($"hold_{button}");

        public void HoldFrames(string button, int frames)
            => _writer!.WriteLine($"hold_{button}_{frames}");

        public void Release()
            => _writer!.WriteLine("release");

        public void Release(string button)
            => _writer!.WriteLine($"release_{button}");

        // ── Macros ────────────────────────────────────────────────────────────────

        public void RunMacroString(string macro)
        {
            string converted = _preprocessor.Process(macro);
            _writer!.WriteLine($"macro_string_{converted}");
        }

        public void RunRepeatMacro(string macro)
        {
            string converted = _preprocessor.Process(macro);
            _writer!.WriteLine($"RP_{converted}");
        }
        public void ClearMacro()
            => _writer!.WriteLine("macro_clear");

        public async Task WaitForMacroAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource();
            OnMacroDone += Complete;
            await tcs.Task.WaitAsync(ct);
            OnMacroDone -= Complete;
            void Complete() => tcs.TrySetResult(); 
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private async Task ListenForEventsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await _reader!.ReadLineAsync(ct);
                if (line == null) break;
                if (line == "macro_done") OnMacroDone?.Invoke();
                if (line == "flushed") OnFlushDone?.Invoke();
                if (line == "pong") Console.WriteLine("[BizHawkCommandClient] pong");
            }
        }

        public override ValueTask DisposeAsync()
        {
            Console.WriteLine("im disposing");
            try { Disconnect(); } catch { }
            //Thread.Sleep(10000);
            _cts.Cancel();
            _writer?.Dispose();
            _reader?.Dispose();
            _stream.Dispose();
            _tcp.Dispose();
            return ValueTask.CompletedTask;
            
        }

        public async Task FlushSave(CancellationToken ct = default)
        {
            _writer!.WriteLine("flush");
            var tcs = new TaskCompletionSource();
            OnFlushDone += Complete;
            await tcs.Task.WaitAsync(ct);
            OnFlushDone -= Complete;
            void Complete() => tcs.TrySetResult();
        }
    }
}
