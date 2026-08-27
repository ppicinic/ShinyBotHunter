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
        private readonly StarterHuntStateStore stateStore;
        private readonly string romDirectory;
        private readonly ushort expectedSpecies;
        private readonly ushort expectedInternalSpecies;

        // Candidate signals for "the action menu is up", probed rather than assumed. gBattleCommunication
        // carries per-battler turn state; gActionSelectionCursor is the FIGHT/BAG/POKEMON/RUN cursor.
        private const uint BattleCommunicationAddress = 0x02024D1E;
        private const uint ActionSelectionCursorAddress = 0x02024E60;
        private readonly Service.Pointers.IPointerService pointers;

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
            this.stateStore = provider.GetRequiredService<StarterHuntStateStore>();
            this.romDirectory = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value.ROMBaseDirectory;

            // Fails here rather than mid-hunt: both are name lookups that either resolve now or never.
            config.Validate();
            config.MacroForStarter(entry.Starter);
            // Resolved now so a starter name the metadata does not know fails at the start rather
            // than after the first battle.
            expectedSpecies = (ushort)provider.GetRequiredService<PokemonMetadataService>()
                .GetPokemonMetadata(entry.Starter).Id;
            // The in-battle structures store internal indices rather than dex numbers.
            expectedInternalSpecies = Gen3PokemonParser.ToInternalSpecies(expectedSpecies);
            pointers = provider.GetRequiredService<GameServices>().Pointers(GameFlavor.RS);
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

            await EnsureSaveExistsAsync(state.Milestone, cancellationToken);
            await bot.LaunchAsync();
            await WaitUntilEmulatorReadyAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                PokemonData? starter = await AttemptAsync(state.Milestone, cancellationToken);

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
        private async Task<PokemonData?> AttemptAsync(string milestone, CancellationToken cancellationToken)
        {
            RunMacro(config.ContinueMacro);
            await bot.WaitForMacroAsync(cancellationToken);

            RunMacro(config.MacroForStarter(entry.Starter));
            await bot.WaitForMacroAsync(cancellationToken);

            if (!await AdvanceUntilStarterIsOutAsync(cancellationToken))
            {
                Console.WriteLine($"[StarterHunt] {entry.RecordingName}: the starter never came out; resetting.");
                return null;
            }

            PokemonData? starter = await ReadStarterAsync(cancellationToken);
            if (starter is null)
            {
                return null;
            }

            await persistence.AddEncounterAsync(new PersistPokemonData
            {
                PokemonData = starter,
                Game = GameFlavor.RS,
                Phase = milestone,
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
        /// Waits for the emulator to actually be running the game before any macro is sent.
        ///
        /// This used to be a fixed wall-clock delay, which is only ever a guess at how long BizHawk
        /// takes to start and load a ROM -- and the guess gets worse the more emulators start at once.
        /// A macro that begins early does not fail: every press lands somewhere unintended, the run
        /// walks off its route, and it can end up in a battle it was never meant to be in.
        ///
        /// A memory read succeeding proves both that the tool is listening and that the core is
        /// running, which is the thing actually being waited for.
        /// </summary>
        private async Task WaitUntilEmulatorReadyAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(config.LaunchReadyTimeoutMs);
            string lastProblem = "no attempt completed";

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await bot.ReadUShortAsync(pointers.Anchor);
                    // Running, but mid-boot. The settle covers the rest of the intro before the first
                    // macro press, which is what the recorded timings assume.
                    await Task.Delay(config.LaunchSettleMs, cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    lastProblem = ex.Message;
                }

                await Task.Delay(config.BattlePollIntervalMs, cancellationToken);
            }

            throw new TimeoutException(
                $"Emulator for {entry.RecordingName} was not responding {config.LaunchReadyTimeoutMs}ms after " +
                $"launch, so no macro was sent ({lastProblem}).");
        }

        /// <summary>
        /// Holds B until the starter is out on the battlefield.
        ///
        /// The pressing has to continue for the whole stretch, which is what went wrong before: the
        /// mash was stopped as soon as gBattleTypeFlags turned on, but that is battle *start*, with
        /// the intro text still to advance. Nothing was pressing B after that, so the game sat waiting
        /// for input until the wait timed out and the run reset without the pokemon ever appearing.
        ///
        /// gBattleMons holds the active battler and carries the starter's species once it is on the
        /// field, so that is the condition to stop on. The precise "FIGHT/BAG/POKEMON/RUN is up"
        /// signal lives in gBattleMainFunc and gBattlerControllerFuncs, but those are ROM function
        /// pointers that move between revisions and would pin the hunt to rev2.
        /// </summary>
        private async Task<bool> AdvanceUntilStarterIsOutAsync(CancellationToken cancellationToken)
        {
            bot.RunRepeatMacro(MacroRegistry.Instance.GetMacro(GameFlavor.RS, config.BattleAdvanceMacro));
            try
            {
                DateTime started = DateTime.UtcNow;
                DateTime deadline = started.AddMilliseconds(config.BattleWaitTimeoutMs);
                bool seenOnField = false;

                while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!seenOnField && await bot.ReadUShortAsync(pointers.BattlePokemon) == expectedInternalSpecies)
                        {
                            seenOnField = true;
                        }

                        if (config.LogBattleStateProbe)
                        {
                            // Records what the candidate action-menu variables actually hold as the
                            // battle progresses, so the stop condition can key on the menu being up
                            // rather than on a duration. Both live in RAM, unlike gBattleMainFunc and
                            // gBattlerControllerFuncs, which are ROM pointers that move per revision.
                            byte[] comm = await bot.ReadBytesAsync(BattleCommunicationAddress, 4);
                            byte cursor = await bot.ReadByteAsync(ActionSelectionCursorAddress);
                            Console.WriteLine(
                                $"[StarterHunt] probe t+{(int)(DateTime.UtcNow - started).TotalMilliseconds}ms " +
                                $"onField={seenOnField} comm=[{string.Join(",", comm)}] cursor={cursor}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[StarterHunt] {entry.RecordingName}: field check failed ({ex.Message}).");
                    }

                    // Keeps pressing past the pokemon appearing, which is the point: the mon is on the
                    // field well before the intro text is done and the action menu is up, and stopping
                    // at the first sighting meant B was never pressed at all.
                    bool longEnough = (DateTime.UtcNow - started).TotalMilliseconds >= config.BattleAdvanceMinMs;
                    if (seenOnField && longEnough)
                    {
                        return true;
                    }

                    await Task.Delay(config.BattlePollIntervalMs, cancellationToken);
                }

                return seenOnField;
            }
            finally
            {
                bot.ClearMacro();
            }
        }

        /// <summary>
        /// Reads the starter, retrying until the game agrees with what was selected.
        ///
        /// The battle flag turns on before the pokemon is out of its ball, and the party is not
        /// settled at that point: most reads fail validation outright, and one decoded stale memory
        /// that happened to pass the checksum and reported a Nidorino in a Mudkip hunt. The checksum
        /// cannot catch that on its own -- it sums all four substructures, so it is blind to which
        /// one is which -- but the hunt knows which ball it chose, and a species that is not the one
        /// selected means the read was too early rather than that the game handed over something
        /// else.
        /// </summary>
        /// <returns>null when nothing readable arrived in time, which is retried by resetting.</returns>
        private async Task<PokemonData?> ReadStarterAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(config.StarterReadTimeoutMs);
            string lastProblem = "no attempt completed";

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    PokemonState party = await reader.GetPartyState(bot);
                    PokemonData? first = party.PartySize > 0 ? party.PartyPokemon[0] : null;

                    if (first is null || first.IsCorrupt)
                    {
                        lastProblem = "party empty or undecodable";
                    }
                    else if (first.Species != expectedSpecies)
                    {
                        // Reported rather than accepted: taking it would file a species the player
                        // never chose, and quietly poison the encounter data.
                        lastProblem = $"read {first.Name} ({first.Species}) but {entry.Starter} ({expectedSpecies}) was chosen";
                    }
                    else
                    {
                        return first;
                    }
                }
                catch (PartyReadUnavailableException ex)
                {
                    lastProblem = ex.Message;
                }

                await Task.Delay(config.StarterReadPollIntervalMs, cancellationToken);
            }

            Console.WriteLine($"[StarterHunt] {entry.RecordingName}: no readable starter ({lastProblem}); resetting.");
            return null;
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
        private async Task EnsureSaveExistsAsync(string milestone, CancellationToken cancellationToken)
        {
            // Whether the save exists is recorded, not inferred from the copy being on disk. The copy
            // is made first and the save written minutes later, so a run interrupted in between leaves
            // a copy with nothing behind it -- and treating that as ready means continuing a game that
            // was never started, which desyncs every macro after it instead of failing.
            string extension = context.RomExtension;
            string target = $"{romDirectory}{context.GameName}{extension}";

            // Both have to hold. The record alone is not enough -- a copy deleted by hand takes its
            // save with it while the record survives -- and the copy alone is not enough either,
            // since it exists for minutes before its save does.
            bool recorded = await stateStore.IsSaveCreatedAsync(GameFlavor.RS, milestone, context.GameName);
            if (recorded && File.Exists(target))
            {
                return;
            }

            if (!File.Exists(target))
            {
                File.Copy($"{romDirectory}{context.SourceGameName}{extension}", target);
            }

            Console.WriteLine($"[StarterHunt] {entry.RecordingName}: creating a new save on {context.GameName}.");

            await bot.LaunchAsync();
            await WaitUntilEmulatorReadyAsync(cancellationToken);
            RunMacro(config.CreateSaveMacro);
            await bot.WaitForMacroAsync(cancellationToken);
            await Task.Delay(config.SaveSettleMs, cancellationToken);
            await bot.FlushSave();
            await bot.DisposeAsync();

            // Only now, once the save is actually on disk. Recording it earlier would make an
            // interrupted creation look complete.
            await stateStore.MarkSaveCreatedAsync(GameFlavor.RS, milestone, context.GameName);
            Console.WriteLine($"[StarterHunt] {entry.RecordingName}: save created on {context.GameName}.");
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
