using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Rs
{
    public static class RsProfile
    {
        private static readonly RsPointerService Table = new();

        private static IPointerService Addresses(int revision) => revision is 1 or 2
            ? Table
            : throw new NotSupportedException($"RS revision {revision}: no address table. Hunts run on revisions 1 and 2.");

        public static GameProfile Create(PokemonMetadataService metadata) => new(
            GameFlavor.RS,
            new GameCapabilities(
                HasOverworldNavigation: false,
                HasWildEncounters: false,
                HasEggs: false,
                CanReadBoxes: false,
                HasSoftResetStarterHunt: true,
                HasShinyStarterHunt: false),
            revisions: [1, 2],
            Addresses,
            new Gen3PokemonParser(metadata),
            battleParser: null);
    }
}
