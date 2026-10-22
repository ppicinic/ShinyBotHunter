using Microsoft.AspNetCore.Mvc;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Controllers
{
    public class BotController : ApiControllerBase
    {
        private BotService botService;

        public BotController(BotService botService)
        {
            this.botService = botService;
        }

        [HttpGet("getTopLevelActions")]
        public ActionResult<EventArgumentOptions[]> GetTopLevelActions()
        {
            return Ok(botService.GetTopLevelOptions());
        }

        [HttpPost("startTopLevelAction")]
        public async Task<ActionResult<Dictionary<int, EmulatorState>>> StartTopLevelAction([FromBody] NewEventArguments args)
        {
            try
            {
                return Ok(await botService.ExecuteNewBotOrSpecialEvent(args));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException)
            {
                // The same mapping as StartAction: a refused request comes back as its message alone.
                return new ContentResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Content = ex.Message,
                    ContentType = "text/plain",
                };
            }
        }

        [HttpGet("bot/getDashboardState")]
        public ActionResult<Dictionary<int, EmulatorState>> GetDashboardState()
        {
            var state = botService.GetBotDashboardState();
            return Ok(state);
        }

        [HttpGet("bot/getBotActions/{pos}")]
        public ActionResult<EventArgumentOptions> GetBotActions(int pos)
        {
            return Ok(botService.GetAvailableActions(pos).ToArray());
        }

        [HttpGet("bot/getParty/{pos}")]
        public async Task<ActionResult<List<PartySlotInfo>>> GetParty(int pos)
        {
            return Ok(await botService.GetPartyAsync(pos));
        }

        [HttpPost("bot/startAction/{pos}")]
        public async Task<ActionResult<Dictionary<int, EmulatorState>>> StartAction(int pos, [FromBody] NewEventArguments args)
        {
            try
            {
                return Ok(await botService.ExecuteEvent(pos, args));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException)
            {
                // These are the "you asked for something that cannot work" cases -- bad arguments, an
                // unmeasured config value, a full party. The dashboard shows the response body
                // verbatim, so return the message alone rather than letting it become a 500 carrying
                // the whole developer exception page. Anything else still surfaces as a 500, because
                // an unexpected exception is a bug and should look like one.
                return new ContentResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Content = ex.Message,
                    ContentType = "text/plain",
                };
            }
        }

        [HttpPost("bot/cancelAction")]
        public async Task<ActionResult<Dictionary<int, EmulatorState>>> CancelAction([FromBody] int pos)
        {
            return Ok(await botService.CancelBotOrEventAsync(pos));
        }
    }
}
