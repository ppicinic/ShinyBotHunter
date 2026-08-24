using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// HTTP command server — replaces the previous raw-TCP command server.
    ///
    /// Listens on http://127.0.0.1:{port}/ (port read from BIZHAWK_COMMAND_PORT, default 50000).
    /// Binding to the explicit loopback IP avoids needing a Windows URL-ACL reservation.
    ///
    /// Endpoints (all POST):
    ///   /command    — body is a command line (hold_*, release*, macro_string_*, RP_*,
    ///                 macro_clear, flush, or '|'-piped batches). Queued for the frame thread;
    ///                 returns "ok" immediately.
    ///   /wait-macro — long-poll: returns "done" immediately if no macro is outstanding,
    ///                 otherwise blocks until the running macro completes (macro_done).
    ///   /flush      — saves RAM and returns "flushed" once the save lands.
    ///   /ping       — returns "pong".
    ///
    /// Completion model (per the synchronous-bot contract):
    ///   A macro is "outstanding" from the moment a completing command (macro_string_* /
    ///   macro_clear) is submitted on the /command handler thread — not when the frame thread
    ///   gets to it — so a /wait-macro arriving right after a submit can never race past it.
    ///   The frame thread clears the flag and releases all waiters when it reaches the
    ///   MacroEnd marker (or a warp-guard / macro_clear completion).
    ///
    /// Stack-replace rule: when the frame thread drains a /command containing a macro push,
    /// it wipes the current command stack first, so a new macro always replaces a running one.
    ///
    /// Thread model:
    ///   Accept thread  — blocks on HttpListener.GetContext, hands each request to the pool.
    ///   Handler (pool) — parses the request, enqueues commands / registers waiters, responds.
    ///   Frame thread   — BizHawk's UpdateValues(PreFrame) callback; drains the command queue,
    ///                    steps the macro stack, applies joypad state, signals completions.
    /// </summary>
    internal sealed class CommandServer : IDisposable
    {
        // ── Frame-stack types ─────────────────────────────────────────────────────

        private enum FrameAction { Frame, RepeatMacro, MacroEnd }

        private sealed class ButtonState
        {
            public string Name = "";
            public int X;
            public int Y;
            public int FramesLeft;
            public short Destination;
            public uint DestinationAddress;
            public bool IsTouch => Name == "Touch";
            public bool IsMove { get; set; } = false;
            public bool ReachedDestination { get; set; } = false;
            // Optional warp guard: stop the move the moment the live map id matches any of
            // the destination map ids (the raw game ids the target internal map resolves to).
            public bool HasMapCheck { get; set; } = false;
            public uint MapAnchorAddress { get; set; }
            public uint MapNegOffset { get; set; }
            public ushort[] ExpectedMapIds { get; set; } = Array.Empty<ushort>();
        }

        private sealed class StackEntry
        {
            public FrameAction Action;
            public List<ButtonState> Buttons = new List<ButtonState>();
            public int Wait;
            public string MacroStr = "";
        }

        /// <summary>One run of consecutive frames that shared the same input state.</summary>
        private sealed class InputRun
        {
            public int Frames;
            public string Buttons = "";   // comma-joined pressed buttons, "" when none
            public bool Touch;
            public int X;
            public int Y;
        }

        // ── Frame-thread state ────────────────────────────────────────────────────
        private readonly Stack<StackEntry> _commandStack = new Stack<StackEntry>();
        private readonly Dictionary<string, bool> _heldButtons = new Dictionary<string, bool>();
        private bool _clearHold;

        // -- Macro recording ------------------------------------------------------
        // While recording, the frame thread stops driving the joypad entirely so the human's
        // physical input reaches the core untouched, and instead samples what the core is about
        // to consume. _recordRuns is appended by the frame thread and read by handler threads,
        // so every touch of it is under _recordLock.
        private static readonly string[] RecordButtons =
            { "Up", "Down", "Left", "Right", "A", "B", "X", "Y", "L", "R", "Start", "Select" };

        private const int MaxRecordFrames = 108000;   // ~30 min at 60fps -- memory backstop

        private readonly object _recordLock = new object();
        private readonly List<InputRun> _recordRuns = new List<InputRun>();
        private int _recordFrames;
        private volatile bool _recording;
        // Set by the handler thread on start; the frame thread performs the actual stack wipe so
        // _commandStack/_heldButtons are only ever touched from the frame thread.
        private volatile bool _recordResetPending;

        // ── Incoming commands from handler threads → frame thread ─────────────────
        private readonly ConcurrentQueue<string> _commandQueue = new ConcurrentQueue<string>();

        // ── Completion tracking (handler threads <→ frame thread) ─────────────────
        private readonly object _completionLock = new object();
        private bool _macroPending;
        private readonly List<ManualResetEventSlim> _macroWaiters = new List<ManualResetEventSlim>();
        private readonly List<ManualResetEventSlim> _flushWaiters = new List<ManualResetEventSlim>();

        // ── HTTP infrastructure ────────────────────────────────────────────────────
        private readonly int _port;
        private volatile bool _disposed;
        private HttpListener? _listener;
        private Thread? _acceptThread;

        // Long-poll window: how long a /wait-macro request blocks before returning "pending"
        // so the client can poll again. This is NOT a macro-completion timeout — a macro that
        // runs longer than this simply spans multiple poll cycles. Kept well under the client's
        // HTTP timeout so each response arrives in time.
        private const int WaitPollWindowMs = 10000;

        // ── BizHawk APIs ──────────────────────────────────────────────────────────
        public IJoypadApi?    JoypadApi { get; set; }
        public IEmuClientApi? ClientApi { get; set; }
        public IMemoryApi? MemoryApi { get; set; }

        public CommandServer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "CommandServer" };
            _acceptThread.Start();

            BotLog.Write($"[CommandServer] Listening on http://127.0.0.1:{_port}/ ...");
        }

        // ── Called by BotServerTool.UpdateValues(PreFrame) every frame ────────────

        public void ServiceFrame()
        {
            if (_recording)
            {
                if (_recordResetPending)
                {
                    _commandStack.Clear();
                    _heldButtons.Clear();
                    _recordResetPending = false;
                }

                // Drop anything the bot tries to submit mid-recording rather than letting it
                // queue up and fire the moment recording stops.
                while (_commandQueue.TryDequeue(out string _)) { }

                CaptureFrame();
                return;   // never StepStack/ApplyJoypad while recording
            }

            while (_commandQueue.TryDequeue(out string cmd))
            {
                if (cmd == null) continue;

                string[] cmds = cmd.Split('|');

                // A new macro replaces whatever is currently queued.
                if (ContainsMacroPush(cmds))
                {
                    _commandStack.Clear();
                    _heldButtons.Clear();
                }

                for (int i = cmds.Length - 1; i >= 0; i--)
                    HandleCommand(cmds[i]);
            }

            StepStack();
            ApplyJoypad();
        }

        private static bool ContainsMacroPush(string[] cmds)
        {
            foreach (string c in cmds)
            {
                if (c.StartsWith("macro_string_") || c.StartsWith("RP_"))
                    return true;
            }
            return false;
        }

        // ── Command handler (runs on frame thread) ────────────────────────────────

        private void HandleCommand(string msg)
        {
            if (msg == "disconnect")
                return;

            string[] parts = msg.Split('_');

            if (parts[0] == "hold")
            {
                string button = parts.Length > 1 ? parts[1] : "";
                int frames = parts.Length > 2 && int.TryParse(parts[2], out int f) ? f : 0;

                if (frames > 0)
                {
                    _commandStack.Push(new StackEntry
                    {
                        Action  = FrameAction.Frame,
                        Buttons = new List<ButtonState> { new ButtonState { Name = button, FramesLeft = frames } },
                        Wait    = 0
                    });
                }
                else
                {
                    _heldButtons[button] = true;
                }
            }
            else if (msg == "release")
            {
                _heldButtons.Clear();
            }
            else if (parts[0] == "release" && parts.Length > 1)
            {
                _heldButtons.Remove(parts[1]);
            }
            else if (parts[0] == "macro" && parts.Length > 1 && parts[1] == "string")
            {
                string macroStr = msg.Length > 13 ? msg.Substring(13) : "";
                PushMacroString(macroStr, repeat: false);
            }
            else if (parts[0] == "RP")
            {
                string macroStr = msg.Length > 3 ? msg.Substring(3) : "";
                PushMacroString(macroStr, repeat: true);
            }
            else if (msg == "macro_clear")
            {
                _commandStack.Clear();
                _heldButtons.Clear();
                SignalMacroDone();
            }
            else if (msg == "flush")
            {
                ClientApi?.SaveRam();
                SignalFlushDone();
            }
        }

        // ── Macro parsing ─────────────────────────────────────────────────────────

        private List<StackEntry> ParseFrameCommands(string macroStr)
        {
            var frames = new List<StackEntry>();

            foreach (string frameStr in macroStr.Split(';'))
            {
                if (string.IsNullOrEmpty(frameStr)) continue;

                var entry = new StackEntry { Action = FrameAction.Frame };

                foreach (string cmdStr in frameStr.Split(':'))
                {
                    if (string.IsNullOrEmpty(cmdStr)) continue;
                    string[] p = cmdStr.Split('.');

                    string action = p[0];

                    if (action == "W")
                    {
                        entry.Wait = p.Length > 1 && int.TryParse(p[1], out int w) ? w : 1;
                    }
                    else if(action == "M")
                    {
                        string button = p[1];
                        short dest = short.Parse(p[2]);
                        uint memAddr = uint.Parse(p[3]);
                        var bs = new ButtonState { Name = button, Destination = dest, DestinationAddress = memAddr, IsMove = true };
                        if (p.Length >= 7)
                        {
                            bs.HasMapCheck = true;
                            bs.MapAnchorAddress = uint.Parse(p[4]);
                            bs.MapNegOffset = uint.Parse(p[5]);
                            var ids = new ushort[p.Length - 6];
                            for (int k = 6; k < p.Length; k++)
                                ids[k - 6] = ushort.Parse(p[k]);
                            bs.ExpectedMapIds = ids;
                        }
                        entry.Buttons.Add(bs);
                    }
                    else if (action == "S")
                    {
                        int x = p.Length > 1 && int.TryParse(p[1], out int sx) ? sx : 0;
                        int y = p.Length > 2 && int.TryParse(p[2], out int sy) ? sy : 0;
                        int f = p.Length > 3 && int.TryParse(p[3], out int sf) ? sf : 1;
                        entry.Buttons.Add(new ButtonState { Name = "Touch", X = x, Y = y, FramesLeft = f });
                    }
                    else if (action == "H")
                    {
                        string button = p.Length > 1 ? p[1] : "";
                        int num = p.Length > 2 && int.TryParse(p[2], out int hn) ? hn : 1;

                        if (num == 1)
                            _heldButtons[button] = true;
                        else
                            entry.Buttons.Add(new ButtonState { Name = button, FramesLeft = num });
                    }
                    else if (action == "R")
                    {
                        string button = p.Length > 1 ? p[1] : "";
                        _heldButtons.Remove(button);
                    }
                    else
                    {
                        string button = p.Length > 1 ? p[1] : "";
                        int num = p.Length > 2 && int.TryParse(p[2], out int bn) ? bn : 1;
                        entry.Buttons.Add(new ButtonState { Name = button, FramesLeft = num });
                    }
                }

                frames.Add(entry);
            }

            return frames;
        }

        private void PushMacroString(string macroStr, bool repeat)
        {
            var frames = ParseFrameCommands(macroStr);

            if (repeat)
                _commandStack.Push(new StackEntry { Action = FrameAction.RepeatMacro, MacroStr = macroStr });
            else if (_commandStack.Count ==  0)
                _commandStack.Push(new StackEntry { Action = FrameAction.MacroEnd });

            for (int i = frames.Count - 1; i >= 0; i--)
                _commandStack.Push(frames[i]);
        }

        // ── Stack stepper ─────────────────────────────────────────────────────────
        private short to_s16(uint u32)
        {
            return (short)(((u32 / 65536) + 32768) % 65536 - 32768);
        }

        private ushort ReadCurrentMapId(ButtonState btn)
        {
            uint anchor = BinaryPrimitives.ReadUInt32LittleEndian(MemoryApi!.ReadByteRange(btn.MapAnchorAddress, 4).ToArray());
            return BinaryPrimitives.ReadUInt16LittleEndian(MemoryApi!.ReadByteRange(anchor - btn.MapNegOffset, 2).ToArray());
        }

        private void StepStack()
        {
            if (_commandStack.Count == 0) return;

            var top = _commandStack.Peek();
            if (top.Action == FrameAction.RepeatMacro)
            {
                _commandStack.Pop();
                PushMacroString(top.MacroStr, repeat: true);
                top = _commandStack.Peek();
            }
            if (top.Action == FrameAction.Frame)
            {
                bool stillActive = false;

                if (top.Wait > 0)
                {
                    top.Wait--;
                    if (top.Wait > 0) stillActive = true;
                }

                foreach (var btn in top.Buttons)
                {
                    if (btn.IsMove)
                    {
                        var data = to_s16(BinaryPrimitives.ReadUInt32LittleEndian(MemoryApi!.ReadByteRange(btn.DestinationAddress, 4).ToArray()));
                        bool coordReached = data == btn.Destination;
                        bool mapChanged = false;
                        if (btn.HasMapCheck)
                        {
                            ushort currentMapId = ReadCurrentMapId(btn);
                            foreach (var expected in btn.ExpectedMapIds)
                            {
                                if (currentMapId == expected)
                                {
                                    mapChanged = true;
                                    break;
                                }
                            }
                        }
                        if (mapChanged)
                        {
                            _commandStack.Clear();
                            _heldButtons.Clear();
                            SignalMacroDone();
                            return;
                        }
                        if (coordReached)
                            btn.ReachedDestination = true;
                        else
                            stillActive = true;
                    }
                    else if (btn.FramesLeft > 0)
                    {
                        btn.FramesLeft--;
                        if (btn.FramesLeft > 0) stillActive = true;
                    }
                }

                if (!stillActive)
                    _commandStack.Pop();
            }
            else if (top.Action == FrameAction.MacroEnd)
            {
                _commandStack.Pop();
                SignalMacroDone();
            }
        }

        // ── Joypad application ────────────────────────────────────────────────────

        private void ApplyJoypad()
        {
            if (JoypadApi == null) return;

            var buttons = new Dictionary<string, bool>();
            var analog  = new Dictionary<string, int?>();

            foreach (var kvp in _heldButtons)
                buttons[kvp.Key] = true;

            if (_commandStack.Count > 0)
            {
                var top = _commandStack.Peek();
                if (top.Action == FrameAction.Frame)
                {
                    foreach (var btn in top.Buttons)
                    {
                        if (btn.IsMove)
                        {
                            if (!btn.ReachedDestination)
                            {
                                buttons[btn.Name] = true;
                            }
                        }
                        else if (btn.FramesLeft > 0)
                        {
                            if (btn.IsTouch)
                            {
                                buttons["Touch"] = true;
                                analog["Touch X"] = btn.X;
                                analog["Touch Y"] = btn.Y;
                                _clearHold = true;
                            }
                            else
                            {
                                buttons[btn.Name] = true;
                            }
                        }
                    }
                }
            }

            if (analog.Count == 0 && _clearHold)
            {
                ClientApi?.ClearAutohold();
                _clearHold = false;
            }

            if (analog.Count > 0)
                JoypadApi.SetAnalog(analog);

            JoypadApi.Set(buttons);
        }

        // ── Completion signalling (frame thread → waiters) ────────────────────────

        private void SignalMacroDone()
        {
            lock (_completionLock)
            {
                _macroPending = false;
                foreach (var w in _macroWaiters)
                {
                    try { w.Set(); } catch { }
                }
                _macroWaiters.Clear();
            }
        }

        private void SignalFlushDone()
        {
            lock (_completionLock)
            {
                foreach (var w in _flushWaiters)
                {
                    try { w.Set(); } catch { }
                }
                _flushWaiters.Clear();
            }
        }

        // ── HTTP accept loop ────────────────────────────────────────────────────────

        private void AcceptLoop()
        {
            while (!_disposed)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener!.GetContext();
                }
                catch (Exception) when (_disposed)
                {
                    break;
                }
                catch (Exception ex)
                {
                    BotLog.WriteThrottled("cmd-accept-error", $"[CommandServer] Accept error: {ex.Message}");
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => HandleContext(context));
            }
        }

        private void HandleContext(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                switch (path)
                {
                    case "/command":
                        HandleCommandRequest(context);
                        break;
                    case "/wait-macro":
                        HandleWaitRequest(context);
                        break;
                    case "/flush":
                        HandleFlushRequest(context);
                        break;
                    case "/record/start":
                        HandleRecordStart(context);
                        break;
                    case "/record/stop":
                        HandleRecordStop(context);
                        break;
                    case "/record/status":
                        HandleRecordStatus(context);
                        break;
                    case "/ping":
                        Respond(context, "pong");
                        break;
                    default:
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                        break;
                }
            }
            catch (Exception ex) when (!_disposed)
            {
                BotLog.WriteThrottled("cmd-request-exception", $"[CommandServer] Request exception: {ex.Message}");
                try { context.Response.Abort(); } catch { }
            }
        }

        private void HandleCommandRequest(HttpListenerContext context)
        {
            string body = ReadBodyText(context.Request);
            if (!string.IsNullOrEmpty(body))
            {
                string[] cmds = body.Split('|');

                // Mark a macro outstanding on the submitting thread so a /wait-macro that
                // arrives before the frame thread runs cannot race past it.
                if (Completes(cmds, "macro_string_", "macro_clear"))
                    lock (_completionLock) { _macroPending = true; }

                _commandQueue.Enqueue(body);
            }
            Respond(context, "ok");
        }

        private static bool Completes(string[] cmds, params string[] prefixes)
        {
            foreach (string c in cmds)
                foreach (string prefix in prefixes)
                    if (c == prefix || c.StartsWith(prefix))
                        return true;
            return false;
        }

        private void HandleWaitRequest(HttpListenerContext context)
        {
            ManualResetEventSlim mre;
            lock (_completionLock)
            {
                if (!_macroPending)
                {
                    // Nothing running — the macro is (already) done.
                    Respond(context, "done");
                    return;
                }
                mre = new ManualResetEventSlim(false);
                _macroWaiters.Add(mre);
            }

            bool signaled = mre.Wait(WaitPollWindowMs);
            lock (_completionLock)
            {
                _macroWaiters.Remove(mre);
                // Report "done" only if the macro actually completed (we were signalled, or the
                // pending flag cleared during our wait). On a plain timeout, tell the client to
                // poll again so a long macro is never mistaken for a finished one.
                bool done = signaled || !_macroPending;
                mre.Dispose();
                Respond(context, done ? "done" : "pending");
            }
        }

        private void HandleFlushRequest(HttpListenerContext context)
        {
            // Submit the flush and register a waiter atomically.
            var mre = new ManualResetEventSlim(false);
            lock (_completionLock)
            {
                _flushWaiters.Add(mre);
            }
            _commandQueue.Enqueue("flush");

            mre.Wait(WaitPollWindowMs);
            lock (_completionLock) { _flushWaiters.Remove(mre); }
            mre.Dispose();

            Respond(context, "flushed");
        }

        // ── HTTP helpers ──────────────────────────────────────────────────────────

        // ── Macro recording ─────────────────────────────────────────────────────────

        /// <summary>
        /// Frame thread. Samples the input the core is about to consume and run-length-encodes it.
        /// Uses Get() rather than GetImmediate() because Get() reports the state after any tool
        /// overrides -- and while recording we apply none, so it is exactly the human's input,
        /// including the Touch X / Touch Y analog axes.
        /// </summary>
        private void CaptureFrame()
        {
            if (JoypadApi == null) return;
            if (_recordFrames >= MaxRecordFrames) return;

            IReadOnlyDictionary<string, object> state;
            try
            {
                state = JoypadApi.Get(null);
            }
            catch (Exception ex)
            {
                BotLog.WriteThrottled("record-joypad-read", $"[CommandServer] Joypad read failed while recording: {ex.Message}");
                return;
            }

            var pressed = new List<string>();
            foreach (string name in RecordButtons)
            {
                if (state.TryGetValue(name, out object v) && v is bool b && b)
                    pressed.Add(name);
            }

            bool touch = state.TryGetValue("Touch", out object t) && t is bool tb && tb;
            int x = touch ? ReadAxis(state, "Touch X") : 0;
            int y = touch ? ReadAxis(state, "Touch Y") : 0;
            string buttons = string.Join(",", pressed.ToArray());

            lock (_recordLock)
            {
                _recordFrames++;

                if (_recordRuns.Count > 0)
                {
                    var last = _recordRuns[_recordRuns.Count - 1];
                    if (last.Buttons == buttons && last.Touch == touch && last.X == x && last.Y == y)
                    {
                        last.Frames++;
                        return;
                    }
                }

                _recordRuns.Add(new InputRun { Frames = 1, Buttons = buttons, Touch = touch, X = x, Y = y });
            }
        }

        private static int ReadAxis(IReadOnlyDictionary<string, object> state, string key)
        {
            if (!state.TryGetValue(key, out object v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        /// <summary>One run per line: <c>frames|btn1,btn2|x|y</c>; x/y empty when not touching.</summary>
        private string SerializeRuns()
        {
            var sb = new StringBuilder();
            lock (_recordLock)
            {
                foreach (var r in _recordRuns)
                {
                    sb.Append(r.Frames).Append('|').Append(r.Buttons).Append('|');
                    if (r.Touch) sb.Append(r.X).Append('|').Append(r.Y);
                    else sb.Append('|');
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        private void HandleRecordStart(HttpListenerContext context)
        {
            lock (_recordLock)
            {
                _recordRuns.Clear();
                _recordFrames = 0;
            }
            _recordResetPending = true;
            _recording = true;
            BotLog.Write("[CommandServer] Macro recording started.");
            Respond(context, "ok");
        }

        private void HandleRecordStop(HttpListenerContext context)
        {
            _recording = false;
            _recordResetPending = false;
            string body = SerializeRuns();
            int frames;
            lock (_recordLock) { frames = _recordFrames; }
            BotLog.Write($"[CommandServer] Macro recording stopped after {frames} frames.");
            Respond(context, body);
        }

        private void HandleRecordStatus(HttpListenerContext context)
        {
            int runs, frames;
            lock (_recordLock)
            {
                runs = _recordRuns.Count;
                frames = _recordFrames;
            }
            Respond(context, $"{(_recording ? "recording" : "idle")}|{runs}|{frames}");
        }
        private static string ReadBodyText(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            return reader.ReadToEnd().Trim();
        }

        private static void Respond(HttpListenerContext context, string text)
        {
            byte[] buf = Encoding.UTF8.GetBytes(text);
            var resp = context.Response;
            resp.StatusCode = 200;
            resp.ContentType = "text/plain";
            resp.ContentLength64 = buf.Length;
            resp.OutputStream.Write(buf, 0, buf.Length);
            resp.OutputStream.Close();
            resp.Close();
        }

        // ── IDisposable ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            _disposed = true;
            _recording = false;
            _recordResetPending = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }

            // Release any blocked waiters so their requests can return.
            SignalMacroDone();
            SignalFlushDone();
        }
    }
}
