using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts;
using System.Threading.Channels;

namespace ShinyBotHunter.Persistence.MongoDb
{
    public class MongoCaughtPokemonDao : IAsyncDisposable
    {
        private IMongoCollection<CaughtPokemon> _caughtPokemon;
        private Channel<CaughtPokemon> ProducerQueue { get; set; }
        public MongoCaughtPokemonDao(MongoDbConnection dbConnection) {
            _caughtPokemon = dbConnection.GetDatabase().GetCollection<CaughtPokemon>("CaughtPokemon");
            ProducerQueue = Channel.CreateUnbounded<CaughtPokemon>();
        }

        public CaughtPokemon CreateCaughtPokemonEntry(PersistPokemonData pokemon)
        {
            return new CaughtPokemon()
            {
                Pokemon = pokemon.PokemonData,
                Game = pokemon.Game,
                Id = Guid.NewGuid(),
                Phase = pokemon.Phase,
                GameFileName = pokemon.GameFileName
            };
        }

        public async Task AddCaughtPokemon(CaughtPokemon pokemon)
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
                    await _caughtPokemon.InsertOneAsync(pokemon);
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

        public async Task<List<CaughtPokemon>> GetAllCaughtShiniesAsync(GameFlavor game)
        {
            var filter = Builders<CaughtPokemon>.Filter.Eq(x => x.Game, game);
            return await _caughtPokemon.Find(filter).ToListAsync();
        }

        public async Task<long> GetAllCaughtFormShiniesCountAsync(GameFlavor game, string formCode)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.Game, game) & builder.Eq(x => x.Pokemon.FormCode, formCode);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }
        public async Task<long> GetAllCaughtPhaseShiniesCountAsync(GameFlavor game, string phase)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.Game, game) & builder.Eq(x => x.Phase, phase);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }

        public async Task<long> GetAllCaughtPhaseFormShiniesCountAsync(GameFlavor game, string phase, string formCode)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.Game, game) & builder.Eq(x => x.Phase, phase) & builder.Eq(x => x.Pokemon.FormCode, formCode);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }

        public async Task<List<CaughtPokemon>> GetAllCaughtShiniesAsync(string gameFileName)
        {
            var filter = Builders<CaughtPokemon>.Filter.Eq(x => x.GameFileName, gameFileName);
            return await _caughtPokemon.Find(filter).ToListAsync();
        }

        public async Task<long> GetAllCaughtFormShiniesCountAsync(string gameFileName, string formCode)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.GameFileName, gameFileName) & builder.Eq(x => x.Pokemon.FormCode, formCode);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }
        public async Task<long> GetAllCaughtPhaseShiniesCountAsync(string gameFileName, string phase)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.GameFileName, gameFileName) & builder.Eq(x => x.Phase, phase);
            return await _caughtPokemon.CountDocumentsAsync(filter);
        }

        public async Task<long> GetAllCaughtPhaseFormShiniesCountAsync(string gameFileName, string phase, string formCode)
        {
            var builder = Builders<CaughtPokemon>.Filter;
            var filter = builder.Eq(x => x.GameFileName, gameFileName) & builder.Eq(x => x.Phase, phase) & builder.Eq(x => x.Pokemon.FormCode, formCode);
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
        public required string GameFileName { get; set; }
    }
}
