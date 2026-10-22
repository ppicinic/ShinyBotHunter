namespace ShinyBotHunter.Games
{
    public interface IGameStateReader
    {
        Task<GameSnapshot> ReadAsync(CancellationToken ct);
    }
}
