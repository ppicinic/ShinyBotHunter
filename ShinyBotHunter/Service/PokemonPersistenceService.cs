using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.InfluxDb;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ShinyBotHunter.Service
{

    public record EncounterNotification(GameFlavor game, string phase);
    public class PokemonPersistenceService : IAsyncDisposable
    {
        private InfluxDbEncounterDao _encounterDao;
        private MongoCaughtPokemonDao _caughtPokemonDao;
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private List<INotify<EncounterNotification, CacheLoadRequest, GameEncounterResponse>> notifies;
        private Channel<List<PersistPokemonData>> EncounterChannel { get; set; }
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
                await NotifyEncountersAsync(encounters[0].Game, encounters[0].Phase);
            }
        }

        private async Task NotifyEncountersAsync(GameFlavor game, string phase)
        {
            foreach(var notify in notifies)
            {
                var gameEvent = new EncounterNotification(game, phase);
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
            await foreach(var caughtPokemon in CaughtChannel.Reader.ReadAllAsync())
            {
                await AddCatchToCacheAsync(caughtPokemon);
                await _caughtPokemonDao.AddCaughtPokemon(caughtPokemon);
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
                    PokemonEncounters = x.Value.PokemonEncounterCache.Where(y => request.Species.Contains(y.Key)).Select(y => new KeyValuePair<int, PokemonCounts>(y.Key, y.Value.Clone())).ToDictionary()
                })).ToDictionary(),
                LatestEncounters = cache.LatestEncounters.Reverse().ToArray()
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
            cache.AllPokemonCounts.Encounters += 1;
            cache.PhaseEncounterCache[encounter.Phase].AllPokemonCounts.Encounters += 1;
            cache.PokemonEncounterCache[encounter.PokemonData.Species].Encounters += 1;
            cache.PhaseEncounterCache[encounter.Phase].PokemonEncounterCache[encounter.PokemonData.Species].Encounters += 1;
            if (encounter.PokemonData.IsShiny)
            {
                cache.AllPokemonCounts.ShinyEncounters += 1;
                cache.PhaseEncounterCache[encounter.Phase].AllPokemonCounts.ShinyEncounters += 1;
                cache.PokemonEncounterCache[encounter.PokemonData.Species].ShinyEncounters += 1;
                cache.PhaseEncounterCache[encounter.Phase].PokemonEncounterCache[encounter.PokemonData.Species].ShinyEncounters += 1;
            }
        }

        private async Task AddCatchToCacheAsync(PersistPokemonData encounter)
        {
            var cache = await GetOrLoadCacheAsync(new CacheLoadRequest(encounter));
            cache.AllPokemonCounts.ShiniesCaught += 1;
            cache.PhaseEncounterCache[encounter.Phase].AllPokemonCounts.ShiniesCaught += 1;
            cache.PokemonEncounterCache[encounter.PokemonData.Species].ShiniesCaught += 1;
            cache.PhaseEncounterCache[encounter.Phase].PokemonEncounterCache[encounter.PokemonData.Species].ShiniesCaught += 1;
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
            return cache;
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
            cache.PhaseEncounterCache.TryAdd(phase, new PhaseEncounterCache()
            {
                AllPokemonCounts = new PokemonCounts()
                {
                    Encounters = encounters.Item1,
                    ShinyEncounters = encounters.Item2,
                    ShiniesCaught = shiniesCaught,
                } 
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
            long shiniesCaught = await _caughtPokemonDao.GetAllCaughtShiniesCountAsync(game);
            var latestPokemon = await _encounterDao.GetLatestEncounters(game);
            var thisGameCache = new GameEncounterCache()
            {
                AllPokemonCounts = new PokemonCounts()
                {
                    Encounters = encounters.Item1,
                    ShinyEncounters = encounters.Item2,
                    ShiniesCaught = shiniesCaught,
                },
                LatestEncounters = new ConcurrentQueue<PokemonData>()
            };
            for(int i = latestPokemon.Count()-1; i>= 0; i--)
            {
                thisGameCache.LatestEncounters.Enqueue(latestPokemon[i]);
            }
            gameCache.TryAdd(game, thisGameCache);

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
        public CacheLoadRequest(GameFlavor game, List<int> species, List<string> phases)
        {
            Game = game;
            Species = species;
            Phases = phases;
        }

        public CacheLoadRequest(PersistPokemonData pokemonData) : this(new List<PersistPokemonData>() { pokemonData }) { }

        public CacheLoadRequest(List<PersistPokemonData> pokemonData)
        {
            Game = pokemonData[0].Game;
            HashSet<int> species = new HashSet<int>();
            HashSet<string> phases = new HashSet<string>();
            foreach(var pokemon in pokemonData)
            {
                species.Add(pokemon.PokemonData.Species);
                phases.Add(pokemon.Phase);
            }
            Species = species.ToList();
            Phases = phases.ToList();
        }

        public GameFlavor Game { get; private set; }
        public List<int> Species { get; private set;  }
        public List<string> Phases { get; private set; }
    }

    public class GameEncounterResponse
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public Dictionary<int, PokemonCounts> PokemonEncounters { get; set; } = new();
        public Dictionary<string, PhaseEncounterResponse> PhaseEncounters { get; set; } = new();
        public PokemonData[] LatestEncounters { get; set; } = [];
    }

    public class PhaseEncounterResponse
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public Dictionary<int, PokemonCounts> PokemonEncounters { get; set; } = new();
    }

    public class GameEncounterCache
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public ConcurrentDictionary<string, PhaseEncounterCache> PhaseEncounterCache { get; set; } = new();
        public ConcurrentDictionary<int, PokemonCounts> PokemonEncounterCache { get; set; } = new();
        public ConcurrentQueue<PokemonData> LatestEncounters { get; set; } = new();

    }

    public class PhaseEncounterCache
    {
        public PokemonCounts AllPokemonCounts { get; set; } = new();
        public ConcurrentDictionary<int, PokemonCounts> PokemonEncounterCache { get; set; } = new();
    }

    public class PokemonCounts
    {
        public long Encounters { get; set; }
        public long ShinyEncounters { get; set;  }
        public long ShiniesCaught { get; set; }

        public PokemonCounts Clone()
        {
            return new PokemonCounts()
            {
                Encounters = Encounters,
                ShinyEncounters = ShinyEncounters,
                ShiniesCaught = ShiniesCaught,
            };
        }
    }
}
