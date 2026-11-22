# Spec: the navigator closes a sign it opens (HGSS)

Status: Implemented on `fix/navigator-sign` (epic #498), 2026-11-22. Not yet merged to main. Read "As built" first. A bug fix, run as a feature branch at the owner's instruction.
Last updated: 2026-11-22
Owner: Phil
Related: [research: signs on the HGSS field](../research/hgss-signs.md) (the source for every address and count here), [HGSS state reader spec](hgss-state-reader.md) (R18, R19: the script press loop), [Primo egg hunt](primo-egg-hunt.md) (E5c and E5d: the same sign), [feature workflow](feature-workflow.md), AGENTS rule 16.

## Summary

A walk that steps Up onto the tile below a sign opens the sign. The navigator then waits for the field to be free, which never comes, and parks the bot. Reported on `v0.1.2` on the walk from Violet City's Pokémon Center back to Route 32, and reproduced there on the dev PC.

The navigator had a recovery for a blocked walk (press B, make the tile expensive, plan again). Since #151 (2026-10-23, in `v0.1.1`) a stall is only counted while the field is free, and with a sign up the field is not free, so the recovery is never reached. The Primo egg hunt met the same sign and closed it in its own walk (#255); that code was deleted when the walk moved onto the navigator (#257).

The fix: the walk sees the sign in the game's state, closes it with B at the state that takes a key, and plans again. No timer decides anything.

## Requirements

| ID | Requirement | Verified by |
|---|---|---|
| FR-1 | A sign is recognised from the snapshot: `Screen == FieldTask`, `TaskFunction == RunScriptsTask` and `Native` one of the two sign natives (the wait for a key, `0x020415E1`; the text sign's printing, `0x02041521`). Not from `Script` (it is the sign's number on its own map), and not from time. | Unit tests; research "What memory shows" |
| FR-2 | While a walk macro runs, a sign recognised by FR-1 ends the segment at once: the macro is cleared, so the held direction is released. A script with any other native, a warp and a transition are not a sign, and the walk treats them as today. | Unit test: a sign mid-walk ends the segment; a warp does not |
| FR-3 | The sign is closed with B, as the owner chose (2026-11-21). One B at `0x020415E1`, confirmed by the field reading free. On a text sign, one B at the printer's arrow under `0x02041521`, confirmed by the native or the printer's cursor changing, then the B at `0x020415E1`. No press while the board slides in (`0x02041455`), while text is still drawing, or with no native read. A press is sent again only when its confirming state did not come within a bounded wait; every wait ends in its state, cancellation or a stale-timeout naming the snapshot (rule 16). | Unit tests: one B for a board; two for a text sign; none during the slide-in; a stale-timeout |
| FR-4 | After the close the walk does what it does after a block today: the tile the player stands on is made expensive and the segment is planned again from where the player stands. It counts toward the segment's blocked-retry cap. | Unit test; the emulator run ends back on the start tile |
| FR-5 | The blind `T.B;W.30;T.B;W.30` of the blocked-walk recovery is deleted. A stall is only counted with the field free, where there is nothing to dismiss. | Review; the existing stall test |
| FR-6 | The two natives join `HgssPointerService` with the table's comment (symbol, kind, observed, QA scenario). No address outside the table. | Review |
| FR-7 | Log lines, prefix `[Navigator]`: the sign seen (with the snapshot), each B with what confirmed it, the close. | QA parses them |

Non-functional: no change to a walk that opens no sign (`hgss-navigate-heal-return`, `hgss-heal-variants`, `hgss-poison-walk` pass unchanged). No ApiHawk change, no new package. Ruby and Sapphire's navigator is not touched.

## Non-goals

- Planning routes that never step under a sign. The tile penalty already steers later walks in the same hunt.
- Trainers, item balls and other field scripts on a walk.
- The Pokégear call (AGENTS rule 17).

## QA

A new save, `hgss.qa-route32`: the player in Route 32's grass at (474, 294) with the lead at 1 HP, so the first decision is the heal trip to Violet City. A new scenario, `hgss-sign-walk`, on it: Wild Encounter, Spin, Train Standard; passes when the log shows the sign seen and closed with one B on the walk back, no stale-timeout, the bot back on its start tile and spinning, the party healed, the save unchanged. Run five times in a row. Regression: `hgss-navigate-heal-return`, `hgss-poison-walk`.

## Tasks

| Task | Scope | Size |
|---|---|---|
| S1 | FR-1 to FR-7: `NavigatorEvent.cs`, `HgssPointerService.cs`, their tests | S |
| S2 | The scenario `hgss-sign-walk`, the save's row in `qa-automation.md`, the guide's "when something goes wrong" line if it names this stop | S |

## As built

- **Recognition** (#499): `NavigatorEvent.IsSign` is true for `FieldTask` under `RunScriptsTask` with the native at `SignWaitNative` (`0x020415E1`) or `SignPrintingNative` (`0x02041521`), both new in `HgssPointerService`. The walk's stall watcher, which already waited for the field to be free whenever it was not, now waits for the field free or a sign (the same 10 s stale) and ends the segment on a sign. It reads every 250 ms with the field free and every 33 ms once it is not, so a board is seen within about a quarter of a second of being up.
- **The close** (#499): `NavigatorEvent.CloseSignAsync`, a loop of its own. It waits for a state that takes a key (the wait native, or the text sign's native with the printer at its arrow), sends one `T.B`, confirms it by that state changing, and sends it again only if the state still reads the same after 1 s, three times at most. It returns when the field reads free. Nothing is sent while the board slides in, while text draws or with no native; a snapshot that stays unchanged for 20 s parks the bot naming it. `PressThroughScriptAsync` (the nurse, poison, Headbutt, Primo, the static hunt) is not touched.
- **Afterwards** (#499): the sign is closed first, then the attempt counts toward the segment's cap of three, the player's tile is made expensive and the segment is planned again from where the player stands. The log line is `Walked under a sign at map=.. (x,y); set tile cost N.`; a stall's line still begins `Movement blocked at`.
- **Deleted** (#499): the blind `T.B;W.30;T.B;W.30` and its constant.
- **QA** (#500): the scenario `hgss-sign-walk` on the new save `hgss.qa-route32`; the three heal-walk scenarios now also fail on a `Sign seen` line.

## What diverged

- FR-3 says the board's B is confirmed by the field reading free. As built it is confirmed by the native leaving the wait (the first thing the key changes, on the frame it is seen), and the loop then returns only at the field free. On the emulator the first read after the B already showed the field free in all fifteen runs.
- The QA section asked that the bot be "back on its start tile and spinning". The scenario judges the tile by the Spin's own position check (`[Spin] facing=` after the arrival, no "moved while turning" line), not by reading the tile from memory: with the start tile in grass a wild battle can begin before that read and the position then reads (0, 0). Found at review of #502.
- The scenario files its run under the existing `Route 33` milestone; the QA database has no `Route 32`. It judges no counts.
- `hgss-poison-walk` was not run: its save is not staged on the dev PC.

## Evidence

All on the dev PC, HeartGold on the QA emulator, 2026-11-21 and 22 (run folders under `ShinyBotHunter.Qa/dashboard/qa-runs/`, gitignored).

| What | Run | Result |
|---|---|---|
| The bug on `v0.1.2` | `20261122-001157` | parked at (488, 264) with the sign up; `Waited for the overworld ready ... Script = 9` |
| The fix, the reproduction five times | `20261122-011321` and the four after it | each: one `Sign seen`, `Sign: T.B at native 0x020415E1 after 1 press(es)`, `Walked under a sign at map=73 (488,264)`, `arrived back at start` |
| `hgss-sign-walk` on the fix, before its end-tile check was changed, five times | `20261122-012335` to `20261122-012941` | passed |
| `hgss-sign-walk` on the fix, five times in a row (the merged scenario) | `20261122-013509` to `20261122-014119` | passed |
| `hgss-sign-walk` on `main` without the fix | `20261122-013259` | failed at "a stale-timeout fired", as it should |
| `hgss-navigate-heal-return` on the fix | twice, the second with the `Sign seen` check | passed, no sign |
| `Scripts/ci-local.ps1` on the branch tip | | see the PR to main |

Not checked: a text sign on the emulator through the bot (unit tests and the research round only); a sign on any other map; SoulSilver; `hgss-poison-walk` and `hgss-heal-variants`' other saves.

## Changelog

- 2026-11-21: first draft, from the reproduction and the emulator round.
- 2026-11-22: implemented (#499, #500); As built, What diverged, Evidence.
