# Research Spike: Better Pointers From the ROMs

Status: HGSS battle and field state mapped and verified; RS not started
Last updated: 2026-10-20
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
- Decomp only: 23 (choosing the move to forget) and the "learned" path 24-30, because the trace answered no.

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
| `0x021EF349` | `ov01_021EF348` (from `ScrCmd_NPCMsg`) | a message is printing, **or finished and waiting at the ▼ arrow**; the text printer's own `state` (2 or 3) distinguishes those and was not read | observed in every message |
| `0x02041075` | `sub_02041074` (`ScrCmd_WaitButton`) | waiting for A or B | decomp only: every message observed used `\r`, which waits inside the printer |
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

Not observed: a bare `WaitButton` native; every message seen waits inside the text printer. The text printer's `state` byte (2 and 3 are "waiting at the arrow", `render_text.c`) would separate "still drawing" from "waiting for a press", but the printer lives in a static array whose address is not in the decomp tree; pressing while drawing is harmless, so it was not chased.

Saves used (PKHeX copies of the Route 33 save, made with the spike's `savetool edit`): `Pokemon HeartGold5` lead poisoned at 6 HP; `HeartGold6` lead at 0 HP; `HeartGold7` lead at 15 HP and slot 2 at 0 HP; `HeartGold8` lead at 15 HP with Pokérus strain 1 and event flag `0x65` cleared. PKHeX's Gen 4 event-flag index matched the decomp's `FLAG_WAS_TOLD_ABOUT_POKERUS` (the flag read true on a save whose party carries cured Pokérus, and clearing it brought the dialogue back).

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
2. **Exercise the decomp-only values** with more PKHeX save copies: a weak lead that faints (script 6 on our side, 231 "use next Pokémon?"), a lead that accepts the new move (getter 23, 24-30), a Poké Ball in the bag to catch (script 11, outcome 4), a trainer battle, and the field signals against scripts, warps and the start menu.
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
- 2026-10-22: field script chain (task manager → script environment → script contexts → native) mapped and verified live through the poison message and the Pokémon Center heal on four PKHeX copies; the heal macro's fainted rule shown wrong for a non-lead faint; Pokérus dialogue reproduced.
- 2026-10-17: first draft.
- 2026-10-20: level-up, move-learn and evolution states observed on PKHeX save copies; every value now marked observed or decomp only.
- 2026-10-20: HGSS battle and field state mapped from the decomp and verified live: the `FieldSystem` chain, battle app states, server commands, menu states, scripts, experience and move-learn prompts, outcome; what each bot constant really is; replacement table; bugs found; input-ready gap.
