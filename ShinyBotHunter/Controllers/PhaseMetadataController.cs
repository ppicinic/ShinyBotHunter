using Microsoft.AspNetCore.Mvc;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.InfluxDb;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Controllers
{
    public class PhaseMetadataController : ApiControllerBase
    {
        private PhaseInfoService phaseInfoService;
        private PokemonPersistenceService pokemonPersistenceService;
        private InfluxDbEncounterDao influxDbEncounterDao;
        public PhaseMetadataController(PhaseInfoService phaseInfoService, PokemonPersistenceService pokemonPersistenceService, InfluxDbEncounterDao encounterDao)
        {
            this.phaseInfoService = phaseInfoService;
            this.pokemonPersistenceService = pokemonPersistenceService;
            influxDbEncounterDao = encounterDao;
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
                var rsp = await pokemonPersistenceService.DashboardLoadRequestAsync(new CacheLoadRequest(gameFlavor, phaseInfo.Pokemon.Select(x => x.SpeciesId).ToList(), new List<string>() { phase }, new List<string>()));
                return Ok(new WrappedPokemonPhase(phaseInfo, rsp));
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
                var rsp = await pokemonPersistenceService.DashboardLoadRequestAsync(new CacheLoadRequest(gameFlavor, phaseInfo.Pokemon.Select(x => x.SpeciesId).ToList(), new List<string>() { phase }, new List<string>() { gameFileName}));
                return Ok(new WrappedPokemonPhase(phaseInfo, rsp));
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

        [HttpGet("phaseinfo/encounterRate/{game}/{phase}")]
        public async Task<ActionResult<double>> GetEncounterRate(string game, string phase)
        {
            double result = await influxDbEncounterDao.GetEncountersPerHour(Enum.Parse<GameFlavor>(game), phase);
            return Ok(result);
        }

        [HttpGet("phaseinfo/encounterRate/{game}/{phase}/{gameFileName}")]
        public async Task<ActionResult<double>> GetEncounterRate(string game, string phase, string gameFileName)
        {
            double result = await influxDbEncounterDao.GetEncountersPerHour(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            return Ok(result);
        }
    }

    public record WrappedPokemonPhase(PhaseInfo phaseInfo, GameEncounterResponse data);
}
