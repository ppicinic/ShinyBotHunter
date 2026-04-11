using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;

namespace ShinyBotHunter.BotEvents
{
    public interface IBotEvent
    {
        public Task ExecuteAsync(CancellationToken cancellationToken);
    }

    public interface IBotEvent<T>
    {
        public Task<T> ExecuteAsync(CancellationToken cancellationToken);
    }
}
