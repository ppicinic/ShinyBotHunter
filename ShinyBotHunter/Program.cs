
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
        public static async Task Main(string[] args)
        {
            //MultiShinyHunterEvent e = new MultiShinyHunterEvent();
            var startup = new Startup();
            await startup.MainAsync();
            //ICommandClient commandClient = new BizHawkCommandClient();
            //commandClient.Connect();
            //Console.ReadKey();
            //commandClient.Tap("Left");
            //await commandClient.WaitForMacroAsync();
            //await Task.Delay(10000);
            //await commandClient.DisposeAsync();
            //string macro = MacroRegistry.Instance.GetMacro(MacroFlavor.HGSS, "SaveStarter");
            //Console.WriteLine(macro);
            //commandClient.RunMacroString(macro);
            //await commandClient.WaitForMacroAsync();
            //await Task.Delay(5000);
            //await commandClient.DisposeAsync();
            //var data = await e.ExecuteAsync();
            //foreach(var pokemon in data)
            //{
            //    Console.WriteLine($"{pokemon.Game}{pokemon.GameId}: {pokemon.PokemonData.Species}");
            //}
            //while(tasks.Count > 0)
            //{
            //    Task<IBot> completedTask = await Task.WhenAny(tasks);
            //    try
            //    {
            //        IBot bot = await completedTask;
            //        await bot.DisposeAsync();
            //    }catch { }
            //}
            //var builder = WebApplication.CreateBuilder(args);

            //// Add services to the container.

            //builder.Services.AddControllers();
            //// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            //builder.Services.AddOpenApi();

            //var app = builder.Build();

            //// Configure the HTTP request pipeline.
            //if (app.Environment.IsDevelopment())
            //{
            //    app.MapOpenApi();
            //}

            //app.UseHttpsRedirection();

            //app.UseAuthorization();


            //app.MapControllers();

            //app.Run();
        }
    }
}
