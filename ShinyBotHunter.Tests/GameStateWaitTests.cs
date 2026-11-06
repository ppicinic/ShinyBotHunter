using System.Diagnostics;
using ShinyBotHunter.Games;

namespace ShinyBotHunter.Tests
{
    // The waits run in real time with short windows; the asserts check results only, never durations.
    public class GameStateWaitTests
    {
        private sealed class ScriptedReader(Func<GameSnapshot> next) : IGameStateReader
        {
            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(next());
        }

        private static GameSnapshot Screen(GameScreen screen) => new(screen, null, null, null, null);

        private static GameSnapshot Script(int script) => new(GameScreen.BattleScript, null, script, null, null);

        [Fact]
        public async Task UntilReturnsOnMatch()
        {
            Queue<GameSnapshot> snapshots = new([Screen(GameScreen.FieldTask), Screen(GameScreen.FieldTask), Screen(GameScreen.FieldFree)]);
            ScriptedReader reader = new(() => snapshots.Count > 1 ? snapshots.Dequeue() : snapshots.Peek());

            GameSnapshot snapshot = await GameStateWait.UntilAsync(
                reader, "the free field", s => s.Screen == GameScreen.FieldFree, TimeSpan.FromMilliseconds(300), CancellationToken.None);

            Assert.Equal(Screen(GameScreen.FieldFree), snapshot);
        }

        [Fact]
        public async Task UntilThrowsWhenStale()
        {
            ScriptedReader reader = new(() => Screen(GameScreen.BattleTurn));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => GameStateWait.UntilAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.FromMilliseconds(200), CancellationToken.None));

            Assert.Equal(Screen(GameScreen.BattleTurn), e.Last);
            Assert.Equal("the battle menu", e.WaitingFor);
        }

        [Fact]
        public async Task UntilWithoutStaleRunsUntilCancelled()
        {
            ScriptedReader reader = new(() => Screen(GameScreen.FieldFree));
            using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(300));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameStateWait.UntilAsync(
                reader, "a battle", s => s.Screen == GameScreen.BattleStarting, null, cts.Token));
        }

        [Fact]
        public async Task StaleCountsSameScriptAsUnchanged()
        {
            ScriptedReader same = new(() => Script(6));
            await Assert.ThrowsAsync<GameStateTimeoutException>(() => GameStateWait.UntilAsync(
                same, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.FromMilliseconds(200), CancellationToken.None));

            // Twenty polls are well past the stale window, but every one is a different script.
            int script = 0;
            ScriptedReader changing = new(() => Script(script++));
            GameSnapshot snapshot = await GameStateWait.UntilAsync(
                changing, "script 20", s => s.Script == 20, TimeSpan.FromMilliseconds(300), CancellationToken.None);

            Assert.Equal(Script(20), snapshot);
        }

        // The menu's message is not up at the first read, then draws, then is gone; the wait returns at the gone read.
        [Fact]
        public async Task PrinterSeenThenClearedReturnsAfterTheDraw()
        {
            GameSnapshot menu = Screen(GameScreen.BattleMenu);
            Queue<GameSnapshot> snapshots = new([menu, menu with { TextPrinter = 0 }, menu with { TextPrinter = 0 }, menu with { Script = 1 }]);
            ScriptedReader reader = new(() => snapshots.Count > 1 ? snapshots.Dequeue() : snapshots.Peek());

            GameSnapshot snapshot = await GameStateWait.WaitForPrinterSeenThenClearedAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Equal(menu with { Script = 1 }, snapshot);
        }

        // A message that printed between two polls is never seen; the wait falls through once the bound has passed.
        [Fact]
        public async Task PrinterNeverSeenFallsThroughAfterTheBound()
        {
            ScriptedReader reader = new(() => Screen(GameScreen.BattleMenu));

            GameSnapshot snapshot = await GameStateWait.WaitForPrinterSeenThenClearedAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(Screen(GameScreen.BattleMenu), snapshot);
        }

        // A zero window never waits to see a printer: with none up, the first menu snapshot is returned at once.
        [Fact]
        public async Task ZeroWindowWithNoPrinterReturnsTheFirstScreenSnapshot()
        {
            int reads = 0;
            ScriptedReader reader = new(() => { reads++; return Screen(GameScreen.BattleMenu); });

            GameSnapshot snapshot = await GameStateWait.WaitForPrinterSeenThenClearedAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.Zero, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(Screen(GameScreen.BattleMenu), snapshot);
            Assert.Equal(2, reads);
        }

        // A zero window still waits out a printer that is up at the first menu snapshot.
        [Fact]
        public async Task ZeroWindowStillWaitsForAPrinterThatIsUp()
        {
            GameSnapshot menu = Screen(GameScreen.BattleMenu);
            Queue<GameSnapshot> snapshots = new([menu with { TextPrinter = 0 }, menu with { TextPrinter = 0 }, menu with { Script = 1 }]);
            ScriptedReader reader = new(() => snapshots.Count > 1 ? snapshots.Dequeue() : snapshots.Peek());

            GameSnapshot snapshot = await GameStateWait.WaitForPrinterSeenThenClearedAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.Zero, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(menu with { Script = 1 }, snapshot);
        }

        // The caller saw the menu before the window passed; with no printer the wait returns at its first read of each step.
        [Fact]
        public async Task PrinterWindowFromEarlierStartFallsThroughAtOnce()
        {
            int reads = 0;
            ScriptedReader reader = new(() => { reads++; return Screen(GameScreen.BattleMenu); });
            Stopwatch menuSince = Stopwatch.StartNew();
            await Task.Delay(150);

            GameSnapshot snapshot = await GameStateWait.WaitForPrinterSeenThenClearedAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5), CancellationToken.None, menuSince);

            Assert.Equal(Screen(GameScreen.BattleMenu), snapshot);
            Assert.Equal(2, reads);
        }

        // The window has passed, but a printer that is up is still waited out.
        [Fact]
        public async Task PrinterWindowFromEarlierStartStillWaitsForThePrinter()
        {
            GameSnapshot menu = Screen(GameScreen.BattleMenu);
            Queue<GameSnapshot> snapshots = new([menu with { TextPrinter = 0 }, menu with { TextPrinter = 0 }, menu with { Script = 1 }]);
            ScriptedReader reader = new(() => snapshots.Count > 1 ? snapshots.Dequeue() : snapshots.Peek());
            Stopwatch menuSince = Stopwatch.StartNew();
            await Task.Delay(150);

            GameSnapshot snapshot = await GameStateWait.WaitForPrinterSeenThenClearedAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5), CancellationToken.None, menuSince);

            Assert.Equal(menu with { Script = 1 }, snapshot);
        }

        // Without a start the window counts from the first menu snapshot the wait reads, not from its first read.
        [Fact]
        public async Task PrinterWindowWithoutStartCountsFromFirstScreenSnapshot()
        {
            int reads = 0;
            int menuReads = 0;
            ScriptedReader reader = new(() =>
            {
                if (++reads <= 3) return Screen(GameScreen.BattleTurn);
                menuReads++;
                return Screen(GameScreen.BattleMenu);
            });

            GameSnapshot snapshot = await GameStateWait.WaitForPrinterSeenThenClearedAsync(
                reader, "the battle menu", s => s.Screen == GameScreen.BattleMenu, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(Screen(GameScreen.BattleMenu), snapshot);
            Assert.True(menuReads > 2);
        }

        [Fact]
        public async Task PressRetriesThenThrows()
        {
            int presses = 0;
            ScriptedReader reader = new(() => Screen(GameScreen.BattleMenu));

            await Assert.ThrowsAsync<GameStateTimeoutException>(() => GameStateWait.PressUntilAsync(
                reader, () => { presses++; return Task.CompletedTask; }, "the battle to end",
                s => s.Screen == GameScreen.BattleOver, TimeSpan.FromMilliseconds(100), 3, CancellationToken.None));

            Assert.Equal(3, presses);
        }

        [Fact]
        public async Task PressReturnsWhenSecondPressLands()
        {
            int presses = 0;
            ScriptedReader reader = new(() => presses >= 2 ? Screen(GameScreen.BattleOver) : Screen(GameScreen.BattleMenu));

            GameSnapshot snapshot = await GameStateWait.PressUntilAsync(
                reader, () => { presses++; return Task.CompletedTask; }, "the battle to end",
                s => s.Screen == GameScreen.BattleOver, TimeSpan.FromMilliseconds(100), 3, CancellationToken.None);

            Assert.Equal(Screen(GameScreen.BattleOver), snapshot);
            Assert.Equal(2, presses);
        }
    }
}
