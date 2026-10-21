using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using System.Text.Json;

namespace ShinyBotHunter.Service
{
    public class PhaseInfoService
    {
        public record GamePhase(GameFlavor game, string phase, string? gameFileName);
        // Set once in the constructor; thread-safe for concurrent use.
        private PokemonMetadataService metadataService;
        // Guards phaseInfos and gamePhases after startup. Held only around dictionary reads, writes and the GetAllPhases copy, never across an await.
        private readonly object phasesLock = new();
        // Filled by InitAsync before the host serves requests or starts bots (single-threaded by construction); after that, written by AddOrUpdatePhaseInfoAsync (dashboard requests) and read by requests, bot loops and the notification pipeline, only under phasesLock.
        private readonly Dictionary<GamePhase, PhaseInfo> phaseInfos = new Dictionary<GamePhase, PhaseInfo>();
        // Same as phaseInfos: filled by InitAsync before the host starts, then read and written only under phasesLock. A name is added here only in the same critical section that adds its phaseInfos entry.
        private readonly Dictionary<GameFlavor, List<string>> gamePhases = new Dictionary<GameFlavor, List<string>>();
        // Set once in the constructor; thread-safe for concurrent use.
        private IMongoCollection<PhaseInfo> phaseInfoCollection;

        public PhaseInfoService(PokemonMetadataService metadataService, MongoDbConnection mongoDbConnection)
        {
            this.metadataService = metadataService;
            phaseInfoCollection = mongoDbConnection.GetDatabase().GetCollection<PhaseInfo>("PhaseInfos");
        }

        public async Task InitAsync()
        {
            IEnumerable<string> files = Directory.EnumerateFiles("./Phases/", "*.*", SearchOption.AllDirectories);
            List<PhaseInfo> phaseInfosToAdd = new List<PhaseInfo>();
            var phaseInfosDb = await phaseInfoCollection.Find(Builders<PhaseInfo>.Filter.Empty).ToListAsync();
            foreach (var file in files)
            {
                string json = File.ReadAllText(file);
                var phaseInfo = JsonSerializer.Deserialize<PhaseInfo>(json);
                phaseInfo!.IsStatic = true;
                if(phaseInfosDb.Where(x => x.Name == phaseInfo!.Name && x.GameName == phaseInfo.GameName).Count() == 0)
                {
                    phaseInfo.Id = Guid.NewGuid();
                    phaseInfosToAdd.Add(phaseInfo);
                }
            }
            if (phaseInfosToAdd.Count() > 0)
            {
                await phaseInfoCollection.InsertManyAsync(phaseInfosToAdd);
            }
            phaseInfosDb = await phaseInfoCollection.Find(Builders<PhaseInfo>.Filter.Empty).ToListAsync();
            foreach (var phaseInfo in phaseInfosDb)
            {
                
                if(phaseInfo is not null)
                {
                    foreach(var pokemon in phaseInfo.Pokemon)
                    {
                        var metadata = GetMetadata(pokemon);
                        pokemon.Name = metadata.PokemonName;
                        pokemon.SpeciesId = metadata.Id;
                        if (String.IsNullOrEmpty(pokemon.FormCode))
                        {
                            pokemon.FormCode = metadata.PokeApiName;
                        }
                    }
                    phaseInfos.Add(new GamePhase(phaseInfo.Game, phaseInfo.Name, null), phaseInfo);
                    if (!gamePhases.ContainsKey(phaseInfo.Game))
                    {
                        gamePhases.Add(phaseInfo.Game, new List<string>());
                    }
                    gamePhases[phaseInfo.Game].Add(phaseInfo.Name);
                }
            }
        }

        public async Task<PhaseInfo> AddOrUpdatePhaseInfoAsync(PhaseInfo phaseInfo)
        {
            var builder = Builders<PhaseInfo>.Filter;
            var filter = builder.Eq(x => x.Name, phaseInfo.Name) & builder.Eq(x => x.GameName, phaseInfo.GameName);
            var phaseInfosDb = await phaseInfoCollection.Find(filter).FirstOrDefaultAsync();
            if (phaseInfosDb is null)
            {
                phaseInfo.Id = Guid.NewGuid();
                await phaseInfoCollection.InsertOneAsync(phaseInfo);
            }
            else
            {
                phaseInfo.Id = phaseInfosDb.Id;
                await phaseInfoCollection.ReplaceOneAsync(Builders<PhaseInfo>.Filter.Eq(x => x.Id, phaseInfo.Id), phaseInfo);
            }
            foreach (var pokemon in phaseInfo.Pokemon)
            {
                var metadata = GetMetadata(pokemon);
                pokemon.Name = metadata.PokemonName;
                pokemon.SpeciesId = metadata.Id;
                if (String.IsNullOrEmpty(pokemon.FormCode))
                {
                    pokemon.FormCode = metadata.PokeApiName;
                }
            }
            lock (phasesLock)
            {
                var key = new GamePhase(phaseInfo.Game, phaseInfo.Name, null);
                if (phaseInfos.ContainsKey(key))
                {
                    phaseInfos[key] = phaseInfo;
                }
                else 
                {
                    phaseInfos.Add(key, phaseInfo);
                    if (!gamePhases.ContainsKey(phaseInfo.Game))
                    {
                        gamePhases.Add(phaseInfo.Game, new List<string>());
                    }
                    gamePhases[phaseInfo.Game].Add(phaseInfo.Name);
                }
            }
            return phaseInfo;
        }

        public PhaseInfo? GetPhaseInfo(GameFlavor game, string phase)
        {
            PhaseInfo? info;
            lock (phasesLock)
            {
                phaseInfos.TryGetValue(new GamePhase(game, phase, null), out info);
            }
            return info;
        }

        public Dictionary<GameFlavor, PhaseInfo[]> GetAllPhases(IEnumerable<GameFlavor> games)
        {
            Dictionary<GameFlavor, PhaseInfo[]> result = new Dictionary<GameFlavor, PhaseInfo[]>();
            lock (phasesLock)
            {
                foreach (GameFlavor game in games)
                {
                    List<PhaseInfo> toReturn = new List<PhaseInfo>();
                    // A flavor with no phase JSON yet is normal, not an error — a newly added flavor has
                    // none until its phases are authored, and this is called with every enum member.
                    if (gamePhases.TryGetValue(game, out var phaseNames))
                    {
                        foreach (var phaseName in phaseNames)
                        {
                            toReturn.Add(phaseInfos[new GamePhase(game, phaseName, null)]);
                        }
                    }
                    result.Add(game, toReturn.ToArray());
                }
            }
            return result;
        }

        private PokemonMetadata GetMetadata(PhasePokemonInfo pokemon) 
        {
            // TODO form code would be at the top as it's the most descriptive
            if(pokemon.SpeciesId != 0)
            {
                return metadataService.GetPokemonMetadata(pokemon.SpeciesId);
            }
            if (!String.IsNullOrWhiteSpace(pokemon.Name))
            {
                return metadataService.GetPokemonMetadata(pokemon.Name);
            }
            throw new ArgumentException("Pokemon phase info is corrupted");
        }
    }

    public class PhaseInfo
    {
        public string GameName { get; set; } = "";
        public string Name { get; set; } = "";
        public GameFlavor Game { get
            {
                return Enum.Parse<GameFlavor>(GameName);
            } 
        }
        public List<PhasePokemonInfo> Pokemon { get; set; } = null!;
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; }
        public bool IsStatic { get; set; } = false;
    }

    public class PhasePokemonInfo
    {
        public string Name { get; set; } = "";
        public int SpeciesId { get; set; } = 0;
        public int Amount { get; set; } = 0;
        public string FormCode { get; set; } = "";
        public bool RequireSpecificForm { get; set; } = false;

        /// <summary>
        /// Whether this species has a distinct female form. Derived from pokemon metadata and filled in
        /// on the way out, never stored: it lets a client tell a male form from a genderless one, which
        /// the form code alone cannot express (GetFormCode only ever appends ";f", so male and
        /// genderless both come through as the plain slug).
        /// </summary>
        [BsonIgnore]
        public bool HasFemaleForm { get; set; } = false;
    }

}
