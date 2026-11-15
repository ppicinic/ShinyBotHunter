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

            try
            {
                await GameStateWait.UntilAsync(bot.State, "a battle",
                    s => s.Screen is not (GameScreen.Transitioning or GameScreen.FieldFree or GameScreen.FieldTask or GameScreen.OtherApp),
                    stale: null, cancellationToken);
            }
            finally
            {
                bot.ClearMacro();
            }
            await bot.WaitForMacroAsync();

            Console.WriteLine($"[Spin] Found battle after {sinceReady.ElapsedMilliseconds} ms");
            // The orchestrator expects the encounter event to advance the state itself, the same
            // way RunEncounterEvent does. Returning the flag alone left Encountering on top of
            // the stack, so the next loop spun again on top of the battle instead of fighting it.
            state.State.Pop();
            // TODO we should go into Waiting if its a non wild battle
            state.State.Push(EventState.BattleStart);
            return true;
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
