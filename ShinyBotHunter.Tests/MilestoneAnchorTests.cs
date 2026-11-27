using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Tests
{
    // FR-22 to FR-24 (#519): the anchor a hunt walks to, its tile check against the committed map data, and the way of hunting
    // it decides. In the collection because MapService opens maps.bin without read sharing, as MapServiceTests says.
    [Collection("macro-registry")]
    public class MilestoneAnchorTests
    {
        private static readonly string HgssDirectory = Path.Combine(RepoPaths.AppProject, "Data", "HGSS");
        private static readonly string RsDirectory = Path.Combine(RepoPaths.AppProject, "Data", "RS");

        private static readonly MapService HgssMaps = new(HgssDirectory);
        private static readonly MapService RsMaps = new(RsDirectory, withElevationAndEvents: true);
        private static readonly GameProfile Hgss = HgssProfile.Create(new PokemonMetadataService(null!), HgssMaps, new GraphService());
        private static readonly GameProfile Rs = RsProfile.Create(new PokemonMetadataService(null!), RsMaps);

        private static readonly IReadOnlyList<Milestone> SoulSilver = MilestoneFile.Load(HgssDirectory)[RomVersion.SoulSilver];
        private static readonly IReadOnlyList<Milestone> Sapphire = MilestoneFile.Load(RsDirectory)[RomVersion.Sapphire];

        private static Milestone Route30 => SoulSilver.Single(m => m.Id == "34-land");

        [Fact]
        public void AnchorFor_Replacement_WinsOverGenerated()
        {
            var replacement = new MilestoneAnchor("34-land", new Anchor(34, 546, 300, 0, 0), Direction.Left);
            var file = new GameFile { Id = "file", Anchors = [new MilestoneAnchor("33-land", new Anchor(33, 1, 2, 0, 0), Direction.Up), replacement] };

            Assert.Equal(replacement, file.AnchorFor("34-land", Route30, null, []));
        }

        [Fact]
        public void AnchorFor_Generated_NoReplacement()
        {
            Assert.Equal(new MilestoneAnchor("34-land", new Anchor(34, 547, 304, 0, 6), Direction.None), new GameFile().AnchorFor("34-land", Route30, null, []));
        }

        // The acceptance "a custom milestone with no anchor starts where the bot stands": null, and UseAnchor changes nothing.
        [Fact]
        public void AnchorFor_NoAnchor_IsNull()
        {
            var noGeneratedAnchor = SoulSilver.First(m => m.Modality == Modality.Land && m.Anchor is null);

            Assert.Null(new GameFile().AnchorFor("my custom phase", null, null, []));
            Assert.Null(new GameFile().AnchorFor(noGeneratedAnchor.Id, noGeneratedAnchor, null, []));
        }

        // FR-42: with no replacement stored, none of production's 20 phases has an anchor, so each is hunted where the bot
        // stands. A stored phase wins over a generated milestone of its name, so generated is null for it.
        [Fact]
        public void AnchorFor_ProductionPhases_NoAnchor()
        {
            var phases = BsonSerializer.Deserialize<List<PhaseInfo>>(
                File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

            Assert.Equal(20, phases.Count);
            Assert.All(phases, p => Assert.Null(new GameFile { Id = "file" }.AnchorFor(p.Name, null, null, [])));
        }

        [Fact]
        public void GameFile_Anchors_BsonRoundTrip()
        {
            var file = new GameFile { Id = "Pokemon SoulSilver2", Anchors = [new MilestoneAnchor("34-land", new Anchor(34, 546, 300, 2, 0), Direction.Right)] };

            var loaded = BsonSerializer.Deserialize<GameFile>(file.ToBsonDocument());
            var stored = BsonSerializer.Deserialize<GameFile>(new BsonDocument { ["_id"] = "Pokemon HeartGold", ["Entries"] = new BsonArray() });
            // A document written before the rework, with the goal and picker fields this class no longer has.
            var beforeRework = file.ToBsonDocument();
            beforeRework["Entries"] = new BsonArray { new BsonDocument { ["Species"] = 16, ["FormCode"] = BsonNull.Value, ["Amount"] = 1 } };
            beforeRework["Hidden"] = new BsonArray { new BsonDocument { ["MilestoneId"] = "34-land", ["Key"] = "#16" } };
            beforeRework["Links"] = new BsonArray { new BsonDocument { ["Phase"] = "1-4 Route 30", ["MilestoneId"] = "34-land" } };
            beforeRework["Archived"] = new BsonArray { "34-land" };
            beforeRework["Order"] = new BsonArray { new BsonDocument { ["Milestone"] = "34-land", ["Label"] = "1-4" } };

            Assert.Equal(file.Anchors, loaded.Anchors);
            Assert.Empty(stored.Anchors);
            Assert.Equal(file.Anchors, BsonSerializer.Deserialize<GameFile>(beforeRework).Anchors);
        }

        // The acceptance test: (550, 304) is the path just east of Route 30's grass column.
        [Fact]
        public void AnchorTileStop_NotOfTheKind_StopsWithSentence()
        {
            Assert.Equal("Route 30 · grass: the anchor (550, 304) is not grass.",
                BotService.AnchorTileStop(Route30, new Anchor(34, 550, 304, 0, 6), HgssMaps, Hgss.LandTileKind!(Route30)));
        }

        // Also shows GetMap(anchor.Map) reads the right map on both games; RS's reverse lookup maps every id to itself.
        [Fact]
        public void AnchorTileStop_EveryGeneratedAnchor_Passes()
        {
            var hgss = SoulSilver.Where(m => m.Anchor is not null).ToList();
            var rs = Sapphire.Where(m => m.Anchor is not null).ToList();

            Assert.Equal(71, hgss.Count);
            Assert.Equal(24, rs.Count);
            Assert.All(hgss, m => Assert.Null(BotService.AnchorTileStop(m, m.Anchor!, HgssMaps, Hgss.LandTileKind!(m))));
            Assert.All(rs, m => Assert.Null(BotService.AnchorTileStop(m, m.Anchor!, RsMaps, Rs.LandTileKind!(m))));
        }

        // B1r's note on #519: an RS cave's encounter tiles are kind 3, so HGSS's rule (a cave's floor, kind 0) is not RS's.
        [Fact]
        public void AnchorTileStop_RsCave_IsKind3()
        {
            var meteorFalls = Sapphire.Single(m => m.Id == "6144-land");

            Assert.False(meteorFalls.HasGrass);
            Assert.Null(BotService.AnchorTileStop(meteorFalls, meteorFalls.Anchor!, RsMaps, Rs.LandTileKind!(meteorFalls)));
            Assert.Equal("Meteor Falls 1F 1R · cave: the anchor (27, 17) is not cave.",
                BotService.AnchorTileStop(meteorFalls, meteorFalls.Anchor!, RsMaps, Hgss.LandTileKind!(meteorFalls)));
            Assert.Equal(Constant.MAP_Grass, Rs.LandTileKind!(meteorFalls));
        }

        [Fact]
        public void AnchorLeg_RunUpDown()
        {
            Assert.Equal(new EncounterParams(EncounterType.Run, yRange: 6), BotService.AnchorLeg(new EncounterParams(EncounterType.Spin), new Anchor(34, 547, 304, 0, 6)));
        }

        [Fact]
        public void AnchorLeg_RunLeftRight()
        {
            Assert.Equal(new EncounterParams(EncounterType.Run, xRange: 3), BotService.AnchorLeg(new EncounterParams(EncounterType.Run, 2, 0), new Anchor(34, 547, 304, 3, 0)));
        }

        [Fact]
        public void AnchorLeg_Spin()
        {
            Assert.Equal(new EncounterParams(EncounterType.Spin), BotService.AnchorLeg(new EncounterParams(EncounterType.Run, 4, 0), new Anchor(16, 4, 2, 0, 0)));
        }

        [Fact]
        public void AnchorLeg_HeadbuttUnchanged()
        {
            Assert.Equal(new EncounterParams(EncounterType.Headbutt), BotService.AnchorLeg(new EncounterParams(EncounterType.Headbutt), new Anchor(34, 547, 304, 0, 6)));
        }

        [Fact]
        public void EncounterTypeRefusal_HeadbuttAtLand_RunOrSpinAtHeadbutt()
        {
            var headbutt = SoulSilver.Single(m => m.Id == "34-headbutt");

            Assert.Equal("Route 30 · grass is not hunted with Headbutt.", BotService.EncounterTypeRefusal(Route30, EncounterType.Headbutt));
            Assert.Equal("Route 30 · headbutt is not hunted with Run.", BotService.EncounterTypeRefusal(headbutt, EncounterType.Run));
            Assert.Equal("Route 30 · headbutt is not hunted with Spin.", BotService.EncounterTypeRefusal(headbutt, EncounterType.Spin));
            Assert.Null(BotService.EncounterTypeRefusal(Route30, EncounterType.Run));
            Assert.Null(BotService.EncounterTypeRefusal(headbutt, EncounterType.Headbutt));
            // A custom phase has no kind, so any type hunts it.
            Assert.Null(BotService.EncounterTypeRefusal(null, EncounterType.Headbutt));
        }

        private static GameState Captured() => new() { StartMapId = 33, StartX = 10, StartY = 11, StartFacing = Direction.Down, BattleMode = BattleMode.Battle };

        [Fact]
        public void UseAnchor_SetsStartTile_GeneratedKeepsFacing()
        {
            var state = Captured();

            Assert.True(OrchestratorEvent.UseAnchor(state, new MilestoneAnchor("34-land", new Anchor(34, 547, 304, 0, 6), Direction.None)));

            Assert.Equal((34, 547, 304, Direction.Down, BattleMode.Flee), (state.StartMapId, state.StartX, state.StartY, state.StartFacing, state.BattleMode));
        }

        [Fact]
        public void UseAnchor_Replacement_SetsFacing()
        {
            var state = Captured();

            Assert.True(OrchestratorEvent.UseAnchor(state, new MilestoneAnchor("34-land", new Anchor(34, 546, 300, 0, 0), Direction.Left)));

            Assert.Equal((34, 546, 300, Direction.Left, BattleMode.Flee), (state.StartMapId, state.StartX, state.StartY, state.StartFacing, state.BattleMode));
        }

        [Fact]
        public void UseAnchor_Null_ChangesNothing()
        {
            var state = Captured();

            Assert.False(OrchestratorEvent.UseAnchor(state, null));

            Assert.Equal((33, 10, 11, Direction.Down, BattleMode.Battle), (state.StartMapId, state.StartX, state.StartY, state.StartFacing, state.BattleMode));
        }

        [Theory]
        [InlineData(EventState.ReturnToStart, true)]
        [InlineData(EventState.BattleStart, true)]
        [InlineData(EventState.DecideOverworldAction, false)]
        [InlineData(EventState.Waiting, false)]
        public void AnchorWalkGoesOn_UntilTheWalkEnds(EventState top, bool goesOn)
        {
            var state = new GameState();
            state.State.Push(top);

            Assert.Equal(goesOn, OrchestratorEvent.AnchorWalkGoesOn(state, new Anchor(34, 547, 304, 0, 6), "Route 30 · grass"));
            Assert.Equal([top], state.State);
        }

        // HGSS's navigator completes in place on no path; a hunt that never reached its anchor parks instead (FR-23).
        [Fact]
        public void AnchorWalkGoesOn_NoPath_ParksInsteadOfComplete()
        {
            var state = new GameState();
            state.State.Push(EventState.Complete);
            var output = new StringWriter();
            var console = Console.Out;
            Console.SetOut(output);
            try
            {
                Assert.False(OrchestratorEvent.AnchorWalkGoesOn(state, new Anchor(34, 547, 304, 0, 6), "Route 30 · grass"));
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.Equal([EventState.Waiting], state.State);
            Assert.Contains("Route 30 · grass: no path to the anchor (547, 304).", output.ToString());
        }

        // FR-21 (#567). Two regular trees: for a trainer ID ending in 0, tree 0 gives the common table and tree 1 the rare one
        // (sRareTreeLUT_2); tree 0's stand is the cheaper.
        private const int CommonSpecies = 1, RareSpecies = 2;
        private const uint OwnersId = 2318286077;

        private static Milestone TwoTrees() => new([1], Modality.Headbutt, false,
            [new Slot(0, CommonSpecies, 1, 1, 100, SlotCondition.CommonTree), new Slot(1, RareSpecies, 1, 1, 100, SlotCondition.RareTree)],
            [new HeadbuttTree(false, [[0, 0]], new Anchor(1, 0, 1, 0, 0), Direction.Up, 10), new HeadbuttTree(false, [[5, 0]], new Anchor(1, 5, 1, 0, 0), Direction.Down, 20)]);

        [Fact]
        public void AnchorFor_Headbutt_TheFilesTreeWithItsFacing()
        {
            var file = new GameFile { Id = "file" };

            Assert.Equal(new MilestoneAnchor("1-headbutt", new Anchor(1, 0, 1, 0, 0), Direction.Up), file.AnchorFor("1-headbutt", TwoTrees(), 10, [CommonSpecies]));
        }

        // FR-42: a stored replacement comes back whatever the ID.
        [Fact]
        public void AnchorFor_Headbutt_ReplacementWins()
        {
            var replacement = new MilestoneAnchor("1-headbutt", new Anchor(1, 9, 9, 0, 0), Direction.Left);
            var file = new GameFile { Id = "file", Anchors = [replacement] };

            Assert.Equal(replacement, file.AnchorFor("1-headbutt", TwoTrees(), null, []));
            for (uint digit = 0; digit < 10; digit++)
            {
                Assert.Equal(replacement, file.AnchorFor("1-headbutt", TwoTrees(), digit, []));
            }
        }

        // No ID, no tree: hunted where the bot stands.
        [Fact]
        public void AnchorFor_Headbutt_NoTrainerId_Null()
        {
            Assert.Null(new GameFile { Id = "file" }.AnchorFor("1-headbutt", TwoTrees(), null, []));
        }

        [Fact]
        public void AnchorFor_Headbutt_CaughtTargetChangesTheKind()
        {
            var file = new GameFile { Id = "file" };

            Assert.Equal(0, file.AnchorFor("1-headbutt", TwoTrees(), 10, [CommonSpecies, RareSpecies])!.Anchor.X);
            Assert.Equal(new MilestoneAnchor("1-headbutt", new Anchor(1, 5, 1, 0, 0), Direction.Down),
                file.AnchorFor("1-headbutt", TwoTrees(), 10, [RareSpecies]));
        }

        [Fact]
        public void AnchorFor_Land_Unchanged()
        {
            Assert.Equal(new MilestoneAnchor("34-land", new Anchor(34, 547, 304, 0, 6), Direction.None),
                new GameFile { Id = "file" }.AnchorFor("34-land", Route30, OwnersId, [16]));
        }

        // FR-42: a saved milestone with no location (generated null), a replacement under its name or a land location reads no
        // trainer ID; only one at a headbutt location with no replacement does, in a single leg or as one stop of a plan.
        [Fact]
        public void NeedsTrainerId_OnlyAGeneratedHeadbuttWithNoReplacement()
        {
            var replaced = new GameFile { Id = "file", Anchors = [new MilestoneAnchor("2-3c Azalea", new Anchor(1, 9, 9, 0, 0), Direction.Left)] };
            var byId = new GameFile { Id = "file", Anchors = [new MilestoneAnchor("1-headbutt", new Anchor(1, 9, 9, 0, 0), Direction.Left)] };

            Assert.True(BotService.NeedsTrainerId([("2-3c Azalea", TwoTrees())], new GameFile { Id = "file" }));
            Assert.True(BotService.NeedsTrainerId([("1-4 Route 30", Route30), ("X", null), ("2-3c Azalea", TwoTrees())], new GameFile { Id = "file" }));
            Assert.False(BotService.NeedsTrainerId([("2-3c Azalea", TwoTrees())], replaced));
            Assert.True(BotService.NeedsTrainerId([("2-3c Azalea", TwoTrees())], byId));
            Assert.False(BotService.NeedsTrainerId([("X", null)], new GameFile { Id = "file" }));
            Assert.False(BotService.NeedsTrainerId([("1-4 Route 30", Route30)], new GameFile { Id = "file" }));
        }

        private static readonly HgssPointerService Table = new();
        private const uint FieldSystem = 0x02300000;
        private const uint SaveData = 0x0227C254;

        [Fact]
        public async Task ReadTrainerId_FourBytesThroughTheSaveData()
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place(FieldSystem + 0x0C, BitConverter.GetBytes(SaveData));
            memory.Place(SaveData + 0x84, BitConverter.GetBytes((ushort)15613));
            memory.Place(SaveData + 0x86, BitConverter.GetBytes((ushort)35374));

            uint? id = await BotService.ReadTrainerIdAsync(memory, Table, CancellationToken.None);

            Assert.Equal(OwnersId, id);
            var (blocks, _) = Assert.Single(memory.ChainReads);
            ChainBlockRequest block = Assert.Single(blocks);
            Assert.Equal(Table.Anchor, block.Anchor);
            Assert.Equal([0x0C, 0x84], block.Hops);
            Assert.Equal(4, block.Length);
        }

        // The title screen: no save data, no ID, no exception.
        [Fact]
        public async Task ReadTrainerId_NullSaveData_Null()
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));

            Assert.Null(await BotService.ReadTrainerIdAsync(memory, Table, CancellationToken.None));
        }

        private static PokemonState Party(params uint[] otIds)
        {
            var party = new PokemonState { PartySize = otIds.Length };
            for (int i = 0; i < otIds.Length; i++) party.PartyPokemon[i] = new PokemonData { OTId = otIds[i] };
            return party;
        }

        [Fact]
        public void CrossCheckTrainerId_OwnPokemonMatches_Compared()
        {
            Assert.Equal((true, (string?)null), BotService.CrossCheckTrainerId(OwnersId, Party(12345u << 16 | 999, OwnersId)));
        }

        // A wrong high half: the party's own Pokémon has the visible ID but another full ID.
        [Fact]
        public void CrossCheckTrainerId_FullIdDiffers_RefusesNamingBoth()
        {
            uint party = 1u << 16 | 15613;

            Assert.Equal((true, "The trainer ID read as 2318286077, but a party Pokémon with the visible ID 15613 has the full ID 81149; "
                + "no headbutt tree can be chosen until the read is fixed."), BotService.CrossCheckTrainerId(OwnersId, Party(OwnersId, party)));
        }

        // Only traded Pokémon (another visible ID): nothing to compare, so Start goes on and logs it.
        [Fact]
        public void CrossCheckTrainerId_NoOwnPokemon_NotCompared()
        {
            Assert.Equal((false, (string?)null), BotService.CrossCheckTrainerId(OwnersId, Party(35374u << 16 | 15614, 7)));
        }
    }
}
