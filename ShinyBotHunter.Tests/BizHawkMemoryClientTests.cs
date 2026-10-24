using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Tests
{
    public class BizHawkMemoryClientTests
    {
        // Nothing binds this. A request that ignored the token would be refused, treated as
        // transient, and retried for about 8 seconds before surfacing as HttpRequestException.
        private const int UnusedPort = 59996;

        [Fact]
        public async Task ReadValidatedParty_PreCancelledToken_ThrowsWithoutRetrying()
        {
            var client = new BizHawkMemoryClient(port: UnusedPort);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadValidatedPartyAsync(
                anchorAddress: 0, countOffset: 0, slotsOffset: 0, slotSize: 1, slotStride: 1, maxSlots: 1, maxRetryFrames: 1, ct: cts.Token));
        }

        [Fact]
        public async Task ReadBytesViaPointer_PreCancelledToken_ThrowsWithoutRetrying()
        {
            var client = new BizHawkMemoryClient(port: UnusedPort);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadBytesViaPointerAsync(0, 1, cts.Token, 0));
        }

        [Fact]
        public async Task ReadChainBlocks_PreCancelledToken_ThrowsWithoutRetrying()
        {
            var client = new BizHawkMemoryClient(port: UnusedPort);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadChainBlocksAsync(
                new[] { new ChainBlockRequest(0, Array.Empty<int>(), 1) }, ct: cts.Token));
        }

        [Fact]
        public void EncodeChainBlocks_WritesHeaderCountAndEachBlock()
        {
            var blocks = new[]
            {
                new ChainBlockRequest(0x021D4158, Array.Empty<int>(), 0x70),
                new ChainBlockRequest(0x02100000, new[] { 0, 4, -0x10 }, 8),
            };

            byte[] body = BizHawkMemoryClient.EncodeChainBlocks(blocks, retryFrames: 3);

            byte[] expected =
            {
                0x05, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
                0x02,
                0x58, 0x41, 0x1D, 0x02, 0x00, 0x70, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x10, 0x02, 0x03,
                0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xF0, 0xFF, 0xFF, 0xFF,
                0x08, 0x00, 0x00, 0x00,
            };
            Assert.Equal(expected, body);
        }

        [Fact]
        public void DecodeChainBlocks_ReadsEveryStatus()
        {
            byte[] body =
            {
                0x00, 0x03, 0x04, 0x00, 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD,
                0x01, 0x02, 0x00, 0x00, 0x00, 0x00,
                0x02, 0x01, 0x00, 0x00, 0x00, 0x00,
                0x03, 0x00, 0x02, 0x00, 0x00, 0x00, 0x11, 0x22,
            };

            ChainBlockResult[] blocks = BizHawkMemoryClient.DecodeChainBlocks(body, 4);

            Assert.Equal(4, blocks.Length);
            Assert.Equal((ChainBlockStatus.Ok, (byte)3), (blocks[0].Status, blocks[0].Hop));
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, blocks[0].Bytes);
            Assert.Equal((ChainBlockStatus.NullPointer, (byte)2), (blocks[1].Status, blocks[1].Hop));
            Assert.Empty(blocks[1].Bytes);
            Assert.Equal((ChainBlockStatus.OutOfRange, (byte)1), (blocks[2].Status, blocks[2].Hop));
            Assert.Empty(blocks[2].Bytes);
            Assert.Equal((ChainBlockStatus.ShortRead, (byte)0), (blocks[3].Status, blocks[3].Hop));
            Assert.Equal(new byte[] { 0x11, 0x22 }, blocks[3].Bytes);
        }

        [Fact]
        public void DecodeChainBlocks_EmptyBody_ThrowsPredatesOpcode()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => BizHawkMemoryClient.DecodeChainBlocks(Array.Empty<byte>(), 1));

            Assert.Contains("0x05", ex.Message);
            Assert.Contains("predates", ex.Message);
        }

        [Fact]
        public void DecodeChainBlocks_TruncatedBody_Throws()
        {
            byte[] body = { 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xAA, 0xBB };

            Assert.Throws<InvalidOperationException>(() => BizHawkMemoryClient.DecodeChainBlocks(body, 1));
        }
    }
}
