using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ShinyBotHunter.Emulators.Dolphin
{
    // A cemuhook (DSU) server: Dolphin's DSU client subscribes to it as GCPad1 (DSUClient.ini and
    // GCPadNew.ini in the bot's user folder), so presses reach the disc with no window focus.
    // Dolphin asks for pad data from a fresh ephemeral port about once a second; every port seen in
    // the last 5 s gets the current frame every 4 ms. The byte layout matches the spike's server,
    // which Dolphin 2609a was proven on.
    public sealed class DolphinPad : IAsyncDisposable
    {
        // Low byte is the frame's buttons1, high byte its buttons2 (cemuhook bit order).
        public const int PadE = 0x0020;
        public const int Cross = 0x4000;

        private const ushort Protocol = 1001;
        private const uint ServerId = 1;
        private const uint VersionType = 0x10_0000;
        private const uint PortInfoType = 0x10_0001;
        private const uint PadDataType = 0x10_0002;
        private const int HeaderLength = 16;
        private const int SendIntervalMs = 4;
        private const long SubscriberLifetimeMs = 5_000;

        // SIO_UDP_CONNRESET: without it, the ICMP port-unreachable from a port Dolphin abandoned
        // surfaces as a SocketException on the next receive.
        private const int SioUdpConnReset = unchecked((int)0x9800_000C);

        private static readonly uint[] CrcTable = BuildCrcTable();

        private readonly Socket _socket;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _receiveLoop;
        private readonly Task _sendLoop;

        // Written by the receive loop, read and pruned by the send loop; the concurrent dictionary
        // is the protection. The value is Environment.TickCount64 of the sender's last request.
        private readonly ConcurrentDictionary<EndPoint, long> _subscribers = new();

        // The pressed buttons. One writer (PressAsync, called from the event loop only) and one
        // reader (the send loop); every access goes through Volatile.
        private int _buttons;

        public DolphinPad(int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            _receiveLoop = ReceiveLoopAsync(_stop.Token);
            _sendLoop = SendLoopAsync(_stop.Token);
        }

        public async Task PressAsync(int button, int holdMs, CancellationToken ct)
        {
            // A dead server is loud at the next press instead of surfacing as a menu timeout.
            if (_receiveLoop.IsFaulted) await _receiveLoop;
            if (_sendLoop.IsFaulted) await _sendLoop;

            Volatile.Write(ref _buttons, button);
            try
            {
                await Task.Delay(holdMs, ct);
            }
            finally
            {
                Volatile.Write(ref _buttons, 0);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[1024];
            while (true)
            {
                SocketReceiveFromResult received = await _socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct);
                if (received.ReceivedBytes < HeaderLength + 4) continue;

                uint type = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(HeaderLength));
                if (type == VersionType)
                {
                    await _socket.SendToAsync(BuildVersionReply(), SocketFlags.None, received.RemoteEndPoint, ct);
                }
                else if (type == PortInfoType)
                {
                    int count = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(HeaderLength + 4));
                    for (int i = 0; i < count; i++)
                    {
                        byte slot = buffer[HeaderLength + 8 + i];
                        await _socket.SendToAsync(BuildPortInfoReply(slot), SocketFlags.None, received.RemoteEndPoint, ct);
                    }
                }
                else if (type == PadDataType)
                {
                    _subscribers[received.RemoteEndPoint] = Environment.TickCount64;
                }
            }
        }

        private async Task SendLoopAsync(CancellationToken ct)
        {
            // Only this loop touches the packet counter.
            uint packet = 0;
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(SendIntervalMs));
            while (await timer.WaitForNextTickAsync(ct))
            {
                long now = Environment.TickCount64;
                byte[] frame = BuildPadFrame(Volatile.Read(ref _buttons), packet++, (ulong)(Stopwatch.GetTimestamp() * 1_000_000 / Stopwatch.Frequency));
                foreach ((EndPoint subscriber, long seen) in _subscribers)
                {
                    if (now - seen > SubscriberLifetimeMs)
                        _subscribers.TryRemove(new KeyValuePair<EndPoint, long>(subscriber, seen));
                    else
                        await _socket.SendToAsync(frame, SocketFlags.None, subscriber, ct);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            foreach (Task loop in new[] { _receiveLoop, _sendLoop })
            {
                try
                {
                    await loop;
                }
                catch (OperationCanceledException)
                {
                }
            }
            _socket.Dispose();
            _stop.Dispose();
        }

        // ── Packets ───────────────────────────────────────────────────────────────

        internal static byte[] BuildVersionReply()
        {
            byte[] packet = NewPacket(2, VersionType);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), Protocol);
            return Seal(packet);
        }

        // Slot 0 is the one pad: connected (2), full gyro model (2), USB (1), MAC 00:..:01,
        // battery full (5). Other slots are reported not connected.
        internal static byte[] BuildPortInfoReply(byte slot)
        {
            byte[] packet = NewPacket(12, PortInfoType);
            WriteSlot(packet, slot);
            return Seal(packet);
        }

        // 84 bytes after the header including the message type, 100 on the wire.
        internal static byte[] BuildPadFrame(int buttons, uint packetNumber, ulong timestampMicros)
        {
            byte[] packet = NewPacket(80, PadDataType);
            Span<byte> p = packet.AsSpan(20);
            WriteSlot(packet, 0);
            p[11] = 1;                                                        // connected
            BinaryPrimitives.WriteUInt32LittleEndian(p[12..], packetNumber);
            p[16] = (byte)buttons;                                            // buttons1: D-pad
            p[17] = (byte)(buttons >> 8);                                     // buttons2: face buttons
            // p[18] HOME and p[19] touch stay 0.
            p[20] = p[21] = p[22] = p[23] = 128;                              // both sticks centred; 0 reads as full left/up
            if ((buttons & PadE) != 0) p[26] = 255;                           // D-pad Right pressure
            if ((buttons & Cross) != 0) p[29] = 255;                          // Cross pressure
            // p[24..36) the rest of the pressure bytes, p[36..48) two touch points: 0.
            BinaryPrimitives.WriteUInt64LittleEndian(p[48..], timestampMicros);
            // p[56..80) six motion floats: 0.
            return Seal(packet);
        }

        private static byte[] NewPacket(int payloadLength, uint type)
        {
            byte[] packet = new byte[HeaderLength + 4 + payloadLength];
            "DSUS"u8.CopyTo(packet);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), Protocol);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)(4 + payloadLength));
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), ServerId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), type);
            return packet;
        }

        private static void WriteSlot(byte[] packet, byte slot)
        {
            packet[20] = slot;
            if (slot != 0) return;
            packet[21] = 2;     // state: connected
            packet[22] = 2;     // model: full gyro
            packet[23] = 1;     // connection: USB
            packet[29] = 1;     // MAC 00:00:00:00:00:01 at 24..30
            packet[30] = 5;     // battery: full
        }

        // The CRC field is still zero when this runs, as the protocol computes it.
        private static byte[] Seal(byte[] packet)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), Crc32(packet));
            return packet;
        }

        internal static uint Crc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFF_FFFF;
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFF_FFFF;
        }

        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }
}
