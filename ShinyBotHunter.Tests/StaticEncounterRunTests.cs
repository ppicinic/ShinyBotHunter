using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PKHeX.Core;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // Run's refusals for HGSS Static Encounter (FR-2), over a fake memory, one snapshot, PKHeX-built party blocks and the
    // pathfinder over the shipped map data. Values from docs/research/sinjoh-static.md: map 522, the save tile (16, 14) facing up.
    // In the collection because MapService opens maps.bin without read sharing, as ServiceRegistrationTests does.
    [Collection("macro-registry")]
    public class StaticEncounterRunTests
    {
        private static readonly HgssPointerService Table = new();
        private const uint FieldSystem = 0x02300000;

        private const string OffTheStage = "Stand on the Mystri Stage in the Sinjoh Ruins.";
        private const string FieldNotFree = "Close any menu or text first: the player must be free to walk on the stage.";
        private const string NotOneArceus = "The party must be one Arceus and nothing else.";
        private const string NoPath = "No path from here to the circle. Stand on the stage in front of the circles.";

        public StaticEncounterRunTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        private sealed class OneSnapshot(GameScreen screen) : IGameStateReader
        {
            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new GameSnapshot(screen, null, null, null, null));
        }

        // Reads the block the way the game holds it, encrypted, through PKHeX; a copy, because PK4 decrypts in place.
        private sealed class Pk4Parser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw)
            {
                var pk = new PK4(raw.ToArray());
                return new PokemonData { Species = pk.Species, IVs = pk.IV32, RawBytes = raw };
            }
        }

        // Fateful by default: the owner's save holds the event Arceus.
        private static byte[] Block(ushort species, bool egg = false, bool fateful = true, GameVersion version = GameVersion.HG, ushort metLocation = 0)
        {
            var pk = new PK4 { Species = species, PID = 0x12345678, FatefulEncounter = fateful, IsEgg = egg, Version = version, MetLocation = metLocation };
            pk.RefreshChecksum();
            byte[] block = pk.Data.ToArray();
            PokeCrypto.Encrypt45(block);
            return block;
        }

        private static void Stand(FakeMemoryClient memory, int map, int x, int y, Direction facing)
        {
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place((uint)(FieldSystem + Table.MapIdOffset), BitConverter.GetBytes((ushort)map));
            memory.Place((uint)(FieldSystem + Table.FacingOffset), [(byte)facing]);
            memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes((uint)y << 16));
        }

        private static GameProfile Profile()
        {
            var hgss = HgssProfile.Create(new PokemonMetadataService(null!), new MapService(HgssProfile.DataDirectory), new GraphService());
            return new GameProfile(hgss.Flavor, hgss.Capabilities, [0], hgss.Addresses, new Pk4Parser(), hgss.BattleParser,
                hgss.CreateOverworldReader, hgss.Maps, hgss.Graph, hgss.Pathfinding, hgss.CreateStateReader);
        }

        // A launched bot at position 0, standing and seeing what the test says, and the service whose Run checks see it.
        private static async Task<BotService> LaunchAsync(int map, int x, int y, GameScreen screen, params byte[][] party)
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 1 } });
            var factory = new FakeBotFactory(settings, Task.CompletedTask, Profile());
            var provider = new ServiceCollection().AddSingleton(new PokemonStateReader()).BuildServiceProvider();
            var service = new BotService(settings, factory, provider, null!, null!, null!);
            await service.ExecuteNewBotOrSpecialEvent(new NewEventArguments { EventName = "Launch Bot", GameName = "fake.nds" });
            FakeBot bot = factory.Bots[0];
            FakeMemoryClient memory = new();
            Stand(memory, map, x, y, Direction.Up);
            bot.Overworld = new HgssOverworldReader(memory, Table);
            bot.State = new OneSnapshot(screen);
            bot.PartyBlocks = party;
            return service;
        }

        private static NewEventArguments Run(string? encounter = "Dialga (Sinjoh Ruins)", string? phase = "Dialga")
            => new() { EventName = "HGSS Static Encounter", PhaseName = phase, StaticEncounter = encounter };

        private static async Task AssertRefusedAsync(BotService service, NewEventArguments args, string sentence)
        {
            var result = await new BotController(service).StartAction(0, args);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal(sentence, content.Content);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }

        // Run starts the hunt; cancelled at once, because the hunt's steps are HgssStaticEncounterEventTests'.
        private static async Task AssertStartedAsync(BotService service)
        {
            var result = await new BotController(service).StartAction(0, Run());

            Assert.IsType<OkObjectResult>(result.Result);
            await service.CancelBotOrEventAsync(0);
        }

        [Fact]
        public async Task OffTheStageIsRefused()
        {
            var service = await LaunchAsync(315, 16, 14, GameScreen.FieldFree, Block(493));

            await AssertRefusedAsync(service, Run(), OffTheStage);
        }

        [Fact]
        public async Task FieldNotFreeIsRefused()
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldTask, Block(493));

            await AssertRefusedAsync(service, Run(), FieldNotFree);
        }

        public static TheoryData<byte[][]> NotOneArceus_Parties => new()
        {
            new[] { Block(493), Block(483) },
            new[] { Block(483) },
            new[] { Block(493, egg: true) },
        };

        [Theory]
        [MemberData(nameof(NotOneArceus_Parties))]
        public async Task PartyIsNotOneArceusIsRefused(byte[][] party)
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldFree, party);

            await AssertRefusedAsync(service, Run(), NotOneArceus);
        }

        public static TheoryData<byte[]> NeitherEventNorHallOfOrigin => new()
        {
            Block(493, fateful: false, version: GameVersion.HG, metLocation: 86),
            Block(493, fateful: false, version: GameVersion.Pt, metLocation: 85),
        };

        [Theory]
        [MemberData(nameof(NeitherEventNorHallOfOrigin))]
        public async Task ArceusNeitherEventNorHallOfOriginIsRefused(byte[] arceus)
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldFree, arceus);

            await AssertRefusedAsync(service, Run(), "This Arceus is not the event Arceus or the Hall of Origin one.");
        }

        [Fact]
        public async Task FatefulArceusPasses()
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldFree, Block(493, fateful: true, version: GameVersion.HG));

            await AssertStartedAsync(service);
        }

        [Fact]
        public async Task HallOfOriginPlatinumArceusPasses()
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldFree, Block(493, fateful: false, version: GameVersion.Pt, metLocation: 86));

            await AssertStartedAsync(service);
        }

        [Fact]
        public async Task NoPathIsRefused()
        {
            // (8, 1) is walkable in the shipped map, but the stage's left edge walls it off from every circle.
            var service = await LaunchAsync(522, 8, 1, GameScreen.FieldFree, Block(493));

            await AssertRefusedAsync(service, Run(), NoPath);
        }

        [Fact]
        public async Task AllChecksPassStartTheHunt()
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldFree, Block(493));

            await AssertStartedAsync(service);
        }

        [Fact]
        public async Task UnknownEncounterIsRefused()
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldFree, Block(493));

            await AssertRefusedAsync(service, Run(encounter: "Mew"),
                "'Mew' is not a static encounter. Choose one of: Dialga (Sinjoh Ruins), Palkia (Sinjoh Ruins), Giratina (Sinjoh Ruins).");
        }

        [Fact]
        public async Task MissingMilestoneIsRefused()
        {
            var service = await LaunchAsync(522, 16, 14, GameScreen.FieldFree, Block(493));

            await AssertRefusedAsync(service, Run(phase: null), "PhaseName cannot be null for the static encounter hunt.");
        }

        [Fact]
        public void PathfinderReachesEverySpotFromTheSaveTile()
        {
            GameProfile profile = Profile();

            foreach (StaticEncounterConfig.Entry entry in StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath).Encounters)
            {
                string? macro = profile.Pathfinding!.Pathfind(entry.MapId, 16, 14, entry.Spot[0], entry.Spot[1], new Capability(), (byte)Direction.Up);

                Assert.False(macro is null, $"No path from (16, 14) to {entry.Name}'s spot.");
            }
        }
    }
}
