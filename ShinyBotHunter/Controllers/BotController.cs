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
            var state = await botService.ExecuteNewBotOrSpecialEvent(args);
            return Ok(state);
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
        public ActionResult<Dictionary<int, EmulatorState>> StartAction(int pos, [FromBody] NewEventArguments args)
        {
            return Ok(botService.ExecuteEvent(pos, args));
        }

        [HttpPost("bot/cancelAction")]
        public async Task<ActionResult<Dictionary<int, EmulatorState>>> CancelAction([FromBody] int pos)
        {
            return Ok(await botService.CancelBotOrEventAsync(pos));
        }
    }
}
