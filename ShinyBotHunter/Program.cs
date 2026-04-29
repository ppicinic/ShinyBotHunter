
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;

namespace ShinyBotHunter
{
    public class Program
    {
        public static async Task  Main(string[] args)
        {

            var startup = new Startup();
            await startup.MainAsync(args);
        }
    }
}
