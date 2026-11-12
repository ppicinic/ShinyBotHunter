using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
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

        private static GameSnapshot Script(uint? native, int? textPrinter = null)
            => new(GameScreen.FieldTask, null, CircleScript, null, RunScripts, native, TextPrinter: textPrinter);

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
    }
}
