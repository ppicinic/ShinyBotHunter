# Spec: Primo's Slugma egg hunt (HGSS)

Status: Approved for implementation (owner, 2026-11-02); epic and branch `feature/primo-egg-hunt` to be opened by the orchestrator
Last updated: 2026-11-02
Owner: Phil
Related: [research: the spike](../research/primo-slugma-egg.md) (the source for every value, rule and timing here), [HGSS state reader spec](hgss-state-reader.md) (the field script chain), [headbutt encounter spec](headbutt-encounter.md) (shares one change to the press loop), the Togepi egg hunt (`HgssTogepiEggEvent`, guide page [Togepi egg](../guide/04-hunts/04-togepi-egg.md)), [QA automation](qa-automation.md), [feature workflow](feature-workflow.md)

## Summary

A new HGSS action, **HGSS Primo Egg**, beside **HGSS Togepi Egg**. The save stands facing Primo in the Violet City Pokémon Center. One cycle: talk to Primo, enter the Slugma password in his two easy-chat prompts, take the egg, walk to the Togepi hunt's line outside the Mart and pace it with the Togepi pacing macro until the egg hatches, answer No to the nickname question, read the hatched Slugma. Shiny: Discord alert, save, record the catch, stop. Not shiny: log the encounter, soft reset to the save in front of Primo, go again.

The Togepi hunt moves onto the same hatch handling in the same epic (owner, 2026-11-02): its stall detection, its hand-timed hatch macro and the false-stall retries go, replaced by the step countdown and the hatch script's own start and end signals, through one hatch step both events call.

Every step is gated on memory, as the spike ran it: the password is computed from the trainer ID with the game's own rule, the easy-chat screen is driven from its own cursor bytes and confirmed from Primo's script variables, the hatch is counted down exactly from the day care's step counter and the egg's cycles, and the hatch is started and ended by its field script, not by a stalled position or a timed macro. One cycle is 13 minutes 45 s, measured twice end to end on the gated steps: 40 s soft reset, 51 s at Primo, 14 s to the line, 11 minutes 40 s of pacing, 17 s of hatch. The spike also showed why the easy-chat presses stay gated rather than replayed as a fixed macro: the app's screen transitions vary by more than any fixed wait absorbs, and the gated entry is already within about 20 percent of the game's own latency floor.

## Goals

- G1: the hunt runs from the dashboard like the Togepi hunt, one action with one Milestone field, with no operator action after Run.
- G2: no blind macro decides anything. Every press is confirmed by a memory value changing; every wait ends in the awaited state, cancellation, or a stale-timeout that names what it waited for and what it saw.
- G3: a bad setup is refused at Run with one sentence: wrong place, full party.
- G4: the walk reuses the Togepi hunt's line and its pacing macro (owner's choice, 2026-11-02).
- G5: the shiny path is the Togepi hunt's: alert, save, record, stop.
- G6: the Togepi hunt hatches the same way, and nothing timed or stall-based is left in either egg hunt.

## Non-goals

- Primo's Mareep and Wooper eggs. The rule covers them (egg types 8 and 9), so adding them later is a parameter and two species ids; this epic hunts Slugma only.
- Changing how the Togepi hunt receives its egg or finds its start tile. Only its walk-and-hatch half changes (FR-17).
- Faster walking: running, the bicycle, or a Flame Body Pokémon in the party (which halves the cycles; the egg itself never counts). The guide can suggest the last; the bot does not depend on it.
- Detecting a shiny before the hatch. The egg's PID is fixed when Primo hands it over, but the hunt decides after the hatch, as the Togepi hunt does, because that is what a player sees.
- RS. Primo is HGSS only.

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | Registry entry `HGSS Primo Egg`: `RequiredParameters = ["PhaseName"]`, requires `HasEggs`, `OwnsEmulators: false`, created by `BotService.StartPrimoEggAsync`. | Must | Unit test: the entry is listed for an idle HGSS bot and not for RS. |
| FR-2 | Run refuses, with a 400 and one sentence, before the event starts: a full party (`EnsurePartyHasRoomForEggAsync`, as the Togepi hunt); not standing on map 158 (Violet Pokémon Center 1F) at (12, 12) facing Up ("Stand facing Primo in the Violet City Pokémon Center: map 158, tile (12, 12), facing up."). | Must | Unit tests; QA: `hgss-primo-egg-refusals`. |
| FR-3 | `PrimoPassword.Encode(trainerId, egg)` returns the four easy-chat word ids for Primo's egg password (Slugma is egg 10), by inverting the decode the research doc gives step by step: the type and checksum bytes, the left rotations that undo the game's right rotations, the XOR mask, and the bank indexes from the byte differences modulo the bank size. The bank is a static table of the 351 word ids in `pms_aikotoba` (`files/resource/eng/pms_aikotoba/pms_aikotoba.s`, in its order), commented with that file. A `Decode` port of `ov02_0224CBF8` and `ov02_0224CAB8` lives in the test project only, to check the encoder. | Must | Unit tests: trainer ID 60647 gives KLUTZ, TEACHER, DARK, STEADFAST (1085, 1337, 981, 1062); for 3,000 seeded random trainer IDs and eggs 8 to 10, the egg decode returns the egg and the wallpaper decode fails. |
| FR-4 | The trainer ID is read from the save data in RAM: `[[0x021D4158] + 0x0C] + 0x84` (u16, `PlayerProfile.id`'s visible half). | Must | Unit test with a fake memory; QA: the password lines in `hgss-primo-egg-password`. |
| FR-5 | Primo's conversation: press A at Primo; press through his script (field script 5) with the navigator's rules (A at a text arrow, one A at the menu native for Yes) until the easy-chat app (overlay 102) opens; enter two words (FR-6); A at Confirm and A at "Is this your answer?" (Yes is the default); repeat for the second prompt; then press through to the end of the script. After each prompt, before anything else, the script's special variables must read the intended words (env + 0x8C, `0x8000` to `0x8003`); after the check, `0x800C` classifies the outcome: 255 or 0, the password was refused; 10, the egg was already received (`FLAG_GOT_SLUGMA_EGG_FROM_PRIMO` set); 6, the party is full; 1 to 5, the egg was given. Anything but 1 to 5 stops the hunt with that reason. At the end the party must hold a Slugma egg (species 218, egg flag set); its PID is logged. | Must | Unit test of the classification over scripted variables; QA: `hgss-primo-egg-password`. |
| FR-6 | `HgssEasyChatInput.EnterAsync(slot, word)` drives the easy-chat app from its data block `[[[0x021D4158] + 0] + 4] + 0x1C]` with the research doc's fields: the screen handler at +0x20 (slots 0x021E7AA5, group grid 0x021E8241, word list 0x021E87B5), the slot cursor (+0x4C row, +0x4E column), the grid cursor (+0x50, row × 3 + column), the list (+0x54 scroll, +0x56 max scroll, +0x58 count, +0x5A column, +0x5B row), busy (+0x1C), the group tables (+0xA4 counts, +0xD4 offsets, +0x104 words) and the chosen words (+0x10, +0x12). Steps: slot cursor to the slot, A until the screen is the grid; grid cursor to the word's group (types and abilities: 4, Trainer 5, People 6, Greetings 7, Lifestyle 8, Feelings 9, Tough words 10, Union 11), A until the list; the word's index from the group's in-memory list; R and L to within five rows, then Down and Up, then Right and Left; A until the slots; the slot must hold the word. Each press is a 4-frame hold, confirmed by its cursor or screen value changing within 1.5 s (pressed again at most three times), then the busy byte must read 0. A word missing from its group's list (a locked group) stops with the word's name. | Must | Unit tests over a scripted fake app: the press sequence for KLUTZ (55 in Status), TEACHER (86 in Lifestyle), STEADFAST (111), a missing word, a dropped press. QA: `hgss-primo-egg-password` logs each prompt's presses and time (spike: 33 and 29 presses, under 12 s each). |
| FR-7 | The press sequence for a trainer ID is built once and cached for the run: it depends only on the starting cursors (which every prompt resets) and the four words. It is never replayed as a fixed macro: the gates in FR-6 decide when each press goes (the research doc's pacing measurements show a fixed replay loses presses), and a press that needs a retry is logged. | Must | QA: the press lines are identical across cycles. |
| FR-8 | The walk to the line, from Primo's tile, as move legs each confirmed by position (a leg whose position stops changing for 1.5 s stops the hunt with the tile): down to (12, 19), left to (8, 19), down through the door (Violet City (497, 272)), west to x 488, north to y 264, west to x 469, north to y 258. The legs are the config's `routeToLine` (FR-12), measured in the spike. | Must | QA: `hgss-primo-egg-hatch` logs every leg reached. |
| FR-9 | Pacing: the Togepi hunt's macro as a repeating macro, `RP_` of `M.Left.458.<TrainerX>;M.Right.500.<TrainerX>` (the line x 458 to 500 at y 258; owner's choice). While pacing, log every 30 s the steps left, `(255 − counter) + 255 × cycles left`, from the day care's egg step counter `[[0x021D4158] + 0x0C] + 0x17E8` and the egg's friendship byte. Stop the macro (`macro_clear`) as soon as the state reader reports field script 2021 (`std_hatch_egg`). A stale-timeout fires when the steps left stop going down for 30 s while pacing (blocked line). | Must | QA: steps left counts down to 1, and script 2021 starts on the next step (spike: exact). |
| FR-10 | The hatch: at script 2021's native 0x02041001 (`WaitABPress`), one A, confirmed by the hatch app (overlay 95) opening. In the app, wait until the Yes/No pointer (hatch app data + 0x88) is a valid pointer and its cursor (+0x19) reads 1 or 2; press Down until it reads 2 (No); A, confirmed by the app closing. Then wait until script 2021 has ended and the field is free (stale-timeout 30 s). The party slot that held the egg must now be Slugma with the egg flag clear and the egg's PID. | Must | Unit test over scripted snapshots; QA: `hgss-primo-egg-hatch`. |
| FR-11 | After the hatch: log the hatched Slugma as an encounter (`AddEncounterAsync`: Game HGSS, Milestone, GameFileName). Shiny: `ShinyAlertService.NotifyTargetFoundAsync(…, "hatched")`, the `SaveGame` macro, `FlushSave`, `AddCaughtPokemonAsync`, Complete, as `HgssTogepiEggEvent.SaveAndFinishAsync`. Not shiny: the `SoftReset` macro, the reset delay, `ContinueGameWithRandom`, wait for the field; the player must be back on Primo's tile and facing (FR-2's check), else stop with where it is. | Must | QA: `hgss-primo-egg-hatch` (not shiny), `hgss-primo-egg-shiny`. |
| FR-12 | Config `Configs/Events/PrimoEgg.json`, loaded and validated at the event's construction like `TogepiEgg.json`: `eggSpecies` (218), `egg` (10), `startMapId` (158), `startX` (12), `startY` (12), `startFacing` ("Up"), `routeToLine` (FR-8's legs as `"y:19"`-style entries), `hatchAxis` ("x"), `hatchLow` (458), `hatchHigh` (500), `softResetMacro`, `continueMacro`, `saveMacro`, `softResetDelayMs`, `postContinueDelayMs`, `pacingStaleMs`, `hatchStaleMs`. | Must | Config validation tests. |
| FR-13 | `PokemonData` gains `Friendship` (byte), parsed by `HgssPokemonParser` from the decrypted block A (0x14 in the unshuffled layout); for an egg it is the cycles left. | Must | Parser unit test on a built PK4. |
| FR-14 | New address members in `HgssPointerService`, each with the address table's comment format (symbol or origin, kind, status, scenario): the save data offset (`FieldSystem.saveData`, + 0x0C), the trainer ID (+ 0x84), the egg step counter (+ 0x17E8, `Daycare.egg_cycles`), the special variables (env + 0x8C), the easy-chat fields (FR-6), the hatch Yes/No pointer and cursor (FR-10), the natives and screen handlers the hunt branches on (0x02041001, the three easy-chat handlers), and the script ids (5, 2021). The HGSS state reader spec's read table gains a row for each new read. | Must | Reviewer checklist (state reader spec FR-9). |
| FR-15 | `NavigatorEvent.PressThroughScriptAsync` gains an optional predicate that ends the loop (default `FieldFree`), so Primo's text can be pressed through until the easy-chat app opens. The headbutt spec's FR-7 makes the same change; whichever epic lands first adds it and the other uses it. | Must | The heal and poison callers' tests unchanged. |
| FR-16 | Guide page `docs/guide/04-hunts/06-primo-egg.md` and its line in the guide's table of contents, in the guide's plain-words style: what the save needs (facing Primo in the Violet Pokémon Center, a free party slot, the Slugma egg not yet received on that save), what a cycle does and how long it takes, the walk on the street outside the Mart, the stop on a shiny. | Must | Reviewer check (the guide rule in AGENTS.md). |
| FR-17 | The Togepi hunt uses the shared hatch step (`HgssEggHatch`, below) for its walk-and-hatch half: it paces its corridor with its existing macro until field script 2021 starts, logs the steps left from the counter and the egg's cycles, answers the nickname question from the hatch app's cursor, and waits for the script to end and the field to be free before reading the party. Deleted in the same PR, with nothing kept behind a flag: `PaceUntilStalledAsync`, `PlayHatchAnimationAsync`, the false-stall loop in `WaitForHatchAsync`, the constants `StallPollIntervalMs`, `StallReadsRequired` and `MaxFalseHatchRetries`, `EggHuntConfig.HatchAnimationMacro` and `HatchTimeoutMs` with their validation, the `hatch_egg_animation.yaml` macro, and the class comment's account of stall detection. `EggWaitTimeoutMs`, the receive macro, the start-tile walk and the soft reset stay. The Togepi guide page loses its "Position stalled" entry and gains the steps-left line. | Must | The Togepi event's unit tests updated to the shared step; `grep -rn "Stall\|HatchAnimationMacro\|HatchTimeoutMs" ShinyBotHunter/` empty; QA: `hgss-togepi-egg-hatch`. |
| NFR-1 | A not-shiny cycle under 20 minutes on the dev PC (spike: about 14). | Should | QA timings. |
| NFR-2 | Two consecutive not-shiny cycles with no stall and every gate passing. | Must | QA: `hgss-primo-egg-hatch`. |

## Design

### The cycle

```mermaid
stateDiagram-v2
  [*] --> Check: Run
  Check --> [*]: wrong place or full party (400)
  Check --> Primo: on Primo's tile, room in the party
  Primo --> Prompt1: Yes at his question
  Prompt1 --> Prompt2: 0x8000, 0x8001 are the first two words
  Prompt2 --> Outcome: 0x8002, 0x8003 are the last two words
  Outcome --> [*]: refused, already received, or full (stop with the reason)
  Outcome --> Route: egg in the party
  Route --> Pace: on (469, 258)
  Pace --> Hatch: script 2021 starts
  Hatch --> Read: script 2021 ended, field free
  Read --> Shiny: shiny
  Read --> Reset: not shiny
  Shiny --> [*]: alert, save, record, Complete
  Reset --> Primo: back on Primo's tile
```

### Where it lives

| Piece | Location | Notes |
|---|---|---|
| Password | `Games/Hgss/PrimoPassword.cs` | Pure; the bank table and `Encode`. |
| Easy chat | `Games/Hgss/HgssEasyChatInput.cs` | Takes `IMemoryClient`, `ICommandClient` (for the presses) and the pointer service; knows nothing about Primo. |
| Hatch step | `BotEvents/HgssEggHatch.cs` | One class, two callers: `PaceUntilHatchAsync(pacingMacro)` (run the repeating macro, log steps left, stop on script 2021, stale-timeout when the steps left stop falling) and `HatchAsync(eggPid)` (A at `WaitABPress`, No at the nickname, wait for the script to end and the field to be free, return the party slot by PID). Takes the bot and the pointer service; knows no egg species. |
| Events | `BotEvents/HgssPrimoEggEvent.cs`, `BotEvents/PrimoEggConfig.cs`, `Configs/Events/PrimoEgg.json`; `BotEvents/HgssTogepiEggEvent.cs`, `BotEvents/EggHuntConfig.cs`, `Configs/Events/TogepiEgg.json`, `Macro/Configs/HGSS/hatch_egg_animation.yaml` (deleted) | The Primo event like the Togepi one; the Togepi one shrinks (FR-17). |
| Registry and Run | `Games/EventRegistry.cs`, `Service/BotService.cs` | FR-1, FR-2. |
| Parser | `Pokemon/PokemonData.cs`, `Pokemon/HgssPokemonParser.cs` | FR-13. |
| Addresses | `Service/Pointers/HgssPointerService.cs`, `docs/specs/hgss-state-reader.md` | FR-14. |
| Press loop | `BotEvents/NavigatorEvent.cs` | FR-15. |
| Guide | `docs/guide/04-hunts/06-primo-egg.md`, `docs/guide/README.md` | FR-16. |

### New things

| Thing | Reason |
|---|---|
| `HgssEggHatch` | The hatch step with two real callers (FR-9, FR-10, FR-17); the Togepi event's three private methods and their constants become it. |
| `HGSS Primo Egg` registry entry, `HgssPrimoEggEvent`, `PrimoEggConfig`, `PrimoEgg.json` | The hunt itself, beside the Togepi hunt's three. A separate config class because Primo's hunt has no receive macro and has a route; reusing `EggHuntConfig` would leave fields that mean nothing for one of the two. |
| `PrimoPassword` | The password rule; pure, tested without an emulator. |
| `HgssEasyChatInput` | The only code that drives overlay 102; Primo is its first user, so it is a class with one caller, justified because the easy-chat screen is a boundary of its own (an app with its own state) and is tested through a fake app. |
| `PokemonData.Friendship` | The egg's cycles left. |
| The address members (FR-14) | The address table rule. |
| The predicate parameter (FR-15) | Shared with the headbutt epic. |

No package, project, interface or folder.

## QA

QA runs on the final feature branch through the Electron UI with the existing Playwright driver, screen-recorded, one emulator run at a time. The bot saves in-game only on a shiny, so every scenario except `hgss-primo-egg-shiny` compares the save with its `.bak` afterwards; `hgss-primo-egg-shiny` runs on a throwaway copy of the save and ROM made by the scenario and deleted after it.

| Scenario | Save | What it does | Asserts |
|---|---|---|---|
| `hgss-primo-egg-refusals` | `hgss.qa-primo-full`, then `hgss.qa-primo` after `runMacro` steps off Primo's tile | Run from the UI on each. | The full-party sentence; the wrong-place sentence; no event starts. |
| `hgss-primo-egg-password` | `hgss.qa-primo` | Run; wait for the egg; cancel. | The password lines (KLUTZ, TEACHER, DARK, STEADFAST for trainer ID 60647), the script variables after each prompt, outcome 5 (the party count), a Slugma egg in the party, the press count and time per prompt. |
| `hgss-primo-egg-hatch` | `hgss.qa-primo` | Run; two full cycles; cancel. | Every route leg reached; steps left counting down to 1 and script 2021 on the next step; the nickname answered No; two `encounters` rows with species Slugma for the milestone, with the logged egg PIDs, different from each other; back on Primo's tile after each soft reset; NFR-1 timings. |
| `hgss-togepi-egg-hatch` | `hgss.qa-togepi` | Run the Togepi hunt from the UI; one full cycle; cancel. | The egg received by the existing macro; steps left counting down to 1 and script 2021 on the next step; No at the nickname; one Togepi row; no "stalled" line anywhere in the log; back on the start tile after the soft reset. |
| `hgss-primo-egg-shiny` | a throwaway copy of `hgss.qa-primo` | After the egg is received, the scenario rewrites the egg's PID in party RAM to a shiny PID for trainer ID 60647 and SID 55034 (decrypt, set the PID, recompute the checksum, re-encrypt in the new block order, ApiHawk write), then lets the hunt walk and hatch. | The Discord alert, the `CaughtPokemon` document, the event Complete, the save written on the copy; the copy is deleted afterwards. This proves the shiny path end to end; the PID rewrite is QA tooling, never bot code. |

Saves (added to the QA save table): `hgss.qa-primo` = the owner's `SS-Slugma` with one party member removed by PKHeX (the spike's `SS-Slugma-Room`; SoulSilver rev 0, trainer ID 60647, SID 55034, facing Primo, five Pokémon); `hgss.qa-primo-full` = the owner's `SS-Slugma` as made, six Pokémon.

## Suggested task split

| Task | Scope | Files |
|---|---|---|
| E1 | FR-3: the password and its tests | `Games/Hgss/PrimoPassword.cs`, tests |
| E2 | FR-6, FR-7, FR-14: the easy-chat input and every new address member; tests over a fake app | `Games/Hgss/HgssEasyChatInput.cs`, `Service/Pointers/HgssPointerService.cs`, `docs/specs/hgss-state-reader.md`, tests |
| E3 | FR-13: friendship in the parser | `Pokemon/PokemonData.cs`, `Pokemon/HgssPokemonParser.cs`, tests |
| E4 | FR-9, FR-10, FR-17: `HgssEggHatch`, the Togepi hunt moved onto it with the deletions, its guide page | `BotEvents/HgssEggHatch.cs`, `BotEvents/HgssTogepiEggEvent.cs`, `BotEvents/EggHuntConfig.cs`, `Configs/Events/TogepiEgg.json`, `Macro/Configs/HGSS/hatch_egg_animation.yaml`, `docs/guide/04-hunts/04-togepi-egg.md`, tests |
| E5 | FR-1, FR-2, FR-4, FR-5, FR-8, FR-11, FR-12, FR-15, FR-16: the Primo event, config, registry, Run checks, the press-loop predicate, its guide page | `BotEvents/HgssPrimoEggEvent.cs`, `BotEvents/PrimoEggConfig.cs`, `Configs/Events/PrimoEgg.json`, `Games/EventRegistry.cs`, `Service/BotService.cs`, `BotEvents/NavigatorEvent.cs`, `docs/guide/04-hunts/06-primo-egg.md`, `docs/guide/README.md`, tests |
| E6 | QA: the five scenarios, the PID rewrite helper, the saves and their rows | `ShinyBotHunter.Qa/dashboard/hgss-primo-egg*.spec.ts`, `hgss-togepi-egg-hatch.spec.ts`, `docs/specs/qa-automation.md` |
| E7 | Close-out: architecture sentence, this spec's status, plan notes | `docs/**` |

E1, E2 and E3 run in parallel; E4 after E2 (it needs the step-counter and hatch members); E5 after E2, E3 and E4; E6 and E7 last.

## Open questions

- The owner's `SS-Slugma` save has six Pokémon. The hunt refuses it (Primo would too); the operator deposits one, or QA uses `hgss.qa-primo`.
- Resolved by assumption: the route to the line is fixed legs in the config, measured in the spike, rather than the navigator's pathfinding. The route never changes for this save point, each leg is confirmed by position, and the owner prefers cached macros; the navigator remains the fallback if a leg ever blocks.
- Resolved by assumption: Slugma only, Mareep and Wooper later (non-goals).

## Changelog

- 2026-11-02: first version, from the spike of the same day; walk moved to the Togepi line and the Togepi hunt's migration added at the owner's request; approved.
