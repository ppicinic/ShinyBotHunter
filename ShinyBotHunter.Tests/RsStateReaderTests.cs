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

        // The flee path (rs-wild-speed): the bot's own reader keeps today's one request of seven blocks, whatever the screen.
        [Fact]
        public async Task FleeReaderSendsTodaysSevenBlocks()
        {
            (uint, int, int)[] today =
            [
                (0x030006A4, 0, 1), (0x03001770, 0, 4), (0x03001BAD, 0, 1), (0x03004330, 0, 4),
                (0x03004226, 0, 2), (0x02024D26, 0, 1), (0x02024E60, 0, 1),
            ];
            FakeMemoryClient[] memories =
            [
                Memory(),
                Memory(locked: 1, inBattle: BattleBit, controller: ChooseAction),
                Memory(locked: 1, inBattle: BattleBit, controller: OtherController, window: 8),
            ];

            foreach (FakeMemoryClient memory in memories)
            {
                await Read(memory);

                var request = Assert.Single(memory.ChainReads);
                Assert.Equal(0, request.RetryFrames);
                Assert.Equal(today, request.Blocks.Select(block => (block.Anchor, block.Hops.Length, block.Length)));
            }
        }

        // The training reads (docs/specs/rs-navigation-and-training.md).
        private const uint Callback2 = 0x03001774;        // gMain.callback2
        private const uint ExpGetter = 0x02016018;        // gBattleStruct->expGetterMonId
        private const uint MoveLearnState = 0x0201601A;   // gBattleStruct->atk5A_StateTracker
        private const uint LevelUpBox = 0x0201609C;       // gBattleStruct->atk6C_statetracker
        private const uint MoveCursor = 0x02024E64;       // gMoveSelectionCursor[0]
        private const uint ForgetCursor = 0x02018079;     // pssData.selectedMoveIndex
        private const uint PartyIndex = 0x02024A6A;       // gBattlerPartyIndexes[0]
        private const uint FieldWindow = 0x0202E892;      // gFieldMessageBoxWindow + 0x16
        private const uint Native = 0x030005BC;           // sGlobalScriptContext.nativePtr
        private const uint Tasks = 0x03004B20;            // gTasks

        private const uint BattleMain = 0x0800F809;       // BattleMainCB2
        private const uint EvolutionScene = 0x0811242D;   // Task_EvolutionScene
        private const uint PartyList = 0x08095139;        // HandleBattlePartyMenu
        private const uint PartyPopup = 0x08095429;       // Task_HandlePopupMenuInput
        private const uint YesNo = 0x080B5505;            // Task_HandleYesNoInput
        private const uint WaitForButton = 0x080670E5;    // WaitForAorBPress, Sapphire

        private static Task<GameSnapshot> ReadTraining(FakeMemoryClient memory)
            => new RsStateReader(memory, RsPointerService.For("AXPE"), trainingReads: true).ReadAsync(CancellationToken.None);

        // A task in slot `slot` of gTasks: func at + 0, isActive at + 4, s16 data[] from + 8.
        private static void PlaceTask(FakeMemoryClient memory, int slot, uint function, bool active = true, params short[] data)
        {
            uint task = (uint)(Tasks + slot * 0x28);
            memory.Place(task, BitConverter.GetBytes(function));
            memory.Place(task + 4, [active ? (byte)1 : (byte)0]);
            for (int i = 0; i < data.Length; i++) memory.Place((uint)(task + 8 + i * 2), BitConverter.GetBytes(data[i]));
        }

        // A battle with the training values; callback 2 is the battle's unless a test says otherwise.
        private static FakeMemoryClient BattleMemory(uint controller = OtherController, ushort window = 0, uint callback2 = BattleMain,
            byte expGetter = 0, byte moveLearn = 0, byte levelUp = 0)
        {
            FakeMemoryClient memory = Memory(locked: 1, inBattle: BattleBit, controller: controller, window: window);
            memory.Place(Callback2, BitConverter.GetBytes(callback2));
            memory.Place(ExpGetter, [expGetter]);
            memory.Place(MoveLearnState, [moveLearn]);
            memory.Place(LevelUpBox, [levelUp]);
            return memory;
        }

        [Fact]
        public async Task TrainingReadsAreOneRequestOfFifteenBlocks()
        {
            FakeMemoryClient memory = BattleMemory();

            await ReadTraining(memory);

            var request = Assert.Single(memory.ChainReads);
            Assert.Equal(0, request.RetryFrames);
            (uint, int, int)[] expected =
            [
                (0x030006A4, 0, 1), (0x03001770, 0, 4), (0x03001BAD, 0, 1), (0x03004330, 0, 4),
                (0x03004226, 0, 2), (0x02024D26, 0, 1), (0x02024E60, 0, 1),
                (0x03001774, 0, 4), (0x02016018, 0, 0x85), (0x02024E64, 0, 1), (0x02018079, 0, 1),
                (0x02024A6A, 0, 2), (0x0202E892, 0, 2), (0x030005BC, 0, 4), (0x03004B20, 0, 0x280),
            ];
            Assert.Equal(expected, request.Blocks.Select(block => (block.Anchor, block.Hops.Length, block.Length)));
        }

        [Fact]
        public async Task TrainingReadsReadTheSpriteOnlyOnThePartyScreen()
        {
            FakeMemoryClient party = BattleMemory();
            PlaceTask(party, 0, PartyList, data: [0, 0, 0, 4 << 8]);
            FakeMemoryClient battle = BattleMemory(controller: ChooseAction);

            await ReadTraining(party);
            await ReadTraining(battle);

            Assert.Equal(2, party.ChainReads.Count);
            (uint, int, int)[] sprite = [(0x02020142, 0, 2)]; // sprite 4's data[0]
            Assert.Equal(sprite, party.ChainReads[1].Blocks.Select(block => (block.Anchor, block.Hops.Length, block.Length)));
            Assert.Single(battle.ChainReads);
        }

        [Fact]
        public async Task TrainingReadsClassifyTheFleeScreensAsBefore()
        {
            Assert.Equal(GameScreen.Transitioning, (await ReadTraining(Memory(callback1: 0))).Screen);
            Assert.Equal(GameScreen.FieldFree, (await ReadTraining(Memory())).Screen);
            Assert.Equal(GameScreen.FieldTask, (await ReadTraining(Memory(locked: 1))).Screen);
            Assert.Equal(GameScreen.BattleTurn, (await ReadTraining(BattleMemory(window: 3))).Screen);

            FakeMemoryClient action = BattleMemory(controller: ChooseAction);
            action.Place(Cursor, [1]);
            GameSnapshot menu = await ReadTraining(action);
            Assert.Equal(GameScreen.BattleMenu, menu.Screen);
            Assert.Equal(1, menu.Prompt);

            Assert.Equal(GameScreen.BattleMoveList, (await ReadTraining(BattleMemory(controller: ChooseMove))).Screen);

            FakeMemoryClient fled = BattleMemory(window: 8);
            fled.Place(Outcome, [4]);
            GameSnapshot message = await ReadTraining(fled);
            Assert.Equal(GameScreen.BattleMessage, message.Screen);
            Assert.Equal(8, message.TextPrinter);
            Assert.Equal(BattleOutcome.Fled, message.Outcome);
        }

        [Fact]
        public async Task LevelUpBoxFirstPage()
        {
            GameSnapshot snapshot = await ReadTraining(BattleMemory(window: 8, expGetter: 2, levelUp: 1));

            Assert.Equal(GameScreen.BattleExpPrompt, snapshot.Screen);
            Assert.Equal(1, snapshot.Prompt);
            Assert.Equal(2, snapshot.PartyMonIndex);
        }

        [Fact]
        public async Task LevelUpBoxSecondPage()
        {
            GameSnapshot snapshot = await ReadTraining(BattleMemory(window: 8, expGetter: 1, levelUp: 2));

            Assert.Equal(GameScreen.BattleExpPrompt, snapshot.Screen);
            Assert.Equal(2, snapshot.Prompt);
            Assert.Equal(1, snapshot.PartyMonIndex);
        }

        [Fact]
        public async Task MovePromptWaitingIsBattleMoveLearnPrompt()
        {
            GameSnapshot snapshot = await ReadTraining(BattleMemory(window: 8, expGetter: 3, moveLearn: 1));

            Assert.Equal(GameScreen.BattleMoveLearnPrompt, snapshot.Screen);
            Assert.Equal(1, snapshot.Prompt);
            Assert.Equal(3, snapshot.PartyMonIndex);
        }

        [Fact]
        public async Task ForgetMoveScreenIsOtherAppWithItsCursor()
        {
            FakeMemoryClient memory = BattleMemory(callback2: 0x080A0001, moveLearn: 3);
            memory.Place(ForgetCursor, [2]);

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.OtherApp, snapshot.Screen);
            Assert.Equal(2, snapshot.Prompt);
        }

        [Fact]
        public async Task MoveListCarriesTheMoveCursor()
        {
            FakeMemoryClient memory = BattleMemory(controller: ChooseMove);
            memory.Place(MoveCursor, [3]);

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.BattleMoveList, snapshot.Screen);
            Assert.Equal(3, snapshot.Prompt);
        }

        [Fact]
        public async Task ActionMenuCarriesTheBattlerSlot()
        {
            FakeMemoryClient memory = BattleMemory(controller: ChooseAction);
            memory.Place(Cursor, [2]);
            memory.Place(PartyIndex, BitConverter.GetBytes((ushort)1));

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.BattleMenu, snapshot.Screen);
            Assert.Equal(2, snapshot.Prompt);
            Assert.Equal(1, snapshot.PartyMonIndex);
        }

        [Fact]
        public async Task EvolutionTaskIsEvolution()
        {
            FakeMemoryClient memory = Memory(locked: 1);
            PlaceTask(memory, 2, EvolutionScene, data: [21, 0, 0, 0, 0, 0, 0, 0, 3]);

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.Evolution, snapshot.Screen);
            Assert.Equal(21, snapshot.Prompt);
            Assert.Null(snapshot.Question);
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        public async Task EvolutionPromptCarriesTheQuestion(short question)
        {
            FakeMemoryClient memory = Memory(locked: 1);
            PlaceTask(memory, 2, EvolutionScene, data: [21, 0, 0, 0, 0, 0, 0, 0, 4, question]);

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.Evolution, snapshot.Screen);
            Assert.Equal(21, snapshot.Prompt);
            Assert.Equal(question, snapshot.Question);
        }

        [Fact]
        public async Task InactiveTaskIsIgnored()
        {
            FakeMemoryClient memory = Memory(locked: 1);
            PlaceTask(memory, 2, EvolutionScene, active: false, data: [21]);

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.FieldTask, snapshot.Screen);
        }

        [Fact]
        public async Task PartyListIsOtherAppWithCursor()
        {
            FakeMemoryClient memory = BattleMemory();
            PlaceTask(memory, 1, PartyList, data: [0, 0, 0, 4 << 8]);
            memory.Place(0x02020142, BitConverter.GetBytes((short)1)); // sprite 4's data[0]

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.OtherApp, snapshot.Screen);
            Assert.Equal(PartyList, snapshot.TaskFunction);
            Assert.Equal(1, snapshot.PartyMonIndex);
        }

        [Fact]
        public async Task PartyPopupChangesTheTaskFunction()
        {
            FakeMemoryClient memory = BattleMemory();
            PlaceTask(memory, 1, PartyPopup, data: [0, 0, 0, 4 << 8]);
            memory.Place(0x02020142, BitConverter.GetBytes((short)2));

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.OtherApp, snapshot.Screen);
            Assert.Equal(PartyPopup, snapshot.TaskFunction);
            Assert.Equal(2, snapshot.PartyMonIndex);
        }

        [Fact]
        public async Task FieldMessageWaitIsFieldTaskWithWindowState()
        {
            FakeMemoryClient memory = Memory(locked: 1);
            memory.Place(FieldWindow, BitConverter.GetBytes((ushort)8));

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.FieldTask, snapshot.Screen);
            Assert.Equal(8, snapshot.TextPrinter);
            Assert.Null(snapshot.TaskFunction);
        }

        [Fact]
        public async Task YesNoTaskIsFieldTaskWithItsFunction()
        {
            FakeMemoryClient memory = Memory(locked: 1);
            PlaceTask(memory, 3, YesNo);

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.FieldTask, snapshot.Screen);
            Assert.Equal(YesNo, snapshot.TaskFunction);
        }

        [Fact]
        public async Task WaitForButtonNativeIsOnFieldTask()
        {
            FakeMemoryClient memory = Memory(locked: 1);
            memory.Place(Native, BitConverter.GetBytes(WaitForButton));

            GameSnapshot snapshot = await ReadTraining(memory);

            Assert.Equal(GameScreen.FieldTask, snapshot.Screen);
            Assert.Equal(WaitForButton, snapshot.Native);
        }
    }
}
