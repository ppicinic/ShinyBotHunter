using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators.Dolphin;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Hunts the Colosseum Bonus Disc Wishmaker Jirachi on the operator's own Ruby or Sapphire save.
    ///
    /// One cycle continues the save in BizHawk, plays a random while, saves in-game until the first
    /// block holds the new save, and closes; then Dolphin boots the disc with a copy of that save on
    /// its integrated GBA, and the disc writes the Jirachi into the copy. EmuHawk then opens the copy,
    /// shows the Jirachi at its summary screen, and reads it from RAM. The Jirachi is a function of
    /// the save, so each cycle's PID is checked against the one the save predicts. The bot never
    /// writes save bytes: the hunt save changes only by the game saving.
    /// </summary>
    public class WishmakerJirachiEvent : AbstractBotEvent<PokemonData?>
    {
        private const string ResultSuffix = "-WISHMKR";

        // Below this the save is not a whole 128 KiB flash image (BizHawk adds a 16-byte footer).
        internal const int FlashSize = 131072;

        // From the spike: three A presses reached the field from the title screen; ten is the margin.
        private const int FieldCheckAttempts = 10;
        // From the spike: one A press and start-menu check every 2.5 s.
        private const int FieldCheckMs = 2500;
        private const int MenuPollMs = 250;
        // From the spike's save, which saved twice in a row every cycle: held presses and the waits
        // after each, so a press is not eaten by the transition the previous one started.
        private const string CloseTextBoxesMacro = "H.B.4;W.20;H.B.4;W.20";
        private const int CloseTextBoxesMs = 1200;
        private const string OpenStartMenuMacro = "H.Start.4;W.6";
        private const int OpenStartMenuMs = 700;
        private const string CloseStartMenuMacro = "H.B.4;W.10";
        private const int CloseStartMenuMs = 1500;
        private const string DismissSavedMacro = "H.A.4;W.30";
        private const int DismissSavedMs = 1000;
        // From the spike: twelve Start presses was its bound for finding the menu.
        private const int OpenStartMenuAttempts = 12;
        // A cursor press shows in the cursor byte within a few frames; 1 s is the margin.
        private const int CursorProbeMs = 1000;
        // FR-9: flush and read the SaveRAM once a second.
        private const int SavePollMs = 1000;
        // NFR-1: a cycle completes in under 300 s.
        private const int CycleWarnMs = 300000;
        // pokeruby's MENU_ACTION order: POKéMON is 1, as SAVE is 5.
        private const int PokemonMenuActionId = 1;
        // FR-17: how long the summary stays on view before the RAM read.
        private const int SummaryDisplayMs = 3000;
        // Observed live on rs.qa-jirachi (#188): held presses and the waits after each, as the save's.
        private const string OpenPartyMacro = "H.A.4;W.10";
        private const int OpenPartyMs = 2000;
        // Observed live on rs.qa-jirachi (#188).
        private const string PartyDownMacro = "H.Down.4;W.8";
        private const int PartyDownMs = 500;
        // Observed live on rs.qa-jirachi (#188).
        private const string OpenSlotMenuMacro = "H.A.4;W.10";
        private const int OpenSlotMenuMs = 1000;
        // Observed live on rs.qa-jirachi (#188).
        private const string OpenSummaryMacro = "H.A.4;W.10";
        private const int OpenSummaryMs = 2000;
        // Observed live on rs.qa-jirachi (#188): four B, from the summary to the slot menu, the party
        // list, the start menu and the field; the longer waits cover the two screen fades.
        private const string CloseToFieldMacro = "H.B.4;W.60;H.B.4;W.30;H.B.4;W.90;H.B.4;W.30";
        private const int CloseToFieldMs = 1000;
        private const ushort JirachiSpecies = 385;

        private readonly string gameName;
        private readonly string phaseName;
        private readonly int position;
        private readonly WishmakerJirachiConfig config;
        private readonly BotFactory botFactory;
        private readonly PokemonPersistenceService persistence;
        private readonly ShinyAlertService alerts;
        private readonly RsStartMenuService startMenu;
        private readonly PokemonStateReader reader;
        private readonly ApplicationSettings settings;
        private readonly (string HuntSave, string ResultSave, string HuntRom, string ResultRom) paths;

        public WishmakerJirachiEvent(string gameName, string phaseName, string extension, int position,
            WishmakerJirachiConfig config, IServiceProvider provider)
        {
            this.gameName = gameName;
            this.phaseName = phaseName;
            this.position = position;
            this.config = config;
            botFactory = provider.GetRequiredService<BotFactory>();
            persistence = provider.GetRequiredService<PokemonPersistenceService>();
            alerts = provider.GetRequiredService<ShinyAlertService>();
            startMenu = provider.GetRequiredService<RsStartMenuService>();
            reader = provider.GetRequiredService<PokemonStateReader>();
            settings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;

            paths = PathsFor(settings.ROMBaseDirectory, settings.BizHawkBaseDirectory, gameName, extension);
            // Runs in BotService's Start, so each refusal comes back as a 400 before anything launches.
            CheckSetup(paths.HuntSave, Path.Combine(settings.DolphinBaseDirectory, "Dolphin.exe"),
                settings.GbaBiosPath, settings.ColosseumBonusDiscPath);
        }

        public override async Task<PokemonData?> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // One-time backup, never written again. Not "{name}.SaveRAM.bak": BizHawk writes that
            // itself on every flush.
            string backup = paths.HuntSave + ".wishmaker.bak";
            if (!File.Exists(backup))
            {
                File.Copy(paths.HuntSave, backup);
                Console.WriteLine($"[Wishmaker] Backed up the hunt save to {backup}.");
            }
            if (!File.Exists(paths.ResultRom))
            {
                File.Copy(paths.HuntRom, paths.ResultRom);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PokemonData jirachi = await CycleAsync(state, cancellationToken);
                if (jirachi.IsShiny)
                {
                    return jirachi;
                }
            }
        }

        /// <summary>One cycle: the BizHawk half, the disc, and the checked, logged result (FR-17).</summary>
        private async Task<PokemonData> CycleAsync(GameState state, CancellationToken ct)
        {
            DateTime started = DateTime.UtcNow;

            await PlayAndSaveAsync(ct);
            ct.ThrowIfCancellationRequested();

            byte[] preDisc = File.ReadAllBytes(paths.HuntSave);
            ushort seed = Gen3SaveFile.ReadWishmakerSeed(preDisc);
            uint predicted = Gen3SaveFile.PredictWishmakerPid(seed);
            Console.WriteLine($"[Wishmaker] Seed {seed:X4} predicts PID {predicted:X8}.");

            await RunDiscAsync(ct);
            ct.ThrowIfCancellationRequested();

            PokemonData jirachi = await ShowAndReadResultAsync(state, seed, predicted, ct);

            double elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds;
            if (elapsedMs > CycleWarnMs)
            {
                Console.WriteLine($"[Wishmaker] Cycle took {elapsedMs / 1000:0} s, over the {CycleWarnMs / 1000} s budget.");
            }
            return jirachi;
        }

        /// <summary>
        /// The BizHawk half: continue, confirm the field, play, save until the first block is new
        /// (FR-8 to FR-10), then flush and close (FR-11). Disposing the bot flushes and closes it, on
        /// success and on failure alike.
        /// </summary>
        private async Task PlayAndSaveAsync(CancellationToken ct)
        {
            byte[] before = File.ReadAllBytes(paths.HuntSave);
            uint previousMax = MaxLiveCounter(before);

            IBot bot = botFactory.CreateBot(botFactory.BuildGameContext(gameName, -1, position));
            try
            {
                var pointers = (Service.Pointers.RsPointerService)bot.Addresses;
                await bot.LaunchAsync();
                await WaitUntilReadyAsync(bot, pointers, ct);
                await ReachFieldAsync(bot, pointers, ct);

                await Task.Delay(Random.Shared.Next(config.MinPlayMs, config.MaxPlayMs + 1), ct);

                // Never cancelled: a cancel lands after the save, never mid-write (FR-19).
                byte[] after = await SaveAsync(bot, pointers, previousMax);
                if (!FirstBlockAdvanced(before, after))
                {
                    Console.WriteLine("[Wishmaker] The save landed in the second block; saving again.");
                    await SaveAsync(bot, pointers, MaxLiveCounter(after));
                }
            }
            finally
            {
                await bot.DisposeAsync();
            }
        }

        private async Task WaitUntilReadyAsync(IBot bot, Service.Pointers.RsPointerService pointers, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(config.LaunchReadyTimeoutMs);
            string lastProblem = "no attempt completed";
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await bot.ReadUShortAsync(pointers.Anchor);
                    return;
                }
                catch (Exception ex)
                {
                    lastProblem = ex.Message;
                }
                await Task.Delay(MenuPollMs, ct);
            }
            throw new TimeoutException(
                $"EmuHawk for {gameName} was not responding {config.LaunchReadyTimeoutMs} ms after launch ({lastProblem}).");
        }

        /// <summary>
        /// FR-8: the field is confirmed by the start menu opening with SAVE in it, then closed with B.
        /// Until then A, never B: B at the CONTINUE menu goes back to the title.
        /// </summary>
        private async Task ReachFieldAsync(IBot bot, Service.Pointers.RsPointerService pointers, CancellationToken ct)
        {
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.RS, config.ContinueMacro));
            await bot.WaitForMacroAsync(ct);

            string lastMenu = "none read";
            for (int attempt = 1; attempt <= FieldCheckAttempts; attempt++)
            {
                bot.RunMacroString("T.Start");
                await bot.WaitForMacroAsync(ct);
                RsStartMenuService.MenuState menu = await WaitForStartMenuAsync(bot, pointers, ct);
                lastMenu = menu.Describe();
                if (IsOpenWithSave(menu))
                {
                    bot.RunMacroString("T.B");
                    await bot.WaitForMacroAsync(ct);
                    Console.WriteLine($"[Wishmaker] in the field after {attempt} check(s).");
                    return;
                }
                bot.RunMacroString("T.A");
                await bot.WaitForMacroAsync(ct);
            }
            throw new InvalidOperationException(
                $"Not in the field after {FieldCheckAttempts} start-menu checks; last start menu {lastMenu}.");
        }

        /// <summary>
        /// FR-9: one in-game save, then the SaveRAM is flushed and read once a second until a whole
        /// block carries counters above <paramref name="previousMax"/>. Returns that save.
        /// </summary>
        private async Task<byte[]> SaveAsync(IBot bot, Service.Pointers.RsPointerService pointers, uint previousMax)
        {
            // Closes a text box first: the menu bytes keep their last values while one is open.
            bot.RunMacroString(CloseTextBoxesMacro);
            await bot.WaitForMacroAsync(CancellationToken.None);
            await Task.Delay(CloseTextBoxesMs);

            RsStartMenuService.MenuState menu = await OpenLiveStartMenuAsync(bot, pointers, CancellationToken.None);
            RsStartMenuService.StepsTo(menu, config.SaveMenuActionId);
            await MoveCursorToAsync(bot, pointers, menu.IndexOf(config.SaveMenuActionId));
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.RS, config.SaveMacro));
            await bot.WaitForMacroAsync(CancellationToken.None);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(config.SaveTimeoutMs);
            string lastSeen = "no read yet";
            while (true)
            {
                await bot.FlushSave();
                byte[]? save = null;
                try
                {
                    // Shared with EmuHawk, which may be writing or replacing the file right now.
                    using var stream = new FileStream(paths.HuntSave, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    save = new byte[stream.Length];
                    stream.ReadExactly(save);
                }
                catch (IOException ex)
                {
                    lastSeen = ex.Message;
                    Console.WriteLine($"[Wishmaker] Save poll: read failed mid-write ({ex.Message}).");
                }

                if (save is not null && save.Length < FlashSize)
                {
                    lastSeen = $"a {save.Length}-byte file";
                    Console.WriteLine($"[Wishmaker] Save poll: skipped {lastSeen}, mid-write.");
                }
                else if (save is not null)
                {
                    if (Gen3SaveFile.HasCompleteBlockAfter(save, previousMax))
                    {
                        bot.RunMacroString(DismissSavedMacro);
                        await bot.WaitForMacroAsync(CancellationToken.None);
                        await Task.Delay(DismissSavedMs);
                        return save;
                    }
                    lastSeen = Counters(save);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        $"In-game save: no complete block with counters above {previousMax} within " +
                        $"{config.SaveTimeoutMs} ms; last seen {lastSeen}.");
                }
                await Task.Delay(SavePollMs);
            }
        }

        /// <summary>
        /// Opens the start menu and proves it live by moving its cursor; returns the menu as read
        /// after the probe. B closes a menu that did not prove live before the next Start press.
        /// </summary>
        private async Task<RsStartMenuService.MenuState> OpenLiveStartMenuAsync(
            IBot bot, Service.Pointers.RsPointerService pointers, CancellationToken ct)
        {
            // The menu bytes also keep their last values after the menu closes, so a Start press the
            // game ate still reads as "open with SAVE". Only a cursor that moves proves the menu live.
            RsStartMenuService.MenuState menu = await startMenu.ReadAsync(bot, pointers);
            bool live = false;
            for (int attempt = 1; attempt <= OpenStartMenuAttempts && !live; attempt++)
            {
                bot.RunMacroString(OpenStartMenuMacro);
                await bot.WaitForMacroAsync(ct);
                await Task.Delay(OpenStartMenuMs, ct);
                menu = await WaitForStartMenuAsync(bot, pointers, ct);
                if (IsOpenWithSave(menu))
                {
                    bot.RunMacroString(menu.CursorPos == menu.Count - 1 ? "H.Up.4;W.8" : "H.Down.4;W.8");
                    await bot.WaitForMacroAsync(ct);
                    DateTime probeDeadline = DateTime.UtcNow.AddMilliseconds(CursorProbeMs);
                    RsStartMenuService.MenuState probed = await startMenu.ReadAsync(bot, pointers);
                    while (probed.CursorPos == menu.CursorPos && DateTime.UtcNow < probeDeadline)
                    {
                        await Task.Delay(MenuPollMs, ct);
                        probed = await startMenu.ReadAsync(bot, pointers);
                    }
                    live = probed.CursorPos != menu.CursorPos;
                    menu = probed;
                }
                if (!live)
                {
                    // Not open: B is harmless in the field and closes the menu if it opened late.
                    bot.RunMacroString(CloseStartMenuMacro);
                    await bot.WaitForMacroAsync(ct);
                    await Task.Delay(CloseStartMenuMs, ct);
                }
            }
            if (!live)
            {
                throw new InvalidOperationException(
                    $"The start menu did not open after {OpenStartMenuAttempts} Start presses (the cursor never " +
                    $"moved); last start menu {menu.Describe()}.");
            }
            return menu;
        }

        /// <summary>
        /// One verified press at a time, as <see cref="StarterHuntEvent"/> does: a swallowed press
        /// costs a retry instead of A on the wrong entry.
        /// </summary>
        private async Task MoveCursorToAsync(IBot bot, Service.Pointers.RsPointerService pointers, int target)
        {
            const int MaxPresses = 24;
            for (int presses = 0; presses < MaxPresses; presses++)
            {
                RsStartMenuService.MenuState menu = await startMenu.ReadAsync(bot, pointers);
                if (menu.CursorPos == target)
                {
                    return;
                }
                bot.RunMacroString(menu.CursorPos < target ? "T.Down;W.15" : "T.Up;W.15");
                await bot.WaitForMacroAsync(CancellationToken.None);
                await Task.Delay(MenuPollMs);
            }
            RsStartMenuService.MenuState final = await startMenu.ReadAsync(bot, pointers);
            throw new InvalidOperationException(
                $"Start-menu cursor would not settle on position {target} after {MaxPresses} presses " +
                $"({final.Describe()}). Refusing to press A on whatever is highlighted.");
        }

        /// <summary>Polls up to <see cref="FieldCheckMs"/> for the start menu; returns the last read either way.</summary>
        private async Task<RsStartMenuService.MenuState> WaitForStartMenuAsync(
            IBot bot, Service.Pointers.RsPointerService pointers, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(FieldCheckMs);
            RsStartMenuService.MenuState menu = await startMenu.ReadAsync(bot, pointers);
            while (!IsOpenWithSave(menu) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(MenuPollMs, ct);
                menu = await startMenu.ReadAsync(bot, pointers);
            }
            return menu;
        }

        private bool IsOpenWithSave(RsStartMenuService.MenuState menu) =>
            menu.Count is >= 1 and <= 10 && menu.IndexOf(config.SaveMenuActionId) >= 0;

        /// <summary>
        /// The Dolphin half (FR-12 to FR-16): the disc gets a copy of the hunt save, and the save it
        /// writes back is copied to the result SaveRAM before Dolphin and the pad close.
        /// </summary>
        private async Task RunDiscAsync(CancellationToken ct)
        {
            // Created first, so it is listening when Dolphin's DSU client looks for it.
            await using var pad = new DolphinPad(config.DsuPort);
            await using var launcher = new DolphinLauncher(settings.DolphinBaseDirectory, settings.ColosseumBonusDiscPath);
            await launcher.LaunchAsync(paths.HuntSave, paths.HuntRom, TimeSpan.FromMilliseconds(config.LaunchReadyTimeoutMs), ct);

            await BonusDisc.DriveGiftAsync(
                launcher.Memory.ReadByte,
                (button, c) => pad.PressAsync(button, BonusDisc.HoldMs, c),
                c => new DolphinGbaWindow(launcher.ProcessId).ResetAsync(TimeSpan.FromMilliseconds(config.DiscPromptTimeoutMs), c),
                launcher.GbaSavePath,
                config.DiscMenuTimeoutMs / BonusDisc.RightIntervalMs,
                config.DiscMenuTimeoutMs / BonusDisc.PlayIntervalMs,
                config.DiscPromptTimeoutMs / BonusDisc.PromptIntervalMs,
                TimeSpan.FromMilliseconds(config.TransferTimeoutMs),
                ct);

            File.Copy(launcher.GbaSavePath, paths.ResultSave, overwrite: true);
        }

        /// <summary>
        /// FR-17: EmuHawk on the result continues, opens POKéMON, the last party member and SUMMARY,
        /// holds the summary on view, and only then reads the party from RAM, checks it against the
        /// prediction and logs it. Not shiny: B back to the field. Shiny: parks on this EmuHawk (FR-18).
        /// The bot is disposed on every way out, the park's Cancel included.
        /// </summary>
        private async Task<PokemonData> ShowAndReadResultAsync(GameState state, ushort seed, uint predicted, CancellationToken ct)
        {
            // The header stays the original's: the suffixed ROM is a byte copy of it.
            var context = botFactory.BuildGameContext(gameName, -1, position);
            context.SourceGameName = gameName + ResultSuffix;
            IBot bot = botFactory.CreateBot(context);
            try
            {
                var pointers = (Service.Pointers.RsPointerService)bot.Addresses;
                await bot.LaunchAsync();
                await WaitUntilReadyAsync(bot, pointers, ct);
                await ReachFieldAsync(bot, pointers, ct);

                // Steers the presses only; the logged value is read after the summary is on view.
                byte count = await bot.ReadByteAsync(pointers.Anchor);
                RsStartMenuService.MenuState menu = await OpenLiveStartMenuAsync(bot, pointers, ct);
                RsStartMenuService.StepsTo(menu, PokemonMenuActionId);
                await MoveCursorToAsync(bot, pointers, menu.IndexOf(PokemonMenuActionId));
                await PressAsync(bot, OpenPartyMacro, OpenPartyMs, ct);
                for (int slot = 1; slot < count; slot++)
                {
                    await PressAsync(bot, PartyDownMacro, PartyDownMs, ct);
                }
                await PressAsync(bot, OpenSlotMenuMacro, OpenSlotMenuMs, ct);
                await PressAsync(bot, OpenSummaryMacro, OpenSummaryMs, ct);
                Console.WriteLine("[Wishmaker] summary on view");
                await Task.Delay(SummaryDisplayMs, ct);

                PokemonState party = await reader.GetPartyState(bot);
                // The gift lands in the first free slot, which is the last one filled.
                PokemonData jirachi = party.PartyPokemon[party.PartySize - 1]!;
                if (jirachi.Species != JirachiSpecies)
                {
                    throw new InvalidOperationException(
                        $"Jirachi not in the RAM party: party size {party.PartySize}, slot {party.PartySize - 1} is species {jirachi.Species}.");
                }
                if (jirachi.PID != predicted)
                {
                    throw new InvalidOperationException(
                        $"Jirachi PID {jirachi.PID:X8} is not the {predicted:X8} that seed {seed:X4} predicts: " +
                        "the disc read a different save than the hunt wrote. Stopping.");
                }
                Console.WriteLine($"[Wishmaker] predicted {predicted:X8} got {jirachi.PID:X8}{(jirachi.IsShiny ? " SHINY" : "")}");

                await persistence.AddEncounterAsync(new PersistPokemonData
                {
                    PokemonData = jirachi,
                    Game = GameFlavor.RS,
                    Phase = phaseName,
                    GameFileName = gameName,
                    DateTime = DateTime.UtcNow,
                });

                if (jirachi.IsShiny)
                {
                    await ParkShinyAsync(state, jirachi, ct);
                }
                await PressAsync(bot, CloseToFieldMacro, CloseToFieldMs, ct);
                return jirachi;
            }
            finally
            {
                await bot.DisposeAsync();
            }
        }

        /// <summary>One held press and the wait after it, as the save's presses run.</summary>
        private static async Task PressAsync(IBot bot, string macro, int waitMs, CancellationToken ct)
        {
            bot.RunMacroString(macro);
            await bot.WaitForMacroAsync(ct);
            await Task.Delay(waitMs, ct);
        }

        /// <summary>
        /// FR-18: record and announce the catch, leave the EmuHawk of FR-17 at the summary for the
        /// operator, and park in Waiting until Cancel, which closes that EmuHawk.
        /// </summary>
        private async Task ParkShinyAsync(GameState state, PokemonData jirachi, CancellationToken ct)
        {
            var caught = new PersistPokemonData
            {
                PokemonData = jirachi,
                Game = GameFlavor.RS,
                Phase = phaseName,
                GameFileName = gameName + ResultSuffix,
                DateTime = DateTime.UtcNow,
            };
            await persistence.AddCaughtPokemonAsync(caught);
            await alerts.NotifyTargetFoundAsync(caught);

            state.TargetPokemon = caught;
            state.State.Push(EventState.Waiting);
            Console.WriteLine(
                $"[Wishmaker] SHINY Jirachi PID {jirachi.PID:X8} in {paths.ResultSave}, open in EmuHawk at its summary. Cancel to close it.");
            await Task.Delay(Timeout.Infinite, ct);
        }

        private static uint MaxLiveCounter(byte[] save) =>
            Gen3SaveFile.ReadSections(save, Gen3SaveFile.LiveBlockOffset(save)!.Value).Max(s => s.Counter);

        /// <summary>Both blocks' section counters for a timeout message; x marks a bad checksum.</summary>
        private static string Counters(byte[] save) =>
            string.Join("; ", new[] { 0x0000, 0xE000 }.Select(block =>
                $"block 0x{block:X4} [{string.Join(",", Gen3SaveFile.ReadSections(save, block).Select(s => $"{s.Counter}{(s.Valid ? "" : "x")}"))}]"));

        /// <summary>
        /// The hunt save and ROM, and the suffixed pair that holds the result (FR-11b). The fork names
        /// SaveRAM after the ROM file, so EmuHawk on the suffixed ROM opens the result and never the hunt save.
        /// </summary>
        internal static (string HuntSave, string ResultSave, string HuntRom, string ResultRom) PathsFor(
            string romDirectory, string bizHawkDirectory, string gameName, string extension)
        {
            string saveRam = Path.Combine(bizHawkDirectory, "GBA", "SaveRAM");
            return (
                Path.Combine(saveRam, $"{gameName}.SaveRAM"),
                Path.Combine(saveRam, $"{gameName}{ResultSuffix}.SaveRAM"),
                $"{romDirectory}{gameName}{extension}",
                $"{romDirectory}{gameName}{ResultSuffix}{extension}");
        }

        /// <summary>The FR-5 refusals, one sentence each, in the order the operator would fix them.</summary>
        internal static void CheckSetup(string huntSave, string dolphinExe, string biosPath, string isoPath)
        {
            if (!File.Exists(huntSave))
            {
                throw new ArgumentException($"No save at {huntSave}: start the game in BizHawk and save once before hunting Jirachi.");
            }

            byte[] save = File.ReadAllBytes(huntSave);
            if (save.Length < FlashSize)
            {
                throw new ArgumentException($"The save at {huntSave} is {save.Length} bytes, shorter than a {FlashSize}-byte Gen 3 save.");
            }

            int? live = Gen3SaveFile.LiveBlockOffset(save);
            if (live is null || !Gen3SaveFile.ReadSections(save, live.Value).All(s => s.Valid))
            {
                throw new ArgumentException($"The save at {huntSave} has no complete save block: save again in-game before hunting Jirachi.");
            }

            if (!Gen3SaveFile.HasPokedex(save))
            {
                throw new ArgumentException($"The save at {huntSave} has no Pokédex yet: the Bonus Disc needs one.");
            }

            if (Gen3SaveFile.ReadPartyCount(save) == 6)
            {
                throw new ArgumentException($"The save at {huntSave} has a full party: leave a slot free for Jirachi.");
            }

            if (!File.Exists(dolphinExe))
            {
                throw new ArgumentException($"Dolphin is not at {dolphinExe}: check DolphinBaseDirectory.");
            }
            if (!File.Exists(biosPath))
            {
                throw new ArgumentException($"The GBA BIOS is not at {biosPath}: check GbaBiosPath.");
            }
            if (!File.Exists(isoPath))
            {
                throw new ArgumentException($"The Colosseum Bonus Disc is not at {isoPath}: check ColosseumBonusDiscPath.");
            }
        }

        /// <summary>
        /// Whether the block at 0x0000 holds a newer save than before (FR-9). The disc seeds Jirachi
        /// from that block, so a save that landed only in the second block would repeat the last Jirachi.
        /// </summary>
        internal static bool FirstBlockAdvanced(byte[] before, byte[] after)
        {
            return Gen3SaveFile.ReadSections(after, 0).Max(s => s.Counter) > Gen3SaveFile.ReadSections(before, 0).Max(s => s.Counter);
        }
    }
}
