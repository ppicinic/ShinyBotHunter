using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Tests
{
    public class BizHawkMemoryClientTests
    {
        // Nothing binds this. A request that ignored the token would be refused, treated as
        // transient, and retried for about 8 seconds before surfacing as HttpRequestException.
        private const int UnusedPort = 59996;

        [Fact]
        public async Task ReadValidatedParty_PreCancelledToken_ThrowsWithoutRetrying()
        {
            var client = new BizHawkMemoryClient(port: UnusedPort);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadValidatedPartyAsync(
                anchorAddress: 0, countOffset: 0, slotsOffset: 0, slotSize: 1, slotStride: 1, maxSlots: 1, maxRetryFrames: 1, ct: cts.Token));
        }
    }
}
