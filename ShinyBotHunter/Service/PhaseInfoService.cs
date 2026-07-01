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
        private PokemonMetadataService metadataService;
        private Dictionary<GamePhase, PhaseInfo> phaseInfos = new Dictionary<GamePhase, PhaseInfo>();
        private Dictionary<GameFlavor, List<string>> gamePhases = new Dictionary<GameFlavor, List<string>>();
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
            return phaseInfo;
        }

        public PhaseInfo? GetPhaseInfo(GameFlavor game, string phase)
        {
            phaseInfos.TryGetValue(new GamePhase(game, phase, null), out var info);
            return info;
        }

        public Dictionary<GameFlavor, PhaseInfo[]> GetAllPhases(IEnumerable<GameFlavor> games)
        {
            Dictionary<GameFlavor, PhaseInfo[]> result = new Dictionary<GameFlavor, PhaseInfo[]>();
            foreach (GameFlavor game in games)
            {
                List<PhaseInfo> toReturn = new List<PhaseInfo>();
                foreach(var phaseName in gamePhases[game])
                {
                    toReturn.Add(phaseInfos[new GamePhase(game, phaseName, null)]);
                }
                result.Add(game, toReturn.ToArray());
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
    }

}
