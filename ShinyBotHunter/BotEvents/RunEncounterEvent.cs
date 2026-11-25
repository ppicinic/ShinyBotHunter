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

        // The Repel message's waits and its A (CloseRepelMessageAsync); init so a test can shorten them.
        internal TimeSpan RepelStale { get; init; } = TimeSpan.FromSeconds(20);
        internal TimeSpan RepelPressConfirm { get; init; } = TimeSpan.FromSeconds(1);
        private const int RepelPressAttempts = 3;
        private const string PressA = "T.A;W.20";

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
            // On HGSS the seek stops at "Repel's effect wore off..." (FR-29), which waits for A with no battle to follow it.
            HgssPointerService? hgss = addresses as HgssPointerService;
            Func<GameSnapshot, bool>? repelMessage = hgss is null ? null : s => IsRepelMessage(s, hgss);
            while (true)
            {
                bot.RunRepeatMacro(state.EncounterMacros["Run"]);
                Stopwatch sinceMacro = Stopwatch.StartNew();
                bool battle = true;
                TimeSpan toBattle = TimeSpan.Zero;
                GameSnapshot at = default;
                long seekMs = 0;
                try
                {
                    (toBattle, at) = await SpinEncounterEvent.SeekAsync(bot.State, NoBattleWithin, LockedHandOff, cancellationToken, repelMessage);
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
                if (repelMessage?.Invoke(at) == true)
                {
                    try
                    {
                        await CloseRepelMessageAsync(bot, hgss!, RepelStale, RepelPressConfirm, cancellationToken);
                    }
                    catch (GameStateTimeoutException ex)
                    {
                        Console.WriteLine($"[Run] The Repel message did not close. {ex.Message}");
                        state.SwapState(EventState.Waiting);
                        return state;
                    }
                    // RunMacro's coordinates are absolute, so the line resumes from wherever the message stopped it.
                    continue;
                }
                Console.WriteLine($"[Run] seek: {seekMs} ms from the macro to the field leaving free, {(long)toBattle.TotalMilliseconds} ms to the battle screen");
                state.State.Pop();
                // TODO we should go into Waiting if its a non wild battle
                state.State.Push(EventState.BattleStart);
                return state;
            }
        }

        // std_repel_wore_off running, printing or waiting for A.
        internal static bool IsRepelMessage(GameSnapshot s, HgssPointerService hgss)
            => s.Screen == GameScreen.FieldTask && s.Script == hgss.RepelMessageScript;

        // Rule 16, from docs/research/milestone-builder.md R5: the message waits in WaitABPressNative in context 0 with no
        // printer, and one A takes ctx.mode 2 to 1, so the snapshot leaves that native. The A is sent only there; a snapshot
        // still waiting there after pressConfirm shows the press was dropped, and only then is it sent again. Every wait
        // throws, naming the snapshot it saw, when that snapshot stays unchanged for stale.
        internal static async Task CloseRepelMessageAsync(IBot bot, HgssPointerService hgss, TimeSpan stale, TimeSpan pressConfirm, CancellationToken ct)
        {
            IGameStateReader game = bot.State;
            Func<GameSnapshot, bool> waitingForA = s => IsRepelMessage(s, hgss) && s.Native == hgss.WaitABPressNative;
            await GameStateWait.UntilAsync(game, "the Repel message to wait for A", waitingForA, stale, ct);
            int presses = 0;
            GameSnapshot taken = await GameStateWait.PressUntilAsync(game, async () =>
            {
                presses++;
                bot.RunMacroString(PressA);
                await bot.WaitForMacroAsync(ct);
            }, "the Repel message to take the A", s => !waitingForA(s), pressConfirm, RepelPressAttempts, ct);
            await GameStateWait.UntilAsync(game, "the field free after the Repel message", s => s.Screen == GameScreen.FieldFree, stale, ct);
            Console.WriteLine($"[Repel] Repel's effect wore off: closed with {presses} A, confirmed by {taken}");
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
