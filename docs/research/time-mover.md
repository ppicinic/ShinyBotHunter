# Spike: a hunt that moves by the in-game clock (HeartGold)

Run 2026-11-24 on `main` at `57c2eef` (v0.1.3), HeartGold (USA, rev 0), on a copy of `hgss.qa-route32`. Throwaway code on a local branch; nothing from it is meant to merge. The patch and logs are in the evidence folder at the end.

The question: can a Wild Encounter hunt on Route 32 walk to Route 30 when the in-game clock enters a window, hunt there, walk back when the window ends, and have the dashboard follow without the operator.

## Answer

Yes. Three runs did it end to end with no operator: Route 32 to Route 30 at the start of a window, Route 30 back to Route 32 at 10:00, and Route 32 to Route 30 at 20:00. The existing navigator found and walked the route. Four things have to change for it to be a feature, listed under "What does not work as built".

## The in-game clock

Two copies, both read on the emulator.

| What | Where | Behaviour seen |
|---|---|---|
| The live clock the game reads the DS clock into (date as year, month, day, weekday; time as hour, minute, second; `u32` each) | `0x021D1058`, a second copy at `0x021D1074` (static, so revision-dependent) | Advances every second, on the field and in battle. |
| `SysInfo_RTC` in the save block (`initialized`, then the same seven values) | save data + `0x20`, save data being `[[0x021D4158] + 0x0C]` | Copied from the live clock only on the field, and only once 60 s have passed since the last copy. Frozen in battle; catches up on the return to the field. |

Samples of the save block copy against the system clock, idle on the field: `15:07:34`, then `15:08:34`, then `15:09:34`, each about one second after the minute; in a battle it stayed at `15:05:52` for 99 s and read `15:07:34` on the first field read after.

**The wild table follows the live clock, not the save block copy.** In the 20:00 run the mover read the save block copy at `19:59:33` and the next encounter was a Wooper, which Route 32 has only at night; the copy read `20:00:37` at the decision after. So a mover that reads the save block copy is up to a minute plus one battle late: 75 s late at the 10:00 boundary (three encounters), about 20 s late at the 20:00 one (one encounter). Reading `0x021D1058` removes that.

The emulator's DS clock is the system clock on the owner's config (`UseRealTime: true`). The spike ran its own config with `UseRealTime: false` and an `InitialTime`, which is how a boundary was reached in minutes; the game then keeps time by emulated frames.

Time of day for the wild tables, from the three runs and the ROM's tables: morning is hours 4 to 9, day 10 to 19, night 20 to 3. The 4:00 boundary was not run.

## Ledyba is not in HeartGold

The example was Ledyba on Route 30 in the morning. Two runs on Route 30 in the morning (07:00 to 07:05 and 09:53 to 09:59) gave about forty encounters, all Caterpie, Metapod or Pidgey, and no Ledyba, so the tables were read from the ROM (`a/0/3/7`, 142 files of 196 bytes: the rate, twelve levels, then twelve species each for morning, day and night):

| Route | Morning | Day | Night |
|---|---|---|---|
| 30 (file 3) | Caterpie 50, Pidgey 40, Metapod 10 | Pidgey 50, Caterpie 35, Metapod 15 | Rattata 40, Spinarak 30, Hoothoot 30 |
| 32 (file 8) | Rattata 35, Bellsprout 30, Mareep 20, Hoppip 10, Zubat 5 | Rattata 40, Bellsprout 30, Mareep 20, Hoppip 10 | Wooper 35, Rattata 30, Bellsprout 20, Mareep 10, Zubat 5 |

No file in HeartGold has Ledyba in a morning list. Ledyba is SoulSilver's; HeartGold's counterpart is Spinarak at night. The third run used that: after 20:00 Route 30 gave Spinarak 8, Rattata 4, Hoothoot 3.

## The walk

`PathfindingService.FindNodePath` already routes between any two tiles; the heal trip's `ReturnToStart` uses it. The mover set the hunt's start tile to the new place and pushed `ReturnToStart`, and the hunt carried on from there.

Route found, both ways: Route 32 (map 36) (474, 294) → Violet City (73) (511, 270) → the gatehouse (97) (1, 7) to (10, 7) → Route 31 (35) (518, 269) → Route 30 (34) (548, 299).

| Trip | In-game | Time |
|---|---|---|
| 32 → 30 | 09:40 | 70 s, two wild battles on the way |
| 32 → 30 | 09:52 | 58 s |
| 32 → 30 | 07:00 | 35 s |
| 30 → 32 | 10:00 | 54 s |
| 32 → 30 | 20:00 | 53 s, one wild battle on the way |

A wild battle on the walk is fled and the walk resumes from where it stopped. No trainer stopped the walk on this save. Other routes, HMs and trainers in the way were not tried.

## The dashboard

As built, each phase is its own window, named and titled by game, phase and game file. A phase change closes one window and opens another at its own remembered place and size (640 x 520 by default), with a different title, so an OBS window capture loses it.

The spike's prototype kept one window per game file: same window handle, same title (`HGSS — HG-TimeMove`), same place and size, with the new phase loaded into it. The backend replaced the old phase with the new one in a single push. Seen: handle `15404672` at (136, 136) 640 x 520 before and after both swaps of the 10:00 run, showing Route 32 (eight rows), Route 30 AM (one row), Route 32 again. The window does not size to the rows; the rows stretch to fill it, so a one-row phase and an eight-row phase are the same size.

## What does not work as built

1. **A door entered sideways parks the walk.** Violet City's gatehouse door is marked on the tile the player stands on, and the warp fires on the next step Right. The navigator stopped on the tile and timed out waiting for the warp (twice, "Waited for the warp to map 73 (still on map 73)"). The spike's workaround was one more step in the last walked direction when the walk ended on the door tile with no warp. A real fix belongs to the navigator and should come from the warp's arrow in the map events, not from the last direction.
2. **Encounters on the walk are filed under the destination's phase.** The mover changed the phase before walking, so a Pidgey and a Hoothoot met on Route 31 were counted under Route 30. Three encounters after 10:00 were also filed under the morning phase because of the clock lag.
3. **The prototype window went blank on both swaps to `Route 30 Night`.** The page finished loading and logged no error; the same phase in an ordinary phase window rendered, and swaps to `Route 30 AM` and back to `Route 32` rendered. Cause not found.
4. **The destination tile is hand-picked.** The first tile given was four columns off the grass and the hunt spun on a path with no encounter and no complaint. A destination needs a check that it is a tile of the kind the hunt needs.

## The blank window, explained (2026-11-24)

A research round on the spike's patch (a worktree at `57c2eef` with `spike-code.patch` applied, the dev databases, no emulator) reproduced item 3 of "What does not work as built" and found the cause. It is not the phase.

- **Reproduced.** A swap with the window in plain view draws, "Route 30 Night" included. A swap while another window fully covers the phase window gives a blank window, for any of the three phases. The blank capture is byte for byte the spike's `night-0.png` (md5 `57b5f14a...`).
- **Cause.** Chromium's occlusion tracking marks a fully covered window hidden (`document.visibilityState` reads `hidden`) and stops producing frames for it. The prototype reloads the page in the existing window (`loadFile` / `loadURL` in `syncPhaseWindows`), so the old frame is gone and no new one is painted until the window is uncovered; what shows is the window's background colour. The page itself was complete while blank: the card's rows were in the DOM and the console had no error. An ordinary phase window drew because a new window is shown on `ready-to-show`, which brings it to the front.
- **Removing it.** With `backgroundThrottling: false` in the card window's `webPreferences`, or with the launch switch `--disable-features=CalculateNativeWinOcclusion`, the same covered swap drew. Uncovering the window also painted it within seconds.
- **Not established.** What covered the window during the spike's two Night swaps. And whether a covered phase window on `main` today stops repainting its counts by the same mechanism: likely, not tried.

Evidence: `ShinyBotHunter.Qa/dashboard/qa-runs/20261124-blank-window/` (gitignored): `covered-0.png`, `amcovered-0.png`, `r32covered-0.png` (blank), `night1-0.png` (drawn, uncovered), `fixB-0.png` and `flagA-0.png` (the two removals).

## Not proved

- The 4:00 boundary, a day-of-week window, and a window crossed while the bot is parked or in a long battle.
- A destination behind an HM, a ledge or an unbeaten trainer.
- Any other game or revision; `0x021D1058` is a static address found on HeartGold rev 0 only.
- Whether the save block copy is ever more than 60 s plus one battle behind (a heal trip, an evolution).
- The live clock's behaviour across midnight.

## Left on the dev PC

- Dev Mongo: three phases added for HGSS (`Route 32`, `Route 30 AM`, `Route 30 Night`); the dashboard subscriptions the spike made were removed.
- Dev Postgres: about 87 encounters for game file `HG-TimeMove` under those phases.
- The ROM copy, its save and the spike's emulator config were deleted. No staged QA save was touched.

## Evidence

`ShinyBotHunter.Qa/dashboard/qa-runs/20261124-time-mover-spike/` (gitignored): `spike-code.patch` (the mover, the door step, the one-push phase swap, the one-window dashboard), `backend5.log` (the 09:52 and 07:00 runs), `backend6.log` (the 20:00 runs), `hg.py` (clock and party probe), `enc.py` (the ROM's wild tables), `shots/`.
