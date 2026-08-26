using Microsoft.Extensions.Options;

namespace ShinyBotHunter.Emulators
{
    public class BizHawkReaderWriterLock
    {
        private SemaphoreSlim _readerLock;
        private SemaphoreSlim _writerLock;
        private int slots;
        public BizHawkReaderWriterLock(IOptions<ApplicationSettings> settings)
        {
            // Sized against every console's budget: this gate is global, and a GBA shutdown must
            // still serialise against DS launches.
            slots = settings.Value.TotalMaxEmulators;
            _readerLock = new SemaphoreSlim(slots, slots);
            _writerLock = new SemaphoreSlim(1, 1);
        }

        public async Task<IAsyncDisposable> GetReaderLockAsync()
        {
            await _readerLock.WaitAsync();
            return AsyncDisposable(() =>
            {
                _readerLock.Release();
                return ValueTask.CompletedTask;
            });
        }

        public async Task<IAsyncDisposable> GetWriterLockAsync()
        {
            await _writerLock.WaitAsync();
            for (int i = 0; i < slots; i++)
            {
                await _readerLock.WaitAsync();
            }
            return AsyncDisposable(() =>
            {
                _readerLock.Release(slots);
                _writerLock.Release();
                return ValueTask.CompletedTask;
            });
        }

        private IAsyncDisposable AsyncDisposable(Func<ValueTask> onDispose)
        => new AsyncDisposableAction(onDispose);

        private sealed class AsyncDisposableAction : IAsyncDisposable
        {
            private readonly Func<ValueTask> _onDispose;
            public AsyncDisposableAction(Func<ValueTask> onDispose) => _onDispose = onDispose;
            public ValueTask DisposeAsync() => _onDispose();
        }
    }

    
}
