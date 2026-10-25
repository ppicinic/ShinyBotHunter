using ShinyBotHunter.Emulators.Dolphin;

namespace ShinyBotHunter.Tests
{
    // The drive runs in real time with the const intervals; the asserts check results only, never durations.
    public class BonusDiscTests
    {
        private static Dictionary<uint, byte> Menu(byte cursor, byte active, byte power) => new()
        {
            [BonusDisc.MenuCursor] = cursor,
            [BonusDisc.GiftActive] = active,
            [BonusDisc.GiftPhase] = 0,
            [BonusDisc.WaitingForPower] = power,
        };

        private static string TempSave()
        {
            string path = Path.GetTempFileName();
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc));
            return path;
        }

        // Already on the Jirachi entry with the gift flow just started: active reads 0 for the
        // menu step's check, then 1, so neither of the first two steps presses.
        private static Func<uint, byte> PastTheMenu(Dictionary<uint, byte> memory)
        {
            bool activeRead = false;
            return a =>
            {
                if (a != BonusDisc.GiftActive) return memory[a];
                byte active = activeRead ? (byte)1 : (byte)0;
                activeRead = true;
                return active;
            };
        }

        [Fact]
        public async Task DrivesMenuToTransfer()
        {
            Dictionary<uint, byte> memory = Menu(cursor: 1, active: 0, power: 0);
            string save = TempSave();
            List<int> presses = [];
            int resets = 0;
            Task Press(int button, CancellationToken _)
            {
                presses.Add(button);
                if (button == DolphinPad.PadE) memory[BonusDisc.MenuCursor] = 2;
                else if (memory[BonusDisc.GiftActive] == 0) { memory[BonusDisc.GiftActive] = 1; memory[BonusDisc.MenuCursor] = 0; }
                else memory[BonusDisc.WaitingForPower] = 1;
                return Task.CompletedTask;
            }
            Task Reset(CancellationToken _)
            {
                resets++;
                memory[BonusDisc.WaitingForPower] = 0;
                memory[BonusDisc.GiftPhase] = 6;
                File.SetLastWriteTimeUtc(save, DateTime.UtcNow);
                return Task.CompletedTask;
            }

            await BonusDisc.DriveGiftAsync(a => memory[a], Press, Reset, save, 60, 10, 40, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal([DolphinPad.PadE, DolphinPad.Cross, DolphinPad.Cross], presses);
            Assert.Equal(1, resets);
            File.Delete(save);
        }

        [Fact]
        public async Task MenuLoopExhaustedThrowsWithBytes()
        {
            Dictionary<uint, byte> memory = Menu(cursor: 1, active: 0, power: 0);
            int presses = 0;

            TimeoutException e = await Assert.ThrowsAsync<TimeoutException>(() => BonusDisc.DriveGiftAsync(
                a => memory[a], (_, _) => { presses++; return Task.CompletedTask; }, _ => Task.CompletedTask,
                "unused.sav", 1, 10, 40, TimeSpan.FromSeconds(5), CancellationToken.None));

            Assert.Contains("the Jirachi menu entry", e.Message);
            Assert.Contains("cursor 1, active 0", e.Message);
            Assert.Equal(1, presses);
        }

        [Fact]
        public async Task PromptLoopExhaustedThrowsWithBytes()
        {
            Dictionary<uint, byte> memory = Menu(cursor: 2, active: 0, power: 0);
            int presses = 0;

            TimeoutException e = await Assert.ThrowsAsync<TimeoutException>(() => BonusDisc.DriveGiftAsync(
                PastTheMenu(memory), (_, _) => { presses++; return Task.CompletedTask; }, _ => Task.CompletedTask,
                "unused.sav", 60, 10, 1, TimeSpan.FromSeconds(5), CancellationToken.None));

            Assert.Contains("the power prompt", e.Message);
            Assert.Contains("active 1, phase 0, wait-for-power 0", e.Message);
            Assert.Equal(1, presses);
        }

        [Fact]
        public async Task TransferWithoutSaveChangeThrows()
        {
            Dictionary<uint, byte> memory = Menu(cursor: 2, active: 0, power: 1);
            string save = TempSave();

            TimeoutException e = await Assert.ThrowsAsync<TimeoutException>(() => BonusDisc.DriveGiftAsync(
                PastTheMenu(memory), (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
                save, 60, 10, 40, TimeSpan.FromMilliseconds(500), CancellationToken.None));

            Assert.Contains("the transfer", e.Message);
            Assert.Contains(".sav mtime 2026-10-25T12:00:00.0000000Z before the reset, 2026-10-25T12:00:00.0000000Z last", e.Message);
            Assert.Contains("phase 0", e.Message);
            File.Delete(save);
        }
    }
}
