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

        // evo.summary, OverlayManager * of the Pokémon summary app (gOverlayTemplate_PokemonSummary), non-NULL only while
        // evo.state is 23 (evo + 0x38); offset (revision-independent); observed; QA: hgss-route33-evolve-learn-replace
        public int EvolutionSummaryOffset => 0x38;

        // OverlayManager.proc_state (u32), overlay_manager.h; in the summary app, Main's state: 8 the move list, 9 the confirm
        // (summary + 0x14); offset (revision-independent); observed; QA: hgss-route33-evolve-learn-replace
        public int OverlayProcStateOffset => 0x14;

        // evo.printer (u8), the text printer slot of the scene's message, sub_020772F8's AddTextPrinterParameterized id
        // (evo + 0x65); offset (revision-independent); observed; QA: hgss-route33-evolve-learn-replace, hgss-heal-variants
        public int EvolutionPrinterOffset => 0x65;

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

        // The field script chain, pointer-discovery.md "Field scripts". Members for natives and tasks no decision
        // uses (WaitStd, the touch menu, 0x02041CA9, the fade child) are left out; their names are in the research doc.

        // task.env, the ScriptEnvironment while the task function is Task_RunScripts (taskman + 0x0C); offset
        // (revision-independent); observed; QA: hgss-heal-variants, hgss-poison-walk
        public int ScriptEnvironmentOffset => 0x0C;

        // env.check, the environment's magic (env + 0x00); offset (revision-independent); observed;
        // QA: hgss-heal-variants, hgss-poison-walk
        public int ScriptEnvironmentCheckOffset => 0x00;

        // env.check's value, 222271 (0x3643F); constant; observed; QA: hgss-heal-variants, hgss-poison-walk
        public uint ScriptEnvironmentMagic => 222271;

        // env.script (u16), the script id: 1 the map's NPC script, 2003 std_survive_poisoning (env + 0x0A); offset
        // (revision-independent); observed; QA: hgss-heal-variants, hgss-poison-walk
        public int ScriptIdOffset => 0x0A;

        // ScriptEnvironment.textPrinterNum (u8), script.h, the slot the printing native waits on; keeps its last value
        // after the printer is gone (env + 0x05); offset (revision-independent); observed;
        // QA: hgss-route33-evolve-learn-replace, hgss-heal-variants
        public int ScriptPrinterOffset => 0x05;

        // env.ctx[0], the first of three ScriptContext pointers, ctx[i] at + 4*i (env + 0x38); offset
        // (revision-independent); observed; QA: hgss-heal-variants (ctx 0 and 1), hgss-poison-walk (ctx 0)
        public int ScriptContextsOffset => 0x38;

        // ctx.mode (u8): 0 stopped, 1 running bytecode, 2 waiting in a native (ctx + 0x01); offset
        // (revision-independent); observed; QA: hgss-heal-variants, hgss-poison-walk
        public int ScriptContextModeOffset => 0x01;

        // ctx.native, the native the context waits in while mode is 2, Thumb bit set (ctx + 0x04); offset
        // (revision-independent); observed; QA: hgss-heal-variants, hgss-poison-walk
        public int ScriptContextNativeOffset => 0x04;

        // Task_RunScripts, the task function while a field script runs; root (revision-dependent; static ARM9, rev 0);
        // observed; QA: hgss-heal-variants, hgss-poison-walk
        public uint RunScriptsTask => 0x0203FF45;

        // The heal animation child task the nurse script pushes after Yes (taskman points at it, the script task is
        // underneath), listed in pointer-discovery.md "Field scripts" without a decomp symbol; root (revision-dependent; overlay, rev 0); observed;
        // QA: hgss-heal-variants
        public uint HealAnimationTask => 0x0224BB91;

        // std_survive_poisoning, env.script of the "survived the poisoning" message; constant; observed;
        // QA: hgss-poison-walk
        public int SurvivePoisoningScript => 2003;

        // ov01_021EF348 (from ScrCmd_NPCMsg), a message printing or waiting at the arrow; root (revision-dependent;
        // overlay 1, rev 0); observed; QA: hgss-heal-variants, hgss-poison-walk
        public uint PrintingNative => 0x021EF349;

        // sub_020477C0 (ScrCmd_GetMenuChoice), a Yes/No or list menu is open; root (revision-dependent; static ARM9,
        // rev 0); observed; QA: hgss-heal-variants
        public uint MenuChoiceNative => 0x020477C1;

        // sub_02041074 (ScrCmd_WaitButton), waiting for A or B; root (revision-dependent; static ARM9, rev 0);
        // observed (run 20261024-004624); pressed like PrintingNative per R18;
        // QA: hgss-heal-variants (HeartGold8, the Pokérus explanation)
        public uint WaitButtonNative => 0x02041075;

        // WaitButtonOrDelay; root (revision-dependent; static ARM9, rev 0); decomp only;
        // pressed like PrintingNative per R18, move to observed in the PR whose run logs one;
        // QA: hgss-heal-variants, hgss-poison-walk
        public uint WaitButtonOrDelayNative => 0x02041041;

        // WaitButtonOrDpad; root (revision-dependent; static ARM9, rev 0); decomp only;
        // pressed like PrintingNative per R18, move to observed in the PR whose run logs one;
        // QA: hgss-heal-variants, hgss-poison-walk
        public uint WaitButtonOrDpadNative => 0x020410F1;

        // The text printer, pointer-discovery.md "Text printer state (2026-10-24)".

        // sTextPrinterTasks (SysTask *[8], text.c), a slot NULL when no printer runs in it; root (revision-dependent;
        // static ARM9, rev 0); observed; QA: hgss-route33-evolve-learn-replace, hgss-heal-variants
        public uint TextPrinterTasks => 0x021D1F74;

        // SysTask.data (sys_task.h), the task's TextPrinter (task + 0x10); offset (revision-independent); observed;
        // QA: hgss-route33-evolve-learn-replace, hgss-heal-variants
        public int TextPrinterTaskDataOffset => 0x10;

        // TextPrinter.state (u8), RenderText's state (render_text.c): 0, 4, 5, 6 drawing, 2, 3, 7, 8 waiting at the arrow
        // (printer + 0x28); offset (revision-independent); observed (0, 2, 6); QA: hgss-route33-evolve-learn-replace,
        // hgss-heal-variants
        public int TextPrinterStateOffset => 0x28;

        // Layout flag, not an address.
        public PartyAddressing PartyAddressing => PartyAddressing.Anchor;

        // Layout flag, not an address.
        public PokemonScheme PokemonScheme => PokemonScheme.Gen4;
    }
}
