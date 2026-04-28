using ShinyBotHunter.Pokemon;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{

    public class DashboardConfig
    {
        public DashboardConfig(GamePhase[] gamePhases)
        {
            GamePhases = gamePhases;
        }

        public GamePhase[] GamePhases { get; private set; }
        public int RecentEncounterCount { get; set; }
    }
    public interface IElectronClient
    {
        Task RecieveEncounters(GameEncounterResponse pokemon);
        Task ReceiveDashboardConfig(DashboardConfig config);
    }
}
