using ShinyBotHunter.Context;

namespace ShinyBotHunter
{
    public class ApplicationSettings
    {
        public Dictionary<string, int> MaxEmulators { get; set; } = null!;


        /// <summary>Emulator budget for one console. Keys are <see cref="ConsoleType"/> names.</summary>
        public int MaxEmulatorsFor(ConsoleType console)
        {
            if (!MaxEmulators.TryGetValue(console.ToString(), out int value))
                throw new InvalidOperationException(
                    $"appsettings has no MaxEmulators entry for console '{console}'.");
            return value;
        }

        /// <summary>
        /// Total emulators allowed across every console. Display slots and ports are a single pool
        /// shared by all consoles, so the global limits are sized against the sum, not one console.
        /// </summary>
        public int TotalMaxEmulators => MaxEmulators.Values.Sum();
        public string ROMBaseDirectory { get; set; } = null!;
        public string BizHawkBaseDirectory { get; set; } = null!;
        public Dictionary<string, int> DisplayWidth { get; set; } = null!;
        public Dictionary<string, int> DisplayHeight { get; set; } = null!;
        public Dictionary<string, int> WidthOffset { get; set; } = null!;

        /// <summary>
        /// How many rows deep the emulator windows tile, per console. A GBA window is roughly half
        /// the height of a DS one, so the two fit very different grids on the same screen.
        /// </summary>
        public Dictionary<string, int> DisplayRows { get; set; } = null!;
        public string SpriteBaseDirectory { get; set; } = null!;
        public string MongoDbUrl { get; set; } = null!;
        public string MongoDbDatabase { get; set; } = null!;
        public string InfluxDbUrl { get; set; } = null!;
        public string InfluxDbDatabase { get; set; } = null!;
        public string DiscordToken { get; set; } = null!;
        public ulong? DiscordGuildId { get; set; } = null!;
        public ulong? ShinyLogChannel { get; set; } = null!;
        public ulong? ShinyLogMention { get; set; } = null!;

        // Dev-only macro recorder. Off by default: when false the /api/dev/* routes 404 and the
        // dashboard hides the tab.
        public bool EnableMacroRecording { get; set; } = false;
        // A recorded press this long or shorter becomes T.<Btn> (which replays as 3 frames);
        // anything longer keeps its exact count as H.<Btn>.<n>.
        public int MacroRecordTapMaxFrames { get; set; } = 5;
        // Pixel radius within which a recorded touch snaps to a CommandPreprocessor shorthand.
        public int MacroRecordTouchSnapRadius { get; set; } = 6;
        public ulong? DiscordNewMoveChannelId { get; set; }
    }
}
