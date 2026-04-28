using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using System.Text.Json;

namespace ShinyBotHunter.Service
{
    public class PhaseInfoService
    {
        public record GamePhase(GameFlavor game, string phase);
        private PokemonMetadataService metadataService;
        private Dictionary<GamePhase, PhaseInfo> phaseInfos = new Dictionary<GamePhase, PhaseInfo>();
        private Dictionary<GameFlavor, List<string>> gamePhases = new Dictionary<GameFlavor, List<string>>();

        public PhaseInfoService(PokemonMetadataService metadataService)
        {
            this.metadataService = metadataService;
        }

        public void Init()
        {
            IEnumerable<string> files = Directory.EnumerateFiles("./Phases/", "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                string json = File.ReadAllText(file);
                var phaseInfo = JsonSerializer.Deserialize<PhaseInfo>(json);
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
                    phaseInfos.Add(new GamePhase(phaseInfo.Game, phaseInfo.Name), phaseInfo);
                    if (!gamePhases.ContainsKey(phaseInfo.Game))
                    {
                        gamePhases.Add(phaseInfo.Game, new List<string>());
                    }
                    gamePhases[phaseInfo.Game].Add(phaseInfo.Name);
                }
            }
        }

        //TODO this should probably return PhaseInfo? and check that the phase actually exists.
        public PhaseInfo GetPhaseInfo(GameFlavor game, string phase)
        {
            return phaseInfos[new GamePhase(game, phase)];
        }

        public Dictionary<GameFlavor, string[]> GetAllPhases(IEnumerable<GameFlavor> games)
        {
            Dictionary<GameFlavor, string[]> result = new Dictionary<GameFlavor, string[]>();
            foreach (GameFlavor game in games)
            {
                result.Add(game, gamePhases[game].ToArray());
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
    }

    public class PhasePokemonInfo
    {
        public string Name { get; set; } = "";
        public int SpeciesId { get; set; } = 0;
        public int Amount { get; set; } = 0;
        public string FormCode { get; set; } = "";
    }

}
