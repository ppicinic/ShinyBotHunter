using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Hgss
{
    public static class HgssProfile
    {
        private static readonly HgssPointerService Table = new();

        private static IPointerService Addresses(int revision) => revision == 0
            ? Table
            : throw new NotSupportedException($"HGSS revision {revision}: no address table. Hunts run on revision 0.");

        public static GameProfile Create(PokemonMetadataService metadata) => new(
            GameFlavor.HGSS,
            new GameCapabilities(
                HasOverworldNavigation: true,
                HasWildEncounters: true,
                HasEggs: true,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: false,
                HasShinyStarterHunt: true),
            revisions: [0],
            Addresses,
            new HgssPokemonParser(metadata),
            new HgssBattlePokemonParser(),
            createOverworldReader: (memory, table) => new HgssOverworldReader(memory, (HgssPointerService)table));
    }
}
