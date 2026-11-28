using MongoDB.Driver;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;

namespace ShinyBotHunter.Persistence.MongoDb
{
    // The Plans collection: one document per game and plan name (FR-37a, NFR-3). It replaced HuntPlans, whose old-shape plans
    // are not read. No index and no seed, so a database without the collection reads as no plans.
    public class MongoHuntPlanDao
    {
        // Set once in the constructor; IMongoCollection is thread-safe.
        private readonly IMongoCollection<Plan> collection;

        public MongoHuntPlanDao(MongoDbConnection dbConnection)
        {
            collection = dbConnection.GetDatabase().GetCollection<Plan>("Plans");
        }

        public async Task<Plan?> LoadAsync(GameFlavor game, string name)
            => await collection.Find(p => p.Game == game && p.Name == name).FirstOrDefaultAsync();

        public Task<List<Plan>> ListAsync(GameFlavor game) => collection.Find(p => p.Game == game).SortBy(p => p.Name).ToListAsync();

        public Task SaveAsync(Plan plan)
            => collection.ReplaceOneAsync(p => p.Game == plan.Game && p.Name == plan.Name, plan, new ReplaceOptions { IsUpsert = true });

        public async Task<bool> DeleteAsync(GameFlavor game, string name)
            => (await collection.DeleteOneAsync(p => p.Game == game && p.Name == name)).DeletedCount > 0;
    }
}
