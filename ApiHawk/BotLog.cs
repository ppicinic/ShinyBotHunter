using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ApiHawk
{
    /// <summary>
    /// File log for the bot servers.
    ///
    /// EmuHawk is a GUI process launched without stdout redirection, so Console.WriteLine from this
    /// assembly lands nowhere reliable -- every diagnostic written here used to be invisible, which
    /// left crashes to be reasoned about rather than read. This writes to a file instead.
    ///
    /// Three properties matter more than convenience:
    ///   Bounded  -- capped size with a single rollover, so the log can never itself be the thing
    ///               that exhausts memory or disk.
    ///   Durable  -- flushed on every line. The interesting case is the tail right before a crash,
    ///               which a buffered writer would lose.
    ///   Quiet    -- WriteThrottled collapses repeats, so a condition that recurs at poll rate costs
    ///               one line per window instead of thousands. That also keeps disk I/O off the
    ///               emulator's frame thread in the pathological case.
    ///
    /// Never throws: a logging failure must not take down the emulator.
    /// </summary>
    internal static class BotLog
    {
        private const long MaxBytes = 4L * 1024 * 1024;
        private const int DefaultThrottleMs = 1000;

        private static readonly object Gate = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, ThrottleState> Throttles = new Dictionary<string, ThrottleState>();

        private static StreamWriter? _writer;
        private static string? _path;
        private static long _written;

        private sealed class ThrottleState
        {
            public long LastEmittedMs;
            public int Suppressed;
        }

        /// <summary>
        /// Opens the log. The directory can be overridden with BOTSERVER_LOG_DIR; otherwise the file
        /// sits beside BotServer.dll, with the temp directory as a fallback when that is not writable.
        /// The port and process id are in the name so several emulators can log side by side.
        /// </summary>
        public static void Init(int memoryPort)
        {
            lock (Gate)
            {
                if (_writer != null)
                    return;

                int pid;
                try { pid = Process.GetCurrentProcess().Id; }
                catch { pid = 0; }

                string name = $"BotServer.{memoryPort}.{pid}.log";
                foreach (string dir in CandidateDirectories())
                {
                    if (TryOpen(Path.Combine(dir, name)))
                    {
                        WriteLine($"=== BotServer log opened {DateTime.Now:yyyy-MM-dd HH:mm:ss} (pid {pid}, memory port {memoryPort}) ===");
                        WriteLine($"=== Log file: {_path} ===");
                        return;
                    }
                }
            }
        }

        private static IEnumerable<string> CandidateDirectories()
        {
            string? env = null;
            try { env = Environment.GetEnvironmentVariable("BOTSERVER_LOG_DIR"); }
            catch { }
            if (!string.IsNullOrWhiteSpace(env))
                yield return env!;

            string? assemblyDir = null;
            try { assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch { }
            if (!string.IsNullOrWhiteSpace(assemblyDir))
                yield return assemblyDir!;

            string? temp = null;
            try { temp = Path.GetTempPath(); }
            catch { }
            if (!string.IsNullOrWhiteSpace(temp))
                yield return temp!;
        }

        private static bool TryOpen(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _path = path;
                _written = 0;
                return true;
            }
            catch
            {
                _writer = null;
                _path = null;
                return false;
            }
        }

        public static void Write(string message)
        {
            lock (Gate)
            {
                WriteLine(message);
            }
        }

        /// <summary>
        /// Writes at most one line per <paramref name="windowMs"/> for a given <paramref name="key"/>,
        /// reporting how many were folded in. Use this for anything a polling caller can trigger --
        /// an unhelpful condition repeating 30 times a second should not be able to fill the log.
        /// </summary>
        public static void WriteThrottled(string key, string message, int windowMs = DefaultThrottleMs)
        {
            lock (Gate)
            {
                long now = Clock.ElapsedMilliseconds;
                if (!Throttles.TryGetValue(key, out ThrottleState state))
                {
                    state = new ThrottleState { LastEmittedMs = long.MinValue };
                    Throttles[key] = state;
                }

                if (state.LastEmittedMs != long.MinValue && now - state.LastEmittedMs < windowMs)
                {
                    state.Suppressed++;
                    return;
                }

                string suffix = state.Suppressed > 0 ? $" (+{state.Suppressed} similar suppressed)" : string.Empty;
                state.Suppressed = 0;
                state.LastEmittedMs = now;
                WriteLine(message + suffix);
            }
        }

        // Caller holds Gate.
        private static void WriteLine(string message)
        {
            if (_writer == null)
                return;
            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}";
                _writer.WriteLine(line);
                _written += line.Length + 2;
                if (_written >= MaxBytes)
                    Roll();
            }
            catch
            {
                // A broken log must never propagate into the emulator.
            }
        }

        // Caller holds Gate. Keeps one previous file so a rollover mid-incident doesn't lose the run-up.
        private static void Roll()
        {
            if (_path == null)
                return;
            string path = _path;
            try
            {
                _writer?.Dispose();
                _writer = null;

                string previous = path + ".1";
                if (File.Exists(previous))
                    File.Delete(previous);
                File.Move(path, previous);
            }
            catch
            {
            }

            if (!TryOpen(path))
                return;
            _writer!.WriteLine($"{DateTime.Now:HH:mm:ss.fff} === continued after rollover ===");
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                try
                {
                    if (_writer != null)
                    {
                        WriteLine("=== BotServer log closed ===");
                        _writer.Dispose();
                    }
                }
                catch
                {
                }
                _writer = null;
                _path = null;
                Throttles.Clear();
            }
        }
    }
}
