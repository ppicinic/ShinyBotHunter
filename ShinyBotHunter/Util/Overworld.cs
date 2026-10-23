using ShinyBotHunter.Games;

namespace ShinyBotHunter.Util
{
    public static class Overworld
    {
        // The overworld is ready when the field is free: no app, no task, map up (R9).
        public static Task<GameSnapshot> WaitForReadyAsync(IGameStateReader game, CancellationToken ct)
            => GameStateWait.UntilAsync(game, "the overworld ready", s => s.Screen == GameScreen.FieldFree, TimeSpan.FromSeconds(10), ct);
    }
}
