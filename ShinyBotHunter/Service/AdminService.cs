using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{
    public class AdminService
    {
        private static readonly string[] ThemeIds = ["midnight", "hgss-drift", "hgss-dusk", "rs-drift", "ruby", "sapphire", "christmas", "christmas-night"];

        // Guards gamePhases, RecentEncounterCount and appearance. Held only around the mutation or the snapshot copy, never across an await.
        private readonly object gamePhasesLock = new();
        // Mutated by concurrent dashboard requests (subscribe/unsubscribe) and copied by UpdateDashboardsAsync (requests and every SignalR connect): read and written only under gamePhasesLock.
        private readonly HashSet<GamePhase> gamePhases = new HashSet<GamePhase>();
        // Set once in the constructor; the hub context and the MongoDB collection behind them are thread-safe.
        private readonly Func<DashboardConfig, Task> push;
        private readonly Func<Task<DashboardConfig?>> load;
        private readonly Func<DashboardConfig, Task> save;
        private readonly ILogger logger;
        // Written by dashboard requests, read by UpdateDashboardsAsync: read and written only under gamePhasesLock so a pushed config is one consistent snapshot.
        private int RecentEncounterCount = 10;
        // Replaced whole by SetAppearanceAsync and InitAsync, never changed in place, so a snapshot may share it; read and written only under gamePhasesLock.
        private Appearance appearance = new();
        // Orders the saves: held from taking the snapshot to the end of the MongoDB write, so the last write is always the newest snapshot.
        // A semaphore, not a lock, because it is held across an await; gamePhasesLock is taken inside it only for the copy.
        private readonly SemaphoreSlim saveGate = new(1, 1);

        public AdminService(IHubContext<ElectronHub, IElectronClient> hubContext, MongoDashboardConfigDao dao, ILogger<AdminService> logger)
            : this(config => hubContext.Clients.All.ReceiveDashboardConfig(config), dao.LoadAsync, dao.SaveAsync, logger)
        {
        }

        // The test seam for the SignalR and MongoDB boundaries.
        internal AdminService(Func<DashboardConfig, Task> push, Func<Task<DashboardConfig?>> load, Func<DashboardConfig, Task> save, ILogger logger)
        {
            this.push = push;
            this.load = load;
            this.save = save;
            this.logger = logger;
        }

        // Restores the saved config. Runs before the host starts, so no dashboard is connected yet and nothing is pushed.
        public async Task InitAsync()
        {
            DashboardConfig? loaded = await load();
            if (loaded == null)
            {
                return;
            }
            Appearance restored = Normalise(loaded.Appearance);
            lock (gamePhasesLock)
            {
                gamePhases.UnionWith(loaded.GamePhases);
                RecentEncounterCount = loaded.RecentEncounterCount;
                appearance = restored;
            }
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

        // FR-19: an unknown theme id falls back to Midnight and a malformed time to its default, with a warning. No window entry is dropped.
        private Appearance Normalise(Appearance loaded)
        {
            loaded.DayStart = Time(loaded.DayStart, "07:00", "dayStart");
            loaded.NightStart = Time(loaded.NightStart, "19:00", "nightStart");
            Choice(loaded.Default, "default");
            foreach ((string window, ThemeChoice choice) in loaded.Windows)
            {
                Choice(choice, window);
            }
            return loaded;
        }

        private string Time(string value, string fallback, string name)
        {
            if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return value;
            }
            logger.LogWarning("Saved dashboard config has {Name} {Value}, not HH:mm; using {Fallback}", name, value, fallback);
            return fallback;
        }

        private void Choice(ThemeChoice choice, string window)
        {
            if (!ThemeIds.Contains(choice.Theme))
            {
                logger.LogWarning("Saved dashboard config has unknown theme {Theme} for {Window}; using midnight", choice.Theme, window);
                choice.Theme = "midnight";
            }
            if (choice.Night != null && !ThemeIds.Contains(choice.Night))
            {
                logger.LogWarning("Saved dashboard config has unknown night theme {Night} for {Window}; using midnight", choice.Night, window);
                choice.Night = "midnight";
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
