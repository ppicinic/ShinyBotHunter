using Microsoft.Extensions.Logging;
using ShinyBotHunter.Persistence;
using System.Threading.Channels;

namespace ShinyBotHunter.Tests
{
    public class SupervisedLoopTests
    {
        [Fact]
        public async Task RestartsAfterThrowAndLogsTheException()
        {
            var logger = new RecordingLogger();
            var failure = new InvalidOperationException("boom");
            var calls = 0;

            await BackgroundLoop.RunSupervisedAsync(() =>
            {
                calls++;
                return calls == 1 ? Task.FromException(failure) : Task.CompletedTask;
            }, logger, "test");

            Assert.Equal(2, calls);
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Same(failure, entry.Exception);
        }

        [Fact]
        public async Task ReturnsWhenTheLoopReturns()
        {
            var logger = new RecordingLogger();
            var calls = 0;

            await BackgroundLoop.RunSupervisedAsync(() =>
            {
                calls++;
                return Task.CompletedTask;
            }, logger, "test");

            Assert.Equal(1, calls);
            Assert.Empty(logger.Entries);
        }

        [Fact]
        public async Task DrainsTheChannelAfterAFaultAndStopsOnComplete()
        {
            var logger = new RecordingLogger();
            var channel = Channel.CreateUnbounded<int>();
            var processed = new List<int>();

            async Task Drain()
            {
                await foreach (var item in channel.Reader.ReadAllAsync())
                {
                    if (item == 1)
                    {
                        throw new InvalidOperationException("bad item");
                    }
                    processed.Add(item);
                }
            }

            var supervisor = BackgroundLoop.RunSupervisedAsync(Drain, logger, "test");
            await channel.Writer.WriteAsync(1);
            await channel.Writer.WriteAsync(2);
            await channel.Writer.WriteAsync(3);
            channel.Writer.TryComplete();

            await supervisor.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { 2, 3 }, processed);
            Assert.Single(logger.Entries);
        }

        private class RecordingLogger : ILogger
        {
            public List<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, exception));
            }
        }
    }
}
