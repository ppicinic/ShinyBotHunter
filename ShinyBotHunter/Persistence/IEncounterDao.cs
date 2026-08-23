using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Persistence
{
    public interface IEncounterDao : IAsyncDisposable
    {
        Task<(long, long)> GetAllEncountersForGame(GameFlavor game);
        Task<(long, long)> GetAllEncountersForGameName(string gameFileName);
        Task<(long, long)> GetAllPhaseEncountersForGame(GameFlavor game, string phase);
        Task<(long, long)> GetAllPhaseEncountersForGameName(string gameFileName, string phase);
        Task<(long, long)> GetAllPhaseFormEncountersForGame(GameFlavor game, string phase, string formCode);
        Task<(long, long)> GetAllPhaseSpeciesEncountersForGame(GameFlavor game, string phase, int species);
        Task<(long, long)> GetAllPhaseSpeciesEncountersForGameName(string gameFileName, string phase, int species);
        Task<(long, long)> GetAllSpeciesEncountersForGame(GameFlavor game, int species);
        Task<(long, long)> GetAllSpeciesEncountersForGameName(string gameFileName, int species);
        Task<(long, long)> GetAllPhaseFormEncountersForGameName(string gameFileName, string phase, string formCode);
        Task<(long, long)> GetAllFormEncountersForGame(GameFlavor game, string formCode);
        Task<(long, long)> GetAllFormEncountersForGameName(string gameFileName, string formCode);
        Task<int> GetCurrentPhaseCount(GameFlavor game, string phase);
        Task<int> GetCurrentPhaseCount(string gameFileName, string phase);
        Task<double> GetEncountersPerHour(GameFlavor gameFlavor, string phase);
        Task<double> GetEncountersPerHour(GameFlavor gameFlavor, string phase, string gameFileName);
        Task<List<PokemonData>> GetLatestEncounters(GameFlavor game);
        void Init();
        Task WriteToQueueAsync(List<PersistPokemonData> encounters);
    }
}
