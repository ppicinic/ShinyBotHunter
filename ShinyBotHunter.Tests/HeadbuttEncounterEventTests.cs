using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // Values from research/headbutt.md: the tree in front of (426, 464) facing Down in Azalea.
    public class HeadbuttEncounterEventTests
    {
        private static readonly HgssPointerService Table = new();

        // Where the anchor points; any value works, the reads add their offsets to it.
        private const uint AnchorTarget = 0x02300000;

        [Theory]
        [InlineData(EncounterType.Spin, typeof(SpinEncounterEvent))]
        [InlineData(EncounterType.Run, typeof(RunEncounterEvent))]
        [InlineData(EncounterType.Headbutt, typeof(HeadbuttEncounterEvent))]
        public void BuildEncounterPicksTheEventForTheType(EncounterType type, Type expected)
        {
            IBotEvent encounter = OrchestratorEvent.BuildEncounter(new FakeBot(Task.CompletedTask), new EncounterParams(type));

            Assert.IsType(expected, encounter);
        }

        [Fact]
        public async Task CaptureStartRecordsTheFacing()
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(AnchorTarget));
            memory.Place((uint)(AnchorTarget + Table.MapIdOffset), BitConverter.GetBytes((ushort)74));
            memory.Place((uint)(AnchorTarget + Table.FacingOffset), [(byte)Direction.Down]);
            memory.Place(Table.TrainerX, BitConverter.GetBytes(426u << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes(464u << 16));
            GameState state = new();

            await OrchestratorEvent.CaptureStartAsync(state, new HgssOverworldReader(memory, Table), CancellationToken.None);

            Assert.Equal(Direction.Down, state.StartFacing);
            Assert.Equal((74, 426, 464), (state.StartMapId, state.StartX, state.StartY));
        }
    }
}
