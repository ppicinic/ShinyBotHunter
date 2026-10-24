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

        public async Task<byte[]> ReadBytesAsync(uint address, int length, CancellationToken ct = default)
        {
            byte[] header = BuildHeader(0x01, address, (uint)length);
            return await PostAsync(header, ct);
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
            => (await ReadBytesViaPointerAsync(anchorAddress, 1, CancellationToken.None, hopOffsets))[0];

        public async Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
            => BinaryPrimitives.ReadUInt16LittleEndian(await ReadBytesViaPointerAsync(anchorAddress, 2, CancellationToken.None, hopOffsets));

        public async Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
            => BinaryPrimitives.ReadUInt32LittleEndian(await ReadBytesViaPointerAsync(anchorAddress, 4, CancellationToken.None, hopOffsets));

        public async Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, CancellationToken ct, params int[] hopOffsets)
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

            byte[] result = await PostAsync(body, ct);
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
            PokemonScheme scheme = PokemonScheme.Gen4,
            CancellationToken ct = default)
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

            byte[] result = await PostAsync(body, ct);
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

        public async Task<ChainBlockResult[]> ReadChainBlocksAsync(IReadOnlyList<ChainBlockRequest> blocks, int retryFrames = 0, CancellationToken ct = default)
        {
            byte[] result = await PostAsync(EncodeChainBlocks(blocks, retryFrames), ct);
            return DecodeChainBlocks(result, blocks.Count);
        }

        // [cmd][0:4][retryFrames:4][blockCount:1] then per block [anchor:4][hopCount:1][hops:4 each][length:4]
        internal static byte[] EncodeChainBlocks(IReadOnlyList<ChainBlockRequest> blocks, int retryFrames)
        {
            int size = 9 + 1;
            foreach (ChainBlockRequest block in blocks)
            {
                size += 4 + 1 + block.Hops.Length * 4 + 4;
            }

            var body = new byte[size];
            WriteHeader(body, 0x05, 0, (uint)retryFrames);
            body[9] = (byte)blocks.Count;
            int at = 10;
            foreach (ChainBlockRequest block in blocks)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(at), block.Anchor);
                body[at + 4] = (byte)block.Hops.Length;
                at += 5;
                foreach (int hop in block.Hops)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(at), hop);
                    at += 4;
                }
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(at), block.Length);
                at += 4;
            }
            return body;
        }

        // Per block, in request order: [status:1][hop:1][length:4][bytes].
        internal static ChainBlockResult[] DecodeChainBlocks(byte[] result, int blockCount)
        {
            EnsureOpcodeSupported(result, 0x05);

            var blocks = new ChainBlockResult[blockCount];
            int at = 0;
            for (int i = 0; i < blockCount; i++)
            {
                if (result.Length - at < 6)
                {
                    throw new InvalidOperationException(
                        $"Chain read block {i} header runs past the end of the {result.Length}-byte body.");
                }
                var status = (ChainBlockStatus)result[at];
                byte hop = result[at + 1];
                int length = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(at + 2));
                at += 6;
                if (length < 0 || result.Length - at < length)
                {
                    throw new InvalidOperationException(
                        $"Chain read block {i} declares {length} bytes but the body has {result.Length - at} left.");
                }
                blocks[i] = new ChainBlockResult(status, hop, result.AsSpan(at, length).ToArray());
                at += length;
            }
            if (at != result.Length)
            {
                throw new InvalidOperationException(
                    $"Chain read body has {result.Length - at} bytes left over after its {blockCount} blocks.");
            }
            return blocks;
        }

        private async Task<byte[]> PostAsync(byte[] body, CancellationToken ct = default)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var content = new ByteArrayContent(body);
                using var resp = await _http.PostAsync("", content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }, ct);
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
