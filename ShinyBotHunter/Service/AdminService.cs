using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Persistence.MongoDb;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{
    public class AdminService
    {
        private static readonly string[] ThemeIds = ["midnight", "hgss-drift", "hgss-dusk", "rs-drift", "ruby", "sapphire", "christmas", "christmas-night"];

        // Guards gamePhases, RecentEncounterCount and appearance. Held only around the mutation or the snapshot copy, never across an await.
        private readonly object gamePhasesLock = new();
        // Mutated by concurrent dashboard requests (subscribe/unsubscribe) and by FollowHuntAsync (a hunt's start and each Chained leg, on bot threads), and copied by UpdateDashboardsAsync (requests and every SignalR connect): read and written only under gamePhasesLock.
        private readonly HashSet<GamePhase> gamePhases = new HashSet<GamePhase>();
        // Set once in the constructor; the hub context and the MongoDB collection behind them are thread-safe,
        // and the appearance file is touched only inside saveGate and once in InitAsync before the host starts.
        private readonly Func<DashboardConfig, Task> push;
        private readonly Func<Task<(DashboardConfig Config, Appearance? Appearance)?>> loadShared;
        private readonly Func<DashboardConfig, Task> saveShared;
        private readonly Func<Task<Appearance?>> loadAppearance;
        private readonly Func<Appearance, Task> saveAppearance;
        private readonly ILogger logger;
        // Written by dashboard requests, read by UpdateDashboardsAsync: read and written only under gamePhasesLock so a pushed config is one consistent snapshot.
        private int RecentEncounterCount = 10;
        // Replaced whole by SetAppearanceAsync and InitAsync, never changed in place, so a snapshot may share it; read and written only under gamePhasesLock.
        private Appearance appearance = new();
        // Orders the saves: held from taking the snapshot to the end of the write, so the last write is always the newest snapshot.
        // A semaphore, not a lock, because it is held across an await; gamePhasesLock is taken inside it only for the copy.
        private readonly SemaphoreSlim saveGate = new(1, 1);

        public AdminService(IHubContext<ElectronHub, IElectronClient> hubContext, MongoDashboardConfigDao dao, AppearanceFile file, ILogger<AdminService> logger)
            : this(config => hubContext.Clients.All.ReceiveDashboardConfig(config), dao.LoadAsync, dao.SaveAsync, file.LoadAsync, file.SaveAsync, logger)
        {
        }

        // The test seam for the SignalR, MongoDB and appearance file boundaries.
        internal AdminService(
            Func<DashboardConfig, Task> push,
            Func<Task<(DashboardConfig Config, Appearance? Appearance)?>> loadShared,
            Func<DashboardConfig, Task> saveShared,
            Func<Task<Appearance?>> loadAppearance,
            Func<Appearance, Task> saveAppearance,
            ILogger logger)
        {
            this.push = push;
            this.loadShared = loadShared;
            this.saveShared = saveShared;
            this.loadAppearance = loadAppearance;
            this.saveAppearance = saveAppearance;
            this.logger = logger;
        }

        // Restores the saved config. Runs before the host starts, so no dashboard is connected yet and nothing is pushed.
        // With no file, an older document's appearance is used and copied to the file once (appearance-per-pc FR-8).
        public async Task InitAsync()
        {
            var shared = await loadShared();
            Appearance? fromFile = await loadAppearance();
            Appearance? copied = fromFile == null ? shared?.Appearance : null;
            Appearance restored = Normalise(fromFile ?? copied ?? new Appearance());
            lock (gamePhasesLock)
            {
                if (shared != null)
                {
                    gamePhases.UnionWith(shared.Value.Config.GamePhases);
                    RecentEncounterCount = shared.Value.Config.RecentEncounterCount;
                }
                appearance = restored;
            }
            if (copied != null)
            {
                await saveAppearance(restored);
            }
        }

        // FR-38d: a game file has at most one follows-the-hunt card, so a second one replaces the first's milestone.
        public async Task SubscribeGamePhaseAsync(string game, string phase, string? gameFileName, bool followsHunt = false)
        {
            GamePhase gp = new GamePhase(Enum.Parse<GameFlavor>(game), phase, gameFileName) { followsHunt = followsHunt };
            lock (gamePhasesLock)
            {
                if (followsHunt)
                {
                    gamePhases.RemoveWhere(x => x.followsHunt && x.game == gp.game && x.gameFileName == gameFileName);
                }
                gamePhases.Add(gp);
            }
            await UpdateDashboardsAsync();
            await SaveAsync(saveShared);
        }

        // FR-40: the file's follows-the-hunt card moves to the hunted milestone in one push and one save; with no such card, or one already there, nothing happens.
        public async Task FollowHuntAsync(GameFlavor game, string gameFileName, string phase)
        {
            lock (gamePhasesLock)
            {
                GamePhase? card = gamePhases.FirstOrDefault(x => x.followsHunt && x.game == game && x.gameFileName == gameFileName);
                if (card == null || card.phase == phase)
                {
                    return;
                }
                gamePhases.Remove(card);
                gamePhases.Add(card with { phase = phase });
            }
            // A dashboard card never stops or fails a hunt: a failed push or save is logged and the move stays in memory.
            try
            {
                await UpdateDashboardsAsync();
                await SaveAsync(saveShared);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Moving the follows-the-hunt card of {Game} {GameFile} to {Phase} was not pushed or saved", game, gameFileName, phase);
            }
        }

        // A follows-the-hunt card is removed whatever milestone it shows now, as subscribe replaces it whatever it shows.
        public async Task UnsubscribeGamePhaseAsync(string game, string phase, string? gameFileName, bool followsHunt = false)
        {
            GamePhase gp = new GamePhase(Enum.Parse<GameFlavor>(game), phase, gameFileName) { followsHunt = followsHunt };
            bool removed;
            lock (gamePhasesLock)
            {
                removed = followsHunt
                    ? gamePhases.RemoveWhere(x => x.followsHunt && x.game == gp.game && x.gameFileName == gameFileName) > 0
                    : gamePhases.Remove(gp);
            }
            if (removed)
            {
                await UpdateDashboardsAsync();
                await SaveAsync(saveShared);
            }
        }

        public async Task UpdateRecentEncounterCount(int count)
        {
            lock (gamePhasesLock)
            {
                RecentEncounterCount = count;
            }
            await UpdateDashboardsAsync();
            await SaveAsync(saveShared);
        }

        public async Task SetAppearanceAsync(Appearance appearance)
        {
            lock (gamePhasesLock)
            {
                this.appearance = appearance;
            }
            await UpdateDashboardsAsync();
            await SaveAsync(config => saveAppearance(config.Appearance));
        }

        // Pushes only; a SignalR connect calls this and never saves.
        public async Task UpdateDashboardsAsync()
        {
            await push(Snapshot());
        }

        // Runs after the push, so a failed write still leaves every dashboard showing the state in memory.
        private async Task SaveAsync(Func<DashboardConfig, Task> save)
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
