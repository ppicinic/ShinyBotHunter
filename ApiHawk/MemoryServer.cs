using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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
    /// Every request starts with the same 9-byte binary header; newer opcodes append their own
    /// fields, so the handler reads those 9 bytes first and then reads the remainder per opcode.
    ///   Request body : [cmd:1][addr:4 LE][aux:4 LE]
    ///   cmd 0x01     : read  aux bytes from addr  → response body: aux bytes
    ///   cmd 0x02     : write aux&amp;0xFF to addr      → response body: 0x01 (ack)
    ///   cmd 0x03     : follow a pointer chain from addr, then read aux bytes.
    ///                  extra: [hopCount:1][hops:i32 LE × hopCount]
    ///                  → response body: aux bytes
    ///   cmd 0x04     : read a whole party, validated, from a single frame.
    ///                  extra: [countOffset:i32][slotsOffset:i32][slotSize:i32][slotStride:i32]
    ///                         [maxSlots:1][maxRetryFrames:2]
    ///                  → response body: [count:1][count × slotSize raw bytes]
    ///
    /// Why 0x03 and 0x04 exist: a client that reads the anchor pointer and then reads through it is
    /// two HTTP round trips, so the pointer it uses is at least a frame stale. Doing both hops here
    /// keeps them on one frame. 0x04 goes further and validates each slot before answering, retrying
    /// on the next frame when a read lands mid-rewrite -- far tighter than the client noticing bad
    /// data and polling again, and it guarantees the whole party comes from one frame rather than
    /// slot 1 from one frame and slot 6 from three frames later.
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

            // cmd 0x03
            public int[] Hops = Array.Empty<int>();

            // cmd 0x04
            public int CountOffset;
            public int SlotsOffset;
            public int SlotSize;
            public int SlotStride;
            public byte MaxSlots;
            public int MaxRetryFrames;
            public int FramesTried;

            // How to reach the party, and which generation's structure to validate. Both default to
            // the Gen 4 behaviour so a client that does not send them behaves exactly as before.
            public byte Addressing;
            public byte Scheme;

            // Set when the frame thread gives up, so the handler can answer 502 rather than hand
            // back a short body the client would have to interpret.
            public bool Failed;
            public string FailureReason = string.Empty;
        }

        // ── Request queue: handler threads → frame thread ─────────────────────────
        private readonly ConcurrentQueue<MemoryRequest> _queue = new ConcurrentQueue<MemoryRequest>();

        // ── HTTP infrastructure ───────────────────────────────────────────────────
        private readonly int _port;
        private volatile bool _disposed;
        private HttpListener? _listener;
        private Thread? _acceptThread;

        // How long a handler waits for the frame thread before giving up. A cmd 0x04 retry budget has
        // to stay well inside this -- at 60fps, 120 frames is 2s against this 5s ceiling.
        private const int FrameWaitTimeoutMs = 5000;

        // Regions a pointer may legitimately address, per console. DS keeps everything in main RAM;
        // GBA splits between EWRAM and the much smaller IWRAM, and Gen 3 puts the party in IWRAM --
        // so validating against the DS window alone rejects every Gen 3 party read.
        private static readonly (uint Start, uint End)[] DsRanges =
        {
            (0x02000000, 0x02400000),
        };

        private static readonly (uint Start, uint End)[] GbaRanges =
        {
            (0x02000000, 0x02040000), // EWRAM
            (0x03000000, 0x03008000), // IWRAM
        };

        // Sanity bounds on request fields, so a malformed body can't allocate wildly or loop long.
        private const int MaxHops = 4;
        private const int MaxSlotSize = 0x1000;

        // cmd 0x04 addressing: Gen 4 reaches the party through an anchor pointer that moves, while
        // Gen 3 keeps it at a fixed address and has nothing to dereference.
        private const byte AddressingAnchor = 0x00;
        private const byte AddressingAbsolute = 0x01;

        // cmd 0x04 validation scheme, selecting which generation's structure the slots are checked as.
        private const byte SchemeGen4 = 0x00;
        private const byte SchemeGen3 = 0x01;

        // cmd 0x04 count byte meaning "could not be read within the frame budget". Safe as a sentinel
        // because a real count is bounded by the request's maxSlots.
        private const byte PartyUnavailable = 0xFF;

        // ── BizHawk API ───────────────────────────────────────────────────────────
        public IMemoryApi? MemoryApi { get; set; }

        private (uint Start, uint End)[]? _ranges;

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

            BotLog.Write($"[MemoryServer] Listening on http://127.0.0.1:{_port}/ ...");
        }

        // ── Called by BotServerTool.UpdateValues(PostFrame) every frame ───────────

        public void ServiceFrame()
        {
            // Requests that need another frame. They must not go straight back onto _queue: the
            // drain loop below would pick them up again in this same frame and spin until the retry
            // budget ran out, freezing the emulator for the whole request.
            List<MemoryRequest>? retry = null;

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
                        req.Result = ReadBytes(req.Addr, (int)req.Aux);
                    }
                    else if (req.Cmd == 0x02)
                    {
                        MemoryApi.WriteByte((long)req.Addr, req.Aux & 0xFF);
                        req.Result = new byte[] { 0x01 };
                    }
                    else if (req.Cmd == 0x03)
                    {
                        uint addr = FollowPointerChain(req.Addr, req.Hops);
                        req.Result = ReadBytes(addr, (int)req.Aux);
                    }
                    else if (req.Cmd == 0x04)
                    {
                        if (!TryReadValidatedParty(req, out byte[] party, out string reason))
                        {
                            req.FramesTried++;
                            if (req.FramesTried < req.MaxRetryFrames)
                            {
                                // Leave Done unsignalled and try again next frame, re-following the
                                // pointer chain so a moved anchor is picked up too.
                                (retry ??= new List<MemoryRequest>()).Add(req);
                                continue;
                            }
                            // Not a 502: giving up here is an ordinary outcome (the battle party is
                            // only populated during a battle), and a 502 would have the client burn
                            // its whole transient-retry budget on it. The sentinel count says
                            // "unavailable" -- 0xFF can never be a real party size.
                            BotLog.WriteThrottled("party-unreadable", $"[MemoryServer] Party unreadable after {req.FramesTried} frames: {reason}");
                            req.Result = new byte[] { PartyUnavailable };
                        }
                        else
                        {
                            if (req.FramesTried > 0)
                                BotLog.WriteThrottled("party-retried", $"[MemoryServer] Party read succeeded after {req.FramesTried} retry frame(s).");
                            req.Result = party;
                        }
                    }
                    else
                    {
                        req.Result = Array.Empty<byte>();
                    }
                }
                catch (Exception ex)
                {
                    BotLog.WriteThrottled("frame-error", $"[MemoryServer] Frame processing error: {ex.Message}");
                    req.Failed = true;
                    req.FailureReason = ex.Message;
                    req.Result = Array.Empty<byte>();
                }

                // The handler may have already given up and disposed Done on timeout.
                try { req.Done.Set(); } catch { }
            }

            if (retry != null)
            {
                foreach (MemoryRequest pending in retry)
                    _queue.Enqueue(pending);
            }
        }

        // ── Frame-thread helpers (only ever called from ServiceFrame) ──────────────

        private byte[] ReadBytes(uint address, int length)
        {
            var list = MemoryApi!.ReadByteRange((long)address, length);
            byte[] buf = new byte[list.Count];
            for (int i = 0; i < list.Count; i++)
                buf[i] = list[i];
            return buf;
        }

        /// <summary>
        /// Reads the pointer at <paramref name="address"/> and applies the first offset, repeating for
        /// each hop. Every pointer read has to land in DS main RAM; a value outside it means the read
        /// caught the pointer being written, and continuing would read an unrelated address and hand
        /// back believable garbage. Throwing here turns that into a 502 the client retries.
        /// </summary>
        /// <summary>
        /// Whether an address falls in a region this console can address.
        ///
        /// The console is inferred from the core's memory domains rather than configured: a GBA core
        /// exposes EWRAM and IWRAM, which is exactly the distinction being validated. Cached because
        /// this runs per pointer hop, on the frame thread.
        /// </summary>
        private bool IsAddressable(uint address)
        {
            if (_ranges == null)
            {
                bool isGba = false;
                try
                {
                    foreach (string domain in MemoryApi!.GetMemoryDomainList())
                    {
                        if (domain == "IWRAM" || domain == "EWRAM")
                        {
                            isGba = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // A core that will not list its domains gets the DS window, which is what every
                    // pre-existing caller assumed.
                }
                _ranges = isGba ? GbaRanges : DsRanges;
                BotLog.Write($"[MemoryServer] Addressable RAM: {(isGba ? "GBA (EWRAM+IWRAM)" : "DS main RAM")}");
            }

            foreach ((uint start, uint end) in _ranges)
            {
                if (address >= start && address < end) return true;
            }
            return false;
        }

        private uint FollowPointerChain(uint address, int[] hops)
        {
            uint addr = address;
            for (int i = 0; i < hops.Length; i++)
            {
                byte[] raw = ReadBytes(addr, 4);
                if (raw.Length < 4)
                    throw new InvalidOperationException($"pointer read at 0x{addr:X8} returned {raw.Length} bytes");
                uint pointer = ReadU32Le(raw, 0);
                if (!IsAddressable(pointer))
                    throw new InvalidOperationException($"pointer 0x{pointer:X8} at 0x{addr:X8} is outside addressable RAM");
                addr = (uint)(pointer + hops[i]);
            }
            return addr;
        }

        /// <summary>
        /// One attempt at the whole party within this frame: follow the anchor, read the count, then
        /// read and validate every slot. All or nothing -- a single bad slot fails the attempt so the
        /// caller never sees a party stitched together from different frames.
        /// </summary>
        private bool TryReadValidatedParty(MemoryRequest req, out byte[] result, out string reason)
        {
            result = Array.Empty<byte>();

            // In absolute mode the address is the party structure itself, so there is nothing to
            // dereference and the offsets are measured from it directly.
            uint basePtr = req.Addressing == AddressingAbsolute
                ? req.Addr
                : FollowPointerChain(req.Addr, new[] { 0 });

            byte[] countRaw = ReadBytes((uint)(basePtr + req.CountOffset), 1);
            if (countRaw.Length < 1)
            {
                reason = "count read returned nothing";
                return false;
            }
            int count = countRaw[0];
            if (count > req.MaxSlots)
            {
                // Uninitialised or mid-write; a real party never exceeds the configured maximum.
                reason = $"count {count} exceeds max {req.MaxSlots}";
                return false;
            }

            byte[] buf = new byte[1 + count * req.SlotSize];
            buf[0] = (byte)count;
            for (int slot = 0; slot < count; slot++)
            {
                uint slotAddr = (uint)(basePtr + req.SlotsOffset + slot * req.SlotStride);
                byte[] block = ReadBytes(slotAddr, req.SlotSize);
                if (block.Length != req.SlotSize)
                {
                    reason = $"slot {slot + 1} read returned {block.Length} bytes";
                    return false;
                }
                bool usable = req.Scheme == SchemeGen3
                    ? Gen3PokemonValidator.IsUsable(block, out string slotReason)
                    : PokemonValidator.IsUsable(block, out slotReason);
                if (!usable)
                {
                    reason = $"slot {slot + 1}: {slotReason}";
                    return false;
                }
                Buffer.BlockCopy(block, 0, buf, 1 + slot * req.SlotSize, req.SlotSize);
            }

            result = buf;
            reason = string.Empty;
            return true;
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
                    BotLog.WriteThrottled("accept-error", $"[MemoryServer] Accept error: {ex.Message}");
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

                if (!TryReadOpcodeExtras(context.Request.InputStream, req))
                {
                    response.StatusCode = 400;
                    response.Close();
                    return;
                }

                _queue.Enqueue(req);

                if (!req.Done.Wait(FrameWaitTimeoutMs))
                {
                    BotLog.WriteThrottled("frame-timeout", $"[MemoryServer] Frame thread timeout (cmd 0x{req.Cmd:X2}, queue depth {_queue.Count}).");
                    response.StatusCode = 504;
                    response.Close();
                    return;
                }

                if (req.Failed)
                {
                    // 502 rather than a short body: the client's retry policy treats it as transient,
                    // and nothing downstream has to guess whether a truncated read meant failure.
                    byte[] message = Encoding.UTF8.GetBytes(req.FailureReason);
                    response.StatusCode = 502;
                    response.ContentType = "text/plain";
                    response.ContentLength64 = message.Length;
                    response.OutputStream.Write(message, 0, message.Length);
                    response.OutputStream.Close();
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
                BotLog.WriteThrottled("request-exception", $"[MemoryServer] Request exception: {ex.Message}");
                try { response.Abort(); } catch { }
            }
            finally
            {
                req?.Done.Dispose();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the per-opcode fields that follow the shared 9-byte header. Opcodes 0x01/0x02 have
        /// none, which is what keeps older clients working unchanged.
        /// </summary>
        private static bool TryReadOpcodeExtras(Stream stream, MemoryRequest req)
        {
            if (req.Cmd == 0x03)
            {
                byte[]? hopCountRaw = ReadExact(stream, 1);
                if (hopCountRaw == null || hopCountRaw[0] == 0 || hopCountRaw[0] > MaxHops)
                    return false;
                int hopCount = hopCountRaw[0];
                byte[]? hopsRaw = ReadExact(stream, hopCount * 4);
                if (hopsRaw == null)
                    return false;
                var hops = new int[hopCount];
                for (int i = 0; i < hopCount; i++)
                    hops[i] = (int)ReadU32Le(hopsRaw, i * 4);
                req.Hops = hops;
                return true;
            }

            if (req.Cmd == 0x04)
            {
                byte[]? extras = ReadExact(stream, 19);
                if (extras == null)
                    return false;
                req.CountOffset = (int)ReadU32Le(extras, 0);
                req.SlotsOffset = (int)ReadU32Le(extras, 4);
                req.SlotSize = (int)ReadU32Le(extras, 8);
                req.SlotStride = (int)ReadU32Le(extras, 12);
                req.MaxSlots = extras[16];
                req.MaxRetryFrames = extras[17] | (extras[18] << 8);
                if (req.SlotSize <= 0 || req.SlotSize > MaxSlotSize || req.MaxSlots == 0)
                    return false;
                if (req.MaxRetryFrames < 1)
                    req.MaxRetryFrames = 1;

                // Optional tail. Absent means a client built before Gen 3 support, which can only
                // have meant anchor addressing and Gen 4 validation.
                byte[]? tail = ReadExact(stream, 2);
                if (tail != null)
                {
                    req.Addressing = tail[0];
                    req.Scheme = tail[1];
                }
                return true;
            }

            return true;
        }

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
