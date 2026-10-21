using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace ShinyBotHunter.Service
{

    public record EncounterNotification(GameFlavor game, string phase, string gameFileName);
    public class PokemonPersistenceService : IAsyncDisposable
    {
        private IEncounterDao _encounterDao;
        private MongoCaughtPokemonDao _caughtPokemonDao;
        private readonly ILogger<PokemonPersistenceService> _logger;
        // One per game, held only while that game's tree is loaded, so two games' cold loads do not wait on each other.
        private readonly ConcurrentDictionary<GameFlavor, SemaphoreSlim> loadLocks = new();
        private List<INotify<EncounterNotification, CacheLoadRequest, GameEncounterResponse>> notifies;
        private Channel<List<PersistPokemonData>> EncounterChannel { get; set; }
        private ConcurrentBag<PokemonData> CaughtPokemon { get; set; }
        private Channel<PersistPokemonData> CaughtChannel { get; set; }
        /**
         * Game
         *   |
         *   -> Phase
         *          |
         *          -> Pokemon
         *          -> All Pokemon
         *   -> All Phases
         *          |
         *          -> Pokemon
         *          -> All Pokemon
        **/
        // Written once per game under that game's load lock; read lock-free. Nodes are added with GetOrAdd and counts change only through Interlocked.
        private ConcurrentDictionary<GameFlavor, GameEncounterCache> gameCache = new();

        public PokemonPersistenceService(IEncounterDao encounterDao, MongoCaughtPokemonDao mongoCaughtPokemonDao, ILogger<PokemonPersistenceService> logger)
        {
            this._encounterDao = encounterDao;
            this._caughtPokemonDao = mongoCaughtPokemonDao;
            _logger = logger;
            EncounterChannel = Channel.CreateUnbounded<List<PersistPokemonData>>();
            CaughtChannel = Channel.CreateUnbounded<PersistPokemonData>();
            notifies = new List<INotify<EncounterNotification, CacheLoadRequest, GameEncounterResponse>>();
            CaughtPokemon = new ConcurrentBag<PokemonData>();
        }

        public async Task AddEncounterAsync(PersistPokemonData pokemonData)
        {
            await AddEncounterAsync(new List<PersistPokemonData> { pokemonData });
        }

        public async Task AddEncounterAsync(List<PersistPokemonData> pokemonData)
        {
            await EncounterChannel.Writer.WriteAsync(pokemonData);
        }

        public async Task AddCaughtPokemonAsync(PersistPokemonData pokemonData)
        {
            await CaughtChannel.Writer.WriteAsync(pokemonData);
        }

        public void Init()
        {
            _ = Task.Run(() => BackgroundLoop.RunSupervisedAsync(ProcessEncounterAsync, _logger, nameof(ProcessEncounterAsync)));
            _ = Task.Run(() => BackgroundLoop.RunSupervisedAsync(ProcessCatchesAsync, _logger, nameof(ProcessCatchesAsync)));
        }

        public async Task ProcessEncounterAsync()
        {
            await foreach(var encounters in EncounterChannel.Reader.ReadAllAsync())
            {
                await GetOrLoadCacheAsync(new CacheLoadRequest(encounters));
                foreach (var encounter in encounters)
                {
                    await AddEncounterToCacheAsync(encounter);
                }
                await _encounterDao.WriteToQueueAsync(encounters);
                await NotifyEncountersAsync(encounters[0].Game, encounters[0].Phase, encounters[0].GameFileName);
            }
        }

        private async Task NotifyEncountersAsync(GameFlavor game, string phase, string gameFileName)
        {
            foreach(var notify in notifies)
            {
                var gameEvent = new EncounterNotification(game, phase, gameFileName);
                CacheLoadRequest request = await notify.BuildNotificationRequestAsync(gameEvent);
                GameEncounterResponse rsp = await DashboardLoadRequestAsync(request);
                await notify.NotifyAsync(rsp, gameEvent);
            }
        }

        public void SubscribeToNotifications(INotify<EncounterNotification, CacheLoadRequest, GameEncounterResponse> notify)
        {
            this.notifies.Add(notify);
        }

        public async Task ProcessCatchesAsync()
        {
            await foreach(var pokemon in CaughtChannel.Reader.ReadAllAsync())
            {
                var caughtPokemon = _caughtPokemonDao.CreateCaughtPokemonEntry(pokemon);
                await AddCatchToCacheAsync(pokemon, caughtPokemon);
                await _caughtPokemonDao.AddCaughtPokemon(caughtPokemon);
                await NotifyEncountersAsync(caughtPokemon.Game, caughtPokemon.Phase, caughtPokemon.GameFileName);
            }
        }

        // this method clones all instances of pokemon counts so it's not mutable by outside sources.
        public async Task<GameEncounterResponse> DashboardLoadRequestAsync(CacheLoadRequest request)
        {
            var cache = await GetOrLoadCacheAsync(request);
            var toReturn = new GameEncounterResponse()
            {
                AllPokemonCounts = cache.AllPokemonCounts.Clone(),
                PokemonEncounters = cache.PokemonEncounterCache.Where(x => request.FormCodes.Contains(x.Key)).Select(x => new KeyValuePair<string, PokemonCounts>(x.Key, x.Value.Clone())).ToDictionary(),
                PhaseEncounters = cache.PhaseEncounterCache.Where(x => request.Phases.Contains(x.Key)).Select(x => new KeyValuePair<string, PhaseEncounterResponse>(x.Key, new PhaseEncounterResponse()
                {
                    AllPokemonCounts = x.Value.AllPokemonCounts.Clone(),
                    PhaseCount = x.Value.PhaseCount,
                    PokemonEncounters = x.Value.PokemonEncounterCache.Where(y => request.FormCodes.Contains(y.Key)).Select(y => new KeyValuePair<string, PokemonCounts>(y.Key, y.Value.Clone())).ToDictionary()
                })).ToDictionary(),
                LatestEncounters = cache.LatestEncounters.Reverse().ToArray(),
                CaughtPokemons = cache.CaughtPokemon.ToArray()
            };
            // TODO game level stuff
            foreach(var gameFileName in request.GameFileNames)
            {
                toReturn.GameSpecificResponse.Add(gameFileName, LoadGameLevelDashboard(cache.GameLevelCache[gameFileName], gameFileName, request));
            }
            return toReturn;
        }

        private GameEncounterResponseBase LoadGameLevelDashboard(GameEncounterCacheBase cache, string gameFileName, CacheLoadRequest request)
        {
            var toReturn = new GameEncounterResponseBase()
            {
                AllPokemonCounts = cache.AllPokemonCounts.Clone(),
                PokemonEncounters = cache.PokemonEncounterCache.Where(x => request.FormCodes.Contains(x.Key)).Select(x => new KeyValuePair<string, PokemonCounts>(x.Key, x.Value.Clone())).ToDictionary(),
                PhaseEncounters = cache.PhaseEncounterCache.Where(x => request.Phases.Contains(x.Key)).Select(x => new KeyValuePair<string, PhaseEncounterResponse>(x.Key, new PhaseEncounterResponse()
                {
                    AllPokemonCounts = x.Value.AllPokemonCounts.Clone(),
                    PhaseCount = x.Value.PhaseCount,
                    PokemonEncounters = x.Value.PokemonEncounterCache.Where(y => request.FormCodes.Contains(y.Key)).Select(y => new KeyValuePair<string, PokemonCounts>(y.Key, y.Value.Clone())).ToDictionary()
                })).ToDictionary(),
                CaughtPokemons = cache.CaughtPokemon.ToArray()
            };
            return toReturn;
        }

        private async Task AddEncounterToCacheAsync(PersistPokemonData encounter)
        {
            var cache = await GetOrLoadCacheAsync(new CacheLoadRequest(encounter));
            PokemonData? remove;
            if (cache.LatestEncounters.Count() > 19)
            {
                cache.LatestEncounters.TryDequeue(out remove);
            }
            cache.LatestEncounters.Enqueue(encounter.PokemonData);
            cache.Increment(encounter);
        }

        private async Task AddCatchToCacheAsync(PersistPokemonData encounter, CaughtPokemon caughtPokemon)
        {
            var cache = await GetOrLoadCacheAsync(new CacheLoadRequest(encounter));
            cache.Increment(caughtPokemon, encounter);
        }

        public async Task<List<CaughtPokemon>> GetCaughtPokemonAsync(GameFlavor game)
        {
            await LoadCacheAsync(game);
            return this.gameCache[game].CaughtPokemon.ToList();
        }

        private async Task LoadCacheAsync(GameFlavor game)
        {
            await GetOrLoadCacheAsync(new CacheLoadRequest(game, new List<string>(), new List<string>(), new List<string>()));
        }


        private async Task<GameEncounterCache> GetOrLoadCacheAsync(CacheLoadRequest request)
        {
            if (!gameCache.TryGetValue(request.Game, out var cache))
            {
                var loadLock = loadLocks.GetOrAdd(request.Game, _ => new SemaphoreSlim(1, 1));
                await loadLock.WaitAsync();
                try
                {
                    if (!gameCache.TryGetValue(request.Game, out cache))
                    {
                        cache = await LoadGameCacheAsync(request.Game);
                        gameCache[request.Game] = cache;
                    }
                }
                finally
                {
                    loadLock.Release();
                }
            }
            EnsureKeys(cache, request);
            return cache;
        }

        private async Task<GameEncounterCache> LoadGameCacheAsync(GameFlavor game)
        {
            var stopwatch = Stopwatch.StartNew();
            List<CountRow> counts = await _encounterDao.GetCountsAsync(game);
            List<ProgressRow> progress = await _encounterDao.GetPhaseProgressAsync(game);
            List<PokemonData> latest = await _encounterDao.GetLatestEncounters(game);
            List<CaughtPokemon> caught = await _caughtPokemonDao.GetAllCaughtShiniesAsync(game);
            var cache = BuildGameCache(counts, progress, caught, latest);
            _logger.LogInformation("Loaded {Game} counts from 3 Postgres queries and 1 Mongo find: {CountRows} count rows, {ProgressRows} progress rows, {Caught} caught, in {ElapsedMs} ms",
                game, counts.Count, progress.Count, caught.Count, stopwatch.ElapsedMilliseconds);
            return cache;
        }

        // Builds a game's whole tree from the grouped rows. Every row counts under its form code and its "#species"
        // key, at game and phase level, on the game node and on its ROM copy's node. Single writer on an unpublished tree.
        internal static GameEncounterCache BuildGameCache(List<CountRow> counts, List<ProgressRow> progress, List<CaughtPokemon> caught, List<PokemonData> latest)
        {
            var cache = new GameEncounterCache()
            {
                CaughtPokemon = new ConcurrentBag<CaughtPokemon>(caught)
            };
            for (int i = latest.Count - 1; i >= 0; i--)
            {
                cache.LatestEncounters.Enqueue(latest[i]);
            }
            foreach (var row in counts)
            {
                AddCounts(cache, row.Milestone, row.FormCode, row.Species, row.Encounters, row.ShinyEncounters, 0);
                AddCounts(cache.GameLevelCache.GetOrAdd(row.GameFileName, _ => new GameEncounterCacheBase()), row.Milestone, row.FormCode, row.Species, row.Encounters, row.ShinyEncounters, 0);
            }
            foreach (var caughtPokemon in caught)
            {
                AddCounts(cache, caughtPokemon.Phase, caughtPokemon.Pokemon.FormCode, caughtPokemon.Pokemon.Species, 0, 0, 1);
                // Documents written before GameFileName existed (2026-05-01) load with it null: they count for the game only.
                if (string.IsNullOrEmpty(caughtPokemon.GameFileName))
                {
                    continue;
                }
                var fileNode = cache.GameLevelCache.GetOrAdd(caughtPokemon.GameFileName, _ => new GameEncounterCacheBase());
                AddCounts(fileNode, caughtPokemon.Phase, caughtPokemon.Pokemon.FormCode, caughtPokemon.Pokemon.Species, 0, 0, 1);
                fileNode.CaughtPokemon.Add(caughtPokemon);
            }
            foreach (var row in progress)
            {
                GameEncounterCacheBase node = row.GameFileName == null
                    ? cache
                    : cache.GameLevelCache.GetOrAdd(row.GameFileName, _ => new GameEncounterCacheBase());
                node.PhaseEncounterCache.GetOrAdd(row.Milestone, _ => new PhaseEncounterCache()).PhaseCount = row.SinceLastShiny;
            }
            return cache;
        }

        private static void AddCounts(GameEncounterCacheBase node, string phase, string formCode, int species, long encounters, long shinyEncounters, long caught)
        {
            string speciesKey = CacheLoadRequest.SpeciesKey(species);
            var phaseNode = node.PhaseEncounterCache.GetOrAdd(phase, _ => new PhaseEncounterCache());
            PokemonCounts[] targets =
            [
                node.AllPokemonCounts,
                node.PokemonEncounterCache.GetOrAdd(formCode, _ => new PokemonCounts()),
                node.PokemonEncounterCache.GetOrAdd(speciesKey, _ => new PokemonCounts()),
                phaseNode.AllPokemonCounts,
                phaseNode.PokemonEncounterCache.GetOrAdd(formCode, _ => new PokemonCounts()),
                phaseNode.PokemonEncounterCache.GetOrAdd(speciesKey, _ => new PokemonCounts()),
            ];
            foreach (var target in targets)
            {
                target.Encounters += encounters;
                target.ShinyEncounters += shinyEncounters;
                target.ShiniesCaught += caught;
            }
        }

        // A key the request names but no row produced gets a zero node, so increments have somewhere to land.
        internal static void EnsureKeys(GameEncounterCache cache, CacheLoadRequest request)
        {
            void Ensure(GameEncounterCacheBase node)
            {
                foreach (var formCode in request.FormCodes)
                {
                    node.PokemonEncounterCache.GetOrAdd(formCode, _ => new PokemonCounts());
                }
                foreach (var phase in request.Phases)
                {
                    var phaseNode = node.PhaseEncounterCache.GetOrAdd(phase, _ => new PhaseEncounterCache());
                    foreach (var formCode in request.FormCodes)
                    {
                        phaseNode.PokemonEncounterCache.GetOrAdd(formCode, _ => new PokemonCounts());
                    }
                }
            }

            Ensure(cache);
            foreach (var gameFileName in request.GameFileNames)
            {
                Ensure(cache.GameLevelCache.GetOrAdd(gameFileName, _ => new GameEncounterCacheBase()));
            }
        }

        public async ValueTask DisposeAsync()
        {
            EncounterChannel.Writer.TryComplete();
            await EncounterChannel.Reader.Completion;
            CaughtChannel.Writer.TryComplete();
            await CaughtChannel.Reader.Completion;
        }
    }

    public class CacheLoadRequest
    {
        public CacheLoadRequest(GameFlavor game, List<string> formCodes, List<string> phases, List<string> gameFileNames)
        {
            Game = game;
            FormCodes = formCodes;
            Phases = phases;
            GameFileNames = gameFileNames;
        }

        public CacheLoadRequest(PersistPokemonData pokemonData) : this(new List<PersistPokemonData>() { pokemonData }) { }

        public CacheLoadRequest(List<PersistPokemonData> pokemonData)
        {
            Game = pokemonData[0].Game;
            HashSet<string> formCodes = new HashSet<string>();
            HashSet<string> phases = new HashSet<string>();
            HashSet<string> gameNames = new HashSet<string>();
            foreach(var pokemon in pokemonData)
            {
                formCodes.Add(pokemon.PokemonData.FormCode);
                formCodes.Add(SpeciesKey(pokemon.PokemonData.Species));
                phases.Add(pokemon.Phase);
                gameNames.Add(pokemon.GameFileName);
            }
            FormCodes = formCodes.ToList();
            Phases = phases.ToList();
            GameFileNames = gameNames.ToList();
        }

        /// <summary>
        /// Counts are cached per "pokemon key". A milestone row that pins a form is counted by form
        /// code; one that accepts any form is counted across the whole species, keyed as "#id". Two
        /// namespaces in one dictionary rather than two parallel caches, since a key is only ever
        /// one or the other.
        /// </summary>
        public static string SpeciesKey(int speciesId) => "#" + speciesId;

        public GameFlavor Game { get; private set; }
        public List<string> FormCodes { get; private set;  }
        public List<string> Phases { get; private set; }
        public List<string> GameFileNames { get; private set; } = new List<string>();
    }

    public class GameEncounterResponse : GameEncounterResponseBase
    {
        public PokemonData[] LatestEncounters { get; set; } = [];
        public Dictionary<string, GameEncounterResponseBase> GameSpecificResponse { get; set; } = new();
    }

    public class GameEncounterResponseBase
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        // Keyed by form code, not species id: a milestone can list several forms of one species
        // (male/female, or Unown's 28), and each row needs its own counts.
        public Dictionary<string, PokemonCounts> PokemonEncounters { get; set; } = new();
        public Dictionary<string, PhaseEncounterResponse> PhaseEncounters { get; set; } = new();
        public CaughtPokemon[] CaughtPokemons { get; set; } = [];
    }
    public class PhaseEncounterResponse
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        // Keyed by form code, not species id: a milestone can list several forms of one species
        // (male/female, or Unown's 28), and each row needs its own counts.
        public Dictionary<string, PokemonCounts> PokemonEncounters { get; set; } = new();
        public int PhaseCount { get; set; } = 0;
    }

    public class GameEncounterCache : GameEncounterCacheBase
    {
        public ConcurrentQueue<PokemonData> LatestEncounters { get; set; } = new();
        public ConcurrentDictionary<string, GameEncounterCacheBase> GameLevelCache { get; set; } = new();

        internal override void Increment(PersistPokemonData encounter)
        {
            base.Increment(encounter);
            GameLevelCache[encounter.GameFileName].Increment(encounter);
        }

        internal override void Increment(CaughtPokemon caughtPokemon, PersistPokemonData encounter)
        {
            base.Increment(caughtPokemon, encounter);
            GameLevelCache[encounter.GameFileName].Increment(caughtPokemon, encounter);
        }
    }

    public class GameEncounterCacheBase
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public ConcurrentDictionary<string, PhaseEncounterCache> PhaseEncounterCache { get; set; } = new();
        public ConcurrentDictionary<string, PokemonCounts> PokemonEncounterCache { get; set; } = new();
        public ConcurrentBag<CaughtPokemon> CaughtPokemon { get; set; } = new();

        internal virtual void Increment(PersistPokemonData encounter)
        {
            AllPokemonCounts.Increment(encounter);
            PokemonEncounterCache[encounter.PokemonData.FormCode].Increment(encounter);
            PokemonEncounterCache[CacheLoadRequest.SpeciesKey(encounter.PokemonData.Species)].Increment(encounter);
            PhaseEncounterCache[encounter.Phase].Increment(encounter);
        }
        internal virtual void Increment(CaughtPokemon caughtPokemon, PersistPokemonData encounter)
        {
            AllPokemonCounts.Increment(caughtPokemon, encounter);
            CaughtPokemon.Add(caughtPokemon);
            PhaseEncounterCache[encounter.Phase].Increment(caughtPokemon, encounter);
            PokemonEncounterCache[encounter.PokemonData.FormCode].Increment(caughtPokemon, encounter);
            PokemonEncounterCache[CacheLoadRequest.SpeciesKey(encounter.PokemonData.Species)].Increment(caughtPokemon, encounter);
        }
    }

    public class PhaseEncounterCache
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public ConcurrentDictionary<string, PokemonCounts> PokemonEncounterCache { get; set; } = new();
        private int phaseCount = 0;
        public int PhaseCount { get => phaseCount; set => Interlocked.Exchange(ref phaseCount, value); }

        internal void Increment(PersistPokemonData encounter)
        {
            AllPokemonCounts.Increment(encounter);
            PokemonEncounterCache[encounter.PokemonData.FormCode].Increment(encounter);
            PokemonEncounterCache[CacheLoadRequest.SpeciesKey(encounter.PokemonData.Species)].Increment(encounter);
            if (encounter.PokemonData.IsShiny)
            {
                PhaseCount = 0;
            }
            else
            {
                Interlocked.Increment(ref phaseCount);
            }
        }

        internal void Increment(CaughtPokemon caughtPokemon, PersistPokemonData encounter)
        {
            AllPokemonCounts.Increment(caughtPokemon, encounter);
            PokemonEncounterCache[encounter.PokemonData.FormCode].Increment(caughtPokemon, encounter);
            PokemonEncounterCache[CacheLoadRequest.SpeciesKey(encounter.PokemonData.Species)].Increment(caughtPokemon, encounter);
        }
    }

    public class PokemonCounts
    {
        private long encounters = 0;
        private long shinyEncounters = 0;
        private long shiniesCaught = 0;
        public long Encounters { get => encounters; set => Interlocked.Exchange(ref encounters, value); }
        public long ShinyEncounters { get => shinyEncounters; set => Interlocked.Exchange(ref shinyEncounters, value); }
        public long ShiniesCaught { get => shiniesCaught; set => Interlocked.Exchange(ref shiniesCaught, value); }

        public PokemonCounts Clone()
        {
            return new PokemonCounts()
            {
                Encounters = Encounters,
                ShinyEncounters = ShinyEncounters,
                ShiniesCaught = ShiniesCaught,
            };
        }

        internal void Increment(PersistPokemonData encounter)
        {
            Interlocked.Increment(ref encounters);
            if (encounter.PokemonData.IsShiny)
            {
                Interlocked.Increment(ref shinyEncounters);
            }
        }

        internal void Increment(CaughtPokemon caughtPokemon, PersistPokemonData encounter)
        {
            if (caughtPokemon.Pokemon.IsShiny)
            {
                Interlocked.Increment(ref shiniesCaught);
            }
        }
    }
}
