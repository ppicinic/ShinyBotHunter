using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// One hunt: which ROM to play, which starter to take, what to file encounters under, and
    /// whether only a female counts -- per entry, since breeding plans differ per starter.
    /// </summary>
    public record StarterHuntEntry(string RomHandle, string Starter, string RecordingName, bool RequireFemale = false);

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
        // Shared with the other runs of this hunt and nothing else, so booting is serialised across
        // the wave without gating any other event's emulators.
        private readonly SemaphoreSlim launchGate;
        private readonly PokemonStateReader reader;
        private readonly PokemonPersistenceService persistence;
        private readonly LegalityService legality;
        private readonly RsStartMenuService startMenu;
        private readonly StarterHuntStateStore stateStore;
        private readonly string romDirectory;
        private readonly string bizHawkDirectory;
        private readonly ushort expectedSpecies;
        private readonly ushort expectedInternalSpecies;

        // Candidate signals for "the action menu is up", probed rather than assumed. gBattleCommunication
        // carries per-battler turn state; gActionSelectionCursor is the FIGHT/BAG/POKEMON/RUN cursor.
        private const uint BattleCommunicationAddress = 0x02024D1E;
        private const uint ActionSelectionCursorAddress = 0x02024E60;
        private readonly Service.Pointers.IPointerService pointers;

        /// <summary>The shiny that ended the hunt, once one has been found and kept.</summary>
        public PokemonData? Result { get; private set; }

        /// <summary>Verdict from the most recent attempt, so the keep decision does not re-analyse.</summary>
        private LegalityResult? lastVerdict;

        public StarterHuntEvent(IBot bot, GameContext context, StarterHuntEntry entry, StarterHuntConfig config, SemaphoreSlim launchGate, IServiceProvider provider)
        {
            this.bot = bot;
            this.context = context;
            this.entry = entry;
            this.config = config;
            this.launchGate = launchGate;
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.persistence = provider.GetRequiredService<PokemonPersistenceService>();
            this.legality = provider.GetRequiredService<LegalityService>();
            this.startMenu = provider.GetRequiredService<RsStartMenuService>();
            this.stateStore = provider.GetRequiredService<StarterHuntStateStore>();
            var settings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;
            this.romDirectory = settings.ROMBaseDirectory;
            this.bizHawkDirectory = settings.BizHawkBaseDirectory;

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
            await LaunchAndWaitReadyAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Throws rather than returning nothing, so reaching here means the pokemon is known.
                PokemonData starter = await AttemptAsync(state.Milestone, cancellationToken);

                if (!starter.IsShiny)
                {
                    await SoftResetAsync(cancellationToken);
                    continue;
                }

                // Gender is decided by the same PID as shininess, so a hunt that needs a female
                // (RS has no Ditto, and only a female passes the species on at the daycare) has to
                // fold it into the keep condition rather than fix it afterwards.
                if (entry.RequireFemale && starter.Gender != 2)
                {
                    // A deliberate discard of a real shiny, so it is said as loudly as possible. The
                    // encounter row already recorded it, PID and all.
                    Console.WriteLine(
                        $"[StarterHunt] !!! {entry.RecordingName}: SHINY {starter.Name} PID {starter.PID:X8} is " +
                        "not female and RequireFemale is set. Resetting past it, as configured.");
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
        ///
        /// Either returns the pokemon that was obtained or throws. There is deliberately no third
        /// outcome: anything that resets without knowing what the pokemon was could be resetting over
        /// a shiny, and no record of it would survive.
        /// </summary>
        private async Task<PokemonData> AttemptAsync(string milestone, CancellationToken cancellationToken)
        {
            RunMacro(config.ContinueMacro);
            await bot.WaitForMacroAsync(cancellationToken);

            RunMacro(config.MacroForStarter(entry.Starter));
            await bot.WaitForMacroAsync(cancellationToken);

            if (!await AdvanceUntilStarterIsOutAsync(cancellationToken))
            {
                // Not a reason to reset on its own. The pokemon is created when the ball is chosen,
                // so one exists whether or not it made it onto the screen -- and it could be the
                // shiny. The read below decides; this only says the battle did not look right.
                Console.WriteLine(
                    $"[StarterHunt] {entry.RecordingName}: the starter never appeared on the field; " +
                    "reading the party anyway before deciding.");
            }

            PokemonData starter = await ReadStarterAsync(cancellationToken);

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
                string gender = starter.Gender switch { 1 => "M", 2 => "F", _ => "-" };
                string label = $"{entry.RecordingName} {starter.Name} ({gender}) PID {starter.PID:X8}{(starter.IsShiny ? " SHINY" : "")}";
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
        /// <summary>
        /// The value gBattleCommunication[1] holds once the FIGHT/BAG/POKEMON/RUN menu is up and
        /// waiting for input. Derived from probing a live battle rather than from a symbol: the
        /// byte reads 0 for the whole intro, steps through 2 as the intro hands over, and settles at
        /// 4 when the menu appears. gBattleCommunication is RAM and revision-invariant, unlike the
        /// controller callbacks that would name this state directly but live in ROM and move per
        /// revision. Re-derivable any time by enabling LogBattleStateProbe.
        /// </summary>
        private const byte ActionMenuCommValue = 4;

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

                        byte[] comm = await bot.ReadBytesAsync(BattleCommunicationAddress, 4);

                        if (config.LogBattleStateProbe)
                        {
                            byte cursor = await bot.ReadByteAsync(ActionSelectionCursorAddress);
                            Console.WriteLine(
                                $"[StarterHunt] probe t+{(int)(DateTime.UtcNow - started).TotalMilliseconds}ms " +
                                $"onField={seenOnField} comm=[{string.Join(",", comm)}] cursor={cursor}");
                        }

                        // Both, not either: the species appears mid-intro, and the menu value alone
                        // could in principle recur in a state where the party is not what we think.
                        if (seenOnField && comm[1] == ActionMenuCommValue)
                        {
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[StarterHunt] {entry.RecordingName}: battle state check failed ({ex.Message}).");
                    }

                    await Task.Delay(config.BattlePollIntervalMs, cancellationToken);
                }

                // A cancelled wave lands here through the loop condition rather than through an
                // await, which used to fall through into the starter read and come out as a
                // refuse-to-reset error nothing cleaned up after. Cancellation is cancellation.
                cancellationToken.ThrowIfCancellationRequested();
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
        /// <returns>The starter. Never returns without one; it throws instead.</returns>
        private async Task<PokemonData> ReadStarterAsync(CancellationToken cancellationToken)
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

            cancellationToken.ThrowIfCancellationRequested();

            // Never reset on this. A reset says "that one was not a target", and an unreadable party
            // does not say that -- the pokemon sitting there could be the shiny this hunt exists to
            // find, and resetting would destroy it with no record that it ever existed. Unknown is
            // not the same as uninteresting, so the hunt stops and says so instead.
            string diagnostic = await DescribePartyMemoryAsync();
            throw new InvalidOperationException(
                $"Could not read the starter on {entry.RecordingName} within {config.StarterReadTimeoutMs}ms " +
                $"({lastProblem}). Refusing to reset, because the pokemon on screen may be a shiny and " +
                $"resetting would lose it. Party memory at the time: {diagnostic}");
        }

        /// <summary>
        /// Dumps the party memory a failed read was looking at, so the cause is diagnosable from the
        /// log rather than by reproducing it.
        /// </summary>
        private async Task<string> DescribePartyMemoryAsync()
        {
            try
            {
                byte count = await bot.ReadByteAsync((uint)(pointers.Anchor + pointers.PartySize));
                byte[] head = await bot.ReadBytesAsync((uint)(pointers.Anchor + pointers.PartySlots), 32);
                return $"count={count} slot0[0..31]={Convert.ToHexString(head)}";
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.Message})";
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

            // The save either produced this copy's file or it did not; saying "saved" on faith is how
            // a failed save went unnoticed until the shiny was gone.
            if (!File.Exists(SaveRamFile))
            {
                throw new InvalidOperationException(
                    $"No save appeared at {SaveRamFile} after saving. The shiny is still on the " +
                    "paused emulator; save manually before closing anything.");
            }

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
            int target = menu.IndexOf(config.SaveMenuActionId);
            // StepsTo carries the validation and its diagnostic message; the movement itself is done
            // one verified step at a time below.
            RsStartMenuService.StepsTo(menu, config.SaveMenuActionId);
            Console.WriteLine($"[StarterHunt] Start menu: {menu.Describe()}; SAVE is at position {target}.");

            await MoveCursorToAsync(target, cancellationToken);

            RunMacro(config.SaveConfirmMacro);
            await bot.WaitForMacroAsync(cancellationToken);
        }

        /// <summary>
        /// Walks the start-menu cursor to a position one verified press at a time.
        ///
        /// Firing the whole run of presses blind is how a save landed on the trainer card: the maths
        /// said three downs, one press was swallowed by the menu, and A went to whatever was two rows
        /// down. The cursor is readable, so each press is checked before the next -- a swallowed
        /// input costs a retry instead of selecting the wrong entry.
        /// </summary>
        private async Task MoveCursorToAsync(int target, CancellationToken cancellationToken)
        {
            const int MaxPresses = 24;
            for (int presses = 0; presses < MaxPresses; presses++)
            {
                RsStartMenuService.MenuState menu = await startMenu.ReadAsync(bot);
                if (menu.CursorPos == target)
                {
                    return;
                }

                bot.RunMacroString(menu.CursorPos < target ? "T.Down;W.15" : "T.Up;W.15");
                await bot.WaitForMacroAsync(cancellationToken);
                await Task.Delay(config.BattlePollIntervalMs, cancellationToken);
            }

            RsStartMenuService.MenuState final = await startMenu.ReadAsync(bot);
            throw new InvalidOperationException(
                $"Start-menu cursor would not settle on position {target} after {MaxPresses} presses " +
                $"({final.Describe()}). Refusing to press A on whatever is highlighted.");
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
            bool recordedSaveExists = await stateStore.IsSaveCreatedAsync(GameFlavor.RS, context.GameName);
            string extension = context.RomExtension;
            string target = $"{romDirectory}{context.GameName}{extension}";

            // All three have to hold. The record alone is not enough -- a copy deleted by hand takes
            // its save with it while the record survives -- the copy alone is not enough either, since
            // it exists for minutes before its save does, and the save file is checked directly
            // because the record cannot know it was moved or removed underneath it.
            if (recordedSaveExists && File.Exists(target) && File.Exists(SaveRamFile))
            {
                return;
            }

            if (!File.Exists(target))
            {
                File.Copy($"{romDirectory}{context.SourceGameName}{extension}", target);
            }

            // A duplicated trainer is thrown away and made again. Two saves sharing one agree on
            // exactly which PIDs are shiny, so the second explores nothing the first did not: it is
            // a wasted copy rather than a broken one, which is why running out of attempts is
            // reported and accepted instead of failing the hunt.
            for (int attempt = 1; ; attempt++)
            {
                // A fresh copy must start saveless, and the copy file is not where the save lives:
                // BizHawk keeps it in the copy's save-slot directory, which survives the copy being
                // deleted and recreated. Left in place, the new-game macro boots into a game that
                // already has a save -- the title menu gains a Continue entry and every recorded
                // press from there lands one row off.
                WipeSaveSlot();

                Console.WriteLine($"[StarterHunt] {entry.RecordingName}: creating a new save on {context.GameName}.");

                await LaunchAndWaitReadyAsync(cancellationToken);
                RunMacro(config.CreateSaveMacro);
                await bot.WaitForMacroAsync(cancellationToken);
                await Task.Delay(config.SaveSettleMs, cancellationToken);
                await bot.FlushSave();
                await bot.DisposeAsync();

                ushort? trainerId = Gen3SaveFile.ReadTrainerIdFromFile(SaveRamFile);
                if (trainerId is null)
                {
                    // The save is verified separately by the hunt itself; an unreadable trainer id
                    // is not grounds to sit here remaking it.
                    Console.WriteLine(
                        $"[StarterHunt] {entry.RecordingName}: could not read a trainer id from the new save " +
                        $"on {context.GameName}; keeping it and continuing.");
                    break;
                }

                string? clash = SaveSharingTrainerId(trainerId.Value);
                if (clash is null)
                {
                    Console.WriteLine($"[StarterHunt] {entry.RecordingName}: trainer id {trainerId} on {context.GameName}.");
                    break;
                }

                if (attempt >= config.SaveTrainerIdRetries)
                {
                    Console.WriteLine(
                        $"[StarterHunt] {entry.RecordingName}: trainer id {trainerId} still clashes with {clash} after " +
                        $"{attempt} attempts. Keeping it -- the save-creation macro may not reach enough RNG frames " +
                        "to produce a distinct trainer.");
                    break;
                }

                Console.WriteLine(
                    $"[StarterHunt] {entry.RecordingName}: trainer id {trainerId} already belongs to {clash}. " +
                    $"Wiping and creating the save again (attempt {attempt + 1} of {config.SaveTrainerIdRetries}).");
            }

            // Only now, once the save is actually on disk. Recording it earlier would make an
            // interrupted creation look complete.
            await stateStore.MarkSaveCreatedAsync(GameFlavor.RS, context.GameName);
            Console.WriteLine($"[StarterHunt] {entry.RecordingName}: save created on {context.GameName}.");
        }

        private string SaveRamDirectory
            => Path.Combine(bizHawkDirectory, context.ConsoleType.ToString(), "SaveRAM");

        /// <summary>
        /// Where this copy's save lives. The fork names SaveRAM after the ROM file, so the path is
        /// derived rather than discovered -- one file per copy, in one directory.
        /// </summary>
        private string SaveRamFile => Path.Combine(SaveRamDirectory, $"{context.GameName}.SaveRAM");

        /// <summary>
        /// The name of another save already using this trainer id, or null when it is new. Every save
        /// on this console is compared, not just this game's copies: two saves that agree on a trainer
        /// agree on which pokemon are shiny regardless of which game they belong to.
        /// </summary>
        private string? SaveSharingTrainerId(ushort trainerId)
        {
            if (!Directory.Exists(SaveRamDirectory))
            {
                return null;
            }

            string mine = Path.GetFileName(SaveRamFile);
            foreach (string save in Directory.EnumerateFiles(SaveRamDirectory, "*.SaveRAM"))
            {
                if (string.Equals(Path.GetFileName(save), mine, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (Gen3SaveFile.ReadTrainerIdFromFile(save) == trainerId)
                {
                    return Path.GetFileNameWithoutExtension(save);
                }
            }
            return null;
        }

        /// <summary>
        /// Boots the emulator and waits for it to answer, one wave member at a time. The gate is held
        /// across both because an emulator that has been started but has not yet answered is exactly
        /// the one still competing for the machine.
        /// </summary>
        private async Task LaunchAndWaitReadyAsync(CancellationToken cancellationToken)
        {
            await launchGate.WaitAsync(cancellationToken);
            try
            {
                await bot.LaunchAsync();
                await WaitUntilEmulatorReadyAsync(cancellationToken);
            }
            finally
            {
                launchGate.Release();
            }
        }

        /// <summary>
        /// Removes this copy's save and the backups BizHawk keeps beside it. The .bak files matter:
        /// left behind, BizHawk restores one and the new-game macro boots into a game that already
        /// has a save, where the title menu gains a Continue entry and every recorded press from
        /// there lands one row off.
        /// </summary>
        private void WipeSaveSlot()
        {
            if (!Directory.Exists(SaveRamDirectory))
            {
                return;
            }

            string prefix = Path.GetFileName(SaveRamFile);
            string[] stale = Directory.EnumerateFiles(SaveRamDirectory, $"{prefix}*").ToArray();
            if (stale.Length == 0)
            {
                return;
            }

            Console.WriteLine($"[StarterHunt] {entry.RecordingName}: wiping {stale.Length} stale save file(s) for {context.GameName}.");
            foreach (string file in stale)
            {
                File.Delete(file);
            }
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
