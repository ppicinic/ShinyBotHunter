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

        /// <summary>
        /// A save belongs to a ROM copy, not to a milestone, so created saves live in one document
        /// per flavor. Keying them by milestone -- as they briefly were -- meant a hunt started under
        /// a different milestone missed the record and replayed the new-game macro against a copy
        /// that already had a save, which desyncs at the main menu: a Continue entry appears once a
        /// save exists and every recorded press lands one row off.
        /// </summary>
        private const string CreatedSavesDocName = "Created Saves";

        /// <summary>Whether this ROM copy already has a save that finished being written.</summary>
        public async Task<bool> IsSaveCreatedAsync(GameFlavor game, string romHandle)
        {
            StarterHuntGameState? saves = await collection.Find(KeyFor(game, CreatedSavesDocName)).FirstOrDefaultAsync();
            if (saves is not null && saves.SavesCreated.Contains(romHandle, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            // Saves recorded before this moved to its own document sit inside per-milestone hunt
            // documents. Read those too, or every copy from that era would be re-created -- which is
            // exactly the desync this exists to prevent.
            var legacy = await collection.Find(x => x.Game == game && x.Name != CreatedSavesDocName).ToListAsync();
            return legacy.Any(x => x.SavesCreated.Contains(romHandle, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Records that a ROM copy's save is written.
        ///
        /// Added to the stored list directly rather than by writing the whole document back: several
        /// emulators create their saves at once, and replacing the document would let one overwrite
        /// what another had just recorded.
        /// </summary>
        public Task MarkSaveCreatedAsync(GameFlavor game, string romHandle)
            => collection.UpdateOneAsync(
                KeyFor(game, CreatedSavesDocName),
                Builders<StarterHuntGameState>.Update
                    .AddToSet(x => x.SavesCreated, romHandle)
                    .Set(x => x.UpdatedUtc, DateTime.UtcNow),
                new UpdateOptions { IsUpsert = true });

        /// <summary>Whether a starter has already been caught, so a resumed hunt can skip it.</summary>
        public static bool IsDone(StarterHuntGameState state, string starter)
            => state.CompletedStarters.Contains(starter, StringComparer.OrdinalIgnoreCase);
    }
}
