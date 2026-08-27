using MongoDB.Driver;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Loads and saves a starter hunt's progress.
    ///
    /// Hunts run for hours, so their progress has to outlive the process: which starters are done,
    /// which shinies are waiting to be played out, and how many encounters it took. Without it a
    /// restart resumes a starter that was already caught.
    ///
    /// Uses the same PhaseStates collection and the same (name, game) key as the HGSS hunt, so both
    /// hunts' states sit together and can be read the same way.
    /// </summary>
    public class StarterHuntStateStore
    {
        private readonly IMongoCollection<StarterHuntGameState> collection;

        public StarterHuntStateStore(MongoDbConnection connection)
        {
            collection = connection.GetDatabase().GetCollection<StarterHuntGameState>("PhaseStates");
        }

        private static FilterDefinition<StarterHuntGameState> KeyFor(GameFlavor game, string milestone)
            => Builders<StarterHuntGameState>.Filter.Where(x => x.Name == milestone && x.Game == game);

        /// <summary>Existing progress for this hunt, or a fresh record if it has never run.</summary>
        public async Task<StarterHuntGameState> LoadOrCreateAsync(GameFlavor game, string milestone)
        {
            StarterHuntGameState? existing = await collection.Find(KeyFor(game, milestone)).FirstOrDefaultAsync();
            if (existing is not null)
            {
                return existing;
            }

            return new StarterHuntGameState
            {
                Id = Guid.NewGuid(),
                Name = milestone,
                Game = game,
                StartedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
            };
        }

        /// <summary>
        /// Writes progress back. Upserted on the same key it was loaded by, so a hunt that started
        /// before this existed still gets a single record rather than a new one each save.
        /// </summary>
        public async Task SaveAsync(StarterHuntGameState state)
        {
            state.UpdatedUtc = DateTime.UtcNow;
            await collection.ReplaceOneAsync(
                KeyFor(state.Game, state.Name),
                state,
                new ReplaceOptions { IsUpsert = true });
        }

        /// <summary>Whether a starter has already been caught, so a resumed hunt can skip it.</summary>
        public static bool IsDone(StarterHuntGameState state, string starter)
            => state.CompletedStarters.Contains(starter, StringComparer.OrdinalIgnoreCase);
    }
}
