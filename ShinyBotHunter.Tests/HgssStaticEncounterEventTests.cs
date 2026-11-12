using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The static encounter hunt's steps over scripted snapshots and a fake memory; values from docs/research/sinjoh-static.md
    // (map 522, Dialga's circle at (12, 15)).
    [Collection("macro-registry")]
    public class HgssStaticEncounterEventTests
    {
        private static readonly HgssPointerService Table = new();
        // Stale for the tests that time out; Long for those whose scripted snapshots repeat over a few polls on the way.
        private static readonly TimeSpan Stale = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

        // Where the anchor points; any RAM value works.
        private const uint FieldSystem = 0x02300000;

        private const uint RunScripts = 0x0203FF45;
        private const uint Printing = 0x021EF349;
        private const uint MenuChoice = 0x020477C1;
        private const int CircleScript = 1;
        // Two of the cutscene's task functions, with no script native; any values work.
        private const uint CutsceneTask = 0x0221A001;
        private const uint CutsceneTask2 = 0x0221A101;

        private const string HoldA = "H.A.6;W.20";
        private const string HoldB = "H.B.6;W.20";
        private const string Yes = "W.5;H.A.6;W.20";

        // The pathfinder's macro to Dialga's circle from (16, 14) facing up.
        private const string WalkMacro = "M.Left.12.35497716;M.Down.15.35497724";

        public HgssStaticEncounterEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        // Serves each stage's snapshots in turn, the last repeating; every press moves to the next stage, so a test does not
        // depend on how many polls a step makes.
        private sealed class PressedReader(params GameSnapshot[][] stages) : IGameStateReader
        {
            private int stage;
            private Queue<GameSnapshot> queue = new(stages[0]);

            // The snapshot last served.
            public GameSnapshot Last { get; private set; }

            public void Advance() => queue = new(stages[++stage]);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                Last = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                return Task.FromResult(Last);
            }
        }

        private static GameSnapshot Script(uint? native, int? textPrinter = null)
            => new(GameScreen.FieldTask, null, CircleScript, null, RunScripts, native, TextPrinter: textPrinter);

        private static GameSnapshot ChildTask(uint function) => new(GameScreen.FieldTask, null, null, null, function);

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);

        private static StaticEncounterConfig.Entry Dialga => StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath).Find("Dialga (Sinjoh Ruins)");

        private static void Stand(FakeMemoryClient memory, int map, int x, int y, Direction facing)
        {
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place((uint)(FieldSystem + Table.MapIdOffset), BitConverter.GetBytes((ushort)map));
            memory.Place((uint)(FieldSystem + Table.FacingOffset), [(byte)facing]);
            memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes((uint)y << 16));
        }

        private static FakeMemoryClient PartyCount(int count)
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place((uint)(FieldSystem + Table.PartySize), [(byte)count]);
            return memory;
        }

        // The press completes at once and is recorded.
        private static Func<string, Task> Recording(List<string> presses) => macro =>
        {
            presses.Add(macro);
            return Task.CompletedTask;
        };

        // A bot that records what it is told to run, sees the given snapshots, and stands where the test says.
        private static FakeBot WalkingBot(List<string> presses, IGameStateReader state, int x, int y, Direction facing)
        {
            FakeMemoryClient memory = new();
            Stand(memory, 522, x, y, facing);
            return new FakeBot(Task.CompletedTask) { Pressed = presses.Add, State = state, Overworld = new HgssOverworldReader(memory, Table) };
        }

        [Fact]
        public async Task TheWalkEndsWhenTheCirclesScriptStarts()
        {
            List<string> presses = [];
            FakeBot bot = WalkingBot(presses, new ScriptedReader(Free, Free, Free, Script(null)), 12, 15, Direction.Down);

            string? stop = await HgssStaticEncounterEvent.WalkAsync(bot, Dialga, WalkMacro, Long, CancellationToken.None);

            Assert.Null(stop);
            Assert.Equal([WalkMacro], presses);
            Assert.Equal(["clear"], bot.Macros);
        }

        [Fact]
        public async Task TheWalkEndingOnTheSpotWithNoScriptDidNotRespond()
        {
            List<string> presses = [];
            FakeBot bot = WalkingBot(presses, new ScriptedReader(Free), 12, 15, Direction.Down);

            string? stop = await HgssStaticEncounterEvent.WalkAsync(bot, Dialga, WalkMacro, Stale, CancellationToken.None);

            Assert.Equal(HgssStaticEncounterEvent.DidNotRespond, stop);
        }

        [Fact]
        public async Task TheWalkEndingOffTheSpotNamesTheTile()
        {
            List<string> presses = [];
            FakeBot bot = WalkingBot(presses, new ScriptedReader(Free), 14, 14, Direction.Left);

            string? stop = await HgssStaticEncounterEvent.WalkAsync(bot, Dialga, WalkMacro, Stale, CancellationToken.None);

            Assert.Equal("The walk to Dialga (Sinjoh Ruins) ended on map 522, tile (14, 14), facing left; the circle is at (12, 15).", stop);
        }

        // The presses this event gives the presser are the ones it sends, at the arrow, the menu and the arrow after the Yes.
        [Fact]
        public async Task ThePresserSendsTheGivenPresses()
        {
            ScriptedReader reader = new(
                Script(Printing, 0), Script(Printing, 2), Script(Printing), Script(MenuChoice), Script(Printing, 0), Script(Printing, 2), Script(Printing), Free);

            List<string> presses = [];
            await NavigatorEvent.PressThroughScriptAsync(reader, Table, Recording(presses), "the script", Long, CancellationToken.None,
                pressA: HoldA, pressB: HoldB);

            Assert.Equal([HoldA, Yes, HoldB], presses);
        }

        [Fact]
        public async Task TheCirclesMenuGetsOneAAndNothingElse()
        {
            ScriptedReader reader = new(Script(null), Script(MenuChoice), Script(MenuChoice), Script(MenuChoice), Script(MenuChoice), Script(null));

            List<string> presses = [];
            string? stop = await HgssStaticEncounterEvent.PromptAsync(reader, Table, Recording(presses), Long, Long, CancellationToken.None);

            Assert.Null(stop);
            Assert.Equal([Yes], presses);
        }

        [Fact]
        public async Task TheCircleRefusingIsTheDidNotRespondStop()
        {
            ScriptedReader reader = new(Script(Printing, 0), Script(Printing, 2), Script(Printing), Free);

            List<string> presses = [];
            string? stop = await HgssStaticEncounterEvent.PromptAsync(reader, Table, Recording(presses), Long, Long, CancellationToken.None);

            Assert.Equal(HgssStaticEncounterEvent.DidNotRespond, stop);
            Assert.Equal([HoldA], presses);
        }

        [Fact]
        public async Task TheMenuThatNeverClosesIsAStaleTimeoutNamingTheMenu()
        {
            ScriptedReader reader = new(Script(MenuChoice));

            List<string> presses = [];
            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(
                () => HgssStaticEncounterEvent.PromptAsync(reader, Table, Recording(presses), Long, Stale, CancellationToken.None));

            Assert.Equal("the circle menu to take the Yes", ex.WaitingFor);
            Assert.Equal([Yes], presses);
        }

        [Fact]
        public async Task PartyCountIsReadFromTheFieldSystem()
        {
            FakeMemoryClient memory = PartyCount(2);

            int count = await HgssStaticEncounterEvent.ReadPartyCountAsync(memory, Table, CancellationToken.None);

            Assert.Equal(2, count);
            var (blocks, _) = Assert.Single(memory.ChainReads);
            ChainBlockRequest block = Assert.Single(blocks);
            Assert.Equal(Table.Anchor, block.Anchor);
            Assert.Equal([Table.PartySize], block.Hops);
            Assert.Equal(1, block.Length);
        }

        // Three task functions with no native, the printer drawing, at its arrow and drawing again, then the nickname menu,
        // which stays up until after the No.
        [Fact]
        public async Task TheCutscenePressesAOnlyAtTheArrow()
        {
            ScriptedReader reader = new(
                ChildTask(CutsceneTask), ChildTask(CutsceneTask2), ChildTask(CutsceneTask), Script(Printing, 0), Script(Printing, 2), Script(Printing, 0),
                Script(MenuChoice), Script(MenuChoice), Script(MenuChoice), Script(null));

            List<string> presses = [];
            string? stop = await HgssStaticEncounterEvent.CutsceneAsync(reader, PartyCount(2), Table, Recording(presses), Long, Long, CancellationToken.None);

            Assert.Null(stop);
            Assert.Equal([HoldA, "W.5;H.B.6;W.20"], presses);
        }

        [Fact]
        public async Task TheCutscenesStaleTimeoutNamesWhatItSaw()
        {
            ScriptedReader reader = new(ChildTask(CutsceneTask));

            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(() =>
                HgssStaticEncounterEvent.CutsceneAsync(reader, PartyCount(1), Table, Recording([]), Stale, Long, CancellationToken.None));

            Assert.StartsWith("Waited for the cutscene to reach the nickname question; the game state stayed", ex.Message);
            Assert.Contains($"TaskFunction = {CutsceneTask}", ex.Message);
        }

        [Fact]
        public async Task TheNicknameQuestionWithoutTheHandOverStops()
        {
            ScriptedReader reader = new(Script(MenuChoice));

            List<string> presses = [];
            string? stop = await HgssStaticEncounterEvent.CutsceneAsync(reader, PartyCount(1), Table, Recording(presses), Long, Long, CancellationToken.None);

            Assert.Equal("At the nickname question the party holds 1, not 2.", stop);
            Assert.Empty(presses);
        }

        // -- The summary --------------------------------------------------------------------------

        private const uint PartyMenuInit = 0x02078E31;
        private const uint SummaryInit = 0x02088299;
        private const uint StartMenuTask = 0x0203D001;

        private static readonly GameSnapshot StartMenu = ChildTask(StartMenuTask);
        private static readonly GameSnapshot PartyList = new(GameScreen.OtherApp, null, null, null, null, AppState: 1, PartyMonIndex: 0, AppInit: PartyMenuInit);
        private static readonly GameSnapshot SummaryScreen = new(GameScreen.OtherApp, null, null, null, null, AppState: 2, AppInit: SummaryInit);

        private static GameSnapshot ContextMenu(int slot) => new(GameScreen.OtherApp, null, null, null, null, AppState: 2, PartyMonIndex: slot, AppInit: PartyMenuInit);

        private static readonly string[] SummaryRoute = ["H.X.6", "W.5;S.44.77.3", "W.5;S.195.34.3", "W.5;S.195.41.3"];

        // Each confirm comes only after two reads of the snapshot the press was sent at.
        private static readonly GameSnapshot[][] ToTheSummarySlowly =
        [
            [Free],
            [Free, Free, StartMenu],
            [StartMenu, StartMenu, PartyList],
            [PartyList, PartyList, ContextMenu(1)],
            [ContextMenu(1), ContextMenu(1), SummaryScreen],
        ];

        private static PokemonState Party(ushort species, uint pid) => new()
        {
            PartySize = 2,
            PartyPokemon = [new PokemonData { Species = 493 }, new PokemonData { Species = species, PID = pid }, null, null, null, null],
        };

        // Every press is recorded with the snapshot that was current when it was sent.
        private static Func<string, Task> Advancing(PressedReader reader, List<(string Macro, GameSnapshot At)> presses) => macro =>
        {
            presses.Add((macro, reader.Last));
            reader.Advance();
            return Task.CompletedTask;
        };

        [Fact]
        public async Task TheSummaryRouteWaitsOnEachStateBeforeTheNext()
        {
            PressedReader reader = new(ToTheSummarySlowly);
            List<(string Macro, GameSnapshot At)> presses = [];

            var (mon, stop) = await HgssStaticEncounterEvent.SummaryAsync(reader, Table, Advancing(reader, presses), Dialga,
                () => Task.FromResult(Party(483, 0x12345678)), Long, CancellationToken.None);

            Assert.Null(stop);
            Assert.Equal(0x12345678u, mon!.PID);
            Assert.Equal(SummaryRoute, presses.Select(p => p.Macro));
            // X's gate, the free field, was seen by the presser before the step; each touch is sent at the confirm before it.
            Assert.Equal([StartMenu, PartyList, ContextMenu(1)], presses.Skip(1).Select(p => p.At));
        }

        [Fact]
        public async Task SlotTwoIsReadOnlyWithTheSummaryOnScreen()
        {
            PressedReader reader = new(ToTheSummarySlowly);
            List<GameSnapshot> readAt = [];

            await HgssStaticEncounterEvent.SummaryAsync(reader, Table, Advancing(reader, []), Dialga, () =>
            {
                readAt.Add(reader.Last);
                return Task.FromResult(Party(483, 0x12345678));
            }, Long, CancellationToken.None);

            GameSnapshot at = Assert.Single(readAt);
            Assert.Equal(SummaryInit, at.AppInit);
        }

        [Fact]
        public async Task AContextMenuOnAnotherSlotIsNotTheSummaryRoute()
        {
            PressedReader reader = new([Free], [StartMenu], [PartyList], [ContextMenu(0)]);
            List<(string Macro, GameSnapshot At)> presses = [];

            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(() => HgssStaticEncounterEvent.SummaryAsync(reader, Table, Advancing(reader, presses),
                Dialga, () => throw new InvalidOperationException("slot 2 read"), Stale, CancellationToken.None));

            Assert.Equal("slot 2's context menu", ex.WaitingFor);
            Assert.Equal(SummaryRoute[..3], presses.Select(p => p.Macro));
        }

        [Fact]
        public async Task AWrongSpeciesInSlotTwoStops()
        {
            PressedReader reader = new(ToTheSummarySlowly);

            var (mon, stop) = await HgssStaticEncounterEvent.SummaryAsync(reader, Table, Advancing(reader, []), Dialga,
                () => Task.FromResult(Party(25, 0x12345678)), Long, CancellationToken.None);

            Assert.Null(mon);
            Assert.Equal("Party slot 2 is species 25, not Dialga (Sinjoh Ruins)'s species 483.", stop);
        }

        // -- The run ------------------------------------------------------------------------------

        private const string SummaryNotBuilt = "The summary step is not built yet (#356).";

        // Before any press, after the save, after the walk.
        private static readonly GameSnapshot[][] ToTheCircle = [[Free], [Free], [Free, Script(null), Script(MenuChoice)]];

        // After the Yes, after the cutscene's A at the arrow, after the nickname's No.
        private static readonly GameSnapshot[][] ThroughTheHandOver =
        [
            [ChildTask(CutsceneTask), ChildTask(CutsceneTask2), Script(Printing, 0), Script(Printing, 2)],
            [Script(Printing), Script(MenuChoice)],
            [Script(null), Free],
        ];

        private static StaticEncounterConfig Config(int cutsceneStaleMs = 200000)
        {
            StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);
            config.SoftResetDelayMs = 0;
            config.PostContinueDelayMs = 0;
            config.CutsceneStaleMs = cutsceneStaleMs;
            return config;
        }

        // A bot standing on Dialga's circle, whose snapshots follow its presses and whose party count is the given one.
        private static FakeBot HuntingBot(List<string> presses, PressedReader reader, int partyCount)
        {
            FakeMemoryClient memory = new();
            Stand(memory, 522, 12, 15, Direction.Down);
            return new FakeBot(Task.CompletedTask)
            {
                Pressed = macro =>
                {
                    presses.Add(macro);
                    reader.Advance();
                },
                ChainBlocks = _ => [new ChainBlockResult(ChainBlockStatus.Ok, 1, [(byte)partyCount])],
                State = reader,
                Overworld = new HgssOverworldReader(memory, Table),
            };
        }

        private static Task<string?> HuntAsync(GameState state, FakeBot bot, StaticEncounterConfig config)
            => HgssStaticEncounterEvent.HuntAsync(state, bot, config, config.Find("Dialga (Sinjoh Ruins)"), WalkMacro, (16, 14, Direction.Up),
                TimeSpan.Zero, CancellationToken.None);

        private static GameState Running()
        {
            GameState state = new();
            state.State.Push(EventState.Running);
            return state;
        }

        [Fact]
        public async Task AResetLandingOnAnotherTileStops()
        {
            List<string> presses = [];
            FakeBot bot = WalkingBot(presses, new ScriptedReader(Free), 16, 15, Direction.Down);
            StaticEncounterConfig config = Config();

            string? stop = await HgssStaticEncounterEvent.ResetAsync(bot, config, (16, 14, Direction.Up), () => Task.FromResult<string?>(null), CancellationToken.None);

            Assert.Equal("After the soft reset the player is at tile (16, 15) facing down, not (16, 14) facing up, where the walk was made from.", stop);
            Assert.Equal([MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, config.SoftResetMacro), MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, config.ContinueMacro)],
                presses);
        }

        [Fact]
        public async Task TheSaveAtRunIsTheFirstPressAndHappensOnce()
        {
            List<string> presses = [];
            FakeBot bot = HuntingBot(presses, new PressedReader([.. ToTheCircle, .. ThroughTheHandOver]), partyCount: 2);

            string? stop = await HuntAsync(Running(), bot, Config());

            string save = MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "SaveGame");
            Assert.Equal(SummaryNotBuilt, stop);
            Assert.Equal([save, WalkMacro, Yes, HoldA, "W.5;H.B.6;W.20"], presses);
            Assert.Single(presses, p => p == save);
        }

        public static TheoryData<string> Stops => ["did not respond", "no hand-over", "stale cutscene"];

        [Theory]
        [MemberData(nameof(Stops))]
        public async Task EveryStopClearsTheMacroAndThrowsNothing(string stopAt)
        {
            List<string> presses = [];
            GameSnapshot[][] stages = [.. ToTheCircle, .. ThroughTheHandOver];
            int partyCount = 2;
            int cutsceneStaleMs = 200000;
            string expected;
            switch (stopAt)
            {
                case "did not respond":
                    // The circle's script prints its refusal and ends with the field free.
                    stages = [[Free], [Free], [Free, Script(null), Script(Printing, 0), Script(Printing, 2)], [Script(Printing), Free]];
                    expected = HgssStaticEncounterEvent.DidNotRespond;
                    break;
                case "no hand-over":
                    partyCount = 1;
                    expected = "At the nickname question the party holds 1, not 2.";
                    break;
                default:
                    stages = [.. ToTheCircle, [ChildTask(CutsceneTask)]];
                    cutsceneStaleMs = 100;
                    expected = "Waited for the cutscene to reach the nickname question; the game state stayed";
                    break;
            }
            FakeBot bot = HuntingBot(presses, new PressedReader(stages), partyCount);
            GameState state = Running();

            string? stop = await HuntAsync(state, bot, Config(cutsceneStaleMs));

            Assert.StartsWith(expected, stop);
            // The walk's, once the circle's script started, and the stop's.
            Assert.Equal(["clear", "clear"], bot.Macros);
            Assert.Equal(EventState.None, state.State.Peek());
            Assert.Single(presses, p => p == WalkMacro);
        }
    }
}
