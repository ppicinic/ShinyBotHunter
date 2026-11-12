using ShinyBotHunter.Bot;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    // The static encounter hunt (docs/specs/sinjoh-static.md, docs/research/sinjoh-static.md): save once at Run, then each
    // attempt replays the walk Run made to the circle. A step that goes wrong stops the hunt with its sentence.
    public class HgssStaticEncounterEvent : AbstractBotEvent<bool>
    {
        internal const string DidNotRespond = "The circle did not respond. This save has already received this Pokémon, or this Arceus has been used.";

        // How long the snapshot may stay the same during the walk: it stays FieldFree throughout, which takes 5.4 to 6.9 s.
        private static readonly TimeSpan WalkStale = TimeSpan.FromSeconds(15);

        // Safety margin after a save so the flush lands before anything else happens; the Primo hunt's value.
        private static readonly TimeSpan PostSaveDelay = TimeSpan.FromSeconds(10);

        private const string SummaryNotBuilt = "The summary step is not built yet (#356).";

        private readonly IBot bot;
        private readonly StaticEncounterConfig config;
        private readonly StaticEncounterConfig.Entry encounter;
        private readonly string walkMacro;
        private readonly (short X, short Y, Direction Facing) start;
        private readonly BotService botService;

        public HgssStaticEncounterEvent(IBot bot, StaticEncounterConfig config, StaticEncounterConfig.Entry encounter, string walkMacro,
            short x, short y, Direction facing, BotService botService)
        {
            this.bot = bot;
            this.config = config;
            this.encounter = encounter;
            this.walkMacro = walkMacro;
            this.start = (x, y, facing);
            this.botService = botService;
        }

        public override async Task<bool> ExecuteWithResultAsync(GameState gameState, CancellationToken cancellationToken)
        {
            PushRunningState(gameState);
            await botService.RefreshDashboardState();

            bool complete = false;
            try
            {
                complete = await HuntAsync(gameState, bot, config, encounter, walkMacro, start, PostSaveDelay, cancellationToken) is null;
                return complete;
            }
            finally
            {
                if (!complete)
                {
                    // Stopped, cancelled or thrown out of: nothing is driving the bot any more, so nothing stays held.
                    bot.ClearMacro();
                }
            }
        }

        // The dashboard decides which actions a slot offers from the top of this stack.
        private static void PushRunningState(GameState gameState)
        {
            if (gameState.State.Count == 0)
            {
                gameState.State.Push(EventState.Running);
            }
            else
            {
                gameState.SwapState(EventState.Running);
            }
        }

        // Clears the macro, logs the sentence and leaves the slot idle, so Run is offered again and checks again. Not Waiting:
        // Resume would run the save at Run again from wherever the stop left the game.
        private static string Stop(GameState state, IBot bot, string sentence)
        {
            bot.ClearMacro();
            Console.WriteLine($"[Static] stopped: {sentence}");
            state.SwapState(EventState.None);
            return sentence;
        }

        // -- Steps, internal for the tests --------------------------------------------------------

        // The run: the save at Run, then the attempts. Null when the hunt completed, otherwise the sentence it stopped with.
        internal static async Task<string?> HuntAsync(GameState state, IBot bot, StaticEncounterConfig config, StaticEncounterConfig.Entry encounter,
            string walkMacro, (short X, short Y, Direction Facing) start, TimeSpan postSave, CancellationToken ct)
        {
            // The one place the macro is printed (FR-12).
            Console.WriteLine($"[Static] {encounter.Name}: species {encounter.Species}, circle ({encounter.Spot[0]}, {encounter.Spot[1]}) on map {encounter.MapId}; " +
                $"walk from ({start.X}, {start.Y}) facing {start.Facing.ToString().ToLowerInvariant()}: {walkMacro}");
            try
            {
                // Once, after Run's checks, so every reset returns to the checked game (FR-3).
                bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, "SaveGame"));
                await bot.WaitForMacroAsync(ct);
                await bot.FlushSave(ct);
                await Task.Delay(postSave, ct);
                await Overworld.WaitForReadyAsync(bot.State, ct);
                Console.WriteLine("[Static] saved at Run");

                int attempt = 0;
                while (true)
                {
                    attempt++;
                    Console.WriteLine($"[Static] attempt {attempt}");
                    if (await WalkAsync(bot, encounter, walkMacro, WalkStale, ct) is { } walkStop)
                    {
                        return Stop(state, bot, walkStop);
                    }
                    return Stop(state, bot, SummaryNotBuilt);
                }
            }
            catch (GameStateTimeoutException ex)
            {
                // Its message already names what was awaited and the state that stayed.
                return Stop(state, bot, ex.Message);
            }
        }

        // Replays Run's walk, raced against the circle's script starting, which is the arrival (FR-5). The gate is the free
        // field, seen after the save or the reset. The event never searches: the macro is the parameter (FR-4).
        internal static async Task<string?> WalkAsync(IBot bot, StaticEncounterConfig.Entry encounter, string walkMacro, TimeSpan stale, CancellationToken ct)
        {
            Stopwatch walk = Stopwatch.StartNew();
            bot.RunMacroString(walkMacro);
            GameSnapshot started;
            try
            {
                started = await GameStateWait.UntilAsync(bot.State, "the circle to start its script",
                    s => s.Screen == GameScreen.FieldTask && s.Script is not null, stale, ct);
            }
            catch (GameStateTimeoutException)
            {
                ushort map = await bot.Overworld.GetMapIdAsync(ct);
                (short x, short y) = await bot.Overworld.GetPositionAsync(ct);
                if (map == encounter.MapId && x == encounter.Spot[0] && y == encounter.Spot[1])
                {
                    return DidNotRespond;
                }
                Direction facing = await bot.Overworld.GetFacingAsync(ct);
                return $"The walk to {encounter.Name} ended on map {map}, tile ({x}, {y}), facing {facing.ToString().ToLowerInvariant()}; " +
                    $"the circle is at ({encounter.Spot[0]}, {encounter.Spot[1]}).";
            }
            // The step that started the script was the macro's last, so nothing of it is left to wait for.
            bot.ClearMacro();
            Console.WriteLine($"[Static] walk {walk.ElapsedMilliseconds} ms, script {started.Script}");
            return null;
        }
    }
}
