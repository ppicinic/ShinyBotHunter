using DnsClient;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    public class SpinEncounterEvent : AbstractBotEvent<bool>
    {
        internal const string MovedWhileTurning = "The player moved while turning.";
        internal const string NotAWildBattle = "Something other than a wild battle started.";

        // FR-9; init so a test can shorten it.
        internal TimeSpan NoBattleWithin { get; init; } = TimeSpan.FromSeconds(15);

        // The profile's SeekLockHandOff, set where the orchestrator builds the encounter; init so a test can shorten it.
        internal TimeSpan? LockedHandOff { get; init; }

        private IBot bot;
        Queue<Direction> directions = new Queue<Direction>();
        Dictionary<Direction, string> cachedMacros = new Dictionary<Direction, string>();

        public SpinEncounterEvent(IBot bot)
        {
            this.bot = bot; 
            directions.Enqueue(Direction.Up);
            directions.Enqueue(Direction.Left);
            directions.Enqueue(Direction.Down);
            directions.Enqueue(Direction.Right);
        }

        public override async Task<bool> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // Spinning resumes straight after a battle, when the field is still fading in and the
            // facing byte has not settled. Reading it early picks a rotation that starts from the
            // wrong direction, so wait for the overworld the same way navigation does.
            try
            {
                await Overworld.WaitForReadyAsync(bot.State, cancellationToken);
            }
            catch (GameStateTimeoutException ex)
            {
                // A stale-timeout parks the bot for the operator rather than crashing the event.
                Console.WriteLine($"[Spin] {ex}");
                state.SwapState(EventState.Waiting);
                return false;
            }
            Stopwatch sinceReady = Stopwatch.StartNew();

            // Turning never leaves the tile, so a seek that starts anywhere else stops before anything is pressed.
            (short x, short y) = await bot.Overworld.GetPositionAsync(cancellationToken);
            if (x != state.StartX || y != state.StartY)
            {
                Console.WriteLine($"[Spin] {MovedWhileTurning} At ({x}, {y}), started on ({state.StartX}, {state.StartY}).");
                state.SwapState(EventState.Waiting);
                return false;
            }

            Direction facing = await bot.Overworld.GetFacingAsync(cancellationToken);
            string macro = GetSpinMacro(facing);
            Console.WriteLine($"[Spin] facing={facing} macro={macro}");
            bot.RunRepeatMacro(macro);
            Stopwatch sinceMacro = Stopwatch.StartNew();

            bool battle = true;
            TimeSpan toBattle = TimeSpan.Zero;
            long seekMs = 0;
            try
            {
                toBattle = await SeekAsync(bot.State, NoBattleWithin, LockedHandOff, cancellationToken);
                seekMs = sinceMacro.ElapsedMilliseconds - (long)toBattle.TotalMilliseconds;
            }
            catch (GameStateTimeoutException ex)
            {
                Console.WriteLine($"[Spin] {NotAWildBattle} {ex.Message}");
                battle = false;
            }
            finally
            {
                bot.ClearMacro();
            }
            await bot.WaitForMacroAsync();
            if (!battle)
            {
                state.SwapState(EventState.Waiting);
                return false;
            }

            Console.WriteLine($"[Spin] seek: {seekMs} ms from the macro to the field leaving free, {(long)toBattle.TotalMilliseconds} ms to the battle screen");
            Console.WriteLine($"[Spin] Found battle after {sinceReady.ElapsedMilliseconds} ms");
            // The orchestrator expects the encounter event to advance the state itself, the same
            // way RunEncounterEvent does. Returning the flag alone left Encountering on top of
            // the stack, so the next loop spun again on top of the battle instead of fighting it.
            state.State.Pop();
            // TODO we should go into Waiting if its a non wild battle
            state.State.Push(EventState.BattleStart);
            return true;
        }

        // Waits for a battle while the seek macro repeats. A deadline from the moment the field leaves free, not a stale:
        // an RS field snapshot is the screen alone, so spinning in the free field looks unchanged, and an HGSS lock whose
        // task fields change would keep restarting a stale clock. Seeking in the free field stays unbounded; any other
        // non-battle screen (a field task, a load, an app) has 15 s to become a battle (FR-9, G7). With a lockedHandOff, a
        // field task that lasts that long returns as a battle would, so the battle event reads what locked the field.
        internal static async Task<TimeSpan> SeekAsync(IGameStateReader game, TimeSpan noBattleWithin, TimeSpan? lockedHandOff, CancellationToken ct)
        {
            Stopwatch? sinceLeftFree = null;
            await GameStateWait.UntilAsync(game, "a battle", s =>
            {
                if (s.Screen is not (GameScreen.Transitioning or GameScreen.FieldFree or GameScreen.FieldTask or GameScreen.OtherApp))
                {
                    return true;
                }
                if (s.Screen == GameScreen.FieldFree)
                {
                    sinceLeftFree = null;
                    return false;
                }
                sinceLeftFree ??= Stopwatch.StartNew();
                if (lockedHandOff is not null && s.Screen == GameScreen.FieldTask && sinceLeftFree.Elapsed >= lockedHandOff)
                {
                    return true;
                }
                if (sinceLeftFree.Elapsed >= noBattleWithin)
                {
                    throw new GameStateTimeoutException($"a battle within {noBattleWithin.TotalSeconds} s of the field leaving free", s);
                }
                return false;
            }, stale: null, ct);
            // The time since the last poll that saw the field leave free; zero when no poll saw it before the battle screen.
            return sinceLeftFree?.Elapsed ?? TimeSpan.Zero;
        }

        private string GetSpinMacro(Direction facing)
        {
            if (cachedMacros.ContainsKey(facing))
            {
                return cachedMacros[facing];
            }
            else
            {
                // Align the cycle to the direction we're actually facing. Bounded by the queue length:
                // a facing value outside 0-3 -- a read that caught the field mid-load -- would never
                // match and used to spin here forever, hanging the run with nothing in the log.
                for (int rotations = 0; rotations < directions.Count && facing != directions.Peek(); rotations++)
                {
                    directions.Enqueue(directions.Dequeue());
                }
                if (facing != directions.Peek())
                {
                    Console.WriteLine($"[Spin] Unexpected facing value {facing}; starting the rotation at {directions.Peek()}.");
                }
                directions.Enqueue(directions.Dequeue());
                List<string> turns = new List<string>();

                for (int i = 0; i < 4; i++)
                {
                    Direction dir = directions.Dequeue();
                    turns.Add(MacroRegistry.Instance.GetMacro(bot.GameFlavor, "Spin" + dir));
                    directions.Enqueue(dir);
                }
                string macro = string.Join(";", turns);
                cachedMacros.Add(facing, macro);
                return macro;
            }
        }
    }
}
