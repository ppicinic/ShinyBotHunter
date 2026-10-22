namespace ShinyBotHunter.Bot
{
    public interface IDataReader
    {
        Task<byte> GetBattleMenuState(CancellationToken ct = default);
        Task<byte> GetBattleValue(CancellationToken ct = default);
        Task<byte> GetBattleState();
    }
}
