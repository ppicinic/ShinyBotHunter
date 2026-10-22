namespace ShinyBotHunter.Games
{
    public interface IOverworldReader
    {
        Task<ushort> GetMapIdAsync(CancellationToken cancellationToken);
        Task<(short X, short Y)> GetPositionAsync(CancellationToken cancellationToken);
        Task<Direction> GetFacingAsync(CancellationToken cancellationToken);
    }
}
