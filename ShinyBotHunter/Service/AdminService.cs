using Microsoft.AspNetCore.SignalR;
using ShinyBotHunter.Macro;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{
    public class AdminService
    {
        // Guards gamePhases and RecentEncounterCount. Held only around the mutation or the snapshot copy, never across an await.
        private readonly object gamePhasesLock = new();
        // Mutated by concurrent dashboard requests (subscribe/unsubscribe) and copied by UpdateDashboardsAsync (requests and every SignalR connect): read and written only under gamePhasesLock.
        private readonly HashSet<GamePhase> gamePhases = new HashSet<GamePhase>();
        // Set once in the constructor; IHubContext is thread-safe.
        private IHubContext<ElectronHub, IElectronClient> _hubContext;
        // Written by dashboard requests, read by UpdateDashboardsAsync: read and written only under gamePhasesLock so a pushed config is one consistent snapshot.
        private int RecentEncounterCount = 10;
        public AdminService(IHubContext<ElectronHub, IElectronClient> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SubscribeGamePhaseAsync(string game, string phase, string? gameFileName)
        {
            GamePhase gp = new GamePhase(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            lock (gamePhasesLock)
            {
                gamePhases.Add(gp);
            }
            await UpdateDashboardsAsync();
        }

        public async Task UnsubscribeGamePhaseAsync(string game, string phase, string? gameFileName)
        {
            GamePhase gp = new GamePhase(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            bool removed;
            lock (gamePhasesLock)
            {
                removed = gamePhases.Remove(gp);
            }
            if (removed)
            {
                await UpdateDashboardsAsync();
            }
        }

        public async Task UpdateRecentEncounterCount(int count)
        {
            lock (gamePhasesLock)
            {
                RecentEncounterCount = count;
            }
            await UpdateDashboardsAsync();
        }

        public async Task UpdateDashboardsAsync()
        {
            DashboardConfig dashboardConfig;
            lock (gamePhasesLock)
            {
                dashboardConfig = new DashboardConfig(gamePhases.ToArray())
                {
                    RecentEncounterCount = RecentEncounterCount
                };
            }
            await _hubContext.Clients.All.ReceiveDashboardConfig(dashboardConfig);
        }
    }
}
