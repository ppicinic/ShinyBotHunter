using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // RunMacro asks the registry for RunHold, so the working directory has to be the app project before anything
    // touches MacroRegistry.Instance (see MacroTests). The RS strings are tested on the static seams because FakeBot's
    // Addresses(0) has no RS revision 0.
    [Collection("macro-registry")]
    public class RunEncounterEventTests
    {
        private static readonly HgssPointerService Hgss = new();

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);
        private static readonly GameSnapshot Battle = new(GameScreen.BattleTurn, null, null, null, null);

        public RunEncounterEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        private static FakeBot HgssBot(int x, int y)
        {
            FakeMemoryClient memory = new();
            memory.Place(Hgss.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Hgss.TrainerY, BitConverter.GetBytes((uint)y << 16));
            return new FakeBot(Task.CompletedTask) { State = new ScriptedReader(Free, Battle), Overworld = new HgssOverworldReader(memory, Hgss) };
        }

        private static GameState Encountering(int startX, int startY)
        {
            GameState state = new() { StartX = startX, StartY = startY };
            state.State.Push(EventState.Encountering);
            return state;
        }

        // Literals copied from origin/main's template: TrainerY 0x21DA6FC = 35497724, TrainerX 0x21DA6F4 = 35497716.
        [Theory]
        [InlineData(2, 0, "M.Up.18.35497724;M.Down.22.35497724")]
        [InlineData(0, 2, "M.Right.12.35497716;M.Left.8.35497716")]
        public void HgssRunMacroIsTodaysText(int yRange, int xRange, string expected)
        {
            string macro = RunEncounterEvent.RunMacro(new HgssPointerService(), GameFlavor.HGSS, new EncounterParams(EncounterType.Run, yRange, xRange), 10, 20);

            Assert.Equal(expected, macro);
        }

        // y is 0x02025734 = 33707828, x two bytes earlier = 33707826.
        [Theory]
        [InlineData(1, 0, "M.Up.3.33707828:H.B.1;M.Down.5.33707828")]
        [InlineData(0, 1, "M.Right.15.33707826:H.B.1;M.Left.13.33707826")]
        public void RsRunMacroHoldsBInTheFirstMove(int yRange, int xRange, string expected)
        {
            string macro = RunEncounterEvent.RunMacro(new RsPointerService(), GameFlavor.RS, new EncounterParams(EncounterType.Run, yRange, xRange), 14, 4);

            Assert.Equal(expected, macro);
        }

        // docs/research/rs-wild-encounter.md, "The Run macro's first frame": a leading H.B.1 step overshoots the line.
        [Theory]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        public void RunHoldIsNeverALeadingStep(int yRange, int xRange)
        {
            string[] steps = RunEncounterEvent.RunMacro(new RsPointerService(), GameFlavor.RS, new EncounterParams(EncounterType.Run, yRange, xRange), 14, 4).Split(';');

            Assert.Equal(2, steps.Length);
            Assert.All(steps, step => Assert.StartsWith("M.", step));
            Assert.DoesNotContain("H.B.1", steps);
        }

        [Theory]
        [InlineData(1, 0, 14, 3, true)]
        [InlineData(1, 0, 14, 4, true)]
        [InlineData(1, 0, 14, 5, true)]
        [InlineData(1, 0, 14, 2, false)]
        [InlineData(1, 0, 14, 6, false)]
        [InlineData(1, 0, 13, 4, false)]
        [InlineData(0, 1, 13, 4, true)]
        [InlineData(0, 1, 15, 4, true)]
        [InlineData(0, 1, 16, 4, false)]
        [InlineData(0, 1, 14, 5, false)]
        public void OffTheLine(int yRange, int xRange, short x, short y, bool onLine)
        {
            string? stop = RunEncounterEvent.OffTheLine(new EncounterParams(EncounterType.Run, yRange, xRange), 14, 4, (x, y));

            Assert.Equal(onLine ? null : "The player is off the run line.", stop);
        }

        [Fact]
        public async Task HgssRunStartsTheLineAndWaitsForABattle()
        {
            FakeBot bot = HgssBot(10, 20);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(["RP:M.Up.18.35497724;M.Down.22.35497724", "clear"], bot.Macros);
            Assert.Equal("M.Up.18.35497724;M.Down.22.35497724", state.EncounterMacros["Run"]);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        // A change for HGSS: from off the line the macro used to hold a direction for ever.
        [Fact]
        public async Task HgssRunOffTheLineParksWithoutPressing()
        {
            FakeBot bot = HgssBot(10, 23);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Empty(bot.Macros);
        }
    }
}
