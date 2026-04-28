using MongoDB.Driver;

namespace ShinyBotHunter.Persistence.MongoDb
{
    public class MongoDbConnection : IDisposable
    {
        private IMongoDatabase database;
        private MongoClient mongoClient;
        public MongoDbConnection()
        {
            string connectionUri = "mongodb://localhost:27017";
            mongoClient = new MongoClient(connectionUri);
            database = mongoClient.GetDatabase("ShinyBotHunter");
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
