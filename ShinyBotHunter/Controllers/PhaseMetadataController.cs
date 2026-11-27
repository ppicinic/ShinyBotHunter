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
        private readonly MongoHuntPlanDao huntPlanDao;
        private readonly GameProfiles gameProfiles;
        private readonly RomInfoService romInfoService;
        private readonly BotService botService;
        public PhaseMetadataController(PhaseInfoService phaseInfoService, PokemonPersistenceService pokemonPersistenceService, IEncounterDao encounterDao, PokemonMetadataService pokemonMetadataService,
            MongoHuntPlanDao huntPlanDao, GameProfiles gameProfiles, RomInfoService romInfoService, BotService botService)
        {
            this.phaseInfoService = phaseInfoService;
            this.pokemonPersistenceService = pokemonPersistenceService;
            this.encounterDao = encounterDao;
            this.pokemonMetadataService = pokemonMetadataService;
            this.huntPlanDao = huntPlanDao;
            this.gameProfiles = gameProfiles;
            this.romInfoService = romInfoService;
            this.botService = botService;
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

        // The count cache's key of a row.
        private static string Key(PhasePokemonInfo pokemon) => pokemon.RequireSpecificForm ? pokemon.FormCode : CacheLoadRequest.SpeciesKey(pokemon.SpeciesId);

        private bool IsLocation(GameFlavor flavor, string locationId)
            => gameProfiles.Get(flavor).Milestones is { } milestones && PhaseInfoService.At(milestones, locationId).Count > 0;

        // The editor without a game file, the card with one. Without a location, today's phaseInfo and data plus the new fields.
        [HttpGet("phaseinfo/{game}/{phase}/{gameFileName?}")]
        public async Task<ActionResult> GetPhaseInfo(string game, string phase, string? gameFileName)
        {
            var gameFlavor = Enum.Parse<GameFlavor>(game);
            var phaseInfo = phaseInfoService.GetPhaseInfo(gameFlavor, phase);
            if (phaseInfo is null)
            {
                return NotFound();
            }
            var rsp = await pokemonPersistenceService.DashboardLoadRequestAsync(new CacheLoadRequest(gameFlavor, phaseInfo.Pokemon.Select(Key).ToList(),
                [phase], gameFileName is null ? [] : [gameFileName]));
            var seen = gameFileName is null ? null : phaseInfoService.Seen(gameFileName);
            SlotCondition? timeOfDay = seen?.Inputs.Hour is int h ? HuntPlan.TimeOfDay(h) : null;
            return Ok(new
            {
                PhaseInfo = WithFormMetadata(phaseInfo),
                Data = rsp,
                Live = seen?.Live ?? false,
                TimeOfDay = timeOfDay,
                SuggestedLocation = phaseInfo.Location is null ? Backfill.Suggestion(phaseInfo.Name) : null,
                Location = phaseInfo.Location is { } locationId ? LocationRows(gameFlavor, phaseInfo, locationId, gameFileName, seen, timeOfDay) : null
            });
        }

        // Each row's version mark, times and share at the milestone's location; built per request, never written onto the cached
        // rows, which concurrent cards for other files read. On a card, times and shares are the file's version's; null when the
        // file's ROM version is not known.
        private object? LocationRows(GameFlavor flavor, PhaseInfo phaseInfo, string locationId, string? gameFileName,
            (RateInputs Inputs, bool Live)? seen, SlotCondition? timeOfDay)
        {
            var profile = gameProfiles.Get(flavor);
            var milestones = profile.Milestones!;
            var at = PhaseInfoService.At(milestones, locationId);
            Milestone? milestone = null;
            if (gameFileName is not null)
            {
                if (romInfoService.GetByHandle(gameFileName)?.Version is not { } version)
                {
                    return null;
                }
                milestone = at.GetValueOrDefault(version);
            }
            IEnumerable<Milestone> scope = gameFileName is null ? at.Values : milestone is null ? [] : [milestone];
            var shares = milestone is null ? null : MilestoneRows.Shares(milestone, profile.Rates, seen);
            var rows = phaseInfo.Pokemon.Select(p => (Pokemon: p, Times: PhaseInfoService.Times(scope, p.SpeciesId))).ToList();
            var first = at.Values.First();
            return new
            {
                first.Id,
                first.Name,
                first.Modality,
                Survive = shares?.Survive,
                Rows = rows.Select(r =>
                {
                    (double Share, string? Reason)? share = shares is { } s && s.Species.TryGetValue(r.Pokemon.SpeciesId, out var found) ? found : null;
                    return new
                    {
                        Key = Key(r.Pokemon),
                        Version = PhaseInfoService.OnlyVersion(milestones, at, r.Pokemon.SpeciesId),
                        r.Times,
                        Share = share?.Share,
                        Reason = share?.Reason,
                        HiddenNow = timeOfDay is { } now && r.Times.Length > 0 && !r.Times.Contains(now)
                    };
                }).ToList(),
                HiddenUntil = PhaseInfoService.HiddenUntil(rows.Select(r =>
                    (pokemonMetadataService.RowName(r.Pokemon.SpeciesId, r.Pokemon.RequireSpecificForm ? r.Pokemon.FormCode : null), r.Times)), timeOfDay)
            };
        }

        [HttpPost("updatePhaseInfo/{game}/{phase}")]
        public async Task<ActionResult<PhaseInfo>> UpdatePhaseInfoAsync([FromBody] PhaseInfo phaseInfo)
        {
            if (phaseInfo.Location is { } locationId && !IsLocation(phaseInfo.Game, locationId))
            {
                return BadRequest($"{locationId} is not a location of {phaseInfo.GameName}");
            }
            return Ok(await phaseInfoService.AddOrUpdatePhaseInfoAsync(phaseInfo));
        }

        // The editor's location dropdown: one entry per id across the game's versions.
        [HttpGet("milestones/{game}/locations")]
        public ActionResult GetLocations(string game)
        {
            var milestones = gameProfiles.Get(Enum.Parse<GameFlavor>(game)).Milestones;
            if (milestones is null)
            {
                return NotFound();
            }
            return Ok(milestones.Values.SelectMany(v => v).DistinctBy(m => m.Id).OrderBy(m => m.Modality).ThenBy(m => m.Name)
                .Select(m => new { m.Id, m.Name, m.Modality }));
        }

        // "New from game data": the location's rows at the preset's amounts, for the version filter (both when left out). Stores nothing.
        [HttpGet("milestones/{game}/locations/{locationId}/seed")]
        public ActionResult GetSeed(string game, string locationId, [FromQuery] GoalPreset preset = GoalPreset.None, [FromQuery] RomVersion? version = null)
        {
            var milestones = gameProfiles.Get(Enum.Parse<GameFlavor>(game)).Milestones;
            if (milestones is null || PhaseInfoService.At(milestones, locationId) is not { Count: > 0 } at)
            {
                return NotFound();
            }
            if (version is { } v && !milestones.ContainsKey(v))
            {
                return BadRequest($"{v} is not a version of {game}");
            }
            List<(GoalEntry Entry, RomVersion? Version, SlotCondition[] Times)> rows;
            try
            {
                rows = PhaseInfoService.Seed(milestones, locationId, version is { } only ? [only] : milestones.Keys.ToList(), preset, pokemonMetadataService);
            }
            catch (ArgumentException e)
            {
                // Ruby and Sapphire have no female forms preset.
                return BadRequest(e.Message);
            }
            return Ok(new
            {
                at.Values.First().Name,
                Location = locationId,
                Rows = rows.Select(r =>
                {
                    var metadata = pokemonMetadataService.GetPokemonMetadata(r.Entry.Species);
                    return new
                    {
                        SpeciesId = r.Entry.Species,
                        Name = metadata.PokemonName,
                        FormCode = r.Entry.FormCode ?? metadata.PokeApiName,
                        RequireSpecificForm = r.Entry.FormCode is not null,
                        metadata.HasFemaleForm,
                        r.Entry.Amount,
                        r.Version,
                        r.Times
                    };
                })
            });
        }

        // Attach and detach write only the milestone's location, never a row or an amount; seeded read-only milestones too.
        [HttpPut("milestones/{game}/{name}/location/{locationId}")]
        public async Task<ActionResult> AttachLocationAsync(string game, string name, string locationId)
        {
            var flavor = Enum.Parse<GameFlavor>(game);
            if (!IsLocation(flavor, locationId))
            {
                return NotFound();
            }
            return await phaseInfoService.SetLocationAsync(flavor, name, locationId) ? NoContent() : NotFound();
        }

        [HttpDelete("milestones/{game}/{name}/location")]
        public async Task<ActionResult> DetachLocationAsync(string game, string name)
            => await phaseInfoService.SetLocationAsync(Enum.Parse<GameFlavor>(game), name, null) ? NoContent() : NotFound();

        [HttpGet("phaseinfo/encounterRate/{game}/{phase}/{gameFileName?}")]
        public async Task<ActionResult<double>> GetEncounterRate(string game, string phase, string? gameFileName)
        {
            double result = await encounterDao.GetEncountersPerHour(Enum.Parse<GameFlavor>(game), phase, gameFileName);
            return Ok(result);
        }

        // The goal editor's names: every species of the flavor's dex, by number.
        [HttpGet("species/{game}")]
        public ActionResult GetSpeciesNames(string game)
        {
            int last = Enum.Parse<GameFlavor>(game) switch
            {
                GameFlavor.HGSS => PKHeX.Core.PersonalTable.HGSS.MaxSpeciesID,
                GameFlavor.RS => PKHeX.Core.PersonalTable.RS.MaxSpeciesID,
                var flavor => throw new ArgumentOutOfRangeException(nameof(game), flavor, null)
            };
            return Ok(Enumerable.Range(1, last).Select(s => new { Species = s, Name = pokemonMetadataService.GetPokemonMetadata(s).PokemonName }));
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

        // FR-37b: refused while a hunt uses the plan, with the same mapping as BotController.StartAction.
        [HttpDelete("plans/{game}/{name}")]
        public async Task<ActionResult> DeletePlanAsync(string game, string name)
        {
            try
            {
                await botService.DeletePlanAsync(Enum.Parse<GameFlavor>(game), name);
                return NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return new ContentResult { StatusCode = StatusCodes.Status400BadRequest, Content = ex.Message, ContentType = "text/plain" };
            }
        }

        [HttpPost("plans/{game}/{name}/rename")]
        public async Task<ActionResult> RenamePlanAsync(string game, string name, [FromQuery] string? to)
        {
            try
            {
                await botService.RenamePlanAsync(Enum.Parse<GameFlavor>(game), name, to);
                return NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return new ContentResult { StatusCode = StatusCodes.Status400BadRequest, Content = ex.Message, ContentType = "text/plain" };
            }
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
    }
}
