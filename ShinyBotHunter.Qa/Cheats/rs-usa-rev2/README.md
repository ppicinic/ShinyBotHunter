# Ruby and Sapphire (USA, Europe) rev 2: wild shiny by a frozen gRngValue

`wild-shiny.cht` is one line, the same for both games:

```
3004818	67F18FB9	N	System Bus	1	wild shiny: gRngValue	d	h	0	None
```

It is in the `hgss-usa-rev0/wild-shiny.cht` column order: address, value, compare `N`, domain `System Bus`, enabled `1`, name, size `d` (a double word), display `h`, big-endian `0`, compare type `None`. The line ends in LF; the scenarios write it with LF whatever the checkout does.

## What it does

It freezes `gRngValue` (`0x03004818`, u32) at `67F18FB9`. While it is armed, every frame starts from that value, so the next encounter check that is not immune passes and builds the same Pokémon: personality value `CD631295`, shiny value 1, a Lv 2 Wurmple (species 265). The battle starts on the fifth turn after arming (four immune checks, then the roll). The frozen value is the state one call before the roll, because the frame's own `Random()` in the VBlank interrupt runs first.

The value belongs to this save's trainer IDs, 39541 / 17794 (`rs.qa-route101`), and to Route 101's encounter rate. The spike's `seed.py <tid> <sid> [rate × 16]` makes one for another save or route.

## How the scenarios use it

- **Installed under the game's name**, not the ROM file's: `C:\BizHawk-2.11\GBA\Cheats\Pokemon - Sapphire Version (USA, Europe) (Rev 2).cht` for `SA-Wild` and `…\Pokemon - Ruby Version (USA, Europe) (Rev 2).cht` for `RU-Wild`. `SA-Wild.cht` and `RU-Wild.cht` are not loaded. `dashboard/rs-wild.ts` (`installCheat`, `removeCheats`) writes the file before launch and deletes both names in teardown.
- **Loads disabled.** The QA install has `DisableOnLoad`, so the line's enabled `1` does not arm it; the Toggle All Cheats hotkey (Ctrl+Shift+F12) does. `assertUnarmed` checks at launch that three reads of `gRngValue` 200 ms apart differ.
- **Armed and disarmed during a battle only.** The QA install binds the GBA's Select to Shift, so the hotkey also presses Select. In the free field Select shows "An item in the BAG can be registered on SELECT", which locks the field until A. At a battle's action menu it does nothing. `armCheat` reads the battle bit after the hotkey and fails at once if the hotkey landed outside the battle.
- **The armed check** is `gRngValue` reading `67F18FB9` on three reads in a row, 200 ms apart; unarmed it differs on every read.

## Verified

On both ROMs in the spike (research doc [`docs/research/rs-wild-encounter.md`](../../../docs/research/rs-wild-encounter.md), "A shiny on demand for QA"): the fifth turn after arming started a battle with personality value `CD631295`, shiny value 1, a Lv 2 Wurmple, as `seed.py` printed. On Ruby the whole QA flow ran: a normal encounter, the cheat armed at its action menu, the battle fled with the cheat on, and the next encounter was the shiny.
