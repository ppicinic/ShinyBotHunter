
using ShinyBotHunter.Context;
using System.Buffers.Binary;

namespace ShinyBotHunter.Emulators
{

    public class BizHawkMemoryClient : BizHawkClientBase, IMemoryClient
    {
        public BizHawkMemoryClient(string host = "127.0.0.1", int port = 50001)
            : base("BizHawkMemoryClient", host, port) { }

        public BizHawkMemoryClient(GameContext context)
            : base("BizHawkMemoryClient", "127.0.0.1", context.CommandPort) { }

        protected override void Disconnect()
        {
            _stream.Write(new byte[] { 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        public async Task<byte> ReadByteAsync(uint address) => (await ReadBytesAsync(address, 1))[0];
        public async Task<ushort> ReadUShortAsync(uint address) => BinaryPrimitives.ReadUInt16LittleEndian(await ReadBytesAsync(address, 2));
        public async Task<uint> ReadUIntAsync(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(await ReadBytesAsync(address, 4));

        public async Task<byte[]> ReadBytesAsync(uint address, int length)
        {
            await _stream.WriteAsync(BuildHeader(0x01, address, (uint)length));
            var buf = new byte[length];
            await ReadExactAsync(buf);
            return buf;
        }

        public async Task WriteByteAsync(uint address, byte value)
        {
            await _stream.WriteAsync(BuildHeader(0x02, address, value));
            var ack = new byte[1];
            await ReadExactAsync(ack);
        }

        private static byte[] BuildHeader(byte cmd, uint address, uint aux)
        {
            var header = new byte[9];
            header[0] = cmd;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), address);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), aux);
            return header;
        }

        private async Task ReadExactAsync(byte[] buf)
        {
            int offset = 0;
            while (offset < buf.Length)
            {
                int read = await _stream.ReadAsync(buf.AsMemory(offset));
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
