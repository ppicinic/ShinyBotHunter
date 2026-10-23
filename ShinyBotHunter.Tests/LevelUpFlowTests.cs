using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;

namespace ShinyBotHunter.Tests
{
    // The prompt presses run in real time against a scripted game; the asserts check the taps, never durations.
    public class LevelUpFlowTests
    {
        // A prompt step holds until a tap closes it; any other step is seen once, then the game moves on.
        // swallowed taps are ignored, as when a press lands before the prompt takes input.
        private sealed class ScriptedGame(GameSnapshot[] steps, int swallowed = 0) : IGameStateReader
        {
            private int index;

            public List<string> Taps { get; } = [];

            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                GameSnapshot current = steps[index];
                if (current.Prompt is null && index < steps.Length - 1) index++;
                return Task.FromResult(current);
            }

            public Task Tap(string macro)
            {
                Taps.Add(macro);
                if (swallowed > 0) swallowed--;
                else if (steps[index].Prompt is not null) index++;
                return Task.CompletedTask;
            }
        }

        private static GameSnapshot Prompt(GameScreen screen, int prompt) => new(screen, null, null, prompt, null);

        private static readonly GameSnapshot Script276 = new(GameScreen.BattleScript, null, 276, null, null);

        private static readonly GameSnapshot Menu = new(GameScreen.BattleMenu, null, null, null, null);

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
    }
}
