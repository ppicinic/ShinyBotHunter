using ShinyBotHunter.Macro;
using System.IO.Pipes;
using System.Text;

namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// Named-pipe implementation of <see cref="ICommandClient"/>.
    /// Drop-in replacement for <see cref="BizHawkCommandClient"/> when BizHawkMode = "ApiHawk".
    ///
    /// Protocol is identical to the TCP version — newline-delimited text commands.
    /// <see cref="NamedPipeClientStream"/> inherits <see cref="System.IO.Stream"/> so
    /// <see cref="StreamReader"/>/<see cref="StreamWriter"/> work identically.
    ///
    /// Reconnection: on <see cref="IOException"/> or <see cref="ObjectDisposedException"/>
    /// the client waits 500 ms and retries up to <see cref="MaxRetries"/> times.
    /// </summary>
    public class BizHawkApiHawkCommandClient : ICommandClient
    {
        private const int MaxRetries = 5;
        private const int RetryDelayMs = 500;

        private readonly string _pipeName;
        private NamedPipeClientStream _pipe = null!;
        private StreamWriter? _writer;
        private StreamReader? _reader;

        private readonly CancellationTokenSource _cts = new();
        private readonly CommandPreprocessor _preprocessor;

        public event Action? OnMacroDone;
        public event Action? OnFlushDone;

        public BizHawkApiHawkCommandClient(string pipeName)
        {
            _pipeName = pipeName;
            _preprocessor = new CommandPreprocessor();
        }

        // ── ICommandClient ────────────────────────────────────────────────────────

        public void Connect()
        {
            Task.Delay(3000).Wait();
            Console.WriteLine($"[BizHawkApiHawkCommandClient] Attempting to connect to pipe '{_pipeName}'");
            _pipe = CreateAndConnect();
            _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };
            _reader = new StreamReader(_pipe, Encoding.UTF8);
            _ = ListenForEventsAsync(_cts.Token);
            Console.WriteLine($"[BizHawkApiHawkCommandClient] Connected to pipe '{_pipeName}'");
        }

        // ── Input ─────────────────────────────────────────────────────────────────

        public void Tap(string button)
            => SendLine($"hold_{button}_{_preprocessor.DefaultTapFrames}");

        public void Hold(string button)
            => SendLine($"hold_{button}");

        public void HoldFrames(string button, int frames)
            => SendLine($"hold_{button}_{frames}");

        public void Release()
            => SendLine("release");

        public void Release(string button)
            => SendLine($"release_{button}");

        // ── Macros ────────────────────────────────────────────────────────────────

        public void RunMacroString(string macro)
        {
            string converted = _preprocessor.Process(macro);
            SendLine($"macro_string_{converted}");
        }

        public void RunMacros(params MacroStruct[] macroStructs)
        {
            string macro = string.Join("|", macroStructs.Select(x =>
                $"{(x.repeat ? "RP_" : "macro_string_")}{_preprocessor.Process(x.macro)}"));
            SendLine(macro);
        }

        public void RunRepeatMacro(string macro)
        {
            string converted = _preprocessor.Process(macro);
            SendLine($"RP_{converted}");
        }

        public void ClearMacro()
            => SendLine("macro_clear");

        public async Task WaitForMacroAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource();
            OnMacroDone += Complete;
            await tcs.Task.WaitAsync(ct);
            OnMacroDone -= Complete;
            void Complete() => tcs.TrySetResult();
        }

        public async Task FlushSave(CancellationToken ct = default)
        {
            SendLine("flush");
            var tcs = new TaskCompletionSource();
            OnFlushDone += Complete;
            await tcs.Task.WaitAsync(ct);
            OnFlushDone -= Complete;
            void Complete() => tcs.TrySetResult();
        }

        // -- Macro recording -------------------------------------------------------
        // Recording is HTTP-only. This named-pipe client predates the HTTP command server and is not
        // constructed by BotFactory; rather than invent a pipe framing for the record commands that
        // nothing would exercise, these fail loudly if the client is ever wired back up.

        private const string RecordingUnsupported =
            "Macro recording is only implemented for the HTTP command server (BizHawkCommandClient).";

        public Task StartRecordingAsync(CancellationToken ct = default)
            => throw new NotSupportedException(RecordingUnsupported);

        public Task<string> StopRecordingAsync(CancellationToken ct = default)
            => throw new NotSupportedException(RecordingUnsupported);

        public Task<string> GetRecordingStatusAsync(CancellationToken ct = default)
            => throw new NotSupportedException(RecordingUnsupported);

        // ── IAsyncDisposable ──────────────────────────────────────────────────────

        public ValueTask DisposeAsync()
        {
            try { SendLine("disconnect"); } catch { }
            _cts.Cancel();
            _writer?.Dispose();
            _reader?.Dispose();
            _pipe.Dispose();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private void SendLine(string line)
        {
            try
            {
                _writer!.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Console.WriteLine($"[BizHawkApiHawkCommandClient] Pipe error ({ex.GetType().Name}), reconnecting...");
                Reconnect();
                _writer!.WriteLine(line);
            }
        }

        private async Task ListenForEventsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    string? line = await _reader!.ReadLineAsync(ct);
                    if (line == null) break;
                    if (line == "macro_done") OnMacroDone?.Invoke();
                    if (line == "flushed")    OnFlushDone?.Invoke();
                    if (line == "pong")       Console.WriteLine("[BizHawkApiHawkCommandClient] pong");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BizHawkApiHawkCommandClient] ListenForEvents error: {ex.Message}");
                    break;
                }
            }
        }

        private void Reconnect()
        {
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _pipe.Dispose(); } catch { }

            _pipe = CreateAndConnect();
            _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };
            _reader = new StreamReader(_pipe, Encoding.UTF8);
            _ = ListenForEventsAsync(_cts.Token);
            Console.WriteLine($"[BizHawkApiHawkCommandClient] Reconnected to pipe '{_pipeName}'");
        }

        private NamedPipeClientStream CreateAndConnect()
        {
            // Note: PipeOptions.None (not Asynchronous) — the server uses Asynchronous internally
            // but the client can use synchronous I/O; StreamReader/Writer handle buffering.
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(timeout: 30_000);
            return pipe;
        }
    }
}
