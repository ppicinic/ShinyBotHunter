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

        // The pointer parameters say what each hop holds; the structs stay at the addresses above, so a bad
        // pointer leads nowhere placed. The defaults are the free field.
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

        [Theory]
        [InlineData(13u)]
        [InlineData(14u)]
        public async Task Evolution(uint state)
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, state: state));

            Assert.Equal(GameScreen.Evolution, snapshot.Screen);
        }

        [Fact]
        public async Task BattleOverAfterMainState()
        {
            GameSnapshot snapshot = await SnapshotOf(Memory(app: App, state: 10, outcome: 1));

            Assert.Equal(GameScreen.BattleOver, snapshot.Screen);
            Assert.Equal(BattleOutcome.Won, snapshot.Outcome);
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

        // The amended NFR-1 counts with the roots cached; the first snapshot adds the two root reads.
        [Theory]
        [InlineData("FieldFree", 3)]
        [InlineData("FieldTask", 4)]
        [InlineData("BattleMenu", 5)]
        [InlineData("BattleMoveList", 5)]
        [InlineData("BattleExpPrompt", 6)]
        [InlineData("BattleMoveLearnPrompt", 6)]
        [InlineData("BattleOverOnCommand45", 6)]
        [InlineData("BattleOverAfterMainState", 4)]
        public async Task RequestCountsPerScreen(string screen, int requests)
        {
            FakeMemoryClient memory = screen switch
            {
                "FieldFree" => Memory(),
                "FieldTask" => Memory(taskman: Taskman, taskFunction: 0x020551B9),
                "BattleMenu" => Memory(app: App, command: 5, sel: 1),
                "BattleMoveList" => Memory(app: App, command: 5, sel: 4),
                "BattleExpPrompt" => Memory(app: App, command: 8, getter: Getter, getterState: 11),
                "BattleMoveLearnPrompt" => Memory(app: App, command: 8, getter: Getter, getterState: 21),
                "BattleOverOnCommand45" => Memory(app: App, command: 45, outcome: 5),
                _ => Memory(app: App, state: 10, outcome: 1),
            };
            HgssFieldSystemStateReader reader = new(memory, Table);

            await reader.ReadAsync(CancellationToken.None);
            int first = memory.ByteReads.Count;
            await reader.ReadAsync(CancellationToken.None);

            Assert.Equal(requests + 2, first);
            Assert.Equal(requests, memory.ByteReads.Count - first);
            Assert.Empty(memory.PointerReads);
        }

        [Fact]
        public async Task ChangedAppBetweenReadsIsTransitioning()
        {
            FakeMemoryClient memory = Memory();
            // Read 3 is the first read of the app slots (after the two root reads).
            memory.AfterRead = n =>
            {
                if (n == 3) memory.Place(At(Sub0, Table.SubAppOffset), BitConverter.GetBytes(App));
            };

            GameSnapshot snapshot = await SnapshotOf(memory);

            Assert.Equal(GameScreen.Transitioning, snapshot.Screen);
        }

        // Zero is a bad pointer only where the chain cannot hold NULL; app, taskman and getter are NULL
        // when nothing runs, so they are checked with the out-of-range value only.
        [Theory]
        [InlineData("fs", 0u)]
        [InlineData("fs", OutOfRange)]
        [InlineData("sub0", 0u)]
        [InlineData("sub0", OutOfRange)]
        [InlineData("app", OutOfRange)]
        [InlineData("bs", 0u)]
        [InlineData("bs", OutOfRange)]
        [InlineData("ctx", 0u)]
        [InlineData("ctx", OutOfRange)]
        [InlineData("taskman", OutOfRange)]
        [InlineData("getter", OutOfRange)]
        public async Task OutOfRangePointerIsTransitioning(string hop, uint pointer)
        {
            FakeMemoryClient memory = hop switch
            {
                "fs" => Memory(fs: pointer),
                "sub0" => Memory(sub0: pointer),
                "app" => Memory(app: pointer),
                "bs" => Memory(app: App, command: 5, sel: 1, bs: pointer),
                "ctx" => Memory(app: App, ctx: pointer),
                "taskman" => Memory(taskman: pointer),
                _ => Memory(app: App, command: 5, sel: 1, getter: pointer, getterState: 11),
            };

            GameSnapshot snapshot = await SnapshotOf(memory);

            Assert.Equal(GameScreen.Transitioning, snapshot.Screen);
            Assert.All(memory.ByteReads, read => Assert.InRange(read.Address, 0x02000000u, 0x023FFFFFu));
        }

        [Fact]
        public async Task RootsReReadAfterThreeTransitioning()
        {
            const uint movedFs = 0x02180000;
            const uint movedSub0 = 0x02190000;
            FakeMemoryClient memory = Memory();
            HgssFieldSystemStateReader reader = new(memory, Table);
            int AnchorReads() => memory.ByteReads.Count(read => read.Address == Table.Anchor);
            Assert.Equal(GameScreen.FieldFree, (await reader.ReadAsync(CancellationToken.None)).Screen);

            // The roots move: the cached app slots now hold garbage, and the anchor leads to a free field.
            memory.Place(At(Sub0, Table.SubAppOffset), BitConverter.GetBytes(0x01000000u));
            memory.Place(Table.Anchor, BitConverter.GetBytes(movedFs));
            memory.Place(At(movedFs, Table.AppSlotsOffset), BitConverter.GetBytes(movedSub0));
            memory.Place(At(movedFs, Table.MapReadyOffset), BitConverter.GetBytes(1u));
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(GameScreen.Transitioning, (await reader.ReadAsync(CancellationToken.None)).Screen);
            }
            Assert.Equal(1, AnchorReads());

            GameSnapshot snapshot = await reader.ReadAsync(CancellationToken.None);

            Assert.Equal(GameScreen.FieldFree, snapshot.Screen);
            Assert.Equal(2, AnchorReads());
            Assert.Empty(memory.PointerReads);
        }
    }
}
