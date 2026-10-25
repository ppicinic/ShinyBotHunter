using Microsoft.Extensions.Configuration;

namespace ShinyBotHunter.Tests
{
    public class ApplicationSettingsTests
    {
        [Fact]
        public void DolphinKeysBindFromAppsettings()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(RepoPaths.AppProject, "appsettings.json"))
                .Build();

            ApplicationSettings settings = configuration.GetSection("ApplicationSettings").Get<ApplicationSettings>()!;

            Assert.Equal("D:/Dolphin/", settings.DolphinBaseDirectory);
            Assert.Equal("D:/Dolphin/gba_bios.bin", settings.GbaBiosPath);
            Assert.Equal("D:/ROMs/colosseum-bonus-disc.iso", settings.ColosseumBonusDiscPath);
        }
    }
}
