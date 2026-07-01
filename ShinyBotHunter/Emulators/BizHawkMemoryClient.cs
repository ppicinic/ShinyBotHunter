using ShinyBotHunter.Context;
using System.Buffers.Binary;
using System.Net.Http;

namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// HTTP memory client. Each read/write POSTs the same 9-byte binary header the server
    /// understands and reads the raw response body. <see cref="HttpClient"/> is thread-safe,
    /// so multiple threads may poll memory concurrently; the server queues and orders the
    /// work on the emulator frame thread.
    /// </summary>
    public class BizHawkMemoryClient : BizHawkClientBase, IMemoryClient
    {
        private bool localMemoryOnly;

        public BizHawkMemoryClient(string host = "127.0.0.1", int port = 50101, bool localMemoryOnly = true)
            : base("BizHawkMemoryClient", host, port, TimeSpan.FromSeconds(15))
        {
            this.localMemoryOnly = localMemoryOnly;
        }

        public BizHawkMemoryClient(GameContext context, bool localMemoryOnly = true)
            : base("BizHawkMemoryClient", "127.0.0.1", context.MemoryPort, TimeSpan.FromSeconds(15))
        {
            this.localMemoryOnly = localMemoryOnly;
        }

        protected override void Disconnect()
        {
            // Connectionless — nothing to tear down.
        }

        public async Task<byte> ReadByteAsync(uint address) => (await ReadBytesAsync(address, 1))[0];
        public async Task<ushort> ReadUShortAsync(uint address) => BinaryPrimitives.ReadUInt16LittleEndian(await ReadBytesAsync(address, 2));
        public async Task<uint> ReadUIntAsync(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(await ReadBytesAsync(address, 4));

        public async Task<byte[]> ReadBytesAsync(uint address, int length)
        {
            byte[] header = BuildHeader(0x01, address, (uint)length);
            return await ExecuteWithRetryAsync(async () =>
            {
                using var content = new ByteArrayContent(header);
                using var resp = await _http.PostAsync("", content).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            });
        }

        public async Task WriteByteAsync(uint address, byte value)
        {
            byte[] header = BuildHeader(0x02, address, value);
            await ExecuteWithRetryAsync(async () =>
            {
                using var content = new ByteArrayContent(header);
                using var resp = await _http.PostAsync("", content).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            });
        }

        private static byte[] BuildHeader(byte cmd, uint address, uint aux)
        {
            var header = new byte[9];
            header[0] = cmd;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), address);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), aux);
            return header;
        }
    }
}
