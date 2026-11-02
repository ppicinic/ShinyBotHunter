using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    public class HgssEasyChatInputTests
    {
        private static readonly HgssPointerService Table = new();

        private const ushort Klutz = 1085;
        private const ushort Teacher = 1337;
        private const ushort Steadfast = 1062;
        private const int Status = 4;
        private const int Lifestyle = 8;

        [Fact]
        public async Task KlutzInStatusFromTheFirstSlot()
        {
            FakeEasyChatApp app = new(grid: 0);
            HgssEasyChatInput input = new(app.Memory, app, Table);

            await input.EnterAsync(0, Klutz, CancellationToken.None);

            Assert.Equal("A Down Right A R R R R R Down Down Right A".Split(' '), app.Presses);
            Assert.Equal(Klutz, app.Words[0]);
        }

        [Fact]
        public async Task TeacherInLifestyleMovesRightToTheSecondSlot()
        {
            FakeEasyChatApp app = new(grid: Status);
            HgssEasyChatInput input = new(app.Memory, app, Table);

            await input.EnterAsync(1, Teacher, CancellationToken.None);

            Assert.Equal("Right A Down Right A R R R R R R R R Down Down Down A".Split(' '), app.Presses);
            Assert.Equal(Teacher, app.Words[1]);
        }

        [Fact]
        public async Task SteadfastWithTheGridAlreadyOnStatus()
        {
            FakeEasyChatApp app = new(grid: Status);
            HgssEasyChatInput input = new(app.Memory, app, Table);

            await input.EnterAsync(1, Steadfast, CancellationToken.None);

            Assert.Equal("Right A A R R R R R R R R R R R Right A".Split(' '), app.Presses);
            Assert.Equal(Steadfast, app.Words[1]);
        }

        [Fact]
        public async Task MissingWordStopsWithTheWord()
        {
            FakeEasyChatApp app = new(grid: 0);
            HgssEasyChatInput input = new(app.Memory, app, Table);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => input.EnterAsync(0, 999, CancellationToken.None));

            Assert.Contains("Word 999", ex.Message);
            Assert.Equal(new[] { "A" }, app.Presses);
        }

        [Fact]
        public async Task DroppedPressIsPressedAgainAndNotCached()
        {
            FakeEasyChatApp app = new(grid: 0);
            app.Drops["Down"] = 1;
            HgssEasyChatInput input = new(app.Memory, app, Table);

            await input.EnterAsync(0, Klutz, CancellationToken.None);
            Assert.Equal("A Down Down Right A R R R R R Down Down Right A".Split(' '), app.Presses);

            app.Reset();
            await input.EnterAsync(0, Klutz, CancellationToken.None);

            Assert.Equal("A Down Right A R R R R R Down Down Right A".Split(' '), app.Presses);
            Assert.Equal(Klutz, app.Words[0]);
        }

        [Fact]
        public async Task PressNeverConfirmedStopsAfterFourPresses()
        {
            FakeEasyChatApp app = new(grid: 0);
            app.Drops["A"] = int.MaxValue;
            HgssEasyChatInput input = new(app.Memory, app, Table);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => input.EnterAsync(0, Klutz, CancellationToken.None));

            Assert.Contains("A not confirmed", ex.Message);
            Assert.Contains($"screen stayed 0x{Table.EasyChatSlotsScreen:X8}", ex.Message);
            Assert.Equal(new[] { "A", "A", "A", "A" }, app.Presses);
        }

        [Fact]
        public async Task WrongWordInTheSlotStops()
        {
            FakeEasyChatApp app = new(grid: 0) { WrongWord = 1234 };
            HgssEasyChatInput input = new(app.Memory, app, Table);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => input.EnterAsync(0, Klutz, CancellationToken.None));

            Assert.Contains("slot 0 holds word 1234, not word 1085", ex.Message);
        }

        [Fact]
        public async Task ConfirmPressesDownThenYesAndTheAppCloses()
        {
            FakeEasyChatApp app = new(grid: 0);
            HgssEasyChatInput input = new(app.Memory, app, Table);

            await input.ConfirmAsync(CancellationToken.None);

            Assert.Equal(new[] { "Down", "A", "A" }, app.Presses);
            Assert.False(app.Open);
        }

        // The easy-chat app as the research doc describes it, over placed memory: each H.<button>.4 macro is applied
        // at once and the state written back.
        private sealed class FakeEasyChatApp : ICommandClient
        {
            private const uint Fs = 0x02100000;
            private const uint Sub0 = 0x02110000;
            private const uint App = 0x02120000;
            private const uint Data = 0x02130000;
            private const uint AnswerScreen = 0x021E8D1D;

            public FakeMemoryClient Memory { get; } = new();
            public List<string> Presses { get; } = new();
            public Dictionary<string, int> Drops { get; } = new();
            public ushort[] Words { get; } = { 0xFFFF, 0xFFFF };
            public ushort? WrongWord { get; init; }
            public bool Open { get; private set; }

            private readonly int startGrid;
            private readonly int[] counts = new int[12];
            private readonly int[] starts = new int[12];
            private readonly ushort[] area;
            private uint screen;
            private int slotRow, slotColumn, grid, scroll, maxScroll, listRow, listColumn;

            public FakeEasyChatApp(int grid)
            {
                startGrid = grid;
                // Ten filler words a group, but 130 in Status and 100 in Lifestyle so the lists scroll far enough.
                for (int g = 0; g < 12; g++)
                {
                    counts[g] = g == Status ? 130 : g == Lifestyle ? 100 : 10;
                    starts[g] = g == 0 ? 0 : starts[g - 1] + counts[g - 1];
                }
                area = new ushort[starts[11] + counts[11]];
                for (int i = 0; i < area.Length; i++) area[i] = (ushort)(2000 + i);
                area[starts[Status] + 55] = Klutz;
                area[starts[Status] + 111] = Steadfast;
                area[starts[Lifestyle] + 86] = Teacher;

                Memory.Place(Table.Anchor, BitConverter.GetBytes(Fs));
                Memory.Place((uint)(Fs + Table.AppSlotsOffset), BitConverter.GetBytes(Sub0));
                Memory.Place((uint)(App + Table.AppDataOffset), BitConverter.GetBytes(Data));
                for (int g = 0; g < 12; g++)
                {
                    Memory.Place((uint)(Data + Table.EasyChatGroupCountsOffset + 4 * g), BitConverter.GetBytes(counts[g]));
                    Memory.Place((uint)(Data + Table.EasyChatGroupStartsOffset + 4 * g), BitConverter.GetBytes(starts[g]));
                }
                for (int i = 0; i < area.Length; i++)
                    Memory.Place((uint)(Data + Table.EasyChatGroupWordsOffset + 2 * i), BitConverter.GetBytes(area[i]));
                Reset();
            }

            // Back to the slots screen with the cursors where the app opened, nothing chosen and nothing recorded.
            public void Reset()
            {
                Open = true;
                screen = Table.EasyChatSlotsScreen;
                slotRow = slotColumn = scroll = maxScroll = listRow = listColumn = 0;
                grid = startGrid;
                Words[0] = Words[1] = 0xFFFF;
                Presses.Clear();
                Write();
            }

            public void RunMacroString(string macro)
            {
                string button = macro.Split('.')[1];
                Presses.Add(button);
                if (Drops.TryGetValue(button, out int drops) && drops > 0)
                {
                    Drops[button] = drops - 1;
                    return;
                }
                if (screen == Table.EasyChatSlotsScreen) Slots(button);
                else if (screen == Table.EasyChatGroupScreen) Grid(button);
                else if (screen == Table.EasyChatListScreen) List(button);
                else if (button == "A") Open = false;
                Write();
            }

            private void Slots(string button)
            {
                if (button == "Down") slotRow = Math.Min(slotRow + 1, 2);
                else if (button == "Up") slotRow = Math.Max(slotRow - 1, 0);
                else if (button is "Left" or "Right") slotColumn = 1 - slotColumn;
                else if (button == "A") screen = slotRow == 0 ? Table.EasyChatGroupScreen : AnswerScreen;
            }

            private void Grid(string button)
            {
                if (button == "Down" && grid + 3 < 12) grid += 3;
                else if (button == "Up" && grid >= 3) grid -= 3;
                else if (button == "Right") grid = grid / 3 * 3 + (grid + 1) % 3;
                else if (button == "Left") grid = grid / 3 * 3 + (grid + 2) % 3;
                else if (button == "A")
                {
                    screen = Table.EasyChatListScreen;
                    scroll = listRow = listColumn = 0;
                    maxScroll = Math.Max(0, (counts[grid] + 1) / 2 - 5);
                }
            }

            private void List(string button)
            {
                if (button == "Down") { if (listRow < 4) listRow++; else if (scroll < maxScroll) scroll++; }
                else if (button == "Up") { if (listRow > 0) listRow--; else if (scroll > 0) scroll--; }
                else if (button == "R" && scroll + 5 <= maxScroll) scroll += 5;
                else if (button == "L" && scroll >= 5) scroll -= 5;
                else if (button is "Left" or "Right") listColumn = 1 - listColumn;
                else if (button == "A")
                {
                    Words[slotColumn] = WrongWord ?? area[starts[grid] + (scroll + listRow) * 2 + listColumn];
                    screen = Table.EasyChatSlotsScreen;
                }
            }

            private void Write()
            {
                Memory.Place((uint)(Sub0 + Table.SubAppOffset), BitConverter.GetBytes(Open ? App : 0u));
                Memory.Place((uint)(Data + Table.EasyChatWordsOffset), BitConverter.GetBytes(Words[0]));
                Memory.Place((uint)(Data + Table.EasyChatWordsOffset + 2), BitConverter.GetBytes(Words[1]));
                Memory.Place((uint)(Data + Table.EasyChatScreenOffset), BitConverter.GetBytes(screen));
                Memory.Place((uint)(Data + Table.EasyChatSlotRowOffset), new[] { (byte)slotRow });
                Memory.Place((uint)(Data + Table.EasyChatSlotColumnOffset), new[] { (byte)slotColumn });
                Memory.Place((uint)(Data + Table.EasyChatGroupCursorOffset), new[] { (byte)grid });
                Memory.Place((uint)(Data + Table.EasyChatListScrollOffset), BitConverter.GetBytes((ushort)scroll));
                Memory.Place((uint)(Data + Table.EasyChatListMaxScrollOffset), BitConverter.GetBytes((ushort)maxScroll));
                Memory.Place((uint)(Data + Table.EasyChatListColumnOffset), new[] { (byte)listColumn });
                Memory.Place((uint)(Data + Table.EasyChatListRowOffset), new[] { (byte)listRow });
            }

            public Task WaitForMacroAsync(CancellationToken ct = default) => Task.CompletedTask;

            public void Connect() => throw new NotImplementedException();
            public void Tap(string button) => throw new NotImplementedException();
            public void Hold(string button) => throw new NotImplementedException();
            public void HoldFrames(string button, int frames) => throw new NotImplementedException();
            public void Release() => throw new NotImplementedException();
            public void Release(string button) => throw new NotImplementedException();
            public void RunRepeatMacro(string macro) => throw new NotImplementedException();
            public void RunMacros(params MacroStruct[] macro) => throw new NotImplementedException();
            public void ClearMacro() => throw new NotImplementedException();
            public Task FlushSave(CancellationToken ct = default) => throw new NotImplementedException();
            public Task StartRecordingAsync(CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string> StopRecordingAsync(CancellationToken ct = default) => throw new NotImplementedException();
            public Task<string> GetRecordingStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
            public ValueTask DisposeAsync() => throw new NotImplementedException();
        }
    }
}
