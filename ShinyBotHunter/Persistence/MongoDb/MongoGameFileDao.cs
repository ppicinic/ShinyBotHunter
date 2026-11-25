using MongoDB.Driver;
using ShinyBotHunter.Milestones;

namespace ShinyBotHunter.Persistence.MongoDb
{
    // The GameFiles collection: one document per game file (FR-7). Each write is one single-document upsert of one field, so a
    // hide and an entries edit never overwrite each other.
    public class MongoGameFileDao
    {
        // Set once in the constructor; IMongoCollection is thread-safe.
        private readonly IMongoCollection<GameFile> collection;

        public MongoGameFileDao(MongoDbConnection dbConnection)
        {
            collection = dbConnection.GetDatabase().GetCollection<GameFile>("GameFiles");
        }

        // A file with no document has an empty goal; the read writes nothing.
        public async Task<GameFile> LoadAsync(string gameFileName)
            => await collection.Find(f => f.Id == gameFileName).FirstOrDefaultAsync() ?? new GameFile { Id = gameFileName };

        public Task SetEntriesAsync(string gameFileName, List<GoalEntry> entries)
            => collection.UpdateOneAsync(f => f.Id == gameFileName, Builders<GameFile>.Update.Set(f => f.Entries, entries),
                new UpdateOptions { IsUpsert = true });

        public Task SetHiddenAsync(string gameFileName, HiddenRow row, bool hidden)
            => collection.UpdateOneAsync(f => f.Id == gameFileName,
                hidden ? Builders<GameFile>.Update.AddToSet(f => f.Hidden, row) : Builders<GameFile>.Update.Pull(f => f.Hidden, row),
                new UpdateOptions { IsUpsert = true });
    }
}
