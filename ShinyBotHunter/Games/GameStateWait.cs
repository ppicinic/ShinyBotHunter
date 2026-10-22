using System.Diagnostics;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Games
{
    // The bounded waits of docs/specs/hgss-state-reader.md ("Stale-timeouts").
    public static class GameStateWait
    {
        // Returns the first snapshot that matches. Throws when the whole snapshot has not changed for
        // stale; a null stale waits until cancelled.
        public static async Task<GameSnapshot> UntilAsync(
            IGameStateReader reader, string waitingFor, Func<GameSnapshot, bool> until, TimeSpan? stale, CancellationToken ct)
        {
            GameSnapshot last = await reader.ReadAsync(ct);
            Stopwatch unchanged = Stopwatch.StartNew();
            while (!until(last))
            {
                if (stale is not null && unchanged.Elapsed >= stale)
                {
                    throw new GameStateTimeoutException(waitingFor, last);
                }
                await Task.Delay(Constant.PollIntervalMs, ct);
                GameSnapshot next = await reader.ReadAsync(ct);
                if (next != last)
                {
                    unchanged.Restart();
                }
                last = next;
            }
            return last;
        }

        // Presses, then polls up to confirm for the expected snapshot; repeats up to attempts times.
        public static async Task<GameSnapshot> PressUntilAsync(
            IGameStateReader reader, Func<Task> press, string waitingFor, Func<GameSnapshot, bool> expect,
            TimeSpan confirm, int attempts, CancellationToken ct)
        {
            GameSnapshot last = default;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                await press();
                Stopwatch sincePress = Stopwatch.StartNew();
                while (true)
                {
                    last = await reader.ReadAsync(ct);
                    if (expect(last))
                    {
                        return last;
                    }
                    if (sincePress.Elapsed >= confirm)
                    {
                        break;
                    }
                    await Task.Delay(Constant.PollIntervalMs, ct);
                }
            }
            throw new GameStateTimeoutException(waitingFor, last);
        }
    }

    public class GameStateTimeoutException : Exception
    {
        public string WaitingFor { get; }

        public GameSnapshot Last { get; }

        public GameStateTimeoutException(string waitingFor, GameSnapshot last)
            : base($"Waited for {waitingFor}; the game state stayed {last}")
        {
            WaitingFor = waitingFor;
            Last = last;
        }
    }
}
