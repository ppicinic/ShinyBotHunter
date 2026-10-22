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
        private readonly ILogger<MongoCaughtPokemonDao> _logger;
        private Channel<CaughtPokemon> ProducerQueue { get; set; }
        public MongoCaughtPokemonDao(MongoDbConnection dbConnection, ILogger<MongoCaughtPokemonDao> logger) {
            _logger = logger;
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
            _ = Task.Run(() => BackgroundLoop.RunSupervisedAsync(ProcessAsync, _logger, nameof(MongoCaughtPokemonDao)));
        }

        private async Task ProcessAsync()
        {
            await foreach(var pokemon in ProducerQueue.Reader.ReadAllAsync())
            {
                await _caughtPokemon.InsertOneAsync(pokemon);
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
