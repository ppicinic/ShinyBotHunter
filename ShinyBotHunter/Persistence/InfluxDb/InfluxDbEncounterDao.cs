using InfluxDB3.Client;
using InfluxDB3.Client.Write;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace ShinyBotHunter.Persistence.InfluxDb
{
    public class InfluxDbEncounterDao : IAsyncDisposable
    {
        private Channel<List<PersistPokemonData>> ProducerQueue { get; set; }
        private InfluxDBClient _client;
        private string database;
        
        public InfluxDbEncounterDao(IConfiguration config, IOptions<ApplicationSettings> appSettings)
        {
            string? token = config.GetValue<string>("InfluxToken");
            if(token is null)
            {
                throw new ArgumentNullException("No InfluxDbToken");
            }
            ProducerQueue = Channel.CreateUnbounded<List<PersistPokemonData>>(new UnboundedChannelOptions() { SingleReader = true});
            database = appSettings.Value.InfluxDbDatabase;
            _client = new InfluxDBClient($"http://{appSettings.Value.InfluxDbUrl}", token, database);
        }

        public async Task WriteToQueueAsync(List<PersistPokemonData> pokemonData)
        {
            await ProducerQueue.Writer.WriteAsync(pokemonData);
        }

        public async Task WriteToQueue(PersistPokemonData pokemonData)
        {
            await ProducerQueue.Writer.WriteAsync(new List<PersistPokemonData> { pokemonData } );
        }

        public void Init()
        {
            var _ = Task.Run(async () =>
                {
                    await ProcessAsync();
                }
            );
        }

        public async Task ProcessAsync()
        {
            await foreach(var item in ProducerQueue.Reader.ReadAllAsync())
            {
                try
                {
                    foreach (var pokemonData in item)
                    {
                        var pokemon = pokemonData.PokemonData;
                        var pokemonPoint = PointData.Measurement("encounter")
                            .SetTag("pokemon_id", pokemon.Species.ToString())
                            .SetTag("form_code", pokemon.FormCode)
                            .SetTag("pokemon_name", pokemon.Name)
                            .SetTag("pokemon_game", pokemonData.Game.ToString())
                            .SetTag("phase", pokemonData.Phase)
                            .SetTag("is_shiny", pokemon.IsShiny.ToString())
                            .SetTag("game_file_name", pokemonData.GameFileName)
                            .SetTimestamp(pokemonData.DateTime)
                            .SetField("PID", pokemon.PID)
                            .SetField("TID", pokemon.TID)
                            .SetField("SID", pokemon.SID)
                            .SetField("IVs", pokemon.IVs)
                            .SetField("Ability", pokemon.Ability)
                            .SetField("Nature", pokemon.Nature)
                            .SetField("ShinyValue", pokemon.ShinyValue)
                            .SetField("Gender", pokemon.Gender)
                            .SetField("Level", pokemon.Level);
                        await _client.WritePointAsync(pokemonPoint, database);
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine($"Failed to write to influxdb: {e.ToString()}");
                    throw;
                }
            }
        }

        public async Task<(long, long)> GetAllEncountersForGame(GameFlavor game)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}'";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<(long, long)> GetAllEncountersForGameName(string gameFileName)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}'";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}' AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<List<PokemonData>> GetLatestEncounters(GameFlavor game)
        {
            string influxQl = $"SELECT * FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' ORDER BY TIME DESC LIMIT 20";
            List<PokemonData> pokemons = new List<PokemonData>();
            await foreach (var item in _client.QueryPoints(influxQl, database: database))
            {
                //Console.WriteLine(string.Join(", ", item));
                PokemonData pokemon = new PokemonData()
                {
                    Species = ushort.Parse(item.GetTag("pokemon_id")!),
                    Name = item.GetTag("pokemon_name")!,
                    Ability = (byte)item.GetIntegerField("Ability")!,
                    Gender = (byte)item.GetIntegerField("Gender")!,
                    IVs = (uint)item.GetIntegerField("IVs")!,
                    Nature = (byte)item.GetIntegerField("Nature")!,
                    PID = (uint)item.GetIntegerField("PID")!,
                    SID = (ushort)item.GetIntegerField("SID")!,
                    TID = (ushort)item.GetIntegerField("TID")!,
                    FormCode = item.GetTag("form_code")!,
                    Level = (byte)item.GetIntegerField("Level")!
                };
                pokemons.Add(pokemon);
            }
            return pokemons;
        }
        private const double OneHourMilliseconds = 1 * 60 * 60 * 1000;
        public async Task<double> GetEncountersPerHour(GameFlavor game, string phase)
        {

            string query = $"SELECT DATE_BIN(INTERVAL '1 hour', time) AS time, COUNT(*) AS hourly_counts FROM \"encounter\" WHERE time >= now() - INTERVAL '24 hours' AND \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' GROUP BY 1 ORDER BY time";
            var result = await _client.Query(query, database: database).ToArrayAsync();
            if(result is null || result[0] is null || result[0][0] is null)
            {
                return 0.0;
            }
            string query2 = $"SELECT MIN(time) AS first_time, MAX(time) AS last_time FROM \"encounter\" WHERE time >= now() - INTERVAL '24 hours' AND \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}'";
            // TODO this isn't accurate the first and last hour buckets don't calculate properly.
            var result2 = await _client.Query(query2, database: database).FirstAsync();
            return CalculateEncounters(result, result2);
        }

        public async Task<double> GetEncountersPerHour(GameFlavor game, string phase, string gameFileName)
        {

            string query = $"SELECT DATE_BIN(INTERVAL '1 hour', time) AS time, COUNT(*) AS hourly_counts FROM \"encounter\" WHERE time >= now() - INTERVAL '24 hours' AND \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"game_file_name\" = '{gameFileName}' GROUP BY 1 ORDER BY time";
            var result = await _client.Query(query, database: database).ToArrayAsync();
            if (result is null || result[0] is null || result[0][0] is null)
            {
                return 0.0;
            }
            string query2 = $"SELECT MIN(time) AS first_time, MAX(time) AS last_time FROM \"encounter\" WHERE time >= now() - INTERVAL '24 hours' AND \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"game_file_name\" = '{gameFileName}'";
            // TODO this isn't accurate the first and last hour buckets don't calculate properly.
            var result2 = await _client.Query(query2, database: database).FirstAsync();
            return CalculateEncounters(result, result2);
        }

        private double CalculateEncounters(object?[][] result, object?[] result2)
        {
            if (result2[0] == result2[1])
            {
                return 0.0;
            }
            if (result.Length == 1)
            {
                var row = result[0];
                var timeDiff = (DateTimeOffset)result2[1]! - (DateTimeOffset)result2[0]!;
                var factor = OneHourMilliseconds / timeDiff.TotalMilliseconds;
                return factor * double.Parse(row[1]!.ToString()!);
            }
            double sum = 0.0;
            var firstRow = result[0];
            var firstDiff = (new DateTime((long)(BigInteger)firstRow[0]!).AddHours(1)) - (DateTimeOffset)result2[0]!;
            var firstFactor = OneHourMilliseconds / firstDiff.TotalMilliseconds;
            sum += double.Parse(firstRow[1]!.ToString()!) * firstFactor;
            var lastRow = result[0];
            var lastDiff = (DateTimeOffset)result2[1]! - new DateTime((long)(BigInteger)lastRow[0]!);
            var lastFactor = OneHourMilliseconds / lastDiff.TotalMilliseconds;
            sum += double.Parse(lastRow[1]!.ToString()!) * lastFactor;
            for (int i = 1; i < result.Length - 1; i++)
            {
                var row = result[i];
                sum += double.Parse(row[1]!.ToString()!);
            }
            return sum / result.Length;
        }

        public async Task<(long, long)> GetAllPhaseEncountersForGame(GameFlavor game, string phase)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}'";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<(long, long)> GetAllPhaseEncountersForGameName(string gameFileName, string phase)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}' AND \"phase\" = '{phase}'";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}' AND \"phase\" = '{phase}' AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<(long, long)> GetAllPhaseSpeciesEncountersForGame(GameFlavor game, string phase, int species)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"pokemon_id\" = {species}";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"pokemon_id\" = {species} AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<(long, long)> GetAllPhaseSpeciesEncountersForGameName(string gameFileName, string phase, int species)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}' AND \"phase\" = '{phase}' AND \"pokemon_id\" = {species}";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}' AND \"phase\" = '{phase}' AND \"pokemon_id\" = {species} AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async ValueTask DisposeAsync()
        {
            ProducerQueue.Writer.TryComplete();
            await ProducerQueue.Reader.Completion;
            _client.Dispose();
        }

        public async Task<(long, long)> GetAllSpeciesEncountersForGame(GameFlavor game, int species)
        {
            // All encounters
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"pokemon_id\" = {species}";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            // all shiny encounters
            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"pokemon_id\" = {species} AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<(long, long)> GetAllSpeciesEncountersForGameName(string gameFileName, int species)
        {
            // All encounters
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}' AND \"pokemon_id\" = {species}";
            var first = await _client.Query(influxQl, database: database).FirstAsync();

            // all shiny encounters
            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"game_file_name\" = '{gameFileName.ToString()}' AND \"pokemon_id\" = {species} AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: database).FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }
    }
}
