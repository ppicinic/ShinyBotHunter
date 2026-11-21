using Microsoft.Extensions.DependencyInjection;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The RS visit's presses and gates from the #488 plan's table, over scripted snapshots. In the collection that captures
    // the console, so the log lines asserted here land in this test's writer.
    [Collection("macro-registry")]
    public class RsPickupTakeEventTests
    {
        private static readonly RsPointerService Table = RsPointerService.For("AXPE");

        // The RS profile's rule, so these tests assert what an RS visit counts as a Pickup member.
        private static readonly Func<PokemonData, bool> RsPickupMember =
            RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>())).IsPickupMember!;

        private const string Start = "H.Start.2";
        private const string A = "H.A.2";
        private const string B = "H.B.2";
        private const string Down = "H.Down.2";
        private const string Up = "H.Up.2";

        private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);
        // A field task with no task, and the party screen with every field null: passing states inside waits (round 2).
        private static readonly GameSnapshot NoTask = new(GameScreen.FieldTask, null, null, null, null);
        private static readonly GameSnapshot Blank = new(GameScreen.OtherApp, null, null, null, null);

        private static GameSnapshot StartMenu(int cursor, string entries = "0,1,2,4,5,6,7")
            => new(GameScreen.FieldTask, null, null, cursor, Table.StartMenuInputTask, AppState: 0, MenuEntries: entries);

        private static GameSnapshot List(int cursor) => new(GameScreen.OtherApp, null, null, null, Table.FieldPartyMenuTask, AppState: 0, PartyMonIndex: cursor);

        private static GameSnapshot Menu(int cursor, int prompt, string entries)
            => new(GameScreen.OtherApp, null, null, prompt, Table.PokemonMenuTask, AppState: 0, PartyMonIndex: cursor, MenuEntries: entries);

        private static GameSnapshot ItemMenu(int cursor, int prompt) => new(GameScreen.OtherApp, null, null, prompt, Table.PokemonItemMenuTask, AppState: 0, PartyMonIndex: cursor);

        private static GameSnapshot Message(int state) => new(GameScreen.OtherApp, null, null, null, null, TextPrinter: state);

        // Each press gets the next scripted response, the snapshots the game shows after it, the last one repeating; a press
        // past the script repeats what was showing. So a press made early, or one too many, shows up in Macros.
        private sealed class MenuGame(GameSnapshot initial, params GameSnapshot[][] responses) : IGameStateReader
        {
            private readonly Queue<GameSnapshot[]> responses = new(responses);
            private Queue<GameSnapshot> current = new([initial]);

            // Written and read by the test's one async flow; no lock.
            public List<string> Macros { get; } = [];
            public List<GameSnapshot> SeenAtPress { get; } = [];
            // The snapshot the last ReadAsync returned, so a party read can tell what was on screen.
            public GameSnapshot? LastRead { get; private set; }

            public void Run(string macro)
            {
                Macros.Add(macro);
                SeenAtPress.Add(current.Peek());
                current = new(responses.Count > 0 ? responses.Dequeue() : [current.Peek()]);
            }

            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                GameSnapshot read = current.Count > 1 ? current.Dequeue() : current.Peek();
                LastRead = read;
                return Task.FromResult(read);
            }
        }

        // Answers each party read with the next party, the last one repeating.
        private static Func<Task<PokemonState>> Reads(params PokemonState[] parties)
        {
            Queue<PokemonState> queue = new(parties);
            return () => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        // Answers each frame read with the next count, the last one repeating.
        private static Func<Task<uint>> Frames(params uint[] counts)
        {
            Queue<uint> queue = new(counts);
            return () => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        private static PickupTally Tally(List<PickupItemsResponse> pushes) => new(push => { pushes.Add(push); return Task.CompletedTask; });

        private static PokemonData Mudkip(ushort item = 0) => new() { Name = "Mudkip", Species = 258, Ability = 0, HeldItem = item };

        private static PokemonData Zigzagoon(ushort item = 0) => new() { Name = "Zigzagoon", Species = 263, Ability = 0, HeldItem = item, HP = 10 };

        private static PokemonState Party(params PokemonData[] members)
        {
            PokemonState party = new() { PartySize = members.Length };
            members.CopyTo(party.PartyPokemon, 0);
            return party;
        }

        private static PokemonState Empty() => Party(Mudkip(), Zigzagoon(), Zigzagoon(), Zigzagoon(), Zigzagoon(), Zigzagoon());

        private static GameState Deciding()
        {
            GameState state = new() { Milestone = "Route 101" };
            state.State.Push(EventState.DecideOverworldAction);
            return state;
        }

        // The visit with the presses fed to the scripted game through a FakeBot, as RunMacroString feeds the emulator.
        private static Task<string?> Visit(MenuGame game, GameState state, Action clear, Func<Task<PokemonState>> readParty, PickupTally tally,
            TimeSpan? retryAfter = null, TimeSpan? stale = null, Func<Task<uint>>? readFrame = null)
            => RsPickupTakeEvent.TakeAsync(state, new FakeBot(Task.CompletedTask) { Pressed = game.Run }, game, Table, clear, readParty,
                readFrame ?? Frames(1000, 1121), RsPickupMember, tally, "SA-Test", retryAfter ?? Second, stale ?? TwoSeconds, stale ?? TwoSeconds,
                CancellationToken.None);

        private static async Task<(T Result, string Log)> Logged<T>(Func<Task<T>> run)
        {
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                return (await run(), log.ToString());
            }
            finally
            {
                Console.SetOut(console);
            }
        }

        // START and A from the field to the list on cursor 0, with the start cursor already on POKéMON.
        private static readonly GameSnapshot[][] Open = [[Free, StartMenu(1)], [Blank, List(0)]];

        // B on the list, then B on the start menu, back to the field.
        private static GameSnapshot[][] Leave() => [[Blank, NoTask, StartMenu(1)], [Free]];

        [Fact]
        public async Task NothingHeld_OpensLooksAndLeaves()
        {
            MenuGame game = new(Free, [.. Open, .. Leave()]);
            GameState state = Deciding();
            List<string> calls = [];
            List<PickupItemsResponse> pushes = [];
            PickupTally tally = Tally(pushes);

            (string? park, string log) = await Logged(() => Visit(game, state, () => calls.Add("clear"), Reads(Empty()), tally));

            Assert.Null(park);
            Assert.Equal([Start, A, B, B], game.Macros);
            Assert.Equal([Free, StartMenu(1), List(0), StartMenu(1)], game.SeenAtPress);
            Assert.Contains("[Pickup] held 0 of 5 pickup members; nothing to take", log);
            Assert.Contains("[Pickup] START, ", log);
            Assert.Contains("ms: confirmed by the start menu taking keys (", log);
            Assert.Contains("[Pickup] trip took 121 frames, ", log);
            Assert.DoesNotContain("dropped", log);
            Assert.Empty(calls);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
            Assert.Empty(tally.Read());
            Assert.Empty(pushes);
        }

        [Fact]
        public async Task StartCursorElsewhere_WalksToPokemonOneConfirmedPressAtATime()
        {
            MenuGame game = new(Free, [[Free, StartMenu(5)], [StartMenu(4)], [StartMenu(3)], [StartMenu(2)], [StartMenu(1)], [Blank, List(0)], .. Leave()]);

            (string? park, string log) = await Logged(() => Visit(game, Deciding(), () => { }, Reads(Empty()), Tally([])));

            Assert.Null(park);
            Assert.Equal([Start, Up, Up, Up, Up, A, B, B], game.Macros);
            Assert.Equal([Free, StartMenu(5), StartMenu(4), StartMenu(3), StartMenu(2), StartMenu(1), List(0), StartMenu(1)], game.SeenAtPress);
            Assert.Contains("[Pickup] Up on the start menu 5 -> 4, ", log);
            Assert.Contains("confirmed by start cursor 1 (", log);
        }

        [Fact]
        public async Task StartCursorAbovePokemon_WalksDown()
        {
            MenuGame game = new(Free, [[Free, StartMenu(0)], [StartMenu(1)], [Blank, List(0)], .. Leave()]);

            string? park = await Visit(game, Deciding(), () => { }, Reads(Empty()), Tally([]));

            Assert.Null(park);
            Assert.Equal([Start, Down, A, B, B], game.Macros);
            Assert.Equal([Free, StartMenu(0), StartMenu(1), List(0), StartMenu(1)], game.SeenAtPress);
        }

        [Fact]
        public async Task ADroppedPressIsSentOnceMore()
        {
            // The first START is not taken: the field stays free past the retry window; the second opens the menu.
            MenuGame game = new(Free, [[Free], [Free, StartMenu(1)], [Blank, List(0)], .. Leave()]);

            (string? park, string log) = await Logged(() => Visit(game, Deciding(), () => { }, Reads(Empty()), Tally([]),
                retryAfter: TimeSpan.FromMilliseconds(50)));

            Assert.Null(park);
            Assert.Equal([Start, Start, A, B, B], game.Macros);
            Assert.Equal([Free, Free, StartMenu(1), List(0), StartMenu(1)], game.SeenAtPress);
            Assert.Contains("confirmed by the start menu taking keys (" + StartMenu(1) + "); dropped, sent again", log);
        }

        [Fact]
        public async Task APressThatChangedTheSnapshotIsNotSentAgain()
        {
            // START moves the screen to a field task with no task, which never becomes the start menu.
            MenuGame game = new(Free, [[NoTask]]);
            GameState state = Deciding();

            string? park = await Visit(game, state, () => { }, Reads(Empty()), Tally([]),
                retryAfter: TimeSpan.FromMilliseconds(50), stale: TimeSpan.FromMilliseconds(150));

            Assert.StartsWith("Pickup: Waited for the start menu taking keys after START", park);
            Assert.Equal([Start], game.Macros);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task StillHoldsAfterTheVisit_Stops()
        {
            MenuGame game = new(Free, [.. Open, .. Leave()]);
            GameState state = Deciding();
            List<EventState> topAtClear = [];

            string? park = await Visit(game, state, () => topAtClear.Add(state.State.Peek()),
                Reads(Empty(), Party(Mudkip(), Zigzagoon(69))), Tally([]));

            Assert.Equal("Pickup: Zigzagoon still holds PP Up after the take; check the bag and the Pokémon menu.", park);
            Assert.Equal([EventState.DecideOverworldAction], topAtClear);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task WrongScreenAtStart_StopsWithoutPressing()
        {
            MenuGame game = new(StartMenu(1));
            GameState state = Deciding();
            List<string> calls = [];
            int reads = 0;

            string? park = await RsPickupTakeEvent.TakeAsync(state, new FakeBot(Task.CompletedTask) { Pressed = game.Run }, game, Table,
                () => calls.Add("clear"), () => { reads++; return Task.FromResult(Empty()); }, Frames(0), RsPickupMember, Tally([]), "SA-Test",
                Second, TwoSeconds, TimeSpan.FromMilliseconds(50), CancellationToken.None);

            Assert.NotNull(park);
            Assert.StartsWith("Pickup: Waited for the field free before the Pickup visit", park);
            Assert.Empty(game.Macros);
            Assert.Equal(0, reads);
            Assert.Equal(["clear"], calls);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        // Down on the list from cursor from to cursor to, one press each.
        private static GameSnapshot[][] Walk(int from, int to) => [.. Enumerable.Range(from + 1, to - from).Select(c => new[] { List(c) })];

        // One held slot's presses on cursor i as the game answers them: A (3 lag frames still showing the list, then the
        // menu), Down to ITEM, A (a blank frame, then the submenu), Down to TAKE, A (the message printing, then waiting), A
        // (the message closing, a blank frame, the list).
        private static GameSnapshot[][] TakeSlot(int i, string entries = "0,1,2,3")
        {
            int item = Array.IndexOf(entries.Split(','), "2");
            return
            [
                [List(i), Menu(i, 0, entries)],
                .. Enumerable.Range(1, item).Select(p => new[] { Menu(i, p, entries) }),
                [Blank, ItemMenu(i, 0)],
                [ItemMenu(i, 1)],
                [Message(3), Message(6), Message(2), Message(3), Message(5)],
                [Message(2), Blank, List(i)],
            ];
        }

        // The macros one held slot's take sends with ITEM at index item.
        private static string[] TakeMacros(int item = 2) => [A, .. Enumerable.Repeat(Down, item), A, Down, A, A];

        [Fact]
        public async Task TheHeldSlotsAreThoseReadAtTheList()
        {
            MenuGame game = new(Free, [.. Open, .. Walk(0, 2), .. TakeSlot(2), .. Leave()]);
            PokemonState held = Party(Mudkip(), Zigzagoon(), Zigzagoon(23), Zigzagoon());
            List<(int Presses, GameSnapshot? Showing)> reads = [];
            // The held party only with the list on screen after exactly START and A; a read made anywhere else sees nothing held.
            Func<Task<PokemonState>> readParty = () =>
            {
                reads.Add((game.Macros.Count, game.LastRead));
                return Task.FromResult(game.Macros.Count == 2 && game.LastRead == List(0) ? held : Party(Mudkip(), Zigzagoon(), Zigzagoon(), Zigzagoon()));
            };
            PickupTally tally = Tally([]);

            string? park = await Visit(game, Deciding(), () => { }, readParty, tally);

            Assert.Null(park);
            Assert.Equal([Start, A, Down, Down, .. TakeMacros(), B, B], game.Macros);
            // The look at List(0), the per-take read back on List(2), then the read in the free field.
            Assert.Equal([(2, List(0)), (11, List(2)), (13, Free)], reads);
            Assert.Equal([new PickupItemCount(27, "Full Heal", 1)], Assert.Single(tally.Read()).Items);
        }

        [Fact]
        public async Task TakesTwoHeldSlotsWithEveryPressConfirmed()
        {
            MenuGame game = new(Free, [.. Open, .. Walk(0, 1), .. TakeSlot(1), .. Walk(1, 3), .. TakeSlot(3), .. Leave()]);
            GameState state = Deciding();
            List<string> calls = [];
            List<PickupItemsResponse> pushes = [];
            PickupTally tally = Tally(pushes);
            Func<Task<PokemonState>> reads = Reads(
                Party(Mudkip(), Zigzagoon(22), Zigzagoon(), Zigzagoon(110), Zigzagoon(), Zigzagoon()),
                Party(Mudkip(), Zigzagoon(), Zigzagoon(), Zigzagoon(110), Zigzagoon(), Zigzagoon()),
                Empty());

            (string? park, string log) = await Logged(() => Visit(game, state, () => calls.Add("clear"), reads, tally, readFrame: Frames(5000, 5300)));

            Assert.Null(park);
            Assert.Equal([Start, A, Down, A, Down, Down, A, Down, A, A, Down, Down, A, Down, Down, A, Down, A, A, B, B], game.Macros);
            string entries = "0,1,2,3";
            Assert.Equal(
            [
                Free, StartMenu(1), List(0),
                List(1), Menu(1, 0, entries), Menu(1, 1, entries), Menu(1, 2, entries), ItemMenu(1, 0), ItemMenu(1, 1), Message(5),
                List(1), List(2),
                List(3), Menu(3, 0, entries), Menu(3, 1, entries), Menu(3, 2, entries), ItemMenu(3, 0), ItemMenu(3, 1), Message(5),
                List(3), StartMenu(1),
            ], game.SeenAtPress);
            PickupItemsResponse took = Assert.Single(tally.Read());
            Assert.Equal("SA-Test", took.GameFileName);
            Assert.Equal("Route 101", took.Milestone);
            Assert.Equal([new PickupItemCount(92, "Nugget", 1), new PickupItemCount(26, "Super Potion", 1)], took.Items);
            Assert.Equal(2, pushes.Count);
            Assert.Contains("[Pickup] held 2 of 5 pickup members; take", log);
            Assert.Contains("[Pickup] slot 2 (Zigzagoon) menu [0,1,2,3]: ITEM at position 2", log);
            Assert.Contains("[Pickup] took Super Potion (Gen 3 item 22) from slot 2 (Zigzagoon)", log);
            Assert.Contains("[Pickup] took Nugget (Gen 3 item 110) from slot 4 (Zigzagoon)", log);
            Assert.Contains("[Pickup] A on TAKE, ", log);
            Assert.Contains("ms: confirmed by slot 4's take message waiting for a button (", log);
            Assert.Contains("[Pickup] trip took 300 frames, ", log);
            Assert.DoesNotContain("dropped", log);
            Assert.Empty(calls);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        [Fact]
        public async Task AFieldMoveComesFirst_ItemIsFoundAtItsPosition()
        {
            MenuGame game = new(Free, [.. Open, .. Walk(0, 1), .. TakeSlot(1, "10,0,1,2,3"), .. Leave()]);

            (string? park, string log) = await Logged(() => Visit(game, Deciding(), () => { },
                Reads(Party(Mudkip(), Zigzagoon(110)), Party(Mudkip(), Zigzagoon())), Tally([])));

            Assert.Null(park);
            Assert.Equal([Start, A, Down, .. TakeMacros(3), B, B], game.Macros);
            Assert.Contains("[Pickup] slot 2 (Zigzagoon) menu [10,0,1,2,3]: ITEM at position 3", log);
            Assert.Contains("[Pickup] took Nugget (Gen 3 item 110) from slot 2 (Zigzagoon)", log);
        }

        [Fact]
        public async Task AMailHolderIsNotVisited_AndIsTheStillHoldsStop()
        {
            // Orange Mail, 121, in slot 2; a Super Potion in slot 3 is still taken.
            MenuGame game = new(Free, [.. Open, .. Walk(0, 2), .. TakeSlot(2), .. Leave()]);
            GameState state = Deciding();
            PickupTally tally = Tally([]);

            (string? park, string log) = await Logged(() => Visit(game, state, () => { },
                Reads(Party(Mudkip(), Zigzagoon(121), Zigzagoon(22)), Party(Mudkip(), Zigzagoon(121), Zigzagoon())), tally));

            Assert.Equal([Start, A, Down, Down, .. TakeMacros(), B, B], game.Macros);
            Assert.Contains("[Pickup] held 2 of 2 pickup members; take", log);
            Assert.Contains("[Pickup] slot 2 (Zigzagoon) holds mail; not visited", log);
            Assert.Equal([new PickupItemCount(26, "Super Potion", 1)], Assert.Single(tally.Read()).Items);
            Assert.Equal($"Pickup: Zigzagoon still holds {PickupTally.ItemName(PickupTally.FromGen3(121))} after the take; check the bag and the Pokémon menu.", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task AMenuWithNoItem_IsClosedWithBAndTheSlotLeft()
        {
            // Slot 2's menu has no ITEM entry; slot 3's item is still taken.
            MenuGame game = new(Free, [.. Open, .. Walk(0, 1), [List(1), Menu(1, 0, "0,1,3")], [List(1)], .. Walk(1, 2), .. TakeSlot(2), .. Leave()]);
            GameState state = Deciding();
            PickupTally tally = Tally([]);

            (string? park, string log) = await Logged(() => Visit(game, state, () => { },
                Reads(Party(Mudkip(), Zigzagoon(22), Zigzagoon(23)), Party(Mudkip(), Zigzagoon(22), Zigzagoon())), tally));

            Assert.Equal([Start, A, Down, A, B, Down, .. TakeMacros(), B, B], game.Macros);
            Assert.Equal(Menu(1, 0, "0,1,3"), game.SeenAtPress[4]);
            Assert.Contains("[Pickup] slot 2 (Zigzagoon) menu [0,1,3]: no ITEM; left", log);
            Assert.Contains("confirmed by the party list taking keys on cursor 1 (", log);
            Assert.Equal([new PickupItemCount(27, "Full Heal", 1)], Assert.Single(tally.Read()).Items);
            Assert.Equal("Pickup: Zigzagoon still holds Super Potion after the take; check the bag and the Pokémon menu.", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task AFullPocket_DismissesRecordsNothingAndStops()
        {
            // Revive, 24, not in the bag, with the pocket full: the message is dismissed and the slot still holds it.
            MenuGame game = new(Free, [.. Open, .. Walk(0, 1), .. TakeSlot(1), .. Leave()]);
            GameState state = Deciding();
            List<PickupItemsResponse> pushes = [];
            PickupTally tally = Tally(pushes);

            (string? park, string log) = await Logged(() => Visit(game, state, () => { }, Reads(Party(Mudkip(), Zigzagoon(24))), tally));

            Assert.Equal([Start, A, Down, .. TakeMacros(), B, B], game.Macros);
            Assert.Contains("[Pickup] slot 2 (Zigzagoon) still holds Revive (Gen 3 item 24) after TAKE; left for the stop", log);
            Assert.DoesNotContain("[Pickup] took", log);
            Assert.Empty(tally.Read());
            Assert.Empty(pushes);
            Assert.Equal("Pickup: Zigzagoon still holds Revive after the take; check the bag and the Pokémon menu.", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task AnEggAndAFaintedMemberAreWalkedOver()
        {
            PokemonData egg = Zigzagoon();
            egg.IVs = 1u << 30;
            PokemonData fainted = Zigzagoon(22);
            fainted.HP = 0;
            MenuGame game = new(Free, [.. Open, .. Walk(0, 2), .. TakeSlot(2), .. Walk(2, 3), .. TakeSlot(3), .. Leave()]);
            PickupTally tally = Tally([]);

            (string? park, string log) = await Logged(() => Visit(game, Deciding(), () => { },
                Reads(Party(Mudkip(), egg, fainted, Zigzagoon(23)), Party(Mudkip(), egg, Zigzagoon(), Zigzagoon(23)), Party(Mudkip(), egg, Zigzagoon(), Zigzagoon())),
                tally));

            Assert.Null(park);
            Assert.Contains("[Pickup] held 2 of 2 pickup members; take", log);
            Assert.Equal([Start, A, Down, Down, .. TakeMacros(), Down, .. TakeMacros(), B, B], game.Macros);
            Assert.Equal([List(0), List(1)], game.SeenAtPress.Where((_, k) => game.Macros[k] == Down).Take(2));
            Assert.Equal(List(2), game.SeenAtPress[4]);
            Assert.Equal(List(2), game.SeenAtPress[11]);
            Assert.Equal([new PickupItemCount(27, "Full Heal", 1), new PickupItemCount(26, "Super Potion", 1)], Assert.Single(tally.Read()).Items);
        }

        // Every press of a visit that takes slot 2 (ITEM at 2) and closes slot 3's menu (no ITEM), with the start cursor on
        // 0, and the game's answer to each.
        private static readonly GameSnapshot[][] EveryStep =
        [
            [Free, StartMenu(0)],
            [StartMenu(1)],
            [Blank, List(0)],
            [List(1)],
            .. TakeSlot(1),
            [List(2)],
            [Menu(2, 0, "0,1,3")],
            [List(2)],
            .. Leave(),
        ];

        // A row per wait of the #488 plan's table, by the index of the press whose answer never comes; -1 is the opening
        // wait, with no press.
        [Theory]
        [InlineData(-1, "Pickup: Waited for the field free before the Pickup visit")]
        [InlineData(0, "Pickup: Waited for the start menu taking keys after START")]
        [InlineData(1, "Pickup: Waited for start cursor 1 after Down on the start menu 0 -> 1")]
        [InlineData(2, "Pickup: Waited for the party list taking keys on cursor 0 after A on POKéMON")]
        [InlineData(3, "Pickup: Waited for the party cursor on 1 after Down on the party list 0 -> 1")]
        [InlineData(4, "Pickup: Waited for slot 2's Pokémon menu after A on cursor 1")]
        [InlineData(5, "Pickup: Waited for the menu cursor on 1 after Down on slot 2's menu 0 -> 1")]
        [InlineData(7, "Pickup: Waited for slot 2's ITEM submenu after A on ITEM")]
        [InlineData(8, "Pickup: Waited for the submenu cursor on TAKE after Down on GIVE")]
        [InlineData(9, "Pickup: Waited for slot 2's take message waiting for a button after A on TAKE")]
        [InlineData(10, "Pickup: Waited for the party list taking keys on cursor 1 after A at slot 2's take message")]
        [InlineData(13, "Pickup: Waited for the party list taking keys on cursor 2 after B on slot 3's menu")]
        [InlineData(14, "Pickup: Waited for the start menu taking keys after B on the party list")]
        [InlineData(15, "Pickup: Waited for the field free after B on the start menu")]
        public async Task AStaleStepStops_NamingTheStepAndTheSnapshot(int stuck, string waitedFor)
        {
            // The gate repeats after the stuck press, so it is sent once more after the retry window and then goes stale.
            GameSnapshot gate = stuck switch { -1 => NoTask, 0 => Free, _ => EveryStep[stuck - 1][^1] };
            MenuGame game = stuck == -1 ? new(NoTask) : new(Free, [.. EveryStep.Take(stuck), [gate]]);
            GameState state = Deciding();
            List<EventState> topAtClear = [];

            string? park = await Visit(game, state, () => topAtClear.Add(state.State.Peek()),
                Reads(Party(Mudkip(), Zigzagoon(22), Zigzagoon(23)), Party(Mudkip(), Zigzagoon(), Zigzagoon(23))), Tally([]),
                retryAfter: TimeSpan.FromMilliseconds(50), stale: TimeSpan.FromMilliseconds(150));

            Assert.StartsWith(waitedFor, park);
            Assert.Contains(gate.ToString(), park);
            Assert.Equal(stuck == -1 ? 0 : stuck + 2, game.Macros.Count);
            if (stuck >= 0)
            {
                Assert.Equal(game.Macros[stuck], game.Macros[stuck + 1]);
            }
            Assert.Equal([EventState.DecideOverworldAction], topAtClear);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task AStoppedVisitRecordsOnlyConfirmedTakes()
        {
            // Slot 4's message is never answered: it stays waiting for a button.
            GameSnapshot[][] slot4 = TakeSlot(3);
            MenuGame game = new(Free, [.. Open, .. Walk(0, 1), .. TakeSlot(1), .. Walk(1, 3), .. slot4[..^1], [Message(5)]]);
            PickupTally tally = Tally([]);

            string? park = await Visit(game, Deciding(), () => { },
                Reads(Party(Mudkip(), Zigzagoon(22), Zigzagoon(), Zigzagoon(110)), Party(Mudkip(), Zigzagoon(), Zigzagoon(), Zigzagoon(110))), tally,
                retryAfter: TimeSpan.FromMilliseconds(50), stale: TimeSpan.FromMilliseconds(150));

            Assert.StartsWith("Pickup: Waited for the party list taking keys on cursor 3 after A at slot 4's take message", park);
            Assert.Equal([new PickupItemCount(26, "Super Potion", 1)], Assert.Single(tally.Read()).Items);
        }

        [Fact]
        public void RsProfile_CreatesTheRsVisit()
        {
            GameProfile profile = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()));
            var provider = new ServiceCollection().AddSingleton<PokemonStateReader>().AddSingleton(new PickupTally(_ => Task.CompletedTask)).BuildServiceProvider();

            Assert.IsType<RsPickupTakeEvent>(profile.CreatePickupVisit!(provider, new FakeBot(Task.CompletedTask, profile)));
        }
    }
}
