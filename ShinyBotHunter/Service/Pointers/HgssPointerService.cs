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

        // std_field_headbutt (include/constants/std_script.h:646), env.script of the headbutt tree's script; constant; observed;
        // QA: hgss-headbutt
        public int HeadbuttScript => 10014;

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

        // Primo's egg and the egg hatch, docs/research/primo-slugma-egg.md (spike 2026-11-02).

        // FieldSystem.saveData, SaveData * (FS + 0x0C); offset (revision-independent); observed;
        // QA: hgss-primo-egg-password
        public int SaveDataOffset => 0x0C;

        // PlayerProfile.id (u16), the visible trainer ID, player_data.h; the secret ID follows (saveData + 0x84); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int TrainerIdOffset => 0x84;

        // Daycare.egg_cycles (u8), the egg step counter, the day care array + 0x1DC (saveData + 0x17E8); offset
        // (revision-independent); observed; QA: hgss-primo-egg-hatch, hgss-togepi-egg-hatch
        public int EggStepCounterOffset => 0x17E8;

        // ScriptEnvironment's special variables, 14 x u16 from VAR_SPECIAL_x8000 (script.h); 0x800C at + 0xA4 (env + 0x8C);
        // offset (revision-independent); observed; QA: hgss-primo-egg-password
        public int ScriptSpecialVarsOffset => 0x8C;

        // scr_seq_T22PC0101_004, Primo's password script, env.script; constant; observed; QA: hgss-primo-egg-password
        public int PrimoScript => 5;

        // std_hatch_egg (scr_seq_0003_021), env.script of the egg hatch; constant; observed;
        // QA: hgss-primo-egg-hatch, hgss-togepi-egg-hatch
        public int HatchEggScript => 2021;

        // WaitABPress, the native "Oh?" waits in before the hatch; root (revision-dependent; static ARM9, rev 0); observed;
        // QA: hgss-primo-egg-hatch, hgss-togepi-egg-hatch
        public uint WaitABPressNative => 0x02041001;

        // OverlayManager.data, the sub-app's data, the easy-chat and hatch apps' blocks (app + 0x1C; the battle app's is
        // BattleSystemOffset); offset (revision-independent); observed; QA: hgss-primo-egg-password, hgss-primo-egg-hatch
        public int AppDataOffset => 0x1C;

        // app.ovyId of the easy-chat app (overlay 102); constant; observed; QA: hgss-primo-egg-password
        public uint EasyChatOverlayId => 102;

        // app.ovyId of the egg hatch app (overlay 95); constant; observed; QA: hgss-primo-egg-hatch, hgss-togepi-egg-hatch
        public uint HatchOverlayId => 95;

        // easy-chat data, the two chosen words (u16 word ids, 0xFFFF when empty), slot i at + 2i (data + 0x10); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatWordsOffset => 0x10;

        // easy-chat data, busy (u8), 1 while the word list scrolls and presses are dropped (data + 0x1C); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatBusyOffset => 0x1C;

        // easy-chat data, the current screen's handler (data + 0x20); offset (revision-independent); observed;
        // QA: hgss-primo-egg-password
        public int EasyChatScreenOffset => 0x20;

        // easy-chat data, the question's step (u8), 1 while the question's Yes/No reads input (data + 0x28); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatQuestionStepOffset => 0x28;

        // easy-chat data, the handler that runs instead of + 0x20 while set (u32): 0x021E8D61 while the app opens,
        // EasyChatQuestionScreen once Confirm is pressed, 0 otherwise (data + 0x2C); offset (revision-independent);
        // observed; QA: hgss-primo-egg-password
        public int EasyChatQuestionOffset => 0x2C;

        // easy-chat data, slot screen cursor row (u8): 0 the slots, 1 Confirm, 2 Cancel (data + 0x4C); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatSlotRowOffset => 0x4C;

        // easy-chat data, slot screen cursor column (u8), the first or second slot (data + 0x4E); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatSlotColumnOffset => 0x4E;

        // easy-chat data, group grid cursor (u8), row x 3 + column, Left and Right wrap within a row (data + 0x50); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatGroupCursorOffset => 0x50;

        // easy-chat data, word list scroll in rows (u16) (data + 0x54); offset (revision-independent); observed;
        // QA: hgss-primo-egg-password
        public int EasyChatListScrollOffset => 0x54;

        // easy-chat data, word list maximum scroll in rows (u16) (data + 0x56); offset (revision-independent); observed;
        // QA: hgss-primo-egg-password
        public int EasyChatListMaxScrollOffset => 0x56;

        // easy-chat data, word list column (u8) (data + 0x5A); offset (revision-independent); observed;
        // QA: hgss-primo-egg-password
        public int EasyChatListColumnOffset => 0x5A;

        // easy-chat data, word list on-screen row (u8), 0 to 4 (data + 0x5B); offset (revision-independent); observed;
        // QA: hgss-primo-egg-password
        public int EasyChatListRowOffset => 0x5B;

        // easy-chat data, 12 x u32 words per grid group, 0 when locked (data + 0xA4); offset (revision-independent);
        // observed; QA: hgss-primo-egg-password
        public int EasyChatGroupCountsOffset => 0xA4;

        // easy-chat data, 12 x u32 each group's start in the word area, in words (data + 0xD4); offset
        // (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatGroupStartsOffset => 0xD4;

        // easy-chat data, the word area (u16 ids): every unlocked group's list, alphabetical, back to back (data + 0x104);
        // offset (revision-independent); observed; QA: hgss-primo-egg-password
        public int EasyChatGroupWordsOffset => 0x104;

        // easy-chat screen handler of the two slots, Confirm and Cancel; kept while "Is this your answer?" runs at
        // + 0x2C; root (revision-dependent; overlay 102, rev 0); observed; QA: hgss-primo-egg-password
        public uint EasyChatSlotsScreen => 0x021E7AA5;

        // easy-chat "Is this your answer?" handler, set at + 0x2C by the A at Confirm; root (revision-dependent;
        // overlay 102, rev 0); observed; QA: hgss-primo-egg-password
        public uint EasyChatQuestionScreen => 0x021E8D95;

        // easy-chat screen handler of the group grid; root (revision-dependent; overlay 102, rev 0); observed;
        // QA: hgss-primo-egg-password
        public uint EasyChatGroupScreen => 0x021E8241;

        // easy-chat screen handler of a group's word list; root (revision-dependent; overlay 102, rev 0); observed;
        // QA: hgss-primo-egg-password
        public uint EasyChatListScreen => 0x021E87B5;

        // hatch app data, the nickname question's Yes/No menu pointer (data + 0x88); offset (revision-independent); observed
        // (NULL before the question not checked); QA: hgss-primo-egg-hatch, hgss-togepi-egg-hatch
        public int HatchYesNoOffset => 0x88;

        // the hatch Yes/No menu's cursor (u8): 1 Yes, 2 No (menu + 0x19); offset (revision-independent); observed;
        // QA: hgss-primo-egg-hatch, hgss-togepi-egg-hatch
        public int HatchYesNoCursorOffset => 0x19;

        // Layout flag, not an address.
        public PartyAddressing PartyAddressing => PartyAddressing.Anchor;

        // Layout flag, not an address.
        public PokemonScheme PokemonScheme => PokemonScheme.Gen4;
    }
}
