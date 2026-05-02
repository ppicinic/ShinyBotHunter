using InfluxDB3.Client;
using InfluxDB3.Client.Write;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using YamlDotNet.Core.Tokens;

namespace ShinyBotHunter.Scripts
{
    public class MigrateInfluxSchemav1Tov2
    {
        private string host;
        private string token;
        private string database;
        private string tableName = "encounter";
        private bool withDeletion;
        private bool withDryRun;
        private string? dryRunTable;
        public MigrateInfluxSchemav1Tov2(IServiceProvider provider, bool withDeletion, bool withDryRun, string? dryRunTable)
        {
            var appSettings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;
            host = $"http://{appSettings.InfluxDbUrl}";
            var tokenTemp = provider.GetRequiredService<IConfiguration>().GetValue<string>("InfluxToken");
            this.token = tokenTemp!;
            database = appSettings.InfluxDbDatabase;
            this.withDeletion = withDeletion;
            this.withDryRun = withDryRun;
            this.dryRunTable = dryRunTable;
            if (tokenTemp is null)
            {
                throw new Exception("No InfluxDb Token is configured");
            }
            if(withDeletion && withDryRun)
            {
                throw new Exception("Cannot delete a dry run");
            }
            if(withDryRun && dryRunTable is null)
            {
                throw new Exception("There must be a dry run table name if it's a dry run.");
            }
        }
        public async Task MigrateAsync()
        {
            Console.WriteLine("Starting InfluxDb3 Schema migration from v1 to v2");
            using var client = new InfluxDBClient(host, token, database);
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 1. READ: Safely pull all current data
            var pointsToMigrate = new List<PointData>();
            string sql = $"SELECT * FROM \"{tableName}\"";

            await foreach (var item in client.QueryPoints(sql, database: database))
            {
                var point = PointData.Measurement(dryRunTable ?? tableName)
                    .SetTag("pokemon_id", item.GetTag("pokemon_id")!)
                    .SetTag("form_code", item.GetTag("form_code")!)
                    .SetTag("pokemon_name", item.GetTag("pokemon_name")!)
                    .SetTag("pokemon_game", item.GetTag("pokemon_game")!)
                    .SetTag("phase", item.GetTag("phase")!)
                    .SetTag("is_shiny", item.GetTag("is_shiny")!)
                    // Apply the new tag
                    .SetTag("game_file_name", "Pokemon SoulSilver")
                    .SetTimestamp((long)item.GetTimestamp()!)
                    // Fields
                    .SetField("PID", item.GetIntegerField("PID")!)
                    .SetField("TID", item.GetIntegerField("TID")!)
                    .SetField("SID", item.GetIntegerField("SID")!)
                    .SetField("IVs", item.GetIntegerField("IVs")!)
                    .SetField("Ability", item.GetIntegerField("Ability")!)
                    .SetField("Nature", item.GetIntegerField("Nature")!)
                    .SetField("ShinyValue", item.GetIntegerField("ShinyValue")!)
                    .SetField("Gender", item.GetIntegerField("Gender")!)
                    .SetField("Level", item.GetIntegerField("Level")!);

                pointsToMigrate.Add(point);
            }

            // 2. DELETE: Remove the old table and its data
            // Use 'hard_delete_at=now' to ensure it's gone immediately for the rewrite
            if (withDeletion)
            {
                var deleteUrl = $"{host}/api/v3/configure/table?db={database}&table={tableName}";
                var deleteResponse = await httpClient.DeleteAsync(deleteUrl);
                deleteResponse.EnsureSuccessStatusCode();
                Console.WriteLine("Table deleted successfully.");

                // 3. OPTIONAL: Recreate table (Explicit Schema)
                // You can skip this and go straight to Write; Influx will auto-create on write.
                //var createUrl = $"{host}/api/v3/configure/table";
                //var createBody = $"{{\"db\": \"{database}\", \"table\": \"{tableName}\", \"tags\": [\"pokemon_id\", \"game_file_name\", \"form_code\"]}}";
                //var createContent = new StringContent(createBody, Encoding.UTF8, "application/json");
                //await httpClient.PostAsync(createUrl, createContent);
                //await Task.Delay(5000);
                await CreateSchema(null);
            }
            if (withDryRun) 
            {
                await CreateSchema(dryRunTable);
            }
            

            // 4. WRITE: Restore the migrated data
            if (pointsToMigrate.Any())
            {
                // Wait ~30s after deletion if you encounter "table not found" or cache issues
                
                await client.WritePointsAsync(pointsToMigrate, database);
                Console.WriteLine($"Migration complete. {pointsToMigrate.Count} points rewritten.");
            }
        }

        private async Task CreateSchema(string? dryRunTableName)
        {
            using var httpClient = new HttpClient(); 
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var createUrl = $"{host}/api/v3/configure/table";

            var schema = new
            {
                db = database,
                table = dryRunTableName ?? tableName,
                // Tags are simple string arrays
                tags = new[] {
                    "pokemon_id", "form_code", "pokemon_name", "pokemon_game",
                    "phase", "is_shiny", "game_file_name"
                },
                            // Fields must specify their exact data type
                            fields = new[] {
                    new { name = "PID", type = "int64" },
                    new { name = "TID", type = "int64" },
                    new { name = "SID", type = "int64" },
                    new { name = "IVs", type = "int64" },
                    new { name = "Ability", type = "int64" },
                    new { name = "Nature", type = "int64" },
                    new { name = "ShinyValue", type = "int64" },
                    new { name = "Gender", type = "int64" },
                    new { name = "Level", type = "int64" }
                }
            };

            var createBody = System.Text.Json.JsonSerializer.Serialize(schema);
            var createContent = new StringContent(createBody, Encoding.UTF8, "application/json");

            var createResponse = await httpClient.PostAsync(createUrl, createContent);
            createResponse.EnsureSuccessStatusCode();
            Console.WriteLine("Table recreated with explicit schema.");
        }
    }
}
