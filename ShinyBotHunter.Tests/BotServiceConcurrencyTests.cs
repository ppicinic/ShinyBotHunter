using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Context;
using ShinyBotHunter.Games;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    internal class FakeBotFactory : BotFactory
    {
        private readonly Task launch;
        private readonly GameProfile? profile;

        public FakeBotFactory(IOptions<ApplicationSettings> settings, Task launch, GameProfile? profile) : base(settings, null!, null!, null!)
        {
            this.launch = launch;
            this.profile = profile;
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
            var bot = new FakeBot(launch, profile);
            Bots.Add(bot);
            return bot;
        }
    }

    /// <summary>
    /// The BotService gate: launches are serialised, so two at once get different positions and a
    /// launch past the emulator budget is refused before anything is built. No sleeps: a launch is
    /// held open on a task the test completes, and the first caller reaches it synchronously.
    /// </summary>
    public class BotServiceConcurrencyTests
    {
        private class ThrowingEvent : IBotEvent
        {
            // Faults the returned task rather than throwing, because StartEvent calls this inline.
            public Task ExecuteAsync(GameState state, CancellationToken cancellationToken) =>
                Task.FromException(new InvalidOperationException("Event crashed"));
        }

        private static readonly NewEventArguments LaunchBot = new() { EventName = "Launch Bot", GameName = "fake.nds" };

        private static (BotService Service, FakeBotFactory Factory) Build(Task launch, GameProfile? profile = null)
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            var factory = new FakeBotFactory(settings, launch, profile);
            // The launch and idle-cancel paths touch neither the hub, the provider nor the ROM index.
            return (new BotService(settings, factory, null!, null!, null!, null!), factory);
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
        public async Task CrashedEvent_LeavesBotIdleWithNoGameState()
        {
            var slots = new EmulatorSlotRegistry(2);
            var runner = new BotEventRunner(slots, () => Task.CompletedTask);
            var state = new GameState();
            state.State.Push(EventState.LevelUp);
            var entry = new BotAndOrEvent(new FakeBot(Task.CompletedTask)) { GameState = state };
            slots.Entries[0] = entry;

            await slots.Gate.WaitAsync();
            runner.StartEvent(0, entry, new ThrowingEvent(), state, "Route 33", removeWhenDone: false);
            Task running = entry.EventTask!;
            slots.Gate.Release();
            await running;

            Assert.NotNull(entry.Bot);
            Assert.Null(entry.Event);
            Assert.Null(entry.EventTask);
            Assert.Null(entry.CancellationTokenSource);
            Assert.Null(entry.GameState);
            Assert.True(slots.Entries.ContainsKey(0));
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
