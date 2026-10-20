# Research Spike: Better Pointers From the ROMs

Status: Draft
Last updated: 2026-10-17
Related: [Code review 2026-10](../reviews/2026-10-code-review.md), [QA automation](../specs/qa-automation.md), [System architecture](../architecture/system-overview.md)

## Problem

Several places in the bot read memory and compare against values that were observed rather than derived. They work, but nobody can say what the value means or when it would change:

| Where | What it assumes |
|---|---|
| `BattleOrchestratorEvent.cs:539` | a byte equal to `0x41`, `0x97` or `0xC0` means "in battle" (copied into four other files) |
| `BattleOrchestratorEvent.cs:505` | `GetBattleState() == 14` means "evolution prompt" |
| `HgssDataReader`, `HgssPointerService`, `PathfindingService:256-261`, `RunEncounterEvent:370`, `HgssTogepiEggEvent:53` | fixed HGSS RAM addresses and pointer offsets found by memory search |
| `RsDataReader` | fixed Gen 3 addresses for party and battle |
| `NavigatorEvent` plausibility checks | retries because a torn or wrong read is possible |

The QA spec needs two more that do not exist yet: the RNG state address per game and the exact frame window in which a starter's PID is generated.

## Approach

Two sources, used together.

### 1. Decompilation projects (free, exact, do first)
The `pret` decompilations are complete for every game in scope and produce symbol maps with every RAM variable and function address:

| Game | Project | What it gives |
|---|---|---|
| Ruby/Sapphire | `pret/pokeruby` | `gRngValue`, `gPlayerParty`, `gEnemyParty`, `gBattleMons`, `gBattleTypeFlags`, `gMain.inBattle`, `gSaveBlock1/2` layouts, `sav` checksums |
| Emerald, FireRed/LeafGreen | `pret/pokeemerald`, `pret/pokefirered` | same names, different addresses; one pointer service per game, one parser per generation |
| HeartGold/SoulSilver | `pret/pokeheartgold` | overlay layouts and the structs behind the pointer chains in `HgssPointerService` (the "anchor" at `0x21D4158` is the save-data pointer; the offsets are struct fields with names) |
| Platinum | `pret/pokeplatinum` | the Gen 4 sibling, for later |

Building the project (or just reading its `.sym`/`.map` output and headers) replaces every observed constant with a named field, and documents what the battle-state bytes actually are (very likely `gBattleTypeFlags` bits or a battle controller state enum in Gen 3; a battle system state in the HGSS battle overlay). This is the highest-value, lowest-risk half of the spike.

### 2. Reverse Engineer Anything MCP on the ROM (for what the decomp does not settle)
The tool is the "Reverse Engineer Anything" MCP server: it disassembles and decompiles a binary and lets a Claude session query it (functions, cross-references, decompiled routines, constant searches). Used here on the GBA ROM and the DS ARM9 and overlay binaries, with the decomp's symbols loaded so the output is named rather than raw addresses. It runs outside both repos; its project files and output are not committed, only the conclusions recorded in this doc.

What it is for:
- Confirming the semantics of a value the decomp names but the bot uses differently (e.g. which exact state the "evolution prompt" is, so `== 14` becomes a named enum).
- The HGSS overlays: the DS loads code overlays dynamically, so a RAM address is only valid while that overlay is loaded. The disassembler shows which overlay owns a routine and so when a pointer chain is safe to follow. This is probably the real reason `NavigatorEvent` needs plausibility checks.
- Finding where the game writes the PID of a generated Pokemon (cross-references to the RNG routine from the wild/starter generation code), which gives the frame window for RNG seed injection.
- Any game not yet decompiled.

How it would be used, concretely: load the ROM in the disassembler with the decomp's symbol file imported, then from a Claude session ask for cross-references to `Random()` from the starter-selection script, decompile the generation routine, and record the write to `gRngValue` and the PID computation. The output is a note in this doc per question, with the function name, address, and the derived rule.

## Deliverables of the spike
1. A table per game flavor: every address or constant the bot uses today → its symbol name, type, and meaning, or "no symbol, here is the derivation".
2. A list of the reads that are unsafe depending on overlay state (HGSS), with the condition that makes them safe.
3. `gRngValue` (and Gen 4 equivalent) address and the PID-generation frame window per game, for the QA injector.
4. Recommendation per constant: replace with named symbol in the game profile (plan 5.3), or keep with a comment that explains it.

No code changes in the spike itself. Its output feeds plan task 5.3 (game profile) and the QA spec.

## Risks
| Risk | Mitigation |
|---|---|
| Decomp symbol addresses differ by ROM revision | `RomInfoService` already identifies revision; the profile keys addresses by revision where they differ. Current hunts on 1.1/1.2; other revisions only when needed. |
| HGSS overlay behaviour makes some reads inherently racy | Document the safe window; prefer frame-coherent reads through ApiHawk (already the pattern). |
| Time sink | Scope to the constants in the table above plus the two QA addresses. Anything else is a new spike. |

## Open questions
Answered 2026-10-17: the ROMs in use today are revision 1.1 or 1.2 (the app reports the exact one through `RomInfo.Revision`). The spike covers that revision only. The profile keys addresses by revision so another revision (a Jirachi hunt would likely need 1.0) can be added later without restructuring, but adding one is a separate decision when a hunt calls for it.

## Changelog
- 2026-10-17: first draft.
