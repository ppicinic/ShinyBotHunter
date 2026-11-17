using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Places the bytes docs/research/rs-wild-encounter.md logged through one encounter at the pret addresses and checks
    /// the screen read. The addresses are literals, so a wrong member in RsPointerService fails here.
    /// </summary>
    public class RsStateReaderTests
    {
        private const uint Lock = 0x030006A4;        // sLockFieldControls
        private const uint Callback1 = 0x03001770;   // gMain.callback1
        private const uint InBattle = 0x03001BAD;    // gMain + 0x43D
        private const uint Controller = 0x03004330;  // gBattlerControllerFuncs[0]
        private const uint Window = 0x03004226;      // the battle Window + 0x16
        private const uint Outcome = 0x02024D26;     // gBattleOutcome
        private const uint Cursor = 0x02024E60;      // gActionSelectionCursor[0]

        private const uint ChooseAction = 0x0802C099; // HandleInputChooseAction
        private const uint ChooseMove = 0x0802C68D;   // HandleInputChooseMove
        private const uint OtherController = 0x0802D2B1;

        // The spike logs callback 1 only as "not 0" in the field and in battle.
        private const uint SomeCallback = 0x08054B81;

        private const byte BattleBit = 0x02;

        private static FakeMemoryClient Memory(uint callback1 = SomeCallback, byte locked = 0, byte inBattle = 0,
            uint controller = 0, ushort window = 0, byte outcome = 0, byte cursor = 0)
        {
            FakeMemoryClient memory = new();
            memory.Place(Lock, [locked]);
            memory.Place(Callback1, BitConverter.GetBytes(callback1));
            memory.Place(InBattle, [inBattle]);
            memory.Place(Controller, BitConverter.GetBytes(controller));
            memory.Place(Window, BitConverter.GetBytes(window));
            memory.Place(Outcome, [outcome]);
            memory.Place(Cursor, [cursor]);
            return memory;
        }

        private static Task<GameSnapshot> Read(FakeMemoryClient memory)
            => new RsStateReader(memory, new RsPointerService()).ReadAsync(CancellationToken.None);

        [Fact]
        public async Task TitleScreenIsTransitioning()
        {
            GameSnapshot snapshot = await Read(Memory(callback1: 0));

            Assert.Equal(GameScreen.Transitioning, snapshot.Screen);
        }

        [Fact]
        public async Task FreeFieldIsFieldFree()
        {
            GameSnapshot snapshot = await Read(Memory());

            Assert.Equal(GameScreen.FieldFree, snapshot.Screen);
            Assert.Null(snapshot.Outcome);
            Assert.Null(snapshot.Prompt);
            Assert.Null(snapshot.TextPrinter);
        }

        [Fact]
        public async Task EncounterRollIsFieldTask()
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1));

            Assert.Equal(GameScreen.FieldTask, snapshot.Screen);
        }

        [Fact]
        public async Task FadeBackIsFieldTaskWithoutOutcome()
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, outcome: 4));

            Assert.Equal(GameScreen.FieldTask, snapshot.Screen);
            Assert.Null(snapshot.Outcome);
        }

        [Fact]
        public async Task BattleSetupIsBattleTurn()
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: BattleBit));

            Assert.Equal(GameScreen.BattleTurn, snapshot.Screen);
        }

        [Fact]
        public async Task PrintingMessageIsBattleTurn()
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: BattleBit, controller: OtherController, window: 3));

            Assert.Equal(GameScreen.BattleTurn, snapshot.Screen);
            Assert.Null(snapshot.TextPrinter);
        }

        [Theory]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(9)]
        public async Task ButtonWaitIsBattleMessage(ushort window)
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: BattleBit, controller: OtherController, window: window));

            Assert.Equal(GameScreen.BattleMessage, snapshot.Screen);
            Assert.Equal((int?)window, snapshot.TextPrinter);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        public async Task ActionMenuIsBattleMenuWithCursor(byte cursor)
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: BattleBit, controller: ChooseAction, cursor: cursor));

            Assert.Equal(GameScreen.BattleMenu, snapshot.Screen);
            Assert.Equal((int?)cursor, snapshot.Prompt);
        }

        [Fact]
        public async Task MoveListIsBattleMoveList()
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: BattleBit, controller: ChooseMove));

            Assert.Equal(GameScreen.BattleMoveList, snapshot.Screen);
        }

        [Fact]
        public async Task GotAwaySafelyIsBattleMessageWithFled()
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: BattleBit, controller: OtherController, window: 8, outcome: 4));

            Assert.Equal(GameScreen.BattleMessage, snapshot.Screen);
            Assert.Equal(8, snapshot.TextPrinter);
            Assert.Equal(BattleOutcome.Fled, snapshot.Outcome);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(1, BattleOutcome.Won)]
        [InlineData(2, BattleOutcome.Lost)]
        [InlineData(3, BattleOutcome.Draw)]
        [InlineData(4, BattleOutcome.Fled)]
        [InlineData(6, null)]
        [InlineData(7, BattleOutcome.Caught)]
        public async Task OutcomeMapsToTheSharedEnum(byte outcome, BattleOutcome? expected)
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: BattleBit, outcome: outcome));

            Assert.Equal(GameScreen.BattleTurn, snapshot.Screen);
            Assert.Equal(expected, snapshot.Outcome);
        }

        [Theory]
        [InlineData(0x01, GameScreen.FieldTask)]
        [InlineData(0x03, GameScreen.BattleTurn)]
        public async Task BattleBitIsBitOneOfTheByte(byte inBattle, GameScreen expected)
        {
            GameSnapshot snapshot = await Read(Memory(locked: 1, inBattle: inBattle));

            Assert.Equal(expected, snapshot.Screen);
        }

        [Fact]
        public async Task OneSnapshotIsOneRequestFromOneFrame()
        {
            FakeMemoryClient memory = Memory();

            await Read(memory);

            var request = Assert.Single(memory.ChainReads);
            Assert.Equal(7, request.Blocks.Count);
            Assert.All(request.Blocks, block => Assert.Empty(block.Hops));
            Assert.Empty(memory.ByteReads);
            Assert.Empty(memory.UIntReads);
            Assert.Empty(memory.PointerReads);
        }
    }
}
