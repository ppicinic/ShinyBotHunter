# Pickup farming as a battle mode (HGSS)

Status: spike done, 2026-11-07. Nothing in this doc is implemented in the bot; it is the source for a pickup-farming spec.
Related: [pointer discovery](pointer-discovery.md) (the field, battle and text-printer chains this reads), [HGSS state reader spec](../specs/hgss-state-reader.md), [headbutt spike](headbutt.md) (the probe style this follows), [QA automation](../specs/qa-automation.md) (saves)

## What was proved

On a copy of the owner's Route 33 save with five Aipom carrying Pickup behind a Lv 37 Typhlosion, the existing bot in train mode (Wild Encounter, Run, train Standard, target level 101) won 39 wild battles in a row on Route 33 and the Aipom picked up five items in the first 19 of them, matching the decomp's rule and tables. Once all five held an item, 21 more wins gave nothing. A probe then opened the Pokémon menu from the field and took all five items through Item → Take, driven by memory reads alone, in 10.4 s for the whole trip, after which the party read showed every Aipom empty-handed. Pickup needs a knockout: the roll lives in the battle-win script only, so a fled battle never gives an item.

| Measure | Result |
|---|---|
| Battles in the Fight run | 40 (39 `Outcome = Won` logged, 0 fled, no exception, one Typhlosion level-up 37 → 38) |
| Pickups | 5: King's Rock (Lv 11 Aipom, battle 5), Super Potion (Lv 22, battle 6), Repel (Lv 36, battle 13), Potion (Lv 1, battle 14), Hyper Potion (Lv 15, battle 19) |
| Pickups once all five held an item | 0 in 21 wins (battles 20–40) |
| Won battle, flee-to-flee | about 30 s (Typhlosion one-shots Lv 2–6 wilds) |
| Menu trip, X to field free | 10.4 s for five takes; 1.27–1.45 s per Pokémon |
| Save after the run | not written by the game (no in-game save), the loaded file equals its `.bak` |

## The rule, from the decomp (pret/pokeheartgold)

- The roll is `BtlCmd_GenerateEndOfBattleItem` (`src/battle/battle_command.c`), run by the battle script command `GenerateEndOfBattleItem`. That command appears in one subscript, `files/battledata/script/subscript/subscript_0004_BattleWin.s`, at `_PAY_DAY_PICKUP`, which the win script reaches for every battle that is not a trainer battle. A fled battle runs a different subscript. **Pickup needs a knockout; fleeing never rolls it.**
- The roll walks the whole party, not the battlers: each member whose ability is Pickup (53), that is not an egg and holds nothing, gets `Random % 10 == 0` (10 %). Participation is not required.
- On a hit, `rnd = Random % 100` and `tier = (level - 1) / 10` (capped at 9). The nine common items are `sPickupTable1[tier + j]` for the first `j` with `sPickupWeightTable[j] > rnd`, weights `30, 40, 50, 60, 70, 80, 90, 94, 98`: 30 / 10 / 10 / 10 / 10 / 10 / 10 / 4 / 4 %. `rnd` 99 gives `sPickupTable2[tier]`, 98 gives `sPickupTable2[tier + 1]`: 1 % each.
- `sPickupTable1` (18): Potion, Antidote, Super Potion, Great Ball, Repel, Escape Rope, Full Heal, Hyper Potion, Ultra Ball, Revive, Rare Candy, Sun Stone, Moon Stone, Heart Scale, Full Restore, Max Revive, PP Up, Max Elixir. `sPickupTable2` (11): Hyper Potion, Nugget, King's Rock, Full Restore, Ether, Iron Ball, TM56, Elixir, TM86, Leftovers, TM26.
- The tier window slides one item per 10 levels. Rare Candy is the 4 % item at Lv 21–40 and the 10 % item at Lv 41–100. No tier reaches its 30 % first slot, since that would need tier 10 and the cap is 9. Leftovers is a 1 % item at Lv 81–100. The five observed items sit where the tables put them (the King's Rock is the Lv 11–20 tier's 1 % entry). (Corrected 2026-11-08: this line said 30 % at Lv 91–100, which these tables do not give; #279.)
- Honey Gather runs in the same loop (Honey only).

Expected yield with five empty-handed Pickup Pokémon: `1 − 0.9^5 = 41 %` of won battles give at least one item, 0.5 items per battle on average while all five are empty; the run's 5 in 19 is in line with that as the empty count fell. Items held stop the roll, so the menu trip is what keeps the rate up.

## The party menu, from the decomp and the probe

- The start menu's Pokémon entry launches `gOverlayTemplate_PartyMenu` through `FieldSystem_LaunchApplication` (`start_menu.c`, `launch_application.c`: `PartyMenu_LaunchApp_Unk1`, context `PARTY_MENU_CONTEXT_FIELD`). It is a sub-app like the battle: while it runs `sub0.app` is non-NULL, `taskman` holds the start menu's task, and the state reader classifies it `OtherApp`. Its `app.ovyId` reads `0xFFFFFFFF` (`FS_OVERLAY_ID_NONE`), so the overlay id does not identify it; the app being up with the start-menu task underneath, and the states below, do.
- Its state machine runs on the overlay manager's state int (`PartyMenuApp_Main(manager, int *pState)`), the word the reader already knows as `app.state` (`app + 0x14`). `PartyMenuState`, observed values in bold: 0 init, **1** the list (pick a Pokémon), **2** the context menu, **15** the Item sub-menu (Give / Take), **17** the "Received the X from Y." message, 24 waiting for the text printer, 25 after the message, **28** Switch's second pick (reached by accident, see below), 32 begin exit, 33 exit fade. The highlighted party slot is `PartyMenu.partyMonIndex` at `data + 0xC65` (`data` = `app + 0x1C`, `AppDataOffset`), read 0–5 as expected on every touch.
- The context menu for a non-egg party member in the field is built by `sub_0207B0B0`: Summary, Switch, Item (Mail when the item is mail), Quit, then one entry per field move the Pokémon knows. Field-move entries are drawn in the left column, over the party cards, so the right column's Summary / Switch / Item / Quit keep their places (observed with Rock Smash on one Aipom).
- The "Received the X from Y." message waits at the arrow (text printer slot 0, `pr.state` 2) about 0.75–0.9 s after the Take touch; an A before the arrow is ignored and the state stays 17, after which the next touches land on the wrong things (the first loop reached state 28 that way). Gate the A on the printer, as the field presses already do.

### Touch targets (DS bottom-screen coordinates, as `S.x.y`)

| Screen | Target | Touch |
|---|---|---|
| Start menu (after X) | POKéMON | (44, 77) |
| Party list | slots 1–6 | (63, 27), (191, 27), (63, 77), (191, 77), (63, 123), (191, 123) |
| Party list | CANCEL | (227, 178) |
| Context menu | ITEM | (191, 106) |
| Item sub-menu | TAKE | (191, 66) |

### The loop, as the probe ran it (every wait on memory, 16 ms polls)

| Step | Press | Wait for | Observed |
|---|---|---|---|
| Open the start menu | X | `app == 0 && taskman != 0` | 80 ms |
| Open the party | touch POKéMON | `app != 0 && app.state == 1` | 1.4 s |
| Pick slot i | touch the slot | `app.state == 2 && partyMonIndex == i` | 0.12 s |
| Item | touch ITEM | `app.state == 15` | 0.27–0.29 s |
| Take | touch TAKE | `app.state == 17` and printer slot 0 at the arrow | 0.73–0.89 s |
| Dismiss | A | `app.state == 1` | 0.12–0.14 s |
| Leave | touch CANCEL | `app == 0 && taskman == 0` (field free) | 1.8 s |

Five takes and the exit: 10.4 s. Which slots to visit comes from the party read the bot already does before every encounter (`HeldItem` per member, `HgssPokemonParser`), so no screen is inspected.

## The save

`hgss.qa-pickup` (`../shinybothunterqasaves/hgss/qa-pickup.SaveRAM`): a copy of the owner's Route 33 save (TID 15613 / 35374, Route 33 path tile (460, 472) facing Down) with slots 2–6 rewritten in place to Aipom with Pickup (ability 53), no held item, levels 15 / 11 / 22 / 36 / 1 and the nicknames, moves and stats of the Pokémon they replaced (one knows Rock Smash); the lead is the owner's Typhlosion Lv 37. `hgss.qa-pickup-held` is the same with Potion, Super Potion, Hyper Potion, Escape Rope and Rare Candy in hand, for the take loop. Both were written by a script that decrypts, edits and re-encrypts the gen 4 party block the way `HgssPokemonParser` reads it (block shuffle by PID, LCRNG keystream seeded by the checksum, party stats seeded by the PID) and fixes the general block's CRC-16; there is no PKHeX on the dev PC. The species swap leaves the stats of the old species until a level-up recalculates them, which does not matter for the roll.

## What this means for the bot

- A pickup mode is the existing fight path plus one new event: train Standard with a target level the lead never reaches already fights every encounter indefinitely (`TargetLevel` 101 passes `BotService`'s `> 0` check and `DecideOverworldAction` never completes it; the dashboard's field caps at 100, so that cap is the one change for "indefinite"). The lead must win: a Lv 37 Typhlosion against Lv 2–6 wilds never took damage worth noting in 40 battles.
- After every won battle, open the Pokémon menu, as a player would. With the party list on screen (a held item shows as an icon on the card), read the party there and take every item a Pickup member holds, then CANCEL. A look costs about 3.3 s (open about 1.5 s, close about 1.8 s), plus about 1.4 s per take, against 30 s per battle. Holding is what costs: each held item removes a 10 % roll from every later battle.
- The take event is a field task like the heal walk: X, POKéMON, then per held slot the four touches above, gated on `app.state`, `partyMonIndex` and the printer, then CANCEL and the field-free wait. The state reader needs one addition: the party menu's `app.state` and `partyMonIndex` behind an `OtherApp` snapshot (or a new screen for it), the way the easy-chat and hatch apps were added for the Primo egg.
- The ability has to be checked, not assumed: Aipom and most Pickup species carry it on a 50/50 slot. The party read has `Ability`; a pickup mode should refuse a party with no Pickup member and report which members count.
- Level picks the loot. For Rare Candy, Lv 41–100 Pickup Pokémon give it on 10 % of hits (Lv 21–40, 4 %); for Leftovers only Lv 81–100 (1 %). Levelling the Aipom is itself possible with the same train mode (switch training).
- Not measured here: the bag's behaviour at 999 of one item (Take then fails?), and whether a Pickup member that faints still rolls (the decomp checks only species, egg and item, so it should).

## Evidence

Run logs and screenshots were kept locally on the dev PC (the probe scripts, the Fight run's backend log with the per-battle item column, the party-menu screenshots at each state). The two saves are in `../shinybothunterqasaves/hgss/`.

Implementation QA ran on the feature tip f72c233 (epic #273). The run folders are under `ShinyBotHunter.Qa/dashboard/qa-runs/` on the dev PC (gitignored):

- `hgss-pickup-greyed`: `20261108-151247-hgss-route33-dashboard`
- `hgss-pickup-take`: `20261108-151341-hgss-route33-dashboard`, one trip of five takes in 11 432 ms
- `hgss-pickup`: `20261108-151447-hgss-route33-dashboard`, 30 won battles and 2 trips (after battles 13 and 30) of five takes each, in 11 125 ms and 11 037 ms

Every trip was under NFR-1's 15 s, about 1 s over the probe's 10.4 s.

The revised build (the menu opened after every won battle, the party read at the list; PRs #290, #291) ran on d6b1a2d, 2026-11-09:

- `hgss-pickup-greyed`: `20261109-072947-hgss-route33-dashboard`
- `hgss-pickup-take`: `20261109-073032-hgss-route33-dashboard`, the first visit took five in 10 407 ms
- `hgss-pickup`: `20261109-073128-hgss-route33-dashboard`, 30 won battles, 31 visits (one at the start), 13 with takes, 19 items; visits from 3.4 s (nothing to take) to 8.1 s (three takes)

So a look costs about 3.5 s per battle, and the farm yielded 19 items in 30 battles against the first build's 10 (two trips at "all held"), because no Aipom sat holding an item for long.
