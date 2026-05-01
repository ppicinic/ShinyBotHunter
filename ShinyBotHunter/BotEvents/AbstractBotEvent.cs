namespace ShinyBotHunter.BotEvents
{
    public abstract class AbstractBotEvent<T> : IBotEvent<T>
    {
        public abstract Task<T> ExecuteWithResultAsync(CancellationToken cancellationToken);
        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await ExecuteWithResultAsync(cancellationToken);
        }

        public abstract EventState EventState { get; }

    }
}
