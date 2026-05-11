using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// TCP command server — drop-in C# replacement for the Lua CommandServer.lua.
    ///
    /// Listens on a TCP port (read from BIZHAWK_COMMAND_PORT env var, default 50000).
    /// Protocol is identical to the Lua version — newline-delimited text commands.
    ///
    /// Thread model:
    ///   TCP thread      — dedicated background Thread; blocks on AcceptTcpClient, reads
    ///                     newline-delimited commands, posts them to the frame thread.
    ///   Response thread — dedicated background Thread; blocks on _responseReady, writes
    ///                     responses (macro_done, flushed, pong) back to the client.
    ///   Frame thread    — BizHawk's UpdateValues(PostFrame) callback; checks _commandReady
    ///                     each frame (non-blocking), pushes command onto the stack, steps
    ///                     the stack, applies joypad state, posts responses.
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

        // ── Pending command from TCP thread → frame thread ────────────────────────
        private volatile string? _pendingCommand;

        // ── Pending response from frame thread → response thread ──────────────────
        private volatile string? _pendingResponse;

        // ── Synchronisation ───────────────────────────────────────────────────────
        private readonly SemaphoreSlim _commandReady  = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _responseReady = new SemaphoreSlim(0, 1);

        // ── TCP infrastructure ────────────────────────────────────────────────────
        private readonly int _port;
        private volatile bool _disposed;
        private Thread? _tcpThread;
        private volatile StreamWriter? _activeWriter;

        // ── BizHawk APIs ──────────────────────────────────────────────────────────
        public IJoypadApi?    JoypadApi { get; set; }
        public IEmuClientApi? ClientApi { get; set; }

        public CommandServer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            _tcpThread = new Thread(TcpLoop) { IsBackground = true, Name = "CommandServer" };
            _tcpThread.Start();
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

        // ── TCP loop ──────────────────────────────────────────────────────────────

        private void TcpLoop()
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, _port);
                listener.Start();
                Console.WriteLine($"[CommandServer] Listening on port {_port}...");

                while (!_disposed)
                {
                    TcpClient? client = null;
                    NetworkStream? stream = null;
                    StreamReader? reader = null;
                    StreamWriter? writer = null;
                    Thread? responseThread = null;

                    try
                    {
                        client = listener.AcceptTcpClient();
                        stream = client.GetStream();
                        Console.WriteLine("[CommandServer] Client connected.");

                        reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                        writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

                        _activeWriter = writer;
                        responseThread = new Thread(ResponseForwarderLoop) { IsBackground = true, Name = "CommandServerResponseForwarder" };
                        responseThread.Start();

                        bool disconnected = false;
                        while (!_disposed && !disconnected)
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
                        }
                    }
                    catch (Exception ex) when (!_disposed)
                    {
                        Console.WriteLine($"[CommandServer] Client exception: {ex.Message}");
                    }
                    finally
                    {
                        _activeWriter = null;
                        try { _responseReady.Release(); } catch { }
                        responseThread?.Join(500);

                        reader?.Dispose();
                        writer?.Dispose();
                        stream?.Dispose();
                        client?.Dispose();
                        while (_commandReady.CurrentCount > 0)  _commandReady.Wait(0);
                        while (_responseReady.CurrentCount > 0) _responseReady.Wait(0);
                        Console.WriteLine("[CommandServer] Client disconnected, waiting for new connection...");
                    }
                }
            }
            catch (Exception ex) when (!_disposed)
            {
                Console.WriteLine($"[CommandServer] Listener exception: {ex.Message}");
            }
            finally
            {
                listener?.Stop();
            }
        }

        private void ResponseForwarderLoop()
        {
            while (!_disposed)
            {
                _responseReady.Wait();
                StreamWriter? w = _activeWriter;
                if (w == null) break;

                string resp = _pendingResponse ?? "";
                _pendingResponse = null;
                try
                {
                    w.WriteLine(resp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommandServer] ResponseForwarder write error: {ex.Message}");
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
