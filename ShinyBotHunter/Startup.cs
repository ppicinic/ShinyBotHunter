using CmdScale.EntityFrameworkCore.TimescaleDB;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Persistence.PostgreSQL;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Tools;
using System.Text.Json.Serialization;

namespace ShinyBotHunter
{
    public class Startup
    {
        public async Task MainAsync(string[] args)
        {
            if (await SetupCommands.TryRunAsync(args))
            {
                return;
            }
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            CreateServices(builder.Services);
            var app = builder.Build();
            var services = app.Services;
            app.MapControllers().AsRequiredPort(5000);
            app.MapHub<ElectronHub>("/socket-api").AsRequiredPort(5001);

            await services.GetRequiredService<DiscordBotService>().StartAsync();
            services.GetRequiredService<IEncounterDao>().Init();
            await services.GetRequiredService<PokemonMetadataService>().InitAsync();
            await services.GetRequiredService<PhaseInfoService>().InitAsync();
            await services.GetRequiredService<RomInfoService>().InitAsync();
            services.GetRequiredService<NotificationService>().Init();
            services.GetRequiredService<MongoCaughtPokemonDao>().Init();
            services.GetRequiredService<PokemonPersistenceService>().Init();
            services.GetRequiredService<BotService>().Init();
            Console.WriteLine(string.Join(", ", args));
            await MainRunnerAsync(app, services);

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
                .AddSingleton(sp => new GameProfiles(
                    HgssProfile.Create(sp.GetRequiredService<PokemonMetadataService>()),
                    RsProfile.Create(sp.GetRequiredService<PokemonMetadataService>())))
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
                .AddSingleton<PokemonStateReader>()
                .AddSingleton<GraphService>(x => GraphService.Instance)
                .AddSingleton<PathfindingService>()
                .AddSingleton<MapService>(x => MapService.Instance)
                .AddSingleton<SmogonDamageService>()
                .AddTransient<BattleOrchestratorEvent>()
                .AddTransient<NavigatorEvent>()
                .AddSingleton<LegalityService>()
                .AddSingleton<RsStartMenuService>()
                .AddSingleton<StarterHuntStateStore>()
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
