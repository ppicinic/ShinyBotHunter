using System.Diagnostics;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    // The HGSS egg hatch the egg hunts share (docs/research/primo-slugma-egg.md, "Counting to the hatch", "The hatch"):
    // pace until the game starts the hatch script, then answer it from memory. Knows no egg species. Every wait goes
    // through the state reader with a stale-timeout, and every press is confirmed by the next read.
    public class HgssEggHatch
    {
        private static readonly TimeSpan CounterLogInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PressConfirm = TimeSpan.FromSeconds(1);
        private const int PressAttempts = 3;
        private const string A = "H.A.4";

        private readonly IBot bot;
        private readonly HgssPointerService addresses;
        private readonly PokemonStateReader party;
        private readonly TimeSpan stale;

        public HgssEggHatch(IBot bot, HgssPointerService addresses, PokemonStateReader party, TimeSpan stale)
        {
            this.bot = bot;
            this.addresses = addresses;
            this.party = party;
            this.stale = stale;
        }

        // Runs the pacing macro until field script 2021 starts, logging the day care step counter every 30 s. Throws
        // when the counter has not changed for stale (something is in the way). Clears the macro either way.
        public async Task PaceUntilHatchAsync(string pacingMacro, CancellationToken ct)
        {
            bot.RunRepeatMacro(pacingMacro);
            try
            {
                byte counter = await ReadCounterAsync(ct);
                Stopwatch unchanged = Stopwatch.StartNew();
                Stopwatch sinceLog = Stopwatch.StartNew();
                while (true)
                {
                    GameSnapshot snapshot = await bot.State.ReadAsync(ct);
                    if (snapshot.Script == addresses.HatchEggScript) return;

                    byte next = await ReadCounterAsync(ct);
                    if (next != counter)
                    {
                        counter = next;
                        unchanged.Restart();
                    }
                    else if (unchanged.Elapsed >= stale)
                    {
                        throw new GameStateTimeoutException(
                            $"script {addresses.HatchEggScript} while pacing; the day care step counter stayed {counter} for {stale.TotalSeconds:0.###} s",
                            snapshot);
                    }
                    if (sinceLog.Elapsed >= CounterLogInterval)
                    {
                        Console.WriteLine($"[EggHatch] day care step counter {counter}");
                        sinceLog.Restart();
                    }
                    await Task.Delay(Constant.PollIntervalMs, ct);
                }
            }
            finally
            {
                bot.ClearMacro();
                await bot.WaitForMacroAsync();
            }
        }

        // From "Oh?" in script 2021 to the field free again: A at WaitABPress, No at the nickname question. Returns the
        // party slot with the egg's PID, which must no longer be an egg.
        public async Task<PokemonData> HatchAsync(uint eggPid, CancellationToken ct)
        {
            uint hatchApp = addresses.HatchOverlayId;
            await WaitAsync("\"Oh?\" (script 2021 at WaitABPress)",
                v => v.Snapshot.Script == addresses.HatchEggScript && v.Snapshot.Native == addresses.WaitABPressNative, ct);

            await PressAsync(A, "the A at \"Oh?\" to be taken", v => v.Snapshot.Native != addresses.WaitABPressNative, ct);
            await WaitAsync($"the hatch app (overlay {hatchApp}) to open", v => v.Overlay == hatchApp, ct);

            var view = await WaitAsync("the nickname question's Yes/No", v => v.Overlay == hatchApp && v.Cursor is 1 or 2, ct);
            if (view.Cursor == 1)
            {
                await PressAsync(GameStateWait.MenuWait + "H.Down.4", "the cursor on No", v => v.Cursor == 2, ct);
            }

            await PressAsync(GameStateWait.MenuWait + A, "the A on No to be taken", v => v.Cursor != 2 || v.Overlay != hatchApp, ct);
            await WaitAsync($"the hatch app (overlay {hatchApp}) to close", v => v.Overlay != hatchApp, ct);
            await WaitAsync("the field to be free after the hatch", v => v.Snapshot.Screen == GameScreen.FieldFree, ct);

            PokemonState state = await party.GetPartyState(bot);
            for (int i = 0; i < state.PartySize; i++)
            {
                PokemonData mon = state.PartyPokemon[i]!;
                if (mon.PID != eggPid) continue;
                if (mon.IsEgg)
                    throw new InvalidOperationException($"Party slot {i + 1} (pid {eggPid:X8}) is still an egg after the hatch script ended");
                Console.WriteLine($"[EggHatch] slot {i + 1}: {mon.Name} (species {mon.Species}), pid {mon.PID:X8}");
                return mon;
            }
            throw new InvalidOperationException($"No party slot holds pid {eggPid:X8} after the hatch script ended");
        }

        // The day care step counter, followed from FieldSystem through saveData each read (saveData moves between boots).
        private async Task<byte> ReadCounterAsync(CancellationToken ct)
        {
            ChainBlockResult read = (await bot.ReadChainBlocksAsync(
                new[] { new ChainBlockRequest(addresses.Anchor, new[] { addresses.SaveDataOffset, addresses.EggStepCounterOffset }, 1) }, ct: ct))[0];
            if (read.Status != ChainBlockStatus.Ok)
                throw new InvalidOperationException($"[EggHatch] the day care step counter read failed: {read.Status} at hop {read.Hop}");
            return read.Bytes[0];
        }

        // The state, the sub-app's overlay id and the hatch app's Yes/No cursor, each null when its chain does not read Ok.
        private async Task<(GameSnapshot Snapshot, uint? Overlay, byte? Cursor)> ReadViewAsync(CancellationToken ct)
        {
            GameSnapshot snapshot = await bot.State.ReadAsync(ct);
            ChainBlockResult[] read = await bot.ReadChainBlocksAsync(new ChainBlockRequest[]
            {
                new(addresses.Anchor, new[] { addresses.AppSlotsOffset, addresses.SubAppOffset, addresses.AppOverlayIdOffset }, 4),
                new(addresses.Anchor, new[] { addresses.AppSlotsOffset, addresses.SubAppOffset, addresses.AppDataOffset,
                    addresses.HatchYesNoOffset, addresses.HatchYesNoCursorOffset }, 1),
            }, ct: ct);
            uint? overlay = read[0].Status == ChainBlockStatus.Ok ? BitConverter.ToUInt32(read[0].Bytes, 0) : null;
            byte? cursor = read[1].Status == ChainBlockStatus.Ok ? read[1].Bytes[0] : null;
            return (snapshot, overlay, cursor);
        }

        // Polls the view until it matches; throws when the whole view has not changed for stale.
        private async Task<(GameSnapshot Snapshot, uint? Overlay, byte? Cursor)> WaitAsync(
            string waitingFor, Func<(GameSnapshot Snapshot, uint? Overlay, byte? Cursor), bool> until, CancellationToken ct)
        {
            var last = await ReadViewAsync(ct);
            Stopwatch unchanged = Stopwatch.StartNew();
            while (!until(last))
            {
                if (unchanged.Elapsed >= stale)
                    throw new GameStateTimeoutException($"{waitingFor} ({Describe(last)})", last.Snapshot);
                await Task.Delay(Constant.PollIntervalMs, ct);
                var next = await ReadViewAsync(ct);
                if (next != last) unchanged.Restart();
                last = next;
            }
            return last;
        }

        // Presses, then polls up to 1 s for the press to show; presses again at most twice more.
        private async Task PressAsync(
            string macro, string waitingFor, Func<(GameSnapshot Snapshot, uint? Overlay, byte? Cursor), bool> taken, CancellationToken ct)
        {
            (GameSnapshot Snapshot, uint? Overlay, byte? Cursor) last = default;
            for (int attempt = 1; attempt <= PressAttempts; attempt++)
            {
                bot.RunMacroString(macro);
                await bot.WaitForMacroAsync(ct);
                Stopwatch sincePress = Stopwatch.StartNew();
                while (true)
                {
                    last = await ReadViewAsync(ct);
                    if (taken(last))
                    {
                        Console.WriteLine($"[EggHatch] {macro}: {Describe(last)}, {last.Snapshot}");
                        return;
                    }
                    if (sincePress.Elapsed >= PressConfirm) break;
                    await Task.Delay(Constant.PollIntervalMs, ct);
                }
            }
            throw new GameStateTimeoutException($"{waitingFor} after {PressAttempts} presses of {macro} ({Describe(last)})", last.Snapshot);
        }

        private static string Describe((GameSnapshot Snapshot, uint? Overlay, byte? Cursor) view) =>
            $"overlay {view.Overlay?.ToString() ?? "none"}, cursor {view.Cursor?.ToString() ?? "none"}";
    }
}
