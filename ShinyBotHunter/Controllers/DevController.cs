using Microsoft.AspNetCore.Mvc;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Controllers
{
    public record MacroRecordStopRequest(string? name);

    /// <summary>
    /// Dev-mode endpoints. Every action 404s unless ApplicationSettings.EnableMacroRecording is on —
    /// hiding the dashboard tab is cosmetic, this is the actual guard.
    /// </summary>
    public class DevController : ApiControllerBase
    {
        private readonly MacroRecordingService macroRecordingService;
        private readonly LocationService locationService;

        public DevController(MacroRecordingService macroRecordingService, LocationService locationService)
        {
            this.macroRecordingService = macroRecordingService;
            this.locationService = locationService;
        }

        // -- Location readout ------------------------------------------------------
        // Independent of macro recording: this reads over the memory server, so it keeps working
        // during a recording or while a bot event is running.

        [HttpGet("dev/location/{pos}")]
        public async Task<ActionResult<LocationInfo>> GetLocation(int pos)
        {
            if (!locationService.Enabled) return NotFound();

            try
            {
                return Ok(await locationService.GetLocationAsync(pos));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("dev/macroRecord/start/{pos}")]
        public async Task<IActionResult> StartRecording(int pos, CancellationToken ct)
        {
            if (!macroRecordingService.Enabled) return NotFound();

            try
            {
                await macroRecordingService.StartAsync(pos, ct);
                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("dev/macroRecord/stop/{pos}")]
        public async Task<ActionResult<MacroRecordResult>> StopRecording(
            int pos, [FromBody] MacroRecordStopRequest request, CancellationToken ct)
        {
            if (!macroRecordingService.Enabled) return NotFound();

            try
            {
                return Ok(await macroRecordingService.StopAsync(pos, request?.name ?? "RecordedMacro", ct));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("dev/macroRecord/status/{pos}")]
        public async Task<ActionResult<MacroRecordStatus>> RecordingStatus(int pos, CancellationToken ct)
        {
            if (!macroRecordingService.Enabled) return NotFound();

            try
            {
                return Ok(await macroRecordingService.GetStatusAsync(pos, ct));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
