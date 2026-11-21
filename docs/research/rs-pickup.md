# Research: Pickup in Ruby and Sapphire (1.2)

Status: spike done, 2026-11-21; emulator rounds 1 and 2 the same day ([below](#emulator-rounds-2026-11-21)). It is the source for the [RS Pickup spec](../specs/rs-pickup.md), built under epic #485.
Related: [HGSS Pickup research](pickup.md) and [spec](../specs/pickup-mode.md) (the mode this extends), [RS navigation and training research](rs-navigation-and-training.md) (the train mode that wins the battles, the battle party screen's reads), [RS wild encounter research](rs-wild-encounter.md) (the field and battle addresses), AGENTS rule 16.

The spike ran on the dev PC with the backend, the dashboard and one EmuHawk from the QA install (`C:\BizHawk-2.11`, sound off) on monitor one, on its own ROM copies (`SA-Pickup`, Sapphire 1.2; `RU-Pickup`, Ruby 1.2) and its own save, all deleted afterwards. The staged QA saves were not touched. The decomp is the `pret/pokeruby` checkout at commit `5784633`; addresses are from pret's symbol tables for both games at 1.1 and 1.2.

## What was proved

1. **The existing train mode makes Pickup roll.** Wild Encounter, Spin, Train Standard, target level 101, started through the backend on the Route 101 QA save with five Zigzagoon added behind the Mudkip: 31 won battles in about 11 minutes, with a Pokémon Center trip on the way, and afterwards every one of the five Zigzagoon held an item (Nugget, Super Potion, Ultra Ball, PP Up, Super Potion).
2. **The take loop runs from memory alone.** From the free field: START, POKéMON, then per holder the party cursor, A, ITEM, TAKE, A at the message, then B and B back to the free field. Every press was confirmed by the byte it changes, none by time, and none was dropped in five visits. A visit with nothing to take is 148 frames (2.5 s). Each take adds about 100 frames (1.7 s). Five takes: 671 frames (11.2 s).
3. **One set of gates serves both games.** The whole visit ran on Ruby 1.2 with three addresses four bytes lower than Sapphire's, as the symbol tables say. The party menu's own steps are the same addresses in both games at 1.1 and 1.2.
4. **The menu's variants are readable.** A member that knows a field move has that move first in its menu, mail replaces ITEM with MAIL, and a full bag pocket leaves the item held. Each was produced on the emulator and each shows in memory.
5. **Two things the bot has today do not work for Gen 3.** The party read gives the ability slot, not the ability, so the HGSS "ability is 53" check never finds a Pickup member. And the Pickup items window names items and loads sprites by Gen 4 item number.

## The rule, from the decomp

- The roll is `atkE5_pickup` (`src/battle_script_commands.c`), the battle script command `pickup`. It appears once, in `BattleScript_PayDayMoneyAndPickUpItems` (`data/battle_scripts_1.s`), which the won-battle script runs. A fled battle and a lost one do not reach it. **Pickup needs a win.**
- It walks the whole party, not the battlers. A member rolls when its ability is Pickup, it is not an egg, and it holds nothing: `Random() % 10 == 0`, 10 %.
- The ability comes from the species and the ability slot: `ALT_ABILITY ? ability2 : ability1` of `gBaseStats[species]`. Six species have it in Ruby and Sapphire:

| Species (National Dex) | Slot with Pickup |
|---|---|
| Zigzagoon (263), Linoone (264), Meowth (52), Teddiursa (216), Phanpy (231) | slot 0 (their only ability) |
| Aipom (190) | slot 1 (slot 0 is Run Away) |

- On a hit, `Random() % 100` picks from one fixed table. There are no level tiers.

| Item | Gen 3 id | Chance |
|---|---|---|
| Super Potion | 22 | 30 % |
| Full Heal | 23 | 10 % |
| Ultra Ball | 2 | 10 % |
| Rare Candy | 68 | 10 % |
| Full Restore | 19 | 10 % |
| Revive | 24 | 10 % |
| Nugget | 110 | 10 % |
| Protein | 64 | 5 % |
| PP Up | 69 | 4 % |
| King's Rock | 187 | 1 % |

With five empty-handed members, `1 − 0.9^5 = 41 %` of won battles give at least one item. A held item stops that member's roll, so taking the items is what keeps the rate up, as on HGSS.

## The bag

`AddBagItem` (`src/item.c`): an item joins a slot that already holds it up to 99, then starts a new slot. The Items pocket has 20 slots and the Balls pocket 16. Nine of the ten Pickup items go to the Items pocket and the Ultra Ball to the Balls pocket. When there is no room, Take prints "The BAG is full. The POKéMON's item could not be removed." and the item stays held.

Seen on the emulator: with the Items pocket's empty slots filled by RAM writes, Take on a PP Up printed that message, the menu behaved exactly as after a successful take, and the party record still held item 69.

## The party menu, from the decomp and the probe

`src/pokemon_menu.c` and `src/party_menu.c`. The menu is one task whose function says which step it is on.

| Step | What memory shows | Address |
|---|---|---|
| The start menu takes input | a task with function `sub_80712B4` (corrected by round 1: the task alone is two frames early; the start menu takes input once that task's `data[0]` reads 1) | `0x080712D9` Sapphire, `0x080712D5` Ruby (the same at 1.1) |
| Start menu entries and cursor | `sCurrentStartMenuActions`, `sNumStartMenuActions`, `sStartMenuCursorPos`; POKéMON is action 1 | `0x0202E8FE`, `0x0202E8FD`, `0x0202E8FC` (in the table today) |
| The party list takes input | callback 2 is `CB2_PartyMenuMain`, the party task's function is `HandleDefaultPartyMenu`, `gPaletteFade.active` is 0 | `0x0806AF01` / `0x0806AEFD` (in the table); `0x08089CF5` both games; `0x0202F38F` bit 7 (in the table) |
| The party cursor | `data[0]` of the sprite named by the party task's `data[3] >> 8`: the slot, 0 to 5, and 7 on CANCEL (round 1) | `gSprites` `0x02020004`, 0x44 a sprite, `+ 0x2E` (the battle party screen's read) |
| The Pokémon's menu is up | the party task's function is `sub_8089D94` | `0x08089DB5` both games |
| Its entries, in order | `sPokeMenuOptionsNo`, `sPokeMenuOptionsOrder` | `0x0202FFA9`, `0x0202FFAA` |
| Its cursor, and the Item submenu's | `sPokeMenuCursorPos` | `0x0202FFA8` |
| The Item submenu is up (GIVE, TAKE, CANCEL) | the party task's function is `sub_808A060` | `0x0808A081` both games |
| The take message waits for a button | `gPartyMenuMessage_IsPrinting` is 1 and the menu window's state is 5 (3 while it prints) | `0x0202E8F6`; `gMenuWindow` `0x0202E908` `+ 0x16` = `0x0202E91E` |
| Back in the field with the start menu open | callback 2 is `CB2_Overworld`, the start menu's input task is back, the fade is off (corrected by round 1: the input task with its `data[0]` reading 1) | `0x080543C9` / `0x080543C5` |
| The field is free | `sLockFieldControls` is 0 | `0x030006A4` (in the table) |

The entries of a Pokémon's menu are ids: 0 SUMMARY, 1 SWITCH, 2 ITEM, 3 CANCEL, 7 MAIL, 10 and up the field moves. The game builds the list with the field moves the Pokémon knows first, then SUMMARY, SWITCH (only with two or more in the party), ITEM or MAIL, CANCEL. Seen:

- A Zigzagoon with no field move: `[0, 1, 2, 3]`, ITEM at position 2.
- The same with Cut written into its first move: `[10, 0, 1, 2, 3]`, ITEM at position 3.
- The same holding a mail item (121): `[0, 1, 7, 3]`, no ITEM entry.

So the position of ITEM is read, never assumed.

The take message is `Received the {item} from {name}.` and ends in a wait for a button. An A before the window's state reads 5 is not taken.

Leaving: B at the list fades out, the game returns to the field and opens the start menu again on its own, with the cursor still on POKéMON. A second B closes it.

## The loop, as the probe ran it

One read per wait, 2-frame holds, frames by `gMain.vblankCounter1`. The same on Sapphire and Ruby.

| Step | Press | Confirmed by | Frames |
|---|---|---|---|
| Open the start menu | START | the start menu's input task is up (corrected by round 1: the input task with its `data[0]` reading 1) | 10 |
| To POKéMON | Down or Up, one at a time | the start cursor reading the next position | 0 to 6 each (0 presses on every visit after the first) |
| Open the party | A | callback 2 `CB2_PartyMenuMain`, task `HandleDefaultPartyMenu`, fade off | 55 to 58 |
| Read the party | none | the list is on screen | |
| To a holder | Down, one at a time | the party cursor reading the next slot | 4 to 6 each |
| Its menu | A | the task function reading `sub_8089D94` | 4 to 8 |
| To ITEM | Down, one at a time | `sPokeMenuCursorPos` reading the next position | 4 to 6 each |
| The Item submenu | A | the task function reading `sub_808A060` | 4 to 5 |
| To TAKE | Down | `sPokeMenuCursorPos` reading 1 | 4 to 6 |
| Take | A | the message printing and the window's state 5 | 40 to 46 |
| Dismiss | A | printing 0 and the task function back to `HandleDefaultPartyMenu` | 5 to 8 |
| Leave the party | B | callback 2 `CB2_Overworld`, the start menu's input task, fade off (corrected by round 1: the input task with its `data[0]` reading 1) | 51 to 59 |
| Close the start menu | B | the field lock reading 0 | 5 |

| Visit | Frames | Seconds |
|---|---|---|
| Nothing held (Sapphire, Ruby) | 148, 148 | 2.5 |
| Two takes plus a mail holder looked at and left (Sapphire) | 399 | 6.7 |
| Five takes (Ruby) | 671 | 11.2 |
| One take alone | 97 to 108 | 1.7 |

A won battle on Route 101 with the Lv 7 to 11 Mudkip took 872 to 1,557 frames, 1,130 on average (18.8 s), so a look after every battle costs about an eighth of the cycle.

## What the bot has that does not fit

As the spike found the bot. Epic #485 answers each: the profile says who has Pickup and makes the visit (`IsPickupMember`, `CreatePickupVisit`), `RsPickupTakeEvent` is the Ruby and Sapphire visit with Gen 3's mail range, `PickupTally.FromGen3` turns a Gen 3 item number into the tally's Gen 4 one, `HasPickup` is deleted, and the training reader reads sixteen blocks.

- **The ability.** `Gen3PokemonParser` sets `PokemonData.Ability` to the ability slot (0 or 1), because that is what a Gen 3 record stores. `BotService.PickupRefusal` and `PickupTakeEvent` compare it with 53. For Gen 3 the test is the table above: species and slot. The parser already gives the National Dex number.
- **The event.** `PickupTakeEvent` is HGSS's: touches on the bottom screen, the HGSS address table, the HGSS party app's states. Ruby and Sapphire need their own visit; `OrchestratorEvent.DecideOverworldAction` builds the HGSS one directly today.
- **Item numbers.** `PickupTally` names an item from PKHeX's Gen 4 strings by id and the dashboard loads `Items/<id>.png`, where the sprites were saved by Gen 4 number. Gen 3 numbers differ (Rare Candy is 68, Gen 4's is 50; Nugget 110 and 92; King's Rock 187 and 221). The ten Pickup items all exist in Gen 4.
- **Mail.** Gen 3 mail is items 121 to 132; HGSS's check is 137 to 148.
- **The capability.** `HasPickup` is false for RS and the choice is greyed out with "Pickup is not available on Ruby and Sapphire."
- **The reader.** `RsStateReader` with its training reads is at 15 of ApiHawk's 16 blocks. Its tasks block and callback 2 are what the visit needs most. Its two-byte block at `0x0202E892` (the field message window's state) can grow to `0x0202E91F` and then also carries `gPartyMenuMessage_IsPrinting`, the start menu's cursor, count and entries, and the menu window's state. `sPokeMenuCursorPos` and the entry list are one more block of 10 bytes at `0x0202FFA8`.

## The save

`rs.qa-pickup` (`../shinybothunterqasaves/rs/qa-pickup.SaveRAM`, sha256 `55d8f0c866be5ee29480933a25f525a068655133c208281dd60b973635d49848`): the `qa-route101` save after the spike's run, saved in-game on Route 101 grass at (14, 4). The party is the shiny Mudkip at Lv 11 (it learned Water Gun into its free slot on the way) and five Zigzagoon at Lv 2, empty-handed. The five are copies of one wild Zigzagoon's record written into party slots 2 to 6, so they share a personality value; that does not matter for the roll or the menu. It loads on both `SA` and `RU` ROMs (seen on both).

The bag the save already holds (round 2): Nugget x2, Super Potion x2, PP Up x1 and Rare Candy x1. With the Items pocket's empty slots filled, a held PP Up stacks onto the one there and the take succeeds, so a full-pocket scenario needs an item the bag does not hold; round 2 used a Revive (24).

Held items for a take scenario are written into the party records in RAM (decrypt, set the item, fix the checksum, encrypt), which is how the spike made every state after the first run.

## Emulator rounds, 2026-11-21

Two rounds on Sapphire 1.2 and Ruby 1.2 with the `qa-pickup` save, EmuHawk alone, one snapshot per frame, every press a 2-frame hold. They were run for the visit's plan (#487, #488) and its QA (#490). The traces are on the dev PC in the session scratchpad (`rspk/probe/`; round 2's `r2.py`, `sa2_all.jsonl`, `ru2_all.jsonl`).

### Round 1: the gates, frame by frame

**One result changes a gate.** The start menu's input task being listed is two frames early. On the first frame `sub_80712B4` is in the task list (call it Y) its `data[0]` reads 0, and from Y+1 it reads 1. A press whose key lands on Y or Y+1 is dropped; one landing on Y+2 or later is taken. That held for Down, A and B, on the first open and on the reopen, 4 of 4 each way on each ROM. A press posted on the first read that shows the task with `data[0]` at 1 is taken. So the start menu takes input when **the task's `data[0]` reads 1**, not when the task is there. A gate on the task alone would work only through the retry, which AGENTS rule 16 forbids. The tables above are corrected to this.

Confirmed as the spike had it:

1. `gMain.callback1` is non-zero on every frame of the visit on both ROMs: the start menu, the fades, the list, the menus, the message and the way out.
2. `sLockFieldControls` reads 1 from the frame START is seen until the frame the closing B is seen, with no 0 between, and reads 0 on the same frame the input task leaves the list.
3. The field list's cursor is the battle party screen's read unchanged: the task with function `HandleDefaultPartyMenu`, `(u16)data[3] >> 8` names the sprite (12 on both ROMs), and the sprite's `data[0]` is the slot, 0 to 5. **CANCEL reads 7**, not 6: Down from 5 goes to 7, then wraps to 0, and Up from 0 goes to 7. `data[3]` is 0 for the first frames under `CB2_InitPartyMenu` and valid under `CB2_PartyMenuMain`. The list opens with the cursor on slot 0 on every visit.
4. On the reopen no frame has the fade on and the input task up (16 trials): the fade ends two frames before the task is listed.

New for the visit:

5. The take message. On the frame A on TAKE is seen the task function leaves `sub_808A060`, printing reads 1 and the window's state 3. The state reads 3, then 6 and 2 for one frame each, then 3, then 5 from 42 frames after the A. After the A that dismisses it, the state is 2 for one frame, then printing reads 0 with the task function `0x0808A699` for one frame, then `HandleDefaultPartyMenu`. Printing drops one frame before the function returns, so the gate "printing 0 and the function back" holds.
6. During the message the party task runs a function that differs per game (`0x0807AC75` Sapphire, `0x0807AC71` Ruby). Nothing should key on it.
7. Other functions on the party task, both ROMs: `0x0808A121` for 2 frames between A on ITEM and the submenu, and `0x08089E6D` for the 22 frames of the exit fade.
8. The menu window's state blips to 1 for single frames while menus are drawn. It means something only while printing is 1.
9. Callback 2 across the visit (Sapphire / Ruby): `CB2_Overworld` `0x080543C9` / `0x080543C5`; one frame `0x08089A91`; `CB2_InitPartyMenu` `0x0806B485` / `0x0806B481` for 9 frames; `CB2_PartyMenuMain` `0x0806AF01` / `0x0806AEFD`, the fade in for 22 frames, then input. On the way out one frame `0x080546C1` / `0x080546BD`, 7 frames `0x08054635` / `0x08054631`, then `CB2_Overworld` with a fade until two frames before the input task.
10. From the press posted: START to the input task 9 to 10 frames, ready one frame later; A to the list ready 55; B at the list to the start menu back 54; the last B to the field free 1.

### Round 2: the visit's gates through the reader's logic

Each frame's snapshot was derived as `RsStateReader` with its training reads computes it, and every press was posted on the first snapshot matching its gate in the visit's plan (#488): 266 presses on Sapphire, 243 on Ruby. **No press was dropped** on either ROM, on any of the visit's screens, the start menu with the `data[0]` condition included (first open and reopen). No frame gave a snapshot the reader's logic mis-classifies.

What needs care:

1. **A on a party slot.** The snapshot stays the list on that slot for 3 lag frames after the A is seen, then reads the Pokémon's menu with its entries complete. Not the same frame, but far inside the one-second retry window.
2. **A raw party read can catch a record mid-rewrite**, in two places only: under `CB2_InitPartyMenu`, which is never a gate, and on those 3 lag frames. Never on a list snapshot before a press. So the party is read only at a list gate with no press outstanding, through the validated party read.
3. **A full pocket is slower.** Its message waits for a button 58 frames after A on TAKE (30 identical snapshots at most), against 34 to 42 for a take. A member holding nothing ("isn't holding anything") gives the same snapshots, at 34.
4. **The bag the save holds** (under "The save" above): a full-pocket scenario needs an item not in the bag.
5. **The start menu remembers its cursor** across closes (moved to 4, reopened at 4). It is 0 on a fresh boot, and on POKéMON after leaving the party. The entries on this save are `0,1,2,4,5,6,7`, POKéMON at index 1. Up from 0 wraps to the last and Down from the last to 0.

Confirmed:

- From a key seen to the first changed snapshot: 0 frames for every step but A on a slot (3). To the confirming snapshot: START 9; the start cursor 0; A on POKéMON 53 to 54; Down on the list 0; A on a slot 3; Down on the menu 0; A on ITEM 2; Down to TAKE 0; A on TAKE 34 to 58; A at the message 2; B on a menu 0; B on the list 54 to 55; B on the start menu 0.
- The longest run of identical snapshots in any wait was 33 frames (B on the list to the start menu). Every wait was under 60 frames in all.
- After the dismiss and after B on a menu, the party cursor is the slot. The list opens on 0 every time, from its first snapshot.
- The Pokémon menu's first snapshot has its cursor at 0, the fade off and its entries complete; the fade is never on with that menu or the ITEM submenu up.
- The party cursor is the `gPlayerParty` index. The held item reads 0 from the frame A on TAKE is seen; with a full pocket it still reads the item on the first list snapshot after.
- A mail item (121): entries `0,1,7,3`, and B gives the list on that slot on the frame. An egg: entries `0,1,3`. A fainted member: `0,1,2,3`, and TAKE works. Down passes an egg and a fainted slot one press each.
- Snapshots with every field empty on the other-app screen: the 2 frames after A on ITEM, the 1 frame after the dismiss, and the 22 frames of the exit fade. A field-task snapshot with no task: the callback 2 changes, `CB2_InitPartyMenu`, the field's fade in, and the input task's first frame.
- Floors, from the probe with no reader overhead: nothing held 121 frames (2.0 s); one take 182; five takes 426 (7.1 s); two full-pocket attempts 278.

Not run: the field between a won battle and the free field through the training reader. The orchestrator's smoke run covers it.

## Not proved

- Any bot code: the spike and both rounds drove the gates from a probe; round 2 derived the reader's snapshots in the probe.
- Versions 1.1. The symbol tables give the same addresses as 1.2 for everything here; not run.
- A full Balls pocket, and a slot at 99.
- An Aipom (the slot-1 case).
- A mail written by the game: the spike and round 2 wrote the item number only. What that item does to the menu (MAIL in place of ITEM, B back to the list) is proved by round 2, as are a fainted member and an egg.
- Which battle each of the five items came from: the hunt was not interrupted to look.

## Evidence

`ShinyBotHunter.Qa/dashboard/qa-runs/20261121-rs-pickup-spike/` on the dev PC (gitignored): `backend.log` (the 31 battles), the probe scripts (`pk.py`, `visit.py`, `setitem.py`, `mkparty.py`, `p1.py` to `p6.py`, `save.py`), `shots/` (the list, TAKE, the message, the full-bag message), `save/qa-pickup.SaveRAM`. The run wrote its encounters to the dev database under the milestone Route 101 for the game file `SA-Pickup`.
