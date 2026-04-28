using InfluxDB3.Client;
using InfluxDB3.Client.Write;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace ShinyBotHunter.Persistence.InfluxDb
{
    public class InfluxDbEncounterDao : IAsyncDisposable
    {
        private Channel<List<PersistPokemonData>> ProducerQueue { get; set; }
        private InfluxDBClient _client;
        
        public InfluxDbEncounterDao(IConfiguration config)
        {
            string? token = config.GetValue<string>("InfluxToken");
            if(token is null)
            {
                throw new ArgumentNullException("No InfluxDbToken");
            }
            ProducerQueue = Channel.CreateUnbounded<List<PersistPokemonData>>(new UnboundedChannelOptions() { SingleReader = true});
            _client = new InfluxDBClient("http://localhost:8181", token, "pokemon_encounters");
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
                        await _client.WritePointAsync(pokemonPoint, "pokemon_encounters");
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
            var first = await _client.Query(influxQl, database: "pokemon_encounters").FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: "pokemon_encounters").FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<List<PokemonData>> GetLatestEncounters(GameFlavor game)
        {
            string influxQl = $"SELECT * FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' ORDER BY TIME DESC LIMIT 20";
            List<PokemonData> pokemons = new List<PokemonData>();
            await foreach (var item in _client.QueryPoints(influxQl, database: "pokemon_encounters"))
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

        public async Task<double> GetEncountersPerHour(GameFlavor game, string phase)
        {
            string query = $"SELECT AVG(hourly_counts) AS average_points_per_hour FROM (SELECT DATE_BIN(INTERVAL '1 hour', time) AS time, COUNT(*) AS hourly_counts FROM \"encounter\" WHERE time >= now() - INTERVAL '24 hours' AND \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' GROUP BY 1)";
            var result = await _client.Query(query, database: "pokemon_encounters").FirstAsync();
            return double.Parse(result![0]!.ToString()!);
        }

        public async Task<(long, long)> GetAllPhaseEncountersForGame(GameFlavor game, string phase)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}'";
            var first = await _client.Query(influxQl, database: "pokemon_encounters").FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: "pokemon_encounters").FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }

        public async Task<(long, long)> GetAllPhaseSpeciesEncountersForGame(GameFlavor game, string phase, int species)
        {
            string influxQl = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"pokemon_id\" = {species}";
            var first = await _client.Query(influxQl, database: "pokemon_encounters").FirstAsync();

            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"phase\" = '{phase}' AND \"pokemon_id\" = {species} AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: "pokemon_encounters").FirstAsync();
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
            var first = await _client.Query(influxQl, database: "pokemon_encounters").FirstAsync();

            // all shiny encounters
            string influxQl2 = $"SELECT COUNT(\"PID\") FROM \"encounter\" WHERE \"pokemon_game\" = '{game.ToString()}' AND \"pokemon_id\" = {species} AND \"is_shiny\" = 'True'";
            var second = await _client.Query(influxQl2, database: "pokemon_encounters").FirstAsync();
            return ((long)(first![0]!), (long)second![0]!);
        }
    }
}
