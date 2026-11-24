using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Service.Pointers
{
    /// <summary>
    /// Ruby/Sapphire memory map.
    ///
    /// The RAM table is shared by both games at every revision. Measured from the pret symbol tables: all 403
    /// EWRAM and 324 IWRAM symbols sit at identical addresses across Ruby and Sapphire and across
    /// rev0/rev1/rev2. A function or script the bot compares against a RAM pointer is a ROM address, and seven
    /// of those, <see cref="WaitForAorBPress"/>, <see cref="DeleteMoveYesNo"/>, <see cref="StopLearningYesNo"/>,
    /// <see cref="UseNextPokemonYesNo"/>, <see cref="PartyMenuMainCallback2"/>, <see cref="StartMenuInputTask"/> and
    /// <see cref="OverworldCallback2"/>, differ between the games: that is the per-game part, chosen by
    /// <see cref="For"/> from the game code.
    ///
    /// Unlike Gen 4 these are absolute, not offsets from a pointer that moves: Gen 3 does not
    /// relocate its save block, so the party is simply at a fixed address. That is why
    /// <see cref="PartyAddressing"/> is Absolute -- dereferencing <see cref="Anchor"/> would read the
    /// party's own first bytes as a pointer.
    /// </summary>
    public class RsPointerService : IPointerService
    {
        private static readonly RsPointerService Shared = new();
        private static readonly RsPointerService Sapphire = new()
        {
            WaitForAorBPress = 0x080670E5, DeleteMoveYesNo = 0x081D8ED2, StopLearningYesNo = 0x081D8EE1,
            UseNextPokemonYesNo = 0x081D8C56, PartyMenuMainCallback2 = 0x0806AF01, StartMenuInputTask = 0x080712D9,
            OverworldCallback2 = 0x080543C9,
        };
        private static readonly RsPointerService Ruby = new()
        {
            WaitForAorBPress = 0x080670E1, DeleteMoveYesNo = 0x081D8F42, StopLearningYesNo = 0x081D8F51,
            UseNextPokemonYesNo = 0x081D8CC6, PartyMenuMainCallback2 = 0x0806AEFD, StartMenuInputTask = 0x080712D5,
            OverworldCallback2 = 0x080543C5,
        };

        /// <summary>
        /// The table for a game code: "AXPE" Sapphire, "AXVE" Ruby, rev 1 and rev 2 alike (pret symbols, commit 78b1990).
        /// Any other code, or null, gives the shared table with the per-game part null.
        /// </summary>
        public static RsPointerService For(string? gameCode) => gameCode switch
        {
            "AXPE" => Sapphire,
            "AXVE" => Ruby,
            _ => Shared,
        };

        // WaitForAorBPress, the field script native that waits for A or B; ROM code, stored with the Thumb bit as RAM
        // holds it (pret symbols 78b1990 line 4437: Sapphire 080670e4, Ruby 080670e0, rev 1 equal to rev 2);
        // constant, per game; observed on Sapphire 1.2 and Ruby 1.2 (research "A trainer's intro speech", "The nurse"),
        // both 1.1 values decomp only; QA: rs-train-heal (Sapphire and Ruby)
        public uint? WaitForAorBPress { get; private init; }

        // BattleScript_AskToLearnMove + 0x11, the battle script's yesnoboxlearnmove at "Delete a move?"; ROM data, the
        // script address as gBattlescriptCurrInstr holds it, no Thumb bit (pret symbols line 24455: Sapphire 081d8ec1,
        // Ruby 081d8f31, rev 1 equal to rev 2); constant, per game; observed on Sapphire 1.2 and Ruby 1.2 (research
        // "Training: the readings behind …" R1), both 1.1 values decomp only; QA: none yet: rs-train-learn-slot1 (Ruby:
        // none yet)
        public uint? DeleteMoveYesNo { get; private init; }

        // BattleScript_AskToLearnMove + 0x20, the battle script's yesnoboxstoplearningmove at "Stop learning?"; ROM data,
        // the script address as gBattlescriptCurrInstr holds it, no Thumb bit (pret symbols line 24455); constant, per
        // game; observed on Sapphire 1.2 and Ruby 1.2 (research R1), both 1.1 values decomp only; QA: none yet:
        // rs-train-learn-refuse (Ruby: none yet)
        public uint? StopLearningYesNo { get; private init; }

        // BattleScript_FaintedMonTryChooseAnother - 0x14, the battle script's yes/no at "Use next POKéMON?" after the active
        // Pokémon faints in a wild battle; ROM data, the script address as gBattlescriptCurrInstr holds it, no Thumb bit
        // (pret symbols line 24425: Sapphire 081d8c6a, Ruby 081d8cda, rev 1 equal to rev 2); constant, per game; observed
        // on Sapphire 1.2 (6) and Ruby 1.2 (2) (research "A battler faints: the readings", F1, F4), both 1.1 values decomp
        // only; QA: none yet: rs-train-switch-battler-spent fainted (Ruby: none yet)
        public uint? UseNextPokemonYesNo { get; private init; }

        // CB2_PartyMenuMain, callback 2 while the party menu runs: the battle party list takes keys only with it set and the
        // palette fade off; ROM code, stored with the Thumb bit as RAM holds it (pret symbols line 4647: Sapphire 0806af00,
        // Ruby 0806aefc, rev 1 equal to rev 2); constant, per game; observed on Sapphire 1.2 and Ruby 1.2 (research "The
        // in-battle switch, read frame by frame" S1, S10), both 1.1 values decomp only; QA: none yet: rs-train-switch
        // (Ruby: none yet: rs-train-switch-ruby)
        public uint? PartyMenuMainCallback2 { get; private init; }

        // sub_80712B4, the start menu's input task function; ROM code, stored with the Thumb bit as RAM holds it (pret
        // symbols line 4846: Sapphire 080712d8, Ruby 080712d4, rev 1 equal to rev 2); constant, per game; observed on
        // Sapphire 1.2 and Ruby 1.2 (research "The party menu, from the decomp and the probe"), both 1.1 values decomp
        // only; QA: none yet: rs-pickup-take (Ruby: rs-pickup-ruby)
        public uint? StartMenuInputTask { get; private init; }

        // CB2_Overworld, callback 2 in the field; ROM code, stored with the Thumb bit as RAM holds it (pret symbols line
        // 2949: Sapphire 080543c8, Ruby 080543c4, rev 1 equal to rev 2); constant, per game; observed on Sapphire 1.2 and
        // Ruby 1.2 (research "The party menu, from the decomp and the probe"), both 1.1 values decomp only; QA: none yet:
        // rs-pickup-take (Ruby: rs-pickup-ruby)
        public uint? OverworldCallback2 { get; private init; }

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

        // gBattleCommunication; root (absolute; same at rev 0-2); QA: none yet: RS starter hunt. [0] is a battle yes/no's
        // state: 0 while "Use next POKéMON?" draws its box, 1 once it reads keys; the training reader reads from here through
        // BattleOutcome, 8 bytes on, in one block (pret symbols lines 80 and 82); observed on
        // Sapphire 1.2 and Ruby 1.2 (research "A battler faints: the readings", round six, A); QA: none yet:
        // rs-train-switch-battler-spent fainted
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

        // gMain.vblankCounter1 (u32), frames since boot; root (absolute; same at rev 0-2); observed; QA: none yet: rs-wild-speed
        public uint VBlankCounter => 0x03001790;

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

        // gPokemonStorage = 0x020300A0 (currentBox u8, then boxes[14][30] at + 4), 30 BoxPokemon of 80 bytes a box with
        // no padding. Storage is from the anchor itself, because the party read is Absolute.
        // gPokemonStorage.boxes; root (absolute; same at rev 0-2); decomp only; QA: none yet: W12 full-party catch
        public BoxLayout? BoxLayout { get; } = new(0x020300A4 - 0x03004350, 14, 30, 30 * 80);

        // Navigation and training, docs/research/rs-navigation-and-training.md. "pret symbols" is pret/pokeruby's symbols
        // branch, commit 78b1990; every value below is the same in the Ruby and Sapphire rev 1 and rev 2 tables. A ROM
        // function is stored with the Thumb bit (table address + 1), as RAM holds the pointer.

        // gSaveBlock1 (0x02025734) + 0x1220, the event flags: flag n is bit n & 7 of byte n >> 3; root (absolute; pret
        // symbols line 106); observed; QA: none yet: rs-nav-heal-return
        public uint EventFlags => 0x02026954;

        // the length of EventFlags in bytes: FLAGS_COUNT 288 bits (include/constants/global.h:46); QA: none yet:
        // rs-nav-heal-return
        public int EventFlagBytes => 0x120;

        // The byte that holds event flag `flag`; the flag is bit flag & 7 of it (research "Flags, variables and trainers in memory").
        public uint EventFlagByte(int flag) => EventFlags + (uint)(flag >> 3);

        // gSaveBlock1 + 0x1340, the script variables, u16 each from 0x4000; root (absolute; pret symbols line 106);
        // observed; QA: none yet: rs-nav-heal-return
        public uint Vars => 0x02026A74;

        // the length of Vars in bytes: VARS_COUNT 256 u16s (include/constants/global.h:47); QA: none yet: rs-nav-heal-return
        public int VarBytes => 0x200;

        // gTrainerBattleOpponent (u16), the trainer id from the first locked frame of a trainer's approach; root
        // (absolute; pret symbols line 210); observed on Sapphire 1.2 and Ruby 1.2; QA: none yet: rs-nav-trainer
        public uint TrainerBattleOpponent => 0x0202FF5E;

        // gFieldMessageBoxWindow (0x0202E87C) + 0x16 (u16), as BattleWindowState: 8 waiting for a button (5 and 9 decomp
        // only); root (absolute; pret symbols line 131); observed; QA: none yet: rs-nav-heal-return
        public uint FieldMessageWindowState => 0x0202E892;

        // sGlobalScriptContext (0x030005B8) + 4, nativePtr (u32) after three u8s and padding (include/script.h); root
        // (absolute; pret symbols line 468); observed; QA: none yet: rs-nav-heal-return
        public uint ScriptNative => 0x030005BC;

        // sGlobalScriptContextStatus (u8): 0 a script is running, 1 stopped in a waitstate, 2 no script; root (absolute;
        // research "A trainer's intro speech"); observed on Sapphire 1.2 and Ruby 1.2; QA: none yet: rs-nav-trainer
        public uint ScriptContextStatus => 0x030005B0;

        // sGlobalScriptContext (0x030005B8) + 1, mode (u8, include/script.h): 2 while a native is polled, 1 bytecode, 0 no
        // script; root (absolute; pret symbols line 468); observed on Sapphire 1.2 and Ruby 1.2; QA: none yet:
        // rs-nav-heal-return
        public uint ScriptContextMode => 0x030005B9;

        // gPlayerAvatar (0x0202E858) + 2, runningState (u8, include/global.fieldmap.h): 2 while moving, 1 through a turn in
        // place, 0 at rest; tileTransitionState is the next byte; root (absolute; pret symbols line 130, pret/pokeruby
        // symbols branch, commit 78b1990); observed on Sapphire 1.2 (the research's navigator log, `run=`); QA: none yet:
        // rs-train-heal
        public uint PlayerRunningState => 0x0202E85A;

        // gPlayerAvatar (0x0202E858) + 3, tileTransitionState (u8, include/global.fieldmap.h): 1 through a step, 2 for one
        // frame, 0 at rest, and 0 through a turn in place; root (absolute; pret symbols line 130); observed on Sapphire 1.2;
        // QA: none yet: rs-nav-heal-return
        public uint PlayerTileTransition => 0x0202E85B;

        // Task_HandleYesNoInput, the field Yes/No task's function; constant (ROM, pret symbols line 7581, 080b5504 + 1);
        // observed on Sapphire 1.2 and Ruby 1.2 (research "The nurse"); QA: none yet: rs-nav-heal-return
        public uint TaskHandleYesNoInput => 0x080B5505;

        // gTasks, 16 tasks of 0x28 bytes (0x280); root (absolute; pret symbols line 642); observed (research "The nurse");
        // QA: none yet: rs-nav-heal-return
        public uint Tasks => 0x03004B20;

        // gMain (0x03001770) + 4, callback2 (u32, include/main.h:13); root (absolute; pret symbols line 526); observed
        // (research R1, R4); QA: none yet: rs-train-learn-slot1
        public uint Callback2 => 0x03001774;

        // BattleMainCB2, callback2 during a battle; constant (ROM below 0x0803FBDC, pret symbols line 1378, 0800f808 + 1);
        // observed; QA: none yet: rs-train-learn-slot1
        public uint BattleMainCallback2 => 0x0800F809;

        // gMoveSelectionCursor[0] (u8), the move list cursor; root (absolute; pret symbols line 95); decomp only;
        // QA: none yet: rs-train
        public uint MoveSelectionCursor => 0x02024E64;

        // gBattleStruct->expGetterMonId (u8): gSharedMem (0x02000000) + 0x16018 (include/battle.h:338, include/ewram.h:25);
        // root (absolute; pret symbols line 1); observed (research R3; "The in-battle switch", S8); QA: none yet:
        // rs-train-two-levelup
        public uint ExpGetterMonId => 0x02016018;

        // gBattleStruct->atk5A_StateTracker (u8): gSharedMem + 0x1601A; 1 at "Delete a move?", 3 on the forget-a-move
        // screen; root (absolute; pret symbols line 1); observed; QA: none yet: rs-train-learn-slot1
        public uint MoveLearnPromptState => 0x0201601A;

        // gBattleStruct->atk6C_statetracker (u8): gSharedMem + 0x1609C; 1 and 2 for the level-up box's pages; root
        // (absolute; pret symbols line 1); observed; QA: none yet: rs-train
        public uint LevelUpBoxState => 0x0201609C;

        // pssData.selectedMoveIndex (u8): gSharedMem + 0x18000 + 0x79 (include/ewram.h:130,
        // include/pokemon_summary_screen.h:51), the forget-a-move cursor; root (absolute; pret symbols line 1); observed;
        // QA: none yet: rs-train-learn-slot1
        public uint ForgetMoveCursor => 0x02018079;

        // Task_EvolutionScene, the evolution scene's task function, the same in both games; constant (ROM, pret symbols
        // line 11039, 0811242c + 1); observed on Sapphire 1.2 and Ruby 1.2 (research R5), both 1.1 values decomp only;
        // QA: none yet: rs-train-evolve (Ruby: none yet)
        public uint TaskEvolutionScene => 0x0811242D;

        // gBattlerPartyIndexes[0] (u16), the player battler's party slot; root (absolute; pret symbols line 31); observed;
        // QA: none yet: rs-train-switch
        public uint ActiveBattlerPartyIndex => 0x02024A6A;

        // HandleBattlePartyMenu, the battle party list's task function; constant (ROM, pret symbols line 6372,
        // 08095138 + 1); observed on Sapphire 1.2 and Ruby 1.2 (research "The in-battle switch"); QA: none yet:
        // rs-train-switch
        public uint BattlePartyMenuTask => 0x08095139;

        // Task_HandlePopupMenuInput (battle_party_menu.c), the task's function once SHIFT / SUMMARY / CANCEL is up;
        // constant (ROM, pret symbols line 6379, 08095428 + 1); observed on Sapphire 1.2 and Ruby 1.2 (research "The
        // in-battle switch", S3); QA: none yet: rs-train-switch
        public uint BattlePartyPopupTask => 0x08095429;

        // gSprites, 65 sprites of 0x44 bytes, data[0] at + 0x2E (include/sprite.h:207); root (absolute; pret symbols
        // line 5); base decomp only, sprite 4's data[0] at 0x02020142 observed; QA: none yet: rs-train-switch
        public uint Sprites => 0x02020004;

        // Training, docs/research/rs-navigation-and-training.md "Training: the readings behind the move prompt, the forget
        // screen and the evolution scene" (R1 to R7).

        // gBattlescriptCurrInstr (u32), the battle script pointer: it stays on a yes/no instruction while that prompt
        // waits; root (absolute; pret symbols line 57); observed (R1); QA: none yet: rs-train-learn-refuse
        public uint BattlescriptCurrInstr => 0x02024C10;

        // gMoveToLearn (u16), the move offered; meaningful only while the prompt is up; root (absolute; pret symbols
        // line 101); observed (R2); QA: none yet: rs-train-learn-slot1
        public uint MoveToLearn => 0x02024E82;

        // sub_809E260, the summary screen's input task on the forget-a-move screen; constant (ROM code, pret symbols
        // line 6648, 0809e280 + 1); observed (R4, R7); QA: none yet: rs-train-learn-slot1
        public uint SummaryMoveInputTask => 0x0809E281;

        // SummaryScreen_DestroyTask, the task function from the frame A is taken on the forget-a-move screen; constant
        // (ROM code, pret symbols line 6646, 0809e15c + 1); observed (R4); QA: none yet: rs-train-learn-slot1
        public uint SummaryDestroyTask => 0x0809E15D;

        // gPaletteFade (0x0202F388) + 7, active in bit 7 (struct PaletteFadeControl, include/palette.h:37); the forget
        // screen ignores keys while it is set; root (absolute; pret symbols line 177); observed (R4); QA: none yet:
        // rs-train-learn-slot1
        public uint PaletteFadeActive => 0x0202F38F;

        // A battler faints, docs/research/rs-navigation-and-training.md "A battler faints: the readings" (F1 to F9).

        // gBattlePartyCurrentOrder, 3 bytes: the battle party menu's row p shows the party slot in nibble p, byte p / 2, the
        // high nibble for even p (0x01, 0x23 is the identity); the party menu's case byte follows at + 3, 1 in the menu a
        // faint forces; root (absolute; pret symbols line 231); observed (F4, F7); QA: none yet:
        // rs-train-switch-battler-spent fainted-run
        public uint BattlePartyCurrentOrder => 0x02038470;

        // gBattleStruct->runTries (u8): gSharedMem + 0x16078 (include/battle.h:362), + 1 at each Run the game takes; root
        // (absolute; pret symbols line 1); observed (F4, F6); QA: none yet: rs-train-switch-battler-spent fainted-stop
        public uint RunTries => 0x02016078;

        // Pickup, docs/research/rs-pickup.md "The party menu, from the decomp and the probe": the field party menu, its
        // Pokémon submenu and the start menu. Every value below is the same in the Ruby and Sapphire rev 1 and rev 2 tables.

        // HandleDefaultPartyMenu, the field party list's task function; constant (ROM, pret symbols line 6009, 08089cf4 +
        // 1, rev 1 equal to rev 2); observed on Sapphire 1.2 and Ruby 1.2, 1.1 decomp only; QA: none yet: rs-pickup-take
        // (Ruby: rs-pickup-ruby)
        public uint FieldPartyMenuTask => 0x08089CF5;

        // sub_8089D94, the task's function while the Pokémon submenu (SUMMARY, the field moves, ITEM, CANCEL) takes keys;
        // constant (ROM, pret symbols line 6010, 08089db4 + 1, rev 1 equal to rev 2); observed on Sapphire 1.2 and Ruby
        // 1.2, 1.1 decomp only; QA: none yet: rs-pickup-take (Ruby: rs-pickup-ruby)
        public uint PokemonMenuTask => 0x08089DB5;

        // sub_808A060, the task's function while the ITEM submenu (GIVE, TAKE, MAIL, CANCEL) takes keys; constant (ROM,
        // pret symbols line 6019, 0808a080 + 1, rev 1 equal to rev 2); observed on Sapphire 1.2 and Ruby 1.2, 1.1 decomp
        // only; QA: none yet: rs-pickup-take (Ruby: rs-pickup-ruby)
        public uint PokemonItemMenuTask => 0x0808A081;

        // gPartyMenuMessage_IsPrinting (u8), 1 while the party menu prints its message; root (absolute; pret symbols line
        // 159, rev 1 equal to rev 2); observed on Sapphire 1.2 and Ruby 1.2, 1.1 decomp only; QA: none yet: rs-pickup-take
        // (Ruby: rs-pickup-ruby)
        public uint PartyMenuMessagePrinting => 0x0202E8F6;

        // gMenuWindow (0x0202E908) + 0x16, state (u16, include/text.h:54), the window the party menu's message prints in;
        // root (absolute; pret symbols line 165, rev 1 equal to rev 2); observed on Sapphire 1.2 and Ruby 1.2, 1.1 decomp
        // only; QA: none yet: rs-pickup-take (Ruby: rs-pickup-ruby)
        public uint MenuWindowState => 0x0202E91E;

        // sPokeMenuCursorPos (u8), the cursor of the Pokémon submenu and of the ITEM submenu; root (absolute; pret symbols
        // line 222, rev 1 equal to rev 2); observed on Sapphire 1.2 and Ruby 1.2, 1.1 decomp only; QA: none yet:
        // rs-pickup-take (Ruby: rs-pickup-ruby)
        public uint PokemonMenuCursorPos => 0x0202FFA8;

        // sPokeMenuOptionsNo (u8), the Pokémon submenu's entry count; root (absolute; pret symbols line 223, rev 1 equal to
        // rev 2); observed on Sapphire 1.2 and Ruby 1.2, 1.1 decomp only; QA: none yet: rs-pickup-take (Ruby: rs-pickup-ruby)
        public uint PokemonMenuActionCount => 0x0202FFA9;

        // sPokeMenuOptionsOrder (u8[8]), the Pokémon submenu's entry ids in order; root (absolute; pret symbols line 224,
        // rev 1 equal to rev 2); observed on Sapphire 1.2 and Ruby 1.2, 1.1 decomp only; QA: none yet: rs-pickup-take
        // (Ruby: rs-pickup-ruby)
        public uint PokemonMenuActions => 0x0202FFAA;
    }
}
