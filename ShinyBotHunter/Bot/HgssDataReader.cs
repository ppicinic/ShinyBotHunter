using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;
using YamlDotNet.Core.Tokens;

namespace ShinyBotHunter.Bot
{
    public class HgssDataReader : IDataReader
    {
        private IMemoryClient bot;
        private readonly HgssPointerService addresses;
        public HgssDataReader(IMemoryClient memoryClient, HgssPointerService addresses)
        {
            this.bot = memoryClient;
            this.addresses = addresses;
        }

        // These four go through the anchor. Reading the anchor and then reading through it as two
        // calls put a frame between them, so the pointer could already have moved; the ViaPointer
        // reads do both hops inside one emulator frame.

        public async Task<byte> GetBattleMenuState(CancellationToken ct = default)
        {
            return (await bot.ReadBytesViaPointerAsync(addresses.Anchor, 1, ct, (int)addresses.BattleMenuState))[0];
        }

        public async Task<byte> GetBattleValue(CancellationToken ct = default)
        {
            return (await bot.ReadBytesAsync(addresses.BattleIndicator, 1, ct))[0];
        }

        public async Task<byte> GetBattleState()
        {
            return await bot.ReadByteViaPointerAsync(addresses.Anchor, addresses.BattleStateOffset);
        }

        public async Task<ushort> GetMapId()
        {
            return await bot.ReadUShortViaPointerAsync(addresses.Anchor, addresses.MapIdOffset);
        }

        private short to_s16(uint u32)
        {
            return (short)(((u32 / 65536) + 32768) % 65536 - 32768);
        }

        public async Task<short> GetTrainerXPos()
        {
            return to_s16(await bot.ReadUIntAsync(addresses.TrainerX));
        }

        public async Task<short> GetTrainerYPos()
        {
            return to_s16(await bot.ReadUIntAsync(addresses.TrainerY));
        }

        public async Task<byte> GetFacing()
        {
            return await bot.ReadByteViaPointerAsync(addresses.Anchor, addresses.FacingOffset);
        }
    }
}
