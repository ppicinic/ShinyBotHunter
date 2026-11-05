using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The hatch step over a scripted game; values from docs/research/primo-slugma-egg.md, "The hatch".
    public class HgssEggHatchTests
    {
        private static readonly HgssPointerService Table = new();
        private static readonly TimeSpan Stale = TimeSpan.FromMilliseconds(200);

        private const uint RunScripts = 0x0203FF45;
        private const uint Printing = 0x021EF349;
        private const uint EggPid = 0xDEADBEEF;
        private const string PaceMacro = "M.Left.488.1;M.Right.496.1";
        private const string A = "H.A.4";
        private const string Down = "W.5;H.Down.4";
        private const string AOnNo = "W.5;H.A.4";

        private static readonly ChainBlockResult NullPointer = new(ChainBlockStatus.NullPointer, 1, []);
        private static readonly ChainBlockResult OutOfRange = new(ChainBlockStatus.OutOfRange, 3, []);
        private static ChainBlockResult Ok(params byte[] bytes) => new(ChainBlockStatus.Ok, 3, bytes);

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);
        private static readonly GameSnapshot Fading = new(GameScreen.Transitioning, null, null, null, null);
        private static readonly GameSnapshot InApp = new(GameScreen.OtherApp, null, null, null, null);
        private static GameSnapshot Hatch(uint? native, int? printer = null) =>
            new(GameScreen.FieldTask, null, Table.HatchEggScript, null, RunScripts, native, TextPrinter: printer);

        // One read of the game: the snapshot, the overlay and cursor chains, and the day care step counter.
        private sealed record Frame(GameSnapshot Snapshot, ChainBlockResult Overlay, ChainBlockResult Cursor, byte Counter = 0);

        private static Frame Field(GameSnapshot snapshot, byte counter = 0) => new(snapshot, NullPointer, NullPointer, counter);
        private static Frame App(ChainBlockResult cursor) => new(InApp, Ok(BitConverter.GetBytes(Table.HatchOverlayId)), cursor);

        // Stages the game waits in for a press: each state read takes the stage's next frame (the last repeats), and a
        // press that is not dropped moves to the next stage.
        private sealed class FakeHatchGame(params Frame[][] stages) : IGameStateReader
        {
            private int stage, frame = -1;

            public int DroppedPresses { get; set; }
            public List<(string Macro, byte? Cursor)> Presses { get; } = new();
            public Frame Current => stages[stage][Math.Max(frame, 0)];

            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                if (frame < stages[stage].Length - 1) frame++;
                return Task.FromResult(Current.Snapshot);
            }

            public ChainBlockResult[] Chain(IReadOnlyList<ChainBlockRequest> blocks) =>
                blocks.Count == 1 ? [Ok(Current.Counter)] : [Current.Overlay, Current.Cursor];

            public void Press(string macro)
            {
                Presses.Add((macro, Current.Cursor.Status == ChainBlockStatus.Ok ? Current.Cursor.Bytes[0] : null));
                if (DroppedPresses > 0)
                {
                    DroppedPresses--;
                    return;
                }
                if (stage < stages.Length - 1)
                {
                    stage++;
                    frame = -1;
                }
            }
        }

        // The party parser reads the PID and IV word from the block's first eight bytes.
        private sealed class BlockParser(Action onParse) : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw)
            {
                onParse();
                return new PokemonData { PID = BitConverter.ToUInt32(raw, 0), IVs = BitConverter.ToUInt32(raw, 4), Species = 175, Name = "Togepi" };
            }
        }

        private static byte[] Block(uint pid, bool egg) => [.. BitConverter.GetBytes(pid), .. BitConverter.GetBytes(egg ? 1u << 30 : 0u)];

        private static (HgssEggHatch Hatch, FakeBot Bot) Build(FakeHatchGame game, byte[][]? party = null, Action? onParse = null)
        {
            GameProfile profile = new(
                GameFlavor.HGSS,
                new GameCapabilities(false, false, false, true, false, false, false),
                [0],
                _ => Table,
                new BlockParser(onParse ?? (() => { })),
                battleParser: null);
            FakeBot bot = new(Task.CompletedTask, profile)
            {
                State = game,
                ChainBlocks = game.Chain,
                Pressed = game.Press,
                PartyBlocks = party ?? [Block(0x12345678, false), Block(EggPid, false)],
            };
            return (new HgssEggHatch(bot, Table, new PokemonStateReader(), Stale), bot);
        }

        private static Frame[] WaitingAtOh() => [Field(Hatch(Printing, 0)), Field(Hatch(Printing, 2)), Field(Hatch(Table.WaitABPressNative))];

        private static Frame[] AppUntilCursor(byte cursor) =>
            [Field(Fading), Field(Fading), App(NullPointer), App(NullPointer), App(NullPointer), App(Ok(cursor))];

        private static Frame[] Closing() => [App(NullPointer), Field(Fading), Field(Fading), Field(Hatch(null)), Field(Free)];

        private static FakeHatchGame FullHatch() => new(WaitingAtOh(), AppUntilCursor(1), [App(Ok(2))], Closing());

        [Fact]
        public async Task PacingRunsTheMacroUntilScript2021ThenClearsIt()
        {
            Frame[] walking = Enumerable.Range(0, 20).Select(i => Field(Free, (byte)(240 + i % 15))).ToArray();
            FakeHatchGame game = new([.. walking, Field(Hatch(Printing, 0))]);
            (HgssEggHatch hatch, FakeBot bot) = Build(game);

            await hatch.PaceUntilHatchAsync(PaceMacro, CancellationToken.None);

            Assert.Equal(["RP:" + PaceMacro, "clear"], bot.Macros);
        }

        [Fact]
        public async Task PacingTimesOutWhenTheCounterStopsChanging()
        {
            FakeHatchGame game = new([Field(Free, 17)]);
            (HgssEggHatch hatch, FakeBot bot) = Build(game);

            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(() => hatch.PaceUntilHatchAsync(PaceMacro, CancellationToken.None));

            Assert.Contains("script 2021", ex.Message);
            Assert.Contains("stayed 17", ex.Message);
            Assert.Equal("clear", bot.Macros[^1]);
        }

        [Fact]
        public async Task HatchPressesAAtWaitABPressDownToNoThenA()
        {
            FakeHatchGame game = FullHatch();
            (HgssEggHatch hatch, _) = Build(game);

            PokemonData hatched = await hatch.HatchAsync(EggPid, CancellationToken.None);

            Assert.Equal([A, Down, AOnNo], game.Presses.Select(p => p.Macro));
            Assert.Equal(EggPid, hatched.PID);
            Assert.False(hatched.IsEgg);
        }

        [Fact]
        public async Task DownWaitsForAValidMenuPointerAndCursor()
        {
            Frame[] opening = [App(NullPointer), App(OutOfRange), App(Ok(0)), App(Ok(0)), App(Ok(1))];
            FakeHatchGame game = new(WaitingAtOh(), opening, [App(Ok(2))], Closing());
            (HgssEggHatch hatch, _) = Build(game);

            await hatch.HatchAsync(EggPid, CancellationToken.None);

            Assert.Equal((Down, (byte?)1), game.Presses.Single(p => p.Macro == Down));
        }

        [Fact]
        public async Task CursorAlreadyOnNoGetsNoDown()
        {
            FakeHatchGame game = new(WaitingAtOh(), AppUntilCursor(2), Closing());
            (HgssEggHatch hatch, _) = Build(game);

            await hatch.HatchAsync(EggPid, CancellationToken.None);

            Assert.Equal([A, AOnNo], game.Presses.Select(p => p.Macro));
        }

        [Fact]
        public async Task PartyIsReadOnlyAfterTheFieldIsFree()
        {
            FakeHatchGame game = FullHatch();
            int reads = 0;
            (HgssEggHatch hatch, _) = Build(game, [Block(EggPid, false)], () =>
            {
                Assert.Equal(GameScreen.FieldFree, game.Current.Snapshot.Screen);
                reads++;
            });

            await hatch.HatchAsync(EggPid, CancellationToken.None);

            Assert.Equal(1, reads);
        }

        [Fact]
        public async Task DroppedAAtWaitABPressIsPressedAgain()
        {
            FakeHatchGame game = FullHatch();
            game.DroppedPresses = 1;
            (HgssEggHatch hatch, _) = Build(game);

            PokemonData hatched = await hatch.HatchAsync(EggPid, CancellationToken.None);

            Assert.Equal([A, A, Down, AOnNo], game.Presses.Select(p => p.Macro));
            Assert.Equal(EggPid, hatched.PID);
        }

        [Fact]
        public async Task AppThatNeverClosesTimesOut()
        {
            FakeHatchGame game = new(WaitingAtOh(), AppUntilCursor(1), [App(Ok(2))], [App(NullPointer)]);
            (HgssEggHatch hatch, _) = Build(game);

            var ex = await Assert.ThrowsAsync<GameStateTimeoutException>(() => hatch.HatchAsync(EggPid, CancellationToken.None));

            Assert.Contains("to close", ex.Message);
            Assert.Contains($"overlay {Table.HatchOverlayId}, cursor none", ex.Message);
        }

        [Fact]
        public async Task StillAnEggAfterTheScriptStops()
        {
            (HgssEggHatch hatch, _) = Build(FullHatch(), [Block(EggPid, true)]);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hatch.HatchAsync(EggPid, CancellationToken.None));

            Assert.Contains("DEADBEEF", ex.Message);
            Assert.Contains("still an egg", ex.Message);
        }

        [Fact]
        public async Task NoSlotWithThePidStops()
        {
            (HgssEggHatch hatch, _) = Build(FullHatch(), [Block(0x12345678, false)]);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hatch.HatchAsync(EggPid, CancellationToken.None));

            Assert.Contains("DEADBEEF", ex.Message);
        }
    }
}
