using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Context;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Headbutt is offered only when a party member knows it (FR-3). The party blocks are four
    /// little-endian move ids, which the stub parser reads back as Move1 to Move4.
    /// </summary>
    public class HeadbuttAvailabilityTests
    {
        private sealed class MovesParser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw) => new PokemonData
            {
                Move1 = BitConverter.ToUInt16(raw, 0),
                Move2 = BitConverter.ToUInt16(raw, 2),
                Move3 = BitConverter.ToUInt16(raw, 4),
                Move4 = BitConverter.ToUInt16(raw, 6),
            };
        }

        private sealed class ThrowingEvent : IBotEvent
        {
            // Faults the returned task rather than throwing, because StartEvent calls this inline.
            public Task ExecuteAsync(GameState state, CancellationToken cancellationToken) =>
                Task.FromException(new InvalidOperationException("Event crashed"));
        }

        private const string NoHeadbutt = "No Pokémon in your party knows Headbutt.";
        private const string ReadFailed = "The party could not be read; try again once the game is in the field.";

        private static readonly NewEventArguments LaunchBot = new() { EventName = "Launch Bot", GameName = "fake.nds" };

        private static byte[] Moves(ushort move1, ushort move2, ushort move3, ushort move4)
            => [.. BitConverter.GetBytes(move1), .. BitConverter.GetBytes(move2), .. BitConverter.GetBytes(move3), .. BitConverter.GetBytes(move4)];

        private static readonly byte[][] WithoutHeadbutt = [Moves(33, 45, 0, 0), Moves(10, 0, 0, 0)];
        private static readonly byte[][] WithHeadbutt = [Moves(33, 45, 0, 0), Moves(10, 0, 0, 0), Moves(52, 43, 98, 29)];

        private static async Task<(BotService Service, FakeBot Bot)> LaunchAsync(byte[][] party)
        {
            var hgss = HgssProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()), new GraphService());
            var profile = new GameProfile(hgss.Flavor, hgss.Capabilities, [0], hgss.Addresses, new MovesParser(), hgss.BattleParser,
                hgss.CreateOverworldReader, hgss.Maps, hgss.Graph, hgss.Pathfinding, hgss.CreateStateReader);
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            var factory = new FakeBotFactory(settings, Task.CompletedTask, profile);
            var provider = new ServiceCollection().AddSingleton<PokemonStateReader>().BuildServiceProvider();
            var service = new BotService(settings, factory, provider, null!, null!, null!);
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);
            FakeBot bot = factory.Bots[0];
            bot.Party = party;
            return (service, bot);
        }

        private static EventArgumentOptions Wild(List<EventArgumentOptions> actions) => actions.Single(a => a.Name == "Wild Encounter");
        private static EventArgumentOptions Chained(List<EventArgumentOptions> actions) => actions.Single(a => a.Name == "Chained Encounter");

        [Fact]
        public async Task HeadbuttAsSlotThreesFourthMove_IsEnabled()
        {
            var (service, _) = await LaunchAsync(WithHeadbutt);

            var actions = await service.GetBotActionsAsync(0);

            Assert.Null(Wild(actions).DisabledChoices);
            Assert.Null(Chained(actions).LegParameters!.DisabledChoices);
        }

        [Fact]
        public async Task NoPartyMemberKnowsHeadbutt_DisablesItWithTheSentence()
        {
            var (service, _) = await LaunchAsync(WithoutHeadbutt);

            var actions = await service.GetBotActionsAsync(0);

            Assert.Equal(NoHeadbutt, Wild(actions).DisabledChoices!["EncounterType"]["Headbutt"]);
            Assert.Equal(NoHeadbutt, Chained(actions).LegParameters!.DisabledChoices!["EncounterType"]["Headbutt"]);
            Assert.Null(actions.Single(a => a.Name == "HGSS Togepi Egg").DisabledChoices);
        }

        [Fact]
        public async Task PartyReadThatThrows_GivesTheReadSentence()
        {
            var (service, bot) = await LaunchAsync(WithHeadbutt);
            bot.PartyReadError = new TimeoutException("party read timed out");

            var actions = await service.GetBotActionsAsync(0);

            Assert.Equal(ReadFailed, Wild(actions).DisabledChoices!["EncounterType"]["Headbutt"]);
            Assert.Equal(ReadFailed, Chained(actions).LegParameters!.DisabledChoices!["EncounterType"]["Headbutt"]);
        }

        [Fact]
        public async Task TwoCalls_MakeOneRead()
        {
            var (service, bot) = await LaunchAsync(WithoutHeadbutt);

            await service.GetBotActionsAsync(0);
            await service.GetBotActionsAsync(0);

            Assert.Equal(1, bot.PartyReads);
        }

        [Fact]
        public async Task EventStartAndEnd_ClearTheAnswer()
        {
            var slots = new EmulatorSlotRegistry(2);
            var runner = new BotEventRunner(slots, () => Task.CompletedTask);
            var state = new GameState();
            var entry = new BotAndOrEvent(new FakeBot(Task.CompletedTask)) { GameState = state, HeadbuttRefusal = Task.FromResult<string?>(NoHeadbutt) };
            slots.Entries[0] = entry;

            await slots.Gate.WaitAsync();
            runner.StartEvent(0, entry, new ThrowingEvent(), state, "Route 33", removeWhenDone: false);
            Assert.Null(entry.HeadbuttRefusal);
            entry.HeadbuttRefusal = Task.FromResult<string?>(NoHeadbutt);
            Task running = entry.EventTask!;
            slots.Gate.Release();
            await running;

            Assert.Null(entry.HeadbuttRefusal);
        }

        [Fact]
        public async Task GetParty_ClearsTheAnswer()
        {
            var (service, bot) = await LaunchAsync(WithoutHeadbutt);
            await service.GetBotActionsAsync(0);
            bot.Party = WithHeadbutt;

            await service.ReloadPartyAsync(0);
            var actions = await service.GetBotActionsAsync(0);

            Assert.Null(Wild(actions).DisabledChoices);
            Assert.Equal(3, bot.PartyReads);
        }

        [Fact]
        public async Task RegistryOptionsAreNotMutated()
        {
            var (service, _) = await LaunchAsync(WithoutHeadbutt);

            var actions = await service.GetBotActionsAsync(0);

            EventArgumentOptions wild = EventRegistry.Get("Wild Encounter").Options;
            EventArgumentOptions chained = EventRegistry.Get("Chained Encounter").Options;
            Assert.Null(wild.DisabledChoices);
            Assert.Null(chained.LegParameters!.DisabledChoices);
            Assert.NotSame(wild, Wild(actions));
            Assert.NotSame(chained, Chained(actions));
            Assert.NotSame(chained.LegParameters, Chained(actions).LegParameters);
        }

        [Fact]
        public async Task StartWildEncounterWithHeadbutt_ReadsFreshAndIs400()
        {
            var (service, bot) = await LaunchAsync(WithHeadbutt);
            await service.GetBotActionsAsync(0);
            bot.Party = WithoutHeadbutt;
            var args = new NewEventArguments { EventName = "Wild Encounter", PhaseName = "x", TargetType = "ShinyTarget", EncounterType = EncounterType.Headbutt };

            var result = await new BotController(service).StartAction(0, args);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal(NoHeadbutt, content.Content);
            Assert.Equal(2, bot.PartyReads);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }

        [Fact]
        public async Task StartChainedEncounterWithAHeadbuttLeg_ReadsFreshAndIs400()
        {
            var (service, bot) = await LaunchAsync(WithHeadbutt);
            await service.GetBotActionsAsync(0);
            bot.Party = WithoutHeadbutt;
            var args = new NewEventArguments
            {
                EventName = "Chained Encounter",
                Legs =
                [
                    new() { PhaseName = "x", TargetType = "ShinyTarget", EncounterType = EncounterType.Spin },
                    new() { PhaseName = "y", TargetType = "ShinyTarget", EncounterType = EncounterType.Headbutt },
                ],
            };

            var result = await new BotController(service).StartAction(0, args);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal(NoHeadbutt, content.Content);
            Assert.Equal(2, bot.PartyReads);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }

        [Fact]
        public async Task StartWithHeadbuttWhenThePartyCannotBeRead_Is400WithTheReadSentence()
        {
            var (service, bot) = await LaunchAsync(WithHeadbutt);
            bot.PartyReadError = new TimeoutException("party read timed out");
            var args = new NewEventArguments { EventName = "Wild Encounter", PhaseName = "x", TargetType = "ShinyTarget", EncounterType = EncounterType.Headbutt };

            var result = await new BotController(service).StartAction(0, args);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal(ReadFailed, content.Content);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }
    }
}
