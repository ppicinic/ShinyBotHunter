using Microsoft.Extensions.Options;
using ShinyBotHunter.Context;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Bot
{
    public class BotFactory
    {
        private ApplicationSettings applicationSettings;
        private IServiceProvider serviceProvider;
        public BotFactory(IOptions<ApplicationSettings> appSettings, IServiceProvider provider)
        {
            applicationSettings = appSettings.Value;
            serviceProvider = provider;
        }
        public IBot CreateBot(GameContext context)
        {
            ILauncher launcher = new BizHawkLauncher(context, serviceProvider);
            ICommandClient commandClient = new BizHawkCommandClient(context);
            IMemoryClient memoryClient = new BizHawkMemoryClient(port: context.MemoryPort);
            return new Bot(launcher, commandClient, memoryClient);
        }

        public GameContext BuildGameContext(string sourceGame, int gameId, int displayPosition, bool? soundOn = null)
        {
            return new GameContext()
            {
                SourceGameName = sourceGame,
                GameId = gameId,
                DisplayPosition = displayPosition,
                MemoryPort = 50101 + (displayPosition * 2),
                CommandPort = 50100 + (displayPosition * 2),
                Width = applicationSettings.DisplayWidth["DS"],
                Height = applicationSettings.DisplayHeight["DS"],
                WidthOffset = applicationSettings.WidthOffset["DS"],
                SoundOn = soundOn is not null ? soundOn.Value : false,
            };
        }
    }
}
