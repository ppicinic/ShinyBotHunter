using System.Net.Http;

namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// Base for the HTTP-based emulator clients. Holds a shared <see cref="HttpClient"/>
    /// pointed at the server's loopback base URL and provides retry helpers so a transient
    /// failure (e.g. the emulator briefly unavailable) does not abort a request.
    /// </summary>
    public abstract class BizHawkClientBase : IAsyncDisposable
    {
        // Not readonly: a bot can be launched, disposed and launched again -- creating a save uses
        // a throwaway emulator session before the real one -- and the client has to survive that.
        protected HttpClient _http;
        private bool _disposed;
        private readonly TimeSpan _timeout;
        protected readonly string _baseUrl;
        private readonly string _name;

        private const int MaxRetries = 10;
        private const int RetryDelayMs = 200;
        private const int MaxRetryDelayMs = 1000;

        // Linear backoff, capped — gives the emulator/server time to come up on the first
        // request while still recovering quickly from a brief transient blip.
        private static int BackoffMs(int attempt) => Math.Min(RetryDelayMs * (attempt + 1), MaxRetryDelayMs);

        protected BizHawkClientBase(string name, string host, int port, TimeSpan? timeout = null)
        {
            _name = name;
            _baseUrl = $"http://{host}:{port}/";
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
            _http = NewClient();
        }

        private HttpClient NewClient() => new()
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = _timeout,
        };

        public void Connect()
        {
            // HTTP is connectionless, so there is nothing to establish up front -- except after a
            // dispose, which tears down the HttpClient for good. Reconnecting has to build a new one
            // or every later request throws ObjectDisposedException, which reads as the emulator
            // being unreachable rather than as the client having been thrown away.
            if (_disposed)
            {
                _http = NewClient();
                _disposed = false;
            }
            Console.WriteLine($"[{_name}] Using {_baseUrl}");
        }

        protected abstract void Disconnect();

        private static bool IsTransient(Exception ex, CancellationToken ct)
        {
            if (ex is HttpRequestException) return true;
            if (ex is IOException) return true;
            // HttpClient surfaces request timeouts as TaskCanceledException; only treat it as
            // transient when the caller did not actually cancel.
            if (ex is TaskCanceledException && !ct.IsCancellationRequested) return true;
            return false;
        }

        protected async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < MaxRetries && IsTransient(ex, ct))
                {
                    Console.WriteLine($"[{_name}] request failed (attempt {attempt + 1}/{MaxRetries}): {ex.Message}; retrying...");
                    await Task.Delay(BackoffMs(attempt), ct).ConfigureAwait(false);
                }
            }
        }

        protected void ExecuteWithRetry(Action action)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (attempt < MaxRetries && IsTransient(ex, CancellationToken.None))
                {
                    Console.WriteLine($"[{_name}] request failed (attempt {attempt + 1}/{MaxRetries}): {ex.Message}; retrying...");
                    Thread.Sleep(BackoffMs(attempt));
                }
            }
        }

        public virtual ValueTask DisposeAsync()
        {
            try { Disconnect(); } catch { }
            _http.Dispose();
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
