namespace ShinyBotHunter.Emulators
{
    /// <summary>How one block of a chain read ended. The values are the wire bytes of opcode 0x05.</summary>
    public enum ChainBlockStatus : byte
    {
        /// <summary>Every hop was followed and the requested length was read.</summary>
        Ok = 0,

        /// <summary>The pointer read at <see cref="ChainBlockResult.Hop"/> was zero; no bytes.</summary>
        NullPointer = 1,

        /// <summary>The pointer read at <see cref="ChainBlockResult.Hop"/> was outside addressable RAM; no bytes.</summary>
        OutOfRange = 2,

        /// <summary>The core returned fewer bytes than asked for; the bytes are what it returned.</summary>
        ShortRead = 3,
    }

    /// <summary>
    /// One chain to follow: each hop reads a 4-byte pointer at the current address and adds that
    /// hop's offset, starting at <paramref name="Anchor"/>. No hops is a direct read at the anchor.
    /// </summary>
    public record ChainBlockRequest(uint Anchor, int[] Hops, int Length);

    /// <summary>
    /// One block's answer. <paramref name="Hop"/> is the hop whose pointer stopped the walk, or the
    /// hop count when the walk reached the final read.
    /// </summary>
    public record ChainBlockResult(ChainBlockStatus Status, byte Hop, byte[] Bytes);
}
