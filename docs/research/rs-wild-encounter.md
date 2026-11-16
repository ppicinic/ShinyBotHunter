# Research: wild encounters in Ruby and Sapphire (1.2)

Date: 2026-11-15. Spike on the QA install (`C:\BizHawk-2.11`, sound off), on copies of the 1.2 ROMs: `D:\ROMs\SA-Wild.gba` (Sapphire, USA/Europe, Rev 2, sha1 `89b45fb1…`) and `D:\ROMs\RU-Wild.gba` (Ruby, USA/Europe, Rev 2, sha1 `5b64eacf…`). Save: the owner's Sapphire save standing in the Route 101 grass, copied to `../shinybothunterqasaves/rs/qa-route101.SaveRAM` and to `GBA/SaveRAM/SA-Wild.SaveRAM` and `RU-Wild.SaveRAM`. Spec: [rs-wild-encounter](../specs/rs-wild-encounter.md). Decomp: `pret/pokeruby` (sources on `master`, symbol tables on the `symbols` branch).

## What was proved

1. **Speed: 270 encounters an hour on the save as it is** (a shiny lead, no White Flute), which is the target the owner settled on. A lead that is not shiny with a one-letter nickname makes 298, and the White Flute's effect on top of that 311 (11.59 s a cycle over 30). See "Reaching 304 an hour".
2. **Tuned for speed (second pass, same day): 13.3 s an encounter**, 40 more cycles (20 Spin, 20 Run with B held), no press missed. See "Tuned for speed". The numbers in 3 and 4 are the first, slower pass.
3. **The flee cycle, driven from memory, 46 times with no press missed and none doubled.** Sapphire 1.2: 40 encounters in a row. Ruby 1.2 on the same save: 6. Each cycle is: turn in place until a battle starts, one A at "Wild … appeared!", read the wild Pokémon at the action menu, Right, Down, A on RUN, one A at "Got away safely!", wait for the free field. Five presses a battle, each confirmed by the byte it changes.
4. **A cycle takes about 15.4 s**: 3.4 s of turning on average and 12.0 s of battle (11.7 to 12.1 s). 40 encounters gave 40 different personality values.
5. **Turning in place rolls for an encounter**, as a step does. A 4-frame hold of a direction the player is not facing turns without stepping: about 550 turns, the player's tile never changed.
6. **Walking works with the existing `M` macro action**, with no ApiHawk change, by pointing it two bytes before a 16-bit coordinate.
7. **Both presses the cycle makes in text are needed.** "Wild … appeared!" sat for over a minute and "Got away safely!" for 6 s until A was pressed; both strings end in a wait-for-button code.
8. **A failed escape goes back to RUN.** With the lead's Speed written to 1, "Can't escape!" came up twice in one battle and four times in another; each time the menu returned with the cursor on RUN, the bot pressed A again, and it got away. No press missed.
9. **A catch ends where Resume expects.** A Master Ball written into the bag caught a Zigzagoon; the field came back free with the outcome byte at 7 and the same personality value in party slot 2 that was read at the action menu.
10. **A fought battle runs on the same rules**: FIGHT, the first move, four turns, "fainted!", "gained EXP. Points!", back to the field. Level-up, a new move and evolution were not reached.
11. **QA can have a shiny on demand with a BizHawk cheat and no code**: freezing the game's random number to a searched value made the next encounter the predicted shiny, on both ROMs, at the first try.
12. **Every address used is the same in all six English ROMs** (Ruby and Sapphire, 1.0, 1.1 and 1.2) by the decomp's symbol tables. Only 1.2 was run.

One thing about the ROM folder: `D:\ROMs\Pokemon Sapphire.gba` is version 1.0 (header byte `0xBC` is 0, sha1 `3ccbbd45…`). Every other Ruby and Sapphire file there is 1.2. The owner's grass save belongs to that 1.0 file's name in the hunting install; it loads and plays the same on both 1.2 ROMs.

## How the game makes a wild encounter (decomp)

- `GetPlayerFieldInput` (`src/field_control_avatar.c`) sets `checkStandardWildEncounter` whenever the player's tile transition state is "centred on a tile", which is the end of a step and also the end of a turn in place. So turning rolls.
- `CheckStandardWildEncounter` skips the first four checks after a battle (`sWildEncounterImmunitySteps`), then calls `StandardWildEncounter` (`src/wild_encounter.c`).
- `StandardWildEncounter`: on a grass or water tile with a table for the map, `DoWildEncounterTest` passes when `Random() % 2880 < rate × 16` (Route 101's rate is 20, so 320 in 2880, one check in nine; Stench halves it, Illuminate doubles it, the flutes and the Cleanse Tag change it). Moving onto a different kind of tile first has to pass a 60% roll; turning and walking within the grass do not.
- Then the slot (`Random() % 100`), the level, a Repel check, the nature (`Random() % 25`), and `CreateMonWithNature`: `Random32()` again and again until the personality value has that nature. The trainer ID is the player's, so the Pokémon is shiny at the ordinary 1 in 8,192.
- The Pokémon is written to `gEnemyParty[0]` at that moment, in the field, about 1.4 s before the battle screen exists. `BattleSetup_StartWildBattle` then locks the field controls and starts the transition.
- `Random()` is also called once every frame in the VBlank interrupt (`src/main.c`), so the sequence moves with time and nothing here depends on a reset.
- Running (`TryRunFromBattle`, `src/battle_main.c`): it always works when the lead is at least as fast as the wild Pokémon, or has Run Away or a Smoke Ball. Otherwise the chance is `speed × 128 / foe speed + 30 × tries` in 256, and a failure costs the turn.
- The strings (`src/data/battle_strings_en.h`): "Wild … appeared!", "Got away safely!", "Can't escape!", "… fainted!" and "… gained … EXP. Points!" all end in `\p`, wait for a button and clear.

## Memory read by the spike

All on the `System Bus` domain through ApiHawk's memory port. The addresses are RAM symbols, the same in every English Ruby and Sapphire ROM. The two function addresses are ROM code below `0x0803FBDC`, where the six ROMs are still identical (the first symbol that differs between any two of them is there).

| What | Symbol | Address | Read as | Seen |
|---|---|---|---|---|
| Field controls locked | `sLockFieldControls` | `0x030006A4` | u8 | 0 in the free field; 1 from the encounter roll until the fade back in ends, and while a field message is up |
| In a battle | `gMain.inBattle` | `0x03001770 + 0x43D`, bit 1 | u8 | 1 from the battle's setup until the return to the field |
| Callback 1 | `gMain.callback1` | `0x03001770` | u32 | 0 on the title screen and the Continue menu; not 0 in the field and in battle |
| Callback 2 | `gMain.callback2` | `0x03001774` | u32 | `CB2_Overworld` in the field: `0x080543C9` on Sapphire 1.2, `0x080543C5` on Ruby 1.2 (this one differs by ROM; the reads above make it unnecessary) |
| Battle type | `gBattleTypeFlags` | `0x020239F8` | u16 | `0x0004` (wild) in every battle here; `0x0008` is a trainer, `0x0080` the Safari Zone, `0x0400` a roamer |
| Battle outcome | `gBattleOutcome` | `0x02024D26` | u8 | 0 during the battle; 4 after running, 1 after winning (2 lost, 3 drew, 6 the wild Pokémon fled, 7 caught: decomp only); stays until the next battle starts |
| The player's controller function | `gBattlerControllerFuncs[0]` | `0x03004330` | u32 | `HandleInputChooseAction` `0x0802C099` at the action menu; `HandleInputChooseMove` `0x0802C68D` at the move list |
| Action cursor | `gActionSelectionCursor[0]` | `0x02024E60` | u8 | 0 FIGHT, 1 BAG, 2 POKéMON, 3 RUN; 0 at the start of each battle |
| Move cursor | `gMoveSelectionCursor[0]` | `0x02024E64` | u8 | 0 to 3 |
| Battle text window state | the battle `Window` at `0x03004210`, `+ 0x16` | `0x03004226` | u16 | 0 idle, 3 printing, 8 waiting for a button then clearing (`\p`); 5 and 9 are the other button waits (decomp only) |
| The wild Pokémon | `gEnemyParty[0]` | `0x030045C0` | 100 bytes | valid from the encounter roll; decodes with the Gen 3 parser |
| The party | `gPlayerPartyCount`, `gPlayerParty` | `0x03004350`, `0x03004360` | as today | |
| PC boxes | `gPokemonStorage.boxes[14][30]` (`struct PokemonStorage`: `currentBox` u8 at + 0, boxes at + 4) | `0x020300A4` (`gPokemonStorage` `0x020300A0`) | 14 x 30 `BoxPokemon` of 80 bytes, 2,400 bytes a box; box n slot s at `0x020300A4 + (n-1)·0x960 + (s-1)·0x50` | decomp only: not read on the emulator; an empty slot is 80 zero bytes; a catch with six in the party goes to the first empty slot from `currentBox` on (`SendMonToPC`) |
| Player's tile | `gSaveBlock1.pos` | `0x02025734` | s16 x, s16 y | (14, 4) on the save |
| Map | `gSaveBlock1.location` | `0x02025738` | u8 group, u8 number | (0, 16) is Route 101 |
| Player's object | `gPlayerAvatar.objectEventId` | `0x0202E858 + 5` | u8 | 0 |
| Facing | `gObjectEvents[id] + 0x18`, low nibble (0x24 bytes each, from `0x030048A0`) | `0x030048B8` for id 0 | u8 | 1 down, 2 up, 3 left, 4 right |
| Moving | `gPlayerAvatar.runningState`, `.tileTransitionState` | `0x0202E85A`, `0x0202E85B` | u8 | both 0 when standing still |
| Random number | `gRngValue` | `0x03004818` | u32 | for QA only |

`gEnemyPartyCount` (`0x030045B8`) stayed 0 through wild battles, so it says nothing; the Pokémon's own checksum does.

One read takes about 20 ms (one per emulated frame), so a snapshot should be a few block reads, not a dozen single ones.

## What memory shows through one cycle

Sapphire 1.2, times from the encounter roll, averages of 40:

| Time | Lock | In battle | Controller function | Window | What is on screen |
|---|---|---|---|---|---|
| before | 0 | 0 | | 0 | the field, turning |
| 0 s | 1 | 0 | | 0 | the roll passed: the transition (callback 2 is `CB2_OverworldBasic`) |
| 1.4 s | 1 | 1 | | 0 | the battle sets up |
| 5.2 s | 1 | 1 | `CompleteOnInactiveTextPrinter2` | 3 then 8 | "Wild … appeared!" waits. **A** |
| 8.7 s | 1 | 1 | `HandleInputChooseAction`, cursor 0 | 0 | the action menu. **Right** (cursor 1), **Down** (cursor 3), **A** |
| 9.6 s | 1 | 1 | other; outcome 4 | 3 then 8 | "Got away safely!" waits. **A** |
| 11.4 s | 1 | 0 | | 0 | back in the field, fading in |
| 12.0 s | 0 | 0 | | 0 | the field is free |

So the field is free when the lock is 0, the battle bit is 0 and callback 1 is not 0; a battle is the battle bit; the menu is the controller function; a text wait is the window state. No ROM address that differs between the games is needed.

## Presses

| Press | When | Confirmed by |
|---|---|---|
| A direction, 4 frames | the field free and the player not facing it | the facing nibble becoming that direction |
| A, 6 frames | the window state at 8 (or 5, 9) | the window state leaving that value |
| Right, 6 frames | the action menu, cursor bit 0 clear | the cursor byte gaining bit 0 |
| Down, 6 frames | the action menu, cursor bit 1 clear | the cursor byte becoming 3 |
| A, 6 frames | the action menu with the cursor at 3 | the controller function leaving `HandleInputChooseAction` |

230 battle presses and about 550 turns were sent this way; none needed a second try.

## Measurements

| | Sapphire 1.2 (40) | Ruby 1.2 (6) |
|---|---|---|
| Battle, roll to free field | 11.97 s (11.74 to 12.14) | 11.98 s (11.87 to 12.05) |
| Roll to the action menu | 8.66 s | 8.69 s |
| Turns before an encounter | 11.95 on average (4 to 34), four of them the immune ones | |
| Turning, per encounter | 3.44 s (1.36 to 9.53), 0.29 s a turn with the spike's 80 ms polling | |
| Failed escapes | 0 (the Lv 7 lead is faster than the Lv 2 and 3 wild Pokémon) | 0 |
| Presses per battle | 5, every time | 5 |

Seen: Wurmple, Zigzagoon and Poochyena at Lv 2 and 3, no shiny, lowest shiny value 333. At 15.4 s a cycle one emulator makes about 234 encounters an hour, so 8,192 encounters is about 35 hours on one emulator.

The save has text speed Fast and battle scene on. A shiny adds about 1.6 s to the intro (the sparkle).

## Walking instead of turning

The `M` action (`ApiHawk/CommandServer.cs`) holds a direction until a coordinate reaches a value. It reads four bytes at the address and takes the upper half as a signed 16-bit number, because HGSS keeps coordinates as 16.16 fixed point. `gSaveBlock1.pos` is two plain 16-bit numbers, x then y, so:

- y: the address of x, `0x02025734` (decimal 33707828). The upper half of those four bytes is y.
- x: two bytes earlier, `0x02025732` (decimal 33707826). The upper half is x.

A repeating `M.Down.5.33707828;M.Up.3.33707828` walked rows 3 to 5 and back until a battle started after 14 steps (0.27 s a step); `M.Right.15.33707826;M.Left.13.33707826` did the same along a row, 16 steps. Both battles were then fled as above.

## Tuned for speed

Timed in game frames (`gMain.vblankCounter1`, `0x03001790`), 60 to the second.

**What a check costs.** A turn in place is the game's "walk in place fast" movement, 8 frames. A walking step is 16 frames; a running step (B held, the save has the Running Shoes) is 8. So turning and running both roll for an encounter every 8 frames, 7.5 times a second, and walking is half that.

**Spin at the 8-frame floor, every turn confirmed.** The `M` action can watch the facing byte as well as a coordinate. The player's object has facing in the low nibble and movement direction in the high nibble of one byte (`0x030048B8` for object 0), and the next byte is 0, so the four bytes from `0x030048B6` (decimal 50350262) have, in their upper half, 17 facing down, 34 up, 51 left, 68 right. `M.Left.51.50350262` therefore holds Left until the player faces left and lets go on that frame. A repeating `M.Left.51.50350262;M.Right.68.50350262` turned once every 8 to 9 frames, never stepped, and never presses the direction already faced (that step of the macro is already satisfied). 20 encounters: the player stayed on (13, 4).

**A full circle.** The owner asked for Spin to turn a full circle, as the HGSS Spin does: Up, Left, Down, Right, starting with the direction after the one faced, one cached repeating macro per starting direction. With the `M` action that is `M.Up.34.<a>;M.Left.51.<a>;M.Down.17.<a>;M.Right.68.<a>`, rotated. 26 encounters (`circle.py`, `circle20.log`): the turns came in order every time (for example `ULDRULDRU`), 8.4 frames a turn, the player stayed on (14, 4), four macros were cached, battles unchanged at 689 frames. The first two-direction version (Left, Right) is replaced by this one.

Two spins that were tried and are worse:
- A repeating timed macro, `H.Left.2;W.6;H.Right.2;W.6`, also reaches 8 frames a turn but is not confirmed, and it **walked the player off the tile**: it started with Left while the player already faced left, which is a step. Any dropped press does the same. Not used.
- A turn sent and confirmed by the bot over HTTP (send, read facing, read the avatar) cost about 16 frames a turn, because each request takes a frame.

**Run with B held.** `H.B.1` in a macro is a sticky hold: ApiHawk sets it when the macro is parsed and keeps it until `macro_clear`. A repeating `H.B.1;M.Down.<y+2>.33707828;M.Up.<y>.33707828` ran three rows at 8 frames a tile with no pause at the turn-round, and stopped inside the rows every time (20 encounters). A held B is not a new press, so it does nothing at "Wild … appeared!" in the moment before the bot clears the macro.

One hazard: `M` waits for the coordinate to *equal* the destination. If the player is already past it (below row 5 with `M.Down.5`), it holds the direction forever. So the bot checks the tile is inside the range before it starts the walk.

**The Run macro's first frame.** Written as `H.B.1;M.Down.5.<a>;M.Up.3.<a>`, the macro overshot: after a battle that ended with the player on row 5 facing down, the player stepped to row 6 and then ran into a tree for ever, because `M.Down.5` never reads 5 again. The cause is in ApiHawk's stepper: `H.B.1` is an empty step, it is popped on the first frame, and the joypad is then set from the new top step, `M.Down`, before that step's coordinate has ever been checked. One frame of Down while facing down is a step. Written as `M.Down.5.<a>:H.B.1;M.Up.3.<a>` the move is the first step and is checked on the frame it starts: 25 battles, 11 of them ending on an end row, none off the line (`overshoot.py`, `overshoot_fixed.log`). The Spin macro starts with an `M` step and was never affected.

**The battle presses.** The battle reads the buttons every frame, so 2-frame holds are enough: 200 battle presses at 2 frames over the 40 tuned cycles, none dropped. Right and Down go as one macro (`H.Right.2;H.Down.2`) confirmed by the cursor reading 3, then A. Menu to RUN chosen fell from 36 frames to 16.

| | First pass | Tuned Spin (20) | Tuned Run with B (20) |
|---|---|---|---|
| Seeking, per encounter | 3.44 s | 1.74 s (45 to 244 frames) | 1.77 s (45 to 239 frames) |
| Roll to the action menu | 8.66 s | 8.51 s | 8.51 s |
| Menu to RUN chosen | 0.61 s | 0.28 s | 0.27 s |
| Battle, roll to free field | 11.97 s | 11.54 s | 11.55 s |
| Cycle | 15.4 s | 13.28 s | 13.32 s |

At 13.3 s one emulator makes about 270 encounters an hour; 8,192 is about 30 hours. What is left is the game's own animation: 1.4 s of transition, 3.8 s to "appeared!", 3.4 s of send-out, 2.7 s from "Got away safely!" to the free field. Text speed is already Fast, and the battle scene option only removes move animations, which a flee never shows.

## Reaching 304 an hour

The owner's first target was 304 encounters an hour: 11.84 s, or 710 frames, a cycle. After these results the owner set it at 270 with the shiny lead and no flute, which is the first row of the second table. The benchmark is `40Cakes/pokebot-gen3`, whose wiki ("Optimisations - General Tips & Tricks", "Optimisations - Battle Transitions", the stream FAQ) and code (`modules/battle_handler.py`, `modules/modes/spin.py`) were read. Each of its tips was tried on Sapphire 1.2, by writing the change into RAM:

| Tip from pokebot-gen3 | Tried how | Result |
|---|---|---|
| A lead that is not shiny | the lead's trainer ID rewritten in the party record so it is no longer shiny | **73 frames (1.2 s) saved every battle**: the shiny sparkle plays each time the lead is sent out. Battle 692 to 619 frames |
| A lead with a one-character nickname | the nickname written as "A" | 8 frames saved ("Go! A!" prints sooner). Battle 619 to 611 frames |
| White Flute, or a lead with Illuminate | the flute's flag set (`FLAG_SYS_ENC_UP_ITEM`, bit 5 of `0x02026A5D`) | the encounter rate is 1.5 times higher: seeking fell from 111 to 80 frames |
| Battle Scene off | the option bit set | no change for a battle that is fled (11.52 s against 11.53 s) |
| A lead with a short cry | the lead's species rewritten to Taillow | no change: the menu came at frame 437 either way |
| The lead a higher level than the wild Pokémon | already so on this save | the short transition (88 frames in their table); a lead at or below the wild level gets the long one (152) |
| B pressed every frame outside the menu (their battle loop) | a repeating `H.B.2;W.1` | 2 frames saved a battle, so nothing: the confirmed presses cost no speed. Holding B does not speed the text either. A one-frame `T.B` every other frame was never read at all |

30 cycles each, Spin, the tuned presses:

| Lead | Flute | Seeking | Battle | Cycle | An hour |
|---|---|---|---|---|---|
| shiny, "MUDKIP" (the save as it is) | no | 104 frames | 692 | 13.28 s | 270 |
| not shiny, "A" | no | 111 | 615 | 12.10 s | 298 |
| not shiny, "A" | yes | 80 | 615 | **11.59 s** | **311** |

So the target needs two things from the operator's save, not from the bot: a first Pokémon that is not shiny, and a higher encounter rate (the White Flute, or Illuminate, which doubles it). On a route with a higher rate than Route 101's 20 the flute is not needed.

Where the 615 frames of a battle go, from the roll: 277 to "Wild … appeared!" (the transition and the slide-in), 23 for it to print, about 6 for the A, 124 for the lead's send-out, about 14 from the menu to RUN chosen, about 20 for "Got away safely!" to print, about 6 for the A, 133 to the free field. About 26 frames are the bot's; the rest is the game. Seeking is four immune checks plus, on average, the inverse of the rate, at 8 frames a check.

## Videos

`video/` in the evidence folder has two clips made from a frame-exact dump (EmuHawk's `--dump-type=imagesequence`, one image per emulated frame, encoded at 60 fps), three encounters each, with the phase and its frame count under the picture: `spin_shiny.mp4` and `run_shiny.mp4`, both with the save's shiny lead. (Clips with a plain lead were recorded too and not kept; their frame counts are in the table.) Closing EmuHawk while it dumps raises a "Really quit?" dialog that `CloseMainWindow` alone does not answer; Yes finalises and quits.

Frames per phase in those twelve encounters:

| Phase | Whose | Shiny lead | Plain lead "A" |
|---|---|---|---|
| Transition and slide-in, to "appeared!" waiting | game | 301 | 301 |
| A at "appeared!" | bot | 6 | 6 |
| Send-out, to the action menu | game | 206 | 130 |
| Right, Down, A on RUN | bot | 14 | 14 |
| "Got away safely!" prints | game | 23 | 24 |
| A at "Got away safely!" | bot | 6 | 6 |
| Fade back to the free field | game | 136 | 138 |
| Battle | | 692 | 619 |

Spin and Run are the same in every phase; only the luck of the encounter roll differs between clips (seeking ran from 62 to 332 frames in these twelve).

## A failed escape

The lead's Speed in the party record (`gPlayerParty[0] + 94`, two bytes, `0x030043BE`) was written to 1 in the field. The battle copies it, so the Lv 7 lead was slower than the Lv 2 and 3 wild Pokémon. Two battles: two failed tries then away (lead 25 to 22 HP), and four failed tries then away. Each "Can't escape!" waited for A (window state 8), the wild Pokémon moved, the action menu came back with the cursor still 3, and the rule "menu, cursor 3, A" ran again. The stat stays until the Pokémon levels up or is boxed, so QA needs no edited save for this: one write before Run.

## A catch

A Master Ball was written into the first ball slot (`gSaveBlock1 + 0x600`: item 1, quantity 5; `0x02025D34`). From the action menu: Right (cursor 1, BAG), A; in the bag Right once to the Poké Balls pocket, A on the ball, A on USE. Then every battle message waited for A in the usual way ("Gotcha! ZIGZAGOON was caught!", "…data was added to the POKéDEX"), the Pokédex page took two A (a new species only), and "Give a nickname" took B. The field was free 0.9 s later with the outcome byte 7, the party count 2 and slot 2 holding personality value `F778149A`, the one read at the action menu. That is the read Resume makes.

The bag and the Pokédex page are other screens (callback 2 is neither the battle nor the field), so the bot's battle rules do not reach into them; in a real hunt those presses are the operator's, and in QA the scenario's.

## A fought battle

One battle with the first move (`fight.py`, `fight1.log`): A on FIGHT confirmed by the controller function becoming `HandleInputChooseMove`, A on the move confirmed by it leaving; the turn's messages print and clear by themselves; the menu comes back each turn. After the fourth Tackle: "Wild ZIGZAGOON fainted!" and "MUDKIP gained … EXP. Points!" each waited (window state 8) and took one A; the outcome byte became 1; the field was free 39.4 s after the roll. Eleven presses, none missed. The lead did not level up, so the level-up box, a new move and evolution are untested.

## A shiny on demand for QA

The QA way is a BizHawk cheat file armed with the Toggle All Cheats hotkey ([QA automation](../specs/qa-automation.md)). For this game the cheat is one line that freezes `gRngValue`:

```
3004818	67F18FB9	N	System Bus	1	wild shiny: gRngValue	d	h	0	None
```

While it is armed every frame starts from the same value, so the next encounter check that is not immune passes and builds the same Pokémon. `seed.py` finds the value by working back from shiny personality values for the save's trainer IDs (39541 / 17794): a state from which the encounter roll passes, then the slot, level and nature rolls, and the first personality value tried has that nature and is shiny. It found eight in a few seconds. The frozen value is the state one call before the roll, because the frame's own `Random()` in the VBlank interrupt runs first.

Result, on both ROMs: the fifth turn after arming (four immune checks, then the roll) started a battle with personality value `CD631295`, shiny value 1, a Lv 2 Wurmple, exactly as `seed.py` printed. On Ruby the whole QA flow ran: a normal encounter, the cheat armed at its action menu, the battle fled with the cheat on, and the next encounter was the shiny.

Three things the harness needs to know:

- The cheat file is named after the game, not the ROM file: `GBA\Cheats\Pokemon - Sapphire Version (USA, Europe) (Rev 2).cht` and `…\Pokemon - Ruby Version (USA, Europe) (Rev 2).cht`. `SA-Wild.cht` and `RU-Wild.cht` were not loaded.
- The QA install loads cheats disabled (`DisableOnLoad`), so the hotkey arms it. The armed check is `gRngValue` reading the frozen value on several reads in a row; unarmed it is different on every read.
- The hotkey is Ctrl+Shift+F12 and the QA install binds the GBA's Select to Shift. Armed in the free field, the game took a Select press and showed "An item in the BAG can be registered on SELECT", which locks the field until A. Armed at a battle's action menu, nothing happened in the game. So arm (and disarm) during a battle.

The seed belongs to the save's trainer IDs and the route's encounter rate; `seed.py <tid> <sid> [rate × 16]` makes one for another save.

## The save file

After a run with no in-game save, `SA-Wild.SaveRAM` differed from its `.bak` in five bytes at offsets 131076 to 131081. The file is 131,088 bytes: the 128 KB flash and a 16-byte clock footer the emulator rewrites on close. The first 131,072 bytes were identical. A "save unchanged" check for these games compares those.

## Not proved

- The lead fainting, a whiteout, a wild Pokémon that traps (Arena Trap, Shadow Tag, Magnet Pull), a roaming Latios or Latias, the Safari Zone (a different battle controller).
- Field interruptions other than the Select message: Repel wearing off, poison, a trainer.
- Level-up, a new move, evolution.
- The bot's own Resume after a catch (the spike read the party by hand), and the save that follows it.
- Water, caves, and any map other than Route 101 (not needed for this version).
- Versions 1.0 and 1.1. The symbol tables say every address is the same; they were not run.
- The PC box address (decomp only), and a catch with a full party.

## What this means for the bot

- A Ruby and Sapphire state can be read from four values (lock, battle bit, controller function, window state) that do not depend on the game or the version.
- Every press of the flee cycle has a byte that confirms it, so the event needs no timed press at all.
- The wild Pokémon is in memory from the roll, but the bot reads it at the action menu, when it has been on screen since the intro ("hunts act as a human would").
- Spin and Run both work at one encounter check every 8 frames, each press confirmed inside ApiHawk by the `M` action, with nothing new in ApiHawk.
- The HGSS hunt can be reused for everything but the battle (spec, "What is reused, and how"): the press text of Spin and Run fits the macro registry as constants per flavor, because Ruby and Sapphire's addresses are fixed, and the state reader answers in the shared screen values. The existing battle orchestrator cannot be reused as it is: it reads HGSS's battle app and presses the touch screen. The Ruby and Sapphire battle is its own, much smaller, event.
- Training is within reach on the same rules but needs the level-up, new-move and evolution screens mapped first.

## Evidence

On the dev PC, in `ShinyBotHunter.Qa/dashboard/qa-runs/20261115-rs-wild-spike/` of the spike worktree (not committed): `rs.py` (the probe), `enc.py` (turning and the battle snapshot), `cycle.py` (the flee cycle) with `cycle40.log`, `ruby6.log` and the `cycles_*.json` timelines, `flags.py` (the lock and battle bit through a cycle, and the 6 s hold at "Got away safely!"), `runmode.py`, `fight.py` and `fight1.log`, `seed.py`, `shiny.py`, `shiny2.py` and `hotkey.ps1` (the cheat), `frames.py`, `spinrate.py`, `spin2.py` and `fast.py` with `mspin20.log`, `brun20.log` and the `fast_*.json` timelines (the tuned cycle), `catch1.py` and `catch2.py` (the Master Ball), `rec.py` and `mkvideo.py` with `video/` (the two clips), `overshoot.py` and `overshoot_fixed.log`, `circle.py` and `circle20.log` (the full-circle Spin), `tune.py`, `species.py`, `phases.py` and `mash.py` with `tuned30.log`, `flute30.log` and `pokebot/` (the 304 target and the pokebot-gen3 files read), `sav.py` (reads a Gen 3 save file), `shots/` (the grass, the battle, the shiny Wurmple), `decomp/` (the files read). The ROM copies and both SaveRAM files with their `.bak` are staged in the QA install; no cheat file is left there.
