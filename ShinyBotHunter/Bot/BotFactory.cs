using ShinyBotHunter.Context;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Bot
{
    public class BotFactory
    {
        public IBot CreateBot(GameContext context)
        {
            ILauncher launcher = new BizHawkLauncher(context /* pull maxemulators from appsettings */);
            ICommandClient commandClient = new BizHawkCommandClient(context);
            IMemoryClient memoryClient = new BizHawkMemoryClient(port: context.MemoryPort);
            return new Bot(launcher, commandClient, memoryClient);
        }

        public GameContext BuildGameContext(string sourceGame, int gameId, int displayPosition)
        {
            return new GameContext()
            {
                SourceGameName = sourceGame,
                GameId = gameId,
                DisplayPosition = displayPosition,
                MemoryPort = 50101 + (displayPosition * 2),
                CommandPort = 50100 + (displayPosition * 2)
            };
        }
    }
}
