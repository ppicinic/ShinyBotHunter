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
