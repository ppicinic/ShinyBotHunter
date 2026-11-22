# Spec: the navigator closes a sign it opens (HGSS)

Status: Draft, 2026-11-21. A bug fix, run as a feature branch (`fix/navigator-sign`) at the owner's instruction.
Last updated: 2026-11-21
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

## Changelog

- 2026-11-21: first draft, from the reproduction and the emulator round.
