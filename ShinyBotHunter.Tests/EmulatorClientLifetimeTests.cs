using System.Reflection;
using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// A bot is launched, disposed and launched again during a hunt: creating its save uses a
    /// throwaway emulator session before the real one starts. The HTTP client has to survive that.
    ///
    /// It did not. The client was built once in the constructor and Connect was a no-op, so a
    /// disposed bot stayed dead and every later call threw ObjectDisposedException -- which read as
    /// the emulator being unreachable rather than as the client having been discarded.
    ///
    /// These assert on the client instance rather than by issuing a request. A request to a port
    /// nothing is listening on is treated as transient and burns the full retry budget, which turned
    /// four tests into a minute of waiting; the thing worth checking is whether reconnecting rebuilds
    /// the client, and that is directly observable.
    /// </summary>
    public class EmulatorClientLifetimeTests
    {
        // Nothing binds this; no request is ever issued.
        private const int UnusedPort = 59997;

        private static HttpClient ClientOf(object target)
        {
            FieldInfo field = target.GetType()
                .GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? typeof(BizHawkClientBase).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (HttpClient)field.GetValue(target)!;
        }

        private static bool IsDisposed(HttpClient client)
        {
            try
            {
                // Any property touch on a disposed HttpClient throws.
                _ = client.BaseAddress;
                client.Timeout = client.Timeout;
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        [Fact]
        public async Task MemoryClient_ReconnectingAfterDisposeRebuildsTheClient()
        {
            var client = new BizHawkMemoryClient(port: UnusedPort);
            client.Connect();
            HttpClient original = ClientOf(client);

            await client.DisposeAsync();
            Assert.True(IsDisposed(original), "Dispose should have torn down the underlying client.");

            client.Connect();
            HttpClient rebuilt = ClientOf(client);

            Assert.NotSame(original, rebuilt);
            Assert.False(IsDisposed(rebuilt), "Reconnecting must leave a usable client behind.");
            await client.DisposeAsync();
        }

        [Fact]
        public async Task CommandClient_ReconnectingAfterDisposeRebuildsTheClient()
        {
            var client = new BizHawkCommandClient(port: UnusedPort);
            client.Connect();
            HttpClient original = ClientOf(client);

            await client.DisposeAsync();
            client.Connect();

            Assert.NotSame(original, ClientOf(client));
            Assert.False(IsDisposed(ClientOf(client)));
            await client.DisposeAsync();
        }

        [Fact]
        public async Task ConnectingWithoutDisposingKeepsTheSameClient()
        {
            // Connect is called on every launch, so it must not churn the client needlessly.
            var client = new BizHawkMemoryClient(port: UnusedPort);
            client.Connect();
            HttpClient first = ClientOf(client);

            client.Connect();

            Assert.Same(first, ClientOf(client));
            await client.DisposeAsync();
        }

        [Fact]
        public async Task DisposingTwiceIsHarmless()
        {
            var client = new BizHawkMemoryClient(port: UnusedPort);

            await client.DisposeAsync();
            await client.DisposeAsync();
        }
    }
}
