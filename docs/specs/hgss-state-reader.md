# Spec: HGSS game-state reader (follow-up to the game profile)

Status: Draft, blocked on the [game profile spec](game-profile.md) P1 to P7 and on the QA scenarios in the [gate](#qa-gate)
Last updated: 2026-10-21
Owner: Phil
Related: [game profile spec](game-profile.md), [pointer discovery](../research/pointer-discovery.md) (the source for every value here), [QA automation](qa-automation.md), [decisions](../decisions/game-profile.md), plan rows [5.3e and 6.2](../plans/2026-10-cleanup-and-hardening.md)

## Summary

After the game profile lands, every HGSS event asks `IGameStateReader` where the game is, and the one implementation answers from the three bytes the bot has always read: an overlay code byte at `0x021E76D2` that happens to be set during wild battles, and two offsets into a heap block that happen to hold the menu state and the battle application state. They work, and they also read garbage during overlay swaps, read freed memory after a battle, and cannot tell a script from a wall or a prompt from an animation, which is why the bot pads them with fixed waits and unbounded loops.

This spec replaces that implementation with one that walks the pointer chain the spike verified in live RAM: from the static `sFieldSysPtr` through the running sub-app, the battle system and the battle context, to the exact fields the game itself branches on. It lists, read by read, what the bot reads today, what replaces it, whether the replacement was observed live or only read in the decomp, and which QA scenario proves it. It also sets the rule that keeps this table true as features are added: every read the bot makes is a row here and a commented member of the address table, or it does not merge.

The change is one new class, one changed line in `HgssProfile`, and the wait loops rewritten against the new states. It is gated on QA because it is the one change in the cleanup that alters what the bot does in the game.

## Goals

- G1: every read in the [read table](#read-table) is replaced by the named chain field, or is explicitly kept with a reason.
- G2: every wait on game state ends in one of three ways: the awaited state, cancellation, or a stale-timeout that names what it waited for and what it saw.
- G3: every press that expects a state change is confirmed by the next snapshot and retried a bounded number of times.
- G4: a value is used for a decision only after it has been observed in a QA scenario; decomp-only values are mapped but not branched on.
- G5: the table of reads stays complete as features are added, by review rule, not by hope.

## Non-goals

- Any ApiHawk change. The reader uses plain reads only; see [Why not a single-frame read](#why-not-a-single-frame-read).
- RS. Its chain is unmapped (plan row 6.1).
- Finding the battle UI's input-ready flag. The gap is covered by confirm-and-retry; if the flag is found later it is one more enum value.
- Reading values the gate cannot observe (lost, draw, caught, a faint on our side, "use next Pokémon?"). They are mapped, documented as decomp-only, and left for the scenario that first reaches them.
- Fixing the suspected issues from the spike that a wait-loop rewrite does not touch.
- The evolution task's "done" byte (`+0x67`, inferred and shown wrong): evolution end is detected by the app closing, not by that byte.

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | `HgssFieldSystemStateReader` implements `IGameStateReader` and returns every `GameScreen` value per the [state table](#state-table). | Must | Table-driven unit test over scripted memory, one case per row of the state table plus the out-of-range and changed-pointer cases. |
| FR-2 | A snapshot is consistent with itself: the reader re-reads its roots and returns `Transitioning` when they moved between reads. | Must | Unit test: a scripted client that changes `app` between reads yields `Transitioning`, not a mixed snapshot. |
| FR-3 | A pointer outside DS main RAM (`0x02000000` to `0x02400000`) makes the snapshot `Transitioning`; nothing throws on bad memory. | Must | Unit test with a zero and an out-of-range pointer at each hop. |
| FR-4 | Every row of the [read table](#read-table) marked "replace" has no remaining caller of the old read after S2; rows marked "keep" are unchanged. | Must | FR-2 grep of the profile spec plus `grep -rn "BattleIndicator\|BattleMenuStateOffset\|BattleStateOffset\|EnemySetupOffset" ShinyBotHunter/` is empty after S2. |
| FR-5 | Every memory-polling wait in an HGSS event goes through `GameStateWait.UntilAsync` with a stale-timeout, listed in the [timeout table](#stale-timeouts). | Must | No `while (true)` or `while (!ct.IsCancellationRequested)` around a memory read remains in `BotEvents/` without a stale-timeout. |
| FR-6 | Run and move selection in battle use `GameStateWait.PressUntilAsync`; the `W.5` after the menu read and its TODO are gone. | Must | QA: `hgss-route33` flees on the first or second press in every encounter of the run. |
| FR-7 | Level-up and move-learn handling presses at the prompt states (11/13, 21, 23, 34) instead of fixed frame waits (`LevelUpFlow.cs:17-49`). | Must | QA: `hgss-route33-train` learns or declines the move with no padding macro. |
| FR-8 | Decomp-only values are mapped in the reader and listed in the [decomp-only table](#decomp-only-values); no event branches on them until the table marks them observed. | Must | Reviewer checklist; the table is updated in the same PR as any branch. |
| FR-9 | Every read the bot makes on HGSS, including any added by a later feature, is a row in the [read table](#read-table) with symbol, kind, status and scenario, and a commented member of `HgssPointerService`. | Must | Reviewer checklist item on every PR that touches `IMemoryClient` calls or `HgssPointerService`. |
| NFR-1 | One snapshot is at most five memory requests and under 50 ms on localhost. | Must | Measured and logged by the `hgss-route33` run. A miss stops S1 and is reported to the owner; no fallback is built unasked. |
| NFR-2 | The change to select the reader is one line in `HgssProfile`, and reverting it restores the legacy reader. | Must | S2's diff shows the line. |

## Design

### The chain as data

Members added to `HgssPointerService` in S1 (kind per the profile spec's FR-9; all offsets are revision-independent, the one root is revision 0's):

| Member | Value | Kind | Decomp symbol / meaning |
|---|---|---|---|
| `FieldSystem` | `0x021D4158` | root | `sFieldSysPtr`; already present as the "anchor" |
| `AppSlotsOffset` | `FS + 0x00` | offset | `sub0`, the app slots block |
| `TaskManagerOffset` | `FS + 0x10` | offset | top field task; NULL when none runs |
| `MapReadyOffset` | `FS + 0x6C` | offset | field map up |
| `MapAppOffset` | `sub0 + 0x00` | offset | field map app; NULL while a sub-app runs |
| `SubAppOffset` | `sub0 + 0x04` | offset | launched sub-app; NULL on the field |
| `PausedOffset` | `sub0 + 0x08` | offset | |
| `TaskFunctionOffset` | `taskman + 0x04` | offset | task function; `+0x08` its state |
| `AppOverlayIdOffset` | `app + 0x0C` | offset | 12 = battle |
| `AppStateOffset` | `app + 0x14` | offset | `battle_022378C0.c:25-42` |
| `AppSetupOffset` | `app + 0x18` | offset | `BattleSetup`; replaces the heap coincidence at `FS + 0x6930` |
| `BattleSystemOffset` | `app + 0x1C` | offset | valid while app state 4..9 |
| `BattleContextOffset` | `BS + 0x30` | offset | |
| `MenuStateOffset` | `ctx + 0x00` (u8) | offset | `battle_controller_player.c:243-262` |
| `ServerCommandOffset` | `ctx + 0x08` | offset | `constants/battle.h:594-642` |
| `ScriptNarcOffset`, `ScriptFileOffset` | `ctx + 0xAC`, `ctx + 0xB0` | offset | while command is 22 |
| `GetterOffset` | `ctx + 0x178` | offset | exp / level-up / move-learn work; NULL when idle |
| `GetterStateOffset` | `getter + 0x28` | offset | `battle_command.c:5871-5911` |
| `OutcomeOffset` | `BS + 0x2420` (u8) | offset | 1 won, 2 lost, 3 draw, 4 caught, 5 fled; bits `0x40`/`0x80` flee in progress |
| `BattleOverlayId` | 12 | constant | |

Deleted in S2: `BattleIndicator`, `BattleMenuStateOffset`, `BattleStateOffset`, `EnemySetupOffset`.

### State table

What `HgssFieldSystemStateReader` returns, from which fields, and its status in the research doc.

| `GameScreen` | Condition | Qualifier fields | Status |
|---|---|---|---|
| `Transitioning` | any pointer out of range, or roots changed between reads (FR-2, FR-3) | | by construction |
| `FieldFree` | `app == 0 && taskman == 0 && mapReady == 1 && paused == 0` | | observed around battles; scripts, menus, warps not yet |
| `FieldTask` | `app == 0 && taskman != 0 && mapReady == 1` | `TaskFunction = [taskman + 4]` | observed: encounter start `020551B9`, leaving `02055245`, wild encounter `02050661`, returning `0205528D`, `020552D5` |
| `OtherApp` | `app != 0 && ovyId != 12` | | decomp only |
| `BattleStarting` | `ovyId == 12 && state < 9` | | observed (3, 8) |
| `Evolution` | `ovyId == 12 && state ∈ {13, 14}` | | 14 observed |
| `BattleOver` | `ovyId == 12 && (state > 9 && state ∉ {13,14})`, or `state == 9 && command == 45` | `Outcome` from `[BS + 0x2420]` | 45 observed; outcome 1 and 5 observed |
| `BattleScript` | `state == 9 && command == 22` | `Script = [ctx + 0xB0]` | observed: 0, 1, 3, 4, 6, 276 and twelve unnamed |
| `BattleMenu` | `state == 9 && command == 5 && sel == 1` | | observed |
| `BattleMoveList` | `state == 9 && command == 5 && sel == 4` | | observed |
| `BattleMessage` | `state == 9 && command == 5 && sel == 15` | | decomp only |
| `BattleExpPrompt` | `state == 9 && getter != 0 && getterState ∈ {11, 13}` | `Prompt = getterState` | observed |
| `BattleMoveLearnPrompt` | `state == 9 && getter != 0 && getterState ∈ {21, 23, 34}` | `Prompt = getterState` | 21 and 34 observed; 23 decomp only |
| `BattleTurn` | `state == 9`, none of the above | | observed (commands 8, 23, 28, 34, 39, 40; sel 14) |

Precedence inside `state == 9`: command 45, then command 22, then getter prompts, then command 5 with `sel`, then `BattleTurn`. The getter is checked before the menu because the stat boxes appear while the command is not 5.

### The walk

Five plain `ReadBytesAsync` calls at most, no exceptions on the happy path, no ApiHawk change:

1. `FS = [0x021D4158]` and `sub0 = [FS]` are read on the first `ReadAsync` and cached for the reader's lifetime (the research doc shows them stable for the session). If three consecutive snapshots are `Transitioning`, both are re-read; this is the only recovery path.
2. One read of 0x70 bytes at `FS` (taskman at `+0x10`, mapReady at `+0x6C`) and one of 12 bytes at `sub0` (mapApp, app, paused). With `app == 0` the field rows above decide, with one more read of 8 bytes at `taskman` for `FieldTask`.
3. One read of 0x20 bytes at `app` (ovyId, state, setup, BS). `OtherApp`, `BattleStarting`, `Evolution`, and the `state > 9` form of `BattleOver` decide here; `BattleOver` reads one byte at `BS + 0x2420` if `BS` is in range.
4. With `state == 9`: `ctx = [BS + 0x30]` (4 bytes), then one read of 0x180 bytes at `ctx` (sel, command, script, getter). The getter state is one more 4-byte read when `getter != 0`.
5. The 12 bytes at `sub0` and, when `app != 0`, the 4 bytes at `app + 0x14` are read again. If either differs from steps 2 and 3, the snapshot is `Transitioning`.

Reads 2 and 5 are the cost floor (two requests); a battle menu snapshot is five (FS block, sub0, app, ctx, re-check) plus the ctx pointer, which is six. If the measurement in NFR-1 misses, the ctx pointer read folds into the app block by reading `[BS + 0x30]` through the existing pointer-chain opcode with two hops, which is within today's four-hop limit and still no ApiHawk change.

### Why not a single-frame read

ApiHawk's pointer-chain opcode allows four hops; the battle context is five from the root and the getter six. Raising the limit or adding a multi-block opcode is an ApiHawk change the project rule reserves for tasks that say so, and it would not give consistency on its own, since a snapshot needs the field roots and the battle context together. The re-read in step 5 gives the same guarantee for one extra small read. If NFR-1 is still missed after the fold-in above, S1 stops and reports the numbers; whether to add an opcode is the owner's decision.

### Read table

Every read the bot makes on HGSS today, from the research doc's appendix and the 2026-10-21 survey of `feature/cleanup-phases-3-5`, with its fate. "Replace" rows lose their old read in S2; "keep" rows are unchanged by this spec. Status is the replacement's, from the research doc.

| # | Read today | Where (after the profile spec) | What it really is | Replacement | Status | Proven by |
|---|---|---|---|---|---|---|
| R1 | byte `0x021E76D2 ∈ {0x41, 0x97, 0xC0}` = "in a wild battle" | `HgssLegacyStateReader`; polled by `RunEncounterEvent`, `SpinEncounterEvent`, `NavigatorEvent.WaitForBattleAsync`, `BattleOrchestratorEvent.IsBattle` | Pokédex overlay 18 code that happens to be loaded; reads garbage during the overlay swap | `app != 0 && app.ovyId == 12` → `Screen` is any `Battle*` or `Evolution` | observed | `hgss-route33` |
| R2 | same byte leaving the set = "battle over" | `WaitForMoveEnd.PollBattleEnded`, `LevelUpFlow.WaitForLevelUpOrField` | as R1 | `Screen == BattleOver` (command 45) then `app == 0`; `Outcome` says how | 45, won, fled observed | `hgss-route33` |
| R3 | `[FS] + 0x230EC == 1` = "menu ready" | `HgssLegacyStateReader`; `BattleOrchestratorEvent` battle-start loop and `WaitForMoveEnd.PollMenuUp` | `ctx.sel` reached through a heap coincidence; dangling after the battle (stays 14) | `Screen == BattleMenu` (command 5, sel 1), pressed with `PressUntilAsync` | observed; input-ready gap covered by retry | `hgss-route33` |
| R4 | `[FS] + 0x692C == 14` = "evolution" | `HgssLegacyStateReader`; `LevelUpFlow.cs:384` | `app.state` through the same coincidence | `Screen == Evolution` (`app.state` 13 or 14) | 14 observed | `hgss-route33-evolve` |
| R5 | `[FS] + 0x6930` → `+0xC14` foe count, `+0xC18` enemy block | `BattleOrchestratorEvent.PokemonDataPending` through `bot.Addresses.EnemySetupOffset` | `app.setup` (`BattleSetup`) through the same coincidence; the `+0xC14`/`+0xC18` offsets inside it are right | root changes to `app.setup = [app + 0x18]`; the enemy block keeps being read with the pointer-chain opcode and parsed by `IBattlePokemonParser` / validated as today | observed | `hgss-route33` |
| R6 | three racing polls in `WaitForMoveEnd` (R2, R3, and party HP 0) | `BattleOrchestratorEvent.cs:140-237` | timing guesses | one `UntilAsync(Screen is BattleMenu or BattleOver or BattleScript{6} or BattleExpPrompt or BattleMoveLearnPrompt)`; HP is then read once to decide, not polled | menu, over, script 6, prompts observed | `hgss-route33-train` |
| R7 | blind macros `W.325`, `W.1245`, 120-frame padding for level-up and move-learn | `LevelUpFlow.cs:17-49` | "reads during that menu crash" was the old byte reading garbage | press at `Prompt` 11/13 (A), 21 (yes/no), 23 (pick), 34 (yes/no) via `PressUntilAsync` | 11, 13, 21, 34 observed; 23 decomp only | `hgss-route33-train` |
| R8 | Run spammed with `W.3` while R1 holds | `BattleOrchestratorEvent.cs:130` macro `"W.10;S.Run.3;W.5;"` and the flee loop | no outcome check; "it's possible we won't run" TODO | `PressUntilAsync(Run, expect Screen == BattleOver && Outcome == Fled, or Screen == BattleMessage)`; `BattleMessage` (sel 15, "Can't escape!") → press B and fight or retry per `BattleMode` | fled observed; sel 15 decomp only | `hgss-route33` |
| R9 | coordinates non-zero = "overworld ready" | `Overworld.WaitForReadyAsync` | heuristic on the fade-in | `Screen == FieldFree` | observed around battles only | `hgss-route33`, `hgss-navigate-heal-return` |
| R10 | position unchanged 2.5 s = "stalled / blocked" | `NavigatorEvent.WaitForStallAsync` | cannot tell a wall from a script or message | stall only while `Screen == FieldFree`; `FieldTask` means talking or warping, not blocked | task functions observed for encounters and returns; scripts, warps not yet | `hgss-navigate-heal-return` |
| R11 | map id poll until it equals the destination | `NavigatorEvent.WaitForWarpAsync` | correct read, unbounded | keep the read (overworld reader); bound it with a stale-timeout on `(Screen, mapId)` | n/a | `hgss-navigate-heal-return` |
| R12 | party diff every 1 s for poison or faint | `NavigatorEvent.WaitForPoisonEventAsync` | correct read, unbounded | keep; bound with the segment's stale-timeout | n/a | `hgss-navigate-heal-return` |
| R13 | evolution "done" byte `[app.setup + 0x198] + 0x67` | not read today (inferred in the spike, shown wrong) | | not used; evolution end is `app == 0` after `Evolution` | observed (app closed) | `hgss-route33-evolve` |
| R14 | map id `[FS] - 0x22DA4`, X/Y `0x021DA6F4`/`0x021DA6FC`, facing `[FS] + 0x1DC4` | `HgssOverworldReader` | not traced in the spike | keep | n/a | all |
| R15 | starter preview block `[FS] + 0x1BC00`, stride `0xEC` | `ShinyStarterEvent` | not traced | keep | n/a | `hgss-starter-shiny` |
| R16 | validated party, boxes, battle mirrors (`+0x25E2C`, `+0x25EEC`, `+0x2AAB0`), enemy via pointer chain | `PokemonStateReader` through ApiHawk opcodes 3 and 4 | correct; parsing and retry on the emulator side by decision | keep | n/a | all |
| R17 | `M.` macro coordinate and map-guard reads inside ApiHawk | `CommandServer` | correct | keep | n/a | all |

Rows R1 to R10 are the whole of what changes. R11 and R12 only gain a timeout. R13 is a non-read recorded so nobody re-derives it. R14 to R17 are listed so the table is the complete inventory FR-9 demands.

### Decomp-only values

Mapped by the reader, not branched on by any event until a scenario observes them and this table is updated in the same PR.

| Value | Field | Meaning | Scenario that would observe it |
|---|---|---|---|
| `OtherApp` | `app.ovyId != 12` | bag, party, PC, summary open | a navigation scenario that opens the menu |
| `BattleMessage` | `sel == 15` | "Can't escape!" or another dismissable message | a flee from a trapping foe (Wobbuffet, Arena Trap); or Mean Look in `hgss-route33` with an edited foe |
| outcome 2, 3, 4 | `[BS + 0x2420]` | lost, draw, caught | a weak lead that faints; a Poké Ball in the bag |
| script 231 | `[ctx + 0xB0]` | "use next Pokémon?" | a lead that faints with a party behind it |
| script 5, 8, 10, 11, 284 | | lost, escape failed, party list, throw ball, escape success | as above |
| getter 23, 24-30 | `[getter + 0x28]` | choosing the move to forget; the "learned" path | `hgss-route33-train` answering yes |
| `FieldTask` for scripts, warps, start menu | `[taskman + 4]` | the task function addresses are unknown until seen | `hgss-navigate-heal-return` |

### Stale-timeouts

`GameStateWait.UntilAsync(reader, until, stale, ct)` polls every `Constant.PollIntervalMs`, returns the first matching snapshot, and throws `GameStateTimeoutException` (the predicate's description and the last snapshot) when the whole `GameSnapshot` has not changed for `stale`. `PressUntilAsync(reader, press, expect, confirm, attempts, ct)` runs `press`, waits `confirm` for `expect`, and repeats up to `attempts` times. Both live in `Games/GameStateWait.cs`; more than ten loops use them, so principle 2 is met.

| Loop (after the profile spec) | Waits for | `stale` | Basis |
|---|---|---|---|
| encounter wait (`RunEncounterEvent`, `SpinEncounterEvent`, `NavigatorEvent.WaitForBattleAsync`) | any `Battle*` | none: the pacing macro bounds it; cancellation only | the loop is the hunt |
| battle start (`PokemonDataPending`, then menu) | `BattleMenu` with the foe readable | 30 s | encounter intro observed under 10 s |
| turn end (`WaitForMoveEnd`) | `BattleMenu`, `BattleOver`, script 6, a prompt | 20 s | longest move animation observed under 6 s; `BattleScript` with the same id counts as unchanged |
| Run press | `BattleOver` or `BattleMessage` | `confirm` 500 ms, 3 attempts | a tap sent before input-ready was ignored; the second landed |
| move selection | `BattleTurn` or `BattleScript` | `confirm` 500 ms, 3 attempts | as above |
| exp and move-learn prompts | the next prompt or `BattleMenu`/`BattleOver` | 15 s | prompt chain observed within 5 s |
| evolution | `app == 0` (`FieldFree` or `FieldTask`) | 30 s | evolving animation observed at 7 s |
| overworld ready | `FieldFree` | 10 s | fade-in under 3 s today |
| warp (`WaitForWarpAsync`) | map id equals destination | 15 s on `(Screen, mapId)` | loading screens under 5 s |
| navigation segment, poison watch | arrival or a battle | the segment's existing stall rule, only while `FieldFree` | |
| switch confirm (`WaitForSwitchAsync`) | PID changed | keeps its 4 s | unchanged |

A timeout ends the event with the exception, logged in full, and the bot's state goes to `Waiting` so the operator sees it on the dashboard. No automatic recovery in this spec.

### Migration order

Three PRs after the profile spec's P7 is on `main`.

| # | PR | Depends on | Behaviour change | Size |
|---|---|---|---|---|
| S1 | `HgssFieldSystemStateReader` with the chain members on `HgssPointerService` and its unit tests; `GameStateWait` with tests; not selected by the profile; NFR-1 measured in a QA run with the reader attached as a passive logger | profile P7 on `main`; the gate's scenarios passing on the legacy reader | none | M |
| S2 | select the reader in `HgssProfile`; delete `HgssLegacyStateReader` and the four legacy table members; R1 to R8 rewritten per the read table; every loop in the timeout table bounded | S1, QA gate | yes: the pointer change | L |
| S3 | R9 and R10: `Overworld.WaitForReadyAsync` on `FieldFree`, navigator stall only while `FieldFree`; R11, R12 bounded | S2 | yes | S |

S2 is over the size tripwire by design (one PR so the swap and the loops it changes are one revertable unit); the task issue says so and the owner accepts it when the issue is opened, or S2 is split into S2a (swap, R1 to R5) and S2b (R6 to R8) with S2a's loops temporarily bounded only by cancellation.

### QA gate

S2 opens only when every scenario below passes on the legacy reader (so the harness is known good) and merges only when every one passes on the chain reader, with the run folders linked from the PR.

| Scenario | Save | Proves |
|---|---|---|
| `hgss-route33` (exists, #107; `ShinyBotHunter.Qa/dashboard/hgss-route33.spec.ts`) | `hgss.qa-route33` | R1, R2, R3, R5, R8, R9; NFR-1 |
| `hgss-route33-train` (exists, #111; one level-up through the UI, new move declined) | `hgss.qa-route33` | R6, R7: scripts, turn, exp prompts 11/13, move-learn 21 and 34 on the decline path |
| `hgss-route33-fight-learn` (new) | `Pokemon HeartGold2` clone (lead one EXP short, move pending), answering yes | R7: getter 23 and the learned path, moving them from decomp-only to observed |
| `hgss-route33-evolve` (new) | `Pokemon HeartGold3` clone | R4, R13 |
| `hgss-navigate-heal-return` (planned in the QA spec) | `hgss.qa-route33` | R9, R10, R11, R12; `FieldTask` for scripts and warps |
| `hgss-togepi-egg-hatch` (planned) | an egg save, to make | the egg loop unchanged under the new field signals |

The two new scenarios (`hgss-route33-fight-learn`, `hgss-route33-evolve`) are added to the QA spec's initial set in S1's PR, as Playwright specs beside the two that exist. The two PKHeX clones are already in `C:\BizHawk-2.11\NDS\SaveRAM` on the dev machine and move to `../shinybothunterqasaves/` with README rows per the QA decision.

## Test cases

| Test | Covers |
|---|---|
| `HgssFieldSystemStateReaderTests.<Screen>` one per state-table row, scripted memory | FR-1 |
| `HgssFieldSystemStateReaderTests.ChangedAppBetweenReadsIsTransitioning` | FR-2 |
| `HgssFieldSystemStateReaderTests.OutOfRangePointerIsTransitioning` (each hop) | FR-3 |
| `HgssFieldSystemStateReaderTests.RootsReReadAfterThreeTransitioning` | the recovery path |
| `GameStateWaitTests.UntilReturnsOnMatch`, `.UntilThrowsWhenStale`, `.StaleCountsSameScriptAsUnchanged`, `.PressRetriesThenThrows` | FR-5, FR-6 |
| `LevelUpFlowTests` over a fake `IGameStateReader` sequence 11 → 13 → 21 → 34 → menu | FR-7 |

## Rollout

- S1 is inert and can ship whenever the profile is on `main`.
- S2 and S3 ship on their own feature branch with the QA evidence; the owner merges to `main` after a manual look at the `hgss-route33` video.
- Rollback: revert the selection line and restore `HgssLegacyStateReader` and the four table members from the previous commit.

## Risks

| Risk | Mitigation |
|---|---|
| `FS` or `sub0` move on a save reload or title-screen return (the HGSS starter hunt soft-resets) | the three-strikes re-read; `hgss-starter-shiny` joins the gate if the starter hunt is moved onto the reader (it does not use it today) |
| Stale-timeouts too short for an unobserved animation | each value above has a 3x margin over what was observed; a timeout is loud (exception, dashboard `Waiting`), not silent |
| A transition that happens and reverts between the two root reads | the next poll sees the true state; no decision is taken on a single `Transitioning` |
| The read table drifts as features are added | FR-9 and the reviewer checklist; the profile spec's grep catches a literal, this table catches an undocumented member |
| Decomp-only values turn out wrong when first observed | they are mapped, not branched on; the first scenario that reaches one updates the table before any event uses it |

## Open questions

> **OPEN:** whether S2 ships as one PR over the size tripwire or as S2a/S2b. Owner's call when the task issue is opened.

## Changelog

- 2026-10-21: split out of the game profile spec at the owner's request so the profile work is restructure only; read table (R1 to R17), decomp-only table, stale-timeout table and the FR-9 rule added.
- 2026-10-22: gate rewritten around the two Playwright scenarios that exist on `main` (`hgss-route33`, `hgss-route33-train`); the level-up scenario is the existing one, the new ones are the learn-accept path and evolution.
