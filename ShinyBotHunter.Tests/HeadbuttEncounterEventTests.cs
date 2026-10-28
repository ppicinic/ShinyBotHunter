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

        private const uint RunScripts = 0x0203FF45;
        private const uint Printing = 0x021EF349;
        private const uint MenuChoice = 0x020477C1;
        private const uint TouchMenuHide = 0x020476E9;

        private const string A = "T.A;W.20";
        private const string B = "T.B;W.20";

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        private static GameSnapshot Script(uint? native, int script, int? textPrinter = null)
            => new(GameScreen.FieldTask, null, script, null, RunScripts, native, TextPrinter: textPrinter);

        // Script 10014 from the A to the field task that starts the encounter (research/headbutt.md).
        private static GameSnapshot[] HeadbuttScript() =>
        [
            Script(Printing, 10014, 0), Script(Printing, 10014, 2), Script(Printing, 10014), Script(TouchMenuHide, 10014), Script(MenuChoice, 10014),
            Script(Printing, 10014, 0), Script(Printing, 10014, 2), Script(Printing, 10014), Script(0x020492F1, 10014),
            new GameSnapshot(GameScreen.FieldTask, null, null, null, 0x020551B9),
        ];

        private static void Stand(FakeMemoryClient memory, int x, int y, Direction facing)
        {
            memory.Place(Table.Anchor, BitConverter.GetBytes(AnchorTarget));
            memory.Place((uint)(AnchorTarget + Table.FacingOffset), [(byte)facing]);
            memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes((uint)y << 16));
        }

        private static GameState Encountering(int startX = 426, int startY = 464, Direction startFacing = Direction.Down)
        {
            GameState state = new() { StartX = startX, StartY = startY, StartFacing = startFacing };
            state.State.Push(EventState.Encountering);
            return state;
        }

        private static Task<(string Result, string? Stop)> Return(GameState state, FakeMemoryClient memory, Func<string, Task> run, Action? clear = null)
            => HeadbuttEncounterEvent.ReturnToTreeAsync(state, new ScriptedReader(Free), new HgssOverworldReader(memory, Table), Table, run, clear ?? (() => { }), CancellationToken.None);

        [Fact]
        public async Task ReturnDoesNothingAtTheStartTileAndFacing()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 464, Direction.Down);
            List<string> macros = [];

            var (result, stop) = await Return(Encountering(), memory, m => { macros.Add(m); return Task.CompletedTask; });

            Assert.Equal("none", result);
            Assert.Null(stop);
            Assert.Empty(macros);
        }

        [Fact]
        public async Task ReturnFromBehindStepsDownThenFacesTheTree()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 463, Direction.Up);
            GameState state = Encountering();
            List<string> macros = [];

            var (result, stop) = await Return(state, memory, m => { macros.Add(m); Stand(memory, 426, 464, Direction.Down); return Task.CompletedTask; });

            string expected = $"M.Down.464.{Table.TrainerY};W.8;H.Down.3;W.10";
            Assert.Equal([expected], macros);
            Assert.Equal("built", result);
            Assert.Null(stop);
            Assert.Equal(expected, state.EncounterMacros["Headbutt:426,463,Up"]);
        }

        [Fact]
        public async Task ReturnFromBesideStepsRightThenFacesTheTree()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 425, 464, Direction.Left);
            List<string> macros = [];

            var (result, _) = await Return(Encountering(), memory, m => { macros.Add(m); Stand(memory, 426, 464, Direction.Down); return Task.CompletedTask; });

            Assert.Equal([$"M.Right.426.{Table.TrainerX};W.8;H.Down.3;W.10"], macros);
            Assert.Equal("built", result);
        }

        [Fact]
        public async Task ReturnUsesTheCachedMacroForAKnownTile()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 463, Direction.Up);
            GameState state = Encountering();
            state.EncounterMacros["Headbutt:426,463,Up"] = "sentinel";
            List<string> macros = [];

            var (result, _) = await Return(state, memory, m => { macros.Add(m); Stand(memory, 426, 464, Direction.Down); return Task.CompletedTask; });

            Assert.Equal(["sentinel"], macros);
            Assert.Equal("cached", result);
        }

        [Fact]
        public async Task ReturnThatMissesRebuildsOnceFromWhereThePlayerIs()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 463, Direction.Up);
            List<string> macros = [];

            var (result, stop) = await Return(Encountering(), memory, m =>
            {
                macros.Add(m);
                if (macros.Count == 1) Stand(memory, 425, 464, Direction.Left);
                else Stand(memory, 426, 464, Direction.Down);
                return Task.CompletedTask;
            });

            Assert.Equal(2, macros.Count);
            Assert.Equal($"M.Right.426.{Table.TrainerX};W.8;H.Down.3;W.10", macros[1]);
            Assert.Equal("rebuilt", result);
            Assert.Null(stop);
        }

        [Fact]
        public async Task ReturnThatMissesTwiceStopsWithThePosition()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 463, Direction.Up);
            List<string> macros = [];

            var (_, stop) = await Return(Encountering(), memory, m => { macros.Add(m); return Task.CompletedTask; });

            Assert.Equal(2, macros.Count);
            Assert.Contains("(426, 463) facing Up", stop);
        }

        // A step into something that blocks it never ends; the stale watch clears it and the miss path takes over.
        [Fact]
        public async Task ReturnMacroThatNeverFinishesIsClearedAndCountsAsAMiss()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 463, Direction.Up);
            TaskCompletionSource blocked = new();
            List<string> macros = [];
            int clears = 0;

            var (result, stop) = await Return(Encountering(), memory, m =>
            {
                macros.Add(m);
                if (macros.Count == 1) return blocked.Task;
                Stand(memory, 426, 464, Direction.Down);
                return Task.CompletedTask;
            }, () => { clears++; blocked.SetResult(); });

            Assert.Equal(1, clears);
            Assert.Equal(2, macros.Count);
            Assert.Equal("rebuilt", result);
            Assert.Null(stop);
        }

        [Theory]
        [InlineData(427, 465)]
        [InlineData(428, 466)]
        public async Task ReturnFarOffBothAxesStops(int x, int y)
        {
            FakeMemoryClient memory = new();
            Stand(memory, x, y, Direction.Up);
            List<string> macros = [];

            var (_, stop) = await Return(Encountering(), memory, m => { macros.Add(m); return Task.CompletedTask; });

            Assert.Empty(macros);
            Assert.Contains($"({x}, {y})", stop);
        }

        private static Task<string?> Cycle(GameState state, ScriptedReader reader, FakeMemoryClient memory, Func<string, Task> run)
            => HeadbuttEncounterEvent.RunCycleAsync(state, reader, new HgssOverworldReader(memory, Table), Table, run, () => { }, CancellationToken.None);

        [Fact]
        public async Task NothingInFrontParksInWaiting()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 464, Direction.Down);
            GameState state = Encountering();
            List<string> presses = [];

            string? park = await Cycle(state, new ScriptedReader(Free), memory, m => { presses.Add(m); return Task.CompletedTask; });

            Assert.Equal([A, A, A], presses);
            Assert.Equal("Nothing in front of you starts a script; stand facing a headbutt tree.", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task AnotherScriptIsClosedWithBThenParks()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 464, Direction.Down);
            GameState state = Encountering();
            List<string> presses = [];

            string? park = await Cycle(state, new ScriptedReader(Free, Script(Printing, 1234, 2), Script(Printing, 1234, 2), Free), memory,
                m => { presses.Add(m); return Task.CompletedTask; });

            Assert.Equal(A, presses[0]);
            Assert.All(presses.Skip(1), p => Assert.Equal(B, p));
            Assert.Equal("The thing in front of you is not a headbutt tree (script 1234).", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task HeadbuttThatStartsABattlePushesBattleStart()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 426, 464, Direction.Down);
            GameState state = Encountering();
            ScriptedReader reader = new([Free, .. HeadbuttScript(), new GameSnapshot(GameScreen.Transitioning, null, null, null, null),
                new GameSnapshot(GameScreen.BattleStarting, null, null, null, null)]);

            string? park = await Cycle(state, reader, memory, _ => Task.CompletedTask);

            Assert.Null(park);
            Assert.Equal([EventState.BattleStart], state.State);
        }

        // The tree with no Pokémon (research/headbutt.md): from (425, 464) facing Down, the swap leaves the player on
        // (426, 464) facing Right.
        [Fact]
        public async Task TreeWithNoPokemonReturnsThenParks()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 425, 464, Direction.Down);
            GameState state = Encountering(startX: 425);
            ScriptedReader reader = new([Free, .. HeadbuttScript(), Free]);
            List<string> macros = [];

            string? park = await Cycle(state, reader, memory, m =>
            {
                macros.Add(m);
                if (m == B) Stand(memory, 426, 464, Direction.Right);
                if (m.StartsWith("M.")) Stand(memory, 425, 464, Direction.Down);
                return Task.CompletedTask;
            });

            Assert.Equal($"M.Left.425.{Table.TrainerX};W.8;H.Down.3;W.10", macros[^1]);
            Assert.Equal(B, macros[^2]);
            Assert.Equal("The tree in front of you gave no Pokémon. It may give none for your trainer ID, or a Repel is active.", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public void ChainedLegDropsHeadbuttMacros()
        {
            GameState state = new();
            state.EncounterMacros["Run"] = "run";
            state.EncounterMacros["Headbutt:426,463,Up"] = "behind";
            state.EncounterMacros["Headbutt:425,464,Left"] = "beside";

            ChainedOrchestratorEvent.ApplyLeg(state, new EventLeg(null!, "leg", new EncounterParams(EncounterType.Headbutt), TrainType.None, null, null));

            Assert.Empty(state.EncounterMacros);
        }
    }
}
