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

        public DevController(MacroRecordingService macroRecordingService)
        {
            this.macroRecordingService = macroRecordingService;
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
