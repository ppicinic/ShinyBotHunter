using Microsoft.Extensions.Options;
using ShinyBotHunter.Context;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Bot
{
    public class BotFactory
    {
        private ApplicationSettings applicationSettings;
        private IServiceProvider serviceProvider;
        private RomInfoService romInfoService;
        private GameProfiles profiles;
        public BotFactory(IOptions<ApplicationSettings> appSettings, IServiceProvider provider, RomInfoService romInfoService, GameProfiles profiles)
        {
            applicationSettings = appSettings.Value;
            serviceProvider = provider;
            this.romInfoService = romInfoService;
            this.profiles = profiles;
        }
        public virtual IBot CreateBot(GameContext context)
        {
            GameProfile profile = profiles.Get(context.GameFlavor);
            IPointerService addresses = profile.Addresses(context.Revision);
            var apiHawkLauncher = new BizHawkApiHawkLauncher(context, serviceProvider);
            ICommandClient commandClient = new BizHawkCommandClient(port: apiHawkLauncher.CommandPort, console: context.ConsoleType);
            IMemoryClient memoryClient = new BizHawkMemoryClient(port: apiHawkLauncher.MemoryPort);
            IDataReader? reader = CreateReader(context, memoryClient, addresses);
            return new Bot(apiHawkLauncher, commandClient, memoryClient, reader, profile, addresses, context);
        }

        /// <summary>
        /// Builds the per-emulator context. <paramref name="sourceGame"/> is a file handle, so the
        /// game it refers to is resolved from that ROM's header rather than assumed — an unidentified
        /// ROM still launches, but leaves <see cref="GameContext.Rom"/> null so flavor-dependent
        /// callers can refuse it.
        /// </summary>
        public virtual GameContext BuildGameContext(string sourceGame, int gameId, int displayPosition, bool? soundOn = null)
        {
            RomInfo? rom = romInfoService.GetByHandle(sourceGame);
            ConsoleType console = rom?.Console ?? ConsoleType.DS;
            string key = console.ToString();

            return new GameContext()
            {
                SourceGameName = sourceGame,
                GameId = gameId,
                DisplayPosition = displayPosition,
                // Deliberately below Windows' ephemeral range, which starts at 49152. The old base of
                // 50100 sat inside it, so any program that opened a network connection could be
                // handed one of these ports first -- pgAdmin and Spotify both did -- and ApiHawk's
                // HttpListener then failed to start with a sharing violation on a random emulator.
                // Reserving the old range is not the answer: an administered exclusion blocks
                // http.sys from registering a prefix at all, which locks out the emulators too.
                MemoryPort = 20001 + (displayPosition * 2),
                CommandPort = 20000 + (displayPosition * 2),
                Width = Setting(applicationSettings.DisplayWidth, key),
                Height = Setting(applicationSettings.DisplayHeight, key),
                WidthOffset = Setting(applicationSettings.WidthOffset, key),
                Rows = Setting(applicationSettings.DisplayRows, key),
                SoundOn = soundOn is not null ? soundOn.Value : false,
                Rom = rom,
                ConsoleType = console,
                GameFlavor = rom?.Flavor ?? GameFlavor.HGSS,
            };
        }

        /// <summary>
        /// Battle reads are per game, so the reader follows the ROM's flavor. RS has none, so its bot
        /// gets no reader and a battle read throws. An unrecognised flavor gets the HGSS reader rather
        /// than nothing, because a bot still has to be launchable for macro recording -- reads are what
        /// fail, and they fail loudly.
        /// </summary>
        private static IDataReader? CreateReader(GameContext context, IMemoryClient memoryClient, IPointerService addresses) => context.GameFlavor switch
        {
            GameFlavor.RS => null,
            _ => new HgssDataReader(memoryClient, (HgssPointerService)addresses),
        };

        private static int Setting(Dictionary<string, int> settings, string consoleKey)
        {
            if (!settings.TryGetValue(consoleKey, out int value))
                throw new InvalidOperationException(
                    $"appsettings is missing a '{consoleKey}' entry for a per-console display setting.");
            return value;
        }
    }
}
