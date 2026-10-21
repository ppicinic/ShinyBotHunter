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
            return Task.FromResult(new byte[length]);
        }

        public Task<uint> ReadUIntAsync(uint address)
        {
            UIntReads.Add(address);
            return Task.FromResult(0u);
        }

        public void Place(uint address, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) placed[(uint)(address + i)] = bytes[i];
        }

        public void Connect() { }
        public Task<byte> ReadByteAsync(uint address) => Task.FromResult((byte)0);
        public Task<ushort> ReadUShortAsync(uint address) => Task.FromResult((ushort)0);
        public Task WriteByteAsync(uint address, byte value) => Task.CompletedTask;
        public Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, CancellationToken ct, params int[] hopOffsets) => Task.FromResult(new byte[length]);
        public Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult((byte)0);
        public Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult((ushort)0);
        public Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult(0u);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
