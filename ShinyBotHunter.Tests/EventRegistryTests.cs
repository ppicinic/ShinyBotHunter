using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class EventRegistryTests
    {
        private static readonly NewEventArguments LaunchBot = new() { EventName = "Launch Bot", GameName = "fake.nds" };

        private static BotService Build(GameProfile? profile = null)
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            // The launch and idle paths touch neither the hub, the provider nor the ROM index.
            return new BotService(settings, new FakeBotFactory(settings, Task.CompletedTask, profile), null!, null!, null!, null!);
        }

        [Fact]
        public void NamesAreUnique()
        {
            var names = EventRegistry.All.Select(e => e.Name).ToList();

            Assert.Equal(names.Distinct(), names);
        }

        [Fact]
        public void EveryRequiresIsAMemberTestOverGameCapabilities()
        {
            var constructor = typeof(GameCapabilities).GetConstructors().Single();
            foreach (EventRegistration entry in EventRegistry.All)
            {
                if (entry.Requires is not { } need) continue;

                Assert.Equal(typeof(bool), typeof(GameCapabilities).GetProperty(need.Capability)?.PropertyType);
                var onlyThat = (GameCapabilities)constructor.Invoke(constructor.GetParameters().Select(p => (object)(p.Name == need.Capability)).ToArray());
                var allButThat = (GameCapabilities)constructor.Invoke(constructor.GetParameters().Select(p => (object)(p.Name != need.Capability)).ToArray());
                Assert.True(need.Has(onlyThat), $"'{entry.Name}': {need.Capability} alone does not satisfy its predicate.");
                Assert.False(need.Has(allButThat), $"'{entry.Name}': its predicate holds without {need.Capability}.");
            }
        }

        [Fact]
        public async Task IdleHgssBotSeesTodaysActions()
        {
            var service = Build();
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);

            var names = service.GetAvailableActions(0).Select(a => a.Name).ToList();

            Assert.Equal(["Wild Encounter", "Chained Encounter", "HGSS Togepi Egg"], names);
        }

        [Fact]
        public async Task RsBotSeesNoHgssActions()
        {
            var service = Build(RsProfile.Create(new PokemonMetadataService(null!)));
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);

            var names = service.GetAvailableActions(0).Select(a => a.Name).ToList();

            Assert.DoesNotContain("Wild Encounter", names);
            Assert.DoesNotContain("Chained Encounter", names);
            Assert.DoesNotContain("HGSS Togepi Egg", names);
            Assert.Empty(names);
        }

        [Fact]
        public async Task UnsupportedActionIs400()
        {
            var service = Build(RsProfile.Create(new PokemonMetadataService(null!)));
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);
            var args = new NewEventArguments { EventName = "Wild Encounter", PhaseName = "x", TargetType = "ShinyTarget", EncounterType = EncounterType.Run, xRange = 1 };

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteEvent(0, args));

            Assert.Contains("HasWildEncounters", ex.Message);
            // No GameState was assigned, so no event was built.
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }
    }
}
