using Microsoft.Extensions.Options;
using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The launch/shutdown gate. No sleeps: a held lock is the disposable the test has not yet
    /// disposed, "blocked" is an incomplete task, and every await after a release has a 5 s hang guard.
    /// </summary>
    public class BizHawkReaderWriterLockTests
    {
        private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(5);

        private static BizHawkReaderWriterLock NewLock(int slots)
            => new(Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = slots } }));

        [Fact]
        public async Task TwoReaders_HoldTheLockAtTheSameTime()
        {
            var gate = NewLock(2);

            var a = gate.GetReaderLockAsync();
            var b = gate.GetReaderLockAsync();

            Assert.True(a.IsCompleted);
            Assert.True(b.IsCompleted);
            await (await a).DisposeAsync();
            await (await b).DisposeAsync();
        }

        [Fact]
        public async Task Writer_WaitsForHeldReader_AndRunsAfterItReleases()
        {
            var gate = NewLock(2);
            var reader = await gate.GetReaderLockAsync();

            var writer = gate.GetWriterLockAsync();
            Assert.False(writer.IsCompleted);

            await reader.DisposeAsync();
            var held = await writer.WaitAsync(HangGuard);
            Assert.True(writer.IsCompletedSuccessfully);
            await held.DisposeAsync();
        }

        [Fact]
        public async Task Writer_ExcludesNewReaders()
        {
            var gate = NewLock(2);
            var writer = gate.GetWriterLockAsync();
            Assert.True(writer.IsCompleted);

            var reader = gate.GetReaderLockAsync();
            Assert.False(reader.IsCompleted);

            await (await writer).DisposeAsync();
            var held = await reader.WaitAsync(HangGuard);
            await held.DisposeAsync();
        }

        [Fact]
        public async Task ReadersQueuedBehindWriter_AllRunAfterItReleases()
        {
            var gate = NewLock(2);
            var writer = await gate.GetWriterLockAsync();

            var b = gate.GetReaderLockAsync();
            var c = gate.GetReaderLockAsync();
            Assert.False(b.IsCompleted);
            Assert.False(c.IsCompleted);

            await writer.DisposeAsync();
            var held = await Task.WhenAll(b, c).WaitAsync(HangGuard);
            Assert.True(b.IsCompletedSuccessfully);
            Assert.True(c.IsCompletedSuccessfully);
            foreach (var h in held)
            {
                await h.DisposeAsync();
            }
        }
    }
}
