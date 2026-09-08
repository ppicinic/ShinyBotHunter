using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;
using System.Net.Http;

namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// HTTP command client.
    ///
    /// Button/macro commands are sent synchronously to <c>POST /command</c> — the call
    /// blocks until the server has acked the submission, which guarantees the macro is
    /// registered as "outstanding" before a following <see cref="WaitForMacroAsync"/> is sent
    /// (so the wait cannot race past it). Completion is a request/response long-poll:
    /// <c>POST /wait-macro</c> returns immediately if nothing is running, otherwise when the
    /// macro finishes. All calls retry on transient transport failures.
    /// </summary>
    public class BizHawkCommandClient : BizHawkClientBase, ICommandClient
    {
        private readonly CommandPreprocessor _preprocessor;

        public BizHawkCommandClient(string host = "127.0.0.1", int port = 50100, ConsoleType console = ConsoleType.DS)
            : base("BizHawkCommandClient", host, port, TimeSpan.FromSeconds(60))
        {
            _preprocessor = new CommandPreprocessor(console);
        }

        public BizHawkCommandClient(GameContext context)
            : base("BizHawkCommandClient", "127.0.0.1", context.CommandPort, TimeSpan.FromSeconds(60))
        {
            _preprocessor = new CommandPreprocessor(context.ConsoleType);
        }

        protected override void Disconnect()
        {
            // Connectionless — nothing to tear down.
        }

        // ── Input ─────────────────────────────────────────────────────────────────

        public void Tap(string button)
            => SendCommand($"hold_{button}_{_preprocessor.DefaultTapFrames}");

        public void Hold(string button)
            => SendCommand($"hold_{button}");

        public void HoldFrames(string button, int frames)
            => SendCommand($"hold_{button}_{frames}");

        public void Release()
            => SendCommand("release");

        public void Release(string button)
            => SendCommand($"release_{button}");

        // ── Macros ────────────────────────────────────────────────────────────────

        public void RunMacroString(string macro)
            => SendCommand($"macro_string_{_preprocessor.Process(macro)}");

        public void RunMacros(params MacroStruct[] macroStructs)
        {
            string macro = String.Join("|", macroStructs.Select(x =>
                $"{(x.repeat ? "RP_" : "macro_string_")}{_preprocessor.Process(x.macro)}"));
            SendCommand(macro);
        }

        public void RunRepeatMacro(string macro)
            => SendCommand($"RP_{_preprocessor.Process(macro)}");

        public void ClearMacro()
            => SendCommand("macro_clear");

        public async Task WaitForMacroAsync(CancellationToken ct = default)
        {
            // Long-poll: the server holds each request for a bounded window and returns "done"
            // only when the macro actually completes, otherwise "pending". Loop until done so a
            // macro longer than one poll window (e.g. walking to the Pokémon Center) is never
            // mistaken for finished.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                string result = await ExecuteWithRetryAsync(async () =>
                {
                    using var resp = await _http.PostAsync("wait-macro", new StringContent(""), ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    return (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
                }, ct).ConfigureAwait(false);

                if (result == "done")
                    return;
                // "pending" — macro still running, poll again.
            }
        }

        public async Task FlushSave(CancellationToken ct = default)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var resp = await _http.PostAsync("flush", new StringContent(""), ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }, ct);
        }

        // -- Macro recording -------------------------------------------------------

        /// <summary>
        /// An emulator running a BotServer.dll built before macro recording existed has no /record/*
        /// route, so its HttpListener answers 404. That is permanent, not transient — surfaced as a
        /// plain HttpRequestException it would burn the full retry budget and then report a bare
        /// "404 (Not Found)" that says nothing about the real cause.
        /// </summary>
        private const string StaleToolMessage =
            "The emulator's BotServer.dll has no /record endpoints, so it predates macro recording. "
            + "Rebuild the ApiHawk project, copy BotServer.dll into your BizHawk ExternalTools folder, "
            + "and restart the emulator.";

        public Task StartRecordingAsync(CancellationToken ct = default)
            => PostRecordAsync("record/start", ct);

        public Task<string> StopRecordingAsync(CancellationToken ct = default)
            => PostRecordAsync("record/stop", ct);

        public Task<string> GetRecordingStatusAsync(CancellationToken ct = default)
            => ExecuteWithRetryAsync(async () =>
            {
                using var resp = await _http.GetAsync("record/status", ct).ConfigureAwait(false);
                ThrowIfStaleTool(resp);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }, ct);

        private Task<string> PostRecordAsync(string path, CancellationToken ct)
            => ExecuteWithRetryAsync(async () =>
            {
                using var resp = await _http.PostAsync(path, new StringContent(""), ct).ConfigureAwait(false);
                ThrowIfStaleTool(resp);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }, ct);

        // InvalidOperationException is deliberate: BizHawkClientBase.IsTransient does not match it, so
        // this fails on the first attempt instead of retrying, and DevController turns it into a 400
        // carrying this message straight to the dashboard banner.
        private static void ThrowIfStaleTool(HttpResponseMessage resp)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(StaleToolMessage);
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        // Synchronous send: blocks until the server acks the command, preserving ordering
        // with a subsequent WaitForMacroAsync.
        private void SendCommand(string line)
        {
            ExecuteWithRetry(() =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "command")
                {
                    Content = new StringContent(line),
                };
                using var resp = _http.Send(req);
                resp.EnsureSuccessStatusCode();
            });
        }
    }
}
