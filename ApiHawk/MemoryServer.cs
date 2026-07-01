using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using BizHawk.Client.Common;

namespace ApiHawk
{
    /// <summary>
    /// HTTP memory server — replaces the previous raw-TCP memory server.
    ///
    /// Listens on http://127.0.0.1:{port}/ (port read from BIZHAWK_MEMORY_PORT, default 50001).
    /// Binding to the explicit loopback IP avoids needing a Windows URL-ACL reservation.
    ///
    /// Wire format is unchanged — the request body is the same 9-byte binary header the
    /// clients already build, and the response body is the raw result:
    ///   Request body : [cmd:1][addr:4 LE][aux:4 LE]
    ///   cmd 0x01     : read  aux bytes from addr  → response body: aux bytes
    ///   cmd 0x02     : write aux&amp;0xFF to addr      → response body: 0x01 (ack)
    ///
    /// Thread model:
    ///   Accept thread — blocks on HttpListener.GetContext, hands each request to the thread
    ///                   pool so many callers can poll concurrently.
    ///   Handler (pool)— parses the body, enqueues a MemoryRequest, blocks on its completion
    ///                   handle, then writes the result. The emulator's IMemoryApi is never
    ///                   touched here — only on the frame thread.
    ///   Frame thread  — BizHawk's UpdateValues(PostFrame) callback; drains the whole request
    ///                   queue each frame (FIFO), calls IMemoryApi, and signals each request.
    /// </summary>
    internal sealed class MemoryServer : IDisposable
    {
        private sealed class MemoryRequest
        {
            public byte Cmd;
            public uint Addr;
            public uint Aux;
            public byte[] Result = Array.Empty<byte>();
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        // ── Request queue: handler threads → frame thread ─────────────────────────
        private readonly ConcurrentQueue<MemoryRequest> _queue = new ConcurrentQueue<MemoryRequest>();

        // ── HTTP infrastructure ───────────────────────────────────────────────────
        private readonly int _port;
        private volatile bool _disposed;
        private HttpListener? _listener;
        private Thread? _acceptThread;

        // How long a handler waits for the frame thread before giving up.
        private const int FrameWaitTimeoutMs = 5000;

        // ── BizHawk API ───────────────────────────────────────────────────────────
        public IMemoryApi? MemoryApi { get; set; }

        public MemoryServer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MemoryServer" };
            _acceptThread.Start();

            Console.WriteLine($"[MemoryServer] Listening on http://127.0.0.1:{_port}/ ...");
        }

        // ── Called by BotServerTool.UpdateValues(PostFrame) every frame ───────────

        public void ServiceFrame()
        {
            while (_queue.TryDequeue(out MemoryRequest req))
            {
                try
                {
                    if (MemoryApi == null)
                    {
                        req.Result = Array.Empty<byte>();
                    }
                    else if (req.Cmd == 0x01)
                    {
                        var list = MemoryApi.ReadByteRange((long)req.Addr, (int)req.Aux);
                        byte[] buf = new byte[list.Count];
                        for (int i = 0; i < list.Count; i++)
                            buf[i] = list[i];
                        req.Result = buf;
                    }
                    else if (req.Cmd == 0x02)
                    {
                        MemoryApi.WriteByte((long)req.Addr, req.Aux & 0xFF);
                        req.Result = new byte[] { 0x01 };
                    }
                    else
                    {
                        req.Result = Array.Empty<byte>();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MemoryServer] Frame processing error: {ex.Message}");
                    req.Result = Array.Empty<byte>();
                }
                finally
                {
                    // The handler may have already given up and disposed Done on timeout.
                    try { req.Done.Set(); } catch { }
                }
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
                    Console.WriteLine($"[MemoryServer] Accept error: {ex.Message}");
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => HandleContext(context));
            }
        }

        private void HandleContext(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            MemoryRequest? req = null;
            try
            {
                byte[]? header = ReadExact(context.Request.InputStream, 9);
                if (header == null)
                {
                    response.StatusCode = 400;
                    response.Close();
                    return;
                }

                req = new MemoryRequest
                {
                    Cmd  = header[0],
                    Addr = ReadU32Le(header, 1),
                    Aux  = ReadU32Le(header, 5),
                };
                _queue.Enqueue(req);

                if (!req.Done.Wait(FrameWaitTimeoutMs))
                {
                    Console.WriteLine("[MemoryServer] Frame thread timeout.");
                    response.StatusCode = 504;
                    response.Close();
                    return;
                }

                byte[] result = req.Result;
                response.StatusCode = 200;
                response.ContentType = "application/octet-stream";
                response.ContentLength64 = result.Length;
                if (result.Length > 0)
                    response.OutputStream.Write(result, 0, result.Length);
                response.OutputStream.Close();
                response.Close();
            }
            catch (Exception ex) when (!_disposed)
            {
                Console.WriteLine($"[MemoryServer] Request exception: {ex.Message}");
                try { response.Abort(); } catch { }
            }
            finally
            {
                req?.Done.Dispose();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static byte[]? ReadExact(Stream stream, int count)
        {
            var buf = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buf, offset, count - offset);
                if (read == 0) return null;
                offset += read;
            }
            return buf;
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
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }

            // Unblock any handlers still waiting on the frame thread.
            while (_queue.TryDequeue(out MemoryRequest req))
            {
                req.Result = Array.Empty<byte>();
                try { req.Done.Set(); } catch { }
            }
        }
    }
}
