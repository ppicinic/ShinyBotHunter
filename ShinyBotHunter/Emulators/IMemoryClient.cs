using System.Buffers.Binary;

namespace ShinyBotHunter.Emulators
{
    public interface IMemoryClient : IAsyncDisposable
    {
        public void Connect();
        public Task<byte> ReadByteAsync(uint address);
        public Task<ushort> ReadUShortAsync(uint address);
        public Task<uint> ReadUIntAsync(uint address);

        public Task<byte[]> ReadBytesAsync(uint address, int length, CancellationToken ct = default);

        public Task WriteByteAsync(uint address, byte value);

        /// <summary>
        /// Follows a pointer chain and reads from where it lands, in one emulator frame. Reading the
        /// anchor and then reading through it as two calls means the pointer is already a frame stale
        /// by the time it is used.
        ///
        /// Each hop reads a 4-byte pointer at the current address and adds that hop's offset, so
        /// <c>ReadBytesViaPointerAsync(anchor, 0xEC, ct, 0x6930, 0xC18)</c> is
        /// <c>anchor -> +0x6930 -> deref -> +0xC18 -> read</c>. Offsets are signed; most are negative.
        /// </summary>
        public Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, CancellationToken ct, params int[] hopOffsets);

        public Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets);
        public Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets);
        public Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets);

        /// <summary>
        /// Reads a whole party in one request, from one frame, with every slot already validated by
        /// the emulator: checksum, species range, and the live battle stats that no checksum covers.
        /// A slot that fails is retried on the next frame rather than handed back, so this either
        /// returns a party that was consistent at a single instant or throws.
        ///
        /// All offsets are signed and relative to the dereferenced anchor.
        /// </summary>
        public Task<byte[][]> ReadValidatedPartyAsync(
            uint anchorAddress,
            int countOffset,
            int slotsOffset,
            int slotSize,
            int slotStride,
            int maxSlots,
            int maxRetryFrames,
            PartyAddressing addressing = PartyAddressing.Anchor,
            PokemonScheme scheme = PokemonScheme.Gen4,
            CancellationToken ct = default);
    }
}
