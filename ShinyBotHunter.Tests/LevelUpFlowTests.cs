using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;

namespace ShinyBotHunter.Tests
{
    // The prompt presses run in real time against a scripted game; the asserts check the taps, never durations.
    public class LevelUpFlowTests
    {
        // A waiting step (by default, one with a prompt) holds until a tap closes it; any other step is seen once,
        // then the game moves on. swallowed taps are ignored, as when a press lands before the prompt takes input.
        private sealed class ScriptedGame(GameSnapshot[] steps, int swallowed = 0, Func<GameSnapshot, bool>? waits = null) : IGameStateReader
        {
            private readonly Func<GameSnapshot, bool> holds = waits ?? (s => s.Prompt is not null);
            private int index;

            public List<string> Taps { get; } = [];

            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                GameSnapshot current = steps[index];
                if (!holds(current) && index < steps.Length - 1) index++;
                return Task.FromResult(current);
            }

            public Task Tap(string macro)
            {
                Taps.Add(macro);
                if (swallowed > 0) swallowed--;
                else if (holds(steps[index])) index++;
                return Task.CompletedTask;
            }
        }

        private static GameSnapshot Prompt(GameScreen screen, int prompt) => new(screen, null, null, prompt, null);

        private static readonly GameSnapshot Script276 = new(GameScreen.BattleScript, null, 276, null, null);

        private static readonly GameSnapshot Menu = new(GameScreen.BattleMenu, null, null, null, null);

        private static readonly GameSnapshot BattleOver = new(GameScreen.BattleOver, null, null, null, null);

        private static readonly GameSnapshot FieldFree = new(GameScreen.FieldFree, null, null, null, null);

        // Every Evolution snapshot has a Prompt (evo.state); the scene holds only at these, and in the summary app's
        // move list (8) and confirm (9).
        private static bool EvolutionWaits(GameSnapshot s)
            => s.Prompt is 3 or 12 or 15 or 17 or 21 or 26 or 28 or 30 or 35 or 36 or 37 || s.SummaryState is 8 or 9;

        private static GameSnapshot[] Evolution(params int[] states) => [.. states.Select(state => Prompt(GameScreen.Evolution, state)), BattleOver, FieldFree];

        [Fact]
        public async Task StatBoxesThenDeclineFollowThePrompts()
        {
            ScriptedGame game = new(
            [
                Prompt(GameScreen.BattleExpPrompt, 11), Script276, Prompt(GameScreen.BattleExpPrompt, 13), Script276,
                Prompt(GameScreen.BattleMoveLearnPrompt, 21), Script276, Prompt(GameScreen.BattleMoveLearnPrompt, 34), Menu,
            ]);

            await LevelUpFlow.PassStatBoxesAsync(game, game.Tap, CancellationToken.None);
            await LevelUpFlow.DeclineMoveAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["T.A", "T.A", "S.DoNotLearn", "S.DoNotLearnComplete"], game.Taps);
        }

        [Fact]
        public async Task SwallowedTapIsRetried()
        {
            ScriptedGame game = new([Prompt(GameScreen.BattleExpPrompt, 11), Script276, Prompt(GameScreen.BattleExpPrompt, 13), Menu], swallowed: 1);

            await LevelUpFlow.PassStatBoxesAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["T.A", "T.A", "T.A"], game.Taps);
        }

        [Fact]
        public async Task PromptThatNeverClosesThrows()
        {
            ScriptedGame game = new([Prompt(GameScreen.BattleMoveLearnPrompt, 21)], swallowed: int.MaxValue);

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(
                () => LevelUpFlow.DeclineMoveAsync(game, game.Tap, CancellationToken.None));

            Assert.Equal("prompt 21 to close", e.WaitingFor);
            Assert.Equal(["S.DoNotLearn", "S.DoNotLearn", "S.DoNotLearn"], game.Taps);
        }

        [Fact]
        public async Task EvolutionDeclineFollowsTheSceneStates()
        {
            ScriptedGame game = new(Evolution(2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 19, 20, 21, 32, 33, 34, 35, 36, 13, 40), waits: EvolutionWaits);

            await LevelUpFlow.EvolveAsync(game, game.Tap, CancellationToken.None);
            await LevelUpFlow.ReachEvolutionForgetPromptAsync(game, game.Tap, CancellationToken.None);
            await LevelUpFlow.DeclineEvolutionMoveAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["T.A", "T.A", "T.A", "T.A", "S.125.125.5", "T.A", "T.A"], game.Taps);
        }

        // Slot 2 proves the slot-to-row mapping.
        [Fact]
        public async Task EvolutionReplaceFollowsTheSceneAndSummaryStates()
        {
            int[] scene = [2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 19, 20, 21, 22];
            int[] summary = [0, 1, 8, 9];
            ScriptedGame game = new(
            [
                .. scene.Select(state => Prompt(GameScreen.Evolution, state)),
                .. summary.Select(proc => new GameSnapshot(GameScreen.Evolution, null, null, 23, null, null, proc)),
                .. Evolution(24, 26, 27, 28, 29, 30, 31, 37, 38, 13, 40),
            ], waits: EvolutionWaits);

            await LevelUpFlow.EvolveAsync(game, game.Tap, CancellationToken.None);
            await LevelUpFlow.ReachEvolutionForgetPromptAsync(game, game.Tap, CancellationToken.None);
            await LevelUpFlow.ReplaceEvolutionMoveAsync(game, game.Tap, 2, CancellationToken.None);

            Assert.Equal(["T.A", "T.A", "T.A", "T.A", "S.125.75.5", "S.EvoMoveThree.5", "S.64.168.5", "T.A", "T.A", "T.A", "T.A"], game.Taps);
        }

        [Fact]
        public async Task EvolutionWithoutAMoveTapsTwice()
        {
            ScriptedGame game = new(Evolution(2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 39, 40), waits: EvolutionWaits);

            await LevelUpFlow.EvolveAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["T.A", "T.A"], game.Taps);
        }
    }
}
