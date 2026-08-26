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
        private RomInfoService romInfoService;
        private bool macroRecordingEnabled;
        public AdminController(AdminService adminService, PhaseInfoService phaseInfoService, RomInfoService romInfoService, IOptions<ApplicationSettings> appSettings)
        {
            this.adminService = adminService;
            this.phaseInfoService = phaseInfoService;
            this.romInfoService = romInfoService;
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

        /// <summary>
        /// Detected ROMs, keyed by file handle. Previously this listed every file in the ROM directory
        /// and took the second-to-last dot-separated segment, which emitted a duplicate entry for each
        /// .sav sitting beside its ROM and could say nothing about what game a file actually was.
        /// </summary>
        [HttpGet("getAllGames")]
        public ActionResult<GameRomInfo[]> GetAllGames() => Ok(Describe(romInfoService.GetAll()));

        /// <summary>
        /// Rescans and rehashes the ROM folder. Scanning reads every ROM, so it happens at startup and
        /// on request rather than behind ordinary lookups — this is how the dashboard picks up a ROM
        /// added or replaced while the app was running.
        /// </summary>
        [HttpPost("refreshGames")]
        public async Task<ActionResult<GameRomInfo[]>> RefreshGames()
            => Ok(Describe(await romInfoService.RefreshAsync()));

        private static GameRomInfo[] Describe(IEnumerable<RomInfo> roms)
        {
            return roms
                .Select(r => new GameRomInfo
                {
                    Handle = r.Handle,
                    Console = r.Console.ToString(),
                    Flavor = r.Flavor?.ToString(),
                    GameName = r.GameName,
                    GameCode = r.GameCode,
                    Language = r.Language,
                    Revision = r.Revision,
                    DisplayName = r.DisplayName,
                    RevisionSupport = r.RevisionSupport.ToString(),
                })
                .ToArray();
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

    /// <summary>Dashboard view of a detected ROM. <see cref="Handle"/> is what gets posted back.</summary>
    public class GameRomInfo
    {
        public string Handle { get; set; } = null!;
        public string Console { get; set; } = null!;
        public string? Flavor { get; set; }
        public string GameName { get; set; } = null!;
        public string GameCode { get; set; } = null!;
        public string Language { get; set; } = null!;
        public int Revision { get; set; }
        public string DisplayName { get; set; } = null!;
        public string RevisionSupport { get; set; } = null!;
    }
}
