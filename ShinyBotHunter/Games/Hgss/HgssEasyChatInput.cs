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
        private const int GateTimeoutMs = 1500;
        private const int OpenTimeoutMs = 10_000;
        private const int MaxPresses = 4; // the first press and at most three more
        private const string Closed = "closed";

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
            await PressAsync("A", ct);
            await PressAsync("A", ct);
            Console.WriteLine("[EasyChat] confirmed");
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

        // One press, confirmed by the gate the screen and the button call for, pressed again at most three times; then
        // the busy byte must clear. Returns the last read, null once the app has closed.
        private async Task<byte[]?> PressAsync(string button, CancellationToken ct)
        {
            byte[] start = await ReadAsync(ct) ?? throw new InvalidOperationException($"[EasyChat] {button}: the easy-chat app is not open");
            (string what, Func<byte[]?, string> value, string? expect) = GateFor(start, button);
            string before = value(start);

            byte[]? block = null;
            string seen = before;
            for (int press = 1; ; press++)
            {
                commands.RunMacroString($"H.{button}.4");
                await commands.WaitForMacroAsync(ct);
                bool passed = false;
                Stopwatch clock = Stopwatch.StartNew();
                while (!passed && clock.ElapsedMilliseconds < GateTimeoutMs)
                {
                    block = await ReadAsync(ct);
                    seen = value(block);
                    passed = expect != null ? seen == expect : seen != Closed && seen != before;
                    if (!passed) await Task.Delay(Constant.PollIntervalMs, ct);
                }
                if (passed) break;
                if (press == MaxPresses)
                    throw new InvalidOperationException($"[EasyChat] {button} not confirmed after {MaxPresses} presses: {what} stayed {seen}");
                Console.WriteLine($"[EasyChat] {button} not confirmed ({what} stayed {seen}); pressing again");
            }

            if (block == null) return null;
            Stopwatch busy = Stopwatch.StartNew();
            while (block == null || block[addresses.EasyChatBusyOffset] != 0)
            {
                if (busy.ElapsedMilliseconds >= GateTimeoutMs)
                    throw new InvalidOperationException($"[EasyChat] the busy byte did not clear within 1.5 s after {button}");
                await Task.Delay(Constant.PollIntervalMs, ct);
                block = await ReadAsync(ct);
            }
            return block;
        }

        // What a press of the button on the current screen must change: A moves between the three screens (or, at
        // Confirm, leaves the slots screen); anything else moves the current screen's cursor. A on any other screen
        // is Yes at "Is this your answer?", and closes the app.
        private (string What, Func<byte[]?, string> Value, string? Expect) GateFor(byte[] block, string button)
        {
            uint screen = BitConverter.ToUInt32(block, addresses.EasyChatScreenOffset);
            if (screen == addresses.EasyChatSlotsScreen)
            {
                if (button != "A") return ("slot cursor", SlotCursor, null);
                return ("screen", Screen, block[addresses.EasyChatSlotRowOffset] == ConfirmRow ? null : Hex(addresses.EasyChatGroupScreen));
            }
            if (screen == addresses.EasyChatGroupScreen)
                return button == "A" ? ("screen", Screen, Hex(addresses.EasyChatListScreen)) : ("grid cursor", GridCursor, null);
            if (screen == addresses.EasyChatListScreen)
                return button == "A" ? ("screen", Screen, Hex(addresses.EasyChatSlotsScreen)) : ("list cursor", ListCursor, null);
            return ("app", b => b == null ? Closed : "open", Closed);
        }

        private string Screen(byte[]? b) => b == null ? Closed : Hex(BitConverter.ToUInt32(b, addresses.EasyChatScreenOffset));

        private string SlotCursor(byte[]? b) =>
            b == null ? Closed : $"({b[addresses.EasyChatSlotRowOffset]}, {b[addresses.EasyChatSlotColumnOffset]})";

        private string GridCursor(byte[]? b) => b == null ? Closed : $"{b[addresses.EasyChatGroupCursorOffset]}";

        private string ListCursor(byte[]? b) => b == null ? Closed :
            $"(scroll {BitConverter.ToUInt16(b, addresses.EasyChatListScrollOffset)}, row {b[addresses.EasyChatListRowOffset]}, column {b[addresses.EasyChatListColumnOffset]})";

        private static string Hex(uint value) => $"0x{value:X8}";

        // Waits for the slots screen with nothing scrolling; a failed read counts as not yet.
        private async Task<byte[]> WaitForSlotsAsync(CancellationToken ct)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (true)
            {
                byte[]? block = await ReadAsync(ct);
                if (block != null && BitConverter.ToUInt32(block, addresses.EasyChatScreenOffset) == addresses.EasyChatSlotsScreen
                    && block[addresses.EasyChatBusyOffset] == 0)
                    return block;
                if (clock.ElapsedMilliseconds >= OpenTimeoutMs)
                    throw new InvalidOperationException($"[EasyChat] the slots screen did not appear within 10 s (screen {Screen(block)})");
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
