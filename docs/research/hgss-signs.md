# Research: signs on the HGSS field

Status: measured 2026-11-21 on HeartGold (US, revision 0) on the QA emulator (`C:\BizHawk-2.11`), EmuHawk alone, one snapshot per frame derived as `HgssFieldSystemStateReader` computes it. Nothing here is implemented; it is the source for the [navigator signs spec](../specs/navigator-signs.md).
Related: [pointer discovery](pointer-discovery.md) (the field script chain and the natives already in the table), [HGSS state reader spec](../specs/hgss-state-reader.md), AGENTS rule 16.

## The bug this came from

On `v0.1.2`, Wild Encounter with a train type from Route 32's grass at (474, 294): the heal trip goes to Violet City's Pokémon Center, and the walk back (`M.Left.488; M.Up.264; M.Left.469; ...` on map 73) lands on (488, 264), the tile below the town sign at (488, 263). The sign opens, the walk's next direction is already held, and the navigator waits 10 s for the field to be free and parks in Waiting (run `20261122-001157`). The same sign stopped the Primo egg hunt's fixed walk on 2026-11-02 (#255).

## What opens a sign

Map 73, the sign at (488, 263), the player on (488, 264).

| How the player got there | Result |
|---|---|
| A step Up from (488, 265), Up released anywhere from 6 frames before landing to 18 after | opens; the script starts 2 frames after Y reads 264 |
| Arriving along y 264 from the side | does not open |
| Standing below it and turning to face it (an Up tap) | opens |
| The walk's `M.Up.264;M.Left.469` from (488, 265) | opens, 4 of 4 |

So a sign opens when the player steps Up onto the tile below it, however short the press. It is not a held key running into it.

## What memory shows

The snapshot while a sign is up is `Screen = FieldTask`, `TaskFunction = RunScriptsTask` (`0x0203FF45`), `Script` = the sign's own script number on its map, and `Native` moving through:

| Native | Decomp (`src/scrcmd_c.c`) | What the game is doing | Frames |
|---|---|---|---|
| none | | the script starting | 5 |
| `0x02041455` | `sub_02041454`, from `ScrCmd_058` | the board sliding in; reads no key | 6 |
| none | | | 2 |
| `0x020415E1` | `sub_020415E0`, from `ScrCmd_060` | waiting for a new A, B or d-pad key (`gSystem.newKeys`) | until a key |
| `0x02041521` | `sub_02041520`, `TrainerTips` | a text sign printing its text; a new d-pad key closes it, else it runs to the end of the page | text signs only |

A direction board (the town sign) is fully up from the first `0x020415E1` frame, 13 frames after the script starts, and has no text printer. A text sign (the signs at (491, 257) and (475, 257)) goes `0x02041455`, then `0x02041521` with the script's printer in slot 0 reaching an arrow, then `0x020415E1`.

`Script` is not a sign marker: Violet City's signs are scripts 8 to 11 of that map, and Route 32's are 7 to 9 of map 36.

A sign never closes by itself (671 frames with no key).

## What closes it

From `0x020415E1` on the town sign. The close is the same every time: `Native` none for 4 frames, `0x02040BCD` for 2, none for 2, then `FieldFree`, 8 frames after the game sees the key.

| Input | Closed | Note |
|---|---|---|
| A or B, 2 emulator frames (the bot's `T.A`, `T.B`) | 8 of 8 | |
| A or B, 4 frames | 8 of 8 | |
| A or B, 1 frame | 4 of 16 | the game reads keys at 30 Hz; a one-frame press is missed on the other frames |
| a new d-pad press, 2 frames | 16 of 16 | the player does not move or turn |
| the direction already held since before the script started (the walk's case) | 0 of 4 | ignored for the whole wait: it is not a new key |
| a press on the first `0x020415E1` snapshot | 12 of 12 | no first-frame gap |
| any press while `Native` is `0x02041455` | 0 of 15 | ignored and not buffered; the board finishes opening |

On a text sign, A or B while the text prints changes nothing; at the page's arrow one turns the page and a second, at `0x020415E1`, closes it. A d-pad tap closes a text sign at any stage.

After the close: by B, nothing else happens, and a second B in the free field does nothing. By A, the player still faces the sign and a second A opens it again.

## A door warp, for contrast

Into and out of the Violet City Pokémon Center: `Script` is null and `TaskFunction` is never `RunScriptsTask` on any frame (168 frames in, 218 out); the field tasks seen are the warp's own, then `Transitioning`, then tasks again.

## Not measured

- The three signs on Route 32 (the walk to them is through grass). By the game's event data their scripts use the same commands.
- Any other game or revision. SoulSilver's addresses are the table's concern as for every other native.
- A sign whose text has more than one page.

## Evidence

Traces and probe scripts were kept in the session's scratch folder on the dev PC (`rspk/probe-hg/`: `e1_repro` to `e14_text_first`). The stuck run is `ShinyBotHunter.Qa/dashboard/qa-runs/20261122-001157-hgss-route33-dashboard/` (gitignored). The decomp is the `pokeheartgold` checkout at `D:\Temp\pokeheartgold`.
