using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace ShinyBotHunter.Persistence.MongoDb
{
    public class MongoDbConnection : IDisposable
    {
        private IMongoDatabase database;
        private MongoClient mongoClient;
        public MongoDbConnection(IOptions<ApplicationSettings> appSettings)
        {
            string connectionUri = $"mongodb://{appSettings.Value.MongoDbUrl}";
            mongoClient = new MongoClient(connectionUri);
            database = mongoClient.GetDatabase(appSettings.Value.MongoDbDatabase);
        }

        public IMongoDatabase GetDatabase()
        {
            return database;
        }

        public void Dispose()
        {
            mongoClient.Dispose();
        }
    }
}
