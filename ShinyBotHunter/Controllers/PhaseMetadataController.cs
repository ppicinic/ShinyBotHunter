using Microsoft.AspNetCore.Mvc;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Controllers
{
    public class PhaseMetadataController : ApiControllerBase
    {
        private PhaseInfoService phaseInfoService;
        private PokemonPersistenceService pokemonPersistenceService;
        private IEncounterDao encounterDao;
        private PokemonMetadataService pokemonMetadataService;
        public PhaseMetadataController(PhaseInfoService phaseInfoService, PokemonPersistenceService pokemonPersistenceService, IEncounterDao encounterDao, PokemonMetadataService pokemonMetadataService)
        {
            this.phaseInfoService = phaseInfoService;
            this.pokemonPersistenceService = pokemonPersistenceService;
            this.encounterDao = encounterDao;
            this.pokemonMetadataService = pokemonMetadataService;
        }

        /// <summary>
        /// Fills in the derived HasFemaleForm flag so the dashboard can render a gender marker. Not
        /// stored on the phase, so it has to be applied on every read.
        /// </summary>
        private PhaseInfo WithFormMetadata(PhaseInfo phaseInfo)
        {
            foreach (var pokemon in phaseInfo.Pokemon)
            {
                pokemon.HasFemaleForm = pokemonMetadataService.GetPokemonMetadata(pokemon.SpeciesId).HasFemaleForm;
            }
            return phaseInfo;
        }

        [HttpGet("phaseinfo/{game}/{phase}")]
        public async Task<ActionResult<WrappedPokemonPhase>> GetPhaseInfo(string game, string phase)
        {
            try
            {
                var gameFlavor = Enum.Parse<GameFlavor>(game);
                var phaseInfo = phaseInfoService.GetPhaseInfo(gameFlavor, phase);
                if (phaseInfo is null)
                {
                    return NotFound();
                }
                var rsp = await pokemonPersistenceService.DashboardLoadRequestAsync(new CacheLoadRequest(gameFlavor, phaseInfo.Pokemon.Select(x => x.RequireSpecificForm ? x.FormCode : CacheLoadRequest.SpeciesKey(x.SpeciesId)).ToList(), new List<string>() { phase }, new List<string>()));
                return Ok(new WrappedPokemonPhase(WithFormMetadata(phaseInfo), rsp));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return NotFound();
            }
        }

        [HttpGet("phaseinfo/{game}/{phase}/{gameFileName}")]
        public async Task<ActionResult<WrappedPokemonPhase>> GetPhaseInfo(string game, string phase, string gameFileName)
        {
            try
            {
                var gameFlavor = Enum.Parse<GameFlavor>(game);
                var phaseInfo = phaseInfoService.GetPhaseInfo(gameFlavor, phase);
                if (phaseInfo is null)
                {
                    return NotFound();
                }
                var rsp = await pokemonPersistenceService.DashboardLoadRequestAsync(new CacheLoadRequest(gameFlavor, phaseInfo.Pokemon.Select(x => x.RequireSpecificForm ? x.FormCode : CacheLoadRequest.SpeciesKey(x.SpeciesId)).ToList(), new List<string>() { phase }, new List<string>() { gameFileName}));
                return Ok(new WrappedPokemonPhase(WithFormMetadata(phaseInfo), rsp));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return NotFound();
            }
        }

        [HttpPost("updatePhaseInfo/{game}/{phase}")]
        public async Task<ActionResult<PhaseInfo>> UpdatePhaseInfoAsync([FromBody] PhaseInfo phaseInfo)
        {
            return Ok(await phaseInfoService.AddOrUpdatePhaseInfoAsync(phaseInfo));
        }

        [HttpGet("phaseinfo/encounterRate/{game}/{phase}/{gameFileName?}")]
        public async Task<ActionResult<double>> GetEncounterRate(string game, string phase, string? gameFileName)
        {
            double result = await encounterDao.GetEncountersPerHour(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            return Ok(result);
        }
    }

    public record WrappedPokemonPhase(PhaseInfo phaseInfo, GameEncounterResponse data);
}
