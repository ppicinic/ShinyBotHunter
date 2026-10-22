using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Records what a reader asked the emulator for, so tests can assert on the memory map a read
    /// used without an emulator being involved.
    /// </summary>
    public class FakeMemoryClient : IMemoryClient
    {
        public record PartyRead(uint Anchor, int CountOffset, int SlotsOffset, int SlotSize, int SlotStride, int MaxSlots, int MaxRetryFrames, PartyAddressing Addressing, PokemonScheme Scheme);

        public PartyRead? LastPartyRead { get; private set; }
        public List<(uint Address, int Length)> ByteReads { get; } = new();
        public List<uint> UIntReads { get; } = new();
        public List<(uint Anchor, int[] Hops)> PointerReads { get; } = new();
        public List<(IReadOnlyList<ChainBlockRequest> Blocks, int RetryFrames)> ChainReads { get; } = new();

        // Runs after each plain read with the number of plain reads so far, so a test can change memory
        // between two reads of one snapshot. Only touched from the test's own thread, like placed.
        public Action<int>? AfterRead { get; set; }

        private readonly int partySize;
        private readonly int slotSize;

        // Bytes a test placed at an address; anything unplaced reads as 0. Only touched from the
        // test's own thread, so no locking.
        private readonly Dictionary<uint, byte> placed = new();

        public FakeMemoryClient(int partySize = 1, int slotSize = 0xEC)
        {
            this.partySize = partySize;
            this.slotSize = slotSize;
        }

        public Task<byte[][]> ReadValidatedPartyAsync(uint anchorAddress, int countOffset, int slotsOffset, int slotSize, int slotStride, int maxSlots, int maxRetryFrames,
            PartyAddressing addressing = PartyAddressing.Anchor, PokemonScheme scheme = PokemonScheme.Gen4, CancellationToken ct = default)
        {
            LastPartyRead = new PartyRead(anchorAddress, countOffset, slotsOffset, slotSize, slotStride, maxSlots, maxRetryFrames, addressing, scheme);
            if (addressing == PartyAddressing.Absolute)
            {
                // The party really sits at the requested addresses, so serve it from placed bytes.
                byte PlacedByte(uint address) => placed.TryGetValue(address, out byte value) ? value : (byte)0;
                byte[][] read = new byte[PlacedByte((uint)(anchorAddress + countOffset))][];
                for (int i = 0; i < read.Length; i++)
                {
                    uint slot = (uint)(anchorAddress + slotsOffset + i * slotStride);
                    read[i] = new byte[slotSize];
                    for (int b = 0; b < slotSize; b++) read[i][b] = PlacedByte((uint)(slot + b));
                }
                return Task.FromResult(read);
            }
            byte[][] blocks = new byte[partySize][];
            for (int i = 0; i < partySize; i++) blocks[i] = new byte[slotSize];
            return Task.FromResult(blocks);
        }

        public Task<byte[]> ReadBytesAsync(uint address, int length, CancellationToken ct = default)
        {
            ByteReads.Add((address, length));
            byte[] bytes = PlacedBytes(address, length);
            AfterRead?.Invoke(ByteReads.Count);
            return Task.FromResult(bytes);
        }

        public Task<uint> ReadUIntAsync(uint address)
        {
            UIntReads.Add(address);
            return Task.FromResult(PlacedValue(address, 4));
        }

        public void Place(uint address, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) placed[(uint)(address + i)] = bytes[i];
        }

        public void Connect() { }
        public Task<byte> ReadByteAsync(uint address) => Task.FromResult((byte)0);
        public Task<ushort> ReadUShortAsync(uint address) => Task.FromResult((ushort)0);
        public Task WriteByteAsync(uint address, byte value) => Task.CompletedTask;
        public Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, CancellationToken ct, params int[] hopOffsets)
        {
            PointerReads.Add((anchorAddress, hopOffsets));
            return Task.FromResult(PlacedBytes(Follow(anchorAddress, hopOffsets), length));
        }

        public Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
        {
            PointerReads.Add((anchorAddress, hopOffsets));
            return Task.FromResult((byte)PlacedValue(Follow(anchorAddress, hopOffsets), 1));
        }

        public Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult((ushort)PlacedValue(Follow(anchorAddress, hopOffsets), 2));
        public Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult(0u);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // The emulator's rules over placed bytes: a zero pointer is NullPointer, one outside DS main
        // RAM is OutOfRange. Placed memory has no end and no frames, so never ShortRead and no retry.
        public Task<ChainBlockResult[]> ReadChainBlocksAsync(IReadOnlyList<ChainBlockRequest> blocks, int retryFrames = 0, CancellationToken ct = default)
        {
            ChainReads.Add((blocks, retryFrames));
            var results = new ChainBlockResult[blocks.Count];
            for (int b = 0; b < blocks.Count; b++) results[b] = ReadChainBlock(blocks[b]);
            return Task.FromResult(results);
        }

        private ChainBlockResult ReadChainBlock(ChainBlockRequest block)
        {
            uint address = block.Anchor;
            for (int i = 0; i < block.Hops.Length; i++)
            {
                uint pointer = PlacedValue(address, 4);
                if (pointer == 0) return new ChainBlockResult(ChainBlockStatus.NullPointer, (byte)i, Array.Empty<byte>());
                if (pointer < 0x02000000 || pointer >= 0x02400000) return new ChainBlockResult(ChainBlockStatus.OutOfRange, (byte)i, Array.Empty<byte>());
                address = (uint)(pointer + block.Hops[i]);
            }
            return new ChainBlockResult(ChainBlockStatus.Ok, (byte)block.Hops.Length, PlacedBytes(address, block.Length));
        }

        private byte[] PlacedBytes(uint address, int length)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++) bytes[i] = placed.TryGetValue((uint)(address + i), out byte b) ? b : (byte)0;
            return bytes;
        }

        // Little-endian, as the emulator reads it.
        private uint PlacedValue(uint address, int length)
        {
            uint value = 0;
            for (int i = length - 1; i >= 0; i--)
                value = (value << 8) | (placed.TryGetValue((uint)(address + i), out byte b) ? b : 0u);
            return value;
        }

        // Each hop reads a placed pointer and adds its offset, as IMemoryClient documents the chain.
        private uint Follow(uint anchorAddress, int[] hopOffsets)
        {
            uint address = anchorAddress;
            foreach (int offset in hopOffsets) address = (uint)(PlacedValue(address, 4) + offset);
            return address;
        }
    }
}
