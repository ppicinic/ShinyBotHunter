using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;

namespace ShinyBotHunter.BotEvents
{
    public interface IBotEvent
    {
        public Task ExecuteAsync(CancellationToken cancellationToken);
        public int[] BotPositions { get { return []; } }
    }

    public interface IBotEvent<T> : IBotEvent
    {
        public Task<T> ExecuteWithResultAsync(CancellationToken cancellationToken);
    }
}
