# Headbutt as a wild encounter mode (HGSS)

Status: spike done, 2026-10-28. Nothing in this doc is implemented in the bot; the design that implements it is the [headbutt encounter spec](../specs/headbutt-encounter.md) (plan phase 9).
Related: [pointer discovery](pointer-discovery.md) (the field script chain this reads), [HGSS state reader spec](../specs/hgss-state-reader.md), the wild encounter guide page [01-wild-encounter](../guide/04-hunts/01-wild-encounter.md)

## What was proved

On the owner's `HG-Headbutt` save (HeartGold rev 0, Azalea Town, map 74, tile (426, 464) facing Down, lead Slowpoke Lv 25 knowing Headbutt and following), a probe drove twenty headbutts in a row with nothing but memory reads and ApiHawk macros: A at the tree, Yes at the menu, B through the text, read the foe when the battle menu came up, Run, and back to the tree. Every one of the twenty gave a battle. Each cycle took 28.5 to 31.9 s. The save file was compared byte for byte after both runs and never changed.

| Measure | Result |
|---|---|
| Headbutts that started a battle | 20 of 20 (and 3 of 3 in the first run) |
| Script | `std_field_headbutt` (10014) every time |
| A press to battle start | 9.5 to 11.7 s, mean 10.4 s |
| Whole cycle (A, battle, flee, back) | 28.5 to 31.9 s, mean 30 s |
| Foes | Spearow 10, Heracross 6, Aipom 4, levels 3 to 5; all PIDs distinct |
| Foe OT ids | the player's own, 15613 / 35374, so the existing shiny rule applies unchanged |
| Tile after each battle | (426, 463) facing Up, all 20 times |

Two more setups were run the same day, five cycles each, with no in-game save:

| Setup | What happened | Tile after each battle |
|---|---|---|
| Follower beside the player: step left off the start tile and back, so the follower stands on (425, 464), then face the tree | the swap put the player on the follower's tile, at a right angle to the tree, every time | (425, 464) facing Left, 5 of 5 |
| Headbutt known by a party member that is not following: a PKHeX copy of the save (`HG-Headbutt-CutIn`) with Typhlosion leading and Slowpoke second | the cut-in branch ran; no swap | the start tile facing the tree, 5 of 5 |

All 33 headbutts across the three runs gave a battle, every foe carried the player's OT ids, and both saves were byte for byte unchanged afterwards.

A fourth run faced the tree to the left, from (425, 464) facing Down at (425, 465), the owner's "tree with no Pokémon". Three headbutts: script 10014 ran in full each time (the question, Yes, "Slowpoke used Headbutt.", the swap, which put the player on (426, 464) facing Right because the follower stood east after the step west), and then the field came back free with no battle, 8.7 to 9.8 s after the A. Nothing stalled or failed. The tile (425, 465) is not in Azalea's tree list, so `Headbutt_GetTreeTypeFromTable` returns "none" for every trainer ID; a listed tree that is "none" for the player's trainer ID behaves the same way. The cached return macro (step left, press Down) put the player back in front of that tree after each try.

## The script, in the order the bot sees it

From `files/fielddata/script/scr_seq/scr_seq_0146.s` (`scr_seq_0146_014`, which is `std_field_headbutt` = 10014 in `include/constants/std_script.h`), observed live through the field script chain (`env.script`, the innermost context's native):

| Step | Script | Observed native | What the bot does |
|---|---|---|---|
| A facing the tree | starts 10014 | `PRINTING` (0x021EF349) | nothing yet, or A at the text arrow |
| "It's a moderately sized tree. Would you like to use Headbutt?" with Yes/No | `NPCMsg`, `TouchscreenMenuHide`, `GetMenuChoice` | `TOUCHMENU_HIDE` (0x020476E9), then `MENU_CHOICE` (0x020477C1) | one A at the menu native (Yes is the default) |
| "Slowpoke used Headbutt." | `NPCMsg msg_0211_00033`, `CloseMsg` | `PRINTING` | B at the arrow (after Yes the navigator's rule is B, which cannot start a second conversation) |
| The swap and the tree shake | the follower branch (see below), `Wait` loops | 0x020492F1, 0x020408D9 (both decomp-unnamed; seen in 17 and 19 of 20 cycles because the probe polls every 80 ms), field tasks 0x0224E0ED and 0x022507E9 | nothing |
| `TryHeadbuttEncounter` | `FieldSystem_TryHeadbuttEncounter` (`src/field/headbutt.c:106`) | field tasks 0x020551B9 (encounter start), 0x02055245 (leaving the field), then the battle app | wait for the battle |

These are the same natives the navigator's `PressThroughScriptAsync` already handles for the nurse and the poison message, with the same rule: A once at the menu, B at every later text arrow, nothing at any other native. Three or four presses per cycle.

## The swap with the following Pokémon

When the party member that knows Headbutt is the one following the player (`scr_seq_0146_014`'s branch `_0B4A` to `_0B67`: the slot with Headbutt equals the follower's party index and the player is walking), the follower does the headbutt, and the player and the follower swap tiles: the follower ends on the player's tile facing the tree, the player ends on the follower's old tile. The follower can be on any side the player last walked in from, so the player ends on any of the three tiles beside the start tile that are not the tree. Both cases were observed: with the follower behind, the player ended one tile north facing Up (20 of 20); with the follower beside, one tile west facing Left (5 of 5). For a given setup the tile and facing were the same every cycle, because after the flee the follower settles back on the same side.

When Headbutt is known by a party member that is not following (`PLAYER_STATE_WALKING` and a different slot), or the player is cycling or surfing (`PLAYER_STATE_CYCLING` 1, `PLAYER_STATE_SURFING` 2), the script plays the field-move cut-in instead. Observed on the party-swapped copy: the cut-in waits in native 0x0204378D, which is `sub_0204378C` (`src/scrcmd_c.c:2312`, set up by `ScrCmd_183`) with the Thumb bit, where the follower branch showed 0x020492F1. Nothing moved the player in 5 of 5 cycles.

The routine that performs the swap and the shake (`ScrCmd_560` case 4, `ov02_0224E074`) is still assembly in the decomp, so the swap is known from observation and the owner's description, not from code.

## Which trees give encounters

From `src/field/headbutt.c` and `files/arc/headbutt.json`:

- Each map has a list of trees and three tables (common, rare, secret). A tree's type is fixed by the trainer ID's last digit and the tree's position in the list (`Headbutt_GetTreeType_Regular`); for maps with five or more trees the lookup uses the tree index modulo 5.
- A tree whose type is "none" for the player's trainer ID never gives an encounter: the script shows the same messages and the swap, then returns to the field.
- On a common, rare or secret tree the encounter is certain unless a Repel suppresses it (`FieldSystem_GenerateRegularEncounter` with `ENCOUNTER_TYPE_HEADBUTT`).
- The spike's tree is tree 0 of Azalea's fifteen, at (426, 465) and (427, 465). Trainer ID 15613 ends in 3, so `sRareTreeLUT_5Plus[3][0]` makes it a common tree: Spearow (slot weight 50 %), Heracross (two slots, 30 %), Aipom (three slots, 20 %) at levels 3 to 5. Twenty cycles gave 10, 6 and 4.

The bot does not need the table to hunt: a tree that gives no encounter is visible in one cycle as the script ending in the field with no battle.

Headbutt Pokémon can be shiny at the normal rate. The encounter goes through `FieldSystem_GenerateRegularEncounter`, the same path as grass, which builds the Pokémon with `generateWildNonShinyAndAddToParty` (`src/field/encounter_check.c:926`). Despite the name, that function only means "not forced shiny": it creates the Pokémon with a random PID (`CreateMonWithNature`) and the player's OT id, while the forced-shiny generator, `generateWildShinyAndAddToParty`, is used only by `FieldSystem_GenerateSingleWildPokemon` for scripted encounters. The slot roll is `EncounterSlot_WildMonSlotRoll_Headbutt`: 50, 15, 15, 10, 5 and 5 percent.

## What a bot needs

A third value for the existing `EncounterType` (`Spin`, `Run`, now `Headbutt`) on the HGSS Wild Encounter and Chained Encounter actions, dispatched by `OrchestratorEvent` to a new encounter event beside `SpinEncounterEvent` and `RunEncounterEvent`. Everything after the battle starts (foe read, persistence, the target check, flee or fight, healing, Resume) is the existing battle path, unchanged.

1. **Start state.** When the event starts, record the start tile (the orchestrator already does) and the start facing (new), and check that A on that facing starts script 10014 within a few seconds. If it does not, stop with "not facing a headbutt tree". Also check that a party member knows Headbutt, from the party read the bot already makes.
2. **Headbutt.** At the start tile and facing, press A, then press through script 10014 with the navigator's rules (one A at the menu native, B at every later text arrow, nothing elsewhere), until the screen leaves the field (a battle: push `BattleStart`) or the field is free again. Field free again with no battle means the tree gives nothing for this trainer ID, or a Repel is active: stop and say so, rather than pressing A forever. Observed on a tree outside the map's list: the whole script, including the swap, runs and ends in the field about 9 s after the A, so the stop is an ordinary result of the press loop, not a timeout or an exception. The swap still happens on such a tree, so the return step runs before the stop if the event is to leave the player where it started.
3. **Back to the tree.** After the battle, read the tile and facing. On the start tile facing the tree (the cut-in branch) there is nothing to do. Otherwise the player is on the follower's old tile: go back to the exact start tile, from whichever side the swap left the player, then turn to the start facing. The spike's macro is the move-until-position primitive the run encounter already uses, one step on the axis that differs (`M.Right.426.<TrainerX>` from the side, `M.Down.464.<TrainerY>` from behind), then a short press toward the tree, which the tree blocks so it only turns (`H.Down.3`). Because the swap lands on the same tile with the same facing every cycle for a given setup (20 of 20 behind, 5 of 5 beside), the macro is built once, from the first cycle's post-battle tile and facing, and cached, as `SpinEncounterEvent` caches its spin macros. After running the cached macro, read the tile and facing once more: if they are not the start tile facing the tree, rebuild the macro from where the player actually is and log that the cache missed, instead of pressing A at whatever is in front. The spike ran this rule for every cycle of the sideways run: built on cycle 1, no miss in 5.
4. **Waits.** Every wait above goes through `GameStateWait` with a stale-timeout, like the rest of the HGSS events: the script's start after A, the press loop, the battle start, the field free after the flee.

The one-step return is a short macro (turn and step), not a pathfinding job; the navigator's `ReturnToStart` is the fallback if the cached macro misses twice.

## Open points

- A tree in the map's list that is "none" for the player's trainer ID was not tried (on Azalea with trainer ID ending in 3, row 3 of `sRareTreeLUT_5Plus` reads common, rare, rare, none, common, so trees 3, 8 and 13 are "none", trees 1, 2, 6, 7, 11 and 12 rare, and trees 0, 4, 5, 9, 10 and 14 common). The decomp sends it down the same path as the unlisted tree that was tried.
- The cycling and surfing branches were not run; the decomp sends them to the same cut-in as the non-follower case.
- Natives 0x020492F1 and 0x020408D9 and tasks 0x0224E0ED and 0x022507E9 are named here by when they appear, not from symbols; 0x0204378D is named from the decomp. The bot does not branch on any of them.
- A Repel would make a common tree look like a "none" tree; the stop message should name both.

## Spike artefacts

In the session scratchpad under `headbutt\`: `hb_probe.py` (the probe: backend launch, chain trace, press loop, foe decryption, flee, the cached return macro and its check, the sideways setup, per-cycle CSV), `sav4\` (PKHeX.Core reader for the save's party, moves and position, and the party swap that made the cut-in copy), `saves\` (the original save's backup and the cut-in copy), and `qa-run\` with every run's `probe.log`, `backend.log`, `cycles.csv` and `run.mp4`. The cut-in copy is also installed as `HG-Headbutt-CutIn.nds` in the ROM folder and `HG-Headbutt-CutIn.SaveRAM` in BizHawk's SaveRAM folder. Nothing was committed from there.
