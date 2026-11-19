using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Persistence.MongoDb
{
    // Subscriptions and the recent count, saved as one shared document (dashboard-alive FR-13); appearance is per PC in AppearanceFile (appearance-per-pc FR-3).
    public class MongoDashboardConfigDao
    {
        private const string Id = "dashboard";
        private static readonly FilterDefinition<BsonDocument> ById = Builders<BsonDocument>.Filter.Eq("_id", Id);

        // Set once in the constructor; IMongoCollection is thread-safe.
        private readonly IMongoCollection<BsonDocument> collection;

        public MongoDashboardConfigDao(MongoDbConnection dbConnection)
        {
            collection = dbConnection.GetDatabase().GetCollection<BsonDocument>("DashboardConfig");
        }

        public async Task<(DashboardConfig Config, Appearance? Appearance)?> LoadAsync()
        {
            BsonDocument? document = await collection.Find(ById).FirstOrDefaultAsync();
            return document == null ? null : FromDocument(document);
        }

        public async Task SaveAsync(DashboardConfig config)
        {
            await collection.ReplaceOneAsync(ById, ToDocument(config), new ReplaceOptions { IsUpsert = true });
        }

        internal static BsonDocument ToDocument(DashboardConfig config)
        {
            BsonDocument document = config.ToBsonDocument();
            document.Remove("Appearance");
            document.InsertAt(0, new BsonElement("_id", Id));
            return document;
        }

        internal static (DashboardConfig Config, Appearance? Appearance) FromDocument(BsonDocument document)
        {
            document.Remove("_id");
            DashboardConfig config = BsonSerializer.Deserialize<DashboardConfig>(document);
            return (config, document.Contains("Appearance") ? config.Appearance : null);
        }
    }
}
