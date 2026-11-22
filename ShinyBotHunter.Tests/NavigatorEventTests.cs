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

        private static GameSnapshot Script(uint? native, int script = 1, int? textPrinter = null, uint? textCursor = null)
            => new(GameScreen.FieldTask, null, script, null, RunScripts, native, TextPrinter: textPrinter, TextCursor: textCursor);

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

        // The #374 watcher: the scroll arrow pressed, and the next box already at its own arrow at the first read after.
        [Fact]
        public async Task NextBoxAtItsArrowConfirmsThePressAtOnce()
        {
            ScriptedReader reader = new(
                Script(Printing, 1, 3, 0x022A7966), Script(Printing, 1, 2, 0x022A798E), Script(Printing, 1, 2, 0x022A798E), Script(Printing), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, A], presses);
        }

        [Fact]
        public async Task ArrowThatDoesNotClearIsRetriedThenThrows()
        {
            List<string> presses = [];
            ScriptedReader reader = new(Script(Printing, 1, 2, 0x022A7966));

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
            }, "the headbutt script", Stale, CancellationToken.None, HeadbuttEncounterEvent.LeftTheScript);

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

        // The sign natives (research/hgss-signs.md).
        private const uint SignWait = 0x020415E1;
        private const uint SignPrinting = 0x02041521;
        private const uint SlideIn = 0x02041455;

        public static TheoryData<GameSnapshot, bool> SignSnapshots => new()
        {
            { Script(SignWait, 9), true },
            { Script(SignPrinting, 10), true },
            { Script(SlideIn, 9), false },
            { Script(null, 9), false },
            { Script(Printing, 2003, 2), false },
            { Script(Printing, 1, 2), false },
            { Script(MenuChoice), false },
            { Script(0x02041075), false },
            { new GameSnapshot(GameScreen.FieldTask, null, 9, null, FadeTask, SignWait), false },
            { new GameSnapshot(GameScreen.Transitioning, null, null, null, null), false },
            { Free, false },
        };

        [Theory]
        [MemberData(nameof(SignSnapshots))]
        public void IsSignOnlyAtTheTwoSignNatives(GameSnapshot snapshot, bool sign)
        {
            Assert.Equal(sign, NavigatorEvent.IsSign(snapshot, Table));
        }

        [Fact]
        public async Task HeldWatchEndsOnASign()
        {
            ScriptedReader reader = new(Script(null, 9), Script(SlideIn, 9), Script(SlideIn, 9), Script(null, 9), Script(SignWait, 9));

            GameSnapshot end = await NavigatorEvent.WaitForFieldFreeOrSignAsync(reader, Table, CancellationToken.None);

            Assert.Equal(Script(SignWait, 9), end);
        }

        // The door trace: no script, never the script task.
        [Fact]
        public async Task HeldWatchEndsOnTheFreeFieldThroughAWarp()
        {
            ScriptedReader reader = new(
                ChildTask(FadeTask), new GameSnapshot(GameScreen.Transitioning, null, null, null, null), ChildTask(FadeTask), Free);

            GameSnapshot end = await NavigatorEvent.WaitForFieldFreeOrSignAsync(reader, Table, CancellationToken.None);

            Assert.Equal(Free, end);
        }

        private static Task CloseSign(ScriptedReader reader, List<string> presses)
            => NavigatorEvent.CloseSignAsync(reader, Table, macro =>
            {
                presses.Add(macro);
                return Task.CompletedTask;
            }, Stale, CancellationToken.None);

        [Fact]
        public async Task BoardIsClosedWithOneBAtTheWaitNative()
        {
            ScriptedReader reader = new(
                Script(null), Script(SlideIn), Script(SlideIn), Script(null), Script(SignWait), Script(null), Script(WaitStd), Script(null), Free);

            List<string> presses = [];
            await CloseSign(reader, presses);

            Assert.Equal([B], presses);
        }

        [Fact]
        public async Task TextSignGetsOneBAtItsArrowThenOneAtTheWait()
        {
            ScriptedReader reader = new(
                Script(SlideIn), Script(SignPrinting, 10, 0), Script(SignPrinting, 10, 0), Script(SignPrinting, 10, 2, 0x022A7966),
                Script(SignPrinting, 10), Script(SignWait, 10), Script(null, 10), Free);

            List<string> presses = [];
            await CloseSign(reader, presses);

            Assert.Equal([B, B], presses);
        }

        public static TheoryData<GameSnapshot> NoKeyStates => new()
        {
            Script(SlideIn),
            Script(null),
            Script(SignPrinting, 10, 0),
        };

        [Theory]
        [MemberData(nameof(NoKeyStates))]
        public async Task NothingIsPressedWithoutAKeyState(GameSnapshot held)
        {
            List<string> presses = [];
            ScriptedReader reader = new(held);

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => CloseSign(reader, presses));

            Assert.Empty(presses);
            Assert.Equal("the sign to take a key", e.WaitingFor);
            Assert.Equal(held, e.Last);
        }

        [Fact]
        public async Task BThatDoesNotCloseTheBoardIsRetriedThenThrows()
        {
            List<string> presses = [];
            ScriptedReader reader = new(Script(SignWait));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => CloseSign(reader, presses));

            Assert.Equal([B, B, B], presses);
            Assert.Equal("the sign close", e.WaitingFor);
            Assert.Equal(Script(SignWait), e.Last);
        }

        [Fact]
        public void StuckTilePenaltyAddsToTheTileItIsGiven()
        {
            GameState state = new();

            int first = NavigatorEvent.AddStuckTilePenalty(state, 73, 488, 264);
            int second = NavigatorEvent.AddStuckTilePenalty(state, 73, 488, 264);

            Assert.Equal(25, first);
            Assert.Equal(50, second);
            Assert.Equal([73], state.TilePenalties.Keys);
            Assert.Equal([(488, 264)], state.TilePenalties[73].Keys);
        }
    }
}
