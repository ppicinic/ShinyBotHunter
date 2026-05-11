using CmdScale.EntityFrameworkCore.TimescaleDB;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client.Balancer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Persistence.InfluxDb;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Persistence.PostgreSQL;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts;
using ShinyBotHunter.Service;
using ShinyBotHunter.Services;
using System.Diagnostics;
using System.Text;
using YamlDotNet.Core;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ShinyBotHunter
{
    public class Startup
    {
        public async Task MainAsync(string[] args)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddSignalR();
            CreateServices(builder.Services);
            var app = builder.Build();
            var services = app.Services;
            app.MapControllers().AsRequiredPort(5000);
            app.MapHub<ElectronHub>("/socket-api").AsRequiredPort(5001);

            await services.GetRequiredService<DiscordBotService>().StartAsync();
            await services.GetRequiredService<BizHawkLuaLoader>().InitAsync();
            services.GetRequiredService<IEncounterDao>().Init();
            await services.GetRequiredService<PokemonMetadataService>().InitAsync();
            await services.GetRequiredService<PhaseInfoService>().InitAsync();
            services.GetRequiredService<NotificationService>().Init();
            services.GetRequiredService<MongoCaughtPokemonDao>().Init();
            services.GetRequiredService<PokemonPersistenceService>().Init();
            services.GetRequiredService<BotService>().Init();
            Console.WriteLine(string.Join(", ", args));
            if (args.Length > 0 && args[0].ToLower() == "--parsepokeapi")
            {
                await ParsePokeApi(services);
            }
            else if (args.Length > 0 && args[0].ToLower() == "--migrateinfluxv1tov2")
            {
                await MigrateInfluxDbv1ToV2(services, args);
            }
            else if (args.Length > 0 && args[0].ToLower() == "--migrateinfluxtopostgresql")
            {
                await MigrateInfluxToPostgres(services, args);
            }
            else
            {
                await MainRunnerAsync(app, services);
                //await TestAsync(services);
            }

            await services.GetRequiredService<DiscordBotService>().StopAsync();
            await services.GetRequiredService<PokemonPersistenceService>().DisposeAsync();
            await services.GetRequiredService<IEncounterDao>().DisposeAsync();
            await services.GetRequiredService<MongoCaughtPokemonDao>().DisposeAsync();
            services.GetRequiredService<MongoDbConnection>().Dispose();
        }

        //public 

        private async Task TestAsync(IServiceProvider services)
        {
            var botFactory = services.GetRequiredService<BotFactory>();
            var bot = botFactory.CreateBot(botFactory.BuildGameContext("Pokemon SoulSilver", -1, 0));
            await bot.LaunchAsync();
            await Task.Delay(5000);
            Console.WriteLine("try to continue game");
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "ContinueGame"));
            await bot.WaitForMacroAsync();

            Console.WriteLine("Finished");
            Console.WriteLine("Press a key to read map coordinates");
            char c = Console.ReadKey().KeyChar;
            while(c != 'c')
            {
                var x = await bot.ReadUIntAsync(0x21DA6F4);
                var y = await bot.ReadUIntAsync(0x21DA6FC);
                Console.Write($"x: {to_s16(x)}, y:{to_s16(y)}");
                c = Console.ReadKey().KeyChar;
            }
            string? s = "";
            do
            {
                s = Console.ReadLine();
            } while (s != "end");
            await Task.Delay(1000);
        }

        private short to_s16(uint u32)
        {
            return (short)(((u32 / 65536) + 32768) % 65536 - 32768);
        }

        private async Task MainRunnerAsync(WebApplication app, IServiceProvider services)
        {
            var _ = Task.Run(async () =>
            {
                await app.RunAsync();
            });
            await Task.Delay(5000);

            //var botFactory = services.GetRequiredService<BotFactory>();
            //var bot = botFactory.CreateBot(botFactory.BuildGameContext("Pokemon SoulSilver", -1, 1));
            //await bot.LaunchAsync();
            //await Task.Delay(5000);
            //bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "ContinueGame"));
            //await bot.WaitForMacroAsync();
            //char c = Console.ReadKey().KeyChar;
            ////var parser = services.GetRequiredService<HgssPokemonParser>();
            //var reader = services.GetRequiredService<PokemonStateReader>();

            //for(int x = 0; x < 1000; x++)
            //{
            //    Stopwatch sw = new Stopwatch();
            //    sw.Start();
            //    var state = await reader.GetPokemonState(bot);
            //    sw.Stop();
            //    Console.WriteLine($"{sw.ElapsedMilliseconds / 1000}.{sw.ElapsedMilliseconds % 1000}s");
            //    for(int i = 0; i < state.PartySize; i++)
            //    {
            //        Console.WriteLine($"Party Slot {i+1}: {state.PartyPokemon[i]!.Nickname}");
            //    }
            //    for(int box = 0; box < 18; box++)
            //    {
            //        for(int slot = 0; slot < 30; slot++)
            //        {
            //            var boxPokemon = state.BoxPokemon[box][slot];
            //            if (boxPokemon is not null)
            //            {
            //                Console.WriteLine($"Box {box + 1}, Slot {slot + 1}: {boxPokemon.Nickname}");
            //            }
            //        }
            //    }
            //}
            //await bot.DisposeAsync();
            //var parser = services.GetRequiredService<HgssPokemonParser>();
            //await Task.Delay(5000);
            //WildPokemonEncounterEvent encounterEvent = new WildPokemonEncounterEvent(bot, Macro.GameFlavor.HGSS, "Testing", EncounterType.Spin, services, new PhaseTarget(services, GameFlavor.HGSS, "Testing", bot.GameFileName));
            //CancellationTokenSource cts = new CancellationTokenSource();
            //var _1 = encounterEvent.ExecuteAsync(cts.Token);

            //string? endStr = "";
            //do
            //{
            //    endStr = Console.ReadLine();
            //} while (endStr != "end");
            ////await cts.CancelAsync();
            ////await _1;
            //await bot.DisposeAsync();
            //Console.WriteLine("BizHawk should be closed or close soon");
            string? s = "";
            do
            {
                s = Console.ReadLine();
            } while (s != "end");
            await Task.Delay(1000);
        }

        private async Task ParsePokeApi(IServiceProvider provider)
        {
            await new PokeApiParser(provider.GetRequiredService<IOptions<ApplicationSettings>>()).ScrapePokeApi();
        }

        private async Task MigrateInfluxDbv1ToV2(IServiceProvider provider, string[] args)
        {
            bool delete = false;
            bool dryRun = false;
            string? dryRunTableName = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].ToLower() == "--delete")
                {
                    delete = true;
                }
                else if (args[i].ToLower() == "--dryrun")
                {
                    dryRun = true;
                    i++;
                    dryRunTableName = args[i];
                }
            }
            await new MigrateInfluxSchemav1Tov2(provider, delete, dryRun, dryRunTableName).MigrateAsync();
        }

        private async Task MigrateInfluxToPostgres(IServiceProvider provider, string[] args)
        {
            await new MigrateInfluxToPostgres(provider).MigrateAsync();
        }

        public void CreateServices(IServiceCollection services)
        {
            InteractionServiceConfig interactionServiceConfig = new InteractionServiceConfig()
            {
                DefaultRunMode = RunMode.Async
            };
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddUserSecrets<Startup>()
                .Build();
            var provider = services
                .Configure<ApplicationSettings>(config.GetSection("ApplicationSettings"))
                .AddDbContextFactory<PokemonDbContext>(options =>
                    options.UseNpgsql(config.GetConnectionString("DefaultConnection"))
                    .UseTimescaleDb())
                .AddSingleton<IConfiguration>(config)
                .AddTransient<MultiShinyHunterEvent>()
                .AddSingleton<BotFactory>()
                .AddSingleton<BizHawkReaderWriterLock>()
                .AddSingleton<MongoDbMetadataDao>()
                .AddSingleton<PokemonMetadataService>()
                .AddSingleton<HgssPokemonParser>()
                .AddSingleton<HgssShinyStarterEventFactory>()
                .AddSingleton<PokemonPersistenceService>()
                .AddSingleton<IEncounterDao, PostgreSQLEncounterDao>()
                .AddSingleton<MongoDbConnection>()
                .AddSingleton<MongoCaughtPokemonDao>()
                .AddSingleton<PhaseInfoService>()
                .AddSingleton<AdminService>()
                .AddSingleton<BotService>()
                .AddSingleton<BizHawkLuaLoader>()
                .AddSingleton<EncounterEventTypeFactory>()
                .AddSingleton<PokemonStateReader>()
                .AddSingleton(new DiscordSocketConfig
                {
                    // Safe default for initial bot setup; add privileged intents later if needed.
                    UseInteractionSnowflakeDate = false,
                    AlwaysDownloadUsers = true,
                    GatewayIntents = GatewayIntents.All
                })
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<InteractionService>(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>(), interactionServiceConfig))
                .AddSingleton<DiscordBotService>()
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
