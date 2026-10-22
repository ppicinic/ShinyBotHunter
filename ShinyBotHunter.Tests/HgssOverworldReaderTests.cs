using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    public class HgssOverworldReaderTests
    {
        // Where the anchor points in these tests; any value works, the reads add their offsets to it.
        private const uint AnchorTarget = 0x02300000;

        [Fact]
        public async Task GetMapIdAsyncReadsTheMapIdThroughTheAnchor()
        {
            HgssPointerService table = new();
            FakeMemoryClient memory = new();
            memory.Place(table.Anchor, BitConverter.GetBytes(AnchorTarget));
            memory.Place((uint)(AnchorTarget + table.MapIdOffset), BitConverter.GetBytes((ushort)412));
            HgssOverworldReader reader = new(memory, table);

            ushort mapId = await reader.GetMapIdAsync(CancellationToken.None);

            Assert.Equal(412, mapId);
        }

        [Fact]
        public async Task GetPositionAsyncDecodesTheHighHalfAsSigned()
        {
            HgssPointerService table = new();
            FakeMemoryClient memory = new();
            memory.Place(table.TrainerX, BitConverter.GetBytes(0x00070000u));
            memory.Place(table.TrainerY, BitConverter.GetBytes(0xFFFF0000u));
            HgssOverworldReader reader = new(memory, table);

            (short x, short y) = await reader.GetPositionAsync(CancellationToken.None);

            Assert.Equal((short)7, x);
            Assert.Equal((short)-1, y);
            Assert.Equal([table.TrainerX, table.TrainerY], memory.UIntReads);
        }

        [Fact]
        public async Task GetFacingAsyncReadsTheFacingByteThroughTheAnchor()
        {
            HgssPointerService table = new();
            FakeMemoryClient memory = new();
            memory.Place(table.Anchor, BitConverter.GetBytes(AnchorTarget));
            memory.Place((uint)(AnchorTarget + table.FacingOffset), [3]);
            HgssOverworldReader reader = new(memory, table);

            Direction facing = await reader.GetFacingAsync(CancellationToken.None);

            Assert.Equal(Direction.Right, facing);
        }

        [Fact]
        public async Task GetFacingAsyncPassesAnUnknownByteThrough()
        {
            HgssPointerService table = new();
            FakeMemoryClient memory = new();
            memory.Place(table.Anchor, BitConverter.GetBytes(AnchorTarget));
            memory.Place((uint)(AnchorTarget + table.FacingOffset), [7]);
            HgssOverworldReader reader = new(memory, table);

            Direction facing = await reader.GetFacingAsync(CancellationToken.None);

            Assert.Equal((Direction)7, facing);
        }
    }
}
