using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Persistence.MongoDb
{
    // The whole dashboard config, saved as one document so it survives a restart (docs/specs/dashboard-alive.md FR-13).
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

        public async Task<DashboardConfig?> LoadAsync()
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
            document.InsertAt(0, new BsonElement("_id", Id));
            return document;
        }

        internal static DashboardConfig FromDocument(BsonDocument document)
        {
            document.Remove("_id");
            // A document saved before the Pickup list has no element, and the driver builds DashboardConfig through its constructor,
            // so a missing element would load as null rather than the initializer's empty array.
            if (!document.Contains("PickupGameFiles"))
            {
                document["PickupGameFiles"] = new BsonArray();
            }
            return BsonSerializer.Deserialize<DashboardConfig>(document);
        }
    }
}
