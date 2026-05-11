using System.Buffers.Binary;
using System.IO.Pipes;

namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// Named-pipe implementation of <see cref="IMemoryClient"/>.
    /// Drop-in replacement for <see cref="BizHawkMemoryClient"/> when BizHawkMode = "ApiHawk".
    ///
    /// Protocol is identical to the TCP version — same 9-byte binary header.
    /// <see cref="NamedPipeClientStream"/> inherits <see cref="System.IO.Stream"/> so
    /// the read/write logic is copy-equivalent to the TCP implementation.
    ///
    /// Reconnection: on <see cref="IOException"/> or <see cref="ObjectDisposedException"/>
    /// the client waits 500 ms and retries up to <see cref="MaxRetries"/> times.
    /// </summary>
    public class BizHawkApiHawkMemoryClient : IMemoryClient
    {
        private const int MaxRetries = 5;
        private const int RetryDelayMs = 500;

        private readonly string _pipeName;
        private NamedPipeClientStream _pipe = null!;

        public BizHawkApiHawkMemoryClient(string pipeName)
        {
            _pipeName = pipeName;
        }

        // ── IMemoryClient ─────────────────────────────────────────────────────────

        public void Connect()
        {
            _pipe = CreateAndConnect();
            Console.WriteLine($"[BizHawkApiHawkMemoryClient] Connected to pipe '{_pipeName}'");
        }

        public async Task<byte> ReadByteAsync(uint address)
            => (await ReadBytesAsync(address, 1))[0];

        public async Task<ushort> ReadUShortAsync(uint address)
            => BinaryPrimitives.ReadUInt16LittleEndian(await ReadBytesAsync(address, 2));

        public async Task<uint> ReadUIntAsync(uint address)
            => BinaryPrimitives.ReadUInt32LittleEndian(await ReadBytesAsync(address, 4));

        public async Task<byte[]> ReadBytesAsync(uint address, int length)
        {
            // Mirror the address fix from BizHawkMemoryClient
            address = address > 0x02000000 ? address - 0x02000000 : address;

            return await ExecuteWithRetryAsync(async () =>
            {
                await _pipe.WriteAsync(BuildHeader(0x01, address, (uint)length));
                var buf = new byte[length];
                await ReadExactAsync(buf);
                return buf;
            });
        }

        public async Task WriteByteAsync(uint address, byte value)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await _pipe.WriteAsync(BuildHeader(0x02, address, value));
                var ack = new byte[1];
                await ReadExactAsync(ack);
                return ack;
            });
        }

        // ── IAsyncDisposable ──────────────────────────────────────────────────────

        public ValueTask DisposeAsync()
        {
            try
            {
                // Send disconnect command
                _pipe.Write(new byte[] { 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 });
            }
            catch { }
            _pipe.Dispose();
            return ValueTask.CompletedTask;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

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
                int read = await _pipe.ReadAsync(buf.AsMemory(offset));
                if (read == 0) throw new EndOfStreamException("Named pipe closed unexpectedly.");
                offset += read;
            }
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
        {
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    if (attempt == MaxRetries)
                        throw;

                    Console.WriteLine($"[BizHawkApiHawkMemoryClient] Pipe error ({ex.GetType().Name}), reconnecting in {RetryDelayMs}ms (attempt {attempt + 1}/{MaxRetries})...");
                    await Task.Delay(RetryDelayMs);
                    Reconnect();
                }
            }
            throw new InvalidOperationException("Unreachable");
        }

        private void Reconnect()
        {
            try { _pipe.Dispose(); } catch { }
            _pipe = CreateAndConnect();
            Console.WriteLine($"[BizHawkApiHawkMemoryClient] Reconnected to pipe '{_pipeName}'");
        }

        private NamedPipeClientStream CreateAndConnect()
        {
            // PipeOptions.None — synchronous I/O; async reads use ReadAsync which works on both.
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(timeout: 30_000);
            return pipe;
        }
    }
}
