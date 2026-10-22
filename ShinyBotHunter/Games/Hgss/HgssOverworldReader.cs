using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Hgss
{
    public class HgssOverworldReader : IOverworldReader
    {
        private readonly IMemoryClient memory;
        private readonly HgssPointerService addresses;

        public HgssOverworldReader(IMemoryClient memory, HgssPointerService addresses)
        {
            this.memory = memory;
            this.addresses = addresses;
        }

        // The map id and facing go through the anchor. Reading the anchor and then reading through it
        // as two calls put a frame between them, so the pointer could already have moved; the
        // ViaPointer reads do both hops inside one emulator frame.

        public async Task<ushort> GetMapIdAsync(CancellationToken cancellationToken)
        {
            return await memory.ReadUShortViaPointerAsync(addresses.Anchor, addresses.MapIdOffset);
        }

        public async Task<(short X, short Y)> GetPositionAsync(CancellationToken cancellationToken)
        {
            short x = to_s16(await memory.ReadUIntAsync(addresses.TrainerX));
            short y = to_s16(await memory.ReadUIntAsync(addresses.TrainerY));
            return (x, y);
        }

        public async Task<Direction> GetFacingAsync(CancellationToken cancellationToken)
        {
            return (Direction)await memory.ReadByteViaPointerAsync(addresses.Anchor, addresses.FacingOffset);
        }

        private static short to_s16(uint u32)
        {
            return (short)(((u32 / 65536) + 32768) % 65536 - 32768);
        }
    }
}
