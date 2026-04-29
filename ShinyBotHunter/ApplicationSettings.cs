namespace ShinyBotHunter
{
    public class ApplicationSettings
    {
        public Dictionary<string, int> MaxEmulators { get; set; } = null!;
        public string ROMBaseDirectory { get; set; } = null!;
        public Dictionary<string, int> DisplayWidth { get; set; } = null!;
        public Dictionary<string, int> DisplayHeight { get; set; } = null!;
        public string SpriteBaseDirectory { get; set; } = null!;
        public string MongoDbUrl { get; set; } = null!;
        public string MongoDbDatabase { get; set; } = null!;
        public string InfluxDbUrl { get; set; } = null!;
        public string InfluxDbDatabase { get; set; } = null!;
    }
}
