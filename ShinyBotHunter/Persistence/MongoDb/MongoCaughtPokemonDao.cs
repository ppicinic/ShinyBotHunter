using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using System.Threading.Channels;

namespace ShinyBotHunter.Persistence.MongoDb
{
    public class MongoCaughtPokemonDao : IAsyncDisposable
    {
        private IMongoCollection<CaughtPokemon> _caughtPokemon;
        private Channel<PersistPokemonData> ProducerQueue { get; set; }
        public MongoCaughtPokemonDao(MongoDbConnection dbConnection) {
            _caughtPokemon = dbConnection.GetDatabase().GetCollection<CaughtPokemon>("CaughtPokemon");
            ProducerQueue = Channel.CreateUnbounded<PersistPokemonData>();
        }

        public async Task AddCaughtPokemon(PersistPokemonData pokemon)
        {
            await ProducerQueue.Writer.WriteAsync(pokemon);
        }

        public void Init()
        {
            var _ = Task.Run(async () =>
            {
                await ProcessAsync();
            });
        }

        private async Task ProcessAsync()
        {
            await foreach(var pokemon in ProducerQueue.Reader.ReadAllAsync())
            {
                try
                {
                    await _caughtPokemon.InsertOneAsync(new CaughtPokemon()
                    {
                        Pokemon = pokemon.PokemonData,
                        Game = pokemon.Game,
                        Id = Guid.NewGuid(),
                        Phase = pokemon.Phase,
                    });
                }catch(Exception e)
                {
                    Console.WriteLine(e.ToString());
                    throw;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            ProducerQueue.Writer.TryComplete();
            await ProducerQueue.Reader.Completion;
        }

        public async Task<long> GetAllCaughtShiniesCountAsync(GameFlavor game)
        {
            var filter = Builders<CaughtPokemon>.Filter.Eq(x => x.Game, game);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }

        public async Task<long> GetAllCaughtSpeciesShiniesCountAsync(GameFlavor game, int species)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.Game, game) & builder.Eq(x => x.Pokemon.Species, species);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }
        public async Task<long> GetAllCaughtPhaseShiniesCountAsync(GameFlavor game, string phase)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.Game, game) & builder.Eq(x => x.Phase, phase);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }

        public async Task<long> GetAllCaughtPhaseSpeciesShiniesCountAsync(GameFlavor game, string phase, int species)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.Game, game) & builder.Eq(x => x.Phase, phase) & builder.Eq(x => x.Pokemon.Species, species);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }
    }

    public class CaughtPokemon
    {
        public required PokemonData Pokemon { get; set; }
        public GameFlavor Game { get; set; }
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; }
        public required string Phase { get; set; }
    }
}
