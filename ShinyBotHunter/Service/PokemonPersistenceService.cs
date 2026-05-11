using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.InfluxDb;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ShinyBotHunter.Service
{

    public record EncounterNotification(GameFlavor game, string phase, string gameFileName);
    public class PokemonPersistenceService : IAsyncDisposable
    {
        private InfluxDbEncounterDao _encounterDao;
        private MongoCaughtPokemonDao _caughtPokemonDao;
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
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
        private ConcurrentDictionary<GameFlavor, GameEncounterCache> gameCache = new();

        public PokemonPersistenceService(InfluxDbEncounterDao encounterDao, MongoCaughtPokemonDao mongoCaughtPokemonDao)
        {
            this._encounterDao = encounterDao;
            this._caughtPokemonDao = mongoCaughtPokemonDao;
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
            var _ = Task.Run(async () =>
            {
                await ProcessEncounterAsync();
            });
            var _1 = Task.Run(async () =>
            {
                await ProcessCatchesAsync();
            });
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
                PokemonEncounters = cache.PokemonEncounterCache.Where(x => request.Species.Contains(x.Key)).Select(x => new KeyValuePair<int, PokemonCounts>(x.Key, x.Value.Clone())).ToDictionary(),
                PhaseEncounters = cache.PhaseEncounterCache.Where(x => request.Phases.Contains(x.Key)).Select(x => new KeyValuePair<string, PhaseEncounterResponse>(x.Key, new PhaseEncounterResponse()
                {
                    AllPokemonCounts = x.Value.AllPokemonCounts.Clone(),
                    PhaseCount = x.Value.PhaseCount,
                    PokemonEncounters = x.Value.PokemonEncounterCache.Where(y => request.Species.Contains(y.Key)).Select(y => new KeyValuePair<int, PokemonCounts>(y.Key, y.Value.Clone())).ToDictionary()
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
                PokemonEncounters = cache.PokemonEncounterCache.Where(x => request.Species.Contains(x.Key)).Select(x => new KeyValuePair<int, PokemonCounts>(x.Key, x.Value.Clone())).ToDictionary(),
                PhaseEncounters = cache.PhaseEncounterCache.Where(x => request.Phases.Contains(x.Key)).Select(x => new KeyValuePair<string, PhaseEncounterResponse>(x.Key, new PhaseEncounterResponse()
                {
                    AllPokemonCounts = x.Value.AllPokemonCounts.Clone(),
                    PhaseCount = x.Value.PhaseCount,
                    PokemonEncounters = x.Value.PokemonEncounterCache.Where(y => request.Species.Contains(y.Key)).Select(y => new KeyValuePair<int, PokemonCounts>(y.Key, y.Value.Clone())).ToDictionary()
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
            await GetOrLoadCacheAsync(new CacheLoadRequest(game, new List<int>(), new List<string>(), new List<string>()));
        }


        //TODO add form encounters
        private async Task<GameEncounterCache> GetOrLoadCacheAsync(CacheLoadRequest request)
        {
            if (!gameCache.ContainsKey(request.Game))
            {
                try
                {
                    await semaphore.WaitAsync();
                    if (!gameCache.ContainsKey(request.Game))
                    {
                        await LoadGameCache(request.Game);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
            var cache = gameCache[request.Game];
            foreach(var species in request.Species)
            {
                if (!cache.PokemonEncounterCache.ContainsKey(species))
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        if (!cache.PokemonEncounterCache.ContainsKey(species))
                        {
                            await LoadSpeciesCache(cache, request.Game, species);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
            }
            foreach(var phase in request.Phases)
            {
                if (!cache.PhaseEncounterCache.ContainsKey(phase))
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        if (!cache.PhaseEncounterCache.ContainsKey(phase))
                        {
                            await LoadPhaseCache(cache, request.Game, phase);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                foreach(var species in request.Species)
                {
                    if (!cache.PhaseEncounterCache[phase].PokemonEncounterCache.ContainsKey(species))
                    {
                        try
                        {
                            await semaphore.WaitAsync();
                            if (!cache.PhaseEncounterCache[phase].PokemonEncounterCache.ContainsKey(species))
                            {
                                await LoadPhaseSpeciesCache(cache, request.Game, phase, species);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                }
            }
            foreach(var gameFileName in request.GameFileNames)
            {
                await EnsureLoadGameLevelCache(cache, gameFileName, request);
            }
            return cache;
        }

        private async Task EnsureLoadGameLevelCache(GameEncounterCache gameFlavorCache, string gameFileName, CacheLoadRequest request)
        {
            if (!gameFlavorCache.GameLevelCache.ContainsKey(gameFileName))
            {
                try
                {
                    await semaphore.WaitAsync();
                    if (!gameFlavorCache.GameLevelCache.ContainsKey(gameFileName))
                    {
                        await LoadGameLevelCache(gameFlavorCache, request.Game, gameFileName);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
            var cache = gameFlavorCache.GameLevelCache[gameFileName];
            foreach (var species in request.Species)
            {
                if (!cache.PokemonEncounterCache.ContainsKey(species))
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        if (!cache.PokemonEncounterCache.ContainsKey(species))
                        {
                            await LoadGameLevelSpeciesCache(cache, gameFileName, species);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
            }
            foreach (var phase in request.Phases)
            {
                if (!cache.PhaseEncounterCache.ContainsKey(phase))
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        if (!cache.PhaseEncounterCache.ContainsKey(phase))
                        {
                            await LoadGameLevelPhaseCache(cache, gameFileName, phase);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                foreach (var species in request.Species)
                {
                    if (!cache.PhaseEncounterCache[phase].PokemonEncounterCache.ContainsKey(species))
                    {
                        try
                        {
                            await semaphore.WaitAsync();
                            if (!cache.PhaseEncounterCache[phase].PokemonEncounterCache.ContainsKey(species))
                            {
                                await LoadGameLevelPhaseSpeciesCache(cache, gameFileName, phase, species);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                }
            }
        }

        private async Task LoadPhaseSpeciesCache(GameEncounterCache cache, GameFlavor game, string phase, int species)
        {
            (long, long) encounters = await _encounterDao.GetAllPhaseSpeciesEncountersForGame(game, phase, species);
            long shiniesCaught = await _caughtPokemonDao.GetAllCaughtPhaseSpeciesShiniesCountAsync(game, phase, species);
            cache.PhaseEncounterCache[phase].PokemonEncounterCache.TryAdd(species, new PokemonCounts()
            {
                Encounters = encounters.Item1,
                ShinyEncounters = encounters.Item2,
                ShiniesCaught = shiniesCaught,
            });
        }

        private async Task LoadPhaseCache(GameEncounterCache cache, GameFlavor game, string phase)
        {
            (long, long) encounters = await _encounterDao.GetAllPhaseEncountersForGame(game, phase);
            long shiniesCaught = await _caughtPokemonDao.GetAllCaughtPhaseShiniesCountAsync(game, phase);
            int phaseCount = await _encounterDao.GetCurrentPhaseCount(game, phase);
            cache.PhaseEncounterCache.TryAdd(phase, new PhaseEncounterCache()
            {
                AllPokemonCounts = new PokemonCounts()
                {
                    Encounters = encounters.Item1,
                    ShinyEncounters = encounters.Item2,
                    ShiniesCaught = shiniesCaught,
                },
                PhaseCount = phaseCount
            });
        }

        private async Task LoadSpeciesCache(GameEncounterCache cache, GameFlavor game, int species)
        {
            (long, long) encounters = await _encounterDao.GetAllSpeciesEncountersForGame(game, species);
            long shiniesCaught = await _caughtPokemonDao.GetAllCaughtSpeciesShiniesCountAsync(game, species);
            cache.PokemonEncounterCache.TryAdd(species, new PokemonCounts()
            {
                Encounters = encounters.Item1,
                ShinyEncounters = encounters.Item2,
                ShiniesCaught = shiniesCaught,
            });
        }

        private async Task LoadGameCache(GameFlavor game)
        {
            (long, long) encounters = await _encounterDao.GetAllEncountersForGame(game);
            List<CaughtPokemon> caughtShinies = await _caughtPokemonDao.GetAllCaughtShiniesAsync(game);
            long shiniesCaught = caughtShinies.Count();
            var latestPokemon = await _encounterDao.GetLatestEncounters(game);
            var thisGameCache = new GameEncounterCache()
            {
                AllPokemonCounts = new PokemonCounts()
                {
                    Encounters = encounters.Item1,
                    ShinyEncounters = encounters.Item2,
                    ShiniesCaught = shiniesCaught,
                },
                LatestEncounters = new ConcurrentQueue<PokemonData>(),
                CaughtPokemon = new ConcurrentBag<CaughtPokemon>(caughtShinies)
            };
            for(int i = latestPokemon.Count()-1; i>= 0; i--)
            {
                thisGameCache.LatestEncounters.Enqueue(latestPokemon[i]);
            }
            gameCache.TryAdd(game, thisGameCache);

        }

        private async Task LoadGameLevelCache(GameEncounterCache gameFlavorCache, GameFlavor gameFlavor, string gameFileName)
        {
            (long, long) encounters = encounters = await _encounterDao.GetAllEncountersForGameName(gameFileName);
            // TODO filter by game on query to DB is faster.
            List<CaughtPokemon> caughtShinies = await _caughtPokemonDao.GetAllCaughtShiniesAsync(gameFlavor);
            caughtShinies = caughtShinies.Where(x => x.GameFileName == gameFileName).ToList();
            long shiniesCaught = caughtShinies.Count();
            var thisGameCache = new GameEncounterCacheBase()
            {
                AllPokemonCounts = new PokemonCounts()
                {
                    Encounters = encounters.Item1,
                    ShinyEncounters = encounters.Item2,
                    ShiniesCaught = shiniesCaught,
                },
                CaughtPokemon = new ConcurrentBag<CaughtPokemon>(caughtShinies)
            };
            gameFlavorCache.GameLevelCache.TryAdd(gameFileName, thisGameCache);
        }

        private async Task LoadGameLevelPhaseSpeciesCache(GameEncounterCacheBase cache, string gameFileName, string phase, int species)
        {
            (long, long) encounters = await _encounterDao.GetAllPhaseSpeciesEncountersForGameName(gameFileName, phase, species);
            long shiniesCaught = await _caughtPokemonDao.GetAllCaughtPhaseSpeciesShiniesCountAsync(gameFileName, phase, species);
            cache.PhaseEncounterCache[phase].PokemonEncounterCache.TryAdd(species, new PokemonCounts()
            {
                Encounters = encounters.Item1,
                ShinyEncounters = encounters.Item2,
                ShiniesCaught = shiniesCaught,
            });
        }

        private async Task LoadGameLevelPhaseCache(GameEncounterCacheBase cache, string gameFileName, string phase)
        {
            (long, long) encounters = await _encounterDao.GetAllPhaseEncountersForGameName(gameFileName, phase);
            long shiniesCaught = await _caughtPokemonDao.GetAllCaughtPhaseShiniesCountAsync(gameFileName, phase);
            int phaseCount = await _encounterDao.GetCurrentPhaseCount(gameFileName, phase);
            cache.PhaseEncounterCache.TryAdd(phase, new PhaseEncounterCache()
            {
                AllPokemonCounts = new PokemonCounts()
                {
                    Encounters = encounters.Item1,
                    ShinyEncounters = encounters.Item2,
                    ShiniesCaught = shiniesCaught,
                },
                PhaseCount = phaseCount
            });
        }

        private async Task LoadGameLevelSpeciesCache(GameEncounterCacheBase cache, string gameFileName, int species)
        {
            (long, long) encounters = await _encounterDao.GetAllSpeciesEncountersForGameName(gameFileName, species);
            long shiniesCaught = await _caughtPokemonDao.GetAllCaughtSpeciesShiniesCountAsync(gameFileName, species);
            cache.PokemonEncounterCache.TryAdd(species, new PokemonCounts()
            {
                Encounters = encounters.Item1,
                ShinyEncounters = encounters.Item2,
                ShiniesCaught = shiniesCaught,
            });
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
        public CacheLoadRequest(GameFlavor game, List<int> species, List<string> phases, List<string> gameFileNames)
        {
            Game = game;
            Species = species;
            Phases = phases;
            GameFileNames = gameFileNames;
        }

        public CacheLoadRequest(PersistPokemonData pokemonData) : this(new List<PersistPokemonData>() { pokemonData }) { }

        public CacheLoadRequest(List<PersistPokemonData> pokemonData)
        {
            Game = pokemonData[0].Game;
            HashSet<int> species = new HashSet<int>();
            HashSet<string> phases = new HashSet<string>();
            HashSet<string> gameNames = new HashSet<string>();
            foreach(var pokemon in pokemonData)
            {
                species.Add(pokemon.PokemonData.Species);
                phases.Add(pokemon.Phase);
                gameNames.Add(pokemon.GameFileName);
            }
            Species = species.ToList();
            Phases = phases.ToList();
            GameFileNames = gameNames.ToList();
        }

        public GameFlavor Game { get; private set; }
        public List<int> Species { get; private set;  }
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
        public Dictionary<int, PokemonCounts> PokemonEncounters { get; set; } = new();
        public Dictionary<string, PhaseEncounterResponse> PhaseEncounters { get; set; } = new();
        public CaughtPokemon[] CaughtPokemons { get; set; } = [];
    }
    public class PhaseEncounterResponse
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public Dictionary<int, PokemonCounts> PokemonEncounters { get; set; } = new();
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
        public ConcurrentDictionary<int, PokemonCounts> PokemonEncounterCache { get; set; } = new();
        public ConcurrentBag<CaughtPokemon> CaughtPokemon { get; set; } = new();

        internal virtual void Increment(PersistPokemonData encounter)
        {
            AllPokemonCounts.Increment(encounter);
            PokemonEncounterCache[encounter.PokemonData.Species].Increment(encounter);
            PhaseEncounterCache[encounter.Phase].Increment(encounter);
        }
        internal virtual void Increment(CaughtPokemon caughtPokemon, PersistPokemonData encounter)
        {
            AllPokemonCounts.Increment(caughtPokemon, encounter);
            CaughtPokemon.Add(caughtPokemon);
            PhaseEncounterCache[encounter.Phase].Increment(caughtPokemon, encounter);
            PokemonEncounterCache[encounter.PokemonData.Species].Increment(caughtPokemon, encounter);
        }
    }

    public class PhaseEncounterCache
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public ConcurrentDictionary<int, PokemonCounts> PokemonEncounterCache { get; set; } = new();
        private int phaseCount = 0;
        public int PhaseCount { get => phaseCount; set => Interlocked.Exchange(ref phaseCount, value); }

        internal void Increment(PersistPokemonData encounter)
        {
            AllPokemonCounts.Increment(encounter);
            PokemonEncounterCache[encounter.PokemonData.Species].Increment(encounter);
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
            PokemonEncounterCache[encounter.PokemonData.Species].Increment(caughtPokemon, encounter);
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
