using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    public class HgssFieldSystemStateReaderTests
    {
        private static readonly HgssPointerService Table = new();

        // Where each struct of the chain sits in these tests; any in-range values work.
        private const uint Fs = 0x02100000;
        private const uint Sub0 = 0x02110000;
        private const uint App = 0x02120000;
        private const uint Bs = 0x02130000;
        private const uint Ctx = 0x02140000;
        private const uint Taskman = 0x02150000;
        private const uint Getter = 0x02160000;

        private const uint OutOfRange = 0x02400000;

        // The pointer parameters say what each hop holds; the structs stay at the addresses above. The fake
        // answers a zero pointer with NullPointer and OutOfRange with OutOfRange. The defaults are the free field.
        private static FakeMemoryClient Memory(
            uint fs = Fs, uint sub0 = Sub0, uint app = 0, uint paused = 0,
            uint taskman = 0, uint mapReady = 1, uint taskFunction = 0,
            uint ovyId = 12, uint state = 9, uint bs = Bs, byte outcome = 0,
            uint ctx = Ctx, uint command = 0, byte sel = 0, uint script = 0,
            uint getter = 0, uint getterState = 0)
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(fs));
            memory.Place(At(Fs, Table.AppSlotsOffset), BitConverter.GetBytes(sub0));
            memory.Place(At(Fs, Table.TaskManagerOffset), BitConverter.GetBytes(taskman));
            memory.Place(At(Fs, Table.MapReadyOffset), BitConverter.GetBytes(mapReady));
            memory.Place(At(Sub0, Table.SubAppOffset), BitConverter.GetBytes(app));
            memory.Place(At(Sub0, Table.PausedOffset), BitConverter.GetBytes(paused));
            memory.Place(At(Taskman, Table.TaskFunctionOffset), BitConverter.GetBytes(taskFunction));
            memory.Place(At(App, Table.AppOverlayIdOffset), BitConverter.GetBytes(ovyId));
            memory.Place(At(App, Table.AppStateOffset), BitConverter.GetBytes(state));
            memory.Place(At(App, Table.BattleSystemOffset), BitConverter.GetBytes(bs));
            memory.Place(At(Bs, Table.BattleContextOffset), BitConverter.GetBytes(ctx));
            memory.Place(At(Bs, Table.OutcomeOffset), [outcome]);
            memory.Place(At(Ctx, Table.MenuStateOffset), [sel]);
            memory.Place(At(Ctx, Table.ServerCommandOffset), BitConverter.GetBytes(command));
            memory.Place(At(Ctx, Table.ScriptFileOffset), BitConverter.GetBytes(script));
            memory.Place(At(Ctx, Table.GetterOffset), BitConverter.GetBytes(getter));
            memory.Place(At(Getter, Table.GetterStateOffset), BitConverter.GetBytes(getterState));
            return memory;
        }

        private static uint At(uint pointer, int offset) => (uint)(pointer + offset);

        private static async Task<GameSnapshot> SnapshotOf(FakeMemoryClient memory)
        {
            GameSnapshot snapshot = await new HgssFieldSystemStateReader(memory, Table).ReadAsync(CancellationToken.None);
            Assert.Equal(0, Assert.Single(memory.ChainReads).RetryFrames);
            Assert.Empty(memory.ByteReads);
            Assert.Empty(memory.UIntReads);
            Assert.Empty(memory.PointerReads);
            return snapshot;
        }

        [Fact]
        public async Task FieldFree()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory());

            Assert.Equal(new GameSnapshot(GameScreen.FieldFree, null, null, null, null), snapshot);
        }

        [Fact]
        public async Task FieldTask()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(taskman: Taskman, taskFunction: 0x020551B9));

            Assert.Equal(GameScreen.FieldTask, snapshot.Screen);
            Assert.Equal(0x020551B9u, snapshot.TaskFunction);
        }

        [Theory]
        [InlineData(Taskman, 0u, 0u)]
        [InlineData(0u, 1u, 1u)]
        public async Task UnmatchedFieldStateIsTransitioning(uint taskman, uint mapReady, uint paused)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(taskman: taskman, mapReady: mapReady, paused: paused, taskFunction: 0x02055245));

            Assert.Equal(GameScreen.Transitioning, snapshot.Screen);
        }

        [Fact]
        public async Task OtherApp()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, ovyId: 3));

            Assert.Equal(GameScreen.OtherApp, snapshot.Screen);
        }

        [Theory]
        [InlineData(3u)]
        [InlineData(8u)]
        public async Task BattleStarting(uint state)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, state: state));

            Assert.Equal(GameScreen.BattleStarting, snapshot.Screen);
        }

        [Fact]
        public async Task Evolution()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, state: 14));

            Assert.Equal(GameScreen.Evolution, snapshot.Screen);
        }

        // State 13 is the evolution check every battle end passes through, seen on a flee.
        [Theory]
        [InlineData(10u, 1, BattleOutcome.Won)]
        [InlineData(13u, 5, BattleOutcome.Fled)]
        public async Task BattleOverAfterMainState(uint state, byte outcome, BattleOutcome expected)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, state: state, outcome: outcome));

            Assert.Equal(GameScreen.BattleOver, snapshot.Screen);
            Assert.Equal(expected, snapshot.Outcome);
        }

        [Fact]
        public async Task BattleOverOnCommand45()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 45, outcome: 5));

            Assert.Equal(GameScreen.BattleOver, snapshot.Screen);
            Assert.Equal(BattleOutcome.Fled, snapshot.Outcome);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0x45)]
        [InlineData(0x85)]
        [InlineData(6)]
        public async Task OutcomeOutsideOneToFiveIsNull(byte outcome)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 45, outcome: outcome));

            Assert.Equal(GameScreen.BattleOver, snapshot.Screen);
            Assert.Null(snapshot.Outcome);
        }

        [Fact]
        public async Task BattleScript()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 22, script: 6));

            Assert.Equal(GameScreen.BattleScript, snapshot.Screen);
            Assert.Equal(6, snapshot.Script);
        }

        [Fact]
        public async Task BattleMenu()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 5, sel: 1));

            Assert.Equal(GameScreen.BattleMenu, snapshot.Screen);
        }

        [Fact]
        public async Task BattleMoveList()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 5, sel: 4));

            Assert.Equal(GameScreen.BattleMoveList, snapshot.Screen);
        }

        [Fact]
        public async Task BattleMessage()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 5, sel: 15));

            Assert.Equal(GameScreen.BattleMessage, snapshot.Screen);
        }

        [Theory]
        [InlineData(11u)]
        [InlineData(13u)]
        public async Task BattleExpPrompt(uint getterState)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 8, getter: Getter, getterState: getterState));

            Assert.Equal(GameScreen.BattleExpPrompt, snapshot.Screen);
            Assert.Equal((int)getterState, snapshot.Prompt);
        }

        [Theory]
        [InlineData(21u)]
        [InlineData(23u)]
        [InlineData(34u)]
        public async Task BattleMoveLearnPrompt(uint getterState)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 8, getter: Getter, getterState: getterState));

            Assert.Equal(GameScreen.BattleMoveLearnPrompt, snapshot.Screen);
            Assert.Equal((int)getterState, snapshot.Prompt);
        }

        [Theory]
        [InlineData(8u, 0)]
        [InlineData(5u, 14)]
        public async Task BattleTurn(uint command, byte sel)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: command, sel: sel));

            Assert.Equal(GameScreen.BattleTurn, snapshot.Screen);
        }

        [Fact]
        public async Task OtherGetterStateFallsThrough()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 5, sel: 1, getter: Getter, getterState: 4));

            Assert.Equal(GameScreen.BattleMenu, snapshot.Screen);
        }

        [Fact]
        public async Task Command45BeatsScriptAndGetter()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 45, script: 6, getter: Getter, getterState: 11, outcome: 1));

            Assert.Equal(GameScreen.BattleOver, snapshot.Screen);
        }

        [Fact]
        public async Task ScriptBeatsGetter()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 22, script: 276, getter: Getter, getterState: 11));

            Assert.Equal(GameScreen.BattleScript, snapshot.Screen);
        }

        [Fact]
        public async Task GetterBeatsMenu()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, command: 5, sel: 1, getter: Getter, getterState: 21));

            Assert.Equal(GameScreen.BattleMoveLearnPrompt, snapshot.Screen);
        }

        [Theory]
        [InlineData("FieldFree", GameScreen.FieldFree)]
        [InlineData("FieldTask", GameScreen.FieldTask)]
        [InlineData("BattleMenu", GameScreen.BattleMenu)]
        [InlineData("BattleMoveList", GameScreen.BattleMoveList)]
        [InlineData("BattleExpPrompt", GameScreen.BattleExpPrompt)]
        [InlineData("BattleOverOnCommand45", GameScreen.BattleOver)]
        [InlineData("BattleOverAfterMainState", GameScreen.BattleOver)]
        [InlineData("Evolution", GameScreen.Evolution)]
        public async Task OneRequestOnEveryScreen(string screen, GameScreen expected)
        {
            FakeMemoryClient memory = screen switch
            {
                "FieldFree" => Memory(),
                "FieldTask" => Memory(taskman: Taskman, taskFunction: 0x020551B9),
                "BattleMenu" => Memory(app: App, command: 5, sel: 1),
                "BattleMoveList" => Memory(app: App, command: 5, sel: 4),
                "BattleExpPrompt" => Memory(app: App, command: 8, getter: Getter, getterState: 11),
                "BattleOverOnCommand45" => Memory(app: App, command: 45, outcome: 5),
                "BattleOverAfterMainState" => Memory(app: App, state: 10, outcome: 1),
                _ => Memory(app: App, state: 14),
            };

            GameSnapshot snapshot = await SnapshotOf(memory);

            Assert.Equal(expected, snapshot.Screen);
        }

        // The spec's block table, written out as literals so a mixed-up member shows here.
        [Fact]
        public async Task RequestIsTheSevenBlocksOfTheWalk()
        {
            FakeMemoryClient memory = Memory();

            await SnapshotOf(memory);

            IReadOnlyList<ChainBlockRequest> blocks = memory.ChainReads[0].Blocks;
            Assert.All(blocks, block => Assert.Equal(0x021D4158u, block.Anchor));
            Assert.Equal(
                new int[][] { [0], [0, 0], [0x10, 0], [0, 4, 0], [0, 4, 0x1C, 0x30, 0], [0, 4, 0x1C, 0x30, 0x178, 0x28], [0, 4, 0x1C, 0x2420] },
                blocks.Select(block => block.Hops));
            Assert.Equal([0x70, 12, 8, 0x20, 0x17C, 4, 1], blocks.Select(block => block.Length));
        }

        // A null pointer is a state where the chain allows one (no app, no task, no getter); everywhere
        // else, and for every out-of-range pointer, the snapshot is Transitioning.
        [Theory]
        [InlineData("fs", 0u, GameScreen.Transitioning)]
        [InlineData("fs", OutOfRange, GameScreen.Transitioning)]
        [InlineData("sub0", 0u, GameScreen.Transitioning)]
        [InlineData("sub0", OutOfRange, GameScreen.Transitioning)]
        [InlineData("app", 0u, GameScreen.FieldFree)]
        [InlineData("app", OutOfRange, GameScreen.Transitioning)]
        [InlineData("bs", 0u, GameScreen.Transitioning)]
        [InlineData("bs", OutOfRange, GameScreen.Transitioning)]
        [InlineData("bsAfterMainState", OutOfRange, GameScreen.BattleOver)]
        [InlineData("ctx", 0u, GameScreen.Transitioning)]
        [InlineData("ctx", OutOfRange, GameScreen.Transitioning)]
        [InlineData("taskman", 0u, GameScreen.FieldFree)]
        [InlineData("taskman", OutOfRange, GameScreen.Transitioning)]
        [InlineData("getter", 0u, GameScreen.BattleMenu)]
        [InlineData("getter", OutOfRange, GameScreen.Transitioning)]
        public async Task BadPointerAtEachHop(string hop, uint pointer, GameScreen expected)
        {
            FakeMemoryClient memory = hop switch
            {
                "fs" => Memory(fs: pointer),
                "sub0" => Memory(sub0: pointer),
                "app" => Memory(app: pointer),
                "bs" => Memory(app: App, command: 5, sel: 1, bs: pointer),
                "bsAfterMainState" => Memory(app: App, state: 10, outcome: 1, bs: pointer),
                "ctx" => Memory(app: App, ctx: pointer),
                "taskman" => Memory(taskman: pointer),
                _ => Memory(app: App, command: 5, sel: 1, getter: pointer, getterState: 11),
            };

            GameSnapshot snapshot = await SnapshotOf(memory);

            Assert.Equal(expected, snapshot.Screen);
            Assert.Null(snapshot.Outcome);
        }
    }
}
