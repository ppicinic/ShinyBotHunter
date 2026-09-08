using Microsoft.Extensions.DependencyInjection;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    public class GameServicesTests
    {
        private static GameServices Build()
        {
            ServiceCollection services = new();
            services.AddKeyedSingleton<IPointerService>(GameFlavor.HGSS, new HgssPointerService());
            services.AddSingleton<GameServices>();
            return services.BuildServiceProvider().GetRequiredService<GameServices>();
        }

        [Fact]
        public void ResolvesTheServiceRegisteredForAFlavor()
        {
            Assert.IsType<HgssPointerService>(Build().Pointers(GameFlavor.HGSS));
        }

        [Fact]
        public void UnregisteredFlavor_NamesBothTheFlavorAndTheMissingService()
        {
            // This is the error a half-implemented flavor produces, so it has to say which piece is
            // missing rather than surfacing as a bare DI failure.
            var ex = Assert.Throws<InvalidOperationException>(() => Build().Pointers(GameFlavor.RS));

            Assert.Contains("RS", ex.Message);
            Assert.Contains("IPointerService", ex.Message);
        }
    }
}
