# Primo's Slugma egg (HGSS)

Status: spike done, 2026-11-02. The design built on it, the [Primo egg hunt spec](../specs/primo-egg-hunt.md) (plan phase 10), is implemented on `feature/primo-egg-hunt` under epic #240 (2026-11-02). Two entries here are corrected from the QA password run, both about the "Is this your answer?" handler: the +0x20 row of the field table and the Confirm row of the pacing table; #260 then found on the emulator the bytes the question writes (+0x28, +0x2C) and rewrote both. #264 traced every press frame by frame and rewrote the +0x1C row, the gate sentence and the pacing table.
Related: [pointer discovery](pointer-discovery.md) (the field script chain), [HGSS state reader spec](../specs/hgss-state-reader.md), the Togepi egg hunt (`HgssTogepiEggEvent`, guide page [Togepi egg](../guide/04-hunts/04-togepi-egg.md))

## What the hunt is

Primo, in the Violet City Pokémon Center, gives a Mareep, Wooper or Slugma egg to a player who enters the right four-word password in two easy-chat prompts. The password depends only on the egg and the player's visible trainer ID. The egg's PID is fixed when he hands it over, so the hunt is the Togepi hunt's shape: soft reset to the save facing Primo, get the egg, walk until it hatches, check the hatched Pokémon, soft reset again unless it is shiny. The spike ran on the owner's `SS-Slugma` save (SoulSilver rev 0, trainer ID 60647, SID 55034, map 158 at (12, 12) facing Primo at (12, 11)).

The owner's save has six Pokémon. Primo's script refuses with a full party (`GetPartyCount == 6` goes to message 20 before the flag is set), so the spike ran on a PKHeX copy, `SS-Slugma-Room`, with the sixth member removed. The bot must refuse to start with a full party, as the Togepi hunt already does (`EnsurePartyHasRoomForEggAsync`).

## The password

### Primo's script

`files/fielddata/script/scr_seq/scr_seq_0860_T22PC0101.s`, `scr_seq_T22PC0101_004` (field script id 5 when running): a message with a Yes/No menu; Yes opens `PromptEasyChat` twice, two words each, into `VAR_SPECIAL_x8000` to `x8003`; then `PrimoPasswordCheck1` (the PC wallpaper passwords) and, when that returns 255, `PrimoPasswordCheck2` (the eggs: 8 Mareep, 9 Wooper, 10 Slugma). On 10: if `FLAG_GOT_SLUGMA_EGG_FROM_PRIMO` (0x15B) is set, "nothing" (message 14); if the party is full, message 20; otherwise set the flag, message 18, `GiveEgg SPECIES_SLUGMA`, a short movement, the egg fanfare, message 19, `WaitButton`, end.

### The rule

Both checks (`ScrCmd_PrimoPasswordCheck1` and `2` in `src/scrcmd_c.c:3580`) call routines still in assembly: `ov02_0224CD38` and `ov02_0224CD74`, which run `ov02_0224CAB8` (wallpapers) and `ov02_0224CBF8` (eggs) in `asm/overlay_02_02248728.s` over a word bank `pms_aikotoba` (`files/resource/eng/pms_aikotoba/pms_aikotoba.s`, 351 easy-chat words: types, abilities and words from the other groups). The egg check:

1. Each of the four words becomes its index in the bank. Any word not in the bank fails.
2. The indexes become four bytes: the first index, then each index minus the one before it, modulo 351. A byte over 255 fails.
3. The four bytes, as one big-endian number, are rotated right by 5 bits (`ov02_0224CA58`).
4. The first three bytes are XORed with `(b3 & 0xF0) | (b3 >> 4)`.
5. The first three bytes are rotated right by `b3 & 0xF` bits.
6. `b0 & 0xF` is the egg (8, 9 or 10; anything else fails), `b0 >> 4` must be 6, `(b1 ^ b0) << 8 | (b2 ^ b0)` must be the visible trainer ID, and `b3` must equal `((b0 + (b1 ^ b0)) * (b2 ^ b0)) & 0xFF`.

The wallpaper check is the same decode with "type below 8" instead, so every egg password fails it and the script falls through to the egg check, as it must. Every step inverts, so the password is computed, not searched: for trainer ID 60647 and Slugma it is **KLUTZ, TEACHER** then **DARK, STEADFAST**. An encoder and a separate line-by-line port of both checks agreed on 3,000 random trainer IDs for all three eggs; every trainer ID has a password for every egg. The game accepted this one first time.

## Entering the words without looking

`PromptEasyChat` opens the easy-chat app, overlay 102. Its data block is `[[[0x021D4158] + 0] + 4] + 0x1C]` (FieldSystem, the running sub-app, its data), and every input can be gated on it:

| Offset | Meaning |
|---|---|
| +0x10, +0x12 | the two chosen words (easy-chat word ids; 0xFFFF when empty) |
| +0x1C | the current screen's step: 0 when it takes input; any other value drops every press. Written on the frame an input is read: the slots A 4, the grid A 1, the list A 3 (each held until the app's graphics tasks end, then +0x20 changes and the step reads 0 on the same frame), R or a scrolling Down or Up 1, the A at Confirm 1 and on the app's next frame 3, held for as long as the question runs at +0x2C (observed on `hgss.qa-primo`, 2026-11-03, #264; `ov102_021E7C84` case 0 reads the slots' input and writes the step, case 1 opens the question) |
| +0x20 | the current screen's handler: 0x021E7AA5 the two slots (kept while the question runs at +0x2C), 0x021E8241 the group grid, 0x021E87B5 a word list; the A on Yes sets 0x021E8D1D, the closing fade, and the app closes about 20 frames later (observed on `hgss.qa-primo`, 2026-11-02, #260) |
| +0x24 | the screen handler +0x20 takes when the current step ends: written with the step by the grid A (0x021E87B5) and the list A (0x021E7AA5); the slots A leaves it (#264) |
| +0x28, +0x2C | a handler that runs instead of +0x20 while +0x2C is not 0, and its step: 0x021E8D61 while the app opens (+0x28 goes 0 to 1, then +0x2C clears 9 to 15 frames later); 0x021E8D95, "Is this your answer?", set 2 to 3 frames after the Confirm A (after +0x1C = 1) with +0x28 = 0, and +0x28 = 1 once its Yes/No reads input, 2 frames later; the Yes A moves +0x28 to 3, with +0x20 = 0x021E8D1D, 19 to 22 frames after the press, because the Yes/No menu (`ov102_021EB130`) answers late (observed on `hgss.qa-primo`, 2026-11-02, #260, and 2026-11-03, #264; the decomp's `EasyChat_Main` and `ov102_021E8D50` in `asm/overlay_102.s` write +0x2C and +0x28 together) |
| +0x4C, +0x4E | slot screen cursor: row (0 the slots, 1 Confirm, 2 Cancel), column (first or second slot) |
| +0x50 | group grid cursor, row × 3 + column; Left and Right wrap within a row; locked "???" groups can be selected |
| +0x54, +0x56, +0x58 | word list scroll (rows), maximum scroll, word count |
| +0x5A, +0x5B | word list column, on-screen row (0 to 4) |
| +0xA4 | 12 × u32: words per grid group (0 when locked) |
| +0xD4 | 12 × u32: each group's start in the word area |
| +0x104 | the word area: every unlocked group's list, alphabetical, back to back |

Grid groups: 0 Pokémon, 1 Pokémon 2, 2 and 3 the two move groups, 4 Status (types and abilities together), 5 Trainer, 6 People, 7 Greetings, 8 Lifestyle, 9 Feelings, 10 Tough words, 11 Union. The in-memory lists leave out locked words (People showed 33 of 38), so the index of a word must come from the app's own list, not from the ROM's full tables (which also exist, at 0x0210774E onward, 0xFFFF-separated).

A word is entered as: slot cursor to the slot, A (to the grid); grid cursor to the group, A (to the list); read the group's list from +0xA4, +0xD4 and +0x104 and find the word's index; move to it with R and L (five rows per press, the on-screen row unchanged), then Down and Up, then Right and Left (index = (scroll + row) × 2 + column); A (back to the slots); read the slot's word back. Each press is sent only while the app takes input (+0x1C = 0 and +0x2C = 0, or the question at +0x2C with +0x28 = 1); a graphics task never ran at such a frame outside the question, and a Yes sent while the question's one task ran landed 4 times in 4. A move is confirmed by its cursor changing, an A by the step it writes (4, 1 or 3, or the screen that follows it), the Confirm A by +0x2C = 0x021E8D95, the Yes A by +0x28 leaving 1 or the app closed; it is pressed again only when that byte has not changed within 1 s. Then the app must take input again on the screen the press leads to (or have closed, after Yes) within 3 s, a wait that never presses (#264). Holds of 2 frames were ignored; 4 frames worked. After a word is chosen the slot cursor stays on that slot, so the second word needs a Right first. Confirm opens "Is this your answer?" with Yes highlighted; A closes the app.

Back in the field, the script's own variables prove what was entered before anything else happens: after the first prompt 0x8000 = 1085 (KLUTZ), 0x8001 = 1337 (TEACHER) and the result 1; after the second 0x8002 = 981 (DARK), 0x8003 = 1062 (STEADFAST). The special variables sit at the script environment + 0x8C (14 × u16; the result, 0x800C, at + 0xA4), from `ScriptEnvironment` in `include/script.h`.

Timing: 11.3 s and 10.6 s for the two prompts (34 and 28 presses); the whole conversation, with its text, took 52 s end to end.

### How tight the pacing can be (2026-11-02, owner's question)

The presses were timed on a gated run, in frames from the press to the gate changing, and to the app taking input again. The #264 probe (`hgss.qa-primo`, 2026-11-03, 15 prompts, 559 presses, none dropped) read each frame's data block, task slots and frame counter in one request; its frames and wall times (about 17 ms a frame) replace the spike's where both exist:

| Press | Its byte, from the press | Taking input again (or closed) |
|---|---|---|
| Down, Up, Left, Right on any screen | the cursor, 1 to 4 frames (11 to 70 ms) | at once; a Down or Up that scrolls the list sets +0x1C = 1 like R |
| R or L (a page of the list) | the scroll and +0x1C = 1, 1 to 4 frames | 14 to 17 frames (299 to 369 ms) |
| A, slots to grid | +0x1C = 4, 1 to 3 frames (14 to 70 ms) | +0x20 = grid, 28 to 30 frames (483 to 525 ms) |
| A, grid to list | +0x1C = 1 and +0x24 = list, 1 to 3 frames | +0x20 = list, 28 to 30 frames (517 to 585 ms) |
| A, list back to the slots | +0x1C = 3, +0x24 = slots and the word, 1 to 3 frames | +0x20 = slots, 43 to 45 frames (750 to 802 ms); once over 90 in the spike |
| A, the first after the app opens | as the slots A; the opening handler at +0x2C clears 18 to 19 frames after the app opens | |
| A, Confirm | +0x1C = 1, 1 to 3 frames; +0x2C = 0x021E8D95 with +0x1C = 3, 3 to 5 frames | +0x28 = 1, 5 to 7 frames (up to 154 ms) |
| A, Yes | nothing for 19 frames; +0x28 = 3 with +0x20 = 0x021E8D1D at 19 to 22 frames (311 to 385 ms) | the app closed at 39 frames (645 to 666 ms) |

A fixed replay of the same presses, as one macro with waits above every measured maximum (12 frames after a move, 30 after a page, 45, 50 and 70 after the three kinds of A, 30 before the first press), navigated the list to the right word every time and still lost the A that leaves the list once in two prompts: that transition took over 90 frames the time it was lost, and the wait was 70. One first A right after the app opened was also dropped on a gated run (the gate pressed again), most likely because the app's opening handler still ran at +0x2C. The first A pressed once it reads 0 landed all five times it was tried: three in the #260 probe on `hgss.qa-primo`, two in the QA password run. So the app's transitions vary by more than a fixed macro can absorb, and a replayed macro is not safe without the gates. With the gates, a prompt is 10.5 to 12.5 s against a floor of about 9.8 s if every press landed at its measured latency, so the gated entry is within about 20 percent of the fastest the game allows. The press sequence itself is identical run to run for a trainer ID, so it can still be cached; the gates decide when each press goes. The presses depend only on the starting cursor and the four words, so the macro for a trainer ID can be cached once built.

## Counting to the hatch

From `HandleDaycareStep` (`src/get_egg.c:854`): every step adds one to the day care's egg step counter (`Daycare.egg_cycles`, +0x1DC), whether or not anything is in the day care. When the counter would reach the cycle length (255; 230 on fifteen listed dates, `sEggCycleSpecialDates`), it resets to 0 and every party egg's remaining cycles (its friendship byte) drops by 1, or by 2 with Flame Body or Magma Armor in the party. An egg already at 0 at a boundary hatches: the field runs `std_hatch_egg` (2021).

Observed: the counter is `[[0x021D4158] + 0x0C] + 0x17E8` (FieldSystem, its `saveData`, then the day care array's offset plus 0x1DC), and it went 254 → 0 on the same step that the egg's cycles went 20 → 19. The egg's cycles are byte 0x0C of block A of its party entry (the friendship field). A Slugma egg starts at 20 (`eggCycles` in `personal.json`), so:

```
steps to the hatch = (255 − counter) + 255 × cycles left
```

About 5,355 steps for a fresh Slugma egg: about 22 minutes at walking pace (16 frames a tile); running or cycling would roughly halve or quarter that, not measured.

## The hatch

`std_hatch_egg` is `scr_seq_0003_021`: "Huh?" message, `WaitABPress`, fade, `EggHatchAnim` (`CallTask_HatchEggInParty`, `src/hatch_egg_task.c`: leave the overworld, the hatch app in overlay 95, clear the egg flag, the naming screen if the player asked for it, restore the overworld), fade back, end.

Observed on the first egg, walking x 488 to 496 on Violet City's street at y 272, just outside the Pokémon Center's door (497, 271):

| Moment | What memory shows |
|---|---|
| Walking | the counter and the egg's cycles count down exactly as the formula says; the last line before the hatch read counter 254, cycles 0, steps left 1 |
| The next step | field script 2021 starts; its context waits in native 0x02041001 (`WaitABPress`, by the script's order) with "Oh?" on screen. Steps from the egg to here: 990 s of walking |
| One A | the field fades and the hatch app opens: overlay 95 |
| About 18 s later | the app waits on "Would you like to nickname the newly hatched SLUGMA?" with Yes highlighted. The hatch app's data (`[[[0x021D4158]] + 4] + 0x1C]`) holds at +0x88 a pointer to the Yes/No menu, whose +0x19 reads 1 on Yes and 2 on No |
| Down, then A on No | the app closes, the overworld reloads, script 2021 ends and the field is free 2.5 s later |
| After | the party slot holds Slugma with the egg flag clear, friendship 120 and the same PID it had as an egg |

So the hatch has a start signal (script 2021) and an end signal (script 2021 gone and the field free, with the slot no longer an egg), and the one question in between is answered from the menu's cursor byte. Nothing is timed. Whether the +0x88 pointer is null before the question appears was not checked: the bot should gate the Down on the pointer being valid and +0x19 reading 1 or 2, and confirm the A by the app closing.

## The loop

After the hatch, the soft reset (`SoftReset`: Start, Select, L and R together), 15 s, and `ContinueGameWithRandom` (A, 120 to 840 frames, A, 240 to 960 frames, A) put the game back at (12, 12) facing Primo with five Pokémon and no egg; both save files were unchanged byte for byte. A second full conversation, run end to end by the gated routine, took 52.2 s (11.8 s and 10.8 s in the prompts, 33 and 29 presses), entered the same four words (read back as 0x8000 to 0x8003) and gave an egg with a different PID (0x2460D20E against 0x01A025BB the first time). Its press sequences matched the first run's apart from one retried A in the first run, so the presses for a trainer ID can be cached once built; the gates stay in place to catch a dropped press.

Two full cycles then ran end to end, unattended, on the gated steps only (`cycle.py`), on the Togepi line:

| Segment | Cycle 1 | Cycle 2 |
|---|---|---|
| Soft reset and continue, back on Primo's tile | 43 s | 40 s |
| Primo, first A to the egg in the party (prompts 11.4 and 10.5 s; 11.2 and 10.6 s) | 51 s | 51 s |
| Route to the line, seven legs, all reached | 14 s | 14 s |
| Pacing until script 2021 | 700 s | 699 s |
| Hatch: A at "Oh?", No at the nickname, field free | 17 s | 17 s |
| Whole cycle | 825 s | 821 s |

No gate timed out and no leg blocked. The egg PIDs differed (0x81052E64, 0xB85C137D) and each hatched Slugma kept its egg's PID. The pacing time is the formula's 5,355 steps at the measured 7.4 steps a second: 11 minutes 40 s both times, to the second. One cycle is 13 minutes 45 s, of which 85 percent is the walk.

## What a bot needs

- **Start checks.** A free party slot (Primo refuses a full party), the Slugma flag 0x15B clear (otherwise he says "nothing"), the save facing Primo at (12, 12) on map 158. The password is computed from the save's visible trainer ID with the rule above; no table, no lookup.
- **Primo.** Press through his text with the navigator's script rules (A at a text arrow, one A at the menu native for Yes), then the two easy-chat prompts with the gated entry, then A through to the end. Confirm the words from 0x8000 to 0x8003 after each prompt and the egg in the party at the end; any mismatch stops the hunt and says which. The first press in each prompt waits for +0x2C to read 0 (the opening handler done); Confirm's A is gated on +0x2C = 0x021E8D95, the Yes A on +0x28 leaving 1 or the app closed, and every press waits for the app to take input (observed on `hgss.qa-primo`, 2026-11-02, #260, and 2026-11-03, #264).
- **The walk.** The owner's choice (2026-11-02) is the Togepi hunt's line and its pacing macro, not the stretch outside the Center the first hatch used. The line is y 258, x 458 to 500: the street just south of the Mart door (469, 257), where `ReceiveTogepiEgg` ends with `H.Down.44`. Observed route from Primo, every leg checked by position: inside the Center down to (12, 19), left to (8, 19), down through the door to (497, 272); then west to x 488 (the street ends there), north to y 264, west to x 469, north to y 258. The line was walked end to end both ways with nothing in the way, and the Togepi hunt's repeating macro (`RP_` of `M.Left.458.<TrainerX>;M.Right.500.<TrainerX>`, `HgssTogepiEggEvent.BuildPaceMacro`) covered 445 steps in 60 s, 7.4 steps a second: about 12 minutes for a fresh Slugma egg. The short stretch outside the Center managed 5.2 steps a second (16.5 minutes), because its legs turned every 8 tiles.
- **Elm's call.** Professor Elm's Pokégear call rings at some point on the walk on a fresh save. Unanswered it runs no script, moves nothing and changes no byte the bot reads; walking, routing and pacing go on through it. It is not an obstacle and nothing dismisses it (owner, 2026-11-02). The first hatch run's failure was the signpost, not the call.
- **The trainer ID** for the password is the save data + 0x84 (u16; the secret ID follows), from `PlayerProfile.id` in `include/player_data.h`. The save data pointer moved between two boots (0x0227C274, then 0x0227C254), so every save read goes through `[[0x021D4158] + 0x0C]`; the step counter's + 0x17E8 held on both boots.
- **Progress.** Steps left from the counter and the egg's cycles, every leg; the dashboard can show it as a countdown.
- **The hatch.** Stop moving when script 2021 starts; A at `WaitABPress`; in overlay 95, Down until the Yes/No cursor reads 2, A; wait for script 2021 to end and the field to be free; read the slot. Replaces the Togepi hunt's stall detection and its hand-timed hatch macro.
- **Shiny or not.** Read the hatched Pokémon from the party, as the Togepi hunt does after its animation. Shiny: Discord alert, the save macro, record the catch, stop (`HgssTogepiEggEvent.SaveAndFinishAsync` already does this). Not shiny: log the encounter, soft reset, continue with random waits, wait for the field, next cycle.

## Open points

- The hatch was observed on the short stretch outside the Center; on the Togepi line the repeating macro was run but not to a hatch. The hatch is a field script wherever it starts, so the signals do not depend on the line.
- Running or the bicycle would shorten the walk; the macros used walking only. A Pokémon with Flame Body or Magma Armor in the party halves the cycles; Slugma itself has one of those abilities once hatched, but the egg does not count.
- Whether the Yes/No pointer at hatch data +0x88 is null before the question (see above).
- The easy-chat app and the hatch app shared the same data address (0x022C027C) in this session because of how the heap fell; the bot always reads it through the app manager, never as a fixed address.
- The spike never ran a cycle with a shiny result; the shiny path is the Togepi hunt's, unchanged.
- One party decode in the spike read a garbled slot once (a read during a write); the bot's party reader retries, as it already does.

## Spike artefacts

In the session scratchpad under `slugma\`: `primo.py` (the encoder and the decoder ports), `easychat.py` (the gated word entry), `live.py` (the live helper), `snapdiff.py` (the byte diffs that found the app's fields), `egg.py` (party decode and the counter search), `pace.py` (the walk to the hatch), `primo_run.py` (one full conversation end to end), `shots\` (the screenshots used only to learn the screens), `saves\` (the original save's backup and the `SS-Slugma-Room` copy). The copy is also installed as `SS-Slugma-Room.nds` and `.SaveRAM`. Nothing was committed from there.
