using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.BotEvents
{
    public class CreateSaveEvent : IBotEvent
    {

        private string romDir;
        private IBot bot;
        private GameContext context;
        public CreateSaveEvent(IBot bot, GameContext context, IServiceProvider provider)
        {
            IOptions<ApplicationSettings> appSettings = provider.GetRequiredService<IOptions<ApplicationSettings>>();
            romDir = appSettings.Value.ROMBaseDirectory;
            this.bot = bot;
            this.context = context;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists($"{romDir}{context.GameName}.nds"))
            {
                File.Copy($"{romDir}{context.SourceGameName}.nds", $"{romDir}{context.GameName}.nds");
            }
            await bot.LaunchAsync();
            await Task.Delay(10000);
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(context.GameFlavor, "CreateNewSave"));
            await bot.WaitForMacroAsync(cancellationToken);
            await Task.Delay(10000); // safety sleep for save before closing emulator
            await bot.DisposeAsync();
        }

    }
}
