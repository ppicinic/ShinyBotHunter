using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Service.Pointers
{
    /// <summary>
    /// Ruby/Sapphire memory map.
    ///
    /// One table covers both games at every revision. Measured from the pret symbol tables: all 403
    /// EWRAM and 324 IWRAM symbols sit at identical addresses across Ruby and Sapphire and across
    /// rev0/rev1/rev2. Only ROM-space symbols move between revisions, and the bot reads no ROM.
    ///
    /// Unlike Gen 4 these are absolute, not offsets from a pointer that moves: Gen 3 does not
    /// relocate its save block, so the party is simply at a fixed address. That is why
    /// <see cref="PartyAddressing"/> is Absolute -- dereferencing <see cref="Anchor"/> would read the
    /// party's own first bytes as a pointer.
    /// </summary>
    public class RsPointerService : IPointerService
    {
        // gPlayerPartyCount = 0x03004350, gPlayerParty = 0x03004360, both in IWRAM. Anchor is the
        // count, so the party sits a fixed 0x10 beyond it and the existing offset fields still apply.
        public uint Anchor => 0x03004350;

        public int PartySize => 0;

        public int PartySlots => 0x10;

        /// <summary>A party-sized Gen 3 pokemon: 80 bytes stored plus 20 of battle stats.</summary>
        public int PartyPokemonSize => 100;

        /// <summary>A box-sized one omits the battle stats.</summary>
        public int BoxPokemonSize => 80;

        public PartyAddressing PartyAddressing => PartyAddressing.Absolute;

        public PokemonScheme PokemonScheme => PokemonScheme.Gen3;

        // gEnemyPartyCount = 0x030045B8, gEnemyParty = 0x030045C0.
        public uint EnemyAnchor => 0x030045B8;

        public uint EnemyCount => 0;

        public uint Enemy => 0x8;

        // gBattleMons = 0x02024A80, the in-battle mirrors rather than the stored structures.
        public uint BattlePokemon => 0x02024A80;

        public uint BattleEnemyPokemon => 0x02024AE4;

        public uint BattlePartyPokemon => 0x03004360;

        // gBattleTypeFlags. Non-zero once a battle is under way, which is what the starter hunt waits
        // on before the chosen pokemon can be read.
        public uint BattleMenuState => 0x020239F8;

        // Box storage lives inside the save block rather than at a standalone address, and nothing in
        // the starter hunt reads it. Left unimplemented so a caller that needs it fails saying so
        // rather than reading an address that was guessed.
        public BoxLayout? BoxLayout => null;
    }
}
