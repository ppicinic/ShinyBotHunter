using Microsoft.AspNetCore.SignalR;
using ShinyBotHunter.Macro;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{
    public class AdminService
    {
        private HashSet<GamePhase> gamePhases = new HashSet<GamePhase>();
        private IHubContext<ElectronHub, IElectronClient> _hubContext;
        private int RecentEncounterCount = 10;
        public AdminService(IHubContext<ElectronHub, IElectronClient> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SubscribeGamePhaseAsync(string game, string phase, string? gameFileName)
        {
            GamePhase gp = new GamePhase(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            gamePhases.Add(gp);
            await UpdateDashboardsAsync();
        }

        public async Task UnsubscribeGamePhaseAsync(string game, string phase, string? gameFileName)
        {
            GamePhase gp = new GamePhase(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            if (gamePhases.Contains(gp))
            {
                gamePhases.Remove(gp);
                await UpdateDashboardsAsync();
            }
        }

        public async Task UpdateRecentEncounterCount(int count)
        {
            RecentEncounterCount = count;
            await UpdateDashboardsAsync();
        }

        public async Task UpdateDashboardsAsync()
        {
            var dashboardConfig = new DashboardConfig(gamePhases.ToArray())
            {
                RecentEncounterCount = RecentEncounterCount
            };
            await _hubContext.Clients.All.ReceiveDashboardConfig(dashboardConfig);
        }
    }
}
