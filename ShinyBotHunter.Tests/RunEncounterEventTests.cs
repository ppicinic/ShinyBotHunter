using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // RunMacro asks the registry for RunHold, so the working directory has to be the app project before anything
    // touches MacroRegistry.Instance (see MacroTests). The RS strings are tested on the static seams because FakeBot's
    // Addresses(0) has no RS revision 0.
    [Collection("macro-registry")]
    public class RunEncounterEventTests
    {
        private static readonly HgssPointerService Hgss = new();

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);
        private static readonly GameSnapshot Battle = new(GameScreen.BattleTurn, null, null, null, null);

        public RunEncounterEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        // Each read asks the function; for a state that answers a press.
        private sealed class FuncReader(Func<GameSnapshot> read) : IGameStateReader
        {
            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(read());
        }

        private static FakeBot HgssBot(int x, int y, Action<string>? pressed = null)
        {
            FakeMemoryClient memory = new();
            memory.Place(Hgss.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Hgss.TrainerY, BitConverter.GetBytes((uint)y << 16));
            return new FakeBot(Task.CompletedTask)
            {
                State = new ScriptedReader(Free, Battle),
                Overworld = new HgssOverworldReader(memory, Hgss),
                Pressed = pressed ?? (_ => { }),
            };
        }

        private static GameState Encountering(int startX, int startY)
        {
            GameState state = new() { StartX = startX, StartY = startY };
            state.State.Push(EventState.Encountering);
            return state;
        }

        // Literals copied from origin/main's template: TrainerY 0x21DA6FC = 35497724, TrainerX 0x21DA6F4 = 35497716.
        [Theory]
        [InlineData(2, 0, "M.Up.18.35497724;M.Down.22.35497724")]
        [InlineData(0, 2, "M.Right.12.35497716;M.Left.8.35497716")]
        public void HgssRunMacroIsTodaysText(int yRange, int xRange, string expected)
        {
            string macro = RunEncounterEvent.RunMacro(new HgssPointerService(), GameFlavor.HGSS, new EncounterParams(EncounterType.Run, yRange, xRange), 10, 20);

            Assert.Equal(expected, macro);
        }

        // y is 0x02025734 = 33707828, x two bytes earlier = 33707826.
        [Theory]
        [InlineData(1, 0, "M.Up.3.33707828:H.B.1;M.Down.5.33707828")]
        [InlineData(0, 1, "M.Right.15.33707826:H.B.1;M.Left.13.33707826")]
        public void RsRunMacroHoldsBInTheFirstMove(int yRange, int xRange, string expected)
        {
            string macro = RunEncounterEvent.RunMacro(new RsPointerService(), GameFlavor.RS, new EncounterParams(EncounterType.Run, yRange, xRange), 14, 4);

            Assert.Equal(expected, macro);
        }

        // docs/research/rs-wild-encounter.md, "The Run macro's first frame": a leading H.B.1 step overshoots the line.
        [Theory]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        public void RunHoldIsNeverALeadingStep(int yRange, int xRange)
        {
            string[] steps = RunEncounterEvent.RunMacro(new RsPointerService(), GameFlavor.RS, new EncounterParams(EncounterType.Run, yRange, xRange), 14, 4).Split(';');

            Assert.Equal(2, steps.Length);
            Assert.All(steps, step => Assert.StartsWith("M.", step));
            Assert.DoesNotContain("H.B.1", steps);
        }

        [Theory]
        [InlineData(1, 0, 14, 3, true)]
        [InlineData(1, 0, 14, 4, true)]
        [InlineData(1, 0, 14, 5, true)]
        [InlineData(1, 0, 14, 2, false)]
        [InlineData(1, 0, 14, 6, false)]
        [InlineData(1, 0, 13, 4, false)]
        [InlineData(0, 1, 13, 4, true)]
        [InlineData(0, 1, 15, 4, true)]
        [InlineData(0, 1, 16, 4, false)]
        [InlineData(0, 1, 14, 5, false)]
        public void OffTheLine(int yRange, int xRange, short x, short y, bool onLine)
        {
            string? stop = RunEncounterEvent.OffTheLine(new EncounterParams(EncounterType.Run, yRange, xRange), 14, 4, (x, y));

            Assert.Equal(onLine ? null : "The player is off the run line.", stop);
        }

        [Fact]
        public async Task HgssRunStartsTheLineAndWaitsForABattle()
        {
            FakeBot bot = HgssBot(10, 20);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(["RP:M.Up.18.35497724;M.Down.22.35497724", "clear"], bot.Macros);
            Assert.Equal("M.Up.18.35497724;M.Down.22.35497724", state.EncounterMacros["Run"]);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        [Fact]
        public async Task RunLogsTheStartTileMacroAndSeek()
        {
            FakeBot bot = HgssBot(10, 20);
            GameState state = Encountering(10, 20);
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)).ExecuteWithResultAsync(state, CancellationToken.None);
            }
            finally
            {
                Console.SetOut(console);
            }

            string text = log.ToString();
            Assert.Contains("[Run] from (10, 20) macro=M.Up.18.35497724;M.Down.22.35497724", text);
            Assert.Matches(@"\[Run\] seek: (\d+) ms from the macro to the field leaving free, (\d+) ms to the battle screen", text);
        }

        private static readonly GameSnapshot Lock = new(GameScreen.FieldTask, null, null, null, null);

        // FR-9: a field lock with no battle stops the bot.
        [Fact]
        public async Task RunLockWithNoBattleStops()
        {
            FakeBot bot = HgssBot(10, 20);
            bot.State = new ScriptedReader(Free, Lock);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)) { NoBattleWithin = TimeSpan.FromMilliseconds(100) }
                .ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Equal(["RP:M.Up.18.35497724;M.Down.22.35497724", "clear"], bot.Macros);
        }

        // Three locks add up to more than the bound, but none reaches it alone; the macro is neither cleared nor sent again.
        [Fact]
        public async Task RunLockThatClearsGoesOnSeeking()
        {
            FakeBot bot = HgssBot(10, 20);
            GameSnapshot[] lockThenFree = [.. Enumerable.Repeat(Lock, 6), Free];
            bot.State = new ScriptedReader([Free, Free, .. lockThenFree, .. lockThenFree, .. lockThenFree, Battle]);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)) { NoBattleWithin = TimeSpan.FromMilliseconds(500) }
                .ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.BattleStart, state.State.Peek());
            Assert.Equal(["RP:M.Up.18.35497724;M.Down.22.35497724", "clear"], bot.Macros);
        }

        // docs/research/milestone-builder.md R5: std_repel_wore_off (2022) prints, then waits in WaitABPressNative with no
        // printer; one A takes ctx.mode 2 to 1, the native leaves the snapshot, and the field is free.
        private static readonly GameSnapshot RepelPrinting = new(GameScreen.FieldTask, null, 2022, null, 0x0203FF45, 0x021EF349, TextPrinter: 0);
        private static readonly GameSnapshot RepelWaiting = new(GameScreen.FieldTask, null, 2022, null, 0x0203FF45, 0x02041001);
        private static readonly GameSnapshot RepelTaken = new(GameScreen.Transitioning, null, null, null, null);

        // The message until takenAfter presses, then what one A gives (the freed environment, the free field), then a battle.
        private static (FakeBot Bot, List<string> Presses) RepelBot(int takenAfter)
        {
            List<string> presses = [];
            FakeBot bot = HgssBot(10, 20, presses.Add);
            int reads = 0;
            int afterTaken = 0;
            bot.State = new FuncReader(() =>
            {
                reads++;
                if (presses.Count < takenAfter) return reads == 1 ? Free : reads == 2 ? RepelPrinting : RepelWaiting;
                afterTaken++;
                return afterTaken switch { 1 => RepelTaken, 2 => Free, _ => Battle };
            });
            return (bot, presses);
        }

        // FR-29, rule 16: one A at the wait, confirmed by the native leaving it, then the Run macro again from the line.
        [Fact]
        public async Task Execute_RepelMessage_ClosedWithOneAAndTheRunResumes()
        {
            (FakeBot bot, List<string> presses) = RepelBot(takenAfter: 1);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(["T.A;W.20"], presses);
            Assert.Equal(["RP:M.Up.18.35497724;M.Down.22.35497724", "clear", "RP:M.Up.18.35497724;M.Down.22.35497724", "clear"], bot.Macros);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        // A press the snapshot shows still waiting was dropped: sent again, once.
        [Fact]
        public async Task Execute_RepelMessage_DroppedPressSentAgain()
        {
            (FakeBot bot, List<string> presses) = RepelBot(takenAfter: 2);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)) { RepelPressConfirm = TimeSpan.FromMilliseconds(100) }
                .ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(["T.A;W.20", "T.A;W.20"], presses);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        [Fact]
        public async Task Execute_RepelMessage_PressNeverTaken_ParksNamingTheSnapshot()
        {
            (FakeBot bot, List<string> presses) = RepelBot(takenAfter: int.MaxValue);
            GameState state = Encountering(10, 20);
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)) { RepelPressConfirm = TimeSpan.FromMilliseconds(50) }
                    .ExecuteWithResultAsync(state, CancellationToken.None);
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.Equal(3, presses.Count);
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Contains($"[Run] The Repel message did not close. Waited for the Repel message to take the A; the game state stayed {RepelWaiting}", log.ToString());
        }

        // Nothing is pressed while the message prints; a message that never reaches its wait parks after the stale.
        [Fact]
        public async Task Execute_RepelMessage_NoPressBeforeTheWait()
        {
            List<string> presses = [];
            FakeBot bot = HgssBot(10, 20, presses.Add);
            bot.State = new ScriptedReader(Free, RepelPrinting);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)) { RepelStale = TimeSpan.FromMilliseconds(100) }
                .ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Empty(presses);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        // HGSS's script-2022 snapshot means nothing on RS: it is a lock like any other there, and nothing is pressed.
        [Fact]
        public async Task Execute_Rs_NoRepelStop()
        {
            FakeMemoryClient memory = new();
            memory.Place(0x02025734, [.. BitConverter.GetBytes((short)14), .. BitConverter.GetBytes((short)4)]);
            GameProfile rs = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()));
            List<string> presses = [];
            FakeBot bot = new(Task.CompletedTask, new GameProfile(rs.Flavor, rs.Capabilities, [0], (_, _) => new RsPointerService(), rs.Parser,
                rs.BattleParser, rs.CreateOverworldReader, rs.Maps, rs.Graph, rs.Pathfinding, rs.CreateStateReader, rs.CreateBattleEvent, rs.SaveGame,
                createNavigatorEvent: rs.CreateNavigatorEvent))
            {
                State = new ScriptedReader(Free, RepelWaiting),
                Overworld = new RsOverworldReader(memory, new RsPointerService()),
                Pressed = presses.Add,
            };
            GameState state = Encountering(14, 4);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 1)) { NoBattleWithin = TimeSpan.FromMilliseconds(100) }
                .ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Empty(presses);
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Equal(["RP:M.Up.3.33707828:H.B.1;M.Down.5.33707828", "clear"], bot.Macros);
        }

        // RsPointerService.For("AXPE"), Sapphire rev 2 (docs/research/milestone-builder.md, "The Repel message on Ruby and
        // Sapphire"): WaitForAorBPress + 1, IsFieldMessageBoxHidden + 1 and S_RepelWoreOff + 8.
        private const uint RsWaitForAorB = 0x080670E5;
        private const uint RsPrinting = 0x08064CA9;
        private const uint RsRepelReturn = 0x081C3394;
        private const string RsRunMacro = "RP:M.Up.3.33707828:H.B.1;M.Down.5.33707828";

        // A script read at stack depth 1.
        private static (byte Lock, byte Mode, uint Native, byte Depth, uint Stack0) RsAt(byte lockByte, byte mode, uint native, uint stack0 = RsRepelReturn)
            => (lockByte, mode, native, 1, stack0);

        private static readonly (byte Lock, byte Mode, uint Native, byte Depth, uint Stack0) RsWaiting = RsAt(1, 2, RsWaitForAorB);
        private static readonly (byte Lock, byte Mode, uint Native, byte Depth, uint Stack0) RsClosed = RsAt(0, 0, RsWaitForAorB);

        // An RS Run hunt from (14, 4) on the given table (Sapphire rev 2's by default). The chain reads answer the lock and
        // the script context from script(read, presses), read counting from 1. The seek sees the free field, then a lock;
        // once a read has shown lock 0, the field is free, then a battle. Events records each script read, each press and
        // that free read, with the number of macros sent at the time.
        private static (FakeBot Bot, List<(string Event, int Macros)> Events) RsBot(
            Func<int, int, (byte Lock, byte Mode, uint Native, byte Depth, uint Stack0)> script, RsPointerService? table = null)
        {
            FakeMemoryClient memory = new();
            memory.Place(0x02025734, [.. BitConverter.GetBytes((short)14), .. BitConverter.GetBytes((short)4)]);
            GameProfile rs = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()));
            RsPointerService addresses = table ?? RsPointerService.For("AXPE");
            List<(string, int)> events = [];
            int reads = 0;
            int presses = 0;
            int seekReads = 0;
            bool closed = false;
            bool freeSeen = false;
            FakeBot bot = null!;
            bot = new(Task.CompletedTask, new GameProfile(rs.Flavor, rs.Capabilities, [0], (_, _) => addresses, rs.Parser,
                rs.BattleParser, rs.CreateOverworldReader, rs.Maps, rs.Graph, rs.Pathfinding, rs.CreateStateReader, rs.CreateBattleEvent, rs.SaveGame,
                createNavigatorEvent: rs.CreateNavigatorEvent))
            {
                Overworld = new RsOverworldReader(memory, addresses),
                Pressed = macro =>
                {
                    presses++;
                    events.Add((macro, bot.Macros.Count));
                },
                ChainBlocks = blocks =>
                {
                    Assert.Equal([(0x030006A4u, 1), (0x030005B8u, 16)], blocks.Select(b => (b.Anchor, b.Length)));
                    (byte Lock, byte Mode, uint Native, byte Depth, uint Stack0) s = script(++reads, presses);
                    closed |= s.Lock == 0;
                    events.Add(($"read {reads}: lock {s.Lock} mode {s.Mode} native {s.Native:X8}", bot.Macros.Count));
                    byte[] context = new byte[16];
                    context[0] = s.Depth;
                    context[1] = s.Mode;
                    BitConverter.GetBytes(s.Native).CopyTo(context, 4);
                    BitConverter.GetBytes(s.Stack0).CopyTo(context, 0xC);
                    return [new(ChainBlockStatus.Ok, 0, [s.Lock]), new(ChainBlockStatus.Ok, 0, context)];
                },
            };
            bot.State = new FuncReader(() =>
            {
                if (!closed) return ++seekReads == 1 ? Free : Lock;
                if (freeSeen) return Battle;
                freeSeen = true;
                events.Add(("free", bot.Macros.Count));
                return Free;
            });
            return (bot, events);
        }

        private static async Task<string> ExecuteLogged(RunEncounterEvent run, GameState state)
        {
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await run.ExecuteWithResultAsync(state, CancellationToken.None);
            }
            finally
            {
                Console.SetOut(console);
            }
            return log.ToString();
        }

        // The seek hands over at the second lock poll; the Repel waits are shortened.
        private static RunEncounterEvent RsRun(FakeBot bot) => new(bot, new EncounterParams(EncounterType.Run, yRange: 1))
        {
            LockedHandOff = TimeSpan.FromMilliseconds(1),
            RepelPressConfirm = TimeSpan.FromMilliseconds(50),
            RepelStale = TimeSpan.FromMilliseconds(100),
        };

        // FR-29 on RS, rule 16: at the hand-off one script read names the box, one A after the seek's clear, confirmed by
        // lock 0; the Run macro starts again only after the free field, and its seek finds the battle on the next step.
        [Fact]
        public async Task Rs_RepelMessage_ClosedWithOneAAndTheRunResumes()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, presses) => presses == 0 ? RsWaiting : RsClosed);
            GameState state = Encountering(14, 4);

            string log = await ExecuteLogged(RsRun(bot), state);

            Assert.Equal([
                ("read 1: lock 1 mode 2 native 080670E5", 2),
                ("read 2: lock 1 mode 2 native 080670E5", 2),
                ("T.A;W.20", 2),
                ("read 3: lock 0 mode 0 native 080670E5", 2),
                ("free", 2),
            ], events);
            Assert.Equal([RsRunMacro, "clear", RsRunMacro, "clear"], bot.Macros);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
            Assert.Contains("[Run] REPEL's effect wore off: closed with 1 A, confirmed by lock 0 (lock 0, mode 0, native 0x080670E5, depth 1, stack[0] 0x081C3394)", log);
        }

        // An A before the wait is eaten: none while the text prints, mode 2 and 1 with IsFieldMessageBoxHidden; the A
        // follows the first read at the wait.
        [Fact]
        public async Task Rs_RepelMessage_NoPressWhilePrinting()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((read, presses) =>
                presses > 0 ? RsClosed : read <= 5 ? RsAt(1, (byte)(read % 2 == 1 ? 2 : 1), RsPrinting) : RsWaiting);
            GameState state = Encountering(14, 4);

            await RsRun(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            int press = Assert.Single(Enumerable.Range(0, events.Count), i => events[i].Event == "T.A;W.20");
            Assert.Equal("read 6: lock 1 mode 2 native 080670E5", events[press - 1].Event);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        // A box that never reaches its wait parks at the stale with no press, naming the read.
        [Fact]
        public async Task Rs_RepelMessage_NeverAtTheWait_ParksWithNoPress()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, _) => RsAt(1, 2, RsPrinting));
            GameState state = Encountering(14, 4);

            string log = await ExecuteLogged(RsRun(bot), state);

            Assert.DoesNotContain(events, e => e.Event == "T.A;W.20");
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Equal([RsRunMacro, "clear"], bot.Macros);
            Assert.Contains("[Run] The Repel message did not close. Waited for the Repel message to wait for A; the script state stayed lock 1, mode 2, native 0x08064CA9", log);
        }

        // Another script at the same wait (the stack says so) is not this box: no press, one script read, today's path.
        [Fact]
        public async Task Rs_OtherScriptAtWait_NoPress_TodaysPath()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, _) => RsAt(1, 2, RsWaitForAorB, 0x081C2E54));
            GameState state = Encountering(14, 4);

            string log = await ExecuteLogged(RsRun(bot), state);

            Assert.Equal([("read 1: lock 1 mode 2 native 080670E5", 2)], events);
            Assert.Equal([RsRunMacro, "clear"], bot.Macros);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
            Assert.Contains("[Run] lock handed over: not the Repel message (lock 1, mode 2, native 0x080670E5, depth 1, stack[0] 0x081C2E54)", log);
        }

        // A read still at the full wait after the confirm shows the A was dropped: sent again, once.
        [Fact]
        public async Task Rs_RepelMessage_DroppedPressSentAgain()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, presses) => presses < 2 ? RsWaiting : RsClosed);
            GameState state = Encountering(14, 4);

            await RsRun(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(2, events.Count(e => e.Event == "T.A;W.20"));
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        // The A was taken (mode 1) but the lock stays: no second press, and the stale names the read.
        [Fact]
        public async Task Rs_RepelMessage_TakenButLockStays_OnePressThenParks()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, presses) => presses == 0 ? RsWaiting : RsAt(1, 1, RsWaitForAorB));
            GameState state = Encountering(14, 4);

            string log = await ExecuteLogged(RsRun(bot), state);

            Assert.Equal(1, events.Count(e => e.Event == "T.A;W.20"));
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Equal([RsRunMacro, "clear"], bot.Macros);
            Assert.Contains("[Run] The Repel message did not close. Waited for the Repel message to take the A; the script state stayed lock 1, mode 1", log);
        }

        [Fact]
        public async Task Rs_RepelMessage_NeverTaken_ParksAfterThreeNamingTheRead()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, _) => RsWaiting);
            GameState state = Encountering(14, 4);

            string log = await ExecuteLogged(RsRun(bot), state);

            Assert.Equal(3, events.Count(e => e.Event == "T.A;W.20"));
            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Equal([RsRunMacro, "clear"], bot.Macros);
            Assert.Contains("[Run] The Repel message did not close. Waited for the Repel message to take the A; the script state stayed lock 1, mode 2, native 0x080670E5, depth 1, stack[0] 0x081C3394", log);
        }

        // NFR-1: a wild encounter's lock, shorter than the hand-off, costs no script read.
        [Fact]
        public async Task Rs_NoScriptReadOnAnEncounter()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, _) => RsWaiting);
            bot.State = new ScriptedReader(Free, Lock, Lock, Battle);
            GameState state = Encountering(14, 4);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 1)) { LockedHandOff = TimeSpan.FromSeconds(4) }
                .ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Empty(events);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        // A game code with no per-game part (the shared table): no read, no press, today's path at the hand-off.
        [Fact]
        public async Task Rs_NoPerGamePart_NoReadNoPress()
        {
            (FakeBot bot, List<(string Event, int Macros)> events) = RsBot((_, _) => RsWaiting, RsPointerService.For(null));
            GameState state = Encountering(14, 4);

            await RsRun(bot).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Empty(events);
            Assert.Equal([RsRunMacro, "clear"], bot.Macros);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
        }

        // A change for HGSS: from off the line the macro used to hold a direction for ever.
        [Fact]
        public async Task HgssRunOffTheLineParksWithoutPressing()
        {
            FakeBot bot = HgssBot(10, 23);
            GameState state = Encountering(10, 20);

            await new RunEncounterEvent(bot, new EncounterParams(EncounterType.Run, yRange: 2)).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal(EventState.Waiting, state.State.Peek());
            Assert.Empty(bot.Macros);
        }
    }
}
