using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service.Pointers;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    public class RunEncounterEvent : AbstractBotEvent<GameState>
    {
        internal const string OffTheRunLine = "The player is off the run line.";

        // FR-9; init so a test can shorten it.
        internal TimeSpan NoBattleWithin { get; init; } = TimeSpan.FromSeconds(15);

        // The profile's SeekLockHandOff, set where the orchestrator builds the encounter (SpinEncounterEvent.SeekAsync).
        internal TimeSpan? LockedHandOff { get; init; }

        private EncounterParams param;
        private IBot bot;
        private readonly IPointerService addresses;
        private string? macro = null;

        public RunEncounterEvent(IBot bot, EncounterParams param)
        {
            this.bot = bot;
            this.addresses = bot.Addresses;
            this.param = param;
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // M holds its direction until the coordinate equals the end of the line, so from outside the line it holds for ever.
            (short X, short Y) tile = await bot.Overworld.GetPositionAsync(cancellationToken);
            if (OffTheLine(param, state.StartX, state.StartY, tile) is { } stop)
            {
                (string axis, int range) = param.yRange > 0 ? ("y", param.yRange) : ("x", param.xRange);
                Console.WriteLine($"[Run] {stop} At ({tile.X}, {tile.Y}); the line is {axis} +-{range} from ({state.StartX}, {state.StartY}).");
                state.SwapState(EventState.Waiting);
                return state;
            }
            if (!state.EncounterMacros.ContainsKey("Run"))
            {
                state.EncounterMacros.Add("Run", RunMacro(addresses, bot.GameFlavor, param, state.StartX, state.StartY));
            }

            Console.WriteLine($"[Run] from ({tile.X}, {tile.Y}) macro={state.EncounterMacros["Run"]}");
            bot.RunRepeatMacro(state.EncounterMacros["Run"]);
            Stopwatch sinceMacro = Stopwatch.StartNew();
            bool battle = true;
            TimeSpan toBattle = TimeSpan.Zero;
            long seekMs = 0;
            try
            {
                (toBattle, _) = await SpinEncounterEvent.SeekAsync(bot.State, NoBattleWithin, LockedHandOff, cancellationToken);
                seekMs = sinceMacro.ElapsedMilliseconds - (long)toBattle.TotalMilliseconds;
            }
            catch (GameStateTimeoutException ex)
            {
                Console.WriteLine($"[Run] {SpinEncounterEvent.NotAWildBattle} {ex.Message}");
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
                return state;
            }
            Console.WriteLine($"[Run] seek: {seekMs} ms from the macro to the field leaving free, {(long)toBattle.TotalMilliseconds} ms to the battle screen");
            state.State.Pop();
            // TODO we should go into Waiting if its a non wild battle
            state.State.Push(EventState.BattleStart);
            return state;
        }

        // Up then Down, or Right then Left, on both games. RunHold (RS) is joined to the first move with ":" so the move
        // is checked on the frame it starts; as a step of its own it overshoots (docs/research/rs-wild-encounter.md,
        // "The Run macro's first frame").
        internal static string RunMacro(IPointerService addresses, GameFlavor flavor, EncounterParams param, int x, int y)
        {
            string hold = MacroRegistry.Instance.TryGetMacro(flavor, "RunHold", out string? runHold) ? ":" + runHold : "";
            if (param.yRange > 0)
            {
                return $"M.Up.{y - param.yRange}.{addresses.RunWatchY}{hold};M.Down.{y + param.yRange}.{addresses.RunWatchY}";
            }
            return $"M.Right.{x + param.xRange}.{addresses.RunWatchX}{hold};M.Left.{x - param.xRange}.{addresses.RunWatchX}";
        }

        internal static string? OffTheLine(EncounterParams param, int startX, int startY, (short X, short Y) tile)
        {
            bool onLine = param.yRange > 0
                ? tile.X == startX && Math.Abs(tile.Y - startY) <= param.yRange
                : tile.Y == startY && Math.Abs(tile.X - startX) <= param.xRange;
            return onLine ? null : OffTheRunLine;
        }
    }
}
