using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The BotService gate: launches are serialised, so two at once get different positions and a
    /// launch past the emulator budget is refused before anything is built. No sleeps: a launch is
    /// held open on a task the test completes, and the first caller reaches it synchronously.
    /// </summary>
    public class BotServiceConcurrencyTests
    {
        private class FakeBotFactory : BotFactory
        {
            private readonly Task launch;

            public FakeBotFactory(IOptions<ApplicationSettings> settings, Task launch) : base(settings, null!, null!)
            {
                this.launch = launch;
            }

            public List<int> Positions { get; } = new();
            public List<FakeBot> Bots { get; } = new();

            public override GameContext BuildGameContext(string sourceGame, int gameId, int displayPosition, bool? soundOn = null)
            {
                Positions.Add(displayPosition);
                return new GameContext { SourceGameName = sourceGame, DisplayPosition = displayPosition };
            }

            public override IBot CreateBot(GameContext context)
            {
                var bot = new FakeBot(launch);
                Bots.Add(bot);
                return bot;
            }
        }

        private static readonly NewEventArguments LaunchBot = new() { EventName = "Launch Bot", GameName = "fake.nds" };

        private static (BotService Service, FakeBotFactory Factory) Build(Task launch)
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            var factory = new FakeBotFactory(settings, launch);
            // The launch and idle-cancel paths touch neither the hub, the provider nor the ROM index.
            return (new BotService(settings, factory, null!, null!, null!), factory);
        }

        [Fact]
        public async Task TwoConcurrentLaunches_GetPositionsZeroAndOne()
        {
            var launch = new TaskCompletionSource();
            var (service, factory) = Build(launch.Task);

            Task<Dictionary<int, EmulatorState>> first = service.ExecuteNewBotOrSpecialEvent(LaunchBot);
            Task<Dictionary<int, EmulatorState>> second = service.ExecuteNewBotOrSpecialEvent(LaunchBot);

            // The second call is waiting on the gate and has not computed a position yet.
            Assert.Equal([0], factory.Positions);

            launch.SetResult();
            await Task.WhenAll(first, second);

            Assert.Equal([0, 1], factory.Positions);
            Assert.Equal([0, 1], service.GetBotDashboardState().Keys.Order());
        }

        [Fact]
        public async Task LaunchPastMaxEmulators_IsRefusedBeforeAnyLaunch()
        {
            var (service, factory) = Build(Task.CompletedTask);
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);

            var ex = await Assert.ThrowsAsync<Exception>(() => service.ExecuteNewBotOrSpecialEvent(LaunchBot));

            Assert.Contains("No open space", ex.Message);
            Assert.Equal(2, factory.Bots.Count);
        }

        [Fact]
        public async Task CancelOnIdleBot_DisposesItAndFreesThePosition()
        {
            var (service, factory) = Build(Task.CompletedTask);
            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);

            var state = await service.CancelBotOrEventAsync(0);

            Assert.True(factory.Bots[0].Disposed);
            Assert.Empty(state);

            await service.ExecuteNewBotOrSpecialEvent(LaunchBot);
            Assert.Equal([0, 0], factory.Positions);
        }

        [Fact]
        public void LaunchBesideEntryKeyedAtZeroReportingOne_GetsPositionTwo()
        {
            // A hunt keyed at 0 whose copy at 0 was parked, so it reports only position 1.
            var slots = new EmulatorSlotRegistry(3);
            slots.Entries[0] = new BotAndOrEvent { BotPosition = 1 };

            var (occupied, next) = slots.AllocatePosition();

            Assert.Equal(2, next);
            Assert.Equal([0, 1], occupied);
        }
    }
}
