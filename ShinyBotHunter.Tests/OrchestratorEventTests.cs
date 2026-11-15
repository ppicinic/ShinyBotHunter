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

        // RS as W6 declares it: wild encounters, no navigation, no box reads. The save goes to the given list.
        private static GameProfile PartyOnlyProfile(List<string> calls) => new(
            GameFlavor.RS,
            new GameCapabilities(
                HasOverworldNavigation: false,
                HasWildEncounters: true,
                HasTraining: false,
                HasEggs: false,
                CanReadBoxes: false,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: false),
            [1],
            _ => new RsPointerService(),
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
        public async Task CatchCheck_PartyOnlyProfile_FindsTheTargetInThePartyAndSaves()
        {
            List<string> calls = [];
            FakeBot bot = new(Task.CompletedTask, PartyOnlyProfile(calls)) { PartyBlocks = [Block(1, 1), Block(TargetPid, TargetSpecies)] };
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
        public async Task CatchCheck_PartyOnlyProfile_TargetMissing_DoesNotSave()
        {
            List<string> calls = [];
            FakeBot bot = new(Task.CompletedTask, PartyOnlyProfile(calls)) { PartyBlocks = [Block(1, 1), Block(TargetPid, TargetSpecies + 1)] };
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

        [Fact]
        public async Task Finish_WithoutOverworldNavigation_CompletesInPlace()
        {
            FakeBot bot = new(Task.CompletedTask, PartyOnlyProfile([]));
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

        private static PokemonState Party(int size) => new() { PartySize = size };

        [Fact]
        public async Task FullPartyWithoutBoxReads_StopsComplete()
        {
            FakeBot bot = new(Task.CompletedTask, PartyOnlyProfile([]));
            GameState state = StateFor(bot, new ShinyTarget(), EventState.DecideOverworldAction);

            bool stopped = await OrchestratorEvent.StopForFullPartyAsync(state, Party(6));

            Assert.True(stopped);
            Assert.Equal([EventState.Complete], state.State);
            Assert.Equal(BattleMode.Flee, state.BattleMode);
        }

        [Fact]
        public async Task FivePartyWithoutBoxReads_Continues()
        {
            FakeBot bot = new(Task.CompletedTask, PartyOnlyProfile([]));
            GameState state = StateFor(bot, new ShinyTarget(), EventState.DecideOverworldAction);

            bool stopped = await OrchestratorEvent.StopForFullPartyAsync(state, Party(5));

            Assert.False(stopped);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        [Fact]
        public async Task FullPartyWithBoxReads_Continues()
        {
            FakeBot bot = new(Task.CompletedTask);
            GameState state = StateFor(bot, new ShinyTarget(), EventState.DecideOverworldAction);

            bool stopped = await OrchestratorEvent.StopForFullPartyAsync(state, Party(6));

            Assert.False(stopped);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }
    }
}
