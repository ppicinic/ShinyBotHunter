using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Persistence.InfluxDb;
using ShinyBotHunter.Persistence.MongoDb;
using MongoDB.Driver;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using System.Text;
using Grpc.Net.Client.Balancer;

namespace ShinyBotHunter
{
    public class Startup
    {
        public async Task MainAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddSignalR();
            CreateServices(builder.Services);
            var app = builder.Build();
            var services = app.Services;
            app.MapControllers().AsRequiredPort(5000);
            app.MapHub<ElectronHub>("/socket-api").AsRequiredPort(5001);

            services.GetRequiredService<InfluxDbEncounterDao>().Init();
            await services.GetRequiredService<PokemonMetadataService>().InitAsync();
            services.GetRequiredService<PhaseInfoService>().Init();
            services.GetRequiredService<NotificationService>().Init();
            services.GetRequiredService<MongoCaughtPokemonDao>().Init();
            services.GetRequiredService<PokemonPersistenceService>().Init();
            services.GetRequiredService<BotService>().Init();


            var _ = Task.Run(async () =>
            {
                await app.RunAsync();
            });
            await Task.Delay(5000);
            Console.ReadLine();
            //var botFactory = services.GetRequiredService<BotFactory>();
            //var bot = botFactory.CreateBot(botFactory.BuildGameContext("Pokemon SoulSilver", -1, 1));
            //await bot.LaunchAsync();
            //Console.WriteLine("Press to check direction");


            //char c = Console.ReadKey().KeyChar;
            //bot.RunMacroString("W.240");
            //await bot.WaitForMacroAsync();
            //c = Console.ReadKey().KeyChar;
            //while (c != 'c')
            //{
            //    uint anchorRaw = await bot.ReadUIntAsync(0x1D4158);
            //    uint anchor = anchorRaw - 0x02000000;
            //    uint facingPtr = anchor + 0x1DC4;
            //    Queue<byte> directions = new Queue<byte>();
            //    directions.Enqueue(0);
            //    directions.Enqueue(2);
            //    directions.Enqueue(1);
            //    directions.Enqueue(3);
            //    byte facing = await bot.ReadByteAsync(facingPtr);
            //    while (facing != directions.Peek())
            //    {
            //        directions.Enqueue(directions.Dequeue());
            //    }
            //    directions.Enqueue(directions.Dequeue());
            //    StringBuilder sb = new StringBuilder();

            //    for (int i = 0; i < 4; i++)
            //    {
            //        if (i != 0)
            //        {
            //            sb.Append(";");
            //        }
            //        sb.Append("T.");
            //        byte dir = directions.Dequeue();
            //        if (dir == 0)
            //        {
            //            sb.Append("Up");
            //        }
            //        else if (dir == 1)
            //        {
            //            sb.Append("Down");
            //        }
            //        else if (dir == 2)
            //        {
            //            sb.Append("Left");
            //        }
            //        else if (dir == 3)
            //        {
            //            sb.Append("Right");
            //        }
            //        sb.Append(";W.5");
            //        directions.Enqueue(dir);
            //    }
            //    Console.WriteLine($"Running repeat macro: {sb.ToString()}");
            //    bot.RunRepeatMacro(sb.ToString());

            //    uint battleIndicator = 0x21E76D2 - 0x02000000;
            //    var battleValue = await bot.ReadByteAsync(battleIndicator);
            //    while (!(battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0))
            //    {
            //        Console.WriteLine(battleValue);
            //        await Task.Delay(166); // sleep 10 frames before re-polling memory -
            //                               // this is semi dangerous we don't know how slow the memory read is and we want to read on ever 10 frames
            //                               // regardless of the delay, so real implementition needs a better solution
            //        battleValue = await bot.ReadByteAsync(battleIndicator);
            //    }
            //    bot.ClearMacro();
            //    Console.WriteLine("Found a battle. Macro stopped.");
            //    anchorRaw = await bot.ReadUIntAsync(0x1D4158);
            //    anchor = anchorRaw - 0x02000000;
            //    uint foeAnchorPtr = anchor + 0x6930;
            //    uint foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr) - 0x02000000;
            //    uint foeCountPtr = foeAnchor + 0xC14;
            //    var foeCount = await bot.ReadByteAsync(foeCountPtr);
            //    while (foeCount == 0)
            //    {
            //        Console.WriteLine(foeCount);
            //        await Task.Delay(166);
            //        anchorRaw = await bot.ReadUIntAsync(0x1D4158);
            //        anchor = anchorRaw - 0x02000000;
            //        foeAnchorPtr = anchor + 0x6930;
            //        foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr) - 0x02000000;
            //        foeCountPtr = foeAnchor + 0xC14;
            //        foeCount = await bot.ReadByteAsync(foeCountPtr);
            //    }
            //    HgssPokemonParser parser = services.GetRequiredService<HgssPokemonParser>();
            //    PokemonData? pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            //    if (pokemon is null)
            //    {
            //        Console.WriteLine("read pokemon too fast");
            //    }
            //    else
            //    {
            //        Console.WriteLine($"{pokemon.Name}, isShiny: {pokemon.IsShiny}");
            //    }
            //    // tap run
            //    bot.RunRepeatMacro("T.B;W.5");
            //    anchorRaw = await bot.ReadUIntAsync(0x1D4158);
            //    anchor = anchorRaw - 0x02000000;
            //    var battleMenuStatePtr = anchor + 0x230EC;
            //    byte battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            //    while (battleMenuState != 1)
            //    {
            //        await Task.Delay(166);
            //        anchorRaw = await bot.ReadUIntAsync(0x1D4158);
            //        anchor = anchorRaw - 0x02000000;
            //        battleMenuStatePtr = anchor + 0x230EC;
            //        battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            //    }
            //    bot.ClearMacro();
            //    Console.WriteLine($"Found battle menu, stopping pressing B");
            //    await bot.WaitForMacroAsync();
            //    bot.RunMacroString("W.20;T.Down;W.5;T.Left;W.5;T.Left;W.5;T.Right;W.5;T.A;");
            //    await bot.WaitForMacroAsync();
            //    Console.WriteLine("Macro to run done");
            //    bot.RunRepeatMacro("T.B;W.5");
            //    battleIndicator = 0x21E76D2 - 0x02000000;
            //    battleValue = await bot.ReadByteAsync(battleIndicator);
            //    while (battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0)
            //    {
            //        Console.WriteLine(battleValue);
            //        await Task.Delay(166); // sleep 10 frames before re-polling memory -
            //                               // this is semi dangerous we don't know how slow the memory read is and we want to read on ever 10 frames
            //                               // regardless of the delay, so real implementition needs a better solution
            //        battleValue = await bot.ReadByteAsync(battleIndicator);
            //    }
            //    bot.ClearMacro();
            //    await bot.WaitForMacroAsync();
            //    Console.WriteLine("W 4 seconds");
            //    bot.RunMacroString("W.120");
            //    await bot.WaitForMacroAsync();
            //    Console.WriteLine("Wait complete.");
            //}
            //Console.ReadKey();
            //await bot.DisposeAsync();
            //await using (var scope = services.CreateAsyncScope())
            //{
            //    MultiShinyHunterEvent e = scope.ServiceProvider.GetRequiredService<MultiShinyHunterEvent>();
            //    var data = await e.ExecuteAsync();
            //    foreach (var pokemon in data)
            //    {
            //        Console.WriteLine($"{pokemon.Game}{pokemon.GameId}: {pokemon.PokemonData.Species}");
            //    }
            //}

            await Task.Delay(5000);

            await services.GetRequiredService<PokemonPersistenceService>().DisposeAsync();
            await services.GetRequiredService<InfluxDbEncounterDao>().DisposeAsync();
            await services.GetRequiredService<MongoCaughtPokemonDao>().DisposeAsync();
            services.GetRequiredService<MongoDbConnection>().Dispose();
        }



        public void CreateServices(IServiceCollection services)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddUserSecrets<Startup>()
                .Build();
            var provider = services
                .Configure<ApplicationSettings>(config.GetSection("ApplicationSettings"))
                .AddSingleton<IConfiguration>(config)
                .AddTransient<MultiShinyHunterEvent>()
                .AddSingleton<BotFactory>()
                .AddSingleton<BizHawkReaderWriterLock>()
                .AddSingleton<MongoDbMetadataDao>()
                .AddSingleton<PokemonMetadataService>()
                .AddSingleton<HgssPokemonParser>()
                .AddSingleton<HgssShinyStarterEventFactory>()
                .AddSingleton<PokemonPersistenceService>()
                .AddSingleton<InfluxDbEncounterDao>()
                .AddSingleton<MongoDbConnection>()
                .AddSingleton<MongoCaughtPokemonDao>()
                .AddSingleton<PhaseInfoService>()
                .AddSingleton<AdminService>()
                .AddSingleton<BotService>()
                .AddSingleton<NotificationService>();
        }
    }

    public static class RouteExtensions
    {
        public static TBuilder AsRequiredPort<TBuilder>(this TBuilder builder, int port)
            where TBuilder : IEndpointConventionBuilder
        {
            builder.Add(endpointBuilder =>
            {
                var originalDelegate = endpointBuilder.RequestDelegate;
                endpointBuilder.RequestDelegate = async context =>
                {
                    if (context.Connection.LocalPort != port)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        await context.Response.WriteAsync("Endpoint not available on this port.");
                        return;
                    }
                    await originalDelegate!(context);
                };
            });
            return builder;
        }
    }
}
