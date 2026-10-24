# Research Spike: Better Pointers From the ROMs

Status: HGSS battle and field state mapped and verified; RS not started
Last updated: 2026-10-23
Related: [Code review 2026-10](../reviews/2026-10-code-review.md), [QA automation](../specs/qa-automation.md), [System architecture](../architecture/system-overview.md), [Improvement plan](../plans/2026-10-cleanup-and-hardening.md)

## Summary

The bot's battle and training logic decides where the game is from a handful of observed bytes, padded with fixed waits and unbounded retry loops. This spike traced each of them through the `pret/pokeheartgold` decompilation and checked the results against a running game. The conclusion:

- **Every one of the bot's battle bytes has a real meaning, but the bot reaches most of them through coincidences** of overlay loading or heap layout rather than the game's own pointers. They work today and can break silently.
- **The game exposes a clean, stable pointer chain** from one static global to the exact battle state: which application is running, the battle phase, the server command, the player's menu state, which battle script is playing, the experience and move-learn prompts, and the outcome. All of it was verified in live RAM on HeartGold US revision 0.
- **The same chain tells "free to move on the field" apart from "a field task is running"** (encounter starting, warp, script, menu), which is the missing signal for self-recovery.

Everything here is for HGSS US revision 1.0 (header revision 0), the revision hunted on every machine. RS, hunted on revisions 1.1 and 1.2, is not started.

## Problem

Observed constants the bot depends on, with what they turned out to be (full inventory of 60+ read sites in the [appendix](#appendix-every-read-the-bot-makes)):

| Bot code | What it assumes | What it actually is |
|---|---|---|
| `0x021E76D2` in `{0x41, 0x97, 0xC0}` (six files) | "in battle" | An instruction byte inside whatever overlay is loaded at `0x021E5900`. In a wild battle that is overlay 18, the **Pokédex** application code, whose byte there is `0x41`. 24 overlays load at that address. During the swap back to the field the byte read `0xBD` with nothing loaded. |
| `[0x021D4158]` | "save-data anchor" | `sFieldSysPtr`, the game's `FieldSystem *` (`src/field_system.c:42`). |
| anchor `+ 0x692C` `== 14` | "evolution screen" | The battle application's state (`OverlayManager.proc_state`), reached through a fixed heap offset: in battle the app happens to sit at `FieldSystem + 0x6918`. 9 is battle main, 14 is evolution main, so the meaning is right and the address is a coincidence. |
| anchor `+ 0x230EC` `== 1` | "battle menu ready" | The battle context's player menu state (`BattleContext.unk_0[0]`), again through a heap coincidence (the context happens to sit at `FieldSystem + 0x230EC`). After the battle it keeps reading 14 because the memory has been freed: a dangling read. |
| anchor `+ 0x6930` `+ 0xC14` / `+ 0xC18` | "enemy anchor", enemy count and data | The `BattleSetup` (the battle app's `args`), reached through the same heap coincidence. |
| X/Y at `0x021DA6F4`/`0x021DA6FC`, facing at anchor `+ 0x1DC4`, map at anchor `- 0x22DA4` | player position | Not traced in this spike; they work and are covered by plan 5.3. |

## The pointer chain (HGSS US rev 0, verified)

```
FS       = u32 [0x021D4158]            FieldSystem (sFieldSysPtr), stable for the whole session
sub0     = u32 [FS + 0x00]             app slots
mapApp   = u32 [sub0 + 0x00]           field map app; NULL while a sub-app runs
app      = u32 [sub0 + 0x04]           launched sub-app (battle, bag, party ...); NULL on the field
paused   = u32 [sub0 + 0x08]
taskman  = u32 [FS + 0x10]             top field task; NULL when no task runs
mapReady = u32 [FS + 0x6C]             field map up and running

app.ovyId    = u32 [app + 0x0C]        12 = battle
app.state    = u32 [app + 0x14]        battle application state, table below
app.setup    = u32 [app + 0x18]        BattleSetup: winFlag +0x14, evolution task +0x198
BS           = u32 [app + 0x1C]        BattleSystem; valid only while app.state is 4..9
ctx          = u32 [BS + 0x30]         BattleContext
ctx.sel      = u8  [ctx + 0x00]        player menu state, table below
ctx.command  = u32 [ctx + 0x08]        server command, table below
ctx.script   = u32 [ctx + 0xAC], [ctx + 0xB0]   battle script narc and file while command is 22
ctx.getter   = u32 [ctx + 0x178]       experience / level-up / move-learn work; NULL when idle
getter.state = u32 [getter + 0x28]
ctx.endFlag  = u8  [ctx + 0x311F]
BS.outcome   = u8  [BS + 0x2420]       1 win, 2 lose, 3 draw, 4 caught, 5 fled
```

Every hop was checked in four RAM snapshots (field, battle start, battle menu, back on field), then traced live every 80 ms through five wild battles: one fought, two fled, and two fought on PKHeX-edited save copies (one with a lead about to level up and be offered a move, one about to evolve). Every save was byte-identical before and after. In the tables below, **observed** means the value was seen live in those traces; **decomp only** means it comes from the source and has not been seen yet.

All observations are from one HeartGold 1.0 save on the dev PC, in single wild battles. Trainer battles, double battles, losing, our Pokémon fainting, and catching were not exercised.

### Where the game is

| Situation | Signal | Status |
|---|---|---|
| **Free to move on the field** | `app == NULL`, `taskman == NULL`, `mapReady == 1`, `paused == 0` (the game's own `FieldSystem_IsPlayerMovementAllowed`, `field_system.c:199`) | observed around battles only; not yet tested against scripts, menus or warps |
| A field task is running | `taskman != NULL`; `[taskman + 4]` is the task function, `[taskman + 8]` its state | observed: `020551B9` encounter starting, `02055245` leaving the field, `02050661` wild encounter (state 1 during the battle), `0205528D` and `020552D5` returning. Script, warp and start-menu tasks not yet seen |
| In a battle | `app != NULL` and `app.ovyId == 12` | observed |
| Some other sub-app (bag, party, summary) | `app != NULL`, another overlay id | decomp only |
| Battle running | `app.state == 9` | observed |
| Evolution after battle | `app.state == 14`; the battle structures are freed by then | observed (13 is too brief to catch at 80 ms) |

Battle application states (`battle_022378C0.c:25-42`): 0 init, 3 allocate, 8 battle init, **9 battle main**, 10-12 end, **13-14 evolution**, 15 exit. Observed: 3, 8, 9, 14, and 0 as the app is torn down.

### Inside a battle

**Server command** (`ctx.command`, `constants/battle.h:594-642`), the turn engine. Stable waiting states are the useful ones; transient commands can be skipped between two reads because the engine can run twice per frame.

| Value | Meaning | Status |
|---|---|---|
| 0, 1 | setup, encounter starting | observed |
| **5** | **waiting for the player's choice** | observed |
| 6-12 | turn order, before-turn, conditions, turn end | 8 observed |
| 13-16 | fight, bag, Pokémon, run input being processed | decomp only (skipped between reads) |
| **22** | **a battle script is running** (see below) | observed |
| 23-41 | move execution | 23, 28, 34, 39 observed |
| 40, 42 | battle-end evaluation | 40 observed |
| **45** | **battle done** | observed |

**Player menu state** (`ctx.sel`, `battle_controller_player.c:243-262`), meaningful while `command == 5`:

| Value | Meaning | Status |
|---|---|---|
| **1** | **Fight / Bag / Pokémon / Run menu** | observed |
| **4** | **move list** | observed |
| 6 | target select | decomp only |
| 8 | bag open | decomp only |
| 10 | party open | decomp only |
| 14 | choice made, waiting | observed |
| **15** | **message shown ("Can't escape!"), waiting to be dismissed** | decomp only |

**Battle script** (`ctx.script` while `command == 22`, narc 1, `constants/battle_subscript.h`): 0 encounter start, 1 use move, 3 escape, **4 battle won**, 5 battle lost, **6 a Pokémon fainted**, 8 escape failed, 10 party list, 11 throw ball, **231 "use next Pokémon?"**, **276 grant experience**, 284 escape success. Narc 30 and narc 0 appear at command 23 for the move being used.
- Observed with a matching event: 0, 1, 3, 4, 6, 276.
- Observed but not named in the tables used: 2, 7, 12, 21, 33, 57, 62, 76, 104, 271, 278, 293 (move animation, damage and turn-end scripts; meanings to look up).
- Decomp only: 5, 8, 10, 11, 231, 284.

**Experience, level-up and move learning** (`ctx.getter.state`, `battle_command.c:5871-5911`): 5 checking level-up, **11 and 13 stat boxes waiting for A/B**, 15 checking learn, **21 "forget a move?" waiting for yes/no**, **23 choosing the move to forget**, **34 "give up learning?" waiting for yes/no**, 38 done.
- Observed on the move-learn save copy, pressing A on the stat boxes, B on "forget a move?" and A on "give up learning?": 1 → 4 → 6 → 7 → **11** → **13** → 17 → 19 → **21** → (B) → 32 → **34** → (A) → 35 → 4 → cleared. Exactly the decomp's order.
- Observed on the evolution save copy (level-up, no new move): 1 → 4 → 6 → 7 → 11 → 13 → 4 → cleared.
- Observed 2026-10-23 on the yes path and the free slot (traces and the full state table in [In-battle move replace and the free-slot paths](#in-battle-move-replace-and-the-free-slot-paths-2026-10-23)): 21 → (top button) → 22 → **23** (the forget-move screen) → 25 → 27 → 29 → 36 → cleared, and with a free slot 15 → 36 → cleared. The message states advance without a press. 24, 26, 28 and 30 only print and were skipped between reads.

**Outcome** (`BS.outcome`): 1 won, 2 lost, 3 draw, 4 caught, 5 fled; bits `0x40`/`0x80` mark a flee attempt in progress. Observed: 1 after each win, 5 after each flee.

**Evolution after battle** (`app.state == 14`): the evolution task is at `[app.setup + 0x198]`; its state byte at `+0x64` is inferred from undecompiled assembly. Observed on the evolution save copy, pressing only A: 2 → 3 → 5 → 6 → 7 → **8** (about 7 s, the evolving animation) → 9 → 10 → 11 → 12 → 40, then the app closed. The inferred "done" byte at `+0x67` never changed, so that inference is wrong or the byte is set in the last frame. The species after evolution was not read back.

### Observed traces (abridged)

Fought battle (move 1, the wild Pokémon fainted):
```
field free → task 020551B9 (encounter starting, 2 s before the battle app) → task 02055245, mapReady 0
→ app ovy 12, state 3 → 8 → 9
→ cmd 22 script 0 (intro) → cmd 22 script 271 → cmd 5 sel 1   (menu)
→ cmd 5 sel 4 (move list) → cmd 23 → cmd 22 scripts 76, 2, 21, 293 (move and damage)
→ cmd 22 script 6 (faint) → cmd 22 script 276 with getter.state 1, 4 (experience)
→ cmd 40, outcome 1, endFlag 1 → cmd 22 script 4 (battle won) → cmd 45 → app NULL
→ tasks 0205528D, 020552D5 (returning) → field free
```

Fled battle:
```
... → cmd 5 sel 1 → (Run) → sel 14, script 3 (escape) → cmd 22 → cmd 45, outcome 5 → app NULL → ... → field free
```

## What this changes for the bot

Status 2026-10-24: every row below is implemented on `feature/hgss-state-reader` by the [state reader spec](../specs/hgss-state-reader.md), rows S0 to S4 (epic #136), with these differences from the table: `Evolution` is `app.state` 14 only (13 is the battle-end teardown every battle passes through); Run is confirmed by `ctx.sel` leaving the menu and the outcome read afterwards, because `sel == 15` ("Can't escape!") is still decomp-only; the navigator's stall stays position-based but counts only while the field is free, and the warp and poison waits keep their reads with stale-timeouts; the Pokémon Center heal and the poison message are driven by the field-script chain below.

| Today | Replace with | Why |
|---|---|---|
| `0x021E76D2 ∈ {0x41,0x97,0xC0}` for "in battle" and "battle over" | `app != NULL && app.ovyId == 12`; battle over when `ctx.command == 45` or `app == NULL` | The current byte depends on which overlay the battle happens to load and reads garbage during the swap. |
| anchor `+0x230EC == 1` for "menu ready" | `ctx.command == 5 && ctx.sel == 1` | Same value, reached through the game's pointers instead of a heap offset; does not read freed memory after the battle. |
| anchor `+0x692C == 14` for "evolution" | `app.state` 13 or 14 | Same value through the real pointer. |
| anchor `+0x6930` enemy chain | `app.setup` | Same structure through the real pointer. |
| Three racing polls in `WaitForMoveEnd` | `ctx.command`: back to 5/1 = next turn; script 6 = a faint; script 231 = "use next Pokémon?"; 45 = over, with `BS.outcome` saying how | One state machine instead of three guesses. |
| Level-up and move-learn blind macros (`W.325`, `W.1245`, padded 120 frames) | `ctx.getter.state` prompts 11/13, 21, 23, 34 | Press exactly when the game waits for input. |
| Flee loop with no outcome check | `BS.outcome` 5 = fled; `ctx.sel == 15` = "Can't escape!" shown | The TODO "it's possible we won't run" becomes a branch. |
| `Overworld.WaitForReadyAsync` (coordinates non-zero) | the "free to move" signal above | Distinguishes a running script, warp or encounter start from a free field. |
| Navigator stall detection (position unchanged 2.5 s) | `taskman != NULL` means a script or message is up, not a wall | Tells "blocked" from "talking". |

### Self-recovery

With these signals the bot can classify where it is at any moment instead of inferring it from timing: field free, field task running, encounter starting, battle at the menu, in the move list, a message waiting, a script playing, an experience or move-learn prompt, battle over, evolution, another app open. A recovery routine can then press the one input that state needs (B on a message, back out of a submenu, Run from the menu) and check that the state moved, instead of tapping B until something changes. Every unbounded wait loop listed in the appendix can be bounded by "this state has not changed for N seconds".

## Remaining gap: input-ready

`ctx.sel` turns 1 a little before the touch screen accepts input. In the trace, a Run tap sent the moment the menu state read 1 was ignored; the same tap 40 s later fled at once. This is the gap the bot covers with `W.5` and the comment "the menu flag is available before we can actually press". The battle UI keeps its own state in `BattleInput` (pointer at `BS + 0x19C`, field `curMenuId`), but the decomp warns its offsets are unreliable. Finding the UI's "input enabled" flag is the next step; until then, a press should be confirmed by `ctx.sel` changing, and retried if it does not.

## Field scripts: poison and the Pokémon Center (2026-10-22)

The battle chain above says nothing about dialogue on the field. The heal at a Pokémon Center and the "survived the poisoning" message are both **field scripts**, and the game runs them through one more chain hanging off the same `FieldSystem`. Traced live on four PKHeX copies of the Route 33 save (poisoned lead; fainted lead; fainted second slot with a hurt lead; Pokérus on the lead with the "told about Pokérus" flag cleared) while the real bot walked, fled and healed.

```
taskman   = u32 [FS + 0x10]              top field task
task.func = u32 [taskman + 0x04]         0x0203FF45 = Task_RunScripts (Thumb bit set): a script is running
task.env  = u32 [taskman + 0x0C]         ScriptEnvironment while func is Task_RunScripts
env.check = u32 [env + 0x00]             222271 (0x3643F), the environment's magic
env.state = u8  [env + 0x04]
env.count = u8  [env + 0x09]             live script contexts (0 to 3)
env.script= u16 [env + 0x0A]             script id: 1 = this map's NPC script, 2003 = std_survive_poisoning
env.ctx[i]= u32 [env + 0x38 + 4*i]       ScriptContext, i in 0..2
ctx.mode  = u8  [ctx + 0x01]             0 stopped, 1 running bytecode, 2 waiting in a native
ctx.native= u32 [ctx + 0x04]             the native function it waits in (Thumb bit set), meaningful while mode == 2
ctx.pc    = u32 [ctx + 0x08]             bytecode pointer
```

Natives seen, by address (all `scrcmd_c.c` / `scrcmd_message.c` in the decomp, static ARM9 or overlay 1, revision 0):

| `ctx.native` | Decomp | Meaning | Status |
|---|---|---|---|
| `0x021EF349` | `ov01_021EF348` (from `ScrCmd_NPCMsg`) | a message is printing, **or finished and waiting at the ▼ arrow**; the text printer's own `state` (2 or 3) distinguishes those; read since 2026-10-24, [Text printer state](#text-printer-state-2026-10-24) | observed in every message |
| `0x02041075` | `sub_02041074` (`ScrCmd_WaitButton`) | waiting for A or B | observed 2026-10-24 in the nurse's Pokérus explanation (`hgss-heal-variants`, HeartGold8, run 20261024-004624); the earlier messages all used `\r`, which waits inside the printer |
| `0x02041041`, `0x020410F1` | `WaitButtonOrDelay`, `WaitButtonOrDpad` | | decomp only |
| `0x02040BCD` | `ScrNative_WaitStd` (from `ScrCmd_CallStd`) | this context called a standard script and waits for it; the work is in the next context | observed: the map's NPC script (ctx 0) calling the nurse (ctx 1) |
| `0x020477C1` | `sub_020477C0` (`ScrCmd_GetMenuChoice`) | a Yes/No or list menu is open | observed: the nurse's "Heal?" |
| `0x02047745` | `ScrCmd_TouchscreenMenuShow` | restoring the touch menu at the end | observed |
| `0x020476E9` | `sub_020476E8` (touch menu hide) | | observed |
| `0x02041CA9` | not named in the decomp's C | sits between messages; almost certainly `WaitMovement` (the nurse turns and bows) | observed |

Task functions seen on the field (`[taskman + 4]`), adding to the battle ones above: `0x0203FF45` script; `0x02056041` / `0x02056005` stepping onto a door; `0x020552D5`, `0x02055DBD`, `0x0205528D`, `0x02055C31`, `0x02053741`, `0x02056531` the warp and map load; `0x02205B15` and `0x0224BB91` children the nurse script pushes for the fade and the heal animation (the script task is underneath them; `taskman` points at the child, so a reader must treat "task is not the script task" as "script still running" until the environment is gone).

### Poison
Every fourth step with a poisoned party member (`ov01_021E794C`, counter `& 3`), `ApplyPoisonStep` takes 1 HP from each poisoned member and **stops at 1 HP**: no faint on the field in HGSS. The member that reaches 1 HP is cured and `std_survive_poisoning` (2003) starts as a field script: one NPC message, two lines, `\r` at the end, so the printer waits at the arrow (printing native, not `WaitButton`). Observed: `task=0203FF45 script#=2003 ctx0 mode=2 native=PRINTING`; two A presses 0.9 s apart (the first lands while the text is still drawing and only speeds it up); then `task=-`, field free, 1.5 s after the script appeared. The navigator's "poison event" (`WaitForPoisonEventAsync`, party diff every second) and its `ClearFaintText` macro are this script.

### Pokémon Center
The nurse script (`scr_seq_0003.s`, std bank 0040) ran in context 1 under the map's NPC script in context 0 (`WaitStd`). Sequence observed on the Pokérus copy: greeting `PRINTING` → touch menu hide → `MENU_CHOICE` (Yes/No) → `0x02041CA9` (nurse moves) → `PRINTING` → bytecode (the fade and heal animation as child tasks, 6 s) → `0x02041CA9` → `PRINTING` (the Pokérus explanation, three boxes, 10 s at the arrows until the macro's presses) → `TOUCHMENU_SHOW` → contexts gone → `task=-`. The whole interaction was 26 s on each copy.

What each party variant did to the bot's blind macro (`heal_pokemon.yaml`, `heal_pokemon_fainted.yaml`):

| Copy | Script branch (from `scr_seq_0003.s`) | Macro the navigator chose | Result |
|---|---|---|---|
| fainted lead | the first alive member changed, so the "your lead is now X" fade and box play | `HealPokemonFainted` (any member fainted) | in step; field free at the macro's end |
| hurt lead, slot 2 fainted | first alive member unchanged: **no extra box** | `HealPokemonFainted` | one A too many: the nurse script ended, and 2 s later a **second nurse conversation** started (`script#=1` again, `ctxs=2`); the macro's remaining presses happened to answer it and the bot walked away 14 s late |
| hurt lead with Pokérus, flag 0x65 cleared | Pokérus explanation added after the heal, flag set | `HealPokemon` | the three extra boxes were absorbed by the macro's long waits and its later presses this time; it is timing luck, not a branch the macro knows |

So the navigator's rule "any fainted member means the longer macro" is wrong in the direction the script shows: the extra box is tied to the **lead** changing, and a wrong guess either way presses A into the overworld or leaves a box up. The fix is not a third macro. With the chain above the heal is: walk to the tile, face the nurse, then **while the script task or its children are up: press A whenever a context waits in the printing or wait-button natives until the menu native appears, one A there to choose Yes, and B for every wait after that, stopping when the environment is gone and the field is free**, bounded by a stale-timeout. B dismisses a message as well as A does and cannot start a second conversation or answer a second Yes/No, which is what the stray A did on the slot-2 copy. Box count, Pokérus, trainer-card stars and the changed-lead fade stop mattering.

Observed 2026-10-24 (S4 QA, the nurse's Pokérus explanation, run 20261024-004624): a bare `WaitButton` native (`0x02041075`); every other message seen waits inside the text printer. The text printer's `state` byte separates "still drawing" from "waiting for a press". The printer lives in a static task array that is not reachable from `sFieldSysPtr`; it was found and read on 2026-10-24, with the field script's printer slot at `env + 0x05`: see [Text printer state](#text-printer-state-2026-10-24).

Saves used (PKHeX copies of the Route 33 save, made with the spike's `savetool edit`): `Pokemon HeartGold5` lead poisoned at 6 HP; `HeartGold6` lead at 0 HP; `HeartGold7` lead at 15 HP and slot 2 at 0 HP; `HeartGold8` lead at 15 HP with Pokérus strain 1 and event flag `0x65` cleared. PKHeX's Gen 4 event-flag index matched the decomp's `FLAG_WAS_TOLD_ABOUT_POKERUS` (the flag read true on a save whose party carries cured Pokérus, and clearing it brought the dialogue back).

## Post-evolution move learning (2026-10-23)

When a Pokémon evolves after a battle and the new species learns a move at its current level (Croconaw, Ice Fang at Lv 21, right after Totodile evolves), the prompt is **not** the battle's. By then the battle heap has been destroyed (`Heap_Destroy(HEAP_ID_BATTLE)` in `BSTATE_END_INIT`, `battle_022378C0.c`), so `BS`, `ctx` and `ctx.getter` are gone and the reader only sees `app.state == 14`. The evolution scene has its **own** state machine and its own learn flow in `asm/unk_020755E8.s`. That file is undecompiled, so everything below is read from the assembly. The only code it shares with the battle's getter is `MonTryLearnMoveOnLevelUp` (`pokemon.c:3142`), which finds the move. The messages, the Yes/No menu and the hand-off to the forget-move screen are separate. The same module runs the field evolution (`start_menu.c:1472`) and the trade evolution (`launch_application.c:959`), with different flags.

```
app.setup    = u32 [app + 0x18]          BattleSetup
evo          = u32 [app.setup + 0x198]   EvolutionTaskData (BattleSetup.evolutionTaskData, battle_setup.h:63), 0xBC bytes,
                                         allocated by sub_02075A7C in BSTATE_EVOLUTION_INIT; valid only while app.state == 14
evo.state    = u8  [evo + 0x64]          scene state, table below (jump table at 0x02075F40, 46 cases)
evo.printer  = u8  [evo + 0x65]          text printer id of the current message
evo.timer    = u8  [evo + 0x66]          frame countdown used by several states
evo.done     = u8  [evo + 0x67]          set to 1 in states 40 and 45; sub_02075D3C returns it, and the battle app then frees evo
evo.learnIdx = s32 [evo + 0x68]          cursor into the new species' level-up learnset (MonTryLearnMoveOnLevelUp)
evo.move     = u16 [evo + 0x6C]          the move being offered
evo.slot     = u8  [evo + 0x6E]          the slot chosen to forget (from the summary app)
evo.flags    = u32 [evo + 0x7C]          3 after a battle: bit 0 lets B cancel the evolution in state 8, bit 1 adds the "What?" box
evo.species  = u16 [evo + 0x60] -> u16 [evo + 0x62]   old and new species
evo.mon      = u32 [evo + 0x28]          the party Pokémon (its species is written in state 11)
evo.yn       = u8  [evo + 0x8A]          Yes/No sub-state: 0 waiting for input, 1 confirm animation playing
evo.cursor   = u8  [evo + 0x8B]          Yes/No cursor: 1 Yes (top button, the default), 2 No (bottom button)
evo.summary  = u32 [evo + 0x38]          OverlayManager of the Pokémon summary app (gOverlayTemplate_PokemonSummary) while state is 23; NULL otherwise
evo.sumArgs  = u32 [evo + 0x3C]          PokemonSummaryArgs (unk_02088288.h), 0x3C bytes: +0x12 mode (2 = move learn),
                                         +0x18 moveToLearn, +0x16 the result (forgotten slot 0-3; 4 = the new move itself, i.e. cancel)
```

The offsets come from these routines:
- `sub_02075A7C`, the constructor
- `sub_02075E14`, the state machine
- `sub_0207584C`, `sub_020758D0` and `sub_02075A04`, the Yes/No menu
- state 22, which fills the summary args

Message ids are in `msg_0197.gmm` (narc 27 file 197, the battle text bank). A message that ends in `\r` keeps its printer active until A or B is pressed. So when a state waits on `TextPrinterCheckActive` for such a message, it is waiting for input.

The Yes/No menu (`sub_020758D0`) is two touch buttons on the bottom screen, with hitboxes at `_020FFECC`:
- **Yes** is the top button, y 50-92, x 3-251.
- **No** is the bottom button, y 99-140.

On the keys, Up and Down move the cursor, A confirms it (the cursor starts on Yes), and **B answers No**. The bot's existing `EvolutionDoNotLearnMacro` taps (125,125) and then (125,75), which are these two buttons.

| `evo.state` | Meaning | Waits for | Status |
|---|---|---|---|
| 0, 1 | Init countdown; picks the "What?" path from `flags` bit 1 | | 1 observed once (2026-10-23); 0 gone before the first read |
| 2 | Prints 916 "What?" | | observed |
| **3** | **"What?" waiting at the arrow** | A | observed: held 10 s until A |
| 4 | Prints 917 "X is evolving!" (no `\r`) | | decomp only (skipped between reads) |
| 5, 6, 7 | Cry, old sprite's animation, particles start | | observed |
| **8** | **The evolving animation.** With `flags` bit 0 set, **B here cancels the evolution** (state 41) | nothing; never press B | observed, 6.7 s |
| 9, 10, 11 | Flash, new sprite and its cry; species written | | observed |
| **12** | **"Congratulations! Your X evolved into Y!" (918)** | A, then a 40-frame countdown | observed: held 10 s until A |
| 13 | Calls `MonTryLearnMoveOnLevelUp`. Nothing left to learn: 39. Free slot: 37. Four moves known: 14 | | observed passing through (13 → 14, 36 → 13 → 39, and 12 → 37 on the free slot, 13 itself skipped) |
| 14 | Prints 1193 "X wants to learn the move Y." | | observed |
| **15** | **1193 waiting at the arrow** | A | observed |
| 16 | Prints 1194 "However, X already knows four moves." | | decomp only (skipped) |
| **17** | **1194 waiting at the arrow** | A | observed |
| 18, 19 | Prints 1195 "Should a move be deleted?" (no `\r`) | | 19 observed |
| 20 | Opens the Yes/No ("Forget a move!" / "Keep old moves!") | | observed |
| **21** | **"Should a move be deleted?" Yes/No** | Yes: 22. No: 32 | observed: touching (125,125) for No gave `yn` 1, `cursor` 2, then 32 |
| 22 | Waits for the fade 21 started, fills `sumArgs`, creates the summary app | | observed (0.5 s) |
| **23** | **The forget-move list.** The summary app runs (`evo.summary` non-null) until it exits | the summary app's own input (next subsection) | observed |
| 24 | Waits for the fade in, reads `sumArgs + 0x16`. Value 4: 32. Otherwise the slot into `evo.slot`, then 25 | | observed (slot 0 → 25) |
| 25-31 | Prints 1199 "1, 2, and... Poof!", 1200 "X forgot Y.", 1201 "And..." and 1202 "X learned Y!". After each of the first three, a wait state (**26, 28, 30**) holds at the arrow, then a 30-frame countdown. 31 prints 1202, writes the move into the slot and goes to **37** | A on 26, 28, 30 | observed: 26, 27, 28, 29, 30, 31 (25 skipped between reads); each wait held until A |
| 32, 33 | Prints 1197 "Stop trying to teach Y?" (no `\r`) | | observed |
| 34 | Opens the Yes/No ("Give up on Y!" / "Don't give up on Y!") | | observed |
| **35** | **"Stop trying to teach Y?" Yes/No** | Yes: 36. No: back to 14 | observed: A on the default cursor (Yes) gave `yn` 1, `cursor` 1, then 36 |
| **36** | **1198 "X did not learn the move Y." waiting at the arrow** | A, then a 30-frame countdown and back to 13 | observed |
| 37, 38 | "X learned Y!" (message 4 from the free-slot path, 1202 from 31) waiting at the arrow, then its fanfare and back to 13 | A on 37 | observed from 31: 37 held 12 s until A, 38 1.0 s, then 39. Observed on the free slot (2026-10-23, `Pokemon HeartGold10`): 12 → 37 held 8.4 s until A, 38 1.0 s, 13, 40 |
| 39, 40 | Fade out; `done = 1` | | 40 observed with `done` 1 |
| 41-45 | Evolution cancelled: 919 "Huh? X stopped evolving!", then `done = 1` | A on 44 | decomp only |

### Sequence observed

The save was `Pokemon HeartGold4`, the Totodile save: Lv 20, one EXP short of 21, four moves, none of them Ice Fang. I fought one wild battle on Route 33 with move 1 and sent every press by hand over ApiHawk's command port, while the probe read the chain every 80 ms and logged each change:

```
battle: getter 1 → … → 11 (A) → 12 → 13 (A) → 14 → 3 → 4 → 37   (level-up stat boxes; Totodile learns nothing at 21)
→ cmd 40 → app.state 13 → 14, evo = 0x022C0604, flags 3, species 158 → 159
→ evo 2 → 3 (waits; A) → 5 → 6 → 7 → 8 (6.7 s) → 9 → 10 → 11 → 12 (waits; A)
→ 14, learnIdx 8, move 423 (Ice Fang) → 15 (waits; A) → 17 (waits; A) → 19 → 20 → 21 (waits, yn 0 cursor 1)
→ touch (125,125) "Keep old moves!" → yn 1 cursor 2 → 32 → 33 → 34 → 35 (waits, yn 0 cursor 1)
→ A "Give up on Ice Fang!" → yn 1 cursor 1 → 36 (waits; A) → 40, learnIdx 16 → done 1 while app.state went back to 13
→ app.state 0 → app NULL → tasks 02050C19, 0205528D, 020552D5 → field free
```

- **Every waiting state is a real input wait.** Each one held for 3 to 13 s with no press, so none of them is timing.
- **`evo` is freed memory once `done` is set.** The battle app frees `evo` but does not clear `app.setup + 0x198`. In the same read that showed `app.state` 13, the pointer still read `0x022C0604` with `done` 1. A reader must use `evo` only while `app.state == 14`.
- **This corrects the "Evolution after battle" paragraph above.** `+0x67` is the done byte. It is set in state 40 (or 45), and the earlier trace missed it because the app frees the task in the next frame.
- **The save was not changed.** Its sha256 was `18e10c7b…4f25c87` before and after. EmuHawk was closed with `taskkill /F`.

Not observed:
- the forget-move list (states 22-31), because the trace answered No (traced separately, next subsection)
- learning into a free slot (37, 38); traced 2026-10-23 in [In-battle move replace and the free-slot paths](#in-battle-move-replace-and-the-free-slot-paths-2026-10-23)
- a cancelled evolution (41-45)
- a second move offered in the same evolution (the loop back through 13 handles it)

### What this changes for the bot

The bot (task S2c, #149) adds one block, `[0, 4, 0x18, 0x198, 0]` for 0x65 bytes (through `evo.state`; as built in #150, which reads neither `evo.move` nor the Yes/No fields and keeps `EvolutionFreeSlotMacro` for the free-slot path, decomp-only at the time and observed since). It is read only while `app.state == 14`, and the `Evolution` snapshot exposes `evo.state` in `Prompt`. The evolution macros (`EvolutionMacro`, `EvolutionLearnMoveInit`, `EvolutionDoNotLearnMacro`, `EvolutionFreeSlotMacro`) become reads and presses:

| `evo.state` | The bot presses |
|---|---|
| 3, 12, 15, 17, 26, 28, 30, 36, 37 | A (also 44, once observed) |
| 8 | nothing; never B |
| 21 | the bottom button (125,125) or B to keep the old moves; the top button (125,75) to forget one |
| 23 | the slot in the summary app: its row, then the confirm button, read from the summary app's own state (next subsection) |
| 35 | the top button (125,75) or A to give up; the bottom button to try again |

Each press is confirmed by `evo.state` changing. The evolution is finished when `app == 0`.

### The forget-move list (yes path), 2026-10-23

Answering Yes at state 21 starts a fade and goes to 22. State 22 waits for the fade, fills `sumArgs` and creates the summary app's `OverlayManager` (`sub_02077394`). State 23 calls `OverlayManager_Run` on it every frame until it returns TRUE, then deletes it, writes NULL to `evo.summary`, rebuilds the evolution scene's graphics and fades in. The summary app is undecompiled too: `asm/unk_02088288.s`, with `PokemonSummary_Init`, `_Main` and `_Exit` at `gOverlayTemplate_PokemonSummary`. Only `PokemonSummaryArgs` (`unk_02088288.h`) and `OverlayManager` (`overlay_manager.h`) are C structs. Every offset into the app's own data comes from the assembly.

```
evo.summary  = u32 [evo + 0x38]           OverlayManager (overlay_manager.h), non-NULL only while evo.state is 23
sum.exec     = u32 [evo.summary + 0x10]   exec_state: 1 Init running, 2 Main running, 3 Exit (one frame, not seen)
sum.proc     = u32 [evo.summary + 0x14]   proc_state: Main's own state (table below); 0 again during Init and Exit
sum.args     = u32 [evo.summary + 0x18]   == evo.sumArgs
sum.data     = u32 [evo.summary + 0x1C]   the app's data, 0x7D8 bytes on heap 0x13 (OverlayManager_CreateAndGetData in Init)
data.args    = u32 [sum.data + 0x22C]     == evo.sumArgs again
data.moves   = u16 [sum.data + 0x264 + 2*i], i 0..3   the Pokémon's four moves as the list shows them
data.cursor  = u8  [sum.data + 0x7BD] & 0x0F          the list cursor: 0-3 a known move, 4 the move to learn; starts at 0
data.flashTo = u8  [sum.data + 0x7CF]     the state that proc 15 (button flash) goes to when it ends
evo.sumArgs  = u32 [evo + 0x3C]           PokemonSummaryArgs; allocated before state 22 (mode, slot, result and move read 0 at 21)
  +0x00 the party Pokémon (state 22 writes evo.mon there, not a Party), +0x04 Options,
  +0x11 0, +0x12 mode 2, +0x13 partyCount 1, +0x14 partySlot 0, +0x18 moveToLearn (= evo.move),
  +0x16 the result: the chosen slot 0-3, or 4 for "keep the old moves",
  +0x17 1 when the app was left with B, 0 otherwise (the evolution scene does not read it)
```

Live chain on the trace (one run): `evo 0x022C053C → summary 0x022C4630 → data 0x022F0304`, `sumArgs 0x022D7564` (also at `data + 0x22C`), `data.moves` = 55, 99, 44, 184 (Water Gun, Rage, Bite, Scary Face). As one ApiHawk block: `[0, 4, 0x18, 0x198, 0x38, 0]` for 0x20 bytes reads the `OverlayManager`. `[0, 4, 0x18, 0x198, 0x38, 0x1C, 0x7B8]` for 0x20 bytes reads the cursor. Both fail at the `0x38` hop whenever `evo.summary` is NULL.

`PokemonSummary_Main` switches on `sum.proc` through a 23-case jump table at `0x02088442`. In mode 2, state 1 jumps straight to the move list (8). The flow is:

| `sum.proc` | Routine | Meaning | Waits for | Status |
|---|---|---|---|---|
| 0 | inline | Starts the fade in, then 1 | | observed (and during Init, `exec` 1) |
| 1 | `sub_02088B08` | Waits for the fade. Mode 2: 8; mode 4: 16; else 2 (the normal summary pages) | | observed |
| **8** | `sub_02089028` | **The move list**: four moves plus the new one, cursor on slot 0 | **Up/Down** moves the cursor over 0-4 (wraps, skips empty slots). **A** on 0-3 goes to 9. A on 4 sets result 4 and leaves (21). **B** sets result 4 and +0x17 1, then flashes and leaves (15 → 21). **Touch** on a move row picks that slot and goes to 9. Touch on the new move's row only moves the cursor to 4. Touch on the back button sets result 4 and leaves. An HM in the slot, while a move is offered, shows a "can't forget" note and stays in 8 | observed: held 14 s, touch (64,24) → 9 |
| **9** | `sub_02089208` | **Confirm**: the chosen move's details and a "Forget" button | **A** or touch on the button at y 152-183: result = cursor, +0x17 0, flash, leave (15 → 21). **B** or touch on the back button: flash, back to 8 | observed: held 14 s, touch (64,168) → 15 |
| 15 | `sub_02089698` | Button flash: palette swap, 4 frames, swap back, 2 frames, then `data.flashTo` | | observed (one read) |
| 21 | `sub_02089670` | Starts the fade out, then 22 | | decomp only (one frame) |
| 22 | `sub_02089680` | Waits for the fade, then Main returns TRUE: Exit runs and the app is freed | | observed |

Touch hitboxes, bottom screen, `TouchscreenHitbox` rects `{top, bottom, left, right}`:
- Move rows, `_021038D4` (`TouchscreenHitbox_FindRectAtTouchNew`): slot 0 y 8-39, slot 1 y 40-71, slot 2 y 72-103, slot 3 y 104-135, all x 8-127. Centres: (64,24), (64,56), (64,88), (64,120).
- `_021038B4`, y 152-183, x 8-127, centre (64,168): the new move's row in 8, the "Forget" confirm in 9.
- `_021038AC`, y 165-188, x 190-249, centre (220,176): the back button. Cancel in 8, back to the list in 9.

On keys, slot *n* from the list (cursor 0) is Down *n* times, A, A. B in 8 is "keep the old moves". B in 9 returns to the list. Every key the routines read is a new press (`gSystem + 0x48`), apart from the d-pad repeat in state 2, which mode 2 never enters.

#### Sequence observed

Same save and battle as above. At state 21 the top button was touched, then the first move (Water Gun) was picked by touch:

```
evo 21 (yn 0 cursor 1) → touch (125,75) "Forget a move!" → yn 1 cursor 1 → 22 (0.27 s) → 23, summary 022C4630
  sum exec 1 proc 0 (Init; sumArgs now mode 2, move18 423) → exec 2 proc 0 → proc 1 → proc 8, cursor 0 (held 14 s)
  → touch (64,24) → proc 9 (held 14 s) → touch (64,168) → proc 15 → proc 22 → summary NULL (0.55 s after the touch)
→ evo 24 (sumArgs+0x16 = 0) → 26 (held 18 s; A) → 27 → 28 (held 4 s; A) → 29 → 30 (held 4 s; A) → 31 → 37 (held 12 s; A)
→ 38 (1.0 s fanfare) → 39, learnIdx 16 → 40 → done 1 → app.state 15 → app NULL → tasks 02050C19, 0205528D, 020552D5 → field free
```

- **25 was skipped between reads.** It only prints 1199 and moves on. 1199 ends in `\r`, so 26 is a real input wait, as are 28 (1200) and 30 (1201). Each one moved about 1.1 s after its A: the printer finishes, then the 30-frame `evo.timer` counts down.
- **State 31 goes to 37, not 32.** It prints 1202 "X learned Y!" (which ends in `\r`), zeroes the slot's PP-ups (`MON_DATA_MOVE1_PP_UPS + slot`), writes the move with `MonSetMoveInSlot`, and sets state 37. From there the yes path shares 37 (printer, A) and 38 (fanfare and timer, then 13) with the free-slot path. The 13 → 39 step was too quick to read.
- **`sumArgs + 0x16` read 0 after the app closed.** That is also its value before the app ran, so this trace alone cannot tell a written 0 from an untouched byte. Two things do show it: the scene went 24 → 25 and not 32, and the decomp's confirm (`sub_02089208`) stores the cursor there. A slot other than 0 was not traced.
- **Touch on a move row skips the cursor.** It writes the slot into `data.cursor` and goes to 9 in the same frame. `data.cursor` stayed 0 throughout because slot 0 was the pick.
- After `done`, `app.state` read 15 (13 on the No trace). Both are the battle app's teardown. The rule "use `evo` only while `app.state == 14`" holds.
- **The save was not changed.** Its sha256 (and the `.bak`'s) was `18e10c7b…4f25c87` before launch and after `taskkill /F`. The game was never saved.

#### What this changes for the bot

A task that forgets a move reads two more blocks while `evo.state == 23`: the `OverlayManager` (`[… 0x198, 0x38, 0]`, 0x20 bytes) and the cursor byte (`[… 0x198, 0x38, 0x1C, 0x7BD]`, 1 byte). It presses:
- at 21, the top button (125,75), confirmed by `evo.state` reaching 22;
- in 23, nothing until `sum.exec == 2 && sum.proc == 8`;
- for slot *n*, the row centre (64, 24 + 32*n), confirmed by `sum.proc` reaching 9;
- in 9, the confirm button (64,168) or A, confirmed by `evo.summary` going NULL and `evo.state` reaching 24 or later;
- to keep the old moves from inside the list instead, B or the back button (220,176) in 8, which leads to 32 as the No path does;
- A at 26, 28, 30 and 37, each confirmed by `evo.state` changing.

The slot to forget is the task's decision: compare `data.moves` (or the party read) against what it wants to keep. Touch is simpler than keys because it does not depend on the cursor. The key path (Down *n*, A, A) needs `data.cursor` read back between presses.

## In-battle move replace and the free-slot paths (2026-10-23)

Three traces, one wild battle each on Route 33, fought with move 1, every press sent by hand over ApiHawk's command port while the probe read the chain every 80 ms:
- `Pokemon HeartGold2`: Bulbasaur Lv 14 with four moves learns Take Down at 15 and replaces Tackle (the yes path).
- `Pokemon HeartGold9`: the same Bulbasaur with three moves learns Take Down into the free slot, in battle.
- `Pokemon HeartGold10`: Totodile Lv 20 with three moves evolves at 21, and Croconaw learns Ice Fang into the free slot.

### The getter's fields and states

`GetterWork` (`battle.h:633`, 0x58 bytes) and the player's battle buffer are C structs, so these offsets come from the decomp, not the assembly. Each one was confirmed live.

```
getter          = u32 [ctx + 0x178]
getter.state    = u32 [getter + 0x28]
getter.learnIdx = s32 [getter + 0x38]    unk30[2], the cursor into the level-up learnset (0 → 7 → 14 on both Bulbasaur traces)
getter.move     = u32 [getter + 0x40]    unk30[4], the move offered; written only on the four-move path (36, Take Down, from state 17); stale otherwise
getter.slot     = u32 [getter + 0x44]    unk30[5], the slot to forget; written in state 23 (read 0 after Tackle was picked); stale before
ctx.buffer      = u8  [ctx + 0x2300]     battleBuffer[0][0], the player battler's answer from its controller
```

`ctx.buffer` reads 1 after a move is chosen in the move list, 1 after "Forget a move!", and *slot + 1* after a move is picked on the forget-move screen. The No answer and a cancelled screen write `0xFF` (decomp only). The getter clears it before each question, and it read 0 on entry to 21 and to 23.

The full state enum is in `battle_command.c:5871-5911`. The names below are the decomp's.

| `getter.state` | Name | What it does | Waits for | Status |
|---|---|---|---|---|
| 11, 13 | `LEVEL_UP_SUMMARY_PRINT_DIFF_WAIT`, `_TRUE_WAIT` | The stat boxes | A, B, X, Y or a touch | observed |
| 15 | `CHECK_LEARN_MOVE` | Calls `MonTryLearnMoveOnLevelUp`. Nothing to learn: 3 (back to the experience gauge). Four moves known: 16. Free slot: writes the move, prints 4 "X learned Y!" and goes to 36 | | observed on both paths (15 → 17, 15 → 36) |
| 16, 17 | `WANTS_TO_LEARN_MOVE_PRINT`, `_WAIT` | 1178 "X wants to learn the move Y." | nothing; the text advances by itself (17 lasted 2.4 s) | 17 observed |
| 18, 19 | `CANT_LEARN_MORE_MOVES_PRINT`, `_WAIT` | 1179 "But X can't learn more than four moves." | nothing (19 lasted 2.5 s) | observed |
| 20 | `MAKE_IT_FORGET_PROMPT` | Opens the two-button menu ("Forget a move!" / "Keep old moves!") | | decomp only (skipped between reads) |
| **21** | `MAKE_IT_FORGET_ANSWER` | **"Make it forget another move?"** `ctx.buffer` 1: 22. `0xFF`: 31 | **the top button (Yes) or the bottom one (No)** | observed: held 14.6 s, touch (128,68) → 22 |
| 22 | `MAKE_IT_FORGET_WAIT` | Prints 1183 "Which move should be forgotten?" (no `\r`), then sends controller command `0x2B` (`ov12_02263D14`), which opens the forget-move screen | | observed (0.55 s) |
| **23** | `MAKE_IT_FORGET_INPUT_TAKEN` | **The forget-move screen is up.** `ctx.buffer` `0xFF`: 31. *slot + 1*: `getter.slot` = slot, then 24 | the screen's own input (below) | observed: held 36 s until the confirm |
| 24, 25 | `ONE_TWO_POOF`, `_WAIT` | 1189 "1, 2, and... ... Poof!" | nothing (25 lasted 3.6 s) | 25 observed |
| 26, 27 | `FORGOT_HOW_TO_USE`, `_WAIT` | 1190 "X forgot how to use Y." | nothing (2.3 s) | 27 observed |
| 28, 29 | `AND_DOTDOTDOT`, `_WAIT` | 1191 "And..." | nothing (1.9 s) | 29 observed |
| 30 | `LEARNED_MOVE` | 1192 "X learned Y!"; zeroes the slot's PP Ups, `MonSetMoveInSlot`, then 36 | | decomp only (skipped) |
| 31, 32 | `MAKE_IT_FORGET_CANCELLED`, `_WAIT` | 1184 "Well, then..." | nothing | 32 observed (No trace, 2026-10-20) |
| 33 | `GIVE_UP_LEARNING_PROMPT` | Opens the two-button menu ("Give up on Y!" / "Don't give up on Y!") | | decomp only |
| **34** | `GIVE_UP_LEARNING_ANSWER` | **"Should this Pokémon give up on learning this new move?"** Yes: prints 1188 and goes to 35. No: back to 16 | the top button (give up) or the bottom one | observed (2026-10-20) |
| 35 | `GIVE_UP_LEARNING_WAIT` | 1188 "X did not learn Y.", then 15 | nothing | observed (2026-10-20) |
| 36 | `LEARNED_MOVE_WAIT` | "X learned Y!" (1192 or 4), then 15 for the next move | nothing (3.3 s on the yes path, 3.2 s on the free slot) | observed on both paths |
| 37, 38 | `CHECK_DONE`, `DONE` | Next Pokémon; the task ends | | decomp only |

- **In battle, only the stat boxes and the two Yes/No menus wait for input.** Every message state (17, 19, 25, 27, 29, 36) advanced by itself, 1.9 to 3.6 s after it started, with no press, although the messages end in `\r`. This is unlike the evolution scene, whose message states hold until A.
- **The two-button menus** are `BattleInput`'s `BATTLE_MENU_KEEP_FORGET_MOVE` and `BATTLE_MENU_GIVE_UP_ON_MOVE` (`battle_input.c`), with the touch rects `sTouchscreenRectTwoOptionsMenuButtons`, `{top, bottom, left, right}`:
  - the top button, Yes, y 40-96, x 8-248, centre (128,68);
  - the bottom button, No, y 112-168, centre (128,140).

  `CommandPreprocessor`'s `NewMove` and `DoNotLearnComplete` (128,70) are the top button; `DoNotLearn` (128,140) is the bottom one.

### The forget-move screen

The forget-move screen is not the summary app that the evolution scene uses. It is overlay 8, the battle's own party and summary screen (`asm/overlay_08.s`, undecompiled). Everything in this subsection is read from the assembly, apart from the touch points that were tapped.

How it is launched:
1. Controller command `0x2B` is entry 43 of the battler's command table `ov12_0226D010`. That entry is `ov12_022593D4`, which calls `ov12_0225B028`.
2. `ov12_0225B028` starts a SysTask, `ov12_022609F8`, with 0x10 bytes of data.
3. The task fades out, then calls `ov12_02237B0C`. That frees `BattleInput`, unloads overlay 7 and loads overlay 8.
4. It allocates a 0x38-byte args block and fills it: +0x00 the party, +0x08 the `BattleSystem`, +0x11 the party slot, +0x24 the move to learn, +0x28 the battler, +0x35 3.
5. It calls the overlay's entry at `0x0221BE20`. The assembly's symbol `ov10_0221BE20` is a mislabel: overlays 8 and 10 share the address.
6. When the screen sets args +0x36 (done), the task reads args +0x34. A slot 0-3 is sent back to the getter as *slot + 1*, and 4 is sent as `0xFF`. The task then restores `BattleInput`.

The screen runs as a SysTask with 0x2090 bytes of data:
- `+0x2078` is its state, a jump table of 27 cases.
- `+0x2079` is the state it goes to after a button flash.
- `+0x208D` is the chosen index.

**No pointer reaches it from `sFieldSysPtr`.** The args and the task data are referenced only from the controller task's own data, and no struct points at that. So the bot cannot read the screen's cursor or state through the chain. It sees `getter.state == 23` while the screen is up, and `ctx.buffer` and `getter.slot` once it closes.

| Screen state | Routine | Page | Touch hitboxes `{top-bottom, left-right}` and what they do |
|---|---|---|---|
| 19 | `ov08_0221C604`, rects `ov08_02224F3C` | **The move list**: four moves in a 2×2 grid, the new move below them | slot 0: y 48-95, x 0-127, centre (64,72). Slot 1: y 48-95, x 128-255, (192,72). Slot 2: y 96-143, x 0-127, (64,120). Slot 3: y 96-143, x 128-255, (192,120). Each sets the result to the slot, flashes, and moves on to the confirm page. The new move: y 144-191, x 64-191, (128,168); sets the result to 4 and also goes on to the confirm page. A toggle at y 0-39, x 184-255. Back: y 152-191, x 216-255, (236,172); sets the result to 4 and leaves straight away |
| 20 | `ov08_0221C6F8`, rects `ov08_02224E44` | **Confirm** | Forget: y 152-191, x 0-207, centre (104,172); leaves the screen (state 25), unless a check in `ov08_0221DB54` (probably the HM rule) keeps it on the page. Back: y 152-191, x 216-255, (236,172); returns to the list. A toggle at y 0-39, x 184-255 |
| 22 | | Button flash, then the state in `+0x2079` | |
| 25 | | Fade out, exit | |

`CommandPreprocessor`'s `NewMoveOne`-`NewMoveFour` (60,70), (190,70), (60,120), (190,120) and `Forget` (100,170) fall inside these rects. The keys (`ov08_02224C94`) were not traced.

### Sequences observed

The yes path, on `Pokemon HeartGold2`:
```
getter 4 → 6 → 7 → 8 → 9 → 10 → 11 (A) → 12 → 13 (A) → 15 → 17 (move 36) → 18 → 19 → 21 (held 14.6 s)
→ touch (128,68) "Forget a move!" → buffer 01 → 22 (0.55 s) → 23 (buffer 00; the screen loads)
→ 26 s later, touch (64,72) on Tackle → 10 s later, touch (104,172) "Forget" → 0.7 s later 25, buffer 01, getter.slot 0
→ 27 → 29 → 36 → 3 → 4 → 5 → getter NULL → app.state 10 → 0 → app NULL → tasks 02050C19, 0205528D, 020552D5 → field free
```
No press was needed from the confirm to the field, 14 s in all. 24, 26, 28, 30 and 15 were skipped between reads. The screen's own states could not be read. The two taps are shown to have worked by the result: slot 0 sent back and the 24 path taken. A miss would have left 23 held, and a tap on the new move's row would have led to 31.

The free slot in battle, on `Pokemon HeartGold9`:
```
getter … → 11 (A) → 12 → 13 (A) → 15 → 36 (3.2 s, "Bulbasaur learned Take Down!") → 3 → 4 → 5 → getter NULL → battle end → field free
```
No press after the stat boxes. `getter.move` stayed at its stale value, because the free-slot branch keeps the move in a local. That Take Down landed in slot 4 was not read back. The decomp writes it in 15 (`MonTryLearnMoveOnLevelUp` returns the move only after adding it).

The free slot after evolution, on `Pokemon HeartGold10`:
```
battle: stat boxes 11 (A), 13 (A) → app.state 14, evo flags 3, species 158 → 159
→ evo 1 → 2 → 3 (A) → 5 → 6 → 7 → 8 (6.7 s) → 9 → 10 → 11 → 12 (A) → 37 (learnIdx 8; held 8.4 s until A) → 38 (1.0 s) → 13 → 40
→ done 1, app.state 15 → app NULL → tasks 02050C19, 0205528D, 020552D5 → field free
```
13 → 37 itself was too quick to read: the read after the A at 12 already showed 37. Likewise 39 between 13 and 40. `evo.move` read 0 throughout, because the free-slot branch, like the battle's, does not store the move.

**The saves were not changed.** Before launch and after `taskkill /F`, each save and its `.bak` hashed the same:
- `Pokemon HeartGold2`: `5a2a3a28…7c26cbd`
- `Pokemon HeartGold9`: `b5707cc6…141c79`
- `Pokemon HeartGold10`: `b3853a93…dc25a2a`

The game was never saved.

### What this changes for the bot

A level-up move-learn task in battle reads `getter.state`, and `ctx.buffer` and `getter.slot` to confirm. It presses:
- A at 11 and 13, as today.
- **Nothing at 15, 17, 19, 36 or any message state.** A free slot is 15 → 36 → the getter clears, with no prompt, so the battle's `FreeSlotMacro` (`T.A;W.30;T.A;W.15`) presses into nothing the game waits for.
- At 21, the top button (128,68) to replace a move, confirmed by 22 or 23; the bottom button (128,140) to keep the old moves, confirmed by 31 or 32.
- In 23, after the screen has loaded, the slot's centre: (64,72), (192,72), (64,120) or (192,120). Then Forget (104,172). The screen's state cannot be read, so the task cannot see when it is ready. It retries the pair until `getter.state` leaves 23, which happened 0.7 s after the confirm.
  - 24 or later, with `getter.slot` equal to the slot, means the move was replaced.
  - 31 means the pair landed on "keep" (the back button, or the new move's row).

  The existing macro (`S.NewMove;W.165;S.New{n};W.20;S.Forget`) waits 165 frames for the load. Nothing observed says that is enough or too much; the trace waited 26 s.
- At 34, as today.

After the evolution, the free slot is 12 (A) → 37 (A) → 38 → 40. `EvolutionFreeSlotMacro` (`W.160;T.A;W.15`) becomes A at `evo.state == 37`, confirmed by 38, as on the yes path.

## Text printer state (2026-10-24)

The evolution scene's message states and the field's printing native both wait on `TextPrinterCheckActive(id)`. That only asks "is there a printer task in slot `id`". The printer's own `state` byte says whether it is still drawing or is waiting at the ▼ arrow for A or B. Read through the static task array below, it lets a task press **once**, after the text has finished, instead of pressing as soon as the state appears and retrying every 1.5 s. Traced live on `Pokemon HeartGold4` (two runs: the No path, then the yes path) and `Pokemon HeartGold5` (the poison message).

```
sTextPrinterTasks = 0x021D1F74           SysTask *[8] (text.c, static ARM9 .bss, US rev 0); MAX_TEXT_PRINTERS = 8 (text.h)
task      = u32 [0x021D1F74 + 4*id]      NULL: no printer in that slot (TextPrinterCheckActive returns FALSE)
printer   = u32 [task + 0x10]            SysTask.data (sys_task.h): the TextPrinter (font_types_def.h), 0x34 bytes
pr.active = u8  [printer + 0x27]         1 for as long as the printer exists; not useful on its own
pr.state  = u8  [printer + 0x28]         RenderText's state (render_text.c), values below
pr.delay  = u8  [printer + 0x2A]         frames left before the next character (or of the pause, in state 6)
pr.id     = u8  [printer + 0x2C]         its own slot; equals id
evo.printer = u8 [evo + 0x65]            the slot of the evolution scene's message (sub_020772F8 returns AddTextPrinterParameterized's id)
env.printer = u8 [env + 0x05]            ScriptEnvironment.textPrinterNum (script.h): the slot the printing native 0x021EF349 waits on
```

How it was found:
- `text.c` keeps no printer structs, only `static SysTask *sTextPrinterTasks[MAX_TEXT_PRINTERS]`, declared just after the address-named `_021D1F6C`. The printer is the task's data.
- A RAM dump on the field showed 32 zero bytes at `0x021D1F74`-`0x021D1F93`, between `_021D1F6C` and the font lookup table. During the next battle message, slot 0 held a task whose data read `id` 0, `active` 1, and `state` 0 then 2.
- The word `0x021D1F74` appears in four literal pools at `0x0202001C`-`0x02020090`, the text.c functions (`0x021D1F6C` is in pools at `0x020202E4`-`0x02020408`).

The array is not reachable from `sFieldSysPtr`. The probe reads its 32 bytes raw (opcode `0x01`), then the printer behind each non-NULL slot as a chain rooted at `0x021D1F74 + 4*id` with hops `[0x10, 0]`. A chain whose root holds NULL makes ApiHawk return an empty body for the whole request, so only non-NULL slots go in. Every message on the three traces used slot 0.

`pr.state` values (`RenderText`, `render_text.c`):

| `pr.state` | Meaning | Set by |
|---|---|---|
| 0 | Drawing characters, including the frames between them (`pr.delay`) | the start, and the end of every wait |
| **2** | **Waiting at the arrow**, then clears the box | `\r` (`0x25BC`), or control `0xFE06` with field `0xFE01` |
| **3** | **Waiting at the arrow**, then scrolls one line | `\f` (`0x25BD`), or control `0xFE06` with field `0xFE00` |
| 4 | Scrolling, after 3 or 8 | |
| 5 | One frame, then 0 | |
| 6 | A timed pause of `pr.delay` frames; no input | control `0x201`; seen as the dots of 1199 "1, 2, and... ... Poof!" |
| **7**, **8** | **Waiting at the arrow**, as 2 and 3 | controls `0x207`, `0x208` |
| slot NULL | Finished: the string reached its end and the task destroyed itself | `RenderFont` returning `RENDER_FINISH` |

- **2, 3, 7 and 8 wait for a new A or B** (`TextPrinter_WaitWithDownArrow`, `gSystem.newKeys`), or a touch where the scene enables touch. A message that ends in `\r` sits in 2 at its end. One A clears it, the next character is the end of the string, and the slot goes NULL. A message without `\r` goes from 0 to NULL with no press.
- **An A while the printer is in 0 only speeds up the drawing** (`canABSpeedUpPrint`). It is not remembered for the arrow, so an early press is lost, and the 1.5 s retry is what makes today's presses work.
- **The battle turns auto-scroll on** (`TextFlags_SetAutoScrollParam(1)` or `(3)`, `overlay_12_022378C0.s`). With it, the arrow waits return by themselves after 100 frames (`TextPrinter_WaitAutoMode`). That is why battle messages advance without a press: every battle message on these traces read 0, then 2 for 1.67 s, then NULL. It is also why the getter's message states need no press. The evolution scene and field scripts leave it off.

**The evolution scene.** Every message state checks `TextPrinterCheckActive(evo.printer)`: 3, 12, 15, 17, 19, 26, 28, 30, 33, 36 and 37 (15, 17, 19, 26, 28, 30 and 33 share one routine, `_02076620`). In 3 and 37 the slot going NULL moves the state on at once. In 12, 15, 17, 26, 28, 30 and 36 it then counts `evo.timer` down, 40 frames in 12 and 30 in the others, so `evo.state` changes about 1.1 s after the press (1.5 s in 12), not at the press. 19 and 33 print messages without `\r` and pass by themselves.

Observed, one A each, sent within 0.1 s of the printer reaching 2. Where there are two values, the first is the No run and the second the yes run:

| `evo.state` | Message | State start → printer at 2 | Press → slot NULL | Press → next state | Presses |
|---|---|---|---|---|---|
| 3 | 916 "What?" | 0.08 s, 0.10 s | in the read that showed 4 | 0.13 s, 0.10 s (→ 4) | 1 |
| 12 | 918 "Congratulations! …" | **4.09 s, 4.08 s** | 0.09 s, 0.08 s | 1.50 s, 1.48 s | 1 |
| 15 | 1193 "X wants to learn the move Y." | 0.65 s, 0.57 s | 0.12 s, 0.13 s | 1.14 s, 1.15 s | 1 |
| 17 | 1194 "However, X already knows four moves." | 0.73 s, 0.65 s | 0.08 s, 0.10 s | 1.14 s, 1.14 s | 1 |
| 26 | 1199 "1, 2, and... ... Poof!" | 1.75 s (three state-6 pauses) | 0.13 s | 1.18 s | 1 |
| 28 | 1200 "X forgot Y." | 0.55 s | 0.11 s | 1.16 s | 1 |
| 30 | 1201 "And..." | 0.08 s | 0.11 s | 1.17 s | 1 |
| 36 | 1198 "X did not learn the move Y." | 0.65 s | 0.09 s | 1.12 s | 1 |
| 37 | 1202 "X learned Y!" | 1.39 s | in the read that showed 38 | 0.11 s (→ 38) | 1 |

- **"State start" is the first 80 ms read that showed the state**, so each duration can be up to 0.1 s short. The print states before 26, 30 and 37 (25, 29, 31) and 14 were short or skipped between reads, so their text was already drawing when the wait state first read; 30's 0.08 s is mostly 29's.
- **The durations depend on the save's text speed** (`Options_GetTextFrameDelay`). Why 918 takes 4.1 s when the longer 1194 takes 0.7 s was not chased: the printer read 0 throughout, with no pauses.
- **No press was lost and none was repeated.** The driver's 3 s retry never fired on either run. The rest of each run matched the earlier traces: 21 No → 32 → 35 (A) → 36 → 39 → 40, and 21 Yes (125,75) → 22 → 23, list (64,24), confirm (64,168) → 24 → 25 → 26 … 37 → 38 → 39 → 40.

**Field scripts.** `ov01_021EF348` is `IsPrintFinished(env.textPrinterNum)`, and `IsPrintFinished` is `!TextPrinterCheckActive` (`text_0205B4EC.s`): the same array. Observed on `Pokemon HeartGold5` (lead poisoned at 6 HP; walked in the grass, fled two battles): `std_survive_poisoning` started with `env.printer` 0; slot 0 read 0 for 0.93 s, then 2; one A; the slot was NULL in the next read (0.08 s), and the script task was gone and the field free 0.25 s after the press. One box, one arrow, one press. `env + 0x05` is meaningful while `task.func == 0x0203FF45` and `env.check == 0x3643F`. It keeps its last value after the printer is gone, so the slot, not the byte, says whether a message is up.

### Battle menu input-ready gap

`BattleInput_CheckTouch` (`battle_input.c`) returns no hit while `BattleInput.isTouchDisabled` is TRUE. `BattleInput_InitMenuSlideIn` sets it when a sliding menu (the main menu among them) is built. `Task_BattleMenuSlideIn` clears it when the slide ends: `x` starts at -25500 and adds 6375 a frame, so the slide takes 4 frames and the 5th clears the flag. `BattleInput_ChangeMenu` clears it for menus that do not slide. That is about 5 frames (0.08 s) after the menu is built. But `ctx.sel == 1` is the server's side and turns 1 before the player's controller has built the menu, and how long the build takes was not measured. `isTouchDisabled` sits at a decomp-named offset in `BattleInput` (`battle_input.h:200`), whose offsets the decomp marks unreliable, so it was not read live. The text printer does not help here: "What will X do?" is printed before the menu, and its printer is gone by then. So the bot keeps a short settle after `ctx.sel == 1` and confirms each menu press by `ctx.sel` changing.

### What this changes for the bot

- **One press per message in the evolution scene.** In each message state (3, 12, 15, 17, 26, 28, 30, 36, 37) the task reads slot `evo.printer`:
  - non-NULL with `pr.state` 0, 4, 5 or 6: drawing; wait.
  - 2, 3, 7 or 8: press A once.
  - NULL while `evo.state` still reads the same: the timer is counting down; press nothing.

  Confirm a press by the slot going NULL (within 0.15 s on every press here), not by `evo.state` changing, which waits for the timer in all but 3 and 37. Retry only if the printer is still at the arrow 0.5 s after the press.
- **The same for field scripts.** While a context waits in `0x021EF349`, read slot `env.printer` and press once at 2, 3, 7 or 8. A message of several boxes goes back to 0 between boxes and needs one press per arrow. B works at the arrow as well as A, so the Pokémon Center rule (B after the Yes) is unchanged.
- **Battle messages need nothing**: auto-scroll is on.
- **Menu presses keep a short settle** (above) and the confirm-and-retry against `ctx.sel`.
- **One more read for the profile**: the 32 bytes at `0x021D1F74`, and the 0x34-byte printer behind a slot in use. The address is static ARM9, so it is per revision, like `sFieldSysPtr`.

**The saves were not changed.** `Pokemon HeartGold4` and its `.bak` hashed `18e10c7b…4f25c87` before launch and after each `taskkill /F`; `Pokemon HeartGold5` and its `.bak` hashed `b7658103…3745280`. The game was never saved.

## Suspected issues, not confirmed

Spotted while reading the code for this spike. None has been reproduced, and none is planned work; each needs a demonstration before it is treated as a bug.

| Where | Suspected issue |
|---|---|
| `NavigatorEvent.cs:347-354` with `:118-124` and `:224-228` | A route abandoned after three blocked attempts returns `false`, which the callers read as "no battle happened" and treat as arrival: the bot heals or finishes the return without having arrived. |
| `PokemonStateReader.cs:238-245` | Box re-read of a corrupt slot is a `do/while` with no limit; one permanently bad slot hangs the read. |
| `BattleOrchestratorEvent.cs:806-845` and `WaitForMoveEnd` | Battle-start and turn waits have no timeout. |
| `HgssDataReader.cs:15` | `PartySize = +0x23F44` contradicts the pointer service's `-0x23F44`; unused today. |
| anchor `+0x230EC` after a battle | Reads freed memory (stays 14 on the field). |

## Found so far, addresses (HGSS US rev 0)

| Symbol (decomp name) | Address | Notes |
|---|---|---|
| `sFieldSysPtr` | `0x021D4158` | `FieldSystem *`; the bot's "anchor" |
| `sLCRNG_State` | `0x021D15A8` | 32-bit LCRNG state; restored to its pre-battle value when a battle closes |
| `LCRandom` | `0x0201FD44` | static ARM9 |
| field app overlay 1 | `0x021E5900` | shares the address with 23 other overlays, including Pokédex (18) |
| battle overlay 12 | `0x022378C0` | `battle_system.c`, `battle_command.c`, controllers |
| overlay 2 | `0x02245B80` | `encounter_check.c` |
| `generateWildShinyAndAddToParty` | `0x02247910` | used by the QA wild-shiny cheat |
| `generateWildNonShinyAndAddToParty` | `0x02247A18` | |
| grass encounter call | `0x02247D96` | |

Struct offsets are in [the chain](#the-pointer-chain-hgss-us-rev-0-verified) and do not depend on the revision as long as the structs are the same; static addresses (`0x021D4158`, overlay functions) do.

## Approach used

1. **Decomp reading**: the structs, enums and state machines in `pret/pokeheartgold`, with offsets computed from the struct definitions and cross-checked against fields named by offset and against the assembly.
2. **ROM inspection**: the overlay table from the ROM header, and overlays decompressed with the DS bottom-up LZ format to see which code sits at a given address.
3. **Live RAM**: snapshots and an 80 ms trace through ApiHawk's existing read opcode, on the QA install with no cheats loaded and no in-game saving.
4. **PKHeX save copies** for states a normal save cannot reach on demand: a scratch console app using `PKHeX.Core` (the same package the backend uses) replaced the lead in copies of the Route 33 save with a Pokémon one experience point short of a level that offers a move, or of an evolution level. The copies were loaded under spare ROM copies (`Pokemon HeartGold2`, `Pokemon HeartGold3`) in the QA install; the original save was not touched. This is a good pattern for QA saves generally: make a base save by hand once, then derive scenario saves from it with PKHeX.

The Reverse Engineer Anything MCP was not needed for HGSS: the decomp plus RAM was enough. It stays the fallback for code the decomp leaves undecompiled (the post-battle evolution module, `asm/unk_020755E8.s`, whose state byte is inferred at `+0x64`) and for games without a decomp. Tools and their output stay outside the repos; only conclusions are recorded here.

## Next steps

1. **Find the battle UI's input-ready flag** (the remaining gap above), then confirm-and-retry presses against `ctx.sel`.
2. **Exercise the decomp-only values** with more PKHeX save copies: a weak lead that faints (script 6 on our side, 231 "use next Pokémon?"), a lead that accepts the new move (getter 23, 24-30; done 2026-10-23), a Poké Ball in the bag to catch (script 11, outcome 4), a trainer battle, and the field signals against scripts, warps and the start menu.
3. **Map RS** from `pret/pokeruby`: `gMain.inBattle`, `gBattleTypeFlags`, the battle controller state, `gRngValue`, for the RS profile and QA cheats.
4. **RS addresses per revision.** RS is hunted on 1.1 and 1.2, so the RS mapping needs both revisions' static addresses; HGSS has only 1.0.
5. **Feed into plan 5.3**: the game profile holds this chain; a small HGSS state reader classifies "where the game is" and the battle and navigation events use it.
6. **Field scripts** (done 2026-10-22 for poison and the Pokémon Center): the egg receive and hatch and the HGSS starter selection are the remaining scripted screens; each is one PKHeX copy and one trace with the same probe.

## Risks
| Risk | Mitigation |
|---|---|
| Static addresses differ by revision | Struct offsets are stable; only `sFieldSysPtr` and overlay addresses need a per-revision table. A profile with no table for the launched revision refuses the launch (decided 2026-10-17). |
| Transient server commands are skipped between reads | Key decisions on stable waiting states (5/1, 22 + script, getter prompts, 45), not on transient commands. |
| Reading battle structures after they are freed | Read `BS` and `ctx` only while `app.state` is 4..9; past that, use `app.state` and `app.setup` only. |
| Torn multi-hop reads | Read the chain in one frame through ApiHawk's pointer-chain opcode (`0x03`), as the party read already does. |

## Appendix: every read the bot makes

Grouped by phase, from the code inventory (`file:line` relative to `ShinyBotHunter/`).

- **Encounter detection**: `0x021E76D2` byte, `RunEncounterEvent.cs:46`, `SpinEncounterEvent.cs:45`, `NavigatorEvent.cs:697`, `BattleOrchestratorEvent.cs:539`, `HgssDataReader.cs:42`, plus two deprecated copies. No timeout.
- **Battle start**: enemy count `[A+0x6930]+0xC14` and data `+0xC18` (`BattleOrchestratorEvent.cs:806-828`), split reads, no timeout; menu `A+0x230EC == 1` with "tap B until" (`:834-845`), no timeout, then `W.5` (TODO).
- **Battle state**: active and enemy battlers at `A+0x25E2C`/`A+0x25EEC`, battle party at `A+0x2AAB0` (`HgssPointerService`), matched to the field party by species and PID.
- **Turn end**: three racing polls in `WaitForMoveEnd` (`:558-648`), no timeout.
- **Faint and switch**: our HP 0 goes to Waiting ("TODO implement losing"); switch confirmed by PID within 4 s, 3 attempts.
- **Level-up, move learn, evolution**: `A+0x692C == 14`; party level comparison by index; blind macros with large fixed waits because "reads during that menu crash".
- **Flee**: Run spammed with `W.3` while the battle byte is set (`WildPokemonEncounterEvent.cs:177-188`), no limit.
- **Navigation**: map, X, Y, facing; warp wait with no timeout and a target map read just before the wait (`NavigatorEvent.cs:363-372`); stall = position unchanged 2.5 s; poison/faint detection by party diffs.
- **Party and boxes**: validated party read with 120-frame retry; box corrupt-slot re-read with no limit.

## Changelog
- 2026-10-24: text printer state mapped and read live: the static task array `sTextPrinterTasks` at `0x021D1F74` (8 slots, printer at `[slot] + 0x10`, `state` at `+0x28`), the arrow-wait values 2, 3, 7, 8, `evo.printer` and the field script's `env + 0x05` indexing it; one A per message on the evolution scene (No and yes paths, `Pokemon HeartGold4`) and the poison message (`Pokemon HeartGold5`) with per-state text durations; battle messages shown to auto-scroll; the battle menu's `isTouchDisabled` slide-in gap described from the decomp.
- 2026-10-23: in-battle move replace (yes path) and the free-slot paths traced on `Pokemon HeartGold2`, `9` and `10`: the getter's full state table with what each waits for (only the stat boxes and the Yes/No menus wait; messages advance by themselves), `getter.move`/`getter.slot` and `ctx.buffer` (`ctx + 0x2300`), the two-button menu rects, and the forget-move screen (overlay 8, assembly only, no pointer from the chain) with its touch hitboxes; getter 23 and 25-36 and the evolution free-slot entry 12 → 37 → 38 now observed.
- 2026-10-23: forget-move list (yes path) mapped and traced live: `evo.summary` OverlayManager (`+0x14` proc state, `+0x1C` data, cursor at data `+0x7BD`), the summary app's mode-2 states 8 (list) and 9 (confirm) with their touch hitboxes and keys; evolution states 22-31 and 37-38 now observed; 31 goes to 37.
- 2026-10-23: post-evolution move learning mapped: the evolution scene's own state machine at `[app.setup + 0x198] + 0x64` (undecompiled `unk_020755E8.s`), its Yes/No menu and the summary-app hand-off; traced live through the evolution, "forget a move?" No and "give up?" Yes on the Totodile save; `+0x67` confirmed as the done byte.
- 2026-10-22: field script chain (task manager → script environment → script contexts → native) mapped and verified live through the poison message and the Pokémon Center heal on four PKHeX copies; the heal macro's fainted rule shown wrong for a non-lead faint; Pokérus dialogue reproduced.
- 2026-10-17: first draft.
- 2026-10-20: level-up, move-learn and evolution states observed on PKHeX save copies; every value now marked observed or decomp only.
- 2026-10-20: HGSS battle and field state mapped from the decomp and verified live: the `FieldSystem` chain, battle app states, server commands, menu states, scripts, experience and move-learn prompts, outcome; what each bot constant really is; replacement table; bugs found; input-ready gap.
