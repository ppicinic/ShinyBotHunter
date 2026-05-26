namespace ShinyBotHunter.Bot
{
    public interface IDataReader
    {
        Task<byte> GetBattleMenuState();
        Task<byte> GetBattleValue();
        Task<byte> GetBattleState();
        Task<ushort> GetMapId();
        Task<short> GetTrainerXPos();
        Task<short> GetTrainerYPos();
        Task<byte> GetFacing();
    }
}
