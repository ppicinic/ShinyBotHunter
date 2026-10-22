using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class GameProfilesTests
    {
        private static readonly GameProfile Hgss = HgssProfile.Create(new PokemonMetadataService(null!));

        [Fact]
        public void ReturnsTheProfileForItsFlavor()
        {
            Assert.Same(Hgss, new GameProfiles(Hgss).Get(GameFlavor.HGSS));
        }

        [Fact]
        public void UnregisteredFlavorThrows()
        {
            // This is the error a half-implemented flavor produces, so it has to name the flavor
            // rather than surfacing as a bare lookup failure.
            var ex = Assert.Throws<InvalidOperationException>(() => new GameProfiles(Hgss).Get(GameFlavor.RS));

            Assert.Contains("RS", ex.Message);
        }
    }
}
