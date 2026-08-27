using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>One hunt: which ROM to play, which starter to take, and what to file encounters under.</summary>
    public record StarterHuntEntry(string RomHandle, string Starter, string RecordingName);

    /// <summary>
    /// Soft-reset hunt for a shiny Ruby/Sapphire starter on one emulator.
    ///
    /// Ruby and Sapphire do not show the starter when it is chosen -- the pokemon is not revealed
    /// until the Zigzagoon battle is under way -- so unlike the HGSS hunt there is nothing to inspect
    /// before committing. The bot picks the configured ball, advances into the battle, and only then
    /// reads what it got. That is also the only version of this a player could perform, which is the
    /// rule the bot is held to.
    ///
    /// A shiny does not end the hunt on its own: the event parks and hands the emulator back so the
    /// battle can be played manually, then saves and finishes when it is resumed.
    /// </summary>
    public class StarterHuntEvent : AbstractBotEvent<PokemonData?>
    {
        private readonly IBot bot;
        private readonly GameContext context;
        private readonly StarterHuntEntry entry;
        private readonly StarterHuntConfig config;
        private readonly PokemonStateReader reader;
        private readonly PokemonPersistenceService persistence;
        private readonly LegalityService legality;
        private readonly RsStartMenuService startMenu;
        private readonly string romDirectory;

        public override EventState EventState { get; }

        /// <summary>The shiny that ended the hunt, once one has been found and kept.</summary>
        public PokemonData? Result { get; private set; }

        /// <summary>Verdict from the most recent attempt, so the keep decision does not re-analyse.</summary>
        private LegalityResult? lastVerdict;

        public StarterHuntEvent(IBot bot, GameContext context, StarterHuntEntry entry, StarterHuntConfig config, IServiceProvider provider)
        {
            this.bot = bot;
            this.context = context;
            this.entry = entry;
            this.config = config;
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.persistence = provider.GetRequiredService<PokemonPersistenceService>();
            this.legality = provider.GetRequiredService<LegalityService>();
            this.startMenu = provider.GetRequiredService<RsStartMenuService>();
            this.romDirectory = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value.ROMBaseDirectory;

            // Fails here rather than mid-hunt: both are name lookups that either resolve now or never.
            config.Validate();
            config.MacroForStarter(entry.Starter);
            EventState = EventState.NotStarted;
        }

        public override async Task<PokemonData?> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // Re-entered after the player finished the battle by hand.
            if (state.State.Count > 0 && state.State.Peek() == EventState.Resume)
            {
                await FinishShinyAsync(state, cancellationToken);
                return Result;
            }

            await EnsureSaveExistsAsync(cancellationToken);
            await bot.LaunchAsync();
            await Task.Delay(config.LaunchSettleMs, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                PokemonData? starter = await AttemptAsync(cancellationToken);

                if (starter is null)
                {
                    await SoftResetAsync(cancellationToken);
                    continue;
                }

                if (!starter.IsShiny)
                {
                    await SoftResetAsync(cancellationToken);
                    continue;
                }

                // An illegal shiny is not recorded: a catch no real game could have produced corrupts
                // exactly the data the hunt exists to collect, so it is logged above and hunted past.
                if (lastVerdict is not null && !lastVerdict.IsLegal)
                {
                    Console.WriteLine($"[StarterHunt] {entry.RecordingName}: skipping illegal shiny and continuing.");
                    await SoftResetAsync(cancellationToken);
                    continue;
                }

                Result = starter;
                state.TargetPokemon = new PersistPokemonData
                {
                    PokemonData = starter,
                    Game = GameFlavor.RS,
                    Phase = state.Milestone,
                    GameFileName = entry.RecordingName,
                    DateTime = DateTime.UtcNow,
                };

                // Hand the emulator back rather than finishing. Parking on Waiting is what keeps this
                // instance alive so Resume re-enters it with the battle already won.
                Console.WriteLine(
                    $"[StarterHunt] Shiny {starter.Name} on {entry.RecordingName}. " +
                    "Play out the battle, then Resume to save.");
                state.State.Push(EventState.Waiting);
                return Result;
            }

            return null;
        }

        /// <summary>
        /// One reset cycle: continue, take the ball, advance into the battle, read what came out.
        /// </summary>
        /// <returns>null when the attempt could not produce a readable starter, which is retried.</returns>
        private async Task<PokemonData?> AttemptAsync(CancellationToken cancellationToken)
        {
            RunMacro(config.ContinueMacro);
            await bot.WaitForMacroAsync(cancellationToken);

            RunMacro(config.MacroForStarter(entry.Starter));
            await bot.WaitForMacroAsync(cancellationToken);

            if (!await AdvanceIntoBattleAsync(cancellationToken))
            {
                Console.WriteLine($"[StarterHunt] {entry.RecordingName}: battle never started; resetting.");
                return null;
            }

            PokemonData? starter;
            try
            {
                var party = await reader.GetPartyState(bot);
                starter = party.PartySize > 0 ? party.PartyPokemon[0] : null;
            }
            catch (PartyReadUnavailableException ex)
            {
                // The emulator could not get a consistent party within its retry budget. A reset is
                // cheaper than reasoning about a half-written one.
                Console.WriteLine($"[StarterHunt] {entry.RecordingName}: party unreadable ({ex.Message}); resetting.");
                return null;
            }

            if (starter is null || starter.IsCorrupt)
            {
                return null;
            }

            await persistence.AddEncounterAsync(new PersistPokemonData
            {
                PokemonData = starter,
                Game = GameFlavor.RS,
                Phase = "1-1 Starters",
                GameFileName = entry.RecordingName,
                DateTime = DateTime.UtcNow,
            });

            // Analysed once and reused for the keep decision, so a shiny is never checked twice.
            // A shiny is always checked; everything else only while QA logging is on.
            if (config.LogLegalityForEveryEncounter || starter.IsShiny)
            {
                lastVerdict = legality.Analyze(RawOf(starter), GameFlavor.RS);
                string label = $"{entry.RecordingName} {starter.Name} PID {starter.PID:X8}{(starter.IsShiny ? " SHINY" : "")}";
                if (lastVerdict.IsLegal)
                {
                    Console.WriteLine($"[StarterHunt] legality OK   {label}");
                }
                else
                {
                    Console.WriteLine($"[StarterHunt] legality FAIL {label}{Environment.NewLine}{lastVerdict.Report}");
                }
            }
            else
            {
                lastVerdict = null;
            }

            return starter;
        }

        /// <summary>
        /// Mashes B until the battle is running. The macro repeats indefinitely and is stopped here,
        /// rather than being a fixed-length press sequence -- a fixed length would be a guess at load
        /// timing, and would silently desync the moment that guess was wrong.
        /// </summary>
        private async Task<bool> AdvanceIntoBattleAsync(CancellationToken cancellationToken)
        {
            bot.RunRepeatMacro(MacroRegistry.Instance.GetMacro(GameFlavor.RS, config.BattleAdvanceMacro));
            try
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(config.BattleWaitTimeoutMs);
                while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
                {
                    if (await bot.GetBattleState() != 0)
                    {
                        return true;
                    }
                    await Task.Delay(config.BattlePollIntervalMs, cancellationToken);
                }
                return false;
            }
            finally
            {
                bot.ClearMacro();
            }
        }

        /// <summary>
        /// Saves the game after the player has finished the battle, then flushes so the save reaches
        /// disk rather than sitting in the emulator.
        /// </summary>
        private async Task FinishShinyAsync(GameState state, CancellationToken cancellationToken)
        {
            await SaveViaStartMenuAsync(cancellationToken);
            await Task.Delay(config.SaveSettleMs, cancellationToken);
            await bot.FlushSave();

            if (state.TargetPokemon is not null)
            {
                await persistence.AddCaughtPokemonAsync(state.TargetPokemon);
                Result = state.TargetPokemon.PokemonData;
            }

            state.SwapState(EventState.Complete);
            Console.WriteLine($"[StarterHunt] {entry.RecordingName}: saved and recorded.");
        }

        /// <summary>
        /// Opens the start menu, reads where SAVE actually is, and moves exactly that far. See
        /// <see cref="RsStartMenuService"/> for why this cannot be a fixed key sequence.
        /// </summary>
        private async Task SaveViaStartMenuAsync(CancellationToken cancellationToken)
        {
            bot.RunMacroString("T.Start");
            await bot.WaitForMacroAsync(cancellationToken);

            RsStartMenuService.MenuState menu = await WaitForStartMenuAsync(cancellationToken);
            int steps = RsStartMenuService.StepsTo(menu, config.SaveMenuActionId);
            Console.WriteLine($"[StarterHunt] Start menu: {menu.Describe()}; SAVE is {steps} step(s) away.");

            string cursorMacro = RsStartMenuService.BuildCursorMacro(steps);
            if (cursorMacro.Length > 0)
            {
                bot.RunMacroString(cursorMacro);
                await bot.WaitForMacroAsync(cancellationToken);
            }

            RunMacro(config.SaveConfirmMacro);
            await bot.WaitForMacroAsync(cancellationToken);
        }

        /// <summary>
        /// Polls until the start menu reports a sane state, rather than waiting a fixed time for it to
        /// open. How long that takes is not worth guessing at: too short and the menu is read before it
        /// is populated, which looks identical to a menu that does not contain SAVE.
        /// </summary>
        private async Task<RsStartMenuService.MenuState> WaitForStartMenuAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(config.BattleWaitTimeoutMs);
            RsStartMenuService.MenuState menu = await startMenu.ReadAsync(bot);

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (menu.Count > 0 && menu.Count <= 10 && menu.CursorPos < menu.Count)
                {
                    return menu;
                }
                await Task.Delay(config.BattlePollIntervalMs, cancellationToken);
                menu = await startMenu.ReadAsync(bot);
            }

            // Returned rather than thrown so StepsTo produces the failure, with the menu contents in
            // its message.
            return menu;
        }

        private async Task SoftResetAsync(CancellationToken cancellationToken)
        {
            RunMacro(config.SoftResetMacro);
            await Task.Delay(config.SoftResetSettleMs, cancellationToken);
        }

        /// <summary>
        /// Creates the save this hunt resets back to, if the per-instance ROM copy does not exist yet.
        /// Each emulator gets its own copy so each gets its own save file.
        /// </summary>
        private async Task EnsureSaveExistsAsync(CancellationToken cancellationToken)
        {
            string extension = context.RomExtension;
            string target = $"{romDirectory}{context.GameName}{extension}";
            if (File.Exists(target))
            {
                return;
            }

            File.Copy($"{romDirectory}{context.SourceGameName}{extension}", target);
            Console.WriteLine($"[StarterHunt] {entry.RecordingName}: creating a new save.");

            await bot.LaunchAsync();
            await Task.Delay(config.LaunchSettleMs, cancellationToken);
            RunMacro(config.CreateSaveMacro);
            await bot.WaitForMacroAsync(cancellationToken);
            await Task.Delay(config.SaveSettleMs, cancellationToken);
            await bot.FlushSave();
            await bot.DisposeAsync();
        }

        private void RunMacro(string macroName)
        {
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.RS, macroName));
        }

        /// <summary>
        /// The block the pokemon was decoded from. Legality analysis reads bytes rather than a parsed
        /// model, so handing it the original avoids two decoders disagreeing.
        /// </summary>
        private static byte[] RawOf(PokemonData data) => data.RawBytes ?? Array.Empty<byte>();
    }
}
