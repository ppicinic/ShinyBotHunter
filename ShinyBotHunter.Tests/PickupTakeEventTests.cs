using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The trip's touches and states from docs/research/pickup.md, "The loop, as the probe ran it".
    public class PickupTakeEventTests
    {
        private static readonly HgssPointerService Table = new();

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);
        private static readonly GameSnapshot StartMenu = new(GameScreen.FieldTask, null, null, null, 0x0203D1C1);

        private static GameSnapshot App(int state, int? index = null, int? printer = null)
            => new(GameScreen.OtherApp, null, null, null, null, TextPrinter: printer, AppState: state, PartyMonIndex: index);

        private const string X = "T.X";
        private const string A = "T.A";
        private const string Pokemon = "W.5;S.44.77.3";
        private const string Item = "W.5;S.191.106.3";
        private const string Take = "W.5;S.191.66.3";
        private const string Cancel = "W.5;S.227.178.3";
        private static readonly string[] Slot = ["W.5;S.63.27.3", "W.5;S.191.27.3", "W.5;S.63.77.3", "W.5;S.191.77.3", "W.5;S.63.123.3", "W.5;S.191.123.3"];

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

            public Task Run(string macro)
            {
                Macros.Add(macro);
                SeenAtPress.Add(current.Peek());
                current = new(responses.Count > 0 ? responses.Dequeue() : [current.Peek()]);
                return Task.CompletedTask;
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

        private static PickupConfig Config(int stepStaleMs = 2000, int fieldFreeStaleMs = 2000) => new()
        {
            PokemonTouch = [44, 77],
            SlotTouches = [[63, 27], [191, 27], [63, 77], [191, 77], [63, 123], [191, 123]],
            ItemTouch = [191, 106],
            TakeTouch = [191, 66],
            CancelTouch = [227, 178],
            StartMenuStaleMs = 2000,
            PartyMenuStaleMs = 2000,
            StepStaleMs = stepStaleMs,
            FieldFreeStaleMs = fieldFreeStaleMs,
        };

        private static PickupTally Tally(List<PickupItemsResponse> pushes) => new(push => { pushes.Add(push); return Task.CompletedTask; });

        private static PokemonData Member(string name, byte ability, ushort item) => new() { Name = name, Ability = ability, HeldItem = item };

        private static PokemonState Party(params PokemonData[] members)
        {
            PokemonState party = new() { PartySize = members.Length };
            members.CopyTo(party.PartyPokemon, 0);
            return party;
        }

        private static PokemonState Empty() => Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 0), Member("Aipom", 53, 0), Member("Aipom", 53, 0));

        private static GameState Deciding()
        {
            GameState state = new() { Milestone = "Route 33" };
            state.State.Push(EventState.DecideOverworldAction);
            return state;
        }

        // One held slot's four presses as the game answers them: the context menu on the slot, the Item menu, the message
        // printing then at the arrow, the list again. Each answer starts on a snapshot that does not confirm the press, so a
        // press made without its wait is seen on that one in SeenAtPress.
        private static GameSnapshot[][] TakeSlot(int i) =>
        [
            [App(1, 0), App(2, i)],
            [App(2, i), App(15, i)],
            [App(17, i, 0), App(17, i, 2)],
            [App(17, i, 2), App(1, i)],
        ];

        [Fact]
        public async Task TakesTwoHeldSlotsWithEveryPressConfirmed()
        {
            MenuGame game = new(Free,
                [[Free, StartMenu], [App(0), App(1, 0)], .. TakeSlot(1), .. TakeSlot(3), [App(1, 3), App(32, 3), Free]]);
            PokemonState held = Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17), Member("Aipom", 53, 0), Member("Aipom", 53, 26));
            GameState state = Deciding();
            List<string> calls = [];
            List<(int Presses, GameSnapshot? Showing)> reads = [];
            // The held party only with the list on screen after exactly X and POKéMON; a read made anywhere else sees nothing held.
            Func<Task<PokemonState>> readParty = () =>
            {
                reads.Add((game.Macros.Count, game.LastRead));
                return Task.FromResult(game.Macros.Count == 2 && game.LastRead == App(1, 0) ? held : Empty());
            };

            List<PickupItemsResponse> pushes = [];
            PickupTally tally = Tally(pushes);

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(), game.Run, () => calls.Add("clear"), readParty, tally, "HG-Test", CancellationToken.None);

            Assert.Null(park);
            PickupItemsResponse took = Assert.Single(tally.Read());
            Assert.Equal("HG-Test", took.GameFileName);
            Assert.Equal("Route 33", took.Milestone);
            Assert.Equal([new PickupItemCount(17, "Potion", 1), new PickupItemCount(26, "Super Potion", 1)], took.Items);
            Assert.Equal(2, pushes.Count);
            // The decision read at the list before any slot touch, then the re-read after CANCEL.
            Assert.Equal([(2, App(1, 0)), (11, Free)], reads);
            Assert.Equal([X, Pokemon, Slot[1], Item, Take, A, Slot[3], Item, Take, A, Cancel], game.Macros);
            // The confirming snapshot each press's wait saw before the press.
            Assert.Equal([Free, StartMenu, App(1, 0), App(2, 1), App(15, 1), App(17, 1, 2), App(1, 1), App(2, 3), App(15, 3), App(17, 3, 2), App(1, 3)], game.SeenAtPress);
            Assert.Empty(calls);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        [Fact]
        public async Task AStoppedVisitRecordsOnlyConfirmedTakes()
        {
            // Slot 4's A is not answered: the take message stays up.
            MenuGame game = new(Free,
                [[Free, StartMenu], [App(0), App(1, 0)], .. TakeSlot(1), [App(1, 1), App(2, 3)], [App(2, 3), App(15, 3)], [App(17, 3, 0), App(17, 3, 2)], [App(17, 3, 2)]]);
            List<PickupItemsResponse> pushes = [];
            PickupTally tally = Tally(pushes);

            string? park = await PickupTakeEvent.TakeAsync(Deciding(), game, Table, Config(stepStaleMs: 50), game.Run, () => { },
                Reads(Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17), Member("Aipom", 53, 0), Member("Aipom", 53, 26)), Empty()),
                tally, "HG-Test", CancellationToken.None);

            Assert.StartsWith("Pickup: Waited for the party list after slot 4's take", park);
            PickupItemsResponse took = Assert.Single(tally.Read());
            Assert.Equal([new PickupItemCount(17, "Potion", 1)], took.Items);
            Assert.Single(pushes);
        }

        [Fact]
        public async Task PressesAOnlyAtTheArrow()
        {
            MenuGame game = new(Free,
                [[StartMenu], [App(1, 0)], [App(2, 0)], [App(15, 0)], [App(17, 0, 0), App(17, 0, 0), App(17, 0, 0), App(17, 0, 2)], [App(1, 0)], [Free]]);
            string? park = await PickupTakeEvent.TakeAsync(Deciding(), game, Table, Config(), game.Run, () => { },
                Reads(Party(Member("Aipom", 53, 17)), Party(Member("Aipom", 53, 0))), Tally([]), "HG-Test", CancellationToken.None);

            Assert.Null(park);
            Assert.Single(game.Macros, A);
            Assert.Equal(App(17, 0, 2), game.SeenAtPress[game.Macros.IndexOf(A)]);
        }

        [Fact]
        public async Task StaleStepStopsNamingStepAndSnapshot()
        {
            // The ITEM touch is dropped: the context menu stays up.
            MenuGame game = new(Free, [[StartMenu], [App(1, 0)], [App(2, 1)], [App(2, 1)]]);
            GameState state = Deciding();
            List<EventState> topAtClear = [];

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(stepStaleMs: 50), game.Run, () => topAtClear.Add(state.State.Peek()),
                Reads(Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17)), Empty()), Tally([]), "HG-Test", CancellationToken.None);

            Assert.NotNull(park);
            Assert.StartsWith("Pickup: Waited for slot 2's Item menu", park);
            Assert.Contains("AppState = 2", park);
            Assert.Equal([X, Pokemon, Slot[1], Item], game.Macros);
            Assert.Equal([EventState.DecideOverworldAction], topAtClear);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task WrongScreenAtStartStopsWithoutPressing()
        {
            MenuGame game = new(StartMenu);
            GameState state = Deciding();
            List<string> calls = [];
            int reads = 0;

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(fieldFreeStaleMs: 50), game.Run, () => calls.Add("clear"),
                () => { reads++; return Task.FromResult(Party(Member("Aipom", 53, 17))); }, Tally([]), "HG-Test", CancellationToken.None);

            Assert.NotNull(park);
            Assert.StartsWith("Pickup: Waited for the field free before the Pickup trip", park);
            Assert.Empty(game.Macros);
            Assert.Equal(0, reads);
            Assert.Equal(["clear"], calls);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task ItemStillHeldAfterTheTripStops()
        {
            MenuGame game = new(Free, [[StartMenu], [App(1, 0)], .. TakeSlot(0), [Free]]);
            GameState state = Deciding();
            List<EventState> topAtClear = [];

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(), game.Run,
                () => topAtClear.Add(state.State.Peek()), Reads(Party(Member("Aipom", 53, 17))), Tally([]), "HG-Test", CancellationToken.None);

            Assert.Equal("Pickup: Aipom still holds item 17 after the take; check the Pokémon menu.", park);
            Assert.Equal([EventState.DecideOverworldAction], topAtClear);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task MailIsNotVisitedAndStopsAsStillHeld()
        {
            // Grass Mail, 137.
            MenuGame game = new(Free, [[StartMenu], [App(1, 0)], [Free]]);
            PokemonState party = Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 137));
            GameState state = Deciding();

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(), game.Run, () => { }, Reads(party), Tally([]), "HG-Test", CancellationToken.None);

            Assert.Equal([X, Pokemon, Cancel], game.Macros);
            Assert.Equal("Pickup: Aipom still holds item 137 after the take; check the Pokémon menu.", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task NothingHeldIsXPokemonCancel()
        {
            MenuGame game = new(Free, [[Free, StartMenu], [App(0), App(1, 0)], [App(1, 0), App(32, 0), Free]]);
            GameState state = Deciding();
            List<string> calls = [];

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(), game.Run, () => calls.Add("clear"), Reads(Empty()), Tally([]), "HG-Test", CancellationToken.None);

            Assert.Null(park);
            Assert.Equal([X, Pokemon, Cancel], game.Macros);
            Assert.Equal([Free, StartMenu, App(1, 0)], game.SeenAtPress);
            Assert.Empty(calls);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        [Fact]
        public async Task ANonPickupHolderIsNotVisited()
        {
            // A lead holding Leftovers (234) is neither a member nor a slot to visit.
            MenuGame game = new(Free, [[Free, StartMenu], [App(0), App(1, 0)], .. TakeSlot(1), [App(1, 1), App(32, 1), Free]]);

            string? park = await PickupTakeEvent.TakeAsync(Deciding(), game, Table, Config(), game.Run, () => { },
                Reads(Party(Member("Typhlosion", 66, 234), Member("Aipom", 53, 17)), Party(Member("Typhlosion", 66, 234), Member("Aipom", 53, 0))),
                Tally([]), "HG-Test", CancellationToken.None);

            Assert.Null(park);
            Assert.Equal([X, Pokemon, Slot[1], Item, Take, A, Cancel], game.Macros);
        }
    }
}
