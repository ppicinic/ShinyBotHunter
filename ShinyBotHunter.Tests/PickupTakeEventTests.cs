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

            public Task Run(string macro)
            {
                Macros.Add(macro);
                SeenAtPress.Add(current.Peek());
                current = new(responses.Count > 0 ? responses.Dequeue() : [current.Peek()]);
                return Task.CompletedTask;
            }

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(current.Count > 1 ? current.Dequeue() : current.Peek());
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
            GameState state = new();
            state.State.Push(EventState.DecideOverworldAction);
            return state;
        }

        // One held slot's four presses as the game answers them: the context menu on the slot, the Item menu, the message
        // printing then at the arrow, the list again.
        private static GameSnapshot[][] TakeSlot(int i) =>
        [
            [App(1, 0), App(2, i)],
            [App(15, i)],
            [App(17, i, 0), App(17, i, 2)],
            [App(17, i, 2), App(1, i)],
        ];

        [Fact]
        public async Task TakesTwoHeldSlotsWithEveryPressConfirmed()
        {
            MenuGame game = new(Free,
                [[StartMenu], [App(0), App(1, 0)], .. TakeSlot(1), .. TakeSlot(3), [App(1, 3), App(32, 3), Free]]);
            PokemonState party = Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17), Member("Aipom", 53, 0), Member("Aipom", 53, 26));
            GameState state = Deciding();
            List<string> calls = [];

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(), party, [1, 3], game.Run, () => calls.Add("clear"),
                () => Task.FromResult(Empty()), CancellationToken.None);

            Assert.Null(park);
            Assert.Equal([X, Pokemon, Slot[1], Item, Take, A, Slot[3], Item, Take, A, Cancel], game.Macros);
            Assert.Empty(calls);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        [Fact]
        public async Task PressesAOnlyAtTheArrow()
        {
            MenuGame game = new(Free,
                [[StartMenu], [App(1, 0)], [App(2, 0)], [App(15, 0)], [App(17, 0, 0), App(17, 0, 0), App(17, 0, 0), App(17, 0, 2)], [App(1, 0)], [Free]]);
            PokemonState party = Party(Member("Aipom", 53, 17));

            string? park = await PickupTakeEvent.TakeAsync(Deciding(), game, Table, Config(), party, [0], game.Run, () => { },
                () => Task.FromResult(Party(Member("Aipom", 53, 0))), CancellationToken.None);

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

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(stepStaleMs: 50), Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17)),
                [1], game.Run, () => topAtClear.Add(state.State.Peek()), () => Task.FromResult(Empty()), CancellationToken.None);

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

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(fieldFreeStaleMs: 50), Party(Member("Aipom", 53, 17)), [0],
                game.Run, () => calls.Add("clear"), () => Task.FromResult(Empty()), CancellationToken.None);

            Assert.NotNull(park);
            Assert.StartsWith("Pickup: Waited for the field free before the Pickup trip", park);
            Assert.Empty(game.Macros);
            Assert.Equal(["clear"], calls);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        [Fact]
        public async Task ItemStillHeldAfterTheTripStops()
        {
            MenuGame game = new(Free, [[StartMenu], [App(1, 0)], .. TakeSlot(0), [Free]]);
            GameState state = Deciding();
            List<EventState> topAtClear = [];

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(), Party(Member("Aipom", 53, 17)), [0], game.Run,
                () => topAtClear.Add(state.State.Peek()), () => Task.FromResult(Party(Member("Aipom", 53, 17))), CancellationToken.None);

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

            string? park = await PickupTakeEvent.TakeAsync(state, game, Table, Config(), party, [1], game.Run, () => { },
                () => Task.FromResult(party), CancellationToken.None);

            Assert.Equal([X, Pokemon, Cancel], game.Macros);
            Assert.Equal("Pickup: Aipom still holds item 137 after the take; check the Pokémon menu.", park);
            Assert.Equal(EventState.Waiting, state.State.Peek());
        }

        private static GameState PickupOn() => new() { Pickup = true };

        [Fact]
        public void AllHeldIsDue()
        {
            PokemonState party = Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17), Member("Aipom", 53, 26), Member("Aipom", 53, 79));

            Assert.Equal([1, 2, 3], OrchestratorEvent.PickupSlotsDue(PickupOn(), party, 0));
        }

        [Fact]
        public void PartialIsNotDueAtTheDefault()
        {
            PokemonState party = Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17), Member("Aipom", 53, 0), Member("Aipom", 53, 79));

            Assert.Empty(OrchestratorEvent.PickupSlotsDue(PickupOn(), party, 0));
        }

        [Fact]
        public void PartialIsDueAtTheThreshold()
        {
            PokemonState party = Party(Member("Typhlosion", 66, 0), Member("Aipom", 53, 17), Member("Aipom", 53, 0), Member("Aipom", 53, 79));

            Assert.Equal([1, 3], OrchestratorEvent.PickupSlotsDue(PickupOn(), party, 2));
            Assert.Empty(OrchestratorEvent.PickupSlotsDue(PickupOn(), party, 3));
        }

        [Fact]
        public void PickupOffIsNeverDue()
        {
            PokemonState party = Party(Member("Aipom", 53, 17), Member("Aipom", 53, 26));

            Assert.Empty(OrchestratorEvent.PickupSlotsDue(new GameState(), party, 0));
        }

        [Fact]
        public void ANonPickupHolderIsNotCounted()
        {
            // A lead holding Leftovers (234) is neither a member nor a slot to visit.
            PokemonState party = Party(Member("Typhlosion", 66, 234), Member("Aipom", 53, 17), Member("Aipom", 53, 26));

            Assert.Equal([1, 2], OrchestratorEvent.PickupSlotsDue(PickupOn(), party, 0));
            Assert.Empty(OrchestratorEvent.PickupSlotsDue(PickupOn(), Party(Member("Typhlosion", 66, 234), Member("Aipom", 53, 0)), 1));
        }
    }
}
