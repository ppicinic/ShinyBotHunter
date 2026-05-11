using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// Manages the command named-pipe server.
    ///
    /// Faithfully reimplements the Lua CommandServer frame-stack logic in C#.
    ///
    /// Thread model:
    ///   Pipe thread     — dedicated background Thread; blocks on WaitForConnection, reads
    ///                     newline-delimited commands, posts them to the frame thread.
    ///   Response thread — dedicated background Thread; blocks on _responseReady, writes
    ///                     responses (macro_done, flushed, pong) back to the client.
    ///   Frame thread    — BizHawk's UpdateValues(PostFrame) callback (synchronous); checks
    ///                     _commandReady each frame (non-blocking), pushes command onto the
    ///                     stack, steps the stack, applies joypad state, posts responses.
    ///
    /// Protocol (identical to the Lua CommandServer):
    ///   Commands (client → server, newline-terminated):
    ///     hold_{button}_{frames}   — press button for N frames
    ///     hold_{button}            — hold button indefinitely
    ///     release                  — release all held buttons
    ///     release_{button}         — release one button
    ///     macro_string_{macro}     — run one-shot macro, responds "macro_done"
    ///     RP_{macro}               — run repeating macro (no response)
    ///     macro_clear              — clear stack + held buttons, responds "macro_done"
    ///     flush                    — call SaveRam(), responds "flushed"
    ///     ping                     — responds "pong"
    ///     disconnect               — clean disconnect
    ///   Responses (server → client, newline-terminated):
    ///     macro_done, flushed, pong
    /// </summary>
    internal sealed class CommandPipeServer : IDisposable
    {
        // ── Frame-stack types ─────────────────────────────────────────────────────

        private enum FrameAction { Frame, RepeatMacro, MacroEnd }

        private sealed class ButtonState
        {
            public string Name = "";
            public int X;
            public int Y;
            public int FramesLeft;
            public bool IsTouch => Name == "Touch";
        }

        private sealed class StackEntry
        {
            public FrameAction Action;
            public List<ButtonState> Buttons = new List<ButtonState>();
            public int Wait;
            public string MacroStr = "";
        }

        // ── Frame-thread state ────────────────────────────────────────────────────
        private readonly Stack<StackEntry> _commandStack = new Stack<StackEntry>();
        private readonly Dictionary<string, bool> _heldButtons = new Dictionary<string, bool>();
        private bool _clearHold;

        // ── Pending command from pipe thread → frame thread ───────────────────────
        private volatile string? _pendingCommand;

        // ── Pending response from frame thread → response thread ──────────────────
        private volatile string? _pendingResponse;

        // ── Synchronisation ───────────────────────────────────────────────────────
        private readonly SemaphoreSlim _commandReady  = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _responseReady = new SemaphoreSlim(0, 1);

        // ── Pipe infrastructure ───────────────────────────────────────────────────
        private readonly string _pipeName;
        private volatile bool _disposed;
        private Thread? _pipeThread;
        private volatile StreamWriter? _activeWriter; // shared with response thread

        // ── BizHawk APIs ──────────────────────────────────────────────────────────
        public IJoypadApi?    JoypadApi { get; set; }
        public IEmuClientApi? ClientApi { get; set; }

        public CommandPipeServer(string pipeName)
        {
            _pipeName = pipeName;
        }

        // ── Called once to start the pipe listener ────────────────────────────────

        public void Start()
        {
            _pipeThread = new Thread(PipeLoop) { IsBackground = true, Name = "CommandPipeServer" };
            _pipeThread.Start();
        }

        // ── Called by BotServerTool.UpdateValues(PostFrame) every frame ───────────

        public void ServiceFrame()
        {
            if (_commandReady.Wait(0))
            {
                string? cmd = _pendingCommand;
                _pendingCommand = null;
                if (cmd != null)
                    HandleCommand(cmd);
            }

            StepStack();
            ApplyJoypad();
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
                PostResponse("macro_done");
            }
            else if (msg == "flush")
            {
                ClientApi?.SaveRam();
                PostResponse("flushed");
            }
            else if (msg == "ping")
            {
                PostResponse("pong");
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
            else
                _commandStack.Push(new StackEntry { Action = FrameAction.MacroEnd });

            for (int i = frames.Count - 1; i >= 0; i--)
                _commandStack.Push(frames[i]);
        }

        // ── Stack stepper ─────────────────────────────────────────────────────────

        private void StepStack()
        {
            if (_commandStack.Count == 0) return;

            var top = _commandStack.Peek();

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
                    if (btn.FramesLeft > 0)
                    {
                        btn.FramesLeft--;
                        if (btn.FramesLeft > 0) stillActive = true;
                    }
                }

                if (!stillActive)
                    _commandStack.Pop();
            }
            else if (top.Action == FrameAction.RepeatMacro)
            {
                _commandStack.Pop();
                PushMacroString(top.MacroStr, repeat: true);
            }
            else if (top.Action == FrameAction.MacroEnd)
            {
                _commandStack.Pop();
                PostResponse("macro_done");
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
                        if (btn.FramesLeft > 0)
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

        // ── Response helper ───────────────────────────────────────────────────────

        private void PostResponse(string response)
        {
            _pendingResponse = response;
            _responseReady.Release();
        }

        // ── Synchronous pipe loop (runs on dedicated background thread) ──────────

        private void PipeLoop()
        {
            while (!_disposed)
            {
                NamedPipeServerStream? pipe = null;
                StreamReader? reader = null;
                StreamWriter? writer = null;
                Thread? responseThread = null;

                try
                {
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None);

                    Console.WriteLine($"[CommandPipeServer] Waiting for connection on pipe '{_pipeName}'...");
                    pipe.WaitForConnection(); // synchronous block
                    Console.WriteLine("[CommandPipeServer] Client connected.");

                    reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    writer = new StreamWriter(pipe, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

                    // Start a response forwarder thread to send async responses (macro_done, flushed, pong)
                    _activeWriter = writer;
                    responseThread = new Thread(ResponseForwarderLoop) { IsBackground = true, Name = "CommandPipeResponseForwarder" };
                    responseThread.Start();

                    bool disconnected = false;
                    while (!_disposed && pipe.IsConnected && !disconnected)
                    {
                        string? line = reader.ReadLine();
                        if (line == null) break;

                        if (line == "disconnect")
                        {
                            disconnected = true;
                            break;
                        }

                        _pendingCommand = line;
                        _commandReady.Release();
                        // All responses are forwarded by the response thread
                    }
                }
                catch (Exception ex) when (!_disposed)
                {
                    Console.WriteLine($"[CommandPipeServer] Exception: {ex.Message}");
                }
                finally
                {
                    _activeWriter = null;
                    // Signal response thread to stop
                    try { _responseReady.Release(); } catch { }
                    responseThread?.Join(500);

                    reader?.Dispose();
                    writer?.Dispose();
                    try { pipe?.Disconnect(); } catch { }
                    try { pipe?.Dispose(); } catch { }
                    while (_commandReady.CurrentCount > 0)  _commandReady.Wait(0);
                    while (_responseReady.CurrentCount > 0) _responseReady.Wait(0);
                    Console.WriteLine("[CommandPipeServer] Pipe closed, waiting for new connection...");
                }

                if (!_disposed)
                    Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Forwards responses from the frame thread to the pipe writer.
        /// Runs on a dedicated thread so it doesn't block the command-reading loop.
        /// </summary>
        private void ResponseForwarderLoop()
        {
            while (!_disposed)
            {
                _responseReady.Wait();
                StreamWriter? w = _activeWriter;
                if (w == null) break; // connection closed

                string resp = _pendingResponse ?? "";
                _pendingResponse = null;
                try
                {
                    w.WriteLine(resp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommandPipeServer] ResponseForwarder write error: {ex.Message}");
                    break;
                }
            }
        }

        // ── IDisposable ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            _disposed = true;
            _activeWriter = null;
            try { _responseReady.Release(); } catch { }
            try { _commandReady.Release(); } catch { }
            _commandReady.Dispose();
            _responseReady.Dispose();
        }
    }
}
