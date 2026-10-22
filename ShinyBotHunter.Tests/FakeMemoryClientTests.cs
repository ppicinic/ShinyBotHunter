using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Tests
{
    public class FakeMemoryClientTests
    {
        [Fact]
        public async Task ReadChainBlocks_FollowsPlacedPointers()
        {
            var memory = new FakeMemoryClient();
            memory.Place(0x02100000, BitConverter.GetBytes(0x02200000u));
            memory.Place(0x02200004, BitConverter.GetBytes(0x02300000u));
            memory.Place(0x02300010, new byte[] { 0xAA, 0xBB });

            ChainBlockResult[] results = await memory.ReadChainBlocksAsync(new[]
            {
                new ChainBlockRequest(0x02100000, new[] { 4, 0x10 }, 2),
                new ChainBlockRequest(0x02100000, Array.Empty<int>(), 4),
            });

            Assert.Equal((ChainBlockStatus.Ok, (byte)2), (results[0].Status, results[0].Hop));
            Assert.Equal(new byte[] { 0xAA, 0xBB }, results[0].Bytes);
            Assert.Equal((ChainBlockStatus.Ok, (byte)0), (results[1].Status, results[1].Hop));
            Assert.Equal(BitConverter.GetBytes(0x02200000u), results[1].Bytes);
        }

        [Fact]
        public async Task ReadChainBlocks_ZeroPointerIsNullPointerAndOtherBlocksStillOk()
        {
            var memory = new FakeMemoryClient();
            memory.Place(0x02100000, BitConverter.GetBytes(0x02200000u));
            memory.Place(0x02200000, new byte[] { 0x01 });

            ChainBlockResult[] results = await memory.ReadChainBlocksAsync(new[]
            {
                new ChainBlockRequest(0x02100000, new[] { 8, 0 }, 4),
                new ChainBlockRequest(0x02100000, new[] { 0 }, 1),
            });

            Assert.Equal((ChainBlockStatus.NullPointer, (byte)1), (results[0].Status, results[0].Hop));
            Assert.Empty(results[0].Bytes);
            Assert.Equal(ChainBlockStatus.Ok, results[1].Status);
            Assert.Equal(new byte[] { 0x01 }, results[1].Bytes);
        }

        [Fact]
        public async Task ReadChainBlocks_PointerOutsideDsRamIsOutOfRange()
        {
            var memory = new FakeMemoryClient();
            memory.Place(0x02100000, BitConverter.GetBytes(0x08000000u));

            ChainBlockResult[] results = await memory.ReadChainBlocksAsync(new[]
            {
                new ChainBlockRequest(0x02100000, new[] { 0, 0 }, 4),
            });

            Assert.Equal((ChainBlockStatus.OutOfRange, (byte)0), (results[0].Status, results[0].Hop));
            Assert.Empty(results[0].Bytes);
        }

        [Fact]
        public async Task ReadChainBlocks_RecordsOneRequest()
        {
            var memory = new FakeMemoryClient();
            var blocks = new[] { new ChainBlockRequest(0x02100000, Array.Empty<int>(), 4) };

            await memory.ReadChainBlocksAsync(blocks);

            var read = Assert.Single(memory.ChainReads);
            Assert.Same(blocks, read.Blocks);
            Assert.Equal(0, read.RetryFrames);
        }
    }
}
