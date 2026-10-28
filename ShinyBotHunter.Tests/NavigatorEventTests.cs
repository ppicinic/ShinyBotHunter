using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The press loop over scripted snapshots; values from pointer-discovery.md, "Field scripts".
    public class NavigatorEventTests
    {
        private static readonly HgssPointerService Table = new();
        private static readonly TimeSpan Stale = TimeSpan.FromMilliseconds(200);

        private const uint RunScripts = 0x0203FF45;
        private const uint Printing = 0x021EF349;
        private const uint MenuChoice = 0x020477C1;
        private const uint Movement = 0x02041CA9;
        private const uint WaitStd = 0x02040BCD;
        private const uint TouchMenuHide = 0x020476E9;
        private const uint TouchMenuShow = 0x02047745;
        private const uint FadeTask = 0x02205B15;
        private const uint HealAnimationTask = 0x0224BB91;

        private const string A = "T.A;W.20";
        private const string B = "T.B;W.20";
        private const string Yes = "W.5;T.A;W.20";

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        private static GameSnapshot Script(uint? native, int script = 1, int? textPrinter = null)
            => new(GameScreen.FieldTask, null, script, null, RunScripts, native, TextPrinter: textPrinter);

        // One box of the printing native: drawing, at the arrow, then the slot NULL.
        private static GameSnapshot[] Box(int script = 1) => [Script(Printing, script, 0), Script(Printing, script, 2), Script(Printing, script)];

        private static GameSnapshot ChildTask(uint function) => new(GameScreen.FieldTask, null, null, null, function);

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);

        // The press completes at once and is recorded.
        private static Task PressThrough(ScriptedReader reader, List<string> presses)
            => NavigatorEvent.PressThroughScriptAsync(reader, Table, macro =>
            {
                presses.Add(macro);
                return Task.CompletedTask;
            }, "the script", Stale, CancellationToken.None);

        // The Pokérus trace: greeting, touch menu hide, Yes/No, the nurse moves, message, the fade and the heal
        // animation, the nurse moves, the closing and Pokérus boxes, touch menu show.
        [Fact]
        public async Task HealPressesAUntilTheMenuThenOneYesThenB()
        {
            ScriptedReader reader = new(
            [
                .. Box(), Script(TouchMenuHide), Script(MenuChoice), Script(Movement), .. Box(), Script(null),
                ChildTask(FadeTask), ChildTask(HealAnimationTask), Script(Movement), .. Box(), .. Box(), .. Box(),
                Script(TouchMenuShow), Free,
            ]);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, Yes, B, B, B, B], presses);
        }

        [Fact]
        public async Task PoisonScriptPressesOnceAtTheArrow()
        {
            ScriptedReader reader = new(Script(Printing, 2003, 0), Script(Printing, 2003, 0), Script(Printing, 2003, 2), Script(Printing, 2003), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A], presses);
        }

        [Fact]
        public async Task NothingIsPressedWhilePrintingDraws()
        {
            ScriptedReader reader = new(Script(Printing, 1, 0), Script(Printing, 1, 6), Script(Printing, 1, 0), Script(Printing), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Empty(presses);
        }

        // Between boxes the printer goes back to drawing; 3 is the arrow that scrolls.
        [Fact]
        public async Task MultiBoxMessageGetsOnePressPerArrow()
        {
            ScriptedReader reader = new(
                Script(Printing, 1, 0), Script(Printing, 1, 2), Script(Printing, 1, 0), Script(Printing, 1, 3), Script(Printing), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, A], presses);
        }

        [Fact]
        public async Task ArrowThatDoesNotClearIsRetriedThenThrows()
        {
            List<string> presses = [];
            ScriptedReader reader = new(Script(Printing, 1, 2));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => PressThrough(reader, presses));

            Assert.Equal([A, A, A], presses);
            Assert.Equal("the script box", e.WaitingFor);
        }

        [Theory]
        [InlineData(0x02041075u)]
        [InlineData(0x02041041u)]
        [InlineData(0x020410F1u)]
        public async Task WaitButtonNativesArePressedLikePrinting(uint native)
        {
            ScriptedReader reader = new(Script(native), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A], presses);
        }

        [Fact]
        public async Task NothingIsPressedOutsideThePressNatives()
        {
            ScriptedReader reader = new(
                Script(Movement), Script(WaitStd), Script(TouchMenuHide), Script(TouchMenuShow), Script(null),
                ChildTask(FadeTask), ChildTask(HealAnimationTask), new GameSnapshot(GameScreen.Transitioning, null, null, null, null), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Empty(presses);
        }

        [Fact]
        public async Task MenuAfterTheYesGetsB()
        {
            ScriptedReader reader = new(Script(MenuChoice), Script(Movement), Script(MenuChoice), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([Yes, B], presses);
        }

        // An A pressed at the greeting lands on the menu between reads: the heal animation says Yes was given.
        [Fact]
        public async Task HealAnimationMarksTheYesWhenTheMenuWasMissed()
        {
            ScriptedReader reader = new(
                Script(Printing, 1, 2), Script(Printing), ChildTask(HealAnimationTask), Script(Printing, 1, 2), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, B], presses);
        }

        [Fact]
        public async Task UnchangedScriptThrowsWhenStale()
        {
            ScriptedReader reader = new(Script(Movement));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => PressThrough(reader, []));

            Assert.Equal("the script", e.WaitingFor);
            Assert.Equal(Script(Movement), e.Last);
        }

        // The headbutt trace (research/headbutt.md): the question, touch menu hide, Yes/No, "used Headbutt.", the swap's
        // natives and tasks, then the encounter task and the screen leaving the field.
        private static GameSnapshot[] HeadbuttScript(GameSnapshot end) =>
        [
            .. Box(10014), Script(TouchMenuHide, 10014), Script(MenuChoice, 10014), .. Box(10014),
            Script(0x020492F1, 10014), Script(0x020408D9, 10014), ChildTask(0x0224E0ED), ChildTask(0x022507E9), ChildTask(0x020551B9),
            new GameSnapshot(GameScreen.Transitioning, null, null, null, null), end,
        ];

        private static Task<GameSnapshot> PressThroughHeadbutt(ScriptedReader reader, List<string> presses)
            => NavigatorEvent.PressThroughScriptAsync(reader, Table, macro =>
            {
                presses.Add(macro);
                return Task.CompletedTask;
            }, "the headbutt script", Stale, CancellationToken.None,
            s => s.Screen is not (GameScreen.Transitioning or GameScreen.FieldTask or GameScreen.OtherApp));

        [Fact]
        public async Task HeadbuttLoopEndsOnTheBattleScreen()
        {
            GameSnapshot battle = new(GameScreen.BattleStarting, null, null, null, null);
            ScriptedReader reader = new(HeadbuttScript(battle));

            List<string> presses = [];
            GameSnapshot end = await PressThroughHeadbutt(reader, presses);

            Assert.Equal([A, Yes, B], presses);
            Assert.Equal(battle, end);
        }

        [Fact]
        public async Task HeadbuttLoopEndsOnFieldFreeWithNoBattle()
        {
            ScriptedReader reader = new(HeadbuttScript(Free));

            List<string> presses = [];
            GameSnapshot end = await PressThroughHeadbutt(reader, presses);

            Assert.Equal([A, Yes, B], presses);
            Assert.Equal(Free, end);
        }

        [Fact]
        public async Task YesThatDoesNotLandIsRetriedThenThrows()
        {
            List<string> presses = [];
            ScriptedReader reader = new(Script(MenuChoice));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => PressThrough(reader, presses));

            Assert.Equal([Yes, Yes, Yes], presses);
            Assert.Equal("the script Yes", e.WaitingFor);
        }
    }
}
