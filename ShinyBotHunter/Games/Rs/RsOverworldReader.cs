using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Rs
{
    public class RsOverworldReader : IOverworldReader
    {
        private readonly IMemoryClient memory;
        private readonly RsPointerService addresses;

        public RsOverworldReader(IMemoryClient memory, RsPointerService addresses)
        {
            this.memory = memory;
            this.addresses = addresses;
        }

        // gSaveBlock1.location: u8 group, then u8 number.
        public async Task<ushort> GetMapIdAsync(CancellationToken cancellationToken)
        {
            byte[] location = await memory.ReadBytesAsync(addresses.PlayerLocation, 2, cancellationToken);
            return (ushort)(location[0] * 256 + location[1]);
        }

        // gSaveBlock1.pos: s16 x, then s16 y, read together so both come from one frame.
        public async Task<(short X, short Y)> GetPositionAsync(CancellationToken cancellationToken)
        {
            byte[] position = await memory.ReadBytesAsync(addresses.PlayerPosition, 4, cancellationToken);
            return (BitConverter.ToInt16(position, 0), BitConverter.ToInt16(position, 2));
        }

        // The facing nibble: 1 down, 2 up, 3 left, 4 right. Anything else (a read caught mid-load) is None.
        public async Task<Direction> GetFacingAsync(CancellationToken cancellationToken)
        {
            byte[] facing = await memory.ReadBytesAsync(addresses.PlayerFacing, 1, cancellationToken);
            return (facing[0] & 0x0F) switch
            {
                1 => Direction.Down,
                2 => Direction.Up,
                3 => Direction.Left,
                4 => Direction.Right,
                _ => Direction.None,
            };
        }

        // gPlayerAvatar.objectEventId. Not on IOverworldReader: HGSS has no such value.
        public async Task<byte> GetPlayerObjectIdAsync(CancellationToken cancellationToken)
        {
            byte[] id = await memory.ReadBytesAsync(addresses.PlayerObjectId, 1, cancellationToken);
            return id[0];
        }
    }
}
