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
        private const uint StartMenuTask = 0x0203BEF1;

        private static readonly GameSnapshot StartMenu = ChildTask(StartMenuTask);
        private static readonly GameSnapshot PartyList = new(GameScreen.OtherApp, null, null, null, null, AppState: 1, PartyMonIndex: 0, AppInit: PartyMenuInit);
        private static readonly GameSnapshot SummaryScreen = new(GameScreen.OtherApp, null, null, null, null, AppState: 2, AppInit: SummaryInit);

        private static GameSnapshot SummaryLoading(int state) => new(GameScreen.OtherApp, null, null, null, null, AppState: state, AppInit: SummaryInit);

        private static GameSnapshot ContextMenu(int slot) => new(GameScreen.OtherApp, null, null, null, null, AppState: 2, PartyMonIndex: slot, AppInit: PartyMenuInit);

        private static readonly string[] SummaryRoute = ["W.5;S.44.77.3", "W.5;S.195.34.3", "W.5;S.195.41.3"];

        // Each confirm comes only after two reads of the snapshot the press was sent at.
        private static readonly GameSnapshot[][] ToTheSummarySlowly =
        [
            [StartMenu],
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
            // POKéMON's gate, the start menu, was confirmed by StartMenuAsync before the step; each later touch is sent at the
            // confirm before it.
            Assert.Equal([PartyList, ContextMenu(1)], presses.Skip(1).Select(p => p.At));
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
        public async Task SlotTwoIsReadOnlyOnceTheSummaryIsDrawn()
        {
            PressedReader reader = new([StartMenu], [PartyList], [ContextMenu(1)], [SummaryLoading(0), SummaryLoading(1), SummaryScreen]);
            List<GameSnapshot> readAt = [];

            var (mon, _) = await HgssStaticEncounterEvent.SummaryAsync(reader, Table, Advancing(reader, []), Dialga, () =>
            {
                readAt.Add(reader.Last);
                return Task.FromResult(Party(483, 0x12345678));
            }, Long, CancellationToken.None);

            GameSnapshot at = Assert.Single(readAt);
            Assert.Equal(SummaryInit, at.AppInit);
            Assert.Equal(2, at.AppState);
            Assert.Equal(0x12345678u, mon!.PID);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task ASummaryThatStaysLoadingIsAStaleTimeout(int state)
        {
            PressedReader reader = new([StartMenu], [PartyList], [ContextMenu(1)], [SummaryLoading(state)]);
            List<(string Macro, GameSnapshot At)> presses = [];

            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(() => HgssStaticEncounterEvent.SummaryAsync(reader, Table, Advancing(reader, presses),
                Dialga, () => throw new InvalidOperationException("slot 2 read"), Stale, CancellationToken.None));

            Assert.Equal("slot 2's summary after SUMMARY", ex.WaitingFor);
            Assert.Contains($"AppState = {state}", ex.Message);
            Assert.Contains($"AppInit = {SummaryInit}", ex.Message);
            Assert.Equal(SummaryRoute, presses.Select(p => p.Macro));
        }

        [Fact]
        public async Task AContextMenuOnAnotherSlotIsNotTheSummaryRoute()
        {
            PressedReader reader = new([StartMenu], [PartyList], [ContextMenu(0)]);
            List<(string Macro, GameSnapshot At)> presses = [];

            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(() => HgssStaticEncounterEvent.SummaryAsync(reader, Table, Advancing(reader, presses),
                Dialga, () => throw new InvalidOperationException("slot 2 read"), Stale, CancellationToken.None));

            Assert.Equal("slot 2's context menu", ex.WaitingFor);
            Assert.Equal(SummaryRoute[..2], presses.Select(p => p.Macro));
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

        private const string NotOneArceus = "The party must be one Arceus and nothing else.";

        private static readonly GameSnapshot[] AfterTheWalk = [Free, Script(null), Script(MenuChoice)];

        // Before any press, after the save, after the walk.
        private static readonly GameSnapshot[][] ToTheCircle = [[Free], [Free], AfterTheWalk];

        // After the Yes, after the cutscene's A at the arrow, after the nickname's No.
        private static readonly GameSnapshot[][] ThroughTheHandOver =
        [
            [ChildTask(CutsceneTask), ChildTask(CutsceneTask2), Script(Printing, 0), Script(Printing, 2)],
            [Script(Printing), Script(MenuChoice)],
            [Script(null), Free],
        ];

        // After X, after POKéMON, after slot 2, after SUMMARY.
        private static readonly GameSnapshot[][] ToTheSummary = [[StartMenu], [PartyList], [ContextMenu(1)], [SummaryScreen]];

        // After the soft reset, after the continue.
        private static readonly GameSnapshot[][] ThroughTheReset = [[Free], [Free]];

        private static readonly GameSnapshot[][] FirstAttempt = [.. ToTheCircle, .. ThroughTheHandOver, .. ToTheSummary, .. ThroughTheReset];

        // From the walk's press to the reset's continue.
        private static readonly GameSnapshot[][] AnotherAttempt = [AfterTheWalk, .. ThroughTheHandOver, .. ToTheSummary, .. ThroughTheReset];

        // Trainer ID and secret ID are 0, so PID 0x00010001 has shiny value 0.
        private const uint NotShinyPid = 0x12345678;
        private const uint ShinyPid = 0x00010001;

        // The hunt's boundaries, recording each call in order; checks answers from the list given, one per call.
        private sealed class Boundaries(uint pid, params string?[] checkAnswers)
        {
            private int checks;

            public List<(string Call, PokemonData? Mon)> Calls { get; } = [];

            public Task<string?> Checks()
            {
                Calls.Add(("checks", null));
                return Task.FromResult(checkAnswers[checks++]);
            }

            public Task<PokemonState> ReadParty()
            {
                Calls.Add(("readParty", null));
                return Task.FromResult(Party(483, pid));
            }

            public Func<PersistPokemonData, Task> Record(string call) => persist =>
            {
                Calls.Add((call, persist.PokemonData));
                return Task.CompletedTask;
            };

            public int Count(string call) => Calls.Count(c => c.Call == call);
        }

        private static StaticEncounterConfig Config(int cutsceneStaleMs = 200000)
        {
            StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);
            config.SoftResetDelayMs = 0;
            config.PostContinueDelayMs = 0;
            config.CutsceneStaleMs = cutsceneStaleMs;
            return config;
        }

        // A bot standing where the walk was made from, whose snapshots follow its presses and whose party count is the given
        // one. Its profile's map service is empty, so a search would throw.
        private static FakeBot HuntingBot(List<string> presses, PressedReader reader, int partyCount)
        {
            FakeMemoryClient memory = new();
            Stand(memory, 522, 16, 14, Direction.Up);
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

        private static Task<string?> HuntAsync(GameState state, FakeBot bot, StaticEncounterConfig config, Boundaries? boundaries = null)
        {
            boundaries ??= new Boundaries(NotShinyPid, NotOneArceus);
            return HgssStaticEncounterEvent.HuntAsync(state, bot, config, config.Find("Dialga (Sinjoh Ruins)"), WalkMacro, (16, 14, Direction.Up),
                boundaries.Checks, boundaries.ReadParty, boundaries.Record("addEncounter"), boundaries.Record("alert"), boundaries.Record("addCaught"),
                TimeSpan.Zero, CancellationToken.None);
        }

        private static string Macro(string name) => MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, name);

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

            string? stop = await HgssStaticEncounterEvent.ResetAsync(bot, Table, config, (16, 14, Direction.Up), () => Task.FromResult<string?>(null),
                Long, CancellationToken.None);

            Assert.Equal("After the soft reset the player is at tile (16, 15) facing down, not (16, 14) facing up, where the walk was made from.", stop);
            Assert.Equal([MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, config.SoftResetMacro), MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, config.ContinueMacro)],
                presses);
        }

        // A bot standing where the walk was made from, whose snapshots follow its presses, recording each press with the
        // snapshot that was current when it was sent.
        private static FakeBot ResettingBot(PressedReader reader, List<(string Macro, GameSnapshot At)> presses)
        {
            FakeMemoryClient memory = new();
            Stand(memory, 522, 16, 14, Direction.Up);
            return new FakeBot(Task.CompletedTask)
            {
                Pressed = macro =>
                {
                    presses.Add((macro, reader.Last));
                    reader.Advance();
                },
                State = reader,
                Overworld = new HgssOverworldReader(memory, Table),
            };
        }

        private static readonly GameSnapshot Rebooting = new(GameScreen.Transitioning, null, null, null, null);

        [Fact]
        public async Task TheContinueWaitsForTheSummaryToLeave()
        {
            // Before the reset, after the reset, after the continue.
            PressedReader reader = new([SummaryScreen], [SummaryScreen, SummaryScreen, Rebooting], [Free]);
            List<(string Macro, GameSnapshot At)> presses = [];
            FakeBot bot = ResettingBot(reader, presses);

            string? stop = await HgssStaticEncounterEvent.ResetAsync(bot, Table, Config(), (16, 14, Direction.Up), () => Task.FromResult<string?>(null),
                Long, CancellationToken.None);

            Assert.Null(stop);
            Assert.Equal([Macro("SoftResetHeld"), Macro("ContinueGameWithRandom")], presses.Select(p => p.Macro));
            Assert.Equal(Rebooting, presses[1].At);
        }

        [Fact]
        public async Task AResetThatDoesNotTakeStopsBeforeTheContinue()
        {
            PressedReader reader = new([SummaryScreen], [SummaryScreen]);
            List<(string Macro, GameSnapshot At)> presses = [];
            FakeBot bot = ResettingBot(reader, presses);
            int checks = 0;

            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(() => HgssStaticEncounterEvent.ResetAsync(bot, Table, Config(), (16, 14, Direction.Up),
                () => { checks++; return Task.FromResult<string?>(null); }, Stale, CancellationToken.None));

            Assert.StartsWith("Waited for the soft reset to leave the summary; the game state stayed", ex.Message);
            Assert.Contains($"AppInit = {SummaryInit}", ex.Message);
            Assert.Equal([Macro("SoftResetHeld")], presses.Select(p => p.Macro));
            Assert.Equal(0, checks);
        }

        [Fact]
        public async Task TheSaveAtRunIsTheFirstPressAndHappensOnce()
        {
            List<string> presses = [];
            FakeBot bot = HuntingBot(presses, new PressedReader(FirstAttempt), partyCount: 2);

            string? stop = await HuntAsync(Running(), bot, Config());

            string save = Macro("SaveGame");
            Assert.Equal(NotOneArceus, stop);
            Assert.Equal([save, WalkMacro, Yes, HoldA, "W.5;H.B.6;W.20", "H.X.6", .. SummaryRoute, Macro("SoftResetHeld"), Macro("ContinueGameWithRandom")], presses);
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

        [Fact]
        public async Task SeveralAttemptsReplayTheMacroAndNeverSearch()
        {
            List<string> presses = [];
            FakeBot bot = HuntingBot(presses, new PressedReader([.. FirstAttempt, .. AnotherAttempt]), partyCount: 2);
            Boundaries boundaries = new(NotShinyPid, null, NotOneArceus);

            string? stop = await HuntAsync(Running(), bot, Config(), boundaries);

            Assert.Equal(NotOneArceus, stop);
            Assert.Equal([WalkMacro, WalkMacro], presses.Where(p => p.StartsWith("M.")));
            Assert.Single(presses, p => p == Macro("SaveGame"));
            Assert.Equal(2, presses.Count(p => p == Macro("SoftResetHeld")));
            Assert.Equal(2, boundaries.Count("addEncounter"));
            Assert.Equal(2, boundaries.Count("checks"));
            Assert.Equal(0, boundaries.Count("alert"));
            Assert.Equal(0, boundaries.Count("addCaught"));
        }

        [Fact]
        public async Task AShinyAlertsOnceSavesRecordsAndCompletes()
        {
            List<string> presses = [];
            // After B from the summary, after B from the list, after the save.
            GameSnapshot[][] stages = [.. ToTheCircle, .. ThroughTheHandOver, .. ToTheSummary, [PartyList], [Free], [Free]];
            FakeBot bot = HuntingBot(presses, new PressedReader(stages), partyCount: 2);
            Boundaries boundaries = new(ShinyPid);
            GameState state = Running();

            string? stop = await HuntAsync(state, bot, Config(), boundaries);

            Assert.Null(stop);
            Assert.Equal(["W.5;H.B.6", "W.5;H.B.6", Macro("SaveGame")], presses[^3..]);
            Assert.DoesNotContain(Macro("SoftResetHeld"), presses);
            // Only the walk's clear, once the circle's script started; no stop's.
            Assert.Equal(["clear"], bot.Macros);
            Assert.Equal(["readParty", "addEncounter", "alert", "addCaught"], boundaries.Calls.Select(c => c.Call));
            PokemonData alerted = boundaries.Calls.Single(c => c.Call == "alert").Mon!;
            Assert.Equal(ShinyPid, alerted.PID);
            Assert.Same(alerted, boundaries.Calls.Single(c => c.Call == "addCaught").Mon);
            Assert.Equal(EventState.Complete, state.State.Peek());
        }
    }
}
