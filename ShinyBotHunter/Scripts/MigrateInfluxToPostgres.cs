using InfluxDB3.Client;
using InfluxDB3.Client.Write;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.PostgreSQL;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using YamlDotNet.Core.Tokens;

namespace ShinyBotHunter.Scripts
{
    public class MigrateInfluxToPostgres
    {
        private string host;
        private string token;
        private string database;
        private string tableName = "encounter";
        private bool withDeletion;
        private bool withDryRun;
        private string? dryRunTable;
        private IServiceProvider provider;
        public MigrateInfluxToPostgres(IServiceProvider provider)
        {
            this.provider = provider;
            var appSettings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;

            host = $"http://{appSettings.InfluxDbUrl}";
            var tokenTemp = provider.GetRequiredService<IConfiguration>().GetValue<string>("InfluxToken");
            this.token = tokenTemp!;
            database = appSettings.InfluxDbDatabase;
            if (tokenTemp is null)
            {
                throw new Exception("No InfluxDb Token is configured");
            }
        }
        public async Task MigrateAsync()
        {
            Console.WriteLine("Starting InfluxDb3 Schema migration from v2 to Postgresql");
            using var client = new InfluxDBClient(host, token, database);
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 1. READ: Safely pull all current data
            var pointsToMigrate = new List<PersistPokemonDataDao>();
            string sql = $"SELECT * FROM \"{tableName}\"";

            await foreach (var item in client.QueryPoints(sql, database: database))
            {
                var point = new PersistPokemonDataDao()
                {

                    Species = (short)ushort.Parse(item.GetTag("pokemon_id")!),
                    FormCode = item.GetTag("form_code")!,
                    Name = item.GetTag("pokemon_name")!,
                    Game = Enum.Parse<GameFlavor>(item.GetTag("pokemon_game")!),
                    Milestone = item.GetTag("phase")!,
                    IsShiny = bool.Parse(item.GetTag("is_shiny")!),
                    GameFileName = item.GetTag("game_file_name")!,
                    Timestamp = DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds((long)item.GetTimestamp()! / 1000000).DateTime, DateTimeKind.Utc),
                    PID = (int)(uint)item.GetIntegerField("PID")!,
                    TID = (short)(ushort)item.GetIntegerField("TID")!,
                    SID = (short)(ushort)item.GetIntegerField("SID")!,
                    IVs = (int)(uint)item.GetIntegerField("IVs")!,
                    Ability = (byte)item.GetIntegerField("Ability")!,
                    Nature = (byte)item.GetIntegerField("Nature")!,
                    ShinyValue = (byte)item.GetIntegerField("ShinyValue")!,
                    Gender = (byte)item.GetIntegerField("Gender")!,
                    Level = (byte)item.GetIntegerField("Level")!,
                };
                pointsToMigrate.Add(point);
            }
            

            // 4. WRITE: Restore the migrated data
            if (pointsToMigrate.Any())
            {
                // Wait ~30s after deletion if you encounter "table not found" or cache issues
                await using (var scope = provider.CreateAsyncScope())
                {
                    using (var context = scope.ServiceProvider.GetRequiredService<PokemonDbContext>())
                    {
                        List<PersistPokemonDataDao> writeData = new List<PersistPokemonDataDao>();
                        for (int i = 0; i < pointsToMigrate.Count; i++)
                        {
                            writeData.Add(pointsToMigrate[i]);
                            if (i % 1000 == 0)
                            {
                                Console.WriteLine($"Writing {i} pokemon to postgresql");
                                await context.Encounters.AddRangeAsync(writeData);
                                await context.SaveChangesAsync();
                                writeData = new List<PersistPokemonDataDao>();
                            }
                        }
                        Console.WriteLine($"Writing {pointsToMigrate.Count} pokemon to postgresql");
                        await context.Encounters.AddRangeAsync(writeData);
                        await context.SaveChangesAsync();
                    }
                }
            }
        }
    }
}
