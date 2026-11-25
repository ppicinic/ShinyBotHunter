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

        public Task SetArchivedAsync(string gameFileName, string milestone, bool archived)
            => collection.UpdateOneAsync(f => f.Id == gameFileName,
                archived ? Builders<GameFile>.Update.AddToSet(f => f.Archived, milestone) : Builders<GameFile>.Update.Pull(f => f.Archived, milestone),
                new UpdateOptions { IsUpsert = true });

        public Task SetAssignedAsync(string gameFileName, string phase, bool assigned)
            => collection.UpdateOneAsync(f => f.Id == gameFileName,
                assigned ? Builders<GameFile>.Update.AddToSet(f => f.Assigned, phase) : Builders<GameFile>.Update.Pull(f => f.Assigned, phase),
                new UpdateOptions { IsUpsert = true });

        // Two updates, because Mongo refuses a pull and a push on one array in one update; an empty label only clears.
        public async Task SetOrderAsync(string gameFileName, MilestoneLabel label)
        {
            await collection.UpdateOneAsync(f => f.Id == gameFileName, Builders<GameFile>.Update.PullFilter(f => f.Order, l => l.Milestone == label.Milestone),
                new UpdateOptions { IsUpsert = true });
            if (!string.IsNullOrEmpty(label.Label))
            {
                await collection.UpdateOneAsync(f => f.Id == gameFileName, Builders<GameFile>.Update.Push(f => f.Order, label));
            }
        }

        // The phase names assigned to any file: a phase in none of them is on every file (FR-16).
        public async Task<HashSet<string>> AssignedPhasesAsync()
            => (await (await collection.DistinctAsync<string>(nameof(GameFile.Assigned), Builders<GameFile>.Filter.Empty)).ToListAsync()).ToHashSet();
    }
}
