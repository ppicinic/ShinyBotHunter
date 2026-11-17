using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Places the overworld values docs/research/rs-wild-encounter.md read at the pret addresses. The addresses are
    /// literals, so a wrong member in RsPointerService fails here.
    /// </summary>
    public class RsOverworldReaderTests
    {
        private const uint Location = 0x02025738; // gSaveBlock1.location
        private const uint Position = 0x02025734; // gSaveBlock1.pos
        private const uint Facing = 0x030048B8;   // gObjectEvents[0] + 0x18
        private const uint ObjectId = 0x0202E85D; // gPlayerAvatar.objectEventId

        private static RsOverworldReader Reader(FakeMemoryClient memory) => new(memory, new RsPointerService());

        [Theory]
        [InlineData(0, 16, 16)]
        [InlineData(1, 5, 261)]
        public async Task MapIdIsGroupTimesTwoFiftySixPlusNumber(byte group, byte number, ushort expected)
        {
            FakeMemoryClient memory = new();
            memory.Place(Location, [group, number]);

            Assert.Equal(expected, await Reader(memory).GetMapIdAsync(CancellationToken.None));
        }

        [Fact]
        public async Task PositionIsTwoSignedShortsFromOneRead()
        {
            FakeMemoryClient memory = new();
            memory.Place(Position, [14, 0, 4, 0]);

            (short X, short Y) position = await Reader(memory).GetPositionAsync(CancellationToken.None);

            Assert.Equal(((short)14, (short)4), position);
            Assert.Equal((Position, 4), Assert.Single(memory.ByteReads));
        }

        [Theory]
        [InlineData(0x11, Direction.Down)]
        [InlineData(0x22, Direction.Up)]
        [InlineData(0x33, Direction.Left)]
        [InlineData(0x44, Direction.Right)]
        [InlineData(0x41, Direction.Down)]
        public async Task FacingIsTheLowNibble(byte facing, Direction expected)
        {
            FakeMemoryClient memory = new();
            memory.Place(Facing, [facing]);

            Assert.Equal(expected, await Reader(memory).GetFacingAsync(CancellationToken.None));
        }

        [Fact]
        public async Task UnknownFacingIsNone()
        {
            FakeMemoryClient memory = new();
            memory.Place(Facing, [0x00]);

            Assert.Equal(Direction.None, await Reader(memory).GetFacingAsync(CancellationToken.None));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public async Task PlayerObjectIdIsRead(byte id)
        {
            FakeMemoryClient memory = new();
            memory.Place(ObjectId, [id]);

            Assert.Equal(id, await Reader(memory).GetPlayerObjectIdAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ScriptStateIsOneReadOfFlagsAndVars()
        {
            FakeMemoryClient memory = new();
            memory.Place(0x02026954, [0xA5]);         // the first event flag byte
            memory.Place(0x02026954 + 0x11F, [0x5A]); // the last
            memory.Place(0x02026A74, [0x34, 0x12]);   // var 0x4000
            memory.Place(0x02026A74 + 0x1FE, [0x78, 0x56]);

            (byte[] flags, byte[] vars) = await Reader(memory).ReadScriptStateAsync(CancellationToken.None);

            var request = Assert.Single(memory.ChainReads);
            Assert.Equal(0, request.RetryFrames);
            Assert.Equal(
                [(0x02026954u, 0, 0x120), (0x02026A74u, 0, 0x200)],
                request.Blocks.Select(block => (block.Anchor, block.Hops.Length, block.Length)));
            Assert.Equal(0x120, flags.Length);
            Assert.Equal(0xA5, flags[0]);
            Assert.Equal(0x5A, flags[0x11F]);
            Assert.Equal(0x200, vars.Length);
            Assert.Equal(0x1234, BitConverter.ToUInt16(vars, 0));
            Assert.Equal(0x5678, BitConverter.ToUInt16(vars, 0x1FE));
        }
    }
}
