using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    public class HgssLegacyStateReaderTests
    {
        // Where the anchor points in these tests; any value works, the reads add their offsets to it.
        private const uint AnchorTarget = 0x02300000;

        private static FakeMemoryClient Memory(HgssPointerService table, byte indicator, byte battleState, byte menu)
        {
            FakeMemoryClient memory = new();
            memory.Place(table.BattleIndicator, [indicator]);
            memory.Place(table.Anchor, BitConverter.GetBytes(AnchorTarget));
            memory.Place((uint)(AnchorTarget + table.BattleStateOffset), [battleState]);
            memory.Place(AnchorTarget + table.BattleMenuState, [menu]);
            return memory;
        }

        [Theory]
        [InlineData(0x00, 0, 0, GameScreen.FieldFree)]
        [InlineData(0x00, 14, 1, GameScreen.FieldFree)]
        [InlineData(0x42, 0, 1, GameScreen.FieldFree)]
        [InlineData(0x41, 0, 1, GameScreen.BattleMenu)]
        [InlineData(0xC0, 0, 1, GameScreen.BattleMenu)]
        [InlineData(0x97, 0, 0, GameScreen.BattleTurn)]
        [InlineData(0xC0, 0, 2, GameScreen.BattleTurn)]
        [InlineData(0x41, 14, 1, GameScreen.Evolution)]
        [InlineData(0x97, 14, 0, GameScreen.Evolution)]
        [InlineData(0xC0, 14, 1, GameScreen.Evolution)]
        public async Task ClassifiesTodaysThreeBytes(byte indicator, byte battleState, byte menu, GameScreen expected)
        {
            HgssPointerService table = new();
            FakeMemoryClient memory = Memory(table, indicator, battleState, menu);
            HgssLegacyStateReader reader = new(memory, table);

            GameSnapshot snapshot = await reader.ReadAsync(CancellationToken.None);

            Assert.Equal(expected, snapshot.Screen);
            // The reads stop once the screen is decided: the state byte only in battle, the menu byte
            // only when the state byte is not the evolution value.
            int[][] expectedHops = expected switch
            {
                GameScreen.FieldFree => [],
                GameScreen.Evolution => [[table.BattleStateOffset]],
                _ => [[table.BattleStateOffset], [(int)table.BattleMenuState]],
            };
            Assert.All(memory.PointerReads, read => Assert.Equal(table.Anchor, read.Anchor));
            Assert.Equal(expectedHops, memory.PointerReads.Select(read => read.Hops));
            Assert.Null(snapshot.Outcome);
            Assert.Null(snapshot.Script);
            Assert.Null(snapshot.Prompt);
            Assert.Null(snapshot.TaskFunction);
        }

        [Fact]
        public async Task NeverReturnsAReservedScreen()
        {
            HgssPointerService table = new();
            GameScreen[] legacy = [GameScreen.FieldFree, GameScreen.BattleMenu, GameScreen.BattleTurn, GameScreen.Evolution];

            for (int indicator = 0; indicator <= 0xFF; indicator++)
            {
                foreach (byte battleState in new byte[] { 0, 14 })
                {
                    foreach (byte menu in new byte[] { 0, 1 })
                    {
                        HgssLegacyStateReader reader = new(Memory(table, (byte)indicator, battleState, menu), table);

                        GameSnapshot snapshot = await reader.ReadAsync(CancellationToken.None);

                        Assert.Contains(snapshot.Screen, legacy);
                    }
                }
            }
        }

        [Fact]
        public async Task FieldFreeReadsOnlyTheIndicator()
        {
            HgssPointerService table = new();
            FakeMemoryClient memory = Memory(table, 0x00, 14, 1);
            HgssLegacyStateReader reader = new(memory, table);

            await reader.ReadAsync(CancellationToken.None);

            Assert.Equal([(table.BattleIndicator, 1)], memory.ByteReads);
            Assert.Empty(memory.PointerReads);
        }
    }
}
