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

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train (start tile)
        public int MapIdOffset => -0x22DA4;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: none yet: spin encounter, navigation, Togepi egg
        public int FacingOffset => 0x1DC4;

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

        // no symbol in pointer-discovery.md (the spec's EnemyCountOffset); offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public uint EnemyCount => 0xC14;

        // no symbol in pointer-discovery.md (the spec's EnemyOffset); offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public uint Enemy => 0xC18;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public int PartySlots => -0x23F40;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33, hgss-route33-train
        public int PartyPokemonSize => 0xEC;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33 (catch check on Resume)
        public int BoxPokemonSize => 136;

        // no symbol in pointer-discovery.md; offset (revision-independent); QA: hgss-route33 (catch check on Resume)
        public BoxLayout? BoxLayout { get; } = new(-0x148D8, 18, 30, 4096);

        // FieldSystem.sub0, the app slots block (FS + 0x00); offset (revision-independent); QA: hgss-route33
        public int AppSlotsOffset => 0x00;

        // FieldSystem taskman, the top field task, NULL when none runs (FS + 0x10); offset (revision-independent); QA: hgss-route33
        public int TaskManagerOffset => 0x10;

        // FieldSystem mapReady, field map up and running (FS + 0x6C); offset (revision-independent); QA: hgss-route33
        public int MapReadyOffset => 0x6C;

        // sub0 mapApp, the field map app, NULL while a sub-app runs (sub0 + 0x00); offset (revision-independent); QA: hgss-route33
        public int MapAppOffset => 0x00;

        // sub0 app, the launched sub-app, NULL on the field (sub0 + 0x04); offset (revision-independent); QA: hgss-route33
        public int SubAppOffset => 0x04;

        // sub0 paused (sub0 + 0x08); offset (revision-independent); QA: hgss-route33
        public int PausedOffset => 0x08;

        // task function, [taskman + 4]; +0x08 is its state; offset (revision-independent); QA: hgss-navigate-heal-return (scripts and warps)
        public int TaskFunctionOffset => 0x04;

        // app.ovyId, the sub-app's overlay id (app + 0x0C); offset (revision-independent); QA: hgss-route33
        public int AppOverlayIdOffset => 0x0C;

        // app.state, the battle application state, battle_022378C0.c:25-42 (app + 0x14); offset (revision-independent);
        // QA: hgss-route33 (state 13 on a flee), hgss-route33-evolve (14)
        public int AppStateOffset => 0x14;

        // app.setup, BattleSetup (app + 0x18; replaces the heap coincidence at FS + 0x6930); offset (revision-independent);
        // QA: hgss-route33 (foe read), hgss-route33-evolve
        public int AppSetupOffset => 0x18;

        // BattleSetup.evolutionTaskData, EvolutionTaskData *, battle_setup.h:63, valid only while app.state is 14 (app.setup + 0x198);
        // offset (revision-independent); QA: hgss-route33-evolve, hgss-route33-evolve-learn
        public int EvolutionTaskOffset => 0x198;

        // evo.state (u8), the evolution scene state, jump table 0x02075F40 in asm/unk_020755E8.s (evo + 0x64); offset (revision-independent);
        // QA: hgss-route33-evolve, hgss-route33-evolve-learn
        public int EvolutionStateOffset => 0x64;

        // BS, the BattleSystem, valid while app.state is 4..9 (app + 0x1C); offset (revision-independent); QA: hgss-route33
        public int BattleSystemOffset => 0x1C;

        // ctx, the BattleContext (BS + 0x30); offset (revision-independent); QA: hgss-route33
        public int BattleContextOffset => 0x30;

        // ctx.sel (u8), the player menu state, battle_controller_player.c:243-262 (ctx + 0x00); offset (revision-independent);
        // QA: hgss-route33
        public int MenuStateOffset => 0x00;

        // ctx.command, the server command, constants/battle.h:594-642 (ctx + 0x08); offset (revision-independent); QA: hgss-route33
        public int ServerCommandOffset => 0x08;

        // ctx.script narc, while command is 22 (ctx + 0xAC); offset (revision-independent); QA: hgss-route33-train
        public int ScriptNarcOffset => 0xAC;

        // ctx.script file, while command is 22, constants/battle_subscript.h (ctx + 0xB0); offset (revision-independent);
        // QA: hgss-route33-train
        public int ScriptFileOffset => 0xB0;

        // ctx.getter, experience / level-up / move-learn work, NULL when idle (ctx + 0x178); offset (revision-independent);
        // QA: hgss-route33-train
        public int GetterOffset => 0x178;

        // getter.state, battle_command.c:5871-5911 (getter + 0x28); offset (revision-independent); QA: hgss-route33-train
        public int GetterStateOffset => 0x28;

        // BS.outcome (u8): 1 won, 2 lost, 3 draw, 4 caught, 5 fled; bits 0x40/0x80 flee in progress (BS + 0x2420);
        // offset (revision-independent); QA: hgss-route33
        public int OutcomeOffset => 0x2420;

        // app.ovyId of the battle application (overlay 12); constant; QA: hgss-route33
        public uint BattleOverlayId => 12;

        // Layout flag, not an address.
        public PartyAddressing PartyAddressing => PartyAddressing.Anchor;

        // Layout flag, not an address.
        public PokemonScheme PokemonScheme => PokemonScheme.Gen4;
    }
}
