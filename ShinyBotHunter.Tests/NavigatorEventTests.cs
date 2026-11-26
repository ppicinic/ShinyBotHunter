using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The press loop over scripted snapshots; values from pointer-discovery.md, "Field scripts". In the collection because
    // the walk tests' MapService opens maps.bin without read sharing.
    [Collection("macro-registry")]
    public class NavigatorEventTests
    {
        private static readonly HgssPointerService Table = new();
        private static readonly TimeSpan Stale = TimeSpan.FromMilliseconds(200);

        private const uint RunScripts = 0x0203FF45;
        private const uint Printing = 0x021EF349;
        private const uint MenuChoice = 0x020477C1;
        private const uint Movement = 0x02041CA9;
        private const uint WaitStd = 0x02040BCD;
        private const uint TouchMenuHide = 0x020476E9;
        private const uint TouchMenuShow = 0x02047745;
        private const uint FadeTask = 0x02205B15;
        private const uint HealAnimationTask = 0x0224BB91;

        private const string A = "T.A;W.20";
        private const string B = "T.B;W.20";
        private const string Yes = "W.5;T.A;W.20";

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        private static GameSnapshot Script(uint? native, int script = 1, int? textPrinter = null, uint? textCursor = null)
            => new(GameScreen.FieldTask, null, script, null, RunScripts, native, TextPrinter: textPrinter, TextCursor: textCursor);

        // One box of the printing native: drawing, at the arrow, then the slot NULL.
        private static GameSnapshot[] Box(int script = 1) => [Script(Printing, script, 0), Script(Printing, script, 2), Script(Printing, script)];

        private static GameSnapshot ChildTask(uint function) => new(GameScreen.FieldTask, null, null, null, function);

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);

        // The press completes at once and is recorded.
        private static Task PressThrough(ScriptedReader reader, List<string> presses)
            => NavigatorEvent.PressThroughScriptAsync(reader, Table, macro =>
            {
                presses.Add(macro);
                return Task.CompletedTask;
            }, "the script", Stale, CancellationToken.None);

        // The Pokérus trace: greeting, touch menu hide, Yes/No, the nurse moves, message, the fade and the heal
        // animation, the nurse moves, the closing and Pokérus boxes, touch menu show.
        [Fact]
        public async Task HealPressesAUntilTheMenuThenOneYesThenB()
        {
            ScriptedReader reader = new(
            [
                .. Box(), Script(TouchMenuHide), Script(MenuChoice), Script(Movement), .. Box(), Script(null),
                ChildTask(FadeTask), ChildTask(HealAnimationTask), Script(Movement), .. Box(), .. Box(), .. Box(),
                Script(TouchMenuShow), Free,
            ]);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, Yes, B, B, B, B], presses);
        }

        [Fact]
        public async Task PoisonScriptPressesOnceAtTheArrow()
        {
            ScriptedReader reader = new(Script(Printing, 2003, 0), Script(Printing, 2003, 0), Script(Printing, 2003, 2), Script(Printing, 2003), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A], presses);
        }

        [Fact]
        public async Task NothingIsPressedWhilePrintingDraws()
        {
            ScriptedReader reader = new(Script(Printing, 1, 0), Script(Printing, 1, 6), Script(Printing, 1, 0), Script(Printing), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Empty(presses);
        }

        // Between boxes the printer goes back to drawing; 3 is the arrow that scrolls.
        [Fact]
        public async Task MultiBoxMessageGetsOnePressPerArrow()
        {
            ScriptedReader reader = new(
                Script(Printing, 1, 0), Script(Printing, 1, 2), Script(Printing, 1, 0), Script(Printing, 1, 3), Script(Printing), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, A], presses);
        }

        // The #374 watcher: the scroll arrow pressed, and the next box already at its own arrow at the first read after.
        [Fact]
        public async Task NextBoxAtItsArrowConfirmsThePressAtOnce()
        {
            ScriptedReader reader = new(
                Script(Printing, 1, 3, 0x022A7966), Script(Printing, 1, 2, 0x022A798E), Script(Printing, 1, 2, 0x022A798E), Script(Printing), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, A], presses);
        }

        [Fact]
        public async Task ArrowThatDoesNotClearIsRetriedThenThrows()
        {
            List<string> presses = [];
            ScriptedReader reader = new(Script(Printing, 1, 2, 0x022A7966));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => PressThrough(reader, presses));

            Assert.Equal([A, A, A], presses);
            Assert.Equal("the script box", e.WaitingFor);
        }

        [Theory]
        [InlineData(0x02041075u)]
        [InlineData(0x02041041u)]
        [InlineData(0x020410F1u)]
        public async Task WaitButtonNativesArePressedLikePrinting(uint native)
        {
            ScriptedReader reader = new(Script(native), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A], presses);
        }

        [Fact]
        public async Task NothingIsPressedOutsideThePressNatives()
        {
            ScriptedReader reader = new(
                Script(Movement), Script(WaitStd), Script(TouchMenuHide), Script(TouchMenuShow), Script(null),
                ChildTask(FadeTask), ChildTask(HealAnimationTask), new GameSnapshot(GameScreen.Transitioning, null, null, null, null), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Empty(presses);
        }

        [Fact]
        public async Task MenuAfterTheYesGetsB()
        {
            ScriptedReader reader = new(Script(MenuChoice), Script(Movement), Script(MenuChoice), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([Yes, B], presses);
        }

        // An A pressed at the greeting lands on the menu between reads: the heal animation says Yes was given.
        [Fact]
        public async Task HealAnimationMarksTheYesWhenTheMenuWasMissed()
        {
            ScriptedReader reader = new(
                Script(Printing, 1, 2), Script(Printing), ChildTask(HealAnimationTask), Script(Printing, 1, 2), Free);

            List<string> presses = [];
            await PressThrough(reader, presses);

            Assert.Equal([A, B], presses);
        }

        [Fact]
        public async Task UnchangedScriptThrowsWhenStale()
        {
            ScriptedReader reader = new(Script(Movement));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => PressThrough(reader, []));

            Assert.Equal("the script", e.WaitingFor);
            Assert.Equal(Script(Movement), e.Last);
        }

        // The headbutt trace (research/headbutt.md): the question, touch menu hide, Yes/No, "used Headbutt.", the swap's
        // natives and tasks, then the encounter task and the screen leaving the field.
        private static GameSnapshot[] HeadbuttScript(GameSnapshot end) =>
        [
            .. Box(10014), Script(TouchMenuHide, 10014), Script(MenuChoice, 10014), .. Box(10014),
            Script(0x020492F1, 10014), Script(0x020408D9, 10014), ChildTask(0x0224E0ED), ChildTask(0x022507E9), ChildTask(0x020551B9),
            new GameSnapshot(GameScreen.Transitioning, null, null, null, null), end,
        ];

        private static Task<GameSnapshot> PressThroughHeadbutt(ScriptedReader reader, List<string> presses)
            => NavigatorEvent.PressThroughScriptAsync(reader, Table, macro =>
            {
                presses.Add(macro);
                return Task.CompletedTask;
            }, "the headbutt script", Stale, CancellationToken.None, HeadbuttEncounterEvent.LeftTheScript);

        [Fact]
        public async Task HeadbuttLoopEndsOnTheBattleScreen()
        {
            GameSnapshot battle = new(GameScreen.BattleStarting, null, null, null, null);
            ScriptedReader reader = new(HeadbuttScript(battle));

            List<string> presses = [];
            GameSnapshot end = await PressThroughHeadbutt(reader, presses);

            Assert.Equal([A, Yes, B], presses);
            Assert.Equal(battle, end);
        }

        [Fact]
        public async Task HeadbuttLoopEndsOnFieldFreeWithNoBattle()
        {
            ScriptedReader reader = new(HeadbuttScript(Free));

            List<string> presses = [];
            GameSnapshot end = await PressThroughHeadbutt(reader, presses);

            Assert.Equal([A, Yes, B], presses);
            Assert.Equal(Free, end);
        }

        [Fact]
        public async Task YesThatDoesNotLandIsRetriedThenThrows()
        {
            List<string> presses = [];
            ScriptedReader reader = new(Script(MenuChoice));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => PressThrough(reader, presses));

            Assert.Equal([Yes, Yes, Yes], presses);
            Assert.Equal("the script Yes", e.WaitingFor);
        }

        // The sign natives (research/hgss-signs.md).
        private const uint SignWait = 0x020415E1;
        private const uint SignPrinting = 0x02041521;
        private const uint SlideIn = 0x02041455;

        public static TheoryData<GameSnapshot, bool> SignSnapshots => new()
        {
            { Script(SignWait, 9), true },
            { Script(SignPrinting, 10), true },
            { Script(SlideIn, 9), false },
            { Script(null, 9), false },
            { Script(Printing, 2003, 2), false },
            { Script(Printing, 1, 2), false },
            { RepelPrinting, false },
            { RepelWaiting, false },
            { Script(MenuChoice), false },
            { Script(0x02041075), false },
            { new GameSnapshot(GameScreen.FieldTask, null, 9, null, FadeTask, SignWait), false },
            { new GameSnapshot(GameScreen.Transitioning, null, null, null, null), false },
            { Free, false },
        };

        [Theory]
        [MemberData(nameof(SignSnapshots))]
        public void IsSignOnlyAtTheTwoSignNatives(GameSnapshot snapshot, bool sign)
        {
            Assert.Equal(sign, NavigatorEvent.IsSign(snapshot, Table));
        }

        [Fact]
        public async Task HeldWatchEndsOnASign()
        {
            ScriptedReader reader = new(Script(null, 9), Script(SlideIn, 9), Script(SlideIn, 9), Script(null, 9), Script(SignWait, 9));

            GameSnapshot end = await NavigatorEvent.WaitForFieldFreeOrSignAsync(reader, Table, CancellationToken.None);

            Assert.Equal(Script(SignWait, 9), end);
        }

        // Youngster Joey's challenge on Route 30 (docs/research/milestone-builder.md, "HeartGold and SoulSilver: a trainer's
        // challenge during a navigator walk", R2): the approach script with its first native, with none, printing, at the arrow.
        private const int TrainerApproach = 3739;
        private static readonly GameSnapshot ChallengeStep = Script(0x02046D41, TrainerApproach);
        private static readonly GameSnapshot ChallengeQuiet = Script(null, TrainerApproach);
        private static readonly GameSnapshot ChallengeArrow = Script(Printing, TrainerApproach, 2, 0x022A860E);

        public static TheoryData<GameSnapshot, bool> ChallengeSnapshots => new()
        {
            { ChallengeStep, true },
            { ChallengeQuiet, true },
            { Script(Printing, TrainerApproach, 0), true },
            { ChallengeArrow, true },
            { Script(Printing, 3007, 2), false },
            { Script(null, 3), false },
            { RepelPrinting, false },
            { Script(Printing, 2003, 2), false },
            { new GameSnapshot(GameScreen.FieldTask, null, null, null, 0x020551B9), false },
            { Free, false },
        };

        [Theory]
        [MemberData(nameof(ChallengeSnapshots))]
        public void IsTrainerChallengeOnlyOnTheApproachScript(GameSnapshot snapshot, bool challenge)
        {
            Assert.Equal(challenge, NavigatorEvent.IsTrainerChallenge(snapshot, Table));
        }

        [Fact]
        public async Task HeldWatchEndsOnATrainerChallenge()
        {
            ScriptedReader reader = new(ChallengeStep, ChallengeQuiet, ChallengeArrow);

            GameSnapshot end = await NavigatorEvent.WaitForFieldFreeOrSignAsync(reader, Table, CancellationToken.None);

            Assert.Equal(ChallengeStep, end);
        }

        // The door trace: no script, never the script task.
        [Fact]
        public async Task HeldWatchEndsOnTheFreeFieldThroughAWarp()
        {
            ScriptedReader reader = new(
                ChildTask(FadeTask), new GameSnapshot(GameScreen.Transitioning, null, null, null, null), ChildTask(FadeTask), Free);

            GameSnapshot end = await NavigatorEvent.WaitForFieldFreeOrSignAsync(reader, Table, CancellationToken.None);

            Assert.Equal(Free, end);
        }

        private static Task CloseSign(ScriptedReader reader, List<string> presses)
            => NavigatorEvent.CloseSignAsync(reader, Table, macro =>
            {
                presses.Add(macro);
                return Task.CompletedTask;
            }, Stale, CancellationToken.None);

        [Fact]
        public async Task BoardIsClosedWithOneBAtTheWaitNative()
        {
            ScriptedReader reader = new(
                Script(null), Script(SlideIn), Script(SlideIn), Script(null), Script(SignWait), Script(null), Script(WaitStd), Script(null), Free);

            List<string> presses = [];
            await CloseSign(reader, presses);

            Assert.Equal([B], presses);
        }

        [Fact]
        public async Task TextSignGetsOneBAtItsArrowThenOneAtTheWait()
        {
            ScriptedReader reader = new(
                Script(SlideIn), Script(SignPrinting, 10, 0), Script(SignPrinting, 10, 0), Script(SignPrinting, 10, 2, 0x022A7966),
                Script(SignPrinting, 10), Script(SignWait, 10), Script(null, 10), Free);

            List<string> presses = [];
            await CloseSign(reader, presses);

            Assert.Equal([B, B], presses);
        }

        public static TheoryData<GameSnapshot> NoKeyStates => new()
        {
            Script(SlideIn),
            Script(null),
            Script(SignPrinting, 10, 0),
        };

        [Theory]
        [MemberData(nameof(NoKeyStates))]
        public async Task NothingIsPressedWithoutAKeyState(GameSnapshot held)
        {
            List<string> presses = [];
            ScriptedReader reader = new(held);

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => CloseSign(reader, presses));

            Assert.Empty(presses);
            Assert.Equal("the sign to take a key", e.WaitingFor);
            Assert.Equal(held, e.Last);
        }

        [Fact]
        public async Task BThatDoesNotCloseTheBoardIsRetriedThenThrows()
        {
            List<string> presses = [];
            ScriptedReader reader = new(Script(SignWait));

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => CloseSign(reader, presses));

            Assert.Equal([B, B, B], presses);
            Assert.Equal("the sign close", e.WaitingFor);
            Assert.Equal(Script(SignWait), e.Last);
        }

        [Fact]
        public void StuckTilePenaltyAddsToTheTileItIsGiven()
        {
            GameState state = new();

            int first = NavigatorEvent.AddStuckTilePenalty(state, 73, 488, 264);
            int second = NavigatorEvent.AddStuckTilePenalty(state, 73, 488, 264);

            Assert.Equal(25, first);
            Assert.Equal(50, second);
            Assert.Equal([73], state.TilePenalties.Keys);
            Assert.Equal([(488, 264)], state.TilePenalties[73].Keys);
        }

        // The player on a live map at a tile, as the HGSS overworld reader reads it; the anchor's target is any value.
        private static HgssOverworldReader StandingOn(int mapId, int x, int y)
        {
            const uint anchorTarget = 0x02300000;
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(anchorTarget));
            memory.Place((uint)(anchorTarget + Table.MapIdOffset), BitConverter.GetBytes((ushort)mapId));
            memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes((uint)y << 16));
            return new HgssOverworldReader(memory, Table);
        }

        private static Task WaitForGatehouse(HgssOverworldReader overworld, List<string> presses)
            => NavigatorEvent.WaitForWarpAsync(new ScriptedReader(Free), overworld, macro =>
            {
                presses.Add(macro);
                return Task.CompletedTask;
            }, [97], 1, 7, Stale, CancellationToken.None);

        [Fact]
        public async Task DoorWaitNamesTheNextNodesMap()
        {
            List<string> presses = [];

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(() => WaitForGatehouse(StandingOn(73, 511, 270), presses));

            Assert.Equal("the warp to map 97 (still on map 73)", e.WaitingFor);
            Assert.Empty(presses);
        }

        [Fact]
        public async Task DoorWaitEndsOnTheNextNodesMap()
        {
            List<string> presses = [];

            await WaitForGatehouse(StandingOn(97, 1, 7), presses);

            Assert.Equal(["W.60"], presses);
        }

        // "Repel's effect wore off..." (docs/research/milestone-builder.md, "HeartGold: the message during a navigator walk").
        private static readonly GameSnapshot RepelPrinting = Script(Printing, 2022, 0);
        private static readonly GameSnapshot RepelWaiting = Script(0x02041001, 2022);
        private static readonly GameSnapshot RepelTaken = new(GameScreen.Transitioning, null, null, null, null);
        private static readonly GameSnapshot Battle = new(GameScreen.BattleTurn, null, null, null, null);

        // Each read asks the function; for a state that answers a press.
        private sealed class FuncReader(Func<GameSnapshot> read) : IGameStateReader
        {
            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(read());
        }

        [Fact]
        public async Task HeldWatchEndsOnTheRepelMessageWhilePrinting()
        {
            ScriptedReader reader = new(ChildTask(FadeTask), RepelPrinting, RepelWaiting);

            GameSnapshot end = await NavigatorEvent.WaitForFieldFreeOrSignAsync(reader, Table, CancellationToken.None);

            Assert.Equal(RepelPrinting, end);
        }

        private static Task CloseRepel(IGameStateReader reader, List<string> presses, TimeSpan confirm)
            => RunEncounterEvent.CloseRepelMessageAsync(new FakeBot(Task.CompletedTask) { State = reader, Pressed = presses.Add }, Table, Stale, confirm,
                CancellationToken.None);

        [Fact]
        public async Task RepelMessageGetsOneAAtItsWaitAndNoneWhilePrinting()
        {
            List<string> presses = [];

            await CloseRepel(new ScriptedReader(RepelPrinting, RepelPrinting, RepelWaiting, RepelTaken, Free), presses, TimeSpan.FromSeconds(1));

            Assert.Equal([A], presses);
        }

        [Fact]
        public async Task RepelMessageThatNeverWaitsStopsNamingWhatItSaw()
        {
            List<string> presses = [];

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(
                () => CloseRepel(new ScriptedReader(RepelPrinting), presses, TimeSpan.FromSeconds(1)));

            Assert.Equal("the Repel message to wait for A", e.WaitingFor);
            Assert.Equal(RepelPrinting, e.Last);
            Assert.Empty(presses);
        }

        [Fact]
        public async Task RepelPressDroppedIsSentOnceMore()
        {
            List<string> presses = [];
            int afterTaken = 0;
            FuncReader reader = new(() => presses.Count < 2 ? RepelWaiting : ++afterTaken == 1 ? RepelTaken : Free);

            await CloseRepel(reader, presses, TimeSpan.FromMilliseconds(100));

            Assert.Equal([A, A], presses);
        }

        [Fact]
        public async Task RepelPressNeverTakenStopsAfterThreeNamingTheWait()
        {
            List<string> presses = [];

            GameStateTimeoutException e = await Assert.ThrowsAsync<GameStateTimeoutException>(
                () => CloseRepel(new ScriptedReader(RepelWaiting), presses, TimeSpan.FromMilliseconds(50)));

            Assert.Equal([A, A, A], presses);
            Assert.Equal("the Repel message to take the A", e.WaitingFor);
            Assert.Equal(RepelWaiting, e.Last);
        }

        // The research's Route 32 segment on the committed maps: (474, 294) up to (474, 292), then left to (471, 292).
        private static readonly string DataDirectory = Path.Combine(RepoPaths.AppProject, "Data", "HGSS");
        private const int Route32 = 36;

        // Where the field system points; any RAM value works.
        private const uint FieldSystem = 0x02300000;

        private static FakeMemoryClient OnRoute32(int x, int y)
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            OnMap(memory, Route32);
            MoveTo(memory, x, y);
            return memory;
        }

        private static void OnMap(FakeMemoryClient memory, int mapId)
            => memory.Place((uint)(FieldSystem + Table.MapIdOffset), BitConverter.GetBytes((ushort)mapId));

        private static void MoveTo(FakeMemoryClient memory, int x, int y)
        {
            memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes((uint)y << 16));
        }

        private static FakeBot Route32Bot(FakeMemoryClient memory, RepelWalk walk, Func<FakeBot, Task>? macroDone = null)
        {
            GameProfile hgss = HgssProfile.Create(new PokemonMetadataService(null!), new MapService(DataDirectory), new GraphService());
            FakeBot bot = null!;
            bot = new FakeBot(Task.CompletedTask, hgss)
            {
                State = new FuncReader(walk.Read),
                Overworld = new HgssOverworldReader(memory, Table),
                Pressed = walk.Pressed,
                MacroDone = () => macroDone is null ? Task.CompletedTask : macroDone(bot),
            };
            return bot;
        }

        // A return to start along the nodes, so no graph search runs.
        private static GameState Returning(FakeBot bot, params (int X, int Y)[] nodes)
        {
            GameState state = new()
            {
                Bot = bot,
                Profile = bot.Profile,
                NavigationPath = nodes.Select(n => new NodePoint { Node = new Node { MapId = Route32, X = n.X, Y = n.Y } }).ToList(),
            };
            state.State.Push(EventState.ReturnToStart);
            return state;
        }

        // The facing is never placed, so it reads 0 as in the walk.
        private static string Segment(FakeBot bot, int fromX, int fromY, int toX, int toY)
            => bot.Profile.Pathfinding!.Pathfind(Route32, fromX, fromY, toX, toY, new Capability(), 0)!;

        // Each segment macro moves the trainer to the next of tiles. The box comes up with the segment numbered boxWith: printing
        // twice, then waiting until an A, then the freed environment, then the free field (or a battle once the next segment
        // is sent, when BattleNext).
        private sealed class RepelWalk((int X, int Y)[] tiles, FakeMemoryClient memory, int boxWith)
        {
            private enum Phase { Free, Box, Taken, After }

            // No lock. The segment's racers read concurrently while a macro runs, and there they only advance boxReads, where a
            // lost increment changes nothing; every other change is made by a press, which comes after the racers are cancelled.
            private Phase phase = Phase.Free;
            private int boxReads;
            private int segments;

            public List<string> Presses { get; } = [];
            public bool BattleNext { get; init; }
            public int Segments => segments;

            public void Pressed(string macro)
            {
                Presses.Add(macro);
                if (macro == A)
                {
                    if (phase == Phase.Box) phase = Phase.Taken;
                    return;
                }
                segments++;
                MoveTo(memory, tiles[segments].X, tiles[segments].Y);
                if (segments == boxWith) phase = Phase.Box;
            }

            public GameSnapshot Read()
            {
                switch (phase)
                {
                    case Phase.Box:
                        return boxReads++ < 2 ? RepelPrinting : RepelWaiting;
                    case Phase.Taken:
                        phase = Phase.After;
                        return RepelTaken;
                    case Phase.After:
                        return BattleNext && segments > boxWith ? Battle : Free;
                    default:
                        return Free;
                }
            }
        }

        [Fact]
        public async Task WalkClosesTheRepelMessageAtArrivalThenHandsBack()
        {
            FakeMemoryClient memory = OnRoute32(474, 294);
            RepelWalk walk = new([(474, 294), (471, 292)], memory, boxWith: 1);
            FakeBot bot = Route32Bot(memory, walk);
            GameState state = Returning(bot, (474, 294), (471, 292));

            await new NavigatorEvent(new PokemonStateReader(), _ => Task.CompletedTask).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal([Segment(bot, 474, 294, 471, 292), A], walk.Presses);
            Assert.Equal(EventState.DecideOverworldAction, state.State.Peek());
            Assert.Null(state.NavigationPath);
            Assert.Equal(((short)471, (short)292), await bot.Overworld.GetPositionAsync(CancellationToken.None));
        }

        [Fact]
        public async Task WalkClosesTheRepelMessageAtASegmentsEndBeforeTheNext()
        {
            FakeMemoryClient memory = OnRoute32(474, 294);
            RepelWalk walk = new([(474, 294), (474, 292), (471, 292)], memory, boxWith: 1);
            FakeBot bot = Route32Bot(memory, walk);
            GameState state = Returning(bot, (474, 294), (474, 292), (471, 292));

            await new NavigatorEvent(new PokemonStateReader(), _ => Task.CompletedTask).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal([Segment(bot, 474, 294, 474, 292), A, Segment(bot, 474, 292, 471, 292)], walk.Presses);
            Assert.Equal(EventState.DecideOverworldAction, state.State.Peek());
            Assert.Empty(state.TilePenalties);
        }

        // Nothing is pressed between the free field and the next segment, and a battle on its first step, while its macro is
        // held, is that segment's battle.
        [Fact]
        public async Task BattleOnTheFirstStepAfterTheCloseIsTheWalksBattle()
        {
            FakeMemoryClient memory = OnRoute32(474, 294);
            RepelWalk walk = new([(474, 294), (474, 292), (471, 292)], memory, boxWith: 1) { BattleNext = true };
            // The second segment's macro holds until it is cleared.
            FakeBot bot = Route32Bot(memory, walk, b => walk.Segments == 2 && b.Macros.Count == 0 ? new TaskCompletionSource().Task : Task.CompletedTask);
            GameState state = Returning(bot, (474, 294), (474, 292), (471, 292));

            await new NavigatorEvent(new PokemonStateReader(), _ => Task.CompletedTask).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal([Segment(bot, 474, 294, 474, 292), A, Segment(bot, 474, 292, 471, 292)], walk.Presses);
            Assert.Equal(["clear"], bot.Macros);
            Assert.Equal(EventState.BattleStart, state.State.Peek());
            Assert.Equal(1, state.NavigationPathIndex);
        }

        // A segment that ends on another map: the warp wait runs first and sees the transition, as the door QA scenarios
        // assert, and the segment's end is read only after it, here with the Repel message up after the warp's W.60.
        [Fact]
        public async Task WarpWaitRunsBeforeTheSegmentEndCheck()
        {
            FakeMemoryClient memory = OnRoute32(474, 294);
            List<string> presses = [];
            string? last = null;
            int fadeReads = 0, afterA = 0;
            // Before the walk the free field; after the segment the door fade, then the free field; after the warp's W.60 the
            // Repel message waiting; after the A the freed environment, then the free field.
            GameSnapshot Read() => last switch
            {
                null => Free,
                "W.60" => RepelWaiting,
                A => afterA++ == 0 ? RepelTaken : Free,
                _ => fadeReads++ < 3 ? ChildTask(FadeTask) : Free,
            };
            GameProfile hgss = HgssProfile.Create(new PokemonMetadataService(null!), new MapService(DataDirectory), new GraphService());
            FakeBot bot = new(Task.CompletedTask, hgss)
            {
                State = new FuncReader(Read),
                Overworld = new HgssOverworldReader(memory, Table),
                Pressed = macro =>
                {
                    presses.Add(macro);
                    last = macro;
                    if (macro.StartsWith("M."))
                    {
                        MoveTo(memory, 471, 292);
                        OnMap(memory, 37);
                    }
                },
            };
            GameState state = Returning(bot, (474, 294), (471, 292));
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await new NavigatorEvent(new PokemonStateReader(), _ => Task.CompletedTask).ExecuteWithResultAsync(state, CancellationToken.None);
            }
            finally
            {
                Console.SetOut(console);
            }

            string[] lines = log.ToString().Split(Environment.NewLine);
            int warpSawTheFade = Array.FindIndex(lines, l => l.Contains("[Navigator] Warp to map 37: on map 37, ") && l.Contains("Screen = FieldTask"));
            int lastWarpLine = Array.FindLastIndex(lines, l => l.Contains("[Navigator] Warp to map 37: "));
            int firstEndLine = Array.FindIndex(lines, l => l.Contains("the field after the segment") || l.Contains("Repel message seen"));
            Assert.Equal([Segment(bot, 474, 294, 471, 292), "W.60", A], presses);
            Assert.True(warpSawTheFade >= 0, "the warp wait did not see the transition");
            Assert.True(firstEndLine > lastWarpLine, "the segment's end was read before the warp wait ended");
            Assert.Equal(EventState.DecideOverworldAction, state.State.Peek());
        }

        // The box comes up one tile into the segment while its macro is held: the macro is cleared, one A closes the box, and the
        // segment is walked again from the tile the box came up on, with no tile penalty.
        [Fact]
        public async Task WalkRetriesTheSegmentFromTheTileAfterTheRepelMessage()
        {
            FakeMemoryClient memory = OnRoute32(474, 294);
            RepelWalk walk = new([(474, 294), (474, 293), (471, 292)], memory, boxWith: 1);
            // The first segment's macro holds until it is cleared; every wait after that is done at once.
            FakeBot bot = Route32Bot(memory, walk, b => b.Macros.Count > 0 ? Task.CompletedTask : new TaskCompletionSource().Task);
            GameState state = Returning(bot, (474, 294), (471, 292));

            await new NavigatorEvent(new PokemonStateReader(), _ => Task.CompletedTask).ExecuteWithResultAsync(state, CancellationToken.None);

            Assert.Equal([Segment(bot, 474, 294, 471, 292), A, Segment(bot, 474, 293, 471, 292)], walk.Presses);
            Assert.Equal(["clear"], bot.Macros);
            Assert.Empty(state.TilePenalties);
            Assert.Equal(EventState.DecideOverworldAction, state.State.Peek());
        }
    }
}
