namespace ShinyBotHunter.BotEvents
{
    public abstract class AbstractBotEvent<T> : IBotEvent<T>
    {
        public abstract Task<T> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken);
        public async Task ExecuteAsync(GameState state, CancellationToken cancellationToken)
        {
            await ExecuteWithResultAsync(state, cancellationToken);
        }

    }
}
