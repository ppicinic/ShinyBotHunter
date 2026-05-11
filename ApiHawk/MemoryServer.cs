using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// TCP memory server — drop-in C# replacement for the Lua MemoryServer.lua.
    ///
    /// Listens on a TCP port (read from BIZHAWK_MEMORY_PORT env var, default 50001).
    /// Protocol is identical to the Lua version:
    ///   Request  : [cmd:1][addr:4 LE][aux:4 LE]
    ///   cmd 0x01 : read  aux bytes from addr  → response: aux bytes
    ///   cmd 0x02 : write aux&amp;0xFF to addr      → response: 0x01 (ack)
    ///   cmd 0xFF : disconnect
    ///
    /// Thread model:
    ///   TCP thread   — dedicated background Thread; blocks on AcceptTcpClient, reads 9-byte headers,
    ///                  posts work to the frame thread via _requestReady, blocks on _requestComplete.
    ///   Frame thread — BizHawk's UpdateValues(PostFrame) callback; checks _requestReady each frame
    ///                  (non-blocking), calls IMemoryApi, stores result, releases _requestComplete.
    /// </summary>
    internal sealed class MemoryServer : IDisposable
    {
        // ── Shared state written by TCP thread, read by frame thread ─────────────
        private volatile byte _pendingCmd;
        private volatile uint _pendingAddr;
        private volatile uint _pendingAux;

        // ── Response written by frame thread, read by TCP thread ──────────────────
        private volatile byte[]? _responseData;

        // ── Synchronisation ───────────────────────────────────────────────────────
        private readonly SemaphoreSlim _requestReady    = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _requestComplete = new SemaphoreSlim(0, 1);

        // ── TCP infrastructure ────────────────────────────────────────────────────
        private readonly int _port;
        private volatile bool _disposed;
        private Thread? _tcpThread;

        // ── BizHawk API ───────────────────────────────────────────────────────────
        public IMemoryApi? MemoryApi { get; set; }

        public MemoryServer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            _tcpThread = new Thread(TcpLoop) { IsBackground = true, Name = "MemoryServer" };
            _tcpThread.Start();
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
                var list = MemoryApi.ReadByteRange((long)addr, (int)aux);
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

        // ── TCP loop ──────────────────────────────────────────────────────────────

        private void TcpLoop()
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, _port);
                listener.Start();
                Console.WriteLine($"[MemoryServer] Listening on port {_port}...");

                while (!_disposed)
                {
                    TcpClient? client = null;
                    NetworkStream? stream = null;
                    try
                    {
                        client = listener.AcceptTcpClient();
                        stream = client.GetStream();
                        Console.WriteLine("[MemoryServer] Client connected.");

                        var header = new byte[9];
                        while (!_disposed)
                        {
                            if (!ReadExact(stream, header))
                                break;

                            byte cmd  = header[0];
                            uint addr = ReadU32Le(header, 1);
                            uint aux  = ReadU32Le(header, 5);

                            if (cmd == 0xFF)
                            {
                                Console.WriteLine("[MemoryServer] Disconnect requested.");
                                break;
                            }

                            _pendingCmd  = cmd;
                            _pendingAddr = addr;
                            _pendingAux  = aux;
                            _requestReady.Release();

                            if (!_requestComplete.Wait(5000))
                            {
                                Console.WriteLine("[MemoryServer] Frame thread timeout.");
                                break;
                            }

                            byte[] resp = _responseData ?? Array.Empty<byte>();
                            stream.Write(resp, 0, resp.Length);
                            stream.Flush();
                        }
                    }
                    catch (Exception ex) when (!_disposed)
                    {
                        Console.WriteLine($"[MemoryServer] Client exception: {ex.Message}");
                    }
                    finally
                    {
                        stream?.Dispose();
                        client?.Dispose();
                        while (_requestReady.CurrentCount > 0)    _requestReady.Wait(0);
                        while (_requestComplete.CurrentCount > 0) _requestComplete.Wait(0);
                        Console.WriteLine("[MemoryServer] Client disconnected, waiting for new connection...");
                    }
                }
            }
            catch (Exception ex) when (!_disposed)
            {
                Console.WriteLine($"[MemoryServer] Listener exception: {ex.Message}");
            }
            finally
            {
                listener?.Stop();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static bool ReadExact(NetworkStream stream, byte[] buf)
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
