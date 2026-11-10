using Microsoft.AspNetCore.SignalR;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{
    public class AdminService
    {
        // Guards gamePhases, RecentEncounterCount and appearance. Held only around the mutation or the snapshot copy, never across an await.
        private readonly object gamePhasesLock = new();
        // Mutated by concurrent dashboard requests (subscribe/unsubscribe) and copied by UpdateDashboardsAsync (requests and every SignalR connect): read and written only under gamePhasesLock.
        private readonly HashSet<GamePhase> gamePhases = new HashSet<GamePhase>();
        // Set once in the constructor; the hub context and the MongoDB collection behind them are thread-safe.
        private readonly Func<DashboardConfig, Task> push;
        private readonly Func<DashboardConfig, Task> save;
        // Written by dashboard requests, read by UpdateDashboardsAsync: read and written only under gamePhasesLock so a pushed config is one consistent snapshot.
        private int RecentEncounterCount = 10;
        // Replaced whole by SetAppearanceAsync, never changed in place, so a snapshot may share it; read and written only under gamePhasesLock.
        private Appearance appearance = new();
        // Orders the saves: held from taking the snapshot to the end of the MongoDB write, so the last write is always the newest snapshot.
        // A semaphore, not a lock, because it is held across an await; gamePhasesLock is taken inside it only for the copy.
        private readonly SemaphoreSlim saveGate = new(1, 1);

        public AdminService(IHubContext<ElectronHub, IElectronClient> hubContext, MongoDashboardConfigDao dao)
            : this(config => hubContext.Clients.All.ReceiveDashboardConfig(config), dao.SaveAsync)
        {
        }

        // The test seam for the SignalR and MongoDB boundaries.
        internal AdminService(Func<DashboardConfig, Task> push, Func<DashboardConfig, Task> save)
        {
            this.push = push;
            this.save = save;
        }

        public async Task SubscribeGamePhaseAsync(string game, string phase, string? gameFileName)
        {
            GamePhase gp = new GamePhase(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            lock (gamePhasesLock)
            {
                gamePhases.Add(gp);
            }
            await UpdateDashboardsAsync();
            await SaveAsync();
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
                await SaveAsync();
            }
        }

        public async Task UpdateRecentEncounterCount(int count)
        {
            lock (gamePhasesLock)
            {
                RecentEncounterCount = count;
            }
            await UpdateDashboardsAsync();
            await SaveAsync();
        }

        public async Task SetAppearanceAsync(Appearance appearance)
        {
            lock (gamePhasesLock)
            {
                this.appearance = appearance;
            }
            await UpdateDashboardsAsync();
            await SaveAsync();
        }

        // Pushes only; a SignalR connect calls this and never saves.
        public async Task UpdateDashboardsAsync()
        {
            await push(Snapshot());
        }

        // Runs after the push, so a MongoDB failure still leaves every dashboard showing the state in memory.
        private async Task SaveAsync()
        {
            await saveGate.WaitAsync();
            try
            {
                await save(Snapshot());
            }
            finally
            {
                saveGate.Release();
            }
        }

        private DashboardConfig Snapshot()
        {
            lock (gamePhasesLock)
            {
                return new DashboardConfig(gamePhases.ToArray())
                {
                    RecentEncounterCount = RecentEncounterCount,
                    Appearance = appearance,
                };
            }
        }
    }
}
