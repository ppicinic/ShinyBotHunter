using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
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

            Assert.Equal(replacement, file.AnchorFor("34-land", Route30));
        }

        [Fact]
        public void AnchorFor_Generated_NoReplacement()
        {
            Assert.Equal(new MilestoneAnchor("34-land", new Anchor(34, 547, 304, 0, 6), Direction.None), new GameFile().AnchorFor("34-land", Route30));
        }

        // The acceptance "a custom milestone with no anchor starts where the bot stands": null, and UseAnchor changes nothing.
        [Fact]
        public void AnchorFor_NoAnchor_IsNull()
        {
            var noGeneratedAnchor = SoulSilver.First(m => m.Modality == Modality.Land && m.Anchor is null);

            Assert.Null(new GameFile().AnchorFor("my custom phase", null));
            Assert.Null(new GameFile().AnchorFor(noGeneratedAnchor.Id, noGeneratedAnchor));
        }

        // FR-42: with no replacement stored, none of production's 20 phases has an anchor, so each is hunted where the bot
        // stands. A stored phase wins over a generated milestone of its name, so generated is null for it.
        [Fact]
        public void AnchorFor_ProductionPhases_NoAnchor()
        {
            var phases = BsonSerializer.Deserialize<List<PhaseInfo>>(
                File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

            Assert.Equal(20, phases.Count);
            Assert.All(phases, p => Assert.Null(new GameFile { Id = "file" }.AnchorFor(p.Name, null)));
        }

        [Fact]
        public void GameFile_Anchors_BsonRoundTrip()
        {
            var file = new GameFile { Id = "Pokemon SoulSilver2", Anchors = [new MilestoneAnchor("34-land", new Anchor(34, 546, 300, 2, 0), Direction.Right)] };

            var loaded = BsonSerializer.Deserialize<GameFile>(file.ToBsonDocument());
            var stored = BsonSerializer.Deserialize<GameFile>(new BsonDocument { ["_id"] = "Pokemon HeartGold", ["Entries"] = new BsonArray() });

            Assert.Equal(file.Anchors, loaded.Anchors);
            Assert.Empty(stored.Anchors);
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
    }
}
