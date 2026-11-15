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

        // The wild encounter, docs/research/rs-wild-encounter.md "Memory read by the spike".

        // sLockFieldControls (u8), 0 in the free field; root (absolute; same at rev 0-2); observed; QA: none yet: rs-wild-sapphire
        public uint FieldControlsLock => 0x030006A4;

        // gMain.callback1 (u32), 0 on the title screen and the Continue menu; root (absolute; same at rev 0-2); observed;
        // QA: none yet: rs-wild-sapphire
        public uint Callback1 => 0x03001770;

        // gMain.inBattle, bit 1 of the byte at gMain + 0x43D; root (absolute; same at rev 0-2); observed;
        // QA: none yet: rs-wild-sapphire
        public uint InBattle => 0x03001BAD;

        // gBattlerControllerFuncs[0] (u32), the player's controller function; root (absolute; same at rev 0-2); observed;
        // QA: none yet: rs-wild-sapphire
        public uint PlayerControllerFunction => 0x03004330;

        // HandleInputChooseAction, the controller function at the action menu; ROM code below 0x0803FBDC, identical in all
        // six English ROMs; constant; observed; QA: none yet: rs-wild-sapphire
        public uint HandleInputChooseAction => 0x0802C099;

        // HandleInputChooseMove, the controller function at the move list; ROM code below 0x0803FBDC, identical in all
        // six English ROMs; constant; observed; QA: none yet: rs-wild-sapphire
        public uint HandleInputChooseMove => 0x0802C68D;

        // the battle Window at 0x03004210 + 0x16 (u16): 0 idle, 3 printing, 8 waiting for a button (5 and 9 decomp only);
        // root (absolute; same at rev 0-2); observed; QA: none yet: rs-wild-sapphire
        public uint BattleWindowState => 0x03004226;

        // gBattleOutcome (u8): 1 won, 4 ran (observed); 2 lost, 3 drew, 6 mon fled, 7 caught (decomp only); root
        // (absolute; same at rev 0-2); observed; QA: none yet: rs-wild-sapphire
        public uint BattleOutcome => 0x02024D26;

        // gBattleTypeFlags (u16), 0x0004 in a wild battle; root (absolute; same at rev 0-2); observed;
        // QA: none yet: rs-wild-sapphire
        public uint BattleTypeFlags => 0x020239F8;

        // gEnemyParty[0], 100 bytes, the wild Pokémon from the encounter roll; root (absolute; same at rev 0-2); observed;
        // QA: none yet: rs-wild-sapphire
        public uint EnemyParty => 0x030045C0;

        // gSaveBlock1.pos, s16 x then s16 y; root (absolute; same at rev 0-2); observed; QA: none yet: rs-wild-sapphire
        public uint PlayerPosition => 0x02025734;

        // gSaveBlock1.location, u8 group then u8 number; root (absolute; same at rev 0-2); observed;
        // QA: none yet: rs-wild-sapphire
        public uint PlayerLocation => 0x02025738;

        // gPlayerAvatar.objectEventId (u8), 0x0202E858 + 5; root (absolute; same at rev 0-2); observed;
        // QA: none yet: rs-wild-sapphire
        public uint PlayerObjectId => 0x0202E85D;

        // gObjectEvents[0] + 0x18, facing in the low nibble: 1 down, 2 up, 3 left, 4 right; root (absolute; same at
        // rev 0-2); observed; QA: none yet: rs-wild-sapphire
        public uint PlayerFacing => 0x030048B8;

        // The M action takes the upper half of four bytes, and gSaveBlock1.pos is two plain s16s: y's watch is x's own
        // address (PlayerPosition), x's is two bytes earlier (research doc "Walking instead of turning").
        // gSaveBlock1.pos - 2; root (absolute; same at rev 0-2); observed; QA: none yet: rs-wild-sapphire
        public uint RunWatchX => 0x02025732;

        // gSaveBlock1.pos; root (absolute; same at rev 0-2); observed; QA: none yet: rs-wild-sapphire
        public uint RunWatchY => 0x02025734;

        // Box storage lives inside the save block rather than at a standalone address, and nothing in
        // the starter hunt reads it. Left unimplemented so a caller that needs it fails saying so
        // rather than reading an address that was guessed.
        // no symbol; not mapped; QA: none yet: RS starter hunt
        public BoxLayout? BoxLayout => null;
    }
}
