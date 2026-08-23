using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Controllers
{
    public record GamePhaseRaw(string game, string phase, string? gameName);
    public class AdminController : ApiControllerBase
    {
        private AdminService adminService;
        private PhaseInfoService phaseInfoService;
        private string romDirectory;
        private bool macroRecordingEnabled;
        public AdminController(AdminService adminService, PhaseInfoService phaseInfoService, IOptions<ApplicationSettings> appSettings) 
        {
            this.adminService = adminService;
            this.phaseInfoService = phaseInfoService;
            this.romDirectory = appSettings.Value.ROMBaseDirectory;
            this.macroRecordingEnabled = appSettings.Value.EnableMacroRecording;
        }

        
        [HttpPost("subcribeGamePhase")]
        public async Task<IActionResult> SubscribeGamePhase([FromBody] GamePhaseRaw gamePhase)
        {
            await adminService.SubscribeGamePhaseAsync(gamePhase.game, gamePhase.phase, gamePhase.gameName);
            return Ok();
        }

        [HttpPost("unsubcribeGamePhase")]
        public async Task<IActionResult> UnsubscribeGamePhase([FromBody] GamePhaseRaw gamePhase)
        {
            await adminService.UnsubscribeGamePhaseAsync(gamePhase.game, gamePhase.phase, gamePhase.gameName);
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
                Games = gameFlavors.Select(x => new KeyValuePair<string, GameMetadata>(x.ToString(), new GameMetadata() { Phases = phases[x] })).ToDictionary(),
                MacroRecordingEnabled = macroRecordingEnabled
            };
            return Ok(metadata);
        }

        [HttpGet("getAllGames")]
        public ActionResult<string[]> GetAllGames()
        {
            var files = Directory.GetFiles(romDirectory);
            List<string> result = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var parts = file.Split('\\', '/', '.');
                if (parts.Length >= 2)
                {
                    result.Add(parts[parts.Length - 2]);
                }

            }
            return Ok(result.ToArray());
        }
    }

    public class AdminMetadata
    {
        public Dictionary<string, GameMetadata> Games { get; set; } = null!;

        /// <summary>Drives whether the dashboard shows the dev-mode macro recorder tab.</summary>
        public bool MacroRecordingEnabled { get; set; }
    }

    public class GameMetadata
    {
        public PhaseInfo[] Phases { get;set; } = null!;
    }
}
