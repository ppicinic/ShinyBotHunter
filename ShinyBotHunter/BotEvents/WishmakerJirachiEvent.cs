using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
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
    /// its integrated GBA, the disc writes the Jirachi into the copy, and the copy is parsed. The
    /// Jirachi is a function of the save, so each cycle's PID is checked against the one the save
    /// predicts. The bot never writes save bytes: the hunt save changes only by the game saving.
    /// </summary>
    public class WishmakerJirachiEvent : AbstractBotEvent<PokemonData?>
    {
        public const string ResultSuffix = "-WISHMKR";

        // Below this the save is not a whole 128 KiB flash image (BizHawk adds a 16-byte footer).
        internal const int FlashSize = 131072;

        private readonly string gameName;
        private readonly string phaseName;
        private readonly int position;
        private readonly WishmakerJirachiConfig config;
        private readonly BotFactory botFactory;
        private readonly PokemonPersistenceService persistence;
        private readonly ShinyAlertService alerts;
        private readonly RsStartMenuService startMenu;
        private readonly IPokemonParser parser;
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
            parser = provider.GetRequiredService<GameProfiles>().Get(GameFlavor.RS).Parser;
            settings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;

            paths = PathsFor(settings.ROMBaseDirectory, settings.BizHawkBaseDirectory, gameName, extension);
            // Runs in BotService's Start, so each refusal comes back as a 400 before anything launches.
            CheckSetup(paths.HuntSave, Path.Combine(settings.DolphinBaseDirectory, "Dolphin.exe"),
                settings.GbaBiosPath, settings.ColosseumBonusDiscPath);
        }

        public override Task<PokemonData?> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("The cycle is plan step 3.");
        }

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
