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

        [Fact]
        public void RsProfile_CreatesTheRsVisit()
        {
            GameProfile profile = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()));
            var provider = new ServiceCollection().AddSingleton<PokemonStateReader>().AddSingleton(new PickupTally(_ => Task.CompletedTask)).BuildServiceProvider();

            Assert.IsType<RsPickupTakeEvent>(profile.CreatePickupVisit!(provider, new FakeBot(Task.CompletedTask, profile)));
        }
    }
}
