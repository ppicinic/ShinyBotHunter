using System.Buffers.Binary;

namespace ShinyBotHunter.Emulators
{
    public interface IMemoryClient : IAsyncDisposable
    {
        public void Connect();
        public Task<byte> ReadByteAsync(uint address);
        public Task<ushort> ReadUShortAsync(uint address);
        public Task<uint> ReadUIntAsync(uint address);

        public Task<byte[]> ReadBytesAsync(uint address, int length);

        public Task WriteByteAsync(uint address, byte value);
    }
}
