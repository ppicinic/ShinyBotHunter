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
        // gPlayerPartyCount; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint Anchor => 0x03004350;

        // gPlayerPartyCount; offset; QA: none yet: RS starter hunt
        public int PartySize => 0;

        // gPlayerParty; offset; QA: none yet: RS starter hunt
        public int PartySlots => 0x10;

        // no symbol; offset; QA: none yet: RS starter hunt
        /// <summary>A party-sized Gen 3 pokemon: 80 bytes stored plus 20 of battle stats.</summary>
        public int PartyPokemonSize => 100;

        // no symbol; offset; QA: none yet: RS starter hunt
        /// <summary>A box-sized one omits the battle stats.</summary>
        public int BoxPokemonSize => 80;

        // Layout flag, not an address.
        public PartyAddressing PartyAddressing => PartyAddressing.Absolute;

        // Layout flag, not an address.
        public PokemonScheme PokemonScheme => PokemonScheme.Gen3;

        // gEnemyPartyCount = 0x030045B8, gEnemyParty = 0x030045C0.
        // gEnemyPartyCount; offset; QA: none yet: RS starter hunt
        public uint EnemyCount => 0;

        // gEnemyParty; offset; QA: none yet: RS starter hunt
        public uint Enemy => 0x8;

        // gBattleMons = 0x02024A80, the in-battle mirrors rather than the stored structures.
        // gBattleMons; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint BattlePokemon => 0x02024A80;

        // no symbol; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint BattleEnemyPokemon => 0x02024AE4;

        // gPlayerParty; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint BattlePartyPokemon => 0x03004360;

        // gBattleCommunication; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint BattleCommunication => 0x02024D1E;

        // gActionSelectionCursor; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint ActionSelectionCursor => 0x02024E60;

        // sStartMenuCursorPos; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint StartMenuCursorPos => 0x0202E8FC;

        // sNumStartMenuActions; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint StartMenuActionCount => 0x0202E8FD;

        // sCurrentStartMenuActions; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt
        public uint StartMenuActions => 0x0202E8FE;

        // Box storage lives inside the save block rather than at a standalone address, and nothing in
        // the starter hunt reads it. Left unimplemented so a caller that needs it fails saying so
        // rather than reading an address that was guessed.
        // no symbol; not mapped; QA: none yet: RS starter hunt
        public BoxLayout? BoxLayout => null;
    }
}
