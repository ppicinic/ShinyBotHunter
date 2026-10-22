namespace ShinyBotHunter.Bot
{
    public interface IDataReader
    {
        Task<byte> GetBattleMenuState(CancellationToken ct = default);
        Task<byte> GetBattleValue(CancellationToken ct = default);
        Task<byte> GetBattleState();
        Task<ushort> GetMapId();
        Task<short> GetTrainerXPos();
        Task<short> GetTrainerYPos();
        Task<byte> GetFacing();
    }
}
