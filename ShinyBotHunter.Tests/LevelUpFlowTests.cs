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

        // Every Evolution snapshot has a Prompt (evo.state); the scene holds only at a printer's arrow, at the Yes/No states
        // (21, 35), and in the summary app's move list (8) and confirm (9).
        private static bool EvolutionWaits(GameSnapshot s)
            => s.AtTextArrow() || s.Prompt is 21 or 35 || s.SummaryState is 8 or 9;

        private static readonly int[] MessageStates = [3, 12, 15, 17, 26, 28, 30, 36, 37];

        private static GameSnapshot Printing(int state, int? printer) => new(GameScreen.Evolution, null, null, state, null, TextPrinter: printer);

        // A message state is drawing, then at the arrow, then its slot NULL while the countdown runs.
        private static IEnumerable<GameSnapshot> Scene(int state)
            => MessageStates.Contains(state) ? [Printing(state, 0), Printing(state, 2), Printing(state, null)] : [Prompt(GameScreen.Evolution, state)];

        private static GameSnapshot[] Evolution(params int[] states) => [.. states.SelectMany(Scene), BattleOver, FieldFree];

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

        // The Yes/No menus are built once their question has printed; the drawing reads at 21 and 34 get no tap.
        [Fact]
        public async Task NoTapWhileTheQuestionDraws()
        {
            GameSnapshot Asking(int prompt) => Prompt(GameScreen.BattleMoveLearnPrompt, prompt) with { TextPrinter = 0 };
            ScriptedGame game = new(
            [
                Asking(21), Asking(21), Prompt(GameScreen.BattleMoveLearnPrompt, 21), Script276,
                Asking(34), Asking(34), Asking(34), Prompt(GameScreen.BattleMoveLearnPrompt, 34), Menu,
            ], waits: s => s.Prompt is not null && s.TextPrinter is null);

            await LevelUpFlow.DeclineMoveAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["S.DoNotLearn", "S.DoNotLearnComplete"], game.Taps);
        }

        // The first read at 34 comes before the question's printer exists; the tap waits for the printer to appear and clear.
        // Only the marked read after the draw takes the tap (Script is a marker the flow ignores).
        [Fact]
        public async Task NoTapBeforeTheQuestionStartsPrinting()
        {
            GameSnapshot Ready(int prompt) => Prompt(GameScreen.BattleMoveLearnPrompt, prompt) with { Script = 1 };
            GameSnapshot Asking = Prompt(GameScreen.BattleMoveLearnPrompt, 34) with { TextPrinter = 0 };
            ScriptedGame game = new(
            [
                Ready(21), Script276,
                Prompt(GameScreen.BattleMoveLearnPrompt, 34), Asking, Asking, Ready(34), Menu,
            ], waits: s => s.Script == 1);

            await LevelUpFlow.DeclineMoveAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["S.DoNotLearn", "S.DoNotLearnComplete"], game.Taps);
        }

        // Slot 2 proves the slot-to-button mapping.
        [Fact]
        public async Task ReplaceTouchesYesThenTheSlotAndForget()
        {
            ScriptedGame game = new(
            [
                Prompt(GameScreen.BattleMoveLearnPrompt, 21), Script276, Prompt(GameScreen.BattleMoveLearnPrompt, 23), Script276, BattleOver, FieldFree,
            ]);

            await LevelUpFlow.ReplaceMoveAsync(game, game.Tap, 2, CancellationToken.None);

            Assert.Equal(["S.NewMove", "S.NewMoveThree;W.20;S.32.172.5"], game.Taps);
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
                .. scene.SelectMany(Scene),
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

        // The drawing reads at 12 (one of them a state-6 pause) get no tap; the one tap at 12 lands at the arrow.
        [Fact]
        public async Task NoTapWhileTheEvolutionTextDraws()
        {
            ScriptedGame game = new(
            [
                .. Scene(3), Prompt(GameScreen.Evolution, 5),
                Printing(12, 0), Printing(12, 6), Printing(12, 0), Printing(12, 0), Printing(12, 2), Printing(12, null),
                Prompt(GameScreen.Evolution, 13), Prompt(GameScreen.Evolution, 39), BattleOver, FieldFree,
            ], waits: EvolutionWaits);

            await LevelUpFlow.EvolveAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["T.A", "T.A"], game.Taps);
        }

        // evo.printer is one slot for the whole scene; outside slot 0 every message state reads no printer and holds
        // until a tap, as before the printer read. The swallowed tap at 3 is retried.
        [Fact]
        public async Task EvolutionPrinterOutsideSlotZeroFallsBackToTheRetry()
        {
            int[] states = [2, 3, 5, 12, 13, 39, 40];
            ScriptedGame game = new([.. states.Select(state => Prompt(GameScreen.Evolution, state)), BattleOver, FieldFree],
                swallowed: 1, waits: s => s.Prompt is 3 or 12);

            await LevelUpFlow.EvolveAsync(game, game.Tap, CancellationToken.None);

            Assert.Equal(["T.A", "T.A", "T.A"], game.Taps);
        }
    }
}
