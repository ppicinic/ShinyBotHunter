namespace ShinyBotHunter.Service
{
    public interface INotify<T, U, V>
    {
        Task<U> BuildNotificationRequestAsync(T message);
        Task NotifyAsync(V response, T message);
    }
}
