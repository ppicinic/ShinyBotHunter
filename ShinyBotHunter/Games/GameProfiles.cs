using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Games
{
    /// <summary>Hands out the profile for a flavor.</summary>
    public sealed class GameProfiles
    {
        // Never mutated after construction, so concurrent reads need no lock.
        private readonly Dictionary<GameFlavor, GameProfile> profiles;

        public GameProfiles(params GameProfile[] profiles)
        {
            this.profiles = profiles.ToDictionary(p => p.Flavor);
        }

        public GameProfile Get(GameFlavor flavor)
        {
            if (!profiles.TryGetValue(flavor, out GameProfile? profile))
            {
                throw new InvalidOperationException(
                    $"No game profile is registered for {flavor}. That game's memory layout and " +
                    "parsers have not been implemented, so it cannot be read from.");
            }
            return profile;
        }
    }
}
