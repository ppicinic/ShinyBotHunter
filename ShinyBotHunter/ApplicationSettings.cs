namespace ShinyBotHunter
{
    public class ApplicationSettings
    {
        public Dictionary<string, int> MaxEmulators { get; set; } = null!;
        public string ROMBaseDirectory { get; set; } = null!;
        public Dictionary<string, int> DisplayWidth { get; set; } = null!;
        public Dictionary<string, int> DisplayHeight { get; set; } = null!;
    }
}
