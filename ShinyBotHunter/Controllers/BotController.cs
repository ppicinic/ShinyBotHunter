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
        public async Task<ActionResult<Dictionary<int, string>>> StartTopLevelAction([FromBody] NewEventArguments args)
        {
            var state = await botService.ExecuteNewBotOrSpecialEvent(args);
            return Ok(state);
        }

        [HttpGet("bot/getDashboardState")]
        public ActionResult<Dictionary<int, string>> GetDashboardState()
        {
            var state = botService.GetBotDashboardState();
            return Ok(state);
        }

        [HttpPost("bot/cancelAction")]
        public async Task<ActionResult<Dictionary<int, string>>> CancelAction([FromBody] int pos)
        {
            return Ok(await botService.CancelBotOrEventAsync(pos));
        }
    }
}
