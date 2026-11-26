using Microsoft.Extensions.DependencyInjection;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Persistence.MongoDb;
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
                (_, _) => { calls.Add("save"); return Task.CompletedTask; }, createNavigatorEvent: hgss.CreateNavigatorEvent);
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

        // A trainer's challenge on the walk to the anchor parks with the leg under Waiting: the walk goes on after Resume. A Waiting
        // for anything else ends it, as before.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AnchorWalkGoesOn_TrainerStop_KeepsTheWalk(bool stoppedForTrainer)
        {
            GameState state = new() { StoppedForTrainer = stoppedForTrainer };
            state.State.Push(EventState.ReturnToStart);
            state.State.Push(EventState.Waiting);

            bool goesOn = OrchestratorEvent.AnchorWalkGoesOn(state, new Anchor(34, 547, 304, 0, 6), "Route 30 · grass");

            Assert.Equal(stoppedForTrainer, goesOn);
            Assert.Equal([EventState.Waiting, EventState.ReturnToStart], state.State);
        }

        // Not after a trainer stop: Resume reads no game state, as before.
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

        // Tackle and Ember damage; Growl does not.
        private const ushort Tackle = 33, Ember = 52, Growl = 45;

        private static PokemonMetadataService Moves()
        {
            PokemonMetadataService metadata = new(null!);
            metadata.SeedMove(new PokemonMoveMetadata { Id = Tackle, Name = "Tackle", DamageClass = "physical" });
            metadata.SeedMove(new PokemonMoveMetadata { Id = Ember, Name = "Ember", DamageClass = "special" });
            metadata.SeedMove(new PokemonMoveMetadata { Id = Growl, Name = "Growl", DamageClass = "status" });
            return metadata;
        }

        // An HGSS party member as its parser fills it, MaxHP 40.
        private static PokemonData HgssMon(ushort hp, byte tacklePp = 35, byte emberPp = 0, byte growlPp = 0) => new()
        {
            HP = hp, MaxHP = 40, Move1 = Tackle, Move1PP = tacklePp, Move2 = Ember, Move2PP = emberPp, Move3 = Growl, Move3PP = growlPp,
        };

        private static bool CanFight(TrainType trainType, int? switchSlot, params PokemonData[] party)
        {
            GameState state = new() { TrainType = trainType, SwitchSlot = switchSlot };
            PokemonState pokeState = new() { PartySize = party.Length };
            party.CopyTo(pokeState.PartyPokemon, 0);
            return OrchestratorEvent.CanFightNextEncounter(state, pokeState, party[0], Moves().GetPokemonMoveMetadata);
        }

        // What HGSS decides today before every encounter of a Standard hunt: HP above a quarter, and three damaging PP.
        [Theory]
        [InlineData((ushort)40, (byte)35, (byte)0, (byte)0, true)]
        [InlineData((ushort)11, (byte)35, (byte)0, (byte)0, true)]
        [InlineData((ushort)10, (byte)35, (byte)0, (byte)0, false)]
        [InlineData((ushort)0, (byte)35, (byte)0, (byte)0, false)]
        [InlineData((ushort)40, (byte)2, (byte)0, (byte)40, false)]
        [InlineData((ushort)40, (byte)2, (byte)1, (byte)0, true)]
        [InlineData((ushort)40, (byte)0, (byte)0, (byte)40, false)]
        public void CanFight_HgssStandard_IsTodays(ushort hp, byte tacklePp, byte emberPp, byte growlPp, bool expected)
        {
            Assert.Equal(expected, CanFight(TrainType.Standard, null, HgssMon(hp, tacklePp, emberPp, growlPp)));
        }

        // And of a Switch hunt: the lead only needs HP; the battler in the 1-based slot needs HP and PP, and must be there.
        [Theory]
        [InlineData((ushort)5, (ushort)40, (byte)35, 2, 2, true)]
        [InlineData((ushort)0, (ushort)40, (byte)35, 2, 2, false)]
        [InlineData((ushort)40, (ushort)10, (byte)35, 2, 2, false)]
        [InlineData((ushort)40, (ushort)40, (byte)2, 2, 2, false)]
        [InlineData((ushort)40, (ushort)40, (byte)35, 2, 3, false)]
        [InlineData((ushort)40, (ushort)40, (byte)35, 3, 3, true)]
        public void CanFight_HgssSwitch_IsTodays(ushort leadHp, ushort battlerHp, byte battlerTacklePp, int partySize, int switchSlot, bool expected)
        {
            PokemonData[] party = [HgssMon(leadHp, tacklePp: 0), .. Enumerable.Repeat(HgssMon(battlerHp, battlerTacklePp), partySize - 1)];

            Assert.Equal(expected, CanFight(TrainType.Switch, switchSlot, party));
        }

        // A Gen 3 party record of a Mudkip (internal 283) with MaxHP 40 and Tackle, Ember and Growl, as the game stores it:
        // pid % 24 == 0 keeps the substructures in order, and otId 0 makes the key the pid.
        private static byte[] Gen3Mon(ushort hp, byte tacklePp = 35, byte emberPp = 0, byte growlPp = 0, byte level = 10)
        {
            const uint pid = 0x60;
            byte[] plain = new byte[48];
            BitConverter.GetBytes((ushort)283).CopyTo(plain, 0);
            BitConverter.GetBytes(Tackle).CopyTo(plain, 12);
            BitConverter.GetBytes(Ember).CopyTo(plain, 14);
            BitConverter.GetBytes(Growl).CopyTo(plain, 16);
            (plain[20], plain[21], plain[22]) = (tacklePp, emberPp, growlPp);
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
            block[0x54] = level;
            BitConverter.GetBytes(hp).CopyTo(block, 0x56);
            BitConverter.GetBytes((ushort)40).CopyTo(block, 0x58);
            return block;
        }

        // The RS profile on the shared table (FakeBot reads revision 0), its parser on the seeded moves and Mudkip; chain reads
        // answer zeroed blocks (no Repel).
        private static FakeBot RsBot(params byte[][] party) =>
            RsBot(blocks => blocks.Select(b => new ChainBlockResult(ChainBlockStatus.Ok, 0, new byte[b.Length])).ToArray(), party);

        private static FakeBot RsBot(Func<IReadOnlyList<ChainBlockRequest>, ChainBlockResult[]> chainBlocks, params byte[][] party)
        {
            PokemonMetadataService metadata = Moves();
            metadata.Seed(new PokemonMetadata { Id = 258, PokemonName = "Mudkip", PokeApiName = "mudkip" });
            GameProfile rs = RsProfile.Create(metadata, new MapService(new Dictionary<int, Map>()));
            return new FakeBot(Task.CompletedTask, new GameProfile(rs.Flavor, rs.Capabilities, [0], (_, _) => new RsPointerService(), rs.Parser,
                rs.BattleParser, rs.CreateOverworldReader, rs.Maps, rs.Graph, rs.Pathfinding, rs.CreateStateReader, rs.CreateBattleEvent, rs.SaveGame,
                createNavigatorEvent: rs.CreateNavigatorEvent))
            {
                PartyBlocks = party,
                ChainBlocks = chainBlocks,
            };
        }

        // A training hunt's state at DecideOverworldAction, in Flee as after a heal trip.
        private static GameState Training(FakeBot bot, TrainType trainType, int? switchSlot = null, int targetLevel = 50)
        {
            GameState state = StateFor(bot, new ShinyTarget(), EventState.DecideOverworldAction);
            (state.TrainType, state.SwitchSlot, state.TargetLevel) = (trainType, switchSlot, targetLevel);
            (state.OriginalBattleMode, state.BattleMode) = (BattleMode.Battle, BattleMode.Flee);
            return state;
        }

        private static Task DecideAsync(GameState state) =>
            OrchestratorEvent.DecideOverworldAction(state, new PokemonStateReader(), Moves().GetPokemonMoveMetadata, null!, CancellationToken.None);

        // FR-18 on Gen 3 data: a lead with HP and damaging PP fights the next encounter, back in Battle.
        [Fact]
        public async Task Decide_RsStandard_Ready_FightsTheNextEncounter()
        {
            GameState state = Training(RsBot(Gen3Mon(40)), TrainType.Standard);

            await DecideAsync(state);

            Assert.Equal([EventState.Encountering], state.State);
            Assert.Equal(BattleMode.Battle, state.BattleMode);
        }

        // Too hurt, two damaging PP with a status move's left, or fainted: the Pokémon Center trip, whose wild battles are fled.
        [Theory]
        [InlineData((ushort)10, (byte)35, (byte)0)]
        [InlineData((ushort)40, (byte)2, (byte)40)]
        [InlineData((ushort)0, (byte)35, (byte)0)]
        public async Task Decide_RsStandard_HurtSpentOrFainted_GoesToThePokemonCenterFleeing(ushort hp, byte tacklePp, byte growlPp)
        {
            GameState state = Training(RsBot(Gen3Mon(hp, tacklePp, growlPp: growlPp)), TrainType.Standard);

            await DecideAsync(state);

            Assert.Equal([EventState.GoToPokemonCenter], state.State);
            Assert.Equal(BattleMode.Flee, state.BattleMode);
        }

        // Switch reads the battler from its 1-based slot: slot 2 spent takes the heal trip, slot 3 ready fights, though the
        // lead has no PP.
        [Theory]
        [InlineData(2, EventState.GoToPokemonCenter)]
        [InlineData(3, EventState.Encountering)]
        public async Task Decide_RsSwitch_ReadsTheBattlerInItsSlot(int switchSlot, EventState next)
        {
            GameState state = Training(RsBot(Gen3Mon(40, tacklePp: 0), Gen3Mon(10), Gen3Mon(40)), TrainType.Switch, switchSlot);

            await DecideAsync(state);

            Assert.Equal([next], state.State);
        }

        // FR-18: the lead at the target level ends training, and a hunt with no target then walks home before Complete.
        [Theory]
        [InlineData((byte)11, false, EventState.Encountering)]
        [InlineData((byte)12, true, EventState.ReturnToStart)]
        public async Task Decide_RsTargetLevel_EndsTrainingAndWalksHome(byte level, bool complete, EventState next)
        {
            GameState state = Training(RsBot(Gen3Mon(40, level: level)), TrainType.Standard, targetLevel: 12);
            state.Target = new NoTarget();

            await DecideAsync(state);

            Assert.Equal(complete, state.TrainingComplete);
            Assert.Equal(complete, state.CompleteAfterReturn);
            Assert.Equal([next], state.State);
        }

        // QA L7: the heal trip's line names the member the check read, its HP and its damaging PP, or the empty slot.
        [Theory]
        [InlineData(TrainType.Standard, null, (ushort)40, "heal trip: Mudkip HP 40/40, damaging PP 0")]
        [InlineData(TrainType.Switch, 2, (ushort)40, "heal trip: Mudkip HP 10/40, damaging PP 35")]
        [InlineData(TrainType.Switch, 2, (ushort)0, "heal trip: Mudkip HP 0/40, damaging PP 0")]
        [InlineData(TrainType.Switch, 3, (ushort)40, "heal trip: switch slot 3 is empty")]
        public async Task HealTripLine_NamesWhyTheTripWasTaken(TrainType trainType, int? switchSlot, ushort leadHp, string line)
        {
            FakeBot bot = RsBot(Gen3Mon(leadHp, tacklePp: 0), Gen3Mon(10));
            GameState state = Training(bot, trainType, switchSlot);
            PokemonState party = await new PokemonStateReader().GetPartyState(bot);

            Assert.False(OrchestratorEvent.CanFightNextEncounter(state, party, party.PartyPokemon[0]!, Moves().GetPokemonMoveMetadata));
            Assert.Equal(line, OrchestratorEvent.HealTripLine(state, party, party.PartyPokemon[0]!, Moves().GetPokemonMoveMetadata));
        }

        // A party block for RateInputs: level, HP, ability, and 1 for an egg.
        private sealed class RateFieldsParser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw) => new PokemonData { Level = raw[0], HP = raw[1], Ability = raw[2], IVs = raw[3] == 1 ? 1u << 30 : 0 };
        }

        private static PokemonState Party(params byte[][] members)
        {
            PokemonState party = new() { PartySize = members.Length };
            for (int i = 0; i < members.Length; i++) party.PartyPokemon[i] = new RateFieldsParser().Parse(members[i]);
            return party;
        }

        // The B3 note on #522: the Repel level skips eggs as well as fainted members.
        [Fact]
        public void RateInputsOf_RepelOn_SkipsFaintedAndEggs()
        {
            RateInputs inputs = RateInputs.Of(5, 20, 50, Party([1, 10, 0, 1], [30, 0, 0, 0], [12, 20, 0, 0]));

            Assert.Equal(new RateInputs(5, 20, 50, 12, 0, 1, true), inputs);
        }

        // No Repel, or the save's section table not read yet: no Repel level.
        [Theory]
        [InlineData(0)]
        [InlineData(null)]
        public void RateInputsOf_NoRepel_NoRepelLevel(int? repelSteps)
        {
            RateInputs inputs = RateInputs.Of(0, 4, repelSteps, Party([12, 20, 51, 0]));

            Assert.Equal(new RateInputs(0, 4, repelSteps, null, 51, 12, false), inputs);
        }

        // HGSS with the rate-fields parser and only what the decision uses; the bot's state reader is the real one over placed
        // memory.
        private static (FakeBot Bot, FakeMemoryClient Memory) HgssDecisionBot(params byte[][] party)
        {
            HgssPointerService table = new();
            FakeMemoryClient memory = new();
            memory.Place(table.LiveClock + 0x0C, BitConverter.GetBytes(3u));
            memory.Place(table.LiveClock + 0x10, BitConverter.GetBytes(21u));
            GameProfile hgss = new(GameFlavor.HGSS,
                new GameCapabilities(HasOverworldNavigation: false, HasWildEncounters: false, HasTraining: false, HasEggs: false,
                    CanReadBoxes: true, HasSoftResetStarterHunt: false, HasShinyStarterHunt: false, HasWishmakerJirachi: false),
                [0], (_, _) => table, new RateFieldsParser(), battleParser: null);
            FakeBot bot = new(Task.CompletedTask, hgss) { PartyBlocks = party, State = new HgssFieldSystemStateReader(memory, table) };
            return (bot, memory);
        }

        // FR-26: each decision makes one chain request beside the party read and keeps what it read as "last seen".
        [Fact]
        public async Task Decide_Hgss_KeepsTheRateInputsFromOneRequest()
        {
            (FakeBot bot, FakeMemoryClient memory) = HgssDecisionBot([12, 20, 51, 0]);
            GameState state = StateFor(bot, new ShinyTarget(), EventState.DecideOverworldAction);

            await DecideAsync(state);
            await DecideAsync(state);

            Assert.Equal(new RateInputs(3, 21, null, null, 51, 12, false), state.RatesSeen);
            Assert.Equal(2, memory.ChainReads.Count);
            Assert.All(memory.ChainReads, read => Assert.Equal(2, read.Blocks.Count));
            Assert.Equal(2, bot.PartyReads);
            Assert.Equal([EventState.Encountering], state.State);
        }

        // FR-26a and NFR-1: on RS each decision reads the Repel counter as one 2-byte block, with no clock.
        [Fact]
        public async Task Decide_Rs_KeepsTheRepelCounterFromOneBlock()
        {
            List<IReadOnlyList<ChainBlockRequest>> requests = [];
            FakeBot bot = RsBot(blocks =>
            {
                requests.Add(blocks);
                return [new ChainBlockResult(ChainBlockStatus.Ok, 0, BitConverter.GetBytes((ushort)3))];
            }, Gen3Mon(40));
            GameState state = Training(bot, TrainType.Standard);

            await DecideAsync(state);

            Assert.Equal(new RateInputs(null, null, 3, 10, 0, 10, false), state.RatesSeen);
            ChainBlockRequest block = Assert.Single(Assert.Single(requests));
            Assert.Equal((0x02026AB6u, 0, 2), (block.Anchor, block.Hops.Length, block.Length));
        }

        // FR-34a with no plan: told when the milestone becomes complete after Init's read, never at Start, never twice.
        [Theory]
        [InlineData(null, true, true, 0)]
        [InlineData(null, false, false, 0)]
        [InlineData(false, false, false, 0)]
        [InlineData(false, true, true, 1)]
        [InlineData(true, true, true, 0)]
        public async Task TellIfComplete_NoPlan(bool? told, bool complete, bool toldAfter, int sentences)
        {
            List<string> sent = [];

            bool result = await OrchestratorEvent.TellIfCompleteAsync(told, complete, () => "Route 30 · grass is complete on fake.nds.",
                s => { sent.Add(s); return Task.CompletedTask; });

            Assert.Equal(toldAfter, result);
            Assert.Equal(sentences, sent.Count);
        }

        // FR-34a: a failed send is logged and the hunt goes on.
        [Fact]
        public async Task Tell_SendThrows_TheHuntGoesOn()
        {
            await OrchestratorEvent.TellAsync(_ => throw new System.Threading.Channels.ChannelClosedException(), "Route 30 · grass is complete on fake.nds.");
        }
    }
}
