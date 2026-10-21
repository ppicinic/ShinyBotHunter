using System.Buffers.Binary;
using System.Net.Http;

namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// HTTP memory client. Each read/write POSTs the same 9-byte binary header the server
    /// understands and reads the raw response body. <see cref="HttpClient"/> is thread-safe,
    /// so multiple threads may poll memory concurrently; the server queues and orders the
    /// work on the emulator frame thread.
    /// </summary>
    public class BizHawkMemoryClient : BizHawkClientBase, IMemoryClient
    {
        // Sentinel count the server sends when a validated party read exhausted its frame budget.
        private const byte PartyUnavailable = 0xFF;

        public BizHawkMemoryClient(string host = "127.0.0.1", int port = 20001)
            : base("BizHawkMemoryClient", host, port, TimeSpan.FromSeconds(15))
        {
        }

        protected override void Disconnect()
        {
            // Connectionless — nothing to tear down.
        }

        public async Task<byte> ReadByteAsync(uint address) => (await ReadBytesAsync(address, 1))[0];
        public async Task<ushort> ReadUShortAsync(uint address) => BinaryPrimitives.ReadUInt16LittleEndian(await ReadBytesAsync(address, 2));
        public async Task<uint> ReadUIntAsync(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(await ReadBytesAsync(address, 4));

        public async Task<byte[]> ReadBytesAsync(uint address, int length)
        {
            byte[] header = BuildHeader(0x01, address, (uint)length);
            return await ExecuteWithRetryAsync(async () =>
            {
                using var content = new ByteArrayContent(header);
                using var resp = await _http.PostAsync("", content).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            });
        }

        public async Task WriteByteAsync(uint address, byte value)
        {
            byte[] header = BuildHeader(0x02, address, value);
            await ExecuteWithRetryAsync(async () =>
            {
                using var content = new ByteArrayContent(header);
                using var resp = await _http.PostAsync("", content).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            });
        }

        public async Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
            => (await ReadBytesViaPointerAsync(anchorAddress, 1, hopOffsets))[0];

        public async Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
            => BinaryPrimitives.ReadUInt16LittleEndian(await ReadBytesViaPointerAsync(anchorAddress, 2, hopOffsets));

        public async Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
            => BinaryPrimitives.ReadUInt32LittleEndian(await ReadBytesViaPointerAsync(anchorAddress, 4, hopOffsets));

        public async Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, params int[] hopOffsets)
        {
            if (hopOffsets is null || hopOffsets.Length == 0)
            {
                throw new ArgumentException("At least one hop offset is required; use ReadBytesAsync for a direct read.", nameof(hopOffsets));
            }

            // [cmd][anchor:4][length:4][hopCount:1][hops:4 each]
            var body = new byte[9 + 1 + hopOffsets.Length * 4];
            WriteHeader(body, 0x03, anchorAddress, (uint)length);
            body[9] = (byte)hopOffsets.Length;
            for (int i = 0; i < hopOffsets.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(10 + i * 4), hopOffsets[i]);
            }

            byte[] result = await PostAsync(body);
            EnsureOpcodeSupported(result, 0x03);
            return result;
        }

        public async Task<byte[][]> ReadValidatedPartyAsync(
            uint anchorAddress,
            int countOffset,
            int slotsOffset,
            int slotSize,
            int slotStride,
            int maxSlots,
            int maxRetryFrames,
            PartyAddressing addressing = PartyAddressing.Anchor,
            PokemonScheme scheme = PokemonScheme.Gen4)
        {
            // [cmd][anchor:4][unused:4][countOffset:4][slotsOffset:4][slotSize:4][slotStride:4][maxSlots:1][maxRetryFrames:2][addressing:1][scheme:1]
            // The last two bytes are a later addition; an emulator-side tool built before them treats
            // their absence as anchor addressing and Gen 4 validation, which is what it always did.
            var body = new byte[9 + 21];
            WriteHeader(body, 0x04, anchorAddress, 0);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), countOffset);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(13), slotsOffset);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(17), slotSize);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(21), slotStride);
            body[25] = (byte)maxSlots;
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(26), (ushort)maxRetryFrames);
            body[28] = (byte)addressing;
            body[29] = (byte)scheme;

            byte[] result = await PostAsync(body);
            EnsureOpcodeSupported(result, 0x04);

            int count = result[0];
            if (count == PartyUnavailable)
            {
                throw new PartyReadUnavailableException(
                    $"No party at 0x{anchorAddress:X8} passed validation within {maxRetryFrames} frames.");
            }
            if (count > maxSlots)
            {
                throw new InvalidOperationException($"Validated party read reported {count} slots, above the requested maximum of {maxSlots}.");
            }
            int expected = 1 + count * slotSize;
            if (result.Length != expected)
            {
                throw new InvalidOperationException(
                    $"Validated party read returned {result.Length} bytes, expected {expected} for {count} slots.");
            }

            var slots = new byte[count][];
            for (int slot = 0; slot < count; slot++)
            {
                slots[slot] = result.AsSpan(1 + slot * slotSize, slotSize).ToArray();
            }
            return slots;
        }

        private async Task<byte[]> PostAsync(byte[] body)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var content = new ByteArrayContent(body);
                using var resp = await _http.PostAsync("", content).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            });
        }

        /// <summary>
        /// The server answers opcodes it doesn't know with 200 and an empty body, so an out-of-date
        /// BotServer.dll would otherwise surface as an index or length error far from the cause.
        /// </summary>
        private static void EnsureOpcodeSupported(byte[] result, byte cmd)
        {
            if (result.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Memory server returned an empty body for opcode 0x{cmd:X2}. The running BotServer.dll " +
                    "predates this opcode -- rebuild ApiHawk and copy BotServer.dll into BizHawk's ExternalTools folder.");
            }
        }

        private static byte[] BuildHeader(byte cmd, uint address, uint aux)
        {
            var header = new byte[9];
            WriteHeader(header, cmd, address, aux);
            return header;
        }

        private static void WriteHeader(byte[] buffer, byte cmd, uint address, uint aux)
        {
            buffer[0] = cmd;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), address);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(5), aux);
        }
    }
}
