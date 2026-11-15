using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The Spin macros come from the registry, so the working directory has to be the app project before anything
    // touches MacroRegistry.Instance (see MacroTests).
    [Collection("macro-registry")]
    public class SpinEncounterEventTests
    {
        private static readonly HgssPointerService Hgss = new();
        private const uint AnchorTarget = 0x02300000;

        private const uint RsPosition = 0x02025734; // gSaveBlock1.pos
        private const uint RsFacing = 0x030048B8;   // gObjectEvents[0] + 0x18

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);
        private static readonly GameSnapshot Battle = new(GameScreen.BattleTurn, null, null, null, null);

        public SpinEncounterEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        private static GameState Encountering(int startX, int startY)
        {
            GameState state = new() { StartX = startX, StartY = startY };
            state.State.Push(EventState.Encountering);
            return state;
        }

        private static FakeBot HgssBot(int x, int y, Direction facing)
        {
            FakeMemoryClient memory = new();
            memory.Place(Hgss.Anchor, BitConverter.GetBytes(AnchorTarget));
            memory.Place((uint)(AnchorTarget + Hgss.FacingOffset), [(byte)facing]);
            memory.Place(Hgss.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Hgss.TrainerY, BitConverter.GetBytes((uint)y << 16));
            return new FakeBot(Task.CompletedTask) { State = new ScriptedReader(Free, Battle), Overworld = new HgssOverworldReader(memory, Hgss) };
        }

        private static FakeBot RsBot(short x, short y, byte facing)
        {
            FakeMemoryClient memory = new();
            memory.Place(RsPosition, [.. BitConverter.GetBytes(x), .. BitConverter.GetBytes(y)]);
            memory.Place(RsFacing, [facing]);
            return new FakeBot(Task.CompletedTask, RsProfile.Create(new PokemonMetadataService(null!)))
            {
                State = new ScriptedReader(Free, Battle),
                Overworld = new RsOverworldReader(memory, new RsPointerService()),
            };
        }

        // Literals copied from origin/main's GetSpinMacro output.
        [Theory]
        [InlineData(Direction.Up, "T.Left;W.5;T.Down;W.5;T.Right;W.5;T.Up;W.5")]
        [InlineData(Direction.Left, "T.Down;W.5;T.Right;W.5;T.Up;W.5;T.Left;W.5")]
        [InlineData(Direction.Down, "T.Right;W.5;T.Up;W.5;T.Left;W.5;T.Down;W.5")]
        [InlineData(Direction.Right, "T.Up;W.5;T.Left;W.5;T.Down;W.5;T.Right;W.5")]
        public async Task HgssSpinMacroIsTodaysText(Direction facing, string expected)
        {
            FakeBot bot = HgssBot(10, 20, facing);
            GameState state = Encountering(10, 20);

            bool found = await new SpinEncounterEvent(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.True(found);
            Assert.Equal(["RP:" + expected, "clear"], bot.Macros);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        // docs/research/rs-wild-encounter.md, "A full circle": the facing nibble is 2 up, 3 left, 1 down, 4 right.
        [Theory]
        [InlineData((byte)0x22, "M.Left.51.50350262;M.Down.17.50350262;M.Right.68.50350262;M.Up.34.50350262")]
        [InlineData((byte)0x33, "M.Down.17.50350262;M.Right.68.50350262;M.Up.34.50350262;M.Left.51.50350262")]
        [InlineData((byte)0x11, "M.Right.68.50350262;M.Up.34.50350262;M.Left.51.50350262;M.Down.17.50350262")]
        [InlineData((byte)0x44, "M.Up.34.50350262;M.Left.51.50350262;M.Down.17.50350262;M.Right.68.50350262")]
        public async Task RsSpinMacroIsTheResearchDocText(byte facing, string expected)
        {
            FakeBot bot = RsBot(14, 4, facing);
            GameState state = Encountering(14, 4);

            bool found = await new SpinEncounterEvent(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.True(found);
            Assert.Equal(["RP:" + expected, "clear"], bot.Macros);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        [Fact]
        public async Task RsPlayerOffTheStartTileStopsWithoutTurning()
        {
            FakeBot bot = RsBot(14, 5, 0x22);
            GameState state = Encountering(14, 4);

            bool found = await new SpinEncounterEvent(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.False(found);
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Empty(bot.Macros);
            Assert.Equal("The player moved while turning.", SpinEncounterEvent.MovedWhileTurning);
        }

        private const string HgssUpSpin = "T.Left;W.5;T.Down;W.5;T.Right;W.5;T.Up;W.5";
        private const string RsUpSpin = "M.Left.51.50350262;M.Down.17.50350262;M.Right.68.50350262;M.Up.34.50350262";

        private static GameSnapshot At(GameScreen screen) => new(screen, null, null, null, null);

        // FR-9: a field lock with no battle stops the bot, on both games and for every non-free, non-battle screen.
        [Theory]
        [InlineData(true, GameScreen.FieldTask)]
        [InlineData(false, GameScreen.FieldTask)]
        [InlineData(false, GameScreen.Transitioning)]
        [InlineData(false, GameScreen.OtherApp)]
        public async Task LockWithNoBattleStops(bool rs, GameScreen screen)
        {
            FakeBot bot = rs ? RsBot(14, 4, 0x22) : HgssBot(14, 4, Direction.Up);
            bot.State = new ScriptedReader(Free, At(screen));
            GameState state = Encountering(14, 4);

            bool found = await new SpinEncounterEvent(bot) { NoBattleWithin = TimeSpan.FromMilliseconds(100) }.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.False(found);
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Equal(["RP:" + (rs ? RsUpSpin : HgssUpSpin), "clear"], bot.Macros);
            Assert.Equal("Something other than a wild battle started.", SpinEncounterEvent.NotAWildBattle);
        }

        // docs/research/pointer-discovery.md: the encounter task 020551B9, then the load, then the battle app.
        [Fact]
        public async Task HgssEncounterHandsOverThroughTheFieldTaskAndLoad()
        {
            FakeBot bot = HgssBot(10, 20, Direction.Up);
            bot.State = new ScriptedReader(Free, Free, new GameSnapshot(GameScreen.FieldTask, null, null, null, 0x020551B9),
                At(GameScreen.Transitioning), At(GameScreen.BattleStarting));
            GameState state = Encountering(10, 20);

            bool found = await new SpinEncounterEvent(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.True(found);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
            Assert.Equal(["RP:" + HgssUpSpin, "clear"], bot.Macros);
        }

        [Fact]
        public async Task RsEncounterHandsOverAfterTheLock()
        {
            FakeBot bot = RsBot(14, 4, 0x22);
            bot.State = new ScriptedReader(Free, Free, At(GameScreen.FieldTask), Battle);
            GameState state = Encountering(14, 4);

            bool found = await new SpinEncounterEvent(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.True(found);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
            Assert.Equal(["RP:" + RsUpSpin, "clear"], bot.Macros);
        }

        [Fact]
        public async Task SpinLogsTheSeekInTwoParts()
        {
            FakeBot bot = RsBot(14, 4, 0x22);
            bot.State = new ScriptedReader(Free, Free, At(GameScreen.FieldTask), Battle);
            GameState state = Encountering(14, 4);
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await new SpinEncounterEvent(bot).ExecuteWithResultAsync(state, CancellationToken.None);
            }
            finally
            {
                Console.SetOut(console);
            }

            string text = log.ToString();
            Assert.Matches(@"\[Spin\] seek: (\d+) ms from the macro to the field leaving free, (\d+) ms to the battle screen", text);
            Assert.Contains("[Spin] facing=", text);
            Assert.Contains("[Spin] Found battle after ", text);
        }

        // Three locks add up to more than the bound, but none reaches it alone: each FieldFree resets the clock, and the
        // macro is neither cleared nor sent again.
        [Fact]
        public async Task LockThatClearsGoesOnSeeking()
        {
            FakeBot bot = RsBot(14, 4, 0x22);
            GameSnapshot[] lockThenFree = [.. Enumerable.Repeat(At(GameScreen.FieldTask), 6), Free];
            bot.State = new ScriptedReader([Free, Free, .. lockThenFree, .. lockThenFree, .. lockThenFree, Battle]);
            GameState state = Encountering(14, 4);

            bool found = await new SpinEncounterEvent(bot) { NoBattleWithin = TimeSpan.FromMilliseconds(500) }.ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.True(found);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
            Assert.Equal(["RP:" + RsUpSpin, "clear"], bot.Macros);
        }

        [Fact]
        public async Task FreeFieldIsUnbounded()
        {
            FakeBot bot = RsBot(14, 4, 0x22);
            bot.State = new ScriptedReader(Free);
            GameState state = Encountering(14, 4);
            using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(300));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new SpinEncounterEvent(bot) { NoBattleWithin = TimeSpan.FromMilliseconds(50) }.ExecuteWithResultAsync(state, cts.Token));

            Assert.Equal(EventState.Encountering, state.State.Peek());
            Assert.Equal(["RP:" + RsUpSpin, "clear"], bot.Macros);
        }

        // A change for HGSS: it used to spin on whatever tile the player stood on.
        [Fact]
        public async Task HgssPlayerOffTheStartTileStops()
        {
            FakeBot bot = HgssBot(11, 20, Direction.Up);
            GameState state = Encountering(10, 20);

            bool found = await new SpinEncounterEvent(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.False(found);
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Empty(bot.Macros);
        }
    }
}
