using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// Manages the memory named-pipe server.
    ///
    /// Thread model:
    ///   Pipe thread  — dedicated background Thread; blocks on WaitForConnection, reads 9-byte headers,
    ///                  posts work to the frame thread via _requestReady, blocks on _requestComplete.
    ///   Frame thread — BizHawk's UpdateValues(PostFrame) callback (synchronous); checks _requestReady
    ///                  each frame (non-blocking), calls IMemoryApi, stores result,
    ///                  releases _requestComplete.
    ///
    /// Synchronisation:
    ///   _requestReady    : SemaphoreSlim(0,1) — pipe thread releases, frame thread acquires (TryWait).
    ///   _requestComplete : SemaphoreSlim(0,1) — frame thread releases, pipe thread blocks on Wait().
    ///
    /// Protocol (identical to the Lua MemoryServer):
    ///   Request  : [cmd:1][addr:4 LE][aux:4 LE]
    ///   cmd 0x01 : read  aux bytes from addr  → response: aux bytes
    ///   cmd 0x02 : write aux&amp;0xFF to addr      → response: 0x01 (ack)
    ///   cmd 0xFF : disconnect
    /// </summary>
    internal sealed class MemoryPipeServer : IDisposable
    {
        // ── Shared state written by pipe thread, read by frame thread ───────────────
        private volatile byte _pendingCmd;
        private volatile uint _pendingAddr;
        private volatile uint _pendingAux;

        // ── Response written by frame thread, read by pipe thread ───────────────────
        private volatile byte[]? _responseData;

        // ── Synchronisation ───────────────────────────────────────────────────────
        private readonly SemaphoreSlim _requestReady    = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _requestComplete = new SemaphoreSlim(0, 1);

        // ── Pipe infrastructure ───────────────────────────────────────────────────
        private readonly string _pipeName;
        private volatile bool _disposed;
        private Thread? _pipeThread;

        // ── BizHawk API (set before first UpdateValues call) ──────────────────────
        public IMemoryApi? MemoryApi { get; set; }

        public MemoryPipeServer(string pipeName)
        {
            _pipeName = pipeName;
        }

        // ── Called once to start the pipe listener ────────────────────────────────

        public void Start()
        {
            _pipeThread = new Thread(PipeLoop) { IsBackground = true, Name = "MemoryPipeServer" };
            _pipeThread.Start();
        }

        // ── Called by BotServerTool.UpdateValues(PostFrame) every frame ───────────

        public void ServiceFrame()
        {
            if (!_requestReady.Wait(0))
                return;

            if (MemoryApi == null)
            {
                _responseData = Array.Empty<byte>();
                _requestComplete.Release();
                return;
            }

            byte cmd  = _pendingCmd;
            uint addr = _pendingAddr;
            uint aux  = _pendingAux;

            if (cmd == 0x01)
            {
                IReadOnlyList<byte> list = MemoryApi.ReadByteRange((long)addr, (int)aux);
                byte[] buf = new byte[list.Count];
                for (int i = 0; i < list.Count; i++)
                    buf[i] = list[i];
                _responseData = buf;
            }
            else if (cmd == 0x02)
            {
                MemoryApi.WriteByte((long)addr, aux & 0xFF);
                _responseData = new byte[] { 0x01 };
            }
            else
            {
                _responseData = Array.Empty<byte>();
            }

            _requestComplete.Release();
        }

        // ── Synchronous pipe loop (runs on dedicated background thread) ──────────

        private void PipeLoop()
        {
            while (!_disposed)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None);

                    Console.WriteLine($"[MemoryPipeServer] Waiting for connection on pipe '{_pipeName}'...");
                    pipe.WaitForConnection(); // synchronous block
                    Console.WriteLine("[MemoryPipeServer] Client connected.");

                    var header = new byte[9];
                    while (!_disposed && pipe.IsConnected)
                    {
                        if (!ReadExact(pipe, header))
                            break;

                        byte cmd  = header[0];
                        uint addr = ReadU32Le(header, 1);
                        uint aux  = ReadU32Le(header, 5);

                        if (cmd == 0xFF)
                        {
                            Console.WriteLine("[MemoryPipeServer] Disconnect requested.");
                            break;
                        }

                        _pendingCmd  = cmd;
                        _pendingAddr = addr;
                        _pendingAux  = aux;
                        _requestReady.Release();

                        // Block until frame thread services the request (with timeout)
                        if (!_requestComplete.Wait(5000))
                        {
                            Console.WriteLine("[MemoryPipeServer] Frame thread timeout — disconnecting.");
                            break;
                        }

                        byte[] resp = _responseData ?? Array.Empty<byte>();
                        pipe.Write(resp, 0, resp.Length);
                        pipe.Flush();
                    }
                }
                catch (Exception ex) when (!_disposed)
                {
                    Console.WriteLine($"[MemoryPipeServer] Exception: {ex.Message}");
                }
                finally
                {
                    try { pipe?.Disconnect(); } catch { }
                    try { pipe?.Dispose(); } catch { }
                    while (_requestReady.CurrentCount > 0)    _requestReady.Wait(0);
                    while (_requestComplete.CurrentCount > 0) _requestComplete.Wait(0);
                    Console.WriteLine("[MemoryPipeServer] Pipe closed, waiting for new connection...");
                }

                if (!_disposed)
                    Thread.Sleep(200);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static bool ReadExact(System.IO.Stream stream, byte[] buf)
        {
            int offset = 0;
            while (offset < buf.Length)
            {
                int read = stream.Read(buf, offset, buf.Length - offset);
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        private static uint ReadU32Le(byte[] buf, int offset)
        {
            return (uint)(buf[offset]
                | (buf[offset + 1] << 8)
                | (buf[offset + 2] << 16)
                | (buf[offset + 3] << 24));
        }

        // ── IDisposable ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            _disposed = true;
            try { _requestComplete.Release(); } catch { }
            try { _requestReady.Release(); } catch { }
            _requestReady.Dispose();
            _requestComplete.Dispose();
        }
    }
}
