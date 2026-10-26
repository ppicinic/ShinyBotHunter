# Sapphire (USA, Europe) rev 2: Wishmaker seed freeze

No `.cht` is stored here. `rs-jirachi-shiny.spec.ts` generates the file for each run with `cheatFile()` in `dashboard/jirachi-seed.ts`. It installs the file enabled as `C:\BizHawk-2.11\GBA\Cheats\Pokemon - Sapphire Version (USA, Europe) (Rev 2).cht`, so BizHawk loads it when the ROM opens on every cycle. The file is deleted in the scenario's `finally` block, and a copy is kept in the run folder.

## What it does

The Bonus Disc seeds Jirachi's PID from the checksum of section 0 in the save's first block. `craftSeed()` sets play time in the hunt save's live section 0 so that this checksum is `ECDD`, which gives PID `987ED636`, shiny for OT WISHMKR 20043/0. The bot saves in-game twice per cycle, and the game copies `gSaveBlock2` into section 0 on each save. The cheat therefore freezes, in RAM, every section-0 field that moves between saves. The first block the bot writes then keeps the crafted checksum.

| Offset in `gSaveBlock2` | Field | Size | Why it is frozen |
|---|---|---|---|
| `0x0E` | play time hours | `w` | moves every hour; the solver searches it |
| `0x10` | play time minutes | `b` | moves; the solver searches it |
| `0x11` | play time seconds | `b` | moves; the solver searches it |
| `0x12` | play time frames | `b` | moves; the solver searches it |
| `0xA0` | `lastBerryTreeUpdate` day (s16) | `w` | can move; kept at the saved value |
| `0xA2` | `lastBerryTreeUpdate` hours | `b` | moved across the soak's saves; kept at the saved value |
| `0xA3` | `lastBerryTreeUpdate` minutes | `b` | as above |
| `0xA4` | `lastBerryTreeUpdate` seconds | `b` | as above |

Every line is in the `hgss-usa-rev0/wild-shiny.cht` column order: address, value, compare `N`, domain `System Bus`, enabled `1`, name, size, display `h`, big-endian `0`, compare type `None`. Lines end in LF. There is no compare value, because these are data fields, not code.

## Verified (2026-10-26, run `20261026-090332-hgss-route33-dashboard`)

- `gSaveBlock2` is at `0x02024EA4` on the `System Bus` domain of BizHawk's mGBA core. Through the ApiHawk memory port, `+0x00` read `CA DC DD E0` ("Phil") and `+0x0A` read TID 39541.
- The cheat was active from the ROM load. Two reads 1.5 s apart returned the crafted values: hours 43, minutes 59, seconds 19, frames 59, berry stamp day 47 at 08:13:30. Play time would have ticked between the reads if nothing held it.
- The first cycle logged `Seed ECDD predicts PID 987ED636`, so the frames byte was saved as crafted. The `+1 frames` allowance was not used.
- After the bot's saves, the hunt save's first block had seed `ECDD`.
