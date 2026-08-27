using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Runs several emulators at a Ruby/Sapphire starter until one of them finds a shiny.
    ///
    /// Top level rather than per-bot, because a hunt owns its emulators: it needs its own ROM copy
    /// per instance so each has a separate save, a throwaway session to create that save, and the
    /// ability to close the rest once one wins. A per-bot action cannot do any of that -- it inherits
    /// an emulator someone else launched, which meant launching a second one on the same ports.
    ///
    /// The winning emulator is left open and parked so its battle can be played by hand; the others
    /// are stopped as soon as it is found, since anything they turn up afterwards would be a second
    /// shiny nobody asked for.
    /// </summary>
    public class MultiStarterHuntEvent : AbstractBotEvent<PokemonData?>
    {
        private readonly BotFactory botFactory;
        private readonly IServiceProvider provider;
        private readonly StarterHuntConfig config;
        private readonly int emulatorCount;
        private readonly string romHandle;
        private readonly string starter;
        private readonly string milestone;
        private readonly string recordingName;

        public override EventState EventState => EventState.Complete;

        public int[] BotPositions => Enumerable.Range(0, emulatorCount).ToArray();

        public PokemonData? Result { get; private set; }

        public MultiStarterHuntEvent(string romHandle, string starter, string milestone, string recordingName,
            int emulatorCount, StarterHuntConfig config, IServiceProvider provider)
        {
            this.romHandle = romHandle;
            this.starter = starter;
            this.milestone = milestone;
            this.recordingName = recordingName;
            this.emulatorCount = emulatorCount;
            this.config = config;
            this.provider = provider;
            this.botFactory = provider.GetRequiredService<BotFactory>();

            config.Validate();
            config.MacroForStarter(starter);
        }

        public override async Task<PokemonData?> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // Cancelling this stops the losing emulators the moment one succeeds, rather than letting
            // them finish a reset cycle and find a second shiny.
            using CancellationTokenSource hunt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var runs = new List<Task<HuntOutcome>>();
            for (int i = 0; i < emulatorCount; i++)
            {
                runs.Add(RunOneAsync(i, hunt.Token));
            }

            while (runs.Count > 0)
            {
                Task<HuntOutcome> finished = await Task.WhenAny(runs);
                runs.Remove(finished);

                HuntOutcome outcome;
                try
                {
                    outcome = await finished;
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    // One emulator failing is not the hunt failing; the rest keep going.
                    Console.WriteLine($"[StarterHunt] An emulator stopped with an error: {ex.Message}");
                    continue;
                }

                if (outcome.Shiny is null)
                {
                    continue;
                }

                Result = outcome.Shiny;
                state.GameFlavor = GameFlavor.RS;
                state.Milestone = milestone;
                state.Bot = outcome.Bot;
                state.TargetPokemon = outcome.Target;

                Console.WriteLine(
                    $"[StarterHunt] Shiny {outcome.Shiny.Name} found on emulator {outcome.Position}. " +
                    "Stopping the others; play out the battle then Resume to save.");
                await hunt.CancelAsync();

                // Held open deliberately: this is the emulator the player finishes the battle on.
                state.State.Push(EventState.Waiting);
                return Result;
            }

            return null;
        }

        private record HuntOutcome(int Position, IBot Bot, PokemonData? Shiny, PersistPokemonData? Target);

        /// <summary>
        /// One emulator's whole hunt: its own ROM copy, its own save, its own reset loop.
        /// </summary>
        private async Task<HuntOutcome> RunOneAsync(int position, CancellationToken cancellationToken)
        {
            // GameId gives this instance its own ROM copy, and therefore its own save file. Sharing
            // one would mean every emulator resetting into the same game.
            GameContext context = botFactory.BuildGameContext(romHandle, position + 1, position);
            IBot bot = botFactory.CreateBot(context);

            var entry = new StarterHuntEntry(romHandle, starter, recordingName);
            var hunt = new StarterHuntEvent(bot, context, entry, config, provider);

            GameState state = new()
            {
                GameFlavor = GameFlavor.RS,
                Bot = bot,
                Milestone = milestone,
                Target = new Target.ShinyTarget(),
            };

            try
            {
                PokemonData? shiny = await hunt.ExecuteWithResultAsync(state, cancellationToken);
                return new HuntOutcome(position, bot, shiny, state.TargetPokemon);
            }
            finally
            {
                // The winner stays open for the player; everything else is torn down.
                if (cancellationToken.IsCancellationRequested)
                {
                    try { await bot.DisposeAsync(); } catch { }
                }
            }
        }
    }
}
