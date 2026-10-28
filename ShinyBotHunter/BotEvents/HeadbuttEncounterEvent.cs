using ShinyBotHunter.Bot;
using ShinyBotHunter.Games;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    // One headbutt per call (docs/specs/headbutt-encounter.md): back to the start tile facing the tree, A, check that
    // the tree's script started, press through it until a battle or the field is free again. Holds no state between
    // cycles; the return macros are cached on GameState.EncounterMacros. Every stop parks in Waiting with
    // the reason logged, as SpinEncounterEvent parks on a stale-timeout.
    public class HeadbuttEncounterEvent : AbstractBotEvent<bool>
    {
        // FR-6: a script has to start within 3 s of the A.
        private static readonly TimeSpan PressConfirm = TimeSpan.FromSeconds(1);
        private const int StartAttempts = 3;
        private const int CloseAttempts = 20;
        // As the heal's press loop.
        private static readonly TimeSpan ScriptStale = TimeSpan.FromSeconds(20);
        // The return macro is one step and a turn, well under a second. A step into something that blocks it holds the
        // direction forever, so the macro is cleared once nothing has changed for this long.
        private static readonly TimeSpan ReturnStale = TimeSpan.FromSeconds(3);

        private const string PressA = "T.A;W.20";
        private const string PressB = "T.B;W.20";

        private const string NothingInFront = "Nothing in front of you starts a script; stand facing a headbutt tree.";
        private const string NoPokemon = "The tree in front of you gave no Pokémon. It may give none for your trainer ID, or a Repel is active.";

        private readonly IBot bot;
        private readonly HgssPointerService addresses;

        public HeadbuttEncounterEvent(IBot bot)
        {
            this.bot = bot;
            this.addresses = (HgssPointerService)bot.Addresses;
        }

        public override async Task<bool> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            Func<string, Task> run = async macro =>
            {
                bot.RunMacroString(macro);
                await bot.WaitForMacroAsync(cancellationToken);
            };
            string? park = await RunCycleAsync(state, bot.State, bot.Overworld, addresses, run, bot.ClearMacro, cancellationToken);
            return park is null;
        }

        // Ends the script loop once the screen has left the field for a battle, or the field is free with no battle.
        internal static bool LeftTheScript(GameSnapshot s) => s.Screen is not (GameScreen.Transitioning or GameScreen.FieldTask or GameScreen.OtherApp);

        // One cycle. Returns null when a battle started (Encountering swapped for BattleStart), otherwise the reason
        // the event parked in Waiting.
        internal static async Task<string?> RunCycleAsync(
            GameState state, IGameStateReader game, IOverworldReader overworld, HgssPointerService addresses,
            Func<string, Task> run, Action clear, CancellationToken ct)
        {
            try
            {
                (string returned, string? stop) = await ReturnToTreeAsync(state, game, overworld, addresses, run, clear, ct);
                if (stop is not null)
                {
                    return Park(state, stop);
                }

                int presses = 0;
                Func<string, Task> counted = macro =>
                {
                    presses++;
                    return run(macro);
                };

                GameSnapshot started;
                try
                {
                    started = await GameStateWait.PressUntilAsync(game, () => counted(PressA), "the headbutt script to start",
                        s => s.Screen == GameScreen.FieldTask && s.TaskFunction == addresses.RunScriptsTask, PressConfirm, StartAttempts, ct);
                }
                catch (GameStateTimeoutException)
                {
                    return Park(state, NothingInFront);
                }
                if (started.Script != addresses.HeadbuttScript)
                {
                    await GameStateWait.PressUntilAsync(game, () => counted(PressB), "the other script to close",
                        s => s.Screen == GameScreen.FieldFree, PressConfirm, CloseAttempts, ct);
                    return Park(state, $"The thing in front of you is not a headbutt tree (script {started.Script}).");
                }

                GameSnapshot end = await NavigatorEvent.PressThroughScriptAsync(game, addresses, counted, "the headbutt script", ScriptStale, ct, LeftTheScript);
                bool battle = end.Screen != GameScreen.FieldFree;
                Console.WriteLine($"[Headbutt] start=script {started.Script} presses={presses} end={(battle ? "battle" : "nothing")} return={returned}");
                if (battle)
                {
                    state.State.Pop();
                    state.State.Push(EventState.BattleStart);
                    return null;
                }

                // The swap happens on a tree with no Pokémon too, so the player is put back before the park.
                (_, string? stopAfter) = await ReturnToTreeAsync(state, game, overworld, addresses, run, clear, ct);
                return Park(state, stopAfter ?? NoPokemon);
            }
            catch (GameStateTimeoutException ex)
            {
                return Park(state, ex.Message);
            }
        }

        // Puts the player back on the start tile facing the tree (FR-9). Result is none, cached, built or rebuilt;
        // Stop is set, with the position, when the player is not one step from the start or two macros missed.
        internal static async Task<(string Result, string? Stop)> ReturnToTreeAsync(
            GameState state, IGameStateReader game, IOverworldReader overworld, HgssPointerService addresses,
            Func<string, Task> run, Action clear, CancellationToken ct)
        {
            await Overworld.WaitForReadyAsync(game, ct);
            (int x, int y, Direction facing) = await ReadPositionAsync(overworld, ct);
            if (AtStart(state, x, y, facing))
            {
                return ("none", null);
            }
            // A swap only lands on a tile beside the start (research/headbutt.md, "The swap").
            if (Math.Abs(x - state.StartX) + Math.Abs(y - state.StartY) > 1)
            {
                return ("", $"The player is at ({x}, {y}) facing {facing}, more than one step from the start tile ({state.StartX}, {state.StartY}); that is not a swap.");
            }

            string key = Key(x, y, facing);
            string result = "cached";
            if (!state.EncounterMacros.TryGetValue(key, out string? macro))
            {
                macro = BuildReturn(state, addresses, x, y);
                state.EncounterMacros[key] = macro;
                result = "built";
            }
            await RunReturnMacroAsync(game, run, clear, macro, ct);
            await Overworld.WaitForReadyAsync(game, ct);
            (x, y, facing) = await ReadPositionAsync(overworld, ct);
            if (AtStart(state, x, y, facing))
            {
                return (result, null);
            }

            Console.WriteLine($"[Headbutt] Cache miss at ({x}, {y}, {facing}); rebuilt");
            macro = BuildReturn(state, addresses, x, y);
            state.EncounterMacros[Key(x, y, facing)] = macro;
            await RunReturnMacroAsync(game, run, clear, macro, ct);
            await Overworld.WaitForReadyAsync(game, ct);
            (x, y, facing) = await ReadPositionAsync(overworld, ct);
            if (AtStart(state, x, y, facing))
            {
                return ("rebuilt", null);
            }
            return ("rebuilt", $"The return to the tree missed twice; the player is at ({x}, {y}) facing {facing}, not ({state.StartX}, {state.StartY}) facing {state.StartFacing}.");
        }

        // Races the macro against a stale watch, as NavigatorEvent.ExecuteSegmentMacroAsync races its walk: on the stale
        // the macro is cleared and waited out, and the caller's position check counts it as a miss.
        private static async Task RunReturnMacroAsync(IGameStateReader game, Func<string, Task> run, Action clear, string macro, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task macroTask = run(macro);
            Task watch = GameStateWait.UntilAsync(game, "the return macro to end", _ => false, ReturnStale, cts.Token);
            Task first = await Task.WhenAny(macroTask, watch);
            ct.ThrowIfCancellationRequested();
            cts.Cancel();
            if (ReferenceEquals(first, watch))
            {
                clear();
                await macroTask;
                try
                {
                    await watch;
                }
                catch (GameStateTimeoutException ex)
                {
                    Console.WriteLine($"[Headbutt] {ex.Message}; cleared the return macro");
                }
                return;
            }
            await macroTask;
        }

        private static async Task<(int X, int Y, Direction Facing)> ReadPositionAsync(IOverworldReader overworld, CancellationToken ct)
        {
            (short x, short y) = await overworld.GetPositionAsync(ct);
            return (x, y, await overworld.GetFacingAsync(ct));
        }

        private static bool AtStart(GameState state, int x, int y, Direction facing)
            => x == state.StartX && y == state.StartY && facing == state.StartFacing;

        private static string Key(int x, int y, Direction facing) => $"Headbutt:{x},{y},{facing}";

        // One step per axis that differs, then a press toward the tree, which the tree blocks so it only turns.
        private static string BuildReturn(GameState state, HgssPointerService addresses, int x, int y)
        {
            List<string> steps = [];
            if (x != state.StartX)
            {
                steps.Add($"M.{(state.StartX > x ? "Right" : "Left")}.{state.StartX}.{addresses.TrainerX}");
            }
            if (y != state.StartY)
            {
                steps.Add($"M.{(state.StartY > y ? "Down" : "Up")}.{state.StartY}.{addresses.TrainerY}");
            }
            steps.Add($"W.8;H.{state.StartFacing}.3;W.10");
            return string.Join(";", steps);
        }

        private static string Park(GameState state, string reason)
        {
            Console.WriteLine($"[Headbutt] {reason}");
            state.SwapState(EventState.Waiting);
            return reason;
        }
    }
}
