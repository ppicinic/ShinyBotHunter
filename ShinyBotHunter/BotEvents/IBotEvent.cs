using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;

namespace ShinyBotHunter.BotEvents
{
    public enum EventState
    {
        NotCreated,
        NotStarted,
        Running,
        Waiting,
        Complete,
    }
    public interface IBotEvent
    {
        public Task ExecuteAsync(CancellationToken cancellationToken);
        public int[] BotPositions { get { return []; } }
        public EventState EventState {  get; }
    }

    public interface IBotEvent<T> : IBotEvent
    {
        public Task<T> ExecuteWithResultAsync(CancellationToken cancellationToken);
    }
}
