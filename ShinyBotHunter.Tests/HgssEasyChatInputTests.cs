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
            HgssEasyChatInput input = new(app, app, Table);

            await input.EnterAsync(0, Klutz, CancellationToken.None);

            Assert.Equal("A Down Right A R R R R R Down Down Right A".Split(' '), app.Presses);
            Assert.Equal(Klutz, app.Words[0]);
        }

        [Fact]
        public async Task TeacherInLifestyleMovesRightToTheSecondSlot()
        {
            FakeEasyChatApp app = new(grid: Status);
            HgssEasyChatInput input = new(app, app, Table);

            await input.EnterAsync(1, Teacher, CancellationToken.None);

            Assert.Equal("Right A Down Right A R R R R R R R R Down Down Down A".Split(' '), app.Presses);
            Assert.Equal(Teacher, app.Words[1]);
        }

        [Fact]
        public async Task SteadfastWithTheGridAlreadyOnStatus()
        {
            FakeEasyChatApp app = new(grid: Status);
            HgssEasyChatInput input = new(app, app, Table);

            await input.EnterAsync(1, Steadfast, CancellationToken.None);

            Assert.Equal("Right A A R R R R R R R R R R R Right A".Split(' '), app.Presses);
            Assert.Equal(Steadfast, app.Words[1]);
        }

        [Fact]
        public async Task MissingWordStopsWithTheWord()
        {
            FakeEasyChatApp app = new(grid: 0);
            HgssEasyChatInput input = new(app, app, Table);

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
            HgssEasyChatInput input = new(app, app, Table);

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
            HgssEasyChatInput input = new(app, app, Table);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => input.EnterAsync(0, Klutz, CancellationToken.None));

            Assert.Contains("A not confirmed", ex.Message);
            Assert.Contains($"screen and step stayed 0x{Table.EasyChatSlotsScreen:X8} step 0", ex.Message);
            Assert.Equal(new[] { "A", "A", "A", "A" }, app.Presses);
        }

        [Fact]
        public async Task PressWaitsUntilTheAppIsIdle()
        {
            FakeEasyChatApp app = new(grid: 0) { TransitionReads = 10 };
            HgssEasyChatInput input = new(app, app, Table);

            await input.EnterAsync(0, Klutz, CancellationToken.None);

            Assert.Equal("A Down Right A R R R R R Down Down Right A".Split(' '), app.Presses);
            Assert.Equal(0, app.Ignored);
            Assert.Equal(Klutz, app.Words[0]);
        }

        // #262's failed run: the grid A landed and the list took longer than the old 1.5 s gate, which pressed A again.
        [Fact]
        public async Task LateScreenChangeIsNotPressedAgain()
        {
            FakeEasyChatApp app = new(grid: 0) { ListOpenReads = 50 };
            HgssEasyChatInput input = new(app, app, Table);

            await input.EnterAsync(0, Klutz, CancellationToken.None);

            Assert.Equal("A Down Right A R R R R R Down Down Right A".Split(' '), app.Presses);
            Assert.Equal(0, app.Ignored);
            Assert.Equal(Klutz, app.Words[0]);
        }

        [Fact]
        public async Task DroppedGridAIsPressedAgainOnce()
        {
            FakeEasyChatApp app = new(grid: 0) { GridADrops = 1 };
            HgssEasyChatInput input = new(app, app, Table);

            await input.EnterAsync(0, Klutz, CancellationToken.None);

            Assert.Equal("A Down Right A A R R R R R Down Down Right A".Split(' '), app.Presses);
            Assert.Equal(Klutz, app.Words[0]);
        }

        [Fact]
        public async Task WrongWordInTheSlotStops()
        {
            FakeEasyChatApp app = new(grid: 0) { WrongWord = 1234 };
            HgssEasyChatInput input = new(app, app, Table);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => input.EnterAsync(0, Klutz, CancellationToken.None));

            Assert.Contains("slot 0 holds word 1234, not word 1085", ex.Message);
        }

        [Fact]
        public async Task ConfirmPressesDownThenYesAndTheAppCloses()
        {
            FakeEasyChatApp app = new(grid: 0);
            HgssEasyChatInput input = new(app, app, Table);

            await input.ConfirmAsync(CancellationToken.None);

            Assert.Equal(new[] { "Down", "A", "A" }, app.Presses);
            Assert.False(app.Open);
        }

        [Fact]
        public async Task DroppedConfirmIsPressedAgainOnce()
        {
            FakeEasyChatApp app = new(grid: 0);
            app.Drops["A"] = 1;
            HgssEasyChatInput input = new(app, app, Table);

            await input.ConfirmAsync(CancellationToken.None);

            Assert.Equal(new[] { "Down", "A", "A", "A" }, app.Presses);
            Assert.False(app.Open);
        }

        [Fact]
        public async Task FirstPressWaitsForTheOpeningHandler()
        {
            FakeEasyChatApp app = new(grid: 0, opening: true);
            HgssEasyChatInput input = new(app, app, Table);
            using CancellationTokenSource cts = new(300);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => input.EnterAsync(0, Klutz, cts.Token));

            Assert.Empty(app.Presses);
        }

        // The easy-chat app as the research doc describes it, over placed memory, with the read count as its clock.
        // An A writes its step (slots 4, grid 1, list 3; Confirm 1, then the question at + 0x2C) when pressed, and the
        // screen changes a number of reads later, as the game's graphics tasks end. While the step is not 0, or a
        // handler other than the question reading input is at + 0x2C, the game ignores every press (counted).
        private sealed class FakeEasyChatApp : ICommandClient, IMemoryClient
        {
            private const uint Fs = 0x02100000;
            private const uint Sub0 = 0x02110000;
            private const uint App = 0x02120000;
            private const uint Data = 0x02130000;
            private const uint OpeningHandler = 0x021E8D61;
            private const uint ClosingScreen = 0x021E8D1D;

            public List<string> Presses { get; } = new();
            public Dictionary<string, int> Drops { get; } = new();
            public ushort[] Words { get; } = { 0xFFFF, 0xFFFF };
            public ushort? WrongWord { get; init; }
            public int TransitionReads { get; init; } = 2;
            public int? ListOpenReads { get; init; }
            public int GridADrops { get; set; }
            public int Ignored { get; private set; }
            public bool Open { get; private set; }

            private readonly FakeMemoryClient memory = new();
            private readonly Queue<(int Reads, Action Apply)> pending = new();
            private int readsLeft;
            private readonly int startGrid;
            private readonly bool startOpening;
            private readonly int[] counts = new int[12];
            private readonly int[] starts = new int[12];
            private readonly ushort[] area;
            private uint screen, question;
            private int step, questionStep;
            private int slotRow, slotColumn, grid, scroll, maxScroll, listRow, listColumn;

            public FakeEasyChatApp(int grid, bool opening = false)
            {
                startGrid = grid;
                startOpening = opening;
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

                memory.Place(Table.Anchor, BitConverter.GetBytes(Fs));
                memory.Place((uint)(Fs + Table.AppSlotsOffset), BitConverter.GetBytes(Sub0));
                memory.Place((uint)(App + Table.AppDataOffset), BitConverter.GetBytes(Data));
                for (int g = 0; g < 12; g++)
                {
                    memory.Place((uint)(Data + Table.EasyChatGroupCountsOffset + 4 * g), BitConverter.GetBytes(counts[g]));
                    memory.Place((uint)(Data + Table.EasyChatGroupStartsOffset + 4 * g), BitConverter.GetBytes(starts[g]));
                }
                for (int i = 0; i < area.Length; i++)
                    memory.Place((uint)(Data + Table.EasyChatGroupWordsOffset + 2 * i), BitConverter.GetBytes(area[i]));
                Reset();
            }

            // Back to the slots screen with the cursors where the app opened, nothing chosen and nothing recorded.
            public void Reset()
            {
                Open = true;
                screen = Table.EasyChatSlotsScreen;
                question = startOpening ? OpeningHandler : 0;
                questionStep = startOpening ? 1 : 0;
                step = 0;
                slotRow = slotColumn = scroll = maxScroll = listRow = listColumn = 0;
                grid = startGrid;
                Words[0] = Words[1] = 0xFFFF;
                Presses.Clear();
                pending.Clear();
                Write();
            }

            // One read is one tick of the app: the pending change at the head of the queue comes one read closer.
            public Task<ChainBlockResult[]> ReadChainBlocksAsync(IReadOnlyList<ChainBlockRequest> blocks, int retryFrames = 0, CancellationToken ct = default)
            {
                if (pending.Count > 0 && --readsLeft <= 0)
                {
                    pending.Dequeue().Apply();
                    if (pending.Count > 0) readsLeft = pending.Peek().Reads;
                    Write();
                }
                return memory.ReadChainBlocksAsync(blocks, retryFrames, ct);
            }

            private void Then(int reads, Action apply)
            {
                if (pending.Count == 0) readsLeft = reads;
                pending.Enqueue((reads, apply));
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
                bool takesInput = question == Table.EasyChatQuestionScreen ? questionStep == 1 : question == 0 && step == 0;
                if (!takesInput) Ignored++;
                else if (question == Table.EasyChatQuestionScreen) Question(button);
                else if (screen == Table.EasyChatSlotsScreen) Slots(button);
                else if (screen == Table.EasyChatGroupScreen) Grid(button);
                else if (screen == Table.EasyChatListScreen) List(button);
                Write();
            }

            private void Slots(string button)
            {
                if (button == "Down") slotRow = Math.Min(slotRow + 1, 2);
                else if (button == "Up") slotRow = Math.Max(slotRow - 1, 0);
                else if (button is "Left" or "Right") slotColumn = 1 - slotColumn;
                else if (button == "A" && slotRow == 0)
                {
                    step = 4;
                    Then(TransitionReads, () => { screen = Table.EasyChatGroupScreen; step = 0; });
                }
                else if (button == "A")
                {
                    step = 1;
                    Then(1, () => { step = 3; question = Table.EasyChatQuestionScreen; questionStep = 0; });
                    Then(TransitionReads, () => questionStep = 1);
                }
            }

            // Yes is highlighted; its A answers late, then the closing handler runs and the app closes.
            private void Question(string button)
            {
                if (button != "A") return;
                Then(TransitionReads, () => { questionStep = 3; screen = ClosingScreen; });
                Then(1, () => question = 0);
                Then(1, () => Open = false);
            }

            private void Grid(string button)
            {
                if (button == "Down" && grid + 3 < 12) grid += 3;
                else if (button == "Up" && grid >= 3) grid -= 3;
                else if (button == "Right") grid = grid / 3 * 3 + (grid + 1) % 3;
                else if (button == "Left") grid = grid / 3 * 3 + (grid + 2) % 3;
                else if (button == "A" && GridADrops > 0) GridADrops--;
                else if (button == "A")
                {
                    step = 1;
                    Then(ListOpenReads ?? TransitionReads, () =>
                    {
                        screen = Table.EasyChatListScreen;
                        step = 0;
                        scroll = listRow = listColumn = 0;
                        maxScroll = Math.Max(0, (counts[grid] + 1) / 2 - 5);
                    });
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
                    step = 3;
                    Then(TransitionReads, () => { screen = Table.EasyChatSlotsScreen; step = 0; });
                }
            }

            private void Write()
            {
                memory.Place((uint)(Sub0 + Table.SubAppOffset), BitConverter.GetBytes(Open ? App : 0u));
                memory.Place((uint)(Data + Table.EasyChatWordsOffset), BitConverter.GetBytes(Words[0]));
                memory.Place((uint)(Data + Table.EasyChatWordsOffset + 2), BitConverter.GetBytes(Words[1]));
                memory.Place((uint)(Data + Table.EasyChatBusyOffset), new[] { (byte)step });
                memory.Place((uint)(Data + Table.EasyChatScreenOffset), BitConverter.GetBytes(screen));
                memory.Place((uint)(Data + Table.EasyChatQuestionStepOffset), new[] { (byte)questionStep });
                memory.Place((uint)(Data + Table.EasyChatQuestionOffset), BitConverter.GetBytes(question));
                memory.Place((uint)(Data + Table.EasyChatSlotRowOffset), new[] { (byte)slotRow });
                memory.Place((uint)(Data + Table.EasyChatSlotColumnOffset), new[] { (byte)slotColumn });
                memory.Place((uint)(Data + Table.EasyChatGroupCursorOffset), new[] { (byte)grid });
                memory.Place((uint)(Data + Table.EasyChatListScrollOffset), BitConverter.GetBytes((ushort)scroll));
                memory.Place((uint)(Data + Table.EasyChatListMaxScrollOffset), BitConverter.GetBytes((ushort)maxScroll));
                memory.Place((uint)(Data + Table.EasyChatListColumnOffset), new[] { (byte)listColumn });
                memory.Place((uint)(Data + Table.EasyChatListRowOffset), new[] { (byte)listRow });
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
            public Task<byte> ReadByteAsync(uint address) => throw new NotImplementedException();
            public Task<ushort> ReadUShortAsync(uint address) => throw new NotImplementedException();
            public Task<uint> ReadUIntAsync(uint address) => throw new NotImplementedException();
            public Task<byte[]> ReadBytesAsync(uint address, int length, CancellationToken ct = default) => throw new NotImplementedException();
            public Task WriteByteAsync(uint address, byte value) => throw new NotImplementedException();
            public Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, CancellationToken ct, params int[] hopOffsets) => throw new NotImplementedException();
            public Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => throw new NotImplementedException();
            public Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => throw new NotImplementedException();
            public Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => throw new NotImplementedException();
            public Task<byte[][]> ReadValidatedPartyAsync(uint anchorAddress, int countOffset, int slotsOffset, int slotSize, int slotStride, int maxSlots,
                int maxRetryFrames, PartyAddressing addressing = PartyAddressing.Anchor, PokemonScheme scheme = PokemonScheme.Gen4,
                CancellationToken ct = default) => throw new NotImplementedException();
        }
    }
}
