namespace ShinyBotHunter
{
    public class ApplicationSettings
    {
        public Dictionary<string, int> MaxEmulators { get; set; } = null!;
        public string ROMBaseDirectory { get; set; } = null!;
        public string BizHawkBaseDirectory { get; set; } = null!;
        public Dictionary<string, int> DisplayWidth { get; set; } = null!;
        public Dictionary<string, int> DisplayHeight { get; set; } = null!;
        public Dictionary<string, int> WidthOffset { get; set; } = null!;
        public string SpriteBaseDirectory { get; set; } = null!;
        public string MongoDbUrl { get; set; } = null!;
        public string MongoDbDatabase { get; set; } = null!;
        public string InfluxDbUrl { get; set; } = null!;
        public string InfluxDbDatabase { get; set; } = null!;
        public string LuaBaseDirectory { get; set; } = null!;
        public string DiscordToken { get; set; } = null!;
        public ulong? DiscordGuildId { get; set; } = null!;
        public ulong? ShinyLogChannel { get; set; } = null!;
        public ulong? ShinyLogMention { get; set; } = null!;
        public string BizHawkMode { get; set; } = "LuaNet";

        // Dev-only macro recorder. Off by default: when false the /api/dev/* routes 404 and the
        // dashboard hides the tab. Requires BizHawkMode == "ApiHawk".
        public bool EnableMacroRecording { get; set; } = false;
        // A recorded press this long or shorter becomes T.<Btn> (which replays as 3 frames);
        // anything longer keeps its exact count as H.<Btn>.<n>.
        public int MacroRecordTapMaxFrames { get; set; } = 5;
        // Pixel radius within which a recorded touch snaps to a CommandPreprocessor shorthand.
        public int MacroRecordTouchSnapRadius { get; set; } = 6;
        public ulong? DiscordNewMoveChannelId { get; set; }
    }
}
