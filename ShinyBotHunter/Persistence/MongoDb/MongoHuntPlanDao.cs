using MongoDB.Driver;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;

namespace ShinyBotHunter.Persistence.MongoDb
{
    // The HuntPlans collection: one document per game and plan name (FR-37a, NFR-3). No index and no seed, so a database
    // without the collection reads as no plans.
    public class MongoHuntPlanDao
    {
        // Set once in the constructor; IMongoCollection is thread-safe.
        private readonly IMongoCollection<HuntPlan> collection;

        public MongoHuntPlanDao(MongoDbConnection dbConnection)
        {
            collection = dbConnection.GetDatabase().GetCollection<HuntPlan>("HuntPlans");
        }

        public async Task<HuntPlan?> LoadAsync(GameFlavor game, string name)
            => await collection.Find(p => p.Game == game && p.Name == name).FirstOrDefaultAsync();

        public Task<List<HuntPlan>> ListAsync(GameFlavor game) => collection.Find(p => p.Game == game).ToListAsync();

        public Task SaveAsync(HuntPlan plan)
            => collection.ReplaceOneAsync(p => p.Game == plan.Game && p.Name == plan.Name, plan, new ReplaceOptions { IsUpsert = true });

        public async Task<bool> DeleteAsync(GameFlavor game, string name)
            => (await collection.DeleteOneAsync(p => p.Game == game && p.Name == name)).DeletedCount > 0;
    }
}
