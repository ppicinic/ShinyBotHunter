using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace ShinyBotHunter.Persistence.MongoDb
{
    public class MongoDbMetadataDao
    {
        MongoDbConnection mongoDbConnection;
        public MongoDbMetadataDao(MongoDbConnection dbConnection) 
        {
            mongoDbConnection = dbConnection;
        }

        public async Task<List<PokemonMetadata>> GetAllPokemonMetadata()
        {
            IMongoDatabase database = mongoDbConnection.GetDatabase();
            IMongoCollection<PokemonMetadata> collection = database.GetCollection<PokemonMetadata>("PokemonMetadata");
            return await collection.AsQueryable().ToListAsync();
        }

        public async Task<List<PokemonMoveMetadata>> GetAllPokemonMoveMetadata()
        {
            IMongoDatabase database = mongoDbConnection.GetDatabase();
            IMongoCollection<PokemonMoveMetadata> collection = database.GetCollection<PokemonMoveMetadata>("PokemonMoveMetadata");
            return await collection.AsQueryable().ToListAsync();
        }
    }
}
