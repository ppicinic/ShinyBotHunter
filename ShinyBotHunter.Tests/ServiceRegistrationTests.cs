using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShinyBotHunter;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Builds the real service graph and validates it.
    ///
    /// This exists because a registration mistake compiles perfectly and only fails when the
    /// application starts. Adding IPokemonParser as a keyed service did exactly that: keyed
    /// registrations do not satisfy plain constructor injection, so three unrelated services became
    /// unconstructible and nothing caught it until launch.
    ///
    /// ValidateOnBuild walks every descriptor's constructor without instantiating anything, so this
    /// needs no database or emulator.
    /// </summary>
    [Collection("macro-registry")]
    public class ServiceRegistrationTests
    {
        public ServiceRegistrationTests()
        {
            // Registration reads appsettings.json, which is resolved against the working directory.
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        /// <summary>
        /// What the web host contributes before CreateServices runs. Without these the graph fails on
        /// logging and SignalR, which are the host's job rather than a registration mistake.
        /// </summary>
        private static ServiceCollection HostServices()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSignalR();
            // SignalR's own connection plumbing wants the host lifetime. Stubbed rather than skipped,
            // so the hub contexts our services depend on are the real registrations.
            services.AddSingleton<IHostApplicationLifetime, StubHostLifetime>();
            return services;
        }

        private sealed class StubHostLifetime : IHostApplicationLifetime
        {
            public CancellationToken ApplicationStarted => CancellationToken.None;
            public CancellationToken ApplicationStopping => CancellationToken.None;
            public CancellationToken ApplicationStopped => CancellationToken.None;
            public void StopApplication() { }
        }

        [Fact]
        public void EveryRegisteredServiceCanBeConstructed()
        {
            ServiceCollection services = HostServices();
            new Startup().CreateServices(services);

            // Throws listing every service whose dependencies cannot be satisfied.
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            Assert.NotNull(provider);
        }

        [Theory]
        [InlineData(GameFlavor.HGSS)]
        [InlineData(GameFlavor.RS)]
        public void EveryPlayableFlavorHasItsPerGameServices(GameFlavor flavor)
        {
            ServiceCollection services = HostServices();
            new Startup().CreateServices(services);
            using var provider = services.BuildServiceProvider();

            // A flavor the dashboard will offer but that has no memory map or parser would fail at
            // the first read, long after the hunt reported itself as running.
            Assert.NotNull(provider.GetKeyedService<IPointerService>(flavor));
            Assert.NotNull(provider.GetKeyedService<IPokemonParser>(flavor));
        }
    }
}
