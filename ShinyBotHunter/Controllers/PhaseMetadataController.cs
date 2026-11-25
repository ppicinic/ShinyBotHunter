using Microsoft.AspNetCore.Mvc;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Controllers
{
    public class PhaseMetadataController : ApiControllerBase
    {
        private PhaseInfoService phaseInfoService;
        private PokemonPersistenceService pokemonPersistenceService;
        private IEncounterDao encounterDao;
        private PokemonMetadataService pokemonMetadataService;
        private readonly MongoGameFileDao gameFileDao;
        private readonly MongoHuntPlanDao huntPlanDao;
        private readonly GameProfiles gameProfiles;
        private readonly RomInfoService romInfoService;
        public PhaseMetadataController(PhaseInfoService phaseInfoService, PokemonPersistenceService pokemonPersistenceService, IEncounterDao encounterDao, PokemonMetadataService pokemonMetadataService,
            MongoGameFileDao gameFileDao, MongoHuntPlanDao huntPlanDao, GameProfiles gameProfiles, RomInfoService romInfoService)
        {
            this.phaseInfoService = phaseInfoService;
            this.pokemonPersistenceService = pokemonPersistenceService;
            this.encounterDao = encounterDao;
            this.pokemonMetadataService = pokemonMetadataService;
            this.gameFileDao = gameFileDao;
            this.huntPlanDao = huntPlanDao;
            this.gameProfiles = gameProfiles;
            this.romInfoService = romInfoService;
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

        [HttpGet("goal/{gameFileName}")]
        public async Task<ActionResult<GameFile>> GetGoalAsync(string gameFileName) => Ok(await gameFileDao.LoadAsync(gameFileName));

        [HttpPut("goal/{gameFileName}")]
        public async Task<ActionResult> PutGoalAsync(string gameFileName, [FromBody] List<GoalEntry> entries)
        {
            string? error = GoalError(entries);
            if (error is not null)
            {
                return BadRequest(error);
            }
            await gameFileDao.SetEntriesAsync(gameFileName, entries);
            return NoContent();
        }

        [HttpPost("goal/{game}/{gameFileName}/preset/{preset}")]
        public async Task<ActionResult<List<GoalEntry>>> ApplyPresetAsync(string game, string gameFileName, GoalPreset preset)
        {
            var flavor = Enum.Parse<GameFlavor>(game);
            RomVersion? version = romInfoService.GetByHandle(gameFileName)?.Version;
            var milestones = gameProfiles.Get(flavor).Milestones;
            if (version is null || milestones is null || !milestones.ContainsKey(version.Value))
            {
                return NotFound();
            }
            IReadOnlyList<GoalEntry> presetEntries;
            try
            {
                presetEntries = GoalPresets.Entries(preset, version.Value, milestones, pokemonMetadataService);
            }
            catch (ArgumentException e)
            {
                // Ruby and Sapphire have no female forms preset.
                return BadRequest(e.Message);
            }
            var entries = GoalPresets.Apply((await gameFileDao.LoadAsync(gameFileName)).Entries, presetEntries);
            await gameFileDao.SetEntriesAsync(gameFileName, entries);
            return Ok(entries);
        }

        [HttpPut("goal/{gameFileName}/hidden")]
        public async Task<ActionResult> SetHiddenAsync(string gameFileName, [FromBody] HiddenRow row, [FromQuery] bool hidden)
        {
            await gameFileDao.SetHiddenAsync(gameFileName, row, hidden);
            return NoContent();
        }

        // FR-17: milestone is a generated id or a custom phase name. Only the picker reads it, so an unknown one is harmless.
        [HttpPut("goal/{gameFileName}/archived")]
        public async Task<ActionResult> SetArchivedAsync(string gameFileName, [FromQuery] string milestone, [FromQuery] bool archived)
        {
            await gameFileDao.SetArchivedAsync(gameFileName, milestone, archived);
            return NoContent();
        }

        // FR-18: an empty label clears the milestone's label.
        [HttpPut("goal/{gameFileName}/order")]
        public async Task<ActionResult> SetOrderAsync(string gameFileName, [FromBody] MilestoneLabel label)
        {
            await gameFileDao.SetOrderAsync(gameFileName, label);
            return NoContent();
        }

        // FR-16: a custom phase assigned to no file is on every file.
        [HttpPut("goal/{gameFileName}/assigned")]
        public async Task<ActionResult> SetAssignedAsync(string gameFileName, [FromQuery] string phase, [FromQuery] bool assigned)
        {
            await gameFileDao.SetAssignedAsync(gameFileName, phase, assigned);
            return NoContent();
        }

        // A generated milestone of the file's version seen through the file's goal: its rows with wanted, caught (FR-9) and the
        // encounters at this milestone and on the file (FR-11).
        [HttpGet("milestone/{game}/{milestoneId}/{gameFileName}")]
        public async Task<ActionResult> GetMilestoneRowsAsync(string game, string milestoneId, string gameFileName)
        {
            var flavor = Enum.Parse<GameFlavor>(game);
            RomVersion? version = romInfoService.GetByHandle(gameFileName)?.Version;
            var milestones = gameProfiles.Get(flavor).Milestones;
            var milestone = version is not null && milestones is not null && milestones.TryGetValue(version.Value, out var versionMilestones)
                ? versionMilestones.SingleOrDefault(m => m.Id == milestoneId)
                : null;
            if (milestone is null)
            {
                return NotFound();
            }
            var rows = MilestoneRows.Build(milestone, await gameFileDao.LoadAsync(gameFileName), await pokemonPersistenceService.GetCaughtPokemonAsync(flavor));
            var rsp = await pokemonPersistenceService.DashboardLoadRequestAsync(new CacheLoadRequest(flavor, rows.Select(r => r.Key).ToList(), [milestoneId], [gameFileName]));
            var file = rsp.GameSpecificResponse[gameFileName];
            return Ok(new
            {
                milestone.Id,
                milestone.Name,
                milestone.Modality,
                milestone.Slots,
                Rows = rows.Select(r => new
                {
                    r.Species,
                    r.Key,
                    r.FormCode,
                    r.Wanted,
                    r.Caught,
                    r.Complete,
                    r.Hidden,
                    r.IsTarget,
                    EncountersHere = file.PhaseEncounters[milestoneId].PokemonEncounters[r.Key].Encounters,
                    EncountersOnFile = file.PokemonEncounters[r.Key].Encounters
                })
            });
        }

        // The game's saved hunt plans (FR-37a).
        [HttpGet("plans/{game}")]
        public async Task<ActionResult<List<HuntPlan>>> GetPlansAsync(string game) => Ok(await huntPlanDao.ListAsync(Enum.Parse<GameFlavor>(game)));

        // Saves the plan under the game and name of the route, whatever the body says. Its milestones are checked at Start against
        // the hunt's game file, because anchors and completion are per file (FR-35).
        [HttpPut("plans/{game}/{name}")]
        public async Task<ActionResult> PutPlanAsync(string game, string name, [FromBody] HuntPlan plan)
        {
            string? error = PlanError(plan);
            if (error is not null)
            {
                return BadRequest(error);
            }
            await huntPlanDao.SaveAsync(plan with { Game = Enum.Parse<GameFlavor>(game), Name = name });
            return NoContent();
        }

        // PUT plans is the boundary for a plan: the default and every rule name a milestone.
        internal static string? PlanError(HuntPlan plan)
        {
            if (string.IsNullOrWhiteSpace(plan.Default))
            {
                return "The plan has no default milestone";
            }
            int empty = plan.Rules.FindIndex(r => string.IsNullOrWhiteSpace(r.Milestone));
            return empty < 0 ? null : $"Rule {empty + 1} names no milestone";
        }

        // PUT goal is the boundary for entries: a species is wanted in any form or by its forms, never both, each key once.
        internal static string? GoalError(IReadOnlyList<GoalEntry> entries)
        {
            if (entries.FirstOrDefault(e => e.Amount < 0) is { } negative)
            {
                return $"{negative.Key} has a negative amount";
            }
            if (entries.GroupBy(e => e.Key).FirstOrDefault(g => g.Count() > 1) is { } repeated)
            {
                return $"{repeated.Key} is listed more than once";
            }
            if (entries.GroupBy(e => e.Species).FirstOrDefault(g => g.Any(e => e.FormCode is null) && g.Any(e => e.FormCode is not null)) is { } mixed)
            {
                return $"Species {mixed.Key} has both an any-form entry and form entries";
            }
            return null;
        }
    }

    public record WrappedPokemonPhase(PhaseInfo phaseInfo, GameEncounterResponse data);
}
