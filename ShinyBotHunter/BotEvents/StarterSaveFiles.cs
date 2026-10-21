using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    public class StarterSaveFiles
    {
        private readonly IBot bot;
        private readonly GameContext context;
        private readonly StarterHuntEntry entry;
        private readonly StarterHuntConfig config;
        private readonly StarterHuntStateStore stateStore;
        private readonly string romDirectory;
        private readonly string bizHawkDirectory;

        public StarterSaveFiles(IBot bot, GameContext context, StarterHuntEntry entry, StarterHuntConfig config, StarterHuntStateStore stateStore, string romDirectory, string bizHawkDirectory)
        {
            this.bot = bot;
            this.context = context;
            this.entry = entry;
            this.config = config;
            this.stateStore = stateStore;
            this.romDirectory = romDirectory;
            this.bizHawkDirectory = bizHawkDirectory;
        }

        /// <summary>
        /// Creates the save this hunt resets back to, if the per-instance ROM copy does not exist yet.
        /// Each emulator gets its own copy so each gets its own save file.
        /// </summary>
        public async Task EnsureSaveExistsAsync(Func<CancellationToken, Task> launchAndWaitReady, CancellationToken cancellationToken)
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

                await launchAndWaitReady(cancellationToken);
                bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.RS, config.CreateSaveMacro));
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
        public string SaveRamFile => Path.Combine(SaveRamDirectory, $"{context.GameName}.SaveRAM");

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
    }
}
