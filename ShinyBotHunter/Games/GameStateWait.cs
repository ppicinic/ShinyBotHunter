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
                    Console.WriteLine($"[GameState] {waitingFor}: {next}");
                    unchanged.Restart();
                }
                last = next;
            }
            return last;
        }

        // For a menu the game builds only once the message above it has printed (the battle menu's "What will X do?", the
        // getter's Yes/No questions): waits until the printer is seen at the screen, or seenWithin has passed there (a
        // message that printed between two polls), then until the screen shows no printer. Returns that snapshot.
        public static async Task<GameSnapshot> WaitForPrinterSeenThenClearedAsync(
            IGameStateReader reader, string waitingFor, Func<GameSnapshot, bool> atScreen, TimeSpan seenWithin, TimeSpan stale, CancellationToken ct)
        {
            Stopwatch? sinceAtScreen = null;
            await UntilAsync(reader, $"{waitingFor}'s message to start printing", s =>
            {
                if (!atScreen(s)) return false;
                sinceAtScreen ??= Stopwatch.StartNew();
                return s.TextPrinter is not null || sinceAtScreen.Elapsed >= seenWithin;
            }, stale, ct);
            return await UntilAsync(reader, $"{waitingFor}'s message to print", s => atScreen(s) && s.TextPrinter is null, stale, ct);
        }

        // Macro prefixes that wait before a press, in frames, so frame drops do not shift them as a wait in milliseconds
        // would. The battle UI clears isTouchDisabled 5 frames after it builds a menu, so a press at the first snapshot that
        // shows the menu can land before input is accepted (pointer-discovery.md, "Battle menu input-ready gap").
        //
        // BattleMenuWait: Run, Fight, the move slot, and the getter's Yes/No (21, 34) once its question's printer has
        // cleared. ctx.sel turns 1 before the player's controller builds the menu, and the Yes/No menu is built only after
        // its question prints, so 10 frames, double the gap, also covers the build.
        public const string BattleMenuWait = "W.10;";

        // MenuWait: the gap alone, for the stat boxes, the forget-move screen, the evolution scene's touches and 35, the
        // summary app's touches and the field Yes.
        public const string MenuWait = "W.5;";

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
