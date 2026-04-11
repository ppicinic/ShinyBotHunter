using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.BotEvents
{
    public class CreateSaveEvent : IBotEvent
    {
        private IBot bot;
        private string game;
        private int gameId;
        private string fullGameName;
        private GameFlavor flavor;

        public CreateSaveEvent(IBot bot, GameContext context)
        {
            this.bot = bot;
            this.game = context.SourceGameName;
            this.gameId = context.GameId;
            this.fullGameName = context.GameName;
            this.flavor = context.GameFlavor;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists($"C:\\ROMs\\{this.fullGameName}.nds"))
            {
                File.Copy($"C:\\ROMs\\{this.game}.nds", $"C:\\ROMs\\{this.fullGameName}.nds");
            }
            await bot.LaunchAsync();
            await Task.Delay(10000);
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, "CreateNewSave"));
            await bot.WaitForMacroAsync(cancellationToken);
            await Task.Delay(10000); // safety sleep for save before closing emulator
            await bot.DisposeAsync();
        }

    }
}
