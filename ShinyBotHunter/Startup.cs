using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;

namespace ShinyBotHunter
{
    public class Startup
    {
        public async Task MainAsync()
        {
            var services = CreateServices();
            await using (var scope = services.CreateAsyncScope())
            {
                MultiShinyHunterEvent e = scope.ServiceProvider.GetRequiredService<MultiShinyHunterEvent>();
                var data = await e.ExecuteAsync();
                foreach (var pokemon in data)
                {
                    Console.WriteLine($"{pokemon.Game}{pokemon.GameId}: {pokemon.PokemonData.Species}");
                }
            }
        }

        public ServiceProvider CreateServices()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
            var services = new ServiceCollection()
                .Configure<ApplicationSettings>(config.GetSection("ApplicationSettings"))
                .AddScoped<MultiShinyHunterEvent>()
                .AddSingleton<BotFactory>()
                .BuildServiceProvider();
            return services;
        }
    }
}
