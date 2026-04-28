using Microsoft.AspNetCore.Mvc;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Controllers
{
    public record GamePhaseRaw(string game, string phase);
    public class AdminController : ApiControllerBase
    {
        private AdminService adminService;
        private PhaseInfoService phaseInfoService;
        public AdminController(AdminService adminService, PhaseInfoService phaseInfoService) 
        {
            this.adminService = adminService;
            this.phaseInfoService = phaseInfoService;
        }

        
        [HttpPost("subcribeGamePhase")]
        public async Task<IActionResult> SubscribeGamePhase([FromBody] GamePhaseRaw gamePhase)
        {
            await adminService.SubscribeGamePhaseAsync(gamePhase.game, gamePhase.phase);
            return Ok();
        }

        [HttpPost("unsubcribeGamePhase")]
        public async Task<IActionResult> UnsubscribeGamePhase([FromBody] GamePhaseRaw gamePhase)
        {
            await adminService.UnsubscribeGamePhaseAsync(gamePhase.game, gamePhase.phase);
            return Ok();
        }

        [HttpPost("updateRecentEncounterConfig")]
        public async Task<IActionResult> UpdateRecentEncounterConfig([FromBody] int recountEncounterCount)
        {
            await adminService.UpdateRecentEncounterCount(recountEncounterCount);
            return Ok();
        }

        [HttpGet("metadata")]
        public ActionResult<AdminMetadata> GetMetadata()
        {
            GameFlavor[] gameFlavors = Enum.GetValues<GameFlavor>();
            var phases = this.phaseInfoService.GetAllPhases(gameFlavors);
            AdminMetadata metadata = new AdminMetadata()
            {
                Games = gameFlavors.Select(x => new KeyValuePair<string, GameMetadata>(x.ToString(), new GameMetadata() { Phases = phases[x] })).ToDictionary()
            };
            return Ok(metadata);
        }
    }

    public class AdminMetadata
    {
        public Dictionary<string, GameMetadata> Games { get; set; } = null!;
    }

    public class GameMetadata
    {
        public string[] Phases { get;set; } = null!;
    }
}
