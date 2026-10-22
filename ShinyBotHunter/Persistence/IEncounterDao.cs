using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Persistence
{
    public interface IEncounterDao : IAsyncDisposable
    {
        Task<List<CountRow>> GetCountsAsync(GameFlavor game);
        Task<List<ProgressRow>> GetPhaseProgressAsync(GameFlavor game);
        Task<double> GetEncountersPerHour(GameFlavor game, string phase, string? gameFileName);
        Task<List<PokemonData>> GetLatestEncounters(GameFlavor game);
        void Init();
        Task WriteToQueueAsync(List<PersistPokemonData> encounters);
    }
}
