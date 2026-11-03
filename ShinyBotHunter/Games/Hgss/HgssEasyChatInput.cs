using System.Diagnostics;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Games.Hgss
{
    // Drives the easy-chat app (overlay 102): enters a word into a slot and confirms the prompt, every press gated on
    // the app's data block (docs/research/primo-slugma-egg.md, "Entering the words without looking").
    public class HgssEasyChatInput
    {
        private const int Groups = 12;
        private const int GridColumns = 3;
        private const int ListColumns = 2;
        private const int ListPage = 5; // on-screen rows, and the rows R and L scroll by
        private const int ConfirmRow = 1;
        // How long a press's byte may take before the press counts as dropped. Every press but Yes writes its byte 1 to 4
        // frames after the press (up to 70 ms); the Yes's menu answers 19 to 22 frames late (318 to 385 ms). Measured on
        // hgss.qa-primo (#264); the margin covers the emulator running slower under the screen recorder.
        private const int GateTimeoutMs = 1000;
        // How long the app may take to take input again after a press landed; only throws. Longest observed: the list A,
        // 43 to 45 frames (802 ms) in the #264 probe, and once over 90 frames in the spike.
        private const int TransitionTimeoutMs = 3000;
        private const int OpenTimeoutMs = 10_000;
        private const int MaxPresses = 4; // the first press and at most three more
        private const string Closed = "closed";
        private const string Asked = "the question";

        private readonly IMemoryClient memory;
        private readonly ICommandClient commands;
        private readonly HgssPointerService addresses;

        // The buttons each (slot, word) took when it was built, without retries. Touched only by the event loop that
        // owns this instance; no lock.
        private readonly Dictionary<(int Slot, ushort Word), string[]> cache = new();

        public HgssEasyChatInput(IMemoryClient memory, ICommandClient commands, HgssPointerService addresses)
        {
            this.memory = memory;
            this.commands = commands;
            this.addresses = addresses;
        }

        public async Task EnterAsync(int slot, ushort word, CancellationToken ct)
        {
            Stopwatch clock = Stopwatch.StartNew();
            byte[] block = await WaitForSlotsAsync(ct);
            bool cached = cache.TryGetValue((slot, word), out string[]? buttons);
            if (cached)
            {
                foreach (string button in buttons!) block = (await PressAsync(button, ct))!;
            }
            else
            {
                (buttons, block) = await BuildAsync(slot, word, block, ct);
            }

            ushort held = BitConverter.ToUInt16(block, addresses.EasyChatWordsOffset + 2 * slot);
            if (held != word)
                throw new InvalidOperationException($"[EasyChat] slot {slot} holds word {held}, not word {word}");
            cache[(slot, word)] = buttons;
            Console.WriteLine($"[EasyChat] slot {slot}, word {word}: {string.Join(" ", buttons)} " +
                $"({buttons.Length} presses, {clock.Elapsed.TotalSeconds:F1} s, {(cached ? "cached" : "built")})");
        }

        // Confirm, then Yes at "Is this your answer?" (the default), which closes the app.
        public async Task ConfirmAsync(CancellationToken ct)
        {
            byte[] block = await WaitForSlotsAsync(ct);
            while (block[addresses.EasyChatSlotRowOffset] != ConfirmRow)
                block = (await PressAsync(block[addresses.EasyChatSlotRowOffset] < ConfirmRow ? "Down" : "Up", ct))!;
            Stopwatch clock = Stopwatch.StartNew();
            await PressAsync("A", ct);
            double question = clock.Elapsed.TotalSeconds;
            await PressAsync("A", ct);
            Console.WriteLine($"[EasyChat] confirmed (question open {question:F1} s after the Confirm A, app closed {clock.Elapsed.TotalSeconds:F1} s after it)");
        }

        private async Task<(string[] Buttons, byte[] Block)> BuildAsync(int slot, ushort word, byte[] block, CancellationToken ct)
        {
            List<string> pressed = new();
            async Task Press(string button)
            {
                block = (await PressAsync(button, ct))!;
                pressed.Add(button);
            }

            while (block[addresses.EasyChatSlotRowOffset] != 0) await Press("Up");
            while (block[addresses.EasyChatSlotColumnOffset] != slot)
                await Press(block[addresses.EasyChatSlotColumnOffset] < slot ? "Right" : "Left");
            await Press("A");

            (int group, int index) = await FindAsync(word, block, ct);

            while (block[addresses.EasyChatGroupCursorOffset] / GridColumns != group / GridColumns)
                await Press(block[addresses.EasyChatGroupCursorOffset] / GridColumns < group / GridColumns ? "Down" : "Up");
            while (block[addresses.EasyChatGroupCursorOffset] % GridColumns != group % GridColumns)
            {
                int right = (group % GridColumns - block[addresses.EasyChatGroupCursorOffset] % GridColumns + GridColumns) % GridColumns;
                await Press(right == 1 ? "Right" : "Left");
            }
            await Press("A");

            int targetRow = index / ListColumns;
            int Scroll() => BitConverter.ToUInt16(block, addresses.EasyChatListScrollOffset);
            int Row() => Scroll() + block[addresses.EasyChatListRowOffset];
            int maxScroll = BitConverter.ToUInt16(block, addresses.EasyChatListMaxScrollOffset);
            while (targetRow - Row() >= ListPage && Scroll() + ListPage <= maxScroll) await Press("R");
            while (Row() - targetRow >= ListPage && Scroll() >= ListPage) await Press("L");
            while (Row() != targetRow) await Press(Row() < targetRow ? "Down" : "Up");
            while (block[addresses.EasyChatListColumnOffset] != index % ListColumns)
                await Press(block[addresses.EasyChatListColumnOffset] < index % ListColumns ? "Right" : "Left");
            await Press("A");

            return (pressed.ToArray(), block);
        }

        // The word's grid group and its index in that group's list, from the app's own tables: the first group in
        // grid order whose list holds it.
        private async Task<(int Group, int Index)> FindAsync(ushort word, byte[] block, CancellationToken ct)
        {
            int[] counts = new int[Groups];
            int[] starts = new int[Groups];
            int area = 0;
            for (int g = 0; g < Groups; g++)
            {
                counts[g] = (int)BitConverter.ToUInt32(block, addresses.EasyChatGroupCountsOffset + 4 * g);
                starts[g] = (int)BitConverter.ToUInt32(block, addresses.EasyChatGroupStartsOffset + 4 * g);
                area = Math.Max(area, starts[g] + counts[g]);
            }

            ChainBlockResult[] read = await memory.ReadChainBlocksAsync(new[] { DataBlock(addresses.EasyChatGroupWordsOffset, 2 * area) }, ct: ct);
            if (read[0].Status != ChainBlockStatus.Ok)
                throw new InvalidOperationException($"[EasyChat] the word area read failed: {read[0].Status} at hop {read[0].Hop}");
            for (int g = 0; g < Groups; g++)
                for (int i = 0; i < counts[g]; i++)
                    if (BitConverter.ToUInt16(read[0].Bytes, 2 * (starts[g] + i)) == word) return (g, i);
            throw new InvalidOperationException($"Word {word} is in none of the easy-chat app's unlocked groups");
        }

        // One press, sent only while the app takes input, confirmed by the byte that press writes and pressed again, at most
        // three times, only when that byte has not changed within the drop window. Then the app must take input again (or
        // have closed, after Yes) on the screen the press leads to, within the transition timeout, which never presses.
        // Returns the last read, null once the app has closed.
        private async Task<byte[]?> PressAsync(string button, CancellationToken ct)
        {
            byte[] start = (await SettleAsync(button, false, ct))!;
            (string what, Func<byte[]?, string> value, Func<byte[]?, bool> landed, string end) = GateFor(start, button);

            string seen = value(start);
            for (int press = 1; ; press++)
            {
                commands.RunMacroString($"H.{button}.4");
                await commands.WaitForMacroAsync(ct);
                bool passed = false;
                Stopwatch clock = Stopwatch.StartNew();
                while (!passed && clock.ElapsedMilliseconds < GateTimeoutMs)
                {
                    byte[]? read = await ReadAsync(ct);
                    seen = value(read);
                    passed = landed(read);
                    if (!passed) await Task.Delay(Constant.PollIntervalMs, ct);
                }
                if (passed) break;
                if (press == MaxPresses)
                    throw new InvalidOperationException($"[EasyChat] {button} not confirmed after {MaxPresses} presses: {what} stayed {seen}");
                Console.WriteLine($"[EasyChat] {button} not confirmed ({what} stayed {seen}); pressing again");
            }

            byte[]? block = await SettleAsync(button, end == Closed, ct);
            if (End(block) != end)
                throw new InvalidOperationException($"[EasyChat] {button} ended on {State(block)}, not {end}");
            return block;
        }

        // Whether the app takes input: the question at + 0x2C once its Yes/No reads (+ 0x28 = 1); otherwise no handler
        // at + 0x2C and the screen's step at + 0x1C at 0 (the app ignores presses while either runs).
        private bool Idle(byte[]? b) => b != null && (BitConverter.ToUInt32(b, addresses.EasyChatQuestionOffset) == addresses.EasyChatQuestionScreen
            ? b[addresses.EasyChatQuestionStepOffset] == 1
            : BitConverter.ToUInt32(b, addresses.EasyChatQuestionOffset) == 0 && b[addresses.EasyChatBusyOffset] == 0);

        // Reads until the app takes input, or until it has closed; a stale-timeout names what it saw last.
        private async Task<byte[]?> SettleAsync(string button, bool closes, CancellationToken ct)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (true)
            {
                byte[]? block = await ReadAsync(ct);
                if (closes ? block == null : Idle(block)) return block;
                if (clock.ElapsedMilliseconds >= TransitionTimeoutMs)
                    throw new InvalidOperationException($"[EasyChat] {button}: the app did not {(closes ? "close" : "take input")} within 3 s ({State(block)})");
                await Task.Delay(Constant.PollIntervalMs, ct);
            }
        }

        // What a press of the button on the current screen writes (observed in the #264 probe), and the screen it ends on.
        // A writes the screen's step on the frame it is read: slots 4, grid 1, list 3, the next screen following once the
        // app's graphics tasks end (that screen also proves the step). At Confirm it writes step 1, then the question at
        // + 0x2C 2 to 3 frames later; the question's Yes moves + 0x28 off 1, 19 to 22 frames late, and the app closes.
        // Anything else moves the current screen's cursor.
        private (string What, Func<byte[]?, string> Value, Func<byte[]?, bool> Landed, string End) GateFor(byte[] block, string button)
        {
            if (BitConverter.ToUInt32(block, addresses.EasyChatQuestionOffset) == addresses.EasyChatQuestionScreen)
                return ("question", Question, b => b == null || b[addresses.EasyChatQuestionStepOffset] != 1, Closed);
            uint screen = BitConverter.ToUInt32(block, addresses.EasyChatScreenOffset);
            if (button != "A")
            {
                (string name, Func<byte[]?, string> cursor) = screen == addresses.EasyChatSlotsScreen ? ("slot cursor", (Func<byte[]?, string>)SlotCursor)
                    : screen == addresses.EasyChatGroupScreen ? ("grid cursor", GridCursor) : ("list cursor", ListCursor);
                string before = cursor(block);
                return (name, cursor, b => b != null && cursor(b) != before, Hex(screen));
            }
            if (screen == addresses.EasyChatSlotsScreen && block[addresses.EasyChatSlotRowOffset] == ConfirmRow)
                return ("question", Question, b => b != null && BitConverter.ToUInt32(b, addresses.EasyChatQuestionOffset) == addresses.EasyChatQuestionScreen, Asked);
            (int step, uint next) = screen == addresses.EasyChatSlotsScreen ? (4, addresses.EasyChatGroupScreen)
                : screen == addresses.EasyChatGroupScreen ? (1, addresses.EasyChatListScreen) : (3, addresses.EasyChatSlotsScreen);
            return ("screen and step", Step, b => b != null && (b[addresses.EasyChatBusyOffset] == step
                || BitConverter.ToUInt32(b, addresses.EasyChatScreenOffset) == next), Hex(next));
        }

        // Where a settled read is: closed, the question, or the screen handler at + 0x20.
        private string End(byte[]? b) => b == null ? Closed
            : BitConverter.ToUInt32(b, addresses.EasyChatQuestionOffset) == addresses.EasyChatQuestionScreen ? Asked
            : Hex(BitConverter.ToUInt32(b, addresses.EasyChatScreenOffset));

        private string State(byte[]? b) => b == null ? Closed : $"{Step(b)}, + 0x2C {Question(b)}";

        private string Step(byte[]? b) => b == null ? Closed :
            $"{Hex(BitConverter.ToUInt32(b, addresses.EasyChatScreenOffset))} step {b[addresses.EasyChatBusyOffset]}";

        private string Question(byte[]? b) => b == null ? Closed :
            $"{Hex(BitConverter.ToUInt32(b, addresses.EasyChatQuestionOffset))} step {b[addresses.EasyChatQuestionStepOffset]}";

        private string SlotCursor(byte[]? b) =>
            b == null ? Closed : $"({b[addresses.EasyChatSlotRowOffset]}, {b[addresses.EasyChatSlotColumnOffset]})";

        private string GridCursor(byte[]? b) => b == null ? Closed : $"{b[addresses.EasyChatGroupCursorOffset]}";

        private string ListCursor(byte[]? b) => b == null ? Closed :
            $"(scroll {BitConverter.ToUInt16(b, addresses.EasyChatListScrollOffset)}, row {b[addresses.EasyChatListRowOffset]}, column {b[addresses.EasyChatListColumnOffset]})";

        private static string Hex(uint value) => $"0x{value:X8}";

        // Waits for the slots screen taking input (the app's opening handler runs at + 0x2C first, instead of the
        // screen); a failed read counts as not yet.
        private async Task<byte[]> WaitForSlotsAsync(CancellationToken ct)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (true)
            {
                byte[]? block = await ReadAsync(ct);
                if (block != null && BitConverter.ToUInt32(block, addresses.EasyChatScreenOffset) == addresses.EasyChatSlotsScreen && Idle(block))
                    return block;
                if (clock.ElapsedMilliseconds >= OpenTimeoutMs)
                    throw new InvalidOperationException($"[EasyChat] the slots screen did not appear within 10 s ({State(block)})");
                await Task.Delay(Constant.PollIntervalMs, ct);
            }
        }

        // The data block up to the word area, one frame; null when the chain does not reach it (the app is not open).
        private async Task<byte[]?> ReadAsync(CancellationToken ct)
        {
            ChainBlockResult[] read = await memory.ReadChainBlocksAsync(new[] { DataBlock(0, addresses.EasyChatGroupWordsOffset) }, ct: ct);
            return read[0].Status == ChainBlockStatus.Ok ? read[0].Bytes : null;
        }

        private ChainBlockRequest DataBlock(int offset, int length) =>
            new(addresses.Anchor, new[] { addresses.AppSlotsOffset, addresses.SubAppOffset, addresses.AppDataOffset, offset }, length);
    }
}
