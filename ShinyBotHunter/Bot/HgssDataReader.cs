using ShinyBotHunter.Emulators;
using YamlDotNet.Core.Tokens;

namespace ShinyBotHunter.Bot
{
    public class HgssDataReader : IDataReader
    {
        // memory addresses
        private const uint Anchor = 0x21D4158;

        private const uint BattlePokemon = 0x25E2C;

        private const uint BattleEnemyPokemon = 0x25EEC;
        private const uint BattlePartyPokemon = 0x2AAB0;
        public const uint PartySize = 0x23F44;
        private const uint EnemyAnchor = 0x6930;
        private const uint EnemyCount = 0xC14;
        private const uint Enemy = 0xC18;
        private const uint BattleMenuState = 0x230EC;
        private const uint BattleState = 0x692C;
        private const uint MapId = 0x22DA4;
        private const uint TrainerX = 0x21DA6F4;
        private const uint TrainerY = 0x21DA6FC;
        private const uint Facing = 0x1DC4;
        private IMemoryClient bot;
        public HgssDataReader(IMemoryClient memoryClient)
        {
            this.bot = memoryClient;
        }

        // These four go through the anchor. Reading the anchor and then reading through it as two
        // calls put a frame between them, so the pointer could already have moved; the ViaPointer
        // reads do both hops inside one emulator frame.

        public async Task<byte> GetBattleMenuState()
        {
            return await bot.ReadByteViaPointerAsync(Anchor, (int)BattleMenuState);
        }

        public async Task<byte> GetBattleValue()
        {
            return await bot.ReadByteAsync(0x21E76D2);
        }

        public async Task<byte> GetBattleState()
        {
            return await bot.ReadByteViaPointerAsync(Anchor, (int)BattleState);
        }

        public async Task<ushort> GetMapId()
        {
            return await bot.ReadUShortViaPointerAsync(Anchor, -(int)MapId);
        }

        private short to_s16(uint u32)
        {
            return (short)(((u32 / 65536) + 32768) % 65536 - 32768);
        }

        public async Task<short> GetTrainerXPos()
        {
            return to_s16(await bot.ReadUIntAsync(TrainerX));
        }

        public async Task<short> GetTrainerYPos()
        {
            return to_s16(await bot.ReadUIntAsync(TrainerY));
        }

        public async Task<byte> GetFacing()
        {
            return await bot.ReadByteViaPointerAsync(Anchor, (int)Facing);
        }
    }
}
