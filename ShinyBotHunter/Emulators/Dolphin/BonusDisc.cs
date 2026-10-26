namespace ShinyBotHunter.Emulators.Dolphin
{
    // The Pokemon Colosseum Bonus Disc (USA): the MEM1 bytes the hunt reads, and the drive from the
    // disc's main menu to a finished Jirachi transfer on them, with no screen read.
    //
    // Origin of every address: snapshot diff of MEM1 on Dolphin 2609a, two snapshots per screen,
    // keeping bytes stable within a screen and different between screens; see the research doc,
    // row "Disc state bytes" (docs/research/wishmaker-jirachi.md). Proving scenario: rs-jirachi-soak.
    public static class BonusDisc
    {
        // "PC6E01", six ASCII bytes at the start of MEM1, once the disc has booted.
        public const uint GameId = 0x8000_0000;
        public const string GameIdValue = "PC6E01";

        // Main menu cursor: 0 movie, 1 Colosseum (where the menu opens), 2 Jirachi Special Gift;
        // 0 once the gift flow starts, 255 after the transfer. Heap-like: the disc allocates
        // deterministically and the address held on seven boots, but it is verified by behaviour
        // (Right until it reads 2, else fail) and read only while on the menu.
        public const uint MenuCursor = 0x80D0_ED38;

        // Gift flow active: 0 on the menu, 1 from Play onward. Static data.
        public const uint GiftActive = 0x800C_662D;

        // Gift phase: 0 instruction screens, 1 after Yes, 2 GAME BOY screens, 3 power prompt,
        // 5 transferring, 6 done. Static data. Logged, and gates only the end of the transfer.
        public const uint GiftPhase = 0x800C_6631;

        // Waiting for GBA power: 1 only at the "turn on the power" prompt. Static data.
        public const uint WaitingForPower = 0x800C_7D63;

        // Presses during screen transitions are eaten, so each press is followed by a wait long
        // enough for the screen it causes (spike: 14 A presses at 1.5 s reach the power prompt).
        public const int RightIntervalMs = 1_000;
        public const int PlayIntervalMs = 2_000;
        public const int PromptIntervalMs = 1_500;
        // How long the caller's pad holds a press: several DSU frames, well under every interval.
        public const int HoldMs = 150;
        // After the .sav changes, the disc's last writes and the GBA settle before the save is read.
        public const int SettleMs = 2_000;

        private const int TransferPollMs = 250;
        private const byte TransferDonePhase = 6;

        // readByte, press and resetGba are the test seam for Dolphin, which cannot run in a test:
        // the event passes DolphinMemory.ReadByte, DolphinPad.PressAsync with HoldMs, and
        // DolphinGbaWindow.ResetAsync. Each exhausted limit throws with the bytes it saw last.
        public static async Task DriveGiftAsync(
            Func<uint, byte> readByte,
            Func<int, CancellationToken, Task> press,
            Func<CancellationToken, Task> resetGba,
            string gbaSavePath,
            int maxRightPresses,
            int maxPlayPresses,
            int maxPromptPresses,
            TimeSpan transferTimeout,
            CancellationToken ct)
        {
            string Bytes() =>
                $"cursor {readByte(MenuCursor)}, active {readByte(GiftActive)}, phase {readByte(GiftPhase)}, wait-for-power {readByte(WaitingForPower)}";

            await PressUntilAsync("the Jirachi menu entry", DolphinPad.PadE, RightIntervalMs, maxRightPresses,
                () => readByte(MenuCursor) == 2 && readByte(GiftActive) == 0, Bytes, press, ct);
            Console.WriteLine($"Bonus Disc: on the Jirachi entry ({Bytes()})");

            await PressUntilAsync("the gift flow", DolphinPad.Cross, PlayIntervalMs, maxPlayPresses,
                () => readByte(GiftActive) == 1, Bytes, press, ct);
            Console.WriteLine($"Bonus Disc: gift flow started ({Bytes()})");

            await PressUntilAsync("the power prompt", DolphinPad.Cross, PromptIntervalMs, maxPromptPresses,
                () => readByte(WaitingForPower) == 1, Bytes, press, ct);
            Console.WriteLine($"Bonus Disc: power prompt ({Bytes()})");

            DateTime before = File.GetLastWriteTimeUtc(gbaSavePath);
            await resetGba(ct);
            Console.WriteLine("Bonus Disc: GBA reset posted, waiting for the transfer");

            DateTime deadline = DateTime.UtcNow + transferTimeout;
            while (true)
            {
                DateTime now = File.GetLastWriteTimeUtc(gbaSavePath);
                if (now != before && readByte(GiftPhase) >= TransferDonePhase) break;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"Bonus Disc: timed out after {transferTimeout.TotalSeconds:0.#} s waiting for the transfer (the .sav to change and phase >= {TransferDonePhase}); "
                        + $".sav mtime {before:O} before the reset, {now:O} last; {Bytes()}.");
                await Task.Delay(TransferPollMs, ct);
            }
            Console.WriteLine($"Bonus Disc: transfer done ({Bytes()})");

            await Task.Delay(SettleMs, ct);
        }

        private static async Task PressUntilAsync(
            string waitingFor, int button, int intervalMs, int maxPresses, Func<bool> done, Func<string> bytes,
            Func<int, CancellationToken, Task> press, CancellationToken ct)
        {
            for (int presses = 0; !done(); presses++)
            {
                if (presses == maxPresses)
                    throw new TimeoutException($"Bonus Disc: {presses} presses did not reach {waitingFor}; last {bytes()}.");
                await press(button, ct);
                await Task.Delay(intervalMs, ct);
            }
        }
    }
}
