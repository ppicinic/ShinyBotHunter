using ShinyBotHunter.Bot;

namespace ShinyBotHunter.BotEvents
{
    public class HeadbuttEncounterEvent : AbstractBotEvent<bool>
    {
        private readonly IBot bot;

        public HeadbuttEncounterEvent(IBot bot)
        {
            this.bot = bot;
        }

        public override Task<bool> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
