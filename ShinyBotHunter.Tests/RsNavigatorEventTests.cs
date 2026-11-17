using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The RS navigator's plan and walk over scripted walk reads on the committed Data/RS; the readings behind them are
    // docs/research/rs-navigation-and-training.md, "The navigator: the walk and the nurse, read frame by frame". RouteSearch
    // asks the registry for RunHold, so the working directory is the app project (as in RouteSearchTests).
    [Collection("macro-registry")]
    public class RsNavigatorEventTests
    {
        // The addresses the event reads, as literals so a wrong table member fails here.
        private const uint FieldControlsLock = 0x030006A4;
        private const uint Callback1 = 0x03001770;
        private const uint InBattle = 0x03001BAD;
        private const uint VBlankCounter = 0x03001790;
        private const uint PlayerPosition = 0x02025734;
        private const uint PlayerLocation = 0x02025738;
        private const uint EventFlags = 0x02026954;

        // Callback 1 in the free field (CB1_Overworld, Sapphire 1.2, from the readings' log).
        private const uint Overworld = 0x08054375;

        private const int OldaleTown = 10, Route101 = 16, Route110 = 25, OldaleCenter = 514;

        // The five macros from Route 101 (14, 4) to the Oldale counter (RouteSearchTests, with RunHold on each), with the
        // map and tile each ends on.
        private static readonly (string Macro, int Map, int X, int Y)[] ToOldaleCenter =
        [
            ("M.Left.8.33707826:H.B.1", Route101, 8, 4),
            ("M.Up.0.33707828:H.B.1", Route101, 8, 0),
            ("M.Up.17.33707828:H.B.1", OldaleTown, 8, 17),
            ("M.Left.6.33707826:H.B.1", OldaleTown, 6, 17),
            ("M.Up.4.33707828:H.B.1", OldaleCenter, 7, 4),
        ];

        private static readonly MapService Rs = new(Path.Combine(RepoPaths.AppProject, "Data", "RS"), withElevationAndEvents: true);

        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

        public RsNavigatorEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        // One walk read: where the player stands, the lock, callback 1 and the battle bit.
        private sealed record Read(int Map, int X, int Y, byte Lock = 0, uint Callback = Overworld, bool Battle = false);

        private static Read Free(int map, int x, int y) => new(map, x, y);

        private static Read Locked(int map, int x, int y) => new(map, x, y, Lock: 1);

        // A walk with its bot and state. The walk reads come from stages, one per macro or clear the event has sent so far
        // (stage 0 before any), each repeating its last read; a send or clear past the last stage fails the test.
        private sealed class Walk
        {
            public required FakeBot Bot { get; init; }
            public required GameState State { get; init; }
            public required RsNavigatorEvent Event { get; init; }

            // The script-state reads and the walk reads served in each stage. Written and read by the test's one async flow.
            public int ScriptStateReads { get; set; }
            public Dictionary<int, int> ReadsInStage { get; } = new();

            public Task<string?> RunAsync() => Event.RunAsync(State, CancellationToken.None);
        }

        private static Walk Start(EventState top, Read[][] stages, (int Map, int X, int Y)? start = null, TimeSpan? handOff = null, TimeSpan? stale = null)
        {
            Queue<Read>[] queues = stages.Select(s => new Queue<Read>(s)).ToArray();
            uint frame = 1000;
            Walk? walk = null;
            FakeBot bot = null!;
            bot = new FakeBot(Task.CompletedTask, new GameProfile(GameFlavor.RS,
                new GameCapabilities(false, false, false, false, false, false, false, false), [], (_, _) => new RsPointerService(),
                new Gen3PokemonParser(new PokemonMetadataService(null!)), null, maps: Rs, seekLockHandOff: handOff ?? TimeSpan.FromSeconds(4)))
            {
                Pressed = macro => bot.Macros.Add(macro),
                ChainBlocks = blocks =>
                {
                    if (blocks[0].Anchor == EventFlags)
                    {
                        walk!.ScriptStateReads++;
                        return [new(ChainBlockStatus.Ok, 0, new byte[0x120]), new(ChainBlockStatus.Ok, 0, new byte[0x200])];
                    }
                    int stage = bot.Macros.Count;
                    Read read = queues[stage].Count > 1 ? queues[stage].Dequeue() : queues[stage].Peek();
                    walk!.ReadsInStage[stage] = walk.ReadsInStage.GetValueOrDefault(stage) + 1;
                    frame += 4;
                    return blocks.Select(b => new ChainBlockResult(ChainBlockStatus.Ok, 0, Bytes(b.Anchor, read, frame))).ToArray();
                },
                State = new FreeField(),
            };
            bot.Overworld = new RsOverworldReader(bot, new RsPointerService());
            GameState state = new() { Bot = bot, Profile = bot.Profile };
            if (start is { } s)
            {
                (state.StartMapId, state.StartX, state.StartY) = s;
            }
            state.State.Push(top);
            walk = new Walk { Bot = bot, State = state, Event = new RsNavigatorEvent(new PokemonStateReader()) { SegmentStale = stale ?? TimeSpan.FromSeconds(20) } };
            return walk;
        }

        private static byte[] Bytes(uint address, Read read, uint frame) => address switch
        {
            FieldControlsLock => [read.Lock],
            Callback1 => BitConverter.GetBytes(read.Callback),
            InBattle => [(byte)(read.Battle ? 0x02 : 0)],
            VBlankCounter => BitConverter.GetBytes(frame),
            PlayerPosition => [.. BitConverter.GetBytes((short)read.X), .. BitConverter.GetBytes((short)read.Y)],
            PlayerLocation => [(byte)(read.Map >> 8), (byte)read.Map],
            _ => throw new InvalidOperationException($"unexpected read at 0x{address:X8}"),
        };

        // The ready wait's reader: the field is free.
        private sealed class FreeField : IGameStateReader
        {
            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new GameSnapshot(GameScreen.FieldFree, null, null, null, null));
        }

        // Stage 0 the plan read on start; then each segment free on its tile.
        private static Read[][] Arriving((string Macro, int Map, int X, int Y)[] segments, Read start) =>
            [[start], .. segments.Select(s => new[] { Free(s.Map, s.X, s.Y) })];

        // FR-11: the cheapest of the fifteen Centers from Route 101 is Oldale's counter, five segments with RunHold on each,
        // none cleared between them and one clear after the last; then the nurse's state.
        [Fact]
        public async Task NearestCenter_FromRoute101_IsOldaleCounter()
        {
            Walk walk = Start(EventState.GoToPokemonCenter, Arriving(ToOldaleCenter, Free(Route101, 14, 4)));

            Assert.Null(await walk.RunAsync());

            Assert.Equal([.. ToOldaleCenter.Select(s => s.Macro), "clear"], walk.Bot.Macros);
            Assert.Equal(EventState.HealAtPokemonCenter, walk.State.State.Peek());
        }

        // N2 review: Route 110 (27, 15) is on a bridge at elevation 15 between tiles at 3 and 4. Passed as 15 the search would
        // refuse every step off it; passed as 0 it plans and the walk starts.
        [Fact]
        public async Task StartElevation_Fifteen_IsPassedAsZero()
        {
            Assert.Equal(15, Rs.GetMap(Route110, true).ElevationAt(15, 27));
            Walk walk = Start(EventState.GoToPokemonCenter, [[Free(Route110, 27, 15)], [new(Route110, 27, 15, Lock: 1, Battle: true)]]);

            Assert.Null(await walk.RunAsync());

            Assert.StartsWith("M.", walk.Bot.Macros[0]);
            Assert.Equal(EventState.BattleStart, walk.State.State.Peek());
        }

        // FR-9: one script-state read for the whole plan, however many walk reads the segments take.
        [Fact]
        public async Task Plan_ReadsFlagsAndVarsOnce()
        {
            Read[][] stages = [[Free(Route101, 14, 4)], .. ToOldaleCenter.Select(s => new[] { Locked(s.Map, s.X + 1, s.Y), Free(s.Map, s.X, s.Y) })];
            Walk walk = Start(EventState.GoToPokemonCenter, stages);

            Assert.Null(await walk.RunAsync());

            Assert.Equal(1, walk.ScriptStateReads);
            Assert.All(Enumerable.Range(1, 5), stage => Assert.Equal(2, walk.ReadsInStage[stage]));
        }

        // FR-9: the walk back from the counter (the exit mat, then outside), each segment ending on its tile with the next
        // sent without a clear; arrived, the return finishes.
        [Fact]
        public async Task Segment_EndsOnItsTile_NextSentWithoutClear()
        {
            (string, int, int, int)[] segments = [("M.Down.8.33707828:H.B.1", OldaleCenter, 7, 8), ("M.Down.17.33707828:H.B.1", OldaleTown, 6, 17)];
            Walk walk = Start(EventState.ReturnToStart, Arriving(segments, Free(OldaleCenter, 7, 4)), start: (OldaleTown, 6, 17));

            Assert.Null(await walk.RunAsync());

            Assert.Equal(["M.Down.8.33707828:H.B.1", "M.Down.17.33707828:H.B.1", "clear"], walk.Bot.Macros);
            Assert.Equal(EventState.DecideOverworldAction, walk.State.State.Peek());
        }

        // W1: after the warp callback 1 reads 0 with the lock 0 for 4 to 7 frames. On the segment's own tile that read does
        // not end it; the next one, with callback 1 back, does.
        [Fact]
        public async Task Segment_AfterWarp_CallbackZeroIsNotFree()
        {
            Walk walk = Start(EventState.GoToPokemonCenter,
                [[Free(OldaleTown, 6, 17)], [new(OldaleCenter, 7, 4, Callback: 0), Free(OldaleCenter, 7, 4)]]);

            Assert.Null(await walk.RunAsync());

            Assert.Equal(["M.Up.4.33707828:H.B.1", "clear"], walk.Bot.Macros);
            Assert.Equal(2, walk.ReadsInStage[1]);
        }

        // W3: crossing the connection, one read shows y 20, one row past Oldale's last; the segment ends on y 17.
        [Fact]
        public async Task Segment_ConnectionRowPastTheEdge_DoesNotEndIt()
        {
            Walk walk = Start(EventState.ReturnToStart,
                [[Free(Route101, 8, 1)], [Free(Route101, 8, 0)],
                    [Free(OldaleTown, 8, 20), Free(OldaleTown, 8, 19), Free(OldaleTown, 8, 18), Free(OldaleTown, 8, 17)]],
                start: (OldaleTown, 8, 17));

            Assert.Null(await walk.RunAsync());

            Assert.Equal(["M.Up.0.33707828:H.B.1", "M.Up.17.33707828:H.B.1", "clear"], walk.Bot.Macros);
            Assert.Equal(4, walk.ReadsInStage[2]);
            Assert.Equal(EventState.DecideOverworldAction, walk.State.State.Peek());
        }

        // W4: the macro has ended with the field locked on the door tile of the old map, mid-step; the warp fires, and the
        // segment ends only once the field is free on its tile in the new map.
        [Fact]
        public async Task Segment_EndsOnLandingAfterDoorCoordinate()
        {
            Walk walk = Start(EventState.GoToPokemonCenter,
                [[Free(OldaleTown, 6, 17)],
                    [Locked(OldaleTown, 6, 16), Locked(OldaleTown, 6, 16), new(OldaleCenter, 7, 8, Callback: 0), Locked(OldaleCenter, 7, 8),
                        Locked(OldaleCenter, 7, 7), Free(OldaleCenter, 7, 6), Free(OldaleCenter, 7, 4)]]);

            Assert.Null(await walk.RunAsync());

            Assert.Equal(["M.Up.4.33707828:H.B.1", "clear"], walk.Bot.Macros);
            Assert.Equal(7, walk.ReadsInStage[1]);
            Assert.Equal(EventState.HealAtPokemonCenter, walk.State.State.Peek());
        }

        // FR-9: no read on Oldale (8, 17) within the stale time stops the walk, the macro cleared.
        [Fact]
        public async Task Segment_Stale_StopsStuckWalking()
        {
            Walk walk = Start(EventState.ReturnToStart, [[Free(Route101, 8, 1)], [Free(Route101, 8, 0)], [Free(OldaleTown, 8, 19)]],
                start: (OldaleTown, 8, 17), stale: Short);

            Assert.Equal("Stuck walking to OldaleTown (8, 17).", await walk.RunAsync());

            Assert.Equal(["M.Up.0.33707828:H.B.1", "M.Up.17.33707828:H.B.1", "clear"], walk.Bot.Macros);
            Assert.Equal(EventState.Waiting, walk.State.State.Peek());
        }

        // FR-9: the battle bit mid-segment clears the macro and pushes BattleStart over the state. Once the battle has
        // popped, the next entry reads the flags again and plans from where the player stands, then walks on.
        [Fact]
        public async Task WildBattleMidSegment_PushesBattleStart_ThenPlansFromWhereThePlayerStands()
        {
            Read[][] stages =
            [
                [Free(Route101, 14, 4)],
                [Free(Route101, 13, 4), Locked(Route101, 13, 4), new(Route101, 13, 4, Lock: 1, Battle: true)],
                [Free(Route101, 13, 4)],
                .. ToOldaleCenter.Select(s => new[] { Free(s.Map, s.X, s.Y) }),
            ];
            Walk walk = Start(EventState.GoToPokemonCenter, stages);

            Assert.Null(await walk.RunAsync());

            Assert.Equal([ToOldaleCenter[0].Macro, "clear"], walk.Bot.Macros);
            Assert.Equal([EventState.BattleStart, EventState.GoToPokemonCenter], walk.State.State);

            walk.State.State.Pop();
            Assert.Null(await walk.RunAsync());

            Assert.Equal(2, walk.ScriptStateReads);
            Assert.Equal([ToOldaleCenter[0].Macro, "clear", .. ToOldaleCenter.Select(s => s.Macro), "clear"], walk.Bot.Macros);
            Assert.Equal(EventState.HealAtPokemonCenter, walk.State.State.Peek());
        }

        // FR-10: a lock with the map and tile unchanged for the hand-off time and no battle bit goes to the battle event,
        // which reads the trainer; the walk clears its macro and sends nothing else.
        [Fact]
        public async Task LockWithoutBattle_HandsOffAfterSeekLockHandOff_PressingNothing()
        {
            Walk walk = Start(EventState.GoToPokemonCenter, [[Free(Route101, 14, 4)], [Free(Route101, 13, 4), Locked(Route101, 13, 4)]], handOff: Short);

            Assert.Null(await walk.RunAsync());

            Assert.Equal([ToOldaleCenter[0].Macro, "clear"], walk.Bot.Macros);
            Assert.Equal([EventState.BattleStart, EventState.GoToPokemonCenter], walk.State.State);
        }

        // A lock that moves the player (a door's walk-in, an arrival step) restarts the hand-off clock on every new tile, so
        // it is not handed off however long it lasts.
        [Fact]
        public async Task LockThatMovesThePlayer_IsNotHandedOff()
        {
            Read[] moving = [.. Enumerable.Range(0, 40).Select(i => Locked(Route101, 13 - i % 2, 4)), Free(Route101, 8, 4)];
            Walk walk = Start(EventState.ReturnToStart, [[Free(Route101, 14, 4)], moving], start: (Route101, 8, 4), handOff: TimeSpan.FromMilliseconds(100));

            Assert.Null(await walk.RunAsync());

            Assert.Equal([ToOldaleCenter[0].Macro, "clear"], walk.Bot.Macros);
            Assert.Equal(EventState.DecideOverworldAction, walk.State.State.Peek());
        }

        // No route: Waiting with the sentence, nothing sent. Oldale (0, 0) is a blocked tile.
        [Fact]
        public async Task NoRoute_StopsWithSentence()
        {
            Walk walk = Start(EventState.ReturnToStart, [[Free(Route101, 14, 4)]], start: (OldaleTown, 0, 0));

            Assert.Equal("No route back to the start tile OldaleTown (0, 0) from Route101 (14, 4).", await walk.RunAsync());

            Assert.Empty(walk.Bot.Macros);
            Assert.Equal(EventState.Waiting, walk.State.State.Peek());
        }

        // Zero steps: already on the start tile, the return finishes with nothing sent.
        [Fact]
        public async Task ReturnToStart_AlreadyThere_Finishes()
        {
            Walk walk = Start(EventState.ReturnToStart, [[Free(Route101, 14, 4)]], start: (Route101, 14, 4));
            walk.State.CompleteAfterReturn = true;

            Assert.Null(await walk.RunAsync());

            Assert.Empty(walk.Bot.Macros);
            Assert.Equal(EventState.Complete, walk.State.State.Peek());
        }

        // The nurse (FR-11) over scripted nurse reads, the values from research "The nurse" (Sapphire run 1 unless named).
        private const uint FieldMessageWindowState = 0x0202E892;
        private const uint ScriptContextStatus = 0x030005B0;
        private const uint ScriptContextMode = 0x030005B9;
        private const uint ScriptNative = 0x030005BC;
        private const uint Tasks = 0x03004B20;
        private const uint PlayerFacing = 0x030048B8;
        private const uint PlayerTileTransition = 0x0202E85B;
        private const uint TaskHandleYesNoInput = 0x080B5505;
        private const uint SapphireWaitForAorBPress = 0x080670E5;
        private const uint RubyWaitForAorBPress = 0x080670E1;

        // The natives read while the nurse speaks (0x08064DD9), heals (0x08067B6D) and bows (0x08066B2D).
        private const uint Speaking = 0x08064DD9, Healing = 0x08067B6D, Bowing = 0x08066B2D;

        // One nurse read; the defaults are the player at rest facing up with no script.
        private sealed record Nurse(byte Lock = 0, ushort Window = 0, byte Status = 2, byte Mode = 0, uint Native = 0,
            short? YesNo = null, byte Facing = 0x22, byte Transition = 0);

        private static Nurse Talking(ushort window) => new(Lock: 1, Window: window, Status: 0, Mode: 2, Native: Speaking);

        private static Nurse YesNo(short counter) => new(Lock: 1, Status: 1, Mode: 1, Native: Speaking, YesNo: counter);

        private static Nurse LastWait(uint native, byte mode = 2) => new(Lock: 1, Status: 0, Mode: mode, Native: native);

        // The nurse's reads, one stage per press sent so far, each repeating its last read: the talk, two page breaks, the
        // Yes once data[2] reads 5, the heal and "Thank you for waiting.", the bow and the last line printing by itself, then
        // the script's button wait and the field free. The first stage is the counter tile before the talk.
        private static Nurse[][] Visit(uint native, Nurse[] first) =>
        [
            first,
            [new Nurse(Native: native), Talking(0), Talking(1), Talking(3), Talking(8)],
            [Talking(2), Talking(3), Talking(8)],
            [Talking(2), Talking(0), YesNo(1), YesNo(2), YesNo(3), YesNo(4), YesNo(5)],
            [new(Lock: 1, Status: 0, Mode: 2, Native: Healing), new(Lock: 1, Status: 0, Mode: 2, Native: Healing), Talking(1), Talking(3), Talking(8)],
            [Talking(2), new(Lock: 1, Status: 0, Mode: 2, Native: Bowing), Talking(1), Talking(3), Talking(0), LastWait(native)],
            [new(Lock: 1, Status: 0, Mode: 1, Native: native), new(Native: native)],
        ];

        // The walk's last step still ending (tile transition 1), then at rest; the native stale from an earlier script.
        private static Nurse[] Arrived(uint native) => [new(Native: native, Transition: 1), new(Native: native)];

        private sealed class NurseVisit
        {
            public required FakeBot Bot { get; init; }
            public required GameState State { get; init; }
            public required RsNavigatorEvent Event { get; init; }

            // The read each press was sent on. Written and read by the test's one async flow.
            public List<Nurse> PressedOn { get; } = [];
            public Nurse? Last { get; set; }

            public Task<string?> RunAsync() => Event.RunAsync(State, CancellationToken.None);
        }

        private static NurseVisit AtTheCounter(Nurse[][] stages, string gameCode = "AXPE", ushort hp = 25, TimeSpan? stale = null, TimeSpan? handOff = null)
        {
            Queue<Nurse>[] queues = stages.Select(s => new Queue<Nurse>(s)).ToArray();
            uint frame = 8000;
            NurseVisit? visit = null;
            FakeBot bot = null!;
            bot = new FakeBot(Task.CompletedTask, new GameProfile(GameFlavor.RS,
                new GameCapabilities(false, false, false, false, false, false, false, false), [], (_, _) => RsPointerService.For(gameCode),
                new Gen3PokemonParser(new PokemonMetadataService(null!)), null, maps: Rs, seekLockHandOff: handOff ?? TimeSpan.FromSeconds(4)))
            {
                Pressed = macro =>
                {
                    bot.Macros.Add(macro);
                    visit!.PressedOn.Add(visit.Last!);
                },
                ChainBlocks = blocks =>
                {
                    Queue<Nurse> queue = queues[bot.Macros.Count];
                    Nurse read = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                    visit!.Last = read;
                    frame++;
                    return blocks.Select(b => new ChainBlockResult(ChainBlockStatus.Ok, 0, NurseBytes(b.Anchor, read, frame))).ToArray();
                },
                PartyBlocks = [PartyRecord(hp, 25)],
            };
            GameState state = new() { Bot = bot, Profile = bot.Profile };
            state.State.Push(EventState.HealAtPokemonCenter);
            visit = new NurseVisit { Bot = bot, State = state, Event = new RsNavigatorEvent(new PokemonStateReader()) { NurseStale = stale ?? TimeSpan.FromSeconds(10) } };
            return visit;
        }

        private static byte[] NurseBytes(uint address, Nurse read, uint frame)
        {
            switch (address)
            {
                case FieldControlsLock: return [read.Lock];
                case Callback1: return BitConverter.GetBytes(Overworld);
                case FieldMessageWindowState: return BitConverter.GetBytes(read.Window);
                case ScriptContextStatus: return [read.Status];
                case ScriptContextMode: return [read.Mode];
                case ScriptNative: return BitConverter.GetBytes(read.Native);
                case VBlankCounter: return BitConverter.GetBytes(frame);
                case PlayerFacing: return [read.Facing];
                case PlayerTileTransition: return [read.Transition];
                case Tasks:
                    // The Yes/No task in slot 4, as read; data[2] at task + 0x0C.
                    byte[] tasks = new byte[16 * 0x28];
                    if (read.YesNo is { } counter)
                    {
                        BitConverter.GetBytes(TaskHandleYesNoInput).CopyTo(tasks, 4 * 0x28);
                        tasks[4 * 0x28 + 4] = 1;
                        BitConverter.GetBytes(counter).CopyTo(tasks, 4 * 0x28 + 0x0C);
                    }
                    return tasks;
                default: throw new InvalidOperationException($"unexpected read at 0x{address:X8}");
            }
        }

        // A party-sized Gen 3 record of a level 10 Mudkip with the HP given. pid % 24 == 0 keeps the substructures in order;
        // otId 0 makes the key the pid.
        private static byte[] PartyRecord(ushort hp, ushort maxHp)
        {
            const uint pid = 0x60;
            byte[] plain = new byte[48];
            BitConverter.GetBytes((ushort)283).CopyTo(plain, 0);
            ushort checksum = 0;
            for (int i = 0; i < 48; i += 2)
            {
                checksum += BitConverter.ToUInt16(plain, i);
            }
            byte[] block = new byte[Gen3PokemonParser.PartyBlockSize];
            BitConverter.GetBytes(pid).CopyTo(block, 0x00);
            BitConverter.GetBytes(checksum).CopyTo(block, 0x1C);
            for (int i = 0; i < 48; i += 4)
            {
                BitConverter.GetBytes(BitConverter.ToUInt32(plain, i) ^ pid).CopyTo(block, 0x20 + i);
            }
            block[0x54] = 10;
            BitConverter.GetBytes(hp).CopyTo(block, 0x56);
            BitConverter.GetBytes(maxHp).CopyTo(block, 0x58);
            return block;
        }

        private static string FieldAdvance => MacroRegistry.Instance.GetMacro(GameFlavor.RS, "FieldAdvance");

        // FR-11: six FieldAdvances, each sent once on the read its gate names: the talk at rest (not mid-step), the two page
        // breaks at window 8, the Yes at data[2] 5 (not 1 to 4), "Thank you for waiting." at 8, and the last line on the
        // script's own button wait after the Yes (not on the stale native before the talk). HP full: ReturnToStart.
        [Fact]
        public async Task Nurse_SixPresses_EachOnItsGate()
        {
            NurseVisit visit = AtTheCounter(Visit(SapphireWaitForAorBPress, Arrived(SapphireWaitForAorBPress)));

            Assert.Null(await visit.RunAsync());

            Assert.Equal(Enumerable.Repeat(FieldAdvance, 6), visit.Bot.Macros);
            Assert.Equal([new Nurse(Native: SapphireWaitForAorBPress), Talking(8), Talking(8), YesNo(5), Talking(8), LastWait(SapphireWaitForAorBPress)],
                visit.PressedOn);
            Assert.Equal(EventState.ReturnToStart, visit.State.State.Peek());
        }

        // N6: on Ruby the last wait is Ruby's WaitForAorBPress, from the per-game table.
        [Fact]
        public async Task Nurse_RubyNative_IsPressedOnRuby()
        {
            NurseVisit visit = AtTheCounter(Visit(RubyWaitForAorBPress, Arrived(RubyWaitForAorBPress)), gameCode: "AXVE");

            Assert.Null(await visit.RunAsync());

            Assert.Equal(6, visit.Bot.Macros.Count);
            Assert.Equal(LastWait(RubyWaitForAorBPress), visit.PressedOn[^1]);
        }

        // The native stays WaitForAorBPress after any script ends: with mode 1 it is not the script's button wait, and before
        // the Yes it is not the last line, so neither is pressed; the wait stops naming what it saw.
        [Fact]
        public async Task Nurse_StaleNativeWithoutModeTwo_IsNotPressed()
        {
            Nurse[][] modeOne = Visit(SapphireWaitForAorBPress, Arrived(SapphireWaitForAorBPress))[..6];
            modeOne[5] = [Talking(2), Talking(0), LastWait(SapphireWaitForAorBPress, mode: 1)];
            NurseVisit visit = AtTheCounter(modeOne, stale: Short);

            Assert.StartsWith("Waited for a page break or the script's last button wait; the game state stayed (f ", await visit.RunAsync());

            Assert.Equal(5, visit.Bot.Macros.Count);
            Assert.Equal(EventState.Waiting, visit.State.State.Peek());

            NurseVisit beforeYes = AtTheCounter([Arrived(SapphireWaitForAorBPress), [LastWait(SapphireWaitForAorBPress)]], stale: Short);

            Assert.StartsWith("Waited for a page break or the Yes/No with its counter at 5; the game state stayed (f ", await beforeYes.RunAsync());

            Assert.Equal([FieldAdvance], beforeYes.Bot.Macros);
        }

        // W8: facing left at the counter, SpinUp turns the player; the talk follows on the first read facing up at rest.
        [Fact]
        public async Task Nurse_FacingLeft_TurnsThenTalks()
        {
            Nurse[][] stages = [[new(Facing: 0x33)], .. Visit(SapphireWaitForAorBPress, [new()])];
            NurseVisit visit = AtTheCounter(stages);

            Assert.Null(await visit.RunAsync());

            Assert.Equal([MacroRegistry.Instance.GetMacro(GameFlavor.RS, "SpinUp"), .. Enumerable.Repeat(FieldAdvance, 6)], visit.Bot.Macros);
            Assert.Equal([new Nurse(Facing: 0x33), new Nurse()], visit.PressedOn[..2]);
        }

        // A talk whose lock never comes: the read stays the same, and the stop names the wait and the read.
        [Fact]
        public async Task Nurse_ReadUnchanged_StopsNamingIt()
        {
            NurseVisit visit = AtTheCounter([[new()], [new()]], stale: Short);

            string? stopped = await visit.RunAsync();

            Assert.StartsWith("Waited for the field locked after the talk; the game state stayed (f ", stopped);
            Assert.EndsWith("lock 0, callback 1 0x08054375, window 0, status 2, mode 0, native 0x00000000, yes/no none, facing 0x22, tile transition 0).", stopped);
            Assert.Equal([FieldAdvance], visit.Bot.Macros);
            Assert.Equal(EventState.Waiting, visit.State.State.Peek());
        }

        // FR-11: HP not full after the nurse stops the bot with the member's HP.
        [Fact]
        public async Task Nurse_HpNotFull_Stops()
        {
            NurseVisit visit = AtTheCounter(Visit(SapphireWaitForAorBPress, Arrived(SapphireWaitForAorBPress)), hp: 5);

            string? stopped = await visit.RunAsync();

            Assert.StartsWith("The party's HP was not full after the nurse: ", stopped);
            Assert.EndsWith(" 5/25.", stopped);
            Assert.Equal(EventState.Waiting, visit.State.State.Peek());
        }

        // From the N4b review: the nurse's lock (564 frames) outlasts SeekLockHandOff, and nothing hands it to the battle event.
        [Fact]
        public async Task Nurse_LockedPastTheHandOff_IsNotHandedOff()
        {
            NurseVisit visit = AtTheCounter(Visit(SapphireWaitForAorBPress, Arrived(SapphireWaitForAorBPress)), handOff: TimeSpan.Zero);

            Assert.Null(await visit.RunAsync());

            Assert.Equal(Enumerable.Repeat(FieldAdvance, 6), visit.Bot.Macros);
            Assert.Equal([EventState.ReturnToStart], visit.State.State);
        }

        [Fact]
        public async Task Nurse_HpFull_GoesToReturnToStart()
        {
            NurseVisit visit = AtTheCounter(Visit(SapphireWaitForAorBPress, Arrived(SapphireWaitForAorBPress)));

            Assert.Null(await visit.RunAsync());

            Assert.Equal(1, visit.Bot.PartyReads);
            Assert.Equal(EventState.ReturnToStart, visit.State.State.Peek());
        }
    }
}
