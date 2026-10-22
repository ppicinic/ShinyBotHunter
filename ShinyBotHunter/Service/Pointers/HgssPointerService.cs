using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Service.Pointers
{
    public class HgssPointerService : IPointerService
    {
        // sFieldSysPtr (FieldSystem *; the spec's FieldSystem); root (revision-dependent); QA: hgss-route33, hgss-route33-train
        public uint Anchor => 0x21D4158;

        // no symbol in pointer-discovery.md; root (revision-dependent); QA: hgss-route33, hgss-route33-train (Run macro)
        public uint TrainerX => 0x21DA6F4;

        // no symbol in pointer-discovery.md; root (revision-dependent); QA: hgss-route33, hgss-route33-train (Run macro)
        public uint TrainerY => 0x21DA6FC;

        // no symbol in pointer-discovery.md (byte inside the overlay loaded at 0x021E5900); root (revision-dependent);
        // QA: hgss-route33, hgss-route33-train; legacy; replaced by docs/specs/hgss-state-reader.md
        public uint BattleIndicator => 0x21E76D2;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train (start tile)
        public int MapIdOffset => -0x22DA4;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: none yet: spin encounter, navigation, Togepi egg
        public int FacingOffset => 0x1DC4;

        // OverlayManager.proc_state via a heap coincidence; offset (revision-independent); QA: none yet: evolution;
        // legacy; replaced by docs/specs/hgss-state-reader.md
        public int BattleStateOffset => 0x692C;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: none yet: HGSS starter hunt
        public uint StarterPreviewOffset => 0x1BC00;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public uint BattlePokemon => 0x25E2C;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public uint BattleEnemyPokemon => 0x25EEC;
        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public uint BattlePartyPokemon => 0x2AAB0;
        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public int PartySize => -0x23F44;

        // BattleSetup via a heap coincidence (the spec's EnemySetupOffset); offset (revision-independent);
        // QA: hgss-route33, hgss-route33-train; legacy; replaced by docs/specs/hgss-state-reader.md
        public uint EnemyAnchor => 0x6930;

        // no symbol in pointer-discovery.md (the spec's EnemyCountOffset); offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public uint EnemyCount => 0xC14;

        // no symbol in pointer-discovery.md (the spec's EnemyOffset); offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public uint Enemy => 0xC18;

        // BattleContext.unk_0[0] via a heap coincidence (the spec's BattleMenuStateOffset); offset (revision-independent);
        // QA: hgss-route33, hgss-route33-train; legacy; replaced by docs/specs/hgss-state-reader.md
        public uint BattleMenuState => 0x230EC;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public int PartySlots => -0x23F40;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public int PartyPokemonSize => 0xEC;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: none yet: box read
        public int BoxPokemonSize => 136;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: none yet: box read
        public BoxLayout? BoxLayout { get; } = new(-0x148D8, 18, 30, 4096);

        // Layout flag, not an address.
        public PartyAddressing PartyAddressing => PartyAddressing.Anchor;

        // Layout flag, not an address.
        public PokemonScheme PokemonScheme => PokemonScheme.Gen4;
    }
}
