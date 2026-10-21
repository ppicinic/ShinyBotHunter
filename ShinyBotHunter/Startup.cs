using CmdScale.EntityFrameworkCore.TimescaleDB;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Persistence.InfluxDb;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Persistence.PostgreSQL;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Services;

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

            bool isParseMode = args.Length > 0 && (args[0].ToLower() == "--parsepokeapi" || args[0].ToLower() == "--parsepokemoveapi");

            await services.GetRequiredService<DiscordBotService>().StartAsync();
            await services.GetRequiredService<BizHawkLuaLoader>().InitAsync();
            services.GetRequiredService<IEncounterDao>().Init();
            // TODO this is jank we should make the modules lazy loaded.
            if (!isParseMode)
            {
                await services.GetRequiredService<PokemonMetadataService>().InitAsync();
                await services.GetRequiredService<PhaseInfoService>().InitAsync();
                await services.GetRequiredService<RomInfoService>().InitAsync();
            }
            services.GetRequiredService<NotificationService>().Init();
            services.GetRequiredService<MongoCaughtPokemonDao>().Init();
            services.GetRequiredService<PokemonPersistenceService>().Init();
            services.GetRequiredService<BotService>().Init();
            Console.WriteLine(string.Join(", ", args));
            if (args.Length > 0 && args[0].ToLower() == "--parsepokeapi")
            {
                await ParsePokeApi(services);
            }
            else if (args.Length > 0 && args[0].ToLower() == "--parsepokemoveapi")
            {
                await new PokeApiParser(services.GetRequiredService<IOptions<ApplicationSettings>>()).ScrapePokeApi();
            }
            else if (args.Length > 0 && args[0].ToLower() == "--migrateinfluxv1tov2")
            {
                await MigrateInfluxDbv1ToV2(services, args);
            }
            else if (args.Length > 0 && args[0].ToLower() == "--migrateinfluxtopostgresql")
            {
                await MigrateInfluxToPostgres(services, args);
            }
            else if(args.Length > 0 && args[0].ToLower() == "--generatemapdatahgss")
            {
                var processor = new MapHeaderProcessor();
                new MatrixProcessor().GenerateAllMaps();
                new MapProcessor().GenerateMapFiles();
                await new GraphProcessor(services).CreateBaseGraph();
            }
            else
            {
                await MainRunnerAsync(app, services);
            }

            await services.GetRequiredService<SmogonDamageService>().DisposeAsync();
            await services.GetRequiredService<DiscordBotService>().StopAsync();
            await services.GetRequiredService<PokemonPersistenceService>().DisposeAsync();
            await services.GetRequiredService<IEncounterDao>().DisposeAsync();
            await services.GetRequiredService<MongoCaughtPokemonDao>().DisposeAsync();
            services.GetRequiredService<MongoDbConnection>().Dispose();
        }

        private async Task MainRunnerAsync(WebApplication app, IServiceProvider services)
        {
            var _ = Task.Run(async () =>
            {
                await app.RunAsync();
            });
            await Task.Delay(5000);

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
                .AddSingleton<RomInfoService>()
                .AddSingleton<AdminService>()
                .AddSingleton<BotService>()
                .AddSingleton<MacroRecordingService>()
                .AddSingleton<LocationService>()
                .AddSingleton<BizHawkLuaLoader>()
                .AddSingleton<PokemonStateReader>()
                .AddSingleton<GraphService>(x => GraphService.Instance)
                .AddSingleton<PathfindingService>()
                .AddSingleton<MapService>(x => MapService.Instance)
                .AddSingleton<SmogonDamageService>()
                .AddTransient<BattleOrchestratorEvent>()
                .AddTransient<NavigatorEvent>()
                .AddSingleton<GameServices>()
                .AddSingleton<LegalityService>()
                .AddSingleton<RsStartMenuService>()
                .AddSingleton<StarterHuntStateStore>()
                // Per-game services, resolved by flavor at the point a bot's game is known rather
                // than named literally at construction. A new flavor is added by registering its
                // three implementations here.
                .AddKeyedSingleton<IPointerService>(GameFlavor.HGSS, new HgssPointerService())
                .AddKeyedSingleton<IBattlePokemonParser>(GameFlavor.HGSS, new HgssBattlePokemonParser())
                .AddKeyedSingleton<IPokemonParser>(GameFlavor.HGSS, (sp, _) => sp.GetRequiredService<HgssPokemonParser>())
                .AddKeyedSingleton<IPointerService>(GameFlavor.RS, new RsPointerService())
                .AddKeyedSingleton<IPokemonParser>(GameFlavor.RS, (sp, _) => new Gen3PokemonParser(sp.GetRequiredService<PokemonMetadataService>()))
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
                .AddSingleton<ShinyAlertService>()
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
