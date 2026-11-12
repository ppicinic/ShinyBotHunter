using ShinyBotHunter.Bot;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    // The static encounter hunt (docs/specs/sinjoh-static.md, docs/research/sinjoh-static.md): save once at Run, then each
    // attempt replays the walk Run made to the circle. A step that goes wrong stops the hunt with its sentence.
    public class HgssStaticEncounterEvent : AbstractBotEvent<bool>
    {
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

                return Stop(state, bot, SummaryNotBuilt);
            }
            catch (GameStateTimeoutException ex)
            {
                // Its message already names what was awaited and the state that stayed.
                return Stop(state, bot, ex.Message);
            }
        }
    }
}
