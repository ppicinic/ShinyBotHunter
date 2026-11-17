using Microsoft.Extensions.DependencyInjection;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The orchestrator's seams, through its static methods: the orchestrator itself resolves services
    /// that need a database, so it is not built here.
    /// </summary>
    public class OrchestratorEventTests
    {
        private const uint TargetPid = 0x12345678;
        private const ushort TargetSpecies = 25;

        // A party block is the PID then the species, little-endian.
        private sealed class PidSpeciesParser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw) => new PokemonData
            {
                PID = BitConverter.ToUInt32(raw, 0),
                Species = BitConverter.ToUInt16(raw, 4),
            };
        }

        private static byte[] Block(uint pid, ushort species) => [.. BitConverter.GetBytes(pid), .. BitConverter.GetBytes(species)];

        // RS as it declares itself for this event: wild encounters, box reads, no navigation. The save goes to the given list.
        private static GameProfile NoNavigationProfile(List<string> calls) => new(
            GameFlavor.RS,
            new GameCapabilities(
                HasOverworldNavigation: false,
                HasWildEncounters: true,
                HasTraining: false,
                HasEggs: false,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: false),
            [1],
            (_, _) => new RsPointerService(),
            new PidSpeciesParser(),
            battleParser: null,
            createOverworldReader: (memory, t) => new RsOverworldReader(memory, (RsPointerService)t),
            createStateReader: (memory, t) => new RsStateReader(memory, (RsPointerService)t),
            createBattleEvent: _ => throw new NotImplementedException(),
            saveGame: (_, _) =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            });

        private static GameState StateFor(FakeBot bot, ITarget target, params EventState[] stack)
        {
            GameState state = new() { Bot = bot, Profile = bot.Profile, Target = target, Milestone = "Phase" };
            foreach (EventState s in stack)
            {
                state.State.Push(s);
            }
            return state;
        }

        private static PersistPokemonData Caught() => new()
        {
            PokemonData = new PokemonData { PID = TargetPid, Species = TargetSpecies },
            Phase = "Phase",
            GameFileName = "fake.nds",
        };

        [Fact]
        public async Task LogEncounterAndAskTarget_TargetIsLoggedAlertedAndWaits()
        {
            FakeBot bot = new(Task.CompletedTask);
            GameState state = StateFor(bot, new ShinyTarget(), EventState.PokemonDataPending);
            // PID, TID and SID all zero: shiny.
            PokemonData pokemon = new();
            List<(string Call, PersistPokemonData Data)> calls = [];

            bool target = await OrchestratorEvent.LogEncounterAndAskTargetAsync(state, pokemon,
                p => { calls.Add(("add", p)); return Task.CompletedTask; },
                p => { calls.Add(("notify", p)); return Task.CompletedTask; });

            Assert.True(target);
            Assert.Equal(["add", "notify"], calls.Select(c => c.Call));
            PersistPokemonData persisted = calls[0].Data;
            Assert.Same(persisted, calls[1].Data);
            Assert.Same(persisted, state.TargetPokemon);
            Assert.Equal(GameFlavor.HGSS, persisted.Game);
            Assert.Equal("Phase", persisted.Phase);
            Assert.Equal("fake.nds", persisted.GameFileName);
            Assert.Same(pokemon, persisted.PokemonData);
            Assert.Equal([EventState.Waiting], state.State);
        }

        [Fact]
        public async Task LogEncounterAndAskTarget_NotTargetIsOnlyLogged()
        {
            FakeBot bot = new(Task.CompletedTask);
            GameState state = StateFor(bot, new NoTarget(), EventState.PokemonDataPending);
            List<string> calls = [];

            bool target = await OrchestratorEvent.LogEncounterAndAskTargetAsync(state, new PokemonData(),
                _ => { calls.Add("add"); return Task.CompletedTask; },
                _ => { calls.Add("notify"); return Task.CompletedTask; });

            Assert.False(target);
            Assert.Equal(["add"], calls);
            Assert.Null(state.TargetPokemon);
            Assert.Equal([EventState.PokemonDataPending], state.State);
        }

        [Fact]
        public async Task LogEncounterAndAskTarget_IllegalIsLoggedMarkedAndNotAsked()
        {
            FakeBot bot = new(Task.CompletedTask);
            GameState state = StateFor(bot, new ShinyTarget(), EventState.PokemonDataPending);
            List<(string Call, PersistPokemonData Data)> calls = [];

            // PID, TID and SID all zero: shiny.
            bool target = await OrchestratorEvent.LogEncounterAndAskTargetAsync(state, new PokemonData(),
                p => { calls.Add(("add", p)); return Task.CompletedTask; },
                p => { calls.Add(("notify", p)); return Task.CompletedTask; },
                legal: false);

            Assert.False(target);
            Assert.Equal(["add"], calls.Select(c => c.Call));
            Assert.True(calls[0].Data.LegalityFailed);
            Assert.Null(state.TargetPokemon);
            Assert.Equal([EventState.PokemonDataPending], state.State);
        }

        [Fact]
        public async Task CatchCheck_Rs_FindsTheTargetInThePartyAndSaves()
        {
            List<string> calls = [];
            FakeBot bot = new(Task.CompletedTask, NoNavigationProfile(calls)) { PartyBlocks = [Block(1, 1), Block(TargetPid, TargetSpecies)] };
            GameState state = StateFor(bot, new ShinyTarget(), EventState.Resume);
            state.TargetPokemon = Caught();

            bool checkedCatch = await OrchestratorEvent.CheckForCatchAsync(state, new PokemonStateReader(), new ServiceCollection().BuildServiceProvider(),
                _ => { calls.Add("caught"); return Task.CompletedTask; },
                _ => { calls.Add("milestone"); return Task.CompletedTask; });

            Assert.True(checkedCatch);
            Assert.Equal(["save", "caught", "milestone"], calls);
            Assert.Equal(1, bot.PartyReads);
            Assert.Null(state.TargetPokemon);
        }

        [Fact]
        public async Task CatchCheck_Rs_TargetMissing_DoesNotSave()
        {
            List<string> calls = [];
            FakeBot bot = new(Task.CompletedTask, NoNavigationProfile(calls)) { PartyBlocks = [Block(1, 1), Block(TargetPid, TargetSpecies + 1)] };
            GameState state = StateFor(bot, new ShinyTarget(), EventState.Resume);
            state.TargetPokemon = Caught();

            bool checkedCatch = await OrchestratorEvent.CheckForCatchAsync(state, new PokemonStateReader(), new ServiceCollection().BuildServiceProvider(),
                _ => { calls.Add("caught"); return Task.CompletedTask; },
                _ => { calls.Add("milestone"); return Task.CompletedTask; });

            Assert.True(checkedCatch);
            Assert.Equal(["milestone"], calls);
            Assert.Null(state.TargetPokemon);
        }

        [Fact]
        public async Task CatchCheck_BoxProfile_StillReadsTheBoxes()
        {
            List<string> calls = [];
            GameProfile hgss = HgssProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()), new GraphService());
            // HGSS with the stub parser and a recording save. FakeBot reads zeros, so every box slot is empty.
            GameProfile profile = new(hgss.Flavor, hgss.Capabilities, [0], hgss.Addresses, new PidSpeciesParser(), hgss.BattleParser,
                hgss.CreateOverworldReader, hgss.Maps, hgss.Graph, hgss.Pathfinding, hgss.CreateStateReader, hgss.CreateBattleEvent,
                (_, _) => { calls.Add("save"); return Task.CompletedTask; });
            FakeBot bot = new(Task.CompletedTask, profile) { PartyBlocks = [Block(TargetPid, TargetSpecies)] };
            GameState state = StateFor(bot, new ShinyTarget(), EventState.Resume);
            state.TargetPokemon = Caught();

            bool checkedCatch = await OrchestratorEvent.CheckForCatchAsync(state, new PokemonStateReader(), new ServiceCollection().BuildServiceProvider(),
                _ => { calls.Add("caught"); return Task.CompletedTask; },
                _ => { calls.Add("milestone"); return Task.CompletedTask; });

            Assert.True(checkedCatch);
            Assert.Equal(["save", "caught", "milestone"], calls);
        }

        private static readonly GameSnapshot FieldFree = new(GameScreen.FieldFree, null, null, null, null);
        private static readonly GameSnapshot Speech = new(GameScreen.FieldTask, null, null, null, null);
        private static readonly GameSnapshot TrainerBattle = new(GameScreen.BattleMessage, null, null, null, null, TextPrinter: 8);

        // Each read takes the next snapshot; the last one repeats. Counts the reads.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public int Reads { get; private set; }

            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                Reads++;
                return Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
            }
        }

        // The Resume block with the real catch check; calls records the catch, the milestone, the save and every press.
        private static Task ResumeAsync(GameState state, List<string> calls) => OrchestratorEvent.ResumeAsync(state,
            () => OrchestratorEvent.CheckForCatchAsync(state, new PokemonStateReader(), new ServiceCollection().BuildServiceProvider(),
                _ => { calls.Add("caught"); return Task.CompletedTask; },
                _ => { calls.Add("milestone"); return Task.CompletedTask; }),
            TimeSpan.FromMilliseconds(50), CancellationToken.None);

        private static FakeBot RsBot(List<string> calls, IGameStateReader game) =>
            new(Task.CompletedTask, NoNavigationProfile(calls)) { State = game, Pressed = m => calls.Add("press " + m) };

        // After a trainer stop there is no target: with the field free, Resume reads no party and goes on to the state
        // underneath. Resume runs before any encounter is built, so this holds for Spin and Run alike.
        [Theory]
        [InlineData(new EventState[0], EventState.DecideOverworldAction)]
        [InlineData(new[] { EventState.GoToPokemonCenter }, EventState.GoToPokemonCenter)]
        public async Task Resume_AfterATrainerStop_FieldFree_SkipsTheCatchAndCarriesOn(EventState[] under, EventState next)
        {
            List<string> calls = [];
            FakeBot bot = RsBot(calls, new ScriptedReader(FieldFree));
            GameState state = StateFor(bot, new ShinyTarget(), [.. under, EventState.Resume]);
            state.StoppedForTrainer = true;

            await ResumeAsync(state, calls);

            Assert.Equal(0, bot.PartyReads);
            Assert.Equal(["milestone"], calls);
            Assert.Empty(bot.Macros);
            Assert.False(state.StoppedForTrainer);
            Assert.Equal([next], state.State);
        }

        // Resume pressed during the speech or a battle screen that does not change: it parks again, logging the snapshot,
        // with no catch check, no press and no macro, so no spin or run starts.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Resume_AfterATrainerStop_FieldNotFree_ParksAgainAndStartsNothing(bool inBattle)
        {
            List<string> calls = [];
            GameSnapshot seen = inBattle ? TrainerBattle : Speech;
            FakeBot bot = RsBot(calls, new ScriptedReader(seen));
            GameState state = StateFor(bot, new ShinyTarget(), EventState.Resume);
            state.StoppedForTrainer = true;
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await ResumeAsync(state, calls);
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.Equal([EventState.Waiting], state.State);
            Assert.True(state.StoppedForTrainer);
            Assert.Empty(calls);
            Assert.Empty(bot.Macros);
            Assert.Equal(0, bot.PartyReads);
            Assert.Contains($"[Orchestrator] Resume: Waited for the free field after the trainer stop; the game state stayed {seen}", log.ToString());
        }

        // The operator fights on after pressing Resume: each change restarts the stale clock, and the free field carries on.
        [Fact]
        public async Task Resume_AfterATrainerStop_BattleThenFreeField_CarriesOn()
        {
            List<string> calls = [];
            FakeBot bot = RsBot(calls, new ScriptedReader(TrainerBattle, TrainerBattle with { TextPrinter = null }, Speech, FieldFree));
            GameState state = StateFor(bot, new ShinyTarget(), EventState.Resume);
            state.StoppedForTrainer = true;

            await ResumeAsync(state, calls);

            Assert.Equal(["milestone"], calls);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        // Not after a trainer stop (HGSS always): Resume reads no game state, as before.
        [Fact]
        public async Task Resume_NotAfterATrainerStop_ReadsNoGameState()
        {
            List<string> calls = [];
            ScriptedReader game = new(Speech);
            FakeBot bot = RsBot(calls, game);
            GameState state = StateFor(bot, new ShinyTarget(), EventState.Resume);

            await ResumeAsync(state, calls);

            Assert.Equal(0, game.Reads);
            Assert.Equal(["milestone"], calls);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        [Fact]
        public async Task Finish_WithoutOverworldNavigation_CompletesInPlace()
        {
            FakeBot bot = new(Task.CompletedTask, NoNavigationProfile([]));
            GameState state = StateFor(bot, new ShinyTarget(), EventState.DecideOverworldAction);
            state.BattleMode = BattleMode.Battle;

            await OrchestratorEvent.FinishAsync(state);

            Assert.Equal([EventState.Complete], state.State);
            Assert.False(state.CompleteAfterReturn);
            Assert.Equal(BattleMode.Flee, state.BattleMode);
        }

        [Fact]
        public async Task Finish_WithOverworldNavigation_WalksHome()
        {
            FakeBot bot = new(Task.CompletedTask);
            GameState state = StateFor(bot, new ShinyTarget(), EventState.DecideOverworldAction);
            state.BattleMode = BattleMode.Battle;

            await OrchestratorEvent.FinishAsync(state);

            Assert.Equal([EventState.ReturnToStart], state.State);
            Assert.True(state.CompleteAfterReturn);
            Assert.Equal(BattleMode.Flee, state.BattleMode);
        }

        private const string SlowTransition = "A first Pokémon at or below the wild level gets the long battle transition.";

        private static Task LogWildAsync(GameState state, byte level) => OrchestratorEvent.LogEncounterAndAskTargetAsync(state,
            new PokemonData { Level = level, PID = 0x12345678 }, _ => Task.CompletedTask, _ => Task.CompletedTask);

        [Fact]
        public async Task LevelLine_AddedOnceWhenAWildIsAtOrAboveTheLead()
        {
            FakeBot bot = new(Task.CompletedTask, NoNavigationProfile([]));
            GameState state = StateFor(bot, new ShinyTarget(), EventState.PokemonDataPending);
            state.SpeedLines = [];
            state.LeadLevel = 7;

            await LogWildAsync(state, 3);
            Assert.Empty(state.SpeedLines);
            await LogWildAsync(state, 7);
            Assert.Equal([SlowTransition], state.SpeedLines);
            await LogWildAsync(state, 8);
            Assert.Equal([SlowTransition], state.SpeedLines);
        }

        [Fact]
        public async Task LevelLine_NotAddedWithoutSpeedLines()
        {
            FakeBot bot = new(Task.CompletedTask);
            GameState state = StateFor(bot, new ShinyTarget(), EventState.PokemonDataPending);

            await LogWildAsync(state, 50);

            Assert.Null(state.SpeedLines);
        }
    }
}
