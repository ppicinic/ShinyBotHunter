using MongoDB.Driver;
using ShinyBotHunter.Milestones;

namespace ShinyBotHunter.Persistence.MongoDb
{
    // The GameFiles collection: one document per game file, holding its replacement anchors.
    public class MongoGameFileDao
    {
        // Set once in the constructor; IMongoCollection is thread-safe.
        private readonly IMongoCollection<GameFile> collection;
        // Set once in a constructor.
        private readonly Func<string, Task<GameFile>> load;

        public MongoGameFileDao(MongoDbConnection dbConnection)
        {
            collection = dbConnection.GetDatabase().GetCollection<GameFile>("GameFiles");
            load = FindAsync;
        }

        // The test seam for the MongoDB boundary, reads only: collection is null, so a write through it fails.
        internal MongoGameFileDao(Func<string, Task<GameFile>> load)
        {
            collection = null!;
            this.load = load;
        }

        public Task<GameFile> LoadAsync(string gameFileName) => load(gameFileName);

        // A file with no document has no replacement anchors; the read writes nothing.
        private async Task<GameFile> FindAsync(string gameFileName)
            => await collection.Find(f => f.Id == gameFileName).FirstOrDefaultAsync() ?? new GameFile { Id = gameFileName };

        // Two updates, because Mongo refuses a pull and a push on one array in one update, so a milestone holds one replacement.
        public async Task SetAnchorAsync(string gameFileName, MilestoneAnchor anchor)
        {
            await ClearAnchorAsync(gameFileName, anchor.Milestone);
            await collection.UpdateOneAsync(f => f.Id == gameFileName, Builders<GameFile>.Update.Push(f => f.Anchors, anchor));
        }

        public Task ClearAnchorAsync(string gameFileName, string milestone)
            => collection.UpdateOneAsync(f => f.Id == gameFileName, Builders<GameFile>.Update.PullFilter(f => f.Anchors, a => a.Milestone == milestone),
                new UpdateOptions { IsUpsert = true });
    }
}
