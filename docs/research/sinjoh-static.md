# Research: the Sinjoh Ruins gift (Dialga, Palkia, Giratina) in HGSS

Date: 2026-11-12. Spike on the QA install (`C:\BizHawk-2.11`, sound off), ROM copy `D:\ROMs\HG-Static.nds`, save `../shinybothunterqasaves/hgss/qa-dpptstatic.sav` copied to `NDS/SaveRAM/HG-Static.SaveRAM`. Spec: [sinjoh-static](../specs/sinjoh-static.md). Decomp: `pret/pokeheartgold`.

## What was proved

1. **The save.** `qa-dpptstatic.sav` is a HeartGold/SoulSilver save (both blocks and both copies pass their CRCs), not Diamond/Pearl/Platinum: trainer Mika (TID 53416, SID 50630), 16 badges, 103 h, Spanish. It loads on the English HeartGold ROM. It stands on the Mystri Stage (map 522) at (16, 14) facing up, with Cynthia present (the first visit) and one party member: an Arceus, level 100, fateful encounter.
2. **The three spots**, from the map's event file and by walking onto each: Dialga (12, 15), Palkia (20, 15), Giratina (16, 8).
3. **The whole attempt, driven from memory, seven times** (Dialga three times, Palkia twice, Giratina twice): soft reset, continue, walk, Yes, cutscene, hand-over, No to the nickname, the dialogue and warp that follow, free field, the gift's summary on screen. About 210 s each.
4. **Each soft reset rerolls.** Six attempts, six personality values, none shiny (shiny values 12798, 4235, 52320, 9133, 44004, 57079). The gift is created with a random personality value at the hand-over, after the cutscene, so it is an ordinary 1 in 8,192.
5. **The party must be the Arceus alone.** The game's script refuses with more than one party member, so "an open slot" is not enough.

## The room, from the decomp

`files/fielddata/eventdata/zone_event/474_D51R0201.json` (map `MAP_SINJOH_RUINS_MYSTRI_STAGE`, 522) has three one-tile coordinate events, live while `VAR_UNK_4104` is 1:

| Tile (x, y) | Script | Sets |
|---|---|---|
| (12, 15) | `scr_seq_D51R0201_004` | Dialga (483), Adamant Orb |
| (20, 15) | `scr_seq_D51R0201_003` | Palkia (484), Lustrous Orb |
| (16, 8) | `scr_seq_D51R0201_005` | Giratina (487), Griseous Orb, Origin form, Levitate |

Cynthia stands at (16, 19) on the first visit; the door is at (16, 30).

The script (`scr_seq_0131_D51R0201.s`), in order:

1. On the spot: if this target was the last one received (`VAR_UNK_4103`), "This place does not seem to respond..." and the script ends.
2. On a repeat visit (`VAR_SCENE_SINJOH_MYSTRI_ROOM` 13 or more) the party is checked: `GetPartyCount` must be 1 ("It seems that only a single ARCEUS can stand here..."), and the lead must be the event Arceus of the right kind ("It seems it no longer responds to this ARCEUS..." when this kind of Arceus has been used). On the first visit (the save's state) Cynthia made those checks when she was spoken to.
3. A Yes/No: "Cynthia: So you choose DIALGA, the guardian of time?" (first visit) or "Will you choose DIALGA?" (repeat). No ends the script.
4. Yes: the cutscene (Unown circle, the player walks to the centre, fade, the egg), then "The mysterious round object took the shape of a Pokémon!" (needs a press), the fanfare, and **`GiveMon`**.
5. A Yes/No: "Would you like to give a nickname to the DIALGA?"
6. First visit only: Cynthia's closing lines. Then a warp to the Ruins of Alph Underground Hall (map 315 on this save), where on the first visit a researcher walks up and talks. Then the field is free.

`GiveMon` (`src/script_pokemon_util.c`) calls `CreateMon(mon, species, 1, 32, FALSE, 0, 0, 0)`: the personality value is two `LCRandom()` calls at that moment (`src/pokemon.c`, `CreateBoxMon`), the trainer IDs are the player's, so it can be shiny, and nothing about it is fixed before the hand-over.

Which Arceus counts (`MonMetadataMatchesEvent`, `src/trainer_memo.c`): a fateful-encounter Arceus (the movie gift), or one met in Diamond, Pearl or Platinum at the Hall of Origin. `VAR_UNK_4102` records which kind was used first; each kind can be used once, so one save yields at most two of the three.

## Measured on the emulator

Reads: FieldSystem pointer at `0x021D4158`; map id at FS − 0x22DA4; party count at FS − 0x23F44, records of 0xEC bytes from FS − 0x23F40; player tile from `0x021DA6F4` and `0x021DA6FC` (16.16 fixed point); facing at FS + 0x1DC4 (0 up, 1 down, 2 left, 3 right); field free when FS + 0x10 is 0 and FS + 0x6C is 1. All are the addresses `HgssPointerService` already has.

Walking, one confirmed tile at a time: a 4-frame hold turns when the facing differs and steps when it does not; an 8-frame hold steps once; a 16-frame hold can step twice. The paths walked from (16, 14):

| Target | Steps | Tiles |
|---|---|---|
| Dialga | Down, Down, Left ×4, Up | (16,15) (16,16) (15,16) (14,16) (13,16) (12,16) (12,15) |
| Palkia | Down, Down, Right ×4, Up | (16,15) (16,16) (17,16) (18,16) (19,16) (20,16) (20,15) |
| Giratina | Up ×6 | (16,13) to (16,8) |

So column 16 from y 8 to 16 and row 16 from x 12 to 20 are walkable, with Arceus following. Other tiles were not tried.

### The pathfinder's map of the stage

The bot's map data (`ShinyBotHunter/Data/HGSS/maps.bin`, `mapreverselookup.json`) has the room: game map 522 is bot map 455 (`D51R0201`), 32 by 32. Its walkable tiles (value 0) form the triangular stage, widening from x 14 to 18 at y 4 down to x 9 to 23 at y 16, with the stairs at (16, 18) below. All three spots are walkable in it. A breadth-first search over those tiles from (16, 14):

| Target | Path |
|---|---|
| Dialga | (16,15) (15,15) (14,15) (13,15) (12,15): five steps |
| Palkia | (16,15) (17,15) (18,15) (19,15) (20,15): five steps |
| Giratina | (16,13) to (16,8): six steps |

The side paths run along row 15, which the spike did not walk (it used row 16); the map says it is open. `Profile.Pathfinding.Pathfind(mapId, x, y, destX, destY, …)` is the call `NavigatorEvent` uses to turn such a search into a walk macro.

Timings (wall clock, emulator at 60 fps), three automated attempts:

| Stage | Palkia | Giratina | Dialga |
|---|---|---|---|
| Soft reset to free field on the stage | 20.6 s | 20.5 s | 22.8 s |
| Walk to the spot | 6.8 s | 5.4 s | 6.9 s |
| Yes to the hand-over (party count 2) | 133.0 s | 130.9 s | 132.5 s |
| Hand-over to free field in the Ruins of Alph | 41.4 s | 41.3 s | 41.4 s |
| Menu, party, summary on screen | 7.8 s | 7.8 s | 7.8 s |
| Total | 212 s | 209 s | 214 s |

The 41 s after the hand-over is scripted scene, not slow pressing. A sixth attempt pressed A every 8 frames instead of every 30 and took 42.7 s: 26.1 s on the stage (the nickname prompt, Cynthia walking up, her lines, two fades) and 15.9 s in the Ruins of Alph (the warp's fade, the researcher walking up, his lines). Both parts play only on a first visit, and every reset returns to the first visit. The summary itself is the 8 s row.

At about 210 s an attempt, 8,192 attempts is about 20 days on one emulator. Several emulators on copies of the save divide that.

The gifts read back: level 1, the orb held (items 135, 136, 112), met location "Sinjoh Ruins" (the summary shows it), Giratina in form 1 with ability 26.

## Input notes

- A one-frame tap (`T.A`) is often not seen in the field: the overworld reads input every other frame, and a loop with an even period can miss every time. An earlier loop of 100 one-frame taps did not advance a text box; 6-frame holds (`H.A.6`) did, every time.
- Both Yes/No prompts take a touch (Yes at (128, 71), No at (128, 119) on the bottom screen) and also the buttons: a fifth attempt answered the first with a 6-frame A and the nickname with a 6-frame B, with the same timings. (A first try with a one-frame B at the nickname prompt did nothing; that was the missed tap above, not the prompt.)
- The party menu route to the summary: X, POKéMON at (44, 77), slot 2 at (195, 34), SUMMARY at (195, 41).

## Driven by the script's state, not by timed presses

The first six attempts confirmed the checkpoints from memory (each step's tile, the party count, the map, the free field) but pressed A on a timer between them. A seventh attempt (Palkia, `gated.py`) sent no timed press: each press was chosen from the field script's innermost waiting native and the text printer's state, the rules `NavigatorEvent.PressThroughScriptAsync` already uses.

| When | What the script waited in | Press |
|---|---|---|
| On the spot, Cynthia's question | `MenuChoiceNative` 0x020477C1 | one A (Yes) |
| The cutscene, about 127 s | no script native; the field task's function is 0x02097CD9, then others | none |
| "…took the shape of a Pokémon!" | `PrintingNative` 0x021EF349, printer at the arrow | one A |
| The nickname question | `MenuChoiceNative` | one B (No) |
| Cynthia's lines, the warp, the researcher's lines | `PrintingNative` at the arrow, 19 times | one A each |

22 presses in all, none missed, none doubled; the hand-over came 134.1 s after the first press and the field was free in the Ruins of Alph 38 s after the hand-over. So the two touch-screen Yes/No prompts sit in the same menu native the nurse's does, and the existing presser's rules cover this event: A at an arrow, the answer at the menu native. The one new thing is the answer order (Yes at the first menu, No at the second), and that the long silence in the cutscene is normal and is bounded by its own timeout.

## The summary screen

The party menu and the summary are both applications in the field's app slot (`[[FS + 0] + 4]`), neither in an overlay (the overlay id at app + 0x0C is 0xFFFFFFFF for both), so the overlay id cannot tell them apart. The first word of the app, its init function, can:

| Screen | app + 0x00 | app + 0x14 (state) |
|---|---|---|
| Party menu, list | 0x02078E31 | 1 |
| Party menu, context menu (Summary, Switch, Item, Quit) | 0x02078E31 | 2 |
| Summary | 0x02088299 | 2 |
| Back at the list after B | 0x02078E31 | 1 |

Read on the English HeartGold ROM; the two function addresses are revision-dependent roots. B from the summary returns to the list. B again empties the app slot, but the field is not free: it returns to the start menu the party was opened from (corrected 2026-11-12; see "Found while building").

## Found while building (2026-11-12)

Measured on the same install during the build, with a 20 ms watcher on the field task and the app slot.

- **The summary's states.** After the SUMMARY touch the app slot shows the party menu in state 33, then the summary's init function in state 0 about 0.5 s after the touch (the screen is black), state 1 at about 1.2 s and state 2 at about 1.5 s, when the summary is drawn. A soft reset pressed in state 0 or 1 is ignored; pressed when state 2 is first seen it takes (3 of 3), with a 3-frame or an 8-frame hold.
- **The start menu.** X in the free field starts the field task `0x0203BEF1` (no script) within about 0.1 s. The 16-bit value at that task's environment + 0x26 (`[[FS + 0x10] + 0x0C] + 0x26`) is its state: 3 idle and taking input, 4 then 5 while an app starts and runs, 0x0F then 0x11 for about 1.5 s after the app closes, then 3. B at the start menu closes it to the free field only at 3 (4 of 4); B at 0x0F or 0x11 does nothing (3 of 3).
- **The scene after the hand-over.** After the warp to map 315 the warp's script (4) ends and the field is free for less than 0.1 s before the researcher's script (8) starts. A poll lands in that moment in most runs. An X sent then arrives with script 8 running and opens nothing.
- **The text boxes.** The scene after the hand-over has 19 boxes. The text printer's state (`TextPrinter + 0x28`) is 2 at an arrow that clears the box and 3 at one that scrolls; after an A at a scroll arrow it is 4 for about 0.3 s, then 0 while the next line prints, and the next arrow can come 0.67 s after the press. The printer's current-character pointer (`TextPrinter + 0x00`) advances with every character and holds at each arrow, so it differs from one box's arrow to the next (for example `0x022A7966` at a state 3 arrow and `0x022A798E` at the state 2 arrow after it).
- **Giratina's form.** The gift is form 1 (Origin Forme) at `data[0x40] >> 3` of the party record, with the Griseous Orb; the summary shows the Origin sprite.
- **The walk.** The pathfinder's macros from (16, 14) facing up are `M.Left.12.35497716;M.Down.15.35497724` (Dialga), `M.Right.20.35497716;M.Down.15.35497724` (Palkia) and `M.Up.8.35497724` (Giratina): along row 14 and one step down for the side circles. Each ended on the circle with its script running in about 0.8 to 1.2 s.
- **The Arceus.** The event Arceus keeps its own OT (MICHINA, ID 02010 / 56468), not the player's.
- **Timings in the bot** (four scenario runs): the save at Run 35 to 38 s; the cutscene 140 to 148 s from Yes to the nickname menu; the scripts after the hand-over 41 to 44 s to the confirmed start menu; the summary 3.0 to 3.5 s from the POKéMON touch; the reset 36 to 43 s to the free field; an attempt about 200 s from its start to its result.
- **The QA install** has BackupSaveram on: every SaveRAM flush moves the previous file over the `.bak`, so after a hunt that saves, the `.bak` holds a saved game. The scenarios restore both from `qa-sinjoh.SaveRAM`.

## The clock

The summary's met date was the PC's date (Nov. 12, 2026): the emulator's clock is real time, so the seed differs on every reset without help. The spike still added a random 0 to 4 s wait before continuing, as `ContinueGameWithRandom` does.

## What this means for the bot

- One event with a target choice; the three targets differ only in the spot and the species.
- Run can check everything it needs from memory: map 522, field free, a party of exactly one Arceus.
- The walk can come from the navigator's pathfinder: its map data holds the stage and reaches all three spots.
- The gift is known only after the dialogue that follows it has been pressed through and the summary is open, per "hunts act as a human would".
- A soft reset goes back to the save, so the save must be the stage itself. The live checks at Run cannot see what was saved; the first reset can.

## Evidence

On the dev PC, in `ShinyBotHunter.Qa/dashboard/qa-runs/20261112-sinjoh-static-spike/` of the spike worktree (not committed): `hg.py` (the probe), `loop.py` (the timed attempts), `gated.py` and `gated1.log` (the attempt driven by the script's state, with every state change and press), `loop*.log` and `runs_*.json`, `shots/` (the prompts, the cutscene, the summaries), and `decomp/` (the files read). The save is also in the saves folder as `qa-sinjoh.SaveRAM`. The ROM copy and `HG-Static.SaveRAM` with its `.bak` are staged in the QA install.

The build's QA runs (2026-11-12, `ShinyBotHunter.Qa/dashboard/qa-runs/` of the main checkout, not committed), on c74e226: `20261112-205409-hgss-route33-dashboard` (refusals), `20261112-205510-hgss-route33-dashboard` (Dialga), `20261112-210406-hgss-route33-dashboard` (Giratina), `20261112-210825-hgss-route33-dashboard` (shiny).
