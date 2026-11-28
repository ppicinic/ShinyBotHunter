using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Context;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// What an RS bot is offered for Wild Encounter, and what Run refuses (docs/specs/rs-wild-encounter.md FR-1, FR-12).
    /// A party block is a little-endian HP, the level, then a little-endian PID, which the stub parser reads back.
    /// </summary>
    public class RsWildEncounterStartTests
    {
        private sealed class HpLevelPidParser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw) => new PokemonData
            {
                HP = BitConverter.ToUInt16(raw, 0),
                Level = raw[2],
                PID = BitConverter.ToUInt32(raw, 3),
            };
        }

        private sealed class ScreenReader : IGameStateReader
        {
            public GameScreen Screen { get; set; } = GameScreen.FieldFree;

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new GameSnapshot(Screen, null, null, null, null));
        }

        private const string NoHeadbuttTrees = "Ruby and Sapphire have no Headbutt trees.";
        private const string NoTrainingAddresses = "Training needs a US Ruby or Sapphire ROM (game code AXVE or AXPE).";
        private const string NoPickup = "Pickup is not available on Ruby and Sapphire.";
        private const string StandInTheField = "Close any menu or message and stand in the field.";
        private const string NotTheFirstObject = "The player is not the first object on this map, which the bot watches as it turns.";
        private const string LeadFainted = "The first Pokémon in the party has fainted.";

        private static readonly NewEventArguments LaunchBot = new() { EventName = "Launch Bot", GameName = "fake.nds" };

        // TID and SID are 0 in the stub, so a PID of 0 is shiny and 0x12345678 is not.
        private static byte[] Mon(ushort hp, byte level, uint pid = 0x12345678) => [.. BitConverter.GetBytes(hp), level, .. BitConverter.GetBytes(pid)];

        private static readonly byte[][] OneHealthy = [Mon(20, 7)];

        private static async Task<(BotService Service, FakeBot Bot, ScreenReader Screen, FakeMemoryClient Memory)> LaunchAsync(byte[][] party,
            GameCapabilities? capabilities = null, IPointerService? table = null)
        {
            var rs = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()));
            // The shared table unless a test gives a game's: the shared one has no per-game part, so it cannot train.
            var profile = new GameProfile(rs.Flavor, capabilities ?? rs.Capabilities, [0], (_, _) => table ?? new RsPointerService(), new HpLevelPidParser(),
                rs.BattleParser, rs.CreateOverworldReader, rs.Maps, rs.Graph, rs.Pathfinding, rs.CreateStateReader, rs.CreateBattleEvent, rs.SaveGame,
                createNavigatorEvent: rs.CreateNavigatorEvent, trainingRefusal: rs.TrainingRefusal);
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            var factory = new FakeBotFactory(settings, Task.CompletedTask, profile);
            var provider = new ServiceCollection().AddSingleton<PokemonStateReader>().BuildServiceProvider();
            // A ROM directory never scanned: no game file has a version, so no phase name is a generated milestone.
            var service = new BotService(settings, factory, provider, null!, new RomInfoService(settings, NullLogger<RomInfoService>.Instance), new GameProfiles(profile));
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);
            FakeBot bot = factory.Bots[0];
            bot.PartyBlocks = party;
            var screen = new ScreenReader();
            bot.State = screen;
            var memory = new FakeMemoryClient();
            bot.Overworld = new RsOverworldReader(memory, new RsPointerService());
            return (service, bot, screen, memory);
        }

        private static NewEventArguments Run(Action<NewEventArguments>? change = null)
        {
            var args = new NewEventArguments { EventName = "Wild Encounter", PhaseName = "x", TargetType = "ShinyTarget", EncounterType = EncounterType.Run };
            change?.Invoke(args);
            return args;
        }

        private static async Task<string?> RefusalAsync(BotService service, NewEventArguments args)
        {
            var result = await new BotController(service).StartAction(0, args);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
            return content.Content;
        }

        [Fact]
        public async Task IdleRsBot_ListsWildEncounterWithTheCapabilityReasons()
        {
            var (service, _, _, _) = await LaunchAsync(OneHealthy);

            var actions = await service.GetBotActionsAsync(0);

            var wild = Assert.Single(actions);
            Assert.Equal("Wild Encounter", wild.Name);
            var disabled = wild.DisabledChoices!;
            // FR-18a: the party knows no Headbutt and has no Pickup member; the capability reasons win. No Target and the train
            // types are offered.
            Assert.Equal(["EncounterType", "Pickup"], disabled.Keys);
            Assert.Equal(["Headbutt"], disabled["EncounterType"].Keys);
            Assert.Equal(NoHeadbuttTrees, disabled["EncounterType"]["Headbutt"]);
            Assert.Equal(["true"], disabled["Pickup"].Keys);
            Assert.Equal(NoPickup, disabled["Pickup"]["true"]);
        }

        [Fact]
        public void DisabledByCapabilities_Hgss_IsEmpty()
        {
            GameProfile hgss = HgssProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()), new GraphService());

            Assert.Empty(BotService.DisabledByCapabilities(hgss.Capabilities));
        }

        [Fact]
        public void DisabledByCapabilities_NavigatingTrainingProfile_HeadbuttAndPickupOnly()
        {
            // RS as T3b will declare it: navigation, training and the run checks, none of HGSS's own members.
            GameCapabilities caps = new(
                HasOverworldNavigation: true,
                HasWildEncounters: true,
                HasTraining: true,
                HasEggs: false,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: true,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: true,
                HasWildRunChecks: true);

            var disabled = BotService.DisabledByCapabilities(caps);

            Assert.Equal(["EncounterType", "Pickup"], disabled.Keys);
            Assert.Equal(["Headbutt"], disabled["EncounterType"].Keys);
            Assert.Equal(NoHeadbuttTrees, disabled["EncounterType"]["Headbutt"]);
            Assert.Equal(["true"], disabled["Pickup"].Keys);
            Assert.Equal(NoPickup, disabled["Pickup"]["true"]);
        }

        [Fact]
        public async Task StartWithHeadbutt_Is400WithoutARead()
        {
            var (service, bot, _, _) = await LaunchAsync(OneHealthy);

            Assert.Equal(NoHeadbuttTrees, await RefusalAsync(service, Run(a => a.EncounterType = EncounterType.Headbutt)));
            Assert.Equal(0, bot.PartyReads);
        }

        [Fact]
        public async Task StartWithNoTargetAndATrainMode_ReachesTheRunChecks()
        {
            var (service, _, screen, _) = await LaunchAsync(OneHealthy, table: RsPointerService.For("AXPE"));
            screen.Screen = GameScreen.FieldTask;

            Assert.Equal(StandInTheField, await RefusalAsync(service, Run(a => (a.TargetType, a.TrainType) = ("NoTarget", TrainType.Standard))));
        }

        // FR-18a: the RS checks at Run with a train mode too, each with its sentence.
        [Theory]
        [InlineData(TrainType.Standard, "field")]
        [InlineData(TrainType.Standard, "object")]
        [InlineData(TrainType.Standard, "empty")]
        [InlineData(TrainType.Standard, "fainted")]
        [InlineData(TrainType.Switch, "field")]
        [InlineData(TrainType.Switch, "object")]
        [InlineData(TrainType.Switch, "empty")]
        [InlineData(TrainType.Switch, "fainted")]
        public async Task StartWithATrainType_RunChecksStillRun(TrainType trainType, string check)
        {
            byte[][] party = check switch { "empty" => [], "fainted" => [Mon(0, 7), Mon(20, 9)], _ => OneHealthy };
            var (service, _, screen, memory) = await LaunchAsync(party, table: RsPointerService.For("AXPE"));
            if (check == "field")
            {
                screen.Screen = GameScreen.FieldTask;
            }
            if (check == "object")
            {
                memory.Place(new RsPointerService().PlayerObjectId, [1]);
            }

            string? refusal = await RefusalAsync(service, Run(a => a.TrainType = trainType));

            Assert.Equal(check switch { "field" => StandInTheField, "object" => NotTheFirstObject, _ => LeadFainted }, refusal);
        }

        // Switch on RS reaches HGSS's leg rules, where the battler's slot is 1-based, from 2 to 6.
        [Fact]
        public async Task StartWithSwitch_ReachesTheSlotRule()
        {
            var (service, _, _, _) = await LaunchAsync([Mon(20, 7), Mon(20, 9)], table: RsPointerService.For("AXPE"));

            Assert.Equal("SwitchSlot must be between 2 and 6 for Switch train mode.", await RefusalAsync(service,
                Run(a => (a.EncounterType, a.TrainType, a.TargetLevel, a.SwitchSlot) = (EncounterType.Spin, TrainType.Switch, 20, 1))));
        }

        // FR-12: a train mode on a game code with no per-game part is refused before anything is read.
        [Theory]
        [InlineData(TrainType.Standard)]
        [InlineData(TrainType.Switch)]
        public async Task StartWithATrainType_UnknownGameCode_Is400WithoutARead(TrainType trainType)
        {
            var (service, bot, _, _) = await LaunchAsync(OneHealthy);

            Assert.Equal(NoTrainingAddresses, await RefusalAsync(service, Run(a => a.TrainType = trainType)));
            Assert.Equal(0, bot.PartyReads);
        }

        [Fact]
        public async Task StartWithPickup_Is400WithThePickupSentence()
        {
            var (service, bot, _, _) = await LaunchAsync(OneHealthy, table: RsPointerService.For("AXPE"));

            Assert.Equal(NoPickup, await RefusalAsync(service, Run(a => (a.Pickup, a.TrainType) = (true, TrainType.Standard))));
            Assert.Equal(0, bot.PartyReads);
        }

        [Fact]
        public async Task StartWithTheFieldNotFree_Is400()
        {
            var (service, _, screen, _) = await LaunchAsync(OneHealthy);
            screen.Screen = GameScreen.FieldTask;

            Assert.Equal(StandInTheField, await RefusalAsync(service, Run()));
        }

        [Fact]
        public async Task StartWithThePlayerNotObjectZero_Is400()
        {
            var (service, _, _, memory) = await LaunchAsync(OneHealthy);
            memory.Place(new RsPointerService().PlayerObjectId, [1]);

            Assert.Equal(NotTheFirstObject, await RefusalAsync(service, Run()));
        }

        [Fact]
        public async Task StartWithAnEmptyParty_Is400()
        {
            var (service, _, _, _) = await LaunchAsync([]);

            Assert.Equal(LeadFainted, await RefusalAsync(service, Run()));
        }

        [Fact]
        public async Task StartWithTheLeadAtZeroHp_Is400()
        {
            var (service, _, _, _) = await LaunchAsync([Mon(0, 7), Mon(20, 9)]);

            Assert.Equal(LeadFainted, await RefusalAsync(service, Run()));
        }

        [Fact]
        public async Task StartWithSixInTheParty_ReachesTheRangeRules()
        {
            var (service, _, _, _) = await LaunchAsync([.. Enumerable.Repeat(Mon(20, 7), 6)]);

            Assert.Equal("A x or y range must be supplied for Run Encounter Event", await RefusalAsync(service, Run()));
        }

        [Fact]
        public async Task StartWithPartyReadsFresh()
        {
            var (service, bot, _, _) = await LaunchAsync(OneHealthy);
            await service.GetBotActionsAsync(0);
            bot.PartyBlocks = [Mon(0, 7)];

            Assert.Equal(LeadFainted, await RefusalAsync(service, Run()));
            Assert.Equal(2, bot.PartyReads);
        }

        [Fact]
        public async Task StartThatPassesTheChecks_ReachesTheRangeRules()
        {
            var (service, _, _, _) = await LaunchAsync(OneHealthy);

            Assert.Equal("A x or y range must be supplied for Run Encounter Event", await RefusalAsync(service, Run()));
        }

        [Theory]
        [InlineData(true, StandInTheField)]
        [InlineData(false, "A x or y range must be supplied for Run Encounter Event")]
        public async Task RunChecks_FollowHasWildRunChecks(bool hasWildRunChecks, string refusal)
        {
            // A training profile, so Standard is not greyed; the field is not free, which only the run checks read.
            GameCapabilities caps = new(
                HasOverworldNavigation: true,
                HasWildEncounters: true,
                HasTraining: true,
                HasEggs: false,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: false,
                HasWildRunChecks: hasWildRunChecks);
            var (service, _, screen, _) = await LaunchAsync(OneHealthy, caps, RsPointerService.For("AXPE"));
            screen.Screen = GameScreen.FieldTask;

            Assert.Equal(refusal, await RefusalAsync(service, Run(a => a.TrainType = TrainType.Standard)));
        }

        [Fact]
        public void SpeedLinesAtRun_ShinyLeadGivesTheShinyLine()
        {
            // PID, TID and SID all zero: shiny.
            Assert.Equal(["A shiny first Pokémon adds about 1.2 s to every battle."], BotService.SpeedLinesAtRun(new PokemonData()));
        }

        [Fact]
        public void SpeedLinesAtRun_PlainLeadGivesNone()
        {
            Assert.Empty(BotService.SpeedLinesAtRun(new PokemonData { PID = 0x12345678 }));
        }
    }
}
