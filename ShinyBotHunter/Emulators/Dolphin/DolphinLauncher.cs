using System.Diagnostics;
using System.Text;

namespace ShinyBotHunter.Emulators.Dolphin
{
    // Starts one Dolphin on the Bonus Disc with a copy of the hunt's save on the integrated GBA in
    // port 2, waits until MEM1 shows the disc booted, and stops Dolphin by process.
    //
    // Not an ILauncher: its LaunchAsync takes the per-cycle save and ROM, and Dolphin has no grid
    // position and no game file of its own, so RepositionAsync and GameFileName would be stubs.
    public sealed class DolphinLauncher(string dolphinDirectory, string isoPath) : IAsyncDisposable
    {
        // The user folder Scripts/deploy-dolphin.ps1 installs the bot's Dolphin config into; -u
        // keeps the operator's own Dolphin settings untouched.
        public const string UserFolderName = "User-ShinyBotHunter";

        private const int PollMs = 250;
        private const int ExitTimeoutMs = 10_000;

        private readonly string _userFolder = Path.Combine(dolphinDirectory, UserFolderName);
        private Process? _process;

        public DolphinMemory Memory { get; private set; } = null!;

        public int ProcessId => _process!.Id;

        // The port-2 GBA save: Dolphin names it after the ROM file with a "-2" suffix and ignores
        // a file without it. Set by LaunchAsync.
        public string GbaSavePath { get; private set; } = "";

        public async Task LaunchAsync(string saveRamPath, string romPath, TimeSpan readyTimeout, CancellationToken ct)
        {
            string savesFolder = Path.Combine(_userFolder, "GBA", "Saves");
            GbaSavePath = Path.Combine(savesFolder, Path.GetFileNameWithoutExtension(romPath) + "-2.sav");
            // A byte copy: the mGBA RTC footer BizHawk writes round-trips through Dolphin's mGBA.
            File.Copy(saveRamPath, GbaSavePath, overwrite: true);

            // -C sets the two per-launch GBA keys for this run only (checked on Dolphin 2609a).
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(dolphinDirectory, "Dolphin.exe"),
                WorkingDirectory = dolphinDirectory,
                UseShellExecute = false,
                ArgumentList =
                {
                    "-u", _userFolder,
                    "-e", isoPath,
                    "-C", "Dolphin.GBA.Rom2=" + romPath,
                    "-C", "Dolphin.GBA.SavesPath=" + savesFolder,
                },
            })!;

            DateTime deadline = DateTime.UtcNow + readyTimeout;
            while (true)
            {
                try
                {
                    Memory = new DolphinMemory(_process.Id);
                    break;
                }
                catch (FileNotFoundException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException($"Timed out after {readyTimeout.TotalSeconds:0.#} s waiting for Dolphin's memory mapping dolphin-emu.{_process.Id}.");
                }
                await Task.Delay(PollMs, ct);
            }

            while (true)
            {
                string gameId = Encoding.ASCII.GetString(Memory.ReadBytes(BonusDisc.GameId, BonusDisc.GameIdValue.Length));
                if (gameId == BonusDisc.GameIdValue) return;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"Timed out after {readyTimeout.TotalSeconds:0.#} s waiting for game id {BonusDisc.GameIdValue} at 0x{BonusDisc.GameId:X8}; last read '{gameId}'.");
                await Task.Delay(PollMs, ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Memory?.Dispose();
            if (_process is null) return;

            // Seen on this PC: one stop request shortly after launch did not close Dolphin 2609a
            // and a second one did, so a kill that does not finish is repeated once before failing.
            for (int attempt = 1; ; attempt++)
            {
                _process.Kill(entireProcessTree: true);
                using CancellationTokenSource wait = new(ExitTimeoutMs);
                try
                {
                    await _process.WaitForExitAsync(wait.Token);
                    break;
                }
                catch (OperationCanceledException) when (attempt == 1)
                {
                }
                catch (OperationCanceledException e)
                {
                    throw new TimeoutException($"Dolphin process {_process.Id} did not exit within {ExitTimeoutMs / 1000} s of two kills.", e);
                }
            }
            _process.Dispose();
        }
    }
}
