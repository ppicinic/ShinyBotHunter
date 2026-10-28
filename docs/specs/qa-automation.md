# QA Automation Spec

Status: Draft, core mechanism proven by spike
Last updated: 2026-10-20
Related: [Improvement plan](../plans/2026-10-cleanup-and-hardening.md), [System architecture](../architecture/system-overview.md), [Engineering principles](../engineering-principles.md), [Pointer discovery spike](../research/pointer-discovery.md), [Decisions](../decisions/global.md)

## Summary

Unit tests cover parsers and arithmetic, but what breaks is the loop between the backend, ApiHawk and a running emulator. This spec adds a QA layer where an agent (a Claude Code session or a script) drives the real app and real emulators through the public API, forces a shiny inside the game so the real code path runs to completion in minutes rather than hours, picks which scenarios to run from what changed, and leaves screenshots, a video and logs as evidence.

Shinies are forced with **BizHawk cheat files**, armed mid-run with BizHawk's **Toggle All Cheats hotkey**. The game then generates a genuine shiny through its own code, the bot sees a real one, and neither `ShinyBotHunter/`, ApiHawk nor the BizHawk fork contains anything for QA. The previous attempt, a `ForceShinyPercent` switch inside `StarterHuntEvent`, was removed in August 2026 because a QA branch in production code can park fake finds in a real hunt; this design cannot.

The October 2026 spike proved the whole loop with the real bot: see [Spike results](#spike-results-2026-10-20).

## Goals

- G1: a scenario like "wild encounters on HGSS, flee a few, then a shiny parks the bot" runs unattended end to end in under 10 minutes. (Spike: under 2 minutes.)
- G2: no QA code or QA hooks in `ShinyBotHunter/`, `ApiHawk/` or the BizHawk fork. QA lives in its own project and in files it writes into the QA emulator install.
- G3: a change to area X runs only the scenarios mapped to X by default. The full set runs on request.
- G4: every run leaves a folder with a log, screenshots at each checkpoint, a video, and the final state, so a passing run is reviewable and a failing one is diagnosable.
- G5: scenarios are plain data plus small C# steps, so adding one does not mean writing a framework.

## Non-goals

- Replacing unit tests. They stay the first line for anything testable without an emulator.
- Testing the Electron UI pixel by pixel. The harness drives the REST API; the dashboard is covered by one smoke scenario.
- Running in GitHub Actions. This needs a Windows box with BizHawk, ROMs and saves. It runs on the dev PC (see the CI decision).

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | A `ShinyBotHunter.Qa` console project runs a named scenario against a backend it starts. | Must | `dotnet run --project ShinyBotHunter.Qa -- run hgss-route33-shiny-park` exits 0 on pass, 1 on fail. |
| FR-2 | The harness can make the next wild encounter, or the next starter, shiny for the bot at position N, by arming a BizHawk cheat with the Toggle All Cheats hotkey. | Must | The backend records the encounter with `IsShiny = true`, computed by the bot from the real TID/SID/PID, and the bot parks. |
| FR-3 | The harness confirms a cheat is armed by reading the patched memory back through ApiHawk's existing read opcode. | Must | A run fails fast if the patch is active before arming or inactive after. |
| FR-4 | The harness captures an emulator screenshot at any checkpoint and records the emulator window for the whole run, using capture from outside the emulator. | Must | PNGs and `run.mp4` in the run folder; frames show the game, not a blank window. |
| FR-5 | `qa select --changed` lists the scenarios mapped to files changed versus `main`. | Must | Editing `PostgreSQLEncounterDao.cs` selects the counts scenarios; editing `NavigatorEvent.cs` selects the navigation scenario. |
| FR-6 | Each run writes `qa-runs/<timestamp>-<scenario>/` with `harness.log`, `backend.log`, screenshots, `run.mp4`, and `result.json`. | Must | |
| FR-7 | The harness starts and tears down its own backend and emulators, so a crashed run leaves no process behind. | Must | After any exit, no `EmuHawk.exe` or backend from the run remains. |
| NFR-1 | A scenario file is under 60 lines and readable without knowing the harness. | Must | |
| NFR-2 | Deployable code (`ShinyBotHunter/`, `ApiHawk/`, `PokemonDashboard/`, the fork) contains no QA code and no cheats. Cheats live only under `ShinyBotHunter.Qa/`, which nothing deployable references. | Must | No deployable project references `ShinyBotHunter.Qa` or contains a `.cht` file. |
| NFR-3 | Run output and saves are never committed. | Must | `qa-runs/` is gitignored; saves live outside the repo; `git status` is clean after a run. |
| NFR-4 | The harness refuses to run unless the database connection is `localhost`. | Must | A non-local connection string aborts before the backend starts. |

## User-facing behaviour

### Running
```
dotnet run --project ShinyBotHunter.Qa -- list
dotnet run --project ShinyBotHunter.Qa -- select --changed          # which scenarios the diff touches
dotnet run --project ShinyBotHunter.Qa -- run hgss-route33-shiny-park [--keep]
dotnet run --project ShinyBotHunter.Qa -- run --changed              # run the selected set
dotnet run --project ShinyBotHunter.Qa -- run --all
```
`Scripts/ci-local.ps1 -Qa` calls `run --changed` after the unit tests. Recording is always on; it costs about 1.5 MB per minute.

### A scenario
Scenarios are YAML under `ShinyBotHunter.Qa/Scenarios/`. Steps are a fixed vocabulary implemented in C#.

```yaml
name: hgss-route33-shiny-park
description: Bot hunts Route 33 left and right, flees normal encounters, then a forced shiny parks it.
areas: [bot-engine, encounter, persistence]
game: HGSS
rom: Pokemon HeartGold                  # handle from /api/getAllGames
save: hgss.qa-route33
cheat: hgss-rev0-wild-shiny
timeoutMinutes: 10
steps:
  - launch-bot: { continueGame: true }
  - assert-cheat: { armed: false }
  - emu-macro: { macro: "M.Down.{y+2}.{yAddr}" }     # path tile -> two tiles into the grass
  - start-action:
      name: Wild Encounter
      args: { PhaseName: Route 33, TargetType: ShinyTarget, EncounterType: Run, xRange: 3 }
  - wait-encounters: { count: 3, isShiny: false }
  - arm-cheat
  - assert-cheat: { armed: true }
  - wait-state: { state: Waiting, timeoutSec: 240 }
  - assert-latest-encounter: { isShiny: true }
  - screenshot: parked
  - cancel-bot
```

### Step vocabulary (initial)
| Step | Does |
|---|---|
| `launch-bot`, `cancel-bot` | `POST /api/startTopLevelAction`, `POST /api/bot/cancelAction` (cancel only if the position still exists) |
| `start-action` | `POST /api/bot/startAction/{pos}`; the harness converts enum names to numbers (see findings) |
| `wait-state` | poll `GET /api/bot/getDashboardState` until the position reports the state, or fail |
| `wait-encounters` | poll the `encounters` table for N new rows in the scenario's phase |
| `emu-macro` | send a macro string straight to the emulator's ApiHawk command port, for positioning the player before the bot takes over |
| `arm-cheat` | focus the EmuHawk window and send the Toggle All Cheats hotkey |
| `assert-cheat` | read the cheat's addresses through ApiHawk and compare to original or patched bytes |
| `screenshot` | emulator PNG through `PrintWindow` (see Evidence) |
| `assert-latest-encounter`, `assert-counts`, `assert-party` | read the `encounters` table, `/api/phaseinfo/...` and `/api/bot/getParty/{pos}` |
| `assert-log-contains` | search the backend's captured stdout |
| `sleep` | last resort, must carry a reason |

## Technical design

### Projects and placement
- `ShinyBotHunter.Qa/` (net10 console). Talks to the backend over HTTP, to the emulator over ApiHawk's existing ports, to Postgres with `psql` or Npgsql, and to Windows for focus, hotkeys and capture. References `ShinyBotHunter` only for DTO types and the parsers.
- `ShinyBotHunter.Qa/Cheats/` holds the cheat definitions, one folder per game and ROM revision (see below). This is the only place cheats exist in the repo.
- `ShinyBotHunter/`, `ApiHawk/`, `PokemonDashboard/`, the fork: no changes, no QA code, no cheats. Decided 2026-10-20. The one allowance is `data-testid` attributes in `PokemonDashboard/` where the dashboard driver has no stable selector; they are a test seam, not QA code.
- Saves are not in the repo. They live in a sibling folder, `../shinybothunterqasaves/` relative to the repo root, and `Scripts/qa-load-save.ps1` copies one into the configured BizHawk install (see [QA save files](#qa-save-files)).
- QA configuration lives in the dev PC's user secrets for the backend (connection string, Discord token, sprite and BizHawk folders). `Startup` reads only `appsettings.json` and user secrets, so an `appsettings.Qa.json` would need a code change; the dev PC is not the streaming PC, so its user secrets are the QA configuration and no code change is needed.

### Shiny injection
A cheat is a short file of RAM writes that BizHawk re-applies every frame. The harness writes the file for the ROM under test before launch, loaded **disabled**, and arms it with the hotkey when the scenario says. Two kinds:

**Wild shiny (code patch).** HeartGold's wild-generation code has a shiny branch the game only uses for scripted encounters such as the Red Gyarados. Two halfword writes in overlay 2 redirect the grass-encounter call to it: the flag load becomes a load of the player ID, and the call targets the shiny generator. Every wild encounter is then a genuine shiny rolled by the game, the RNG stays live, and nothing depends on the lead Pokemon, the route, or timing.

| Game, revision | Address | Original | Patched | Meaning |
|---|---|---|---|---|
| HGSS US rev 0 | `0x02247D90` | `2300` | `682B` | `movs r3,#0` → `ldr r3,[r5]` (player ID from the generator state) |
| HGSS US rev 0 | `0x02247D98` | `FE3F` | `FDBB` | low half of `bl` → retarget `generateWildNonShinyAndAddToParty` to `generateWildShinyAndAddToParty` (`0x02247910`) |

**Starter shiny (RNG freeze).** Freezing the RNG state makes every frame start from one seed, so generation is deterministic regardless of timing. The harness searches offline for a seed whose next two outputs form a shiny PID for the save's TID/SID, and freezes the state to it. HGSS rolls the starter the moment the save loads, so this cheat is loaded enabled rather than armed later.

| Game, revision | RNG state | Formula | PID |
|---|---|---|---|
| HGSS US rev 0 | `0x021D15A8` (32-bit) | `s = s * 0x41C64E6D + 0x6073`, output `s >> 16` | two consecutive outputs, low half first |

The RNG entry point is `0x0201FD44`. Addresses for other games and revisions come from the [pointer spike](../research/pointer-discovery.md); RS is not mapped yet.

**Where cheats live.** Every game, generation and ROM revision needs its own addresses, and some need a different technique (Gen 3 has no overlays; RS starters and wild encounters are generated by different code from HGSS). So cheats are stored per game and revision under the QA project:
```
ShinyBotHunter.Qa/Cheats/
  hgss-usa-rev0/
    wild-shiny.cht        code patch, loaded disabled, armed by hotkey
    starter-shiny.cht     RNG freeze; the seed line is filled per save from its TID/SID
    README.md             how each cheat works, the decomp symbols behind each address, how it was verified
  rs-sapphire-usa-rev2/
    README.md             the Wishmaker seed freeze: the .cht is generated per run by jirachi-seed.ts, installed enabled, removed after
  rs-us-rev1/             added when RS is mapped
```
The harness picks the folder from the ROM's game code and revision (`RomInfoService` already reports both), copies the scenario's cheat into the QA BizHawk install as `NDS\Cheats\<ROM file name>.cht` before launch, and fails with a clear message if no folder matches. Adding a game or revision means adding a folder, verified the way the spike verified HGSS: original bytes match the decomp, one encounter with and one without the cheat.

**Cheat file rules learned the hard way:**
- Location: `<BizHawk>\NDS\Cheats\<ROM file name>.cht` (per system, not the root `Cheats` folder). BizHawk auto-loads it when the ROM opens.
- Format: tab-separated `address  value  compare  domain  enabled  name  size  display  bigEndian  compareType`, domain `ARM9 System Bus`. A malformed line is skipped silently, which is why `assert-cheat` exists.
- **Every code-patch line carries a compare value with compare type `Equal`**, so the write only lands when the original instruction is there. The address range is shared by several overlays, and an unguarded write corrupts whichever overlay is loaded (the battle overlay, in the spike).
- **Use halfword (`w`) writes for Thumb instructions.** A four-byte write wrote the halves in an unexpected order.
- **Turning a cheat off does not restore the original bytes** until the game reloads that overlay. Scenarios arm once and never disarm.
- **BizHawk saves the cheat list on close**, so an armed cheat comes back armed next launch. The QA install has `Cheats.AutoSaveOnClose = false`, and the harness rewrites the file disabled before every run and aborts if the patch is active at launch.

**Arming.** `Toggle All Cheats` is bound to `Ctrl+Shift+F12` in the QA install. The harness brings the EmuHawk window to the foreground and sends the chord with `keybd_event`. BizHawk only takes keyboard input when focused (`AcceptBackgroundInput = false`), and that should stay so: enabling background input would let one keypress arm every emulator on a multi-emulator run.

### QA emulator install
A separate BizHawk install, the fork's release build `2.11.2-savepaths.2`, extracted to `C:\BizHawk-2.11` with ApiHawk deployed by `Scripts/deploy-apihawk.ps1 -BizHawkDir C:\BizHawk-2.11`. Its `config.ini` differs from a hunting install in four keys: `Toggle All Cheats` bound, `Cheats.AutoSaveOnClose` off, Lua console and Lua script autoload off, and ApiHawk trusted. Saves sit in `NDS\SaveRAM` named after the ROM file.

### QA databases
On the dev PC, from the October 2026 setup:
- **Postgres**: portable PostgreSQL 17 with TimescaleDB 2.30.2 in `C:\Tools\pgsql`, data in `C:\Tools\pgsql-data`, database `shinybothunter_qa`. No Windows service and no admin rights needed; it stops on restart and starts with `pg_ctl -D C:\Tools\pgsql-data start`.
- **Mongo**: the local MongoDB service, database `ShinyBotHunter`.
- **Migrations**: `dotnet ef` works through the app's `Main`. EF runs `Startup.MainAsync` up to `WebApplication.Build()` and stops there, so no design-time factory is needed and nothing starts. The harness does not apply migrations; bring the QA database up to date with `dotnet ef database update --project ShinyBotHunter`.
- **Metadata**: `--parsepokeapi` once per machine, after pointing `SpriteBaseDirectory` at an existing folder.
- **Phases**: scenario milestones such as `Route 33` are created in the dashboard's phase editor, which saves them to Mongo. They are not added as JSON files under `Phases/`; those are for the static phases that ship with the app.

### Evidence
Capture is done from outside the emulator; nothing is added to ApiHawk or the fork, and nothing from a run is committed.

- **Screenshots and video use `PrintWindow` with `PW_RENDERFULLCONTENT`.** It sees BizHawk's OpenGL surface even when the window is partly covered. `ffmpeg -f gdigrab` records a blank white window and cannot be used.
- **Recording**: a background thread captures the window at 15 fps and pipes raw BGRA frames into `ffmpeg -f rawvideo ... -c:v libx264`, scaled 2x with nearest-neighbour so the 272-pixel-wide window is readable. ffmpeg is a dev-machine prerequisite, not a repo dependency.
- **Nothing is committed.** `qa-runs/` is gitignored.

Run folder layout:
```
qa-runs/20261020-030019-hgss-route33-shiny-park/
  harness.log             step log with timestamps, encounter rows, cheat reads
  backend.log             backend stdout
  01-launched.png
  01b-in-grass.png
  02-before-arm.png
  03-parked.png
  run.mp4                 whole run, 15 fps
  result.json             pass/fail, duration, failing step, assertion detail
```

### Scenario selection
`ShinyBotHunter.Qa/qa-map.yaml` maps path globs to areas, and each scenario lists its areas:
```yaml
areas:
  persistence:  [ShinyBotHunter/Persistence/**, ShinyBotHunter/Service/PokemonPersistenceService.cs, ShinyBotHunter/Migrations/**]
  bot-engine:   [ShinyBotHunter/Bot/**, ShinyBotHunter/Service/BotService.cs, ShinyBotHunter/Emulators/**]
  encounter:    [ShinyBotHunter/BotEvents/Orchestrator*.cs, ShinyBotHunter/BotEvents/*Encounter*.cs, ShinyBotHunter/BotEvents/Battle*.cs]
  navigation:   [ShinyBotHunter/BotEvents/NavigatorEvent.cs, ShinyBotHunter/Pathfinding/**, ShinyBotHunter/Data/HGSS/**]
  starter-hunt: [ShinyBotHunter/BotEvents/*StarterHunt*.cs, ShinyBotHunter/Configs/Events/RsStarterHunt.json, ShinyBotHunter/Macro/Configs/RS/**]
  egg:          [ShinyBotHunter/BotEvents/HgssTogepiEggEvent.cs, ShinyBotHunter/BotEvents/EggHuntConfig.cs, ShinyBotHunter/Configs/Events/TogepiEgg.json]
  apihawk:      [ApiHawk/**]
  dashboard:    [PokemonDashboard/src/**]
  macros:       [ShinyBotHunter/Macro/**]
```
`select --changed` diffs the working tree and the current branch against `main`, maps files to areas, and returns every scenario sharing an area. A change outside every glob (docs, scripts) selects nothing; `--all` overrides.

### Dashboard driver
`ShinyBotHunter.Qa/dashboard/` is a small Node project with `@playwright/test` as its only dependency. It drives the built Electron dashboard with Playwright's Electron support, using the dashboard's own Electron binary, and attaches to the Admin, Recent Encounters and `HGSS — Route 33` windows by title. The rest of `ShinyBotHunter.Qa` (the console, YAML scenarios, `qa-map.yaml`) does not exist yet.

One scenario, `hgss-route33.spec.ts`, runs the real bot on Route 33 through the UI:
1. It rebuilds the dashboard, copies `Cheats/hgss-usa-rev0/wild-shiny.cht` into the QA install disabled, and starts the backend with `dotnet run` on localhost:5000.
2. It launches the dashboard and subscribes HGSS / Route 33 in Dashboard Config if it is not listed.
3. In Bot Control it runs Launch Bot + Continue for Pokemon HeartGold, then moves the player two tiles into the grass with a macro over ApiHawk's command port.
4. It starts Wild Encounter (Route 33, ShinyTarget, Run, xRange 3), waits for three new encounters on the phase card, and arms the cheat with the hotkey.
5. It waits for Resume on the emulator card, cancels twice (event, then bot), and stops the backend by writing `end` to its stdin.

It asserts that the count rose by at least 3, that Resume appeared, that the card is gone and no EmuHawk remains, that the save equals its `.bak`, and that the backend exited. Selectors are roles and visible text, plus the `data-testid` attributes `phase-encounter-count`, `emulator-card-<pos>`, `top-level-action-<name>` and `param-<name>`.

Run it:
```
cd ShinyBotHunter.Qa/dashboard
npm ci
npx playwright install ffmpeg    # once per machine; Playwright's own ffmpeg, needed for its videos
npx playwright test hgss-route33.spec     # one spec at a time: both write to the same run folder name
npx playwright test hgss-route33-train.spec
npx playwright test hgss-route33-train-replace
npx playwright test hgss-route33-train-free
npx playwright test hgss-route33-evolve.spec
npx playwright test hgss-route33-evolve-learn.spec
npx playwright test hgss-route33-evolve-learn-replace
npx playwright test hgss-route33-evolve-learn-free
npx playwright test hgss-navigate-heal-return
$env:QA_HEAL_ROM = "Pokemon HeartGold6"; npx playwright test hgss-heal-variants   # PowerShell; then again with 7 and 8
npx playwright test hgss-poison-walk
npx playwright test rs-jirachi-setup-refusals
npx playwright test rs-jirachi-soak
npx playwright test rs-jirachi-shiny
npx playwright test rs-jirachi-saves
```
Each run writes `ShinyBotHunter.Qa/dashboard/qa-runs/<yyyyMMdd-HHmmss>-hgss-route33-dashboard/` (gitignored). It holds `steps.log`, `backend.log`, `dashboard-build.log`, a PNG of every window at each step (`01-launched` to `08-bot-cancelled`), `admin.webm` from Playwright, the other windows' videos under `videos/`, and `emulator.mp4` from `record-window.ps1`, at twice the window size, with the console code page in `emulator.mp4.log`. The recorder clears the console input encoding's preamble before starting ffmpeg: under code page 65001 `Process.Start` writes a 3-byte BOM into the redirected stdin, which shifts every raw frame and turns the video red.

A second scenario, `hgss-route33-train.spec.ts`, trains the lead one level through the same UI. It launches `Pokemon HeartGold2` (lead Bulbasaur Lv 14, one experience point short of Lv 15, which offers Take Down), moves into the grass, checks the lead with `GET /api/bot/getParty/0`, and starts Wild Encounter with Route 33, NoTarget, Run, xRange 3, TrainType Standard and TargetLevel 15. It does not read the party while the event runs, because reads fail in the move-learn and evolution menus. It waits up to 10 minutes for the card to go idle (training complete), show Resume, or for a `crashed` line in `backend.log` (a crashed event keeps its game state, so its card still looks busy), then asserts that the event did not crash (`[BotService] Event ... crashed` in `backend.log`), that it did not park, that the lead is Lv 15, and that the encounter count rose. On a timeout it logs the last `[LevelUp]` and `[Orchestrator] State:` lines, cancels the event and fails if the cancel does not complete within 60 s. Teardown is the same as the first scenario's. It needs no cheat, and it needs `Pokemon HeartGold2.SaveRAM.bak` beside the save. Bulbasaur knows four moves, so `LevelUpFlow` asks Discord whether it learns Take Down; the backend is logged into Discord, and the operator clicks **Do Not Learn** in the Discord app by hand. A click that misses the 10 minutes is a rerun, not a pass.

A third scenario, `hgss-route33-evolve.spec.ts`, is the train scenario's flow on `Pokemon HeartGold3`, whose lead is one experience point short of an evolution level with no new move on that level. The save's species and level are not recorded, so the scenario reads the lead with `GET /api/bot/getParty/0`, logs it, and trains to its level plus one. It waits the same 10 minutes, then asserts what the train scenario does plus that the lead's name changed (it evolved) and that `backend.log` holds both `start evolution` and `end evolution`, which `LevelUpFlow.EvolutionInit` logs only when the state reader reported `Evolution`. A nicknamed lead would fail the name assert even after evolving; the logged party shows it. It needs no cheat, and it needs `Pokemon HeartGold3.SaveRAM.bak` beside the save.

A fourth scenario, `hgss-route33-evolve-learn.spec.ts`, is the evolve scenario's flow on `Pokemon HeartGold4`, whose lead is Totodile Lv 20, kept unevolved, one experience point short of Lv 21, with four moves. It checks the lead like the train scenario and trains to Lv 21, where Totodile evolves to Croconaw and Croconaw then offers Ice Fang inside the evolution app. `LevelUpFlow` asks Discord, and the operator clicks **Do Not Learn** in the Discord app by hand; no hook or config answers it, and a click that misses the 10 minutes is a rerun, not a pass. It asserts what the evolve scenario does plus a `will not learn Ice Fang` line in `backend.log`. On a timeout it also logs the last `[GameState]` lines. It needs no cheat, and it needs `Pokemon HeartGold4.SaveRAM.bak` beside the save.

A fifth scenario, `hgss-navigate-heal-return.spec.ts`, follows the train scenario's flow on `Pokemon HeartGold6` (`hgss.qa-heal-lead-fainted`), whose lead Slowpoke is at 0 HP on the Route 33 path tile, so the first decision is the walk to the Azalea Pokémon Center. It records the live map id and tile, checks the lead with `GET /api/bot/getParty/0`, and starts Wild Encounter with Route 33, NoTarget, **Spin**, TrainType Standard and TargetLevel the lead's level plus one, with no grass step; Spin turns in place after the return, so the tile can be read without a race. It waits up to 10 minutes for `Clearing navigation path (arrived back at start)`, a `crashed` line, or Resume, and on a timeout logs the last `[Navigator]`, `[GameState]` and `[Orchestrator] State:` lines. It asserts no crash, no `GameStateTimeoutException`, no park, exactly one `Healing with`, no `Movement blocked` or `abandoning the route`, at least two `[Navigator] Warp to map` lines with `Screen = FieldTask`, that each warp wait's last line is on its destination map with `Screen = FieldFree`, that the live map and tile after the return equal the ones before the run and the `Start tile captured` line, and that every party member is at full HP. It then cancels twice (event, then bot). The reviewer watches `emulator.mp4` for one Yes/No and one heal jingle. It needs no cheat and no Discord click, and it needs `Pokemon HeartGold6.SaveRAM.bak` beside the save.

A sixth scenario, `hgss-heal-variants.spec.ts`, is the fifth scenario's flow run once per heal save, chosen by the `QA_HEAL_ROM` environment variable: `Pokemon HeartGold6` (`hgss.qa-heal-lead-fainted`), `7` (`hgss.qa-heal-slot2-fainted`) or `8` (`hgss.qa-heal-pokerus`). It fails at once when the variable is missing or not one of the three. Each value runs in its own process, so each gets its own run folder, `backend.log` and `emulator.mp4`. The precondition is lead HP 0 on 6, lead HP 15 and slot 2 HP 0 on 7, and lead HP 15 on 8 (Pokérus is not in the API; the log is its evidence). On top of the fifth scenario's asserts it checks that there was exactly one nurse conversation: exactly one `the nurse's script: Yes at the menu native` line, exactly one `[GameState] the nurse's script:` line with `Native = 33847233` (the menu), no `the nurse's script: T.A at native` line after the Yes, a `TaskFunction = 35961745` line (the heal animation) after the Yes, and no `[GameState]` line with `FieldTask` between the loop's `FieldFree` line and `Return path found`. It writes the counts of `T.A` and `T.B` press lines to `steps.log`; they are reported, not asserted. It needs no cheat and no Discord click, and it needs the save's `.bak` beside it.

A seventh scenario, `hgss-poison-walk.spec.ts`, is the fifth scenario's flow on `Pokemon HeartGold5` (`hgss.qa-poison`), whose lead Slowpoke is poisoned at 6 HP. 6 is under a quarter of its max HP, so the bot walks to the Azalea Pokémon Center, and on the way poison takes the lead to 1 HP and `std_survive_poisoning` (script 2003) starts. On top of the fifth scenario's asserts it checks a `Party snapshot` line with Slowpoke poisoned, exactly one `Poison event confirmed` line (Slowpoke, `poison cleared`), a `[GameState] the survive-poisoning script:` line with `Script = 2003` before `arrived at pokemon center`, at least one press line for that script and every one of them `T.A`, that script's `FieldFree` line, a `[Navigator] Segment` line after it and before the Center, and then one Yes in the heal with no A after it. No 2003 line before the Center means the party diff missed the script or the walk was too short; the log says which. It needs no cheat and no Discord click, and it needs `Pokemon HeartGold5.SaveRAM.bak` beside the save.

An eighth scenario, `hgss-route33-evolve-learn-replace.spec.ts`, is the fourth scenario's flow on the same save, `Pokemon HeartGold4`, with the other answer: the operator clicks **Water Gun** in the "Totodile - New Move" message, then **Confirm Choice** (every move button asks for it; the fourth scenario's Do Not Learn does too). The bot touches Yes at evolution state 21 and forgets Water Gun in the summary app's move list. On top of the evolve scenario's asserts it checks that the lead is `Croconaw`, a `[GameState]` line with `Prompt = 23` and `SummaryState = 8` (the move list), a `[LevelUp]` line with `Prompt = 23` and `SummaryState = 9` (the row touch reached the confirm), a line with `Screen = Evolution` and `Prompt = 24`, a `[LevelUp] T.A at prompt` line for each of 26, 28, 30 and 37, `forgot Water Gun and learned Ice Fang`, `moves after the evolution: 423, 99, 44, 184` (read from memory after the scene), and no `will not learn`. It needs no cheat, and it needs `Pokemon HeartGold4.SaveRAM.bak` beside the save. Its name contains the fourth scenario's, so the fourth runs as `hgss-route33-evolve-learn.spec`.

A ninth scenario, `hgss-route33-evolve-learn-free.spec.ts`, is the evolve scenario's flow on `Pokemon HeartGold10` (`hgss.qa-route33-evolve-learn-3`), whose Totodile Lv 20 knows three moves, so Croconaw learns Ice Fang into the free slot with no Discord prompt (`evo.state` 12 → 37 → 38 → 13 → 40). On top of the evolve scenario's asserts it checks that the lead is `Croconaw`, `learned Ice Fang in an open move slot`, `[LevelUp] T.A at prompt 37`, `moves after the evolution: 55, 99, 44, 423`, and no `forgot`, `will not learn` or `asking Discord` line. It needs no cheat and no Discord click, and it needs `Pokemon HeartGold10.SaveRAM.bak` beside the save.

A tenth scenario, `hgss-route33-train-replace.spec.ts`, is the train scenario's flow on the same save, `Pokemon HeartGold2`, with the other answer: the operator clicks **Tackle** in the "Bulbasaur - New Move" message, then **Confirm Choice**. The bot touches Yes at getter 21, then Tackle's slot and Forget at (32,172) as one press on the battle's forget-move screen (getter 23), repeated only while the getter is still 23. On top of the train scenario's asserts it checks `[LevelUp] S.NewMove at prompt 21`, a `[GameState]` line with `Prompt = 23`, `[LevelUp] S.NewMoveOne;W.20;S.32.172.5 at prompt 23 after N press(es)` (N is written to `steps.log`), `forgot Tackle and learned Take Down`, `Bulbasaur moves now: 36, 45, 73, 22` (read from memory after the learn), and no `Prompt = 34` line, `will not learn` or `GameStateTimeoutException`. The reviewer checks `emulator.mp4` for "Bulbasaur forgot how to use Tackle." and "Bulbasaur learned Take Down!". It needs no cheat, and it needs `Pokemon HeartGold2.SaveRAM.bak` beside the save. Its name contains the train scenario's, so that one runs as `hgss-route33-train.spec`.

An eleventh scenario, `hgss-route33-train-free.spec.ts`, is the train scenario's flow on `Pokemon HeartGold9` (`hgss.qa-route33-movelearn-3`), whose Bulbasaur knows three moves, so the game learns Take Down into the free slot in battle with no prompt (getter 15 → 36 → cleared). On top of the train scenario's asserts it checks that the only `[LevelUp] ... at prompt` lines are at 11 and 13, no `asking Discord` line, exactly one `Bulbasaur learned Take Down in an open move slot` or `Bulbasaur already knows Take Down` line (the loop's fresh read can see the move already written at 15), and `Bulbasaur moves now: 33, 45, 73, 36`. It needs no cheat and no Discord click, and it needs `Pokemon HeartGold9.SaveRAM.bak` beside the save.

A twelfth scenario, `rs-jirachi-setup-refusals.spec.ts`, checks the [Wishmaker Jirachi](wishmaker-jirachi.md) setup rules through the UI without launching anything. It fails at once if EmuHawk or Dolphin is running, if `qa-jirachi`, `qa-jirachi-full` or `qa-jirachi-nodex` is missing from `../shinybothunterqasaves/rs/` (found beside the main checkout through `git rev-parse --git-common-dir`, so a worktree finds it too), or if the installed hunt save `GBA\SaveRAM\Pokemon - Sapphire Version (USA, Europe) (Rev 2).SaveRAM` exists and differs from `qa-jirachi`. In Bot Control it reads the `GameName` options of `top-level-action-Wishmaker Jirachi Hunt`, writes them to `steps.log`, and asserts that the rev 2 handle is listed and `Pokemon - Sapphire Version (USA)` (rev 0) is not (FR-4); the soak's `-WISHMKR` copy is rev 2 too, so it may be listed. Then three Starts on the rev 2 ROM and milestone `Jirachi`, with the hunt save deleted, then `qa-jirachi-full` copied in, then `qa-jirachi-nodex` copied in (FR-5): each must show the event's sentence in the form's red message ("start the game in BizHawk and save once", "has a full party", "has no Pokédex yet"), with no `emulator-card-*`, no EmuHawk and no Dolphin, and a screenshot each (`01-dropdown` to `04-no-pokedex`). Its `finally` copies `qa-jirachi` back over the hunt save and logs both hashes. It takes under a minute.

A thirteenth scenario, `rs-jirachi-soak.spec.ts`, runs the hunt for an hour (timeout 90 minutes). It deletes any `{hunt}.SaveRAM.wishmaker.bak`, `{rom}-WISHMKR.SaveRAM` and `{rom}-WISHMKR.SaveRAM.bak` (BizHawk writes it when the result EmuHawk flushes on close) left by an earlier run, installs `qa-jirachi` as the hunt save, checks its hash, and records the first block's highest section counter. It starts `Wishmaker Jirachi Hunt` on the rev 2 ROM and `Jirachi` from Bot Control and expects an emulator card. The scenario records no emulator video, since EmuHawk and Dolphin restart every cycle; a recording, when one is wanted, is made ad hoc beside the run. The dashboard gets a screenshot every 15 minutes. Every 30 s it fails on a `[BotService] Event '…' crashed` line and logs the row count and the newest `[Wishmaker] predicted` line. After the hour it waits for the next `Bonus Disc: on the Jirachi entry` line, checks that the hunt save's first-block seed is the last `[Wishmaker] Seed` line's, that Dolphin runs and EmuHawk does not, then cancels from the card (FR-19). It asserts that the card goes and both emulators are gone within 60 s, that the hunt save is byte-identical to the snapshot with a complete live block, and that no cycle finished after the cancel. After the documented shutdown it reads `encounters` with `psql` (`C:\Tools\pgsql\bin\psql.exe`, connection taken from the backend's `ConnectionStrings:DefaultConnection` user secret and never logged) and asserts at least 20 Jirachi rows for `Jirachi` since the Start, all PIDs distinct, every `predicted X got Y` line with X = Y and their PIDs equal to the rows in order, at least as many `in the field after`, `Bonus Disc: power prompt` and `[Wishmaker] summary on view` lines as rows, no gap over 300 s from the Start click to the first row or between rows and no `over the 300 s budget` line, exactly one `Backed up the hunt save` line with the `.wishmaker.bak` equal to `qa-jirachi`, a first-block counter rise between the row count and 2 × (rows + 1), and that `{rom}-WISHMKR.SaveRAM` exists. Its `finally` kills any stray EmuHawk or Dolphin, copies the end-state hunt save, the `.wishmaker.bak` and the `-WISHMKR.SaveRAM` into the run folder, and copies `qa-jirachi` back with both hashes logged. Fewer than 20 rows after exactly one hour fails the run; 18 or 19 with no stall is a rerun, not a bug.

A fourteenth scenario, `rs-jirachi-shiny.spec.ts`, runs one real cycle to a shiny, through REST rather than the UI (timeout 30 minutes).
- **Setup.** It deletes any `.wishmaker.bak`, `-WISHMKR.SaveRAM`, `-WISHMKR.SaveRAM.bak` and `GBA\Cheats\{rom}.cht` left by an earlier run. `jirachi-seed.ts` crafts a copy of `qa-jirachi` whose live section 0 has checksum `ECDD`: play time hours, searched upward from the save's own, plus minutes, seconds and frames. The scenario asserts that the crafted block is valid, installs it as the hunt save, and installs the generated cheat enabled (see `Cheats/rs-sapphire-usa-rev2/README.md`). It counts the matching `CaughtPokemon` documents with `mongosh`.
- **Start and the cheat check.** It starts the backend and POSTs `startTopLevelAction` with `Wishmaker Jirachi Hunt`, the rev 2 ROM and `Jirachi`, expecting position 0. It then checks the cheat: once `gSaveBlock2` (`0x02024EA4`) reads "Phil" and TID 39541, two reads 1.5 s apart must both return the crafted fields.
- **Hold point.** If the first `[Wishmaker] Seed` line is not `ECDD`, the run stops and reports the seed, the crafted fields, the last RAM read and the hunt save's first-block fields. There is no second cycle.
- **Asserts.** On `ECDD` it waits for the park and asserts:
  - the log order Seed, `Bonus Disc: power prompt`, `summary on view`, `predicted 987ED636 got 987ED636 SHINY`, then the park line;
  - `Waiting` in `getDashboardState`;
  - exactly one EmuHawk, on `{rom}-WISHMKR.gba`, and no Dolphin (a screenshot of the park);
  - the result save holds 2 party members, with slot 2 being PID `987ED636` and OT 20043/0;
  - the hunt save holds 1 party member, and its first block has seed `ECDD`;
  - one new `CaughtPokemon` (polled, since the insert is queued), species 385 and shiny;
  - the shiny channel's latest Discord message is the `Shiny Jirachi` alert, sent after the Start;
  - a single Cancel leaves no event and no emulator;
  - after the documented shutdown, exactly one `encounters` row since the Start: 385, `Jirachi`, the rev 2 ROM, `987ED636`, shiny.
- **Teardown.** Its `finally` kills the backend first, then any emulator, and removes the cheat. It keeps the end-state saves in the run folder, deletes the bot's artefacts, and restores `qa-jirachi` with its hash asserted.

The Jirachi scenarios write to the same `-hgss-route33-dashboard` run folder name as the others, because `driver.ts` fixes it.

Preconditions:
- Postgres is started, the Route 33 milestone exists in Mongo, and the backend's user secrets point at both on localhost and hold the Discord token. The train, train-replace, evolve-learn and evolve-learn-replace scenarios need someone to answer the move prompt in Discord.
- `PokemonDashboard/override-config.json` sets `spritesRootPath`.
- The QA BizHawk install is in `C:\BizHawk-2.11` with the Route 33 save and its `.bak` in `NDS\SaveRAM`.
- Playwright's ffmpeg is installed (`npx playwright install ffmpeg`). Without it, launching with video recording leaves every window unanswered and the scenario hangs until its timeout.
- ffmpeg is on PATH (for `record-window.ps1`), the desktop is unlocked, and nothing else holds port 5000 or runs EmuHawk. The emulator must keep focus while the hotkey is sent.
- For the Jirachi scenarios: `Pokemon - Sapphire Version (USA, Europe) (Rev 2).gba` and `Pokemon - Sapphire Version (USA).gba` are in the backend's `ROMBaseDirectory` (`C:\ROMs`), and the soak's `-WISHMKR.gba` copy there is expected; `../shinybothunterqasaves/rs/` holds the three Jirachi saves; the RS milestone `Jirachi` exists in Mongo, made in the Milestone Editor (never as a `Phases/*.json`); the Dolphin user folder is deployed with `Scripts/deploy-dolphin.ps1` and the Dolphin, BIOS and Bonus Disc settings are in the user secrets; Postgres is started; no EmuHawk or Dolphin is running, and for the soak nothing else uses the machine's emulators for the hour. For the shiny scenario, three more things must be in place. The user secrets need the Discord token, and the bot needs read access to the shiny channel (`ShinyLogChannel`). `SpriteBaseDirectory` must end in a slash and hold `Shiny/jirachi.png`. `mongosh` must be under `%LOCALAPPDATA%\Programs\mongosh`.

### Initial scenario set
| Scenario | Areas | Cheat |
|---|---|---|
| `hgss-route33-shiny-park` | encounter, bot-engine, persistence | wild patch, armed after 3 flees (proven by spike) |
| `hgss-route33-shiny-resume` | encounter, persistence | wild patch; Resume records the catch and counts move |
| `hgss-route33-phase-count` | encounter, persistence | none; counts after N encounters |
| `hgss-starter-shiny` | starter-hunt | RNG freeze (proven by spike, harness not yet built) |
| `hgss-navigate-heal-return` (#140) | navigation | none |
| `hgss-heal-variants` (#141) | navigation | none; the three heal saves in turn (fainted lead, fainted slot 2, Pokérus), each must end with the bot back on Route 33 and no second nurse conversation (spike 2026-10-22 saw one on the slot-2 save) |
| `hgss-poison-walk` (#141) | navigation | none; pace the path row on the poison save until the "survived the poisoning" message, which must be cleared and the walk resumed |
| `hgss-togepi-egg-hatch` | egg | none, or an egg PID patch once mapped |
| `rs-starter-wave-shiny-park` | starter-hunt, bot-engine | RNG freeze, once RS is mapped |
| `rs-starter-double-find` | starter-hunt (the race the review found) | RNG freeze on two emulators |
| `api-concurrent-launch` | bot-engine (plan task 2.2) | none; two launches at once |
| `counts-cold-load` | persistence (plan phase 3) | none; first-load time against a seeded 1M-row database |
| `hgss-route33-dashboard` (exists, #107) | dashboard, bot-engine, encounter | `hgss-usa-rev0/wild-shiny` |
| `hgss-route33-train` (#111) | dashboard, bot-engine, encounter | none |
| `hgss-route33-evolve` (#137) | dashboard, bot-engine, encounter | none |
| `hgss-route33-evolve-learn` (#139) | dashboard, bot-engine, encounter | none; the move prompt is declined in Discord by hand |
| `hgss-route33-evolve-learn-replace` (#152) | dashboard, bot-engine, encounter | none; Water Gun, then Confirm Choice, in Discord by hand |
| `hgss-route33-evolve-learn-free` (#152) | dashboard, bot-engine, encounter | none; no Discord prompt |
| `hgss-route33-train-replace` (#154) | dashboard, bot-engine, encounter | none; Tackle, then Confirm Choice, in Discord by hand |
| `hgss-route33-train-free` (#154) | dashboard, bot-engine, encounter | none; no Discord prompt |
| `rs-jirachi-setup-refusals` | dashboard, jirachi | none; a rev 0 ROM, a missing save, a full party and a save without the Pokédex each refused at Start with the spec's sentence ([Wishmaker Jirachi spec](wishmaker-jirachi.md)) |
| `rs-jirachi-soak` | dashboard, bot-engine, jirachi, persistence | none; one unattended hour, at least 20 Jirachi encounters with distinct PIDs, every PID equal to the save's prediction, a "summary on view" line per cycle, no cycle over 300 s, then a cancel during the Dolphin half (FR-19) |
| `rs-jirachi-shiny` | bot-engine, jirachi, persistence, discord | `rs-sapphire-usa-rev2` seed freeze, generated per run: `jirachi-seed.ts` crafts `qa-jirachi`'s live section 0 to seed `ECDD` (PID `987ED636`, shiny), and the cheat freezes the play time and berry-stamp fields so the bot's own saves keep it. Asserts the encounter row, the Mongo catch, the Discord alert, the event in Waiting and EmuHawk parked at the summary |
| `rs-jirachi-saves` (#195) | jirachi | none; no emulator, backend or database, under a second, no run folder. `jirachi-seed.ts` reads `rs.rev0-base` and `rs.seed-ecdd` in place and asserts their sizes and sha256, every section of the base's two blocks valid, counters 2 and 3, live block `0xE000`, seed `0A64` with and without the mGBA footer, complete after counter 2 and not after 3, live play time 0:15:13 frame 3, Pokédex, one party member, and seed `ECDD` on the crafted save. The C# `Gen3SaveFile` is unit-tested on synthetic saves only; its coverage on real bytes is `rs-jirachi-shiny` (`Seed ECDD`) and `rs-jirachi-soak` (every prediction equal to the PID) |

## Spike results (2026-10-20)

Run on the dev PC with the real backend and bot, the QA install above, and the owner's Route 33 save. Evidence was kept locally, not committed.

| Check | Result |
|---|---|
| Fork release build installs and binds ApiHawk | Yes; no source build needed |
| RNG state located at runtime | `0x021D15A8`; the decomp's `sLCRNG_State` |
| Starter forced shiny by RNG freeze, repeated over three launches | Same predicted PID each time; golden Chikorita on screen |
| Wild patch makes grass encounters shiny | 2 of 2 with the patch, 0 of 1 without; different PIDs, so the RNG stayed live |
| Hotkey arms the cheat mid-run | Patch bytes read original before, patched after |
| Real bot flees normal encounters, then parks on the forced shiny | 3 normal flees (Zubat, Rattata, Rattata), hotkey, shiny Rattata, bot state `Waiting`, `IsShiny = true` in Postgres |
| Video of the run | 1 min 45 s at 15 fps via `PrintWindow`; `gdigrab` produced blank frames |

**Bugs and gaps the spike hit**, all already in the review or now added to the plan:
- The API only accepts enums as numbers (`EncounterType: 1`, not `"Run"`); no string-enum converter is registered. Plan 5.7.
- A cancel on a position already removed returns 500 (`KeyNotFoundException`) instead of 404. Plan phase 2.
- `--parsepokeapi` crashes if `SpriteBaseDirectory` does not exist. Plan 1.5.
- The Discord placeholder in `appsettings.json` (`your-token-here`) does not match the skip check (`YOUR_BOT_TOKEN`), so a fresh machine tries to log in with it. Plan 1.6b.
- `Microsoft.AspNetCore.OpenApi` was removed in the October 2026 cleanup (plan 1.6b); `Microsoft.OpenApi` 2.0.0 is still pulled in transitively and carries a high-severity advisory.
- `dotnet ef` needs no design-time DbContext factory: it runs the app's `Main` up to `WebApplication.Build()`. Corrected 2026-10-27 (#218); no change planned.

## Test cases for the harness itself
| Case | Setup | Expected |
|---|---|---|
| Seed search finds a shiny seed | TID/SID fixed | PID from the next two outputs has `ShinyValue < 8` |
| Cheat file writer | wild-shiny definition | file loads in BizHawk with every line, disabled, with `Equal` guards |
| Pre-armed guard | patch active at launch | run aborts before starting the hunt |
| Area mapping | changed `NavigatorEvent.cs` | selects `hgss-navigate-heal-return` only |
| Teardown on failure | step throws | no EmuHawk or backend from the run remains |

## QA save files
The owner makes the saves. They are personal game data, so they never go in the repo. They live in a folder beside the repo, which the QA agent finds by relative path from the repo root:
```
<parent of the repo>/
  ShinyBotHunter/              this repo
  shinybothunterqasaves/       QA saves, not in git
    README.md                  what state each save is in, its TID/SID, which ROM it goes with
    hgss/
      qa-route33.SaveRAM
      qa-starter.SaveRAM
    rs/
      qa-starter.SaveRAM
```
Saves are loaded with a script, `Scripts/qa-load-save.ps1`, which the harness calls and which works just as well by hand:
```
Scripts/qa-load-save.ps1 -Save hgss/qa-route33 -Rom "Pokemon HeartGold"
```
- It resolves the BizHawk install from the backend's `BizHawkBaseDirectory` (user secrets, then `appsettings.json`), and the system's Save RAM folder from that install's `config.ini` path entries, so it follows BizHawk's configuration rather than assuming `NDS\SaveRAM`.
- It copies `../shinybothunterqasaves/<Save>.SaveRAM` to `<Save RAM folder>\<Rom>.SaveRAM`, since the fork names saves after the ROM file, and prints both hashes.
- It refuses to run while EmuHawk is open, because EmuHawk would overwrite the save when it closes.
- It never writes into the saves folder.
- An allow rule in `.claude/settings.json` lets an agent run exactly this script without a permission prompt. Nothing else gets file access to the BizHawk folder that way.

The harness calls the script before launch and keeps its own before-and-after hash of the loaded copy, so a run that changed the game state is flagged. Until the owner moves the saves, they sit directly in the QA install's `NDS\SaveRAM`, as in the spike. TID and SID are recorded here because the RNG-freeze seed search needs them.

| Save | ROM file it is copied to | State when loaded | TID / SID | Used by |
|---|---|---|---|---|
| `hgss.qa-route33` | `Pokemon HeartGold` | Route 33 path tile above the grass, lead Slowpoke Lv 25, full party | 15613 / 35374 | the `hgss-route33-*` scenarios |
| `hgss.qa-starter` | `Pokemon HeartGold1` | Elm's lab at starter selection, no starter yet | 3591 / 51910 | `hgss-starter-shiny` (seed `1234695E` gives PID `D1DC151F`) |
| `hgss.qa-route33-movelearn` | `Pokemon HeartGold2` | Route 33 path tile, lead Bulbasaur Lv 14, one EXP short of Lv 15 (Take Down); PKHeX copy of `hgss.qa-route33` | 15613 / 35374 | `hgss-route33-train`, `hgss-route33-train-replace` |
| `hgss.qa-route33-evolve` | `Pokemon HeartGold3` | Route 33 path tile; lead one EXP short of an evolution level, no new move on the level (pointer spike); PKHeX copy of `hgss.qa-route33` | 15613 / 35374 | `hgss-route33-evolve` |
| `hgss.qa-route33-evolve-learn` | `Pokemon HeartGold4` | Route 33 path tile; lead Totodile Lv 20 unevolved, one EXP short of 21, four moves without Ice Fang; PKHeX copy of `hgss.qa-route33` | 15613 / 35374 | `hgss-route33-evolve-learn`, `hgss-route33-evolve-learn-replace` |
| `hgss.qa-poison` | `Pokemon HeartGold5` | Route 33 path tile; lead Slowpoke poisoned at 6 HP; PKHeX copy of `hgss.qa-route33` | 15613 / 35374 | `hgss-poison-walk` |
| `hgss.qa-heal-lead-fainted` | `Pokemon HeartGold6` | Route 33 path tile; lead at 0 HP; copy of `hgss.qa-route33` | 15613 / 35374 | `hgss-heal-variants`, `hgss-navigate-heal-return` |
| `hgss.qa-heal-slot2-fainted` | `Pokemon HeartGold7` | Route 33 path tile; lead at 15 HP, slot 2 at 0 HP; copy of `hgss.qa-route33` | 15613 / 35374 | `hgss-heal-variants` |
| `hgss.qa-route33-movelearn-3` | `Pokemon HeartGold9` | Route 33 path tile; lead Bulbasaur Lv 14, one EXP short of 15, three moves (no Vine Whip, no Take Down); PKHeX copy of `hgss.qa-route33-movelearn` | 15613 / 35374 | `hgss-route33-train-free` |
| `hgss.qa-route33-evolve-learn-3` | `Pokemon HeartGold10` | Route 33 path tile; lead Totodile Lv 20 unevolved, one EXP short of 21, three moves (no Scary Face, no Ice Fang); PKHeX copy of `hgss.qa-route33-evolve-learn` | 15613 / 35374 | `hgss-route33-evolve-learn-free` |
| `hgss.qa-heal-pokerus` | `Pokemon HeartGold8` | Route 33 path tile; lead at 15 HP with Pokérus, event flag `0x65` cleared so the nurse explains it; copy of `hgss.qa-route33` | 15613 / 35374 | `hgss-heal-variants` |
| `hgss.qa-togepi` | to make | Just before receiving the Togepi egg, party of 5 or fewer | | `hgss-togepi-egg-hatch` |
| `rs.qa-starter` | to make | Whatever the RS starter hunt expects | | the `rs-starter-*` scenarios |
| `rs.qa-jirachi` | `Pokemon - Sapphire Version (USA, Europe) (Rev 2)` (`AXPE` revision 2) | Trainer Phil in the field, Pokédex received (flag `0x801`), one party member, play time about 93 h, first-block counter 82; a byte copy of the spike's final hunt save, kept as `../shinybothunterqasaves/rs/qa-jirachi.SaveRAM` | 39541 / 17794 | `rs-jirachi-soak`, `rs-jirachi-shiny` (seed `ECDD` crafted by `jirachi-seed.ts` on a copy) |
| `rs.qa-jirachi-full` | `Pokemon - Sapphire Version (USA, Europe) (Rev 2)` | `rs.qa-jirachi` with six party members (PKHeX), kept as `../shinybothunterqasaves/rs/qa-jirachi-full.SaveRAM` | 39541 / 17794 | `rs-jirachi-setup-refusals` |
| `rs.qa-jirachi-nodex` | `Pokemon - Sapphire Version (USA, Europe) (Rev 2)` | `Pokemon Sapphire8.SaveRAM` as the owner made it: no Pokédex, one party member, kept as `../shinybothunterqasaves/rs/qa-jirachi-nodex.SaveRAM` | 39541 / 17794 | `rs-jirachi-setup-refusals` |
| `rs.rev0-base` | none: read in place, never installed (a rev 0 `Pokemon - Sapphire Version (USA)` save) | The spike's rev 0 base, 131088 bytes with the mGBA footer: every section of both blocks valid, section-0 counters 2 (block A) and 3 (block B, live), first-block seed `0A64`, live play time 0:15:13 frame 3, Pokédex set, one party member; kept as `../shinybothunterqasaves/rs/rs-rev0-base.SaveRAM`, sha256 `8aebc82d4453815c91ecd6a6667d3f2b1e01521f992431aabb4016b66458cc9e` | 39541 / 17794 | `rs-jirachi-saves` |
| `rs.seed-ecdd` | none: read in place, never installed | The spike's crafted shiny save, 131072 bytes with no footer: first-block seed `ECDD` (Jirachi PID `987ED636`, shiny); kept as `../shinybothunterqasaves/rs/rs-seed-ecdd.sav`, sha256 `3d86c956005d064dcf5684486502ddd002a02e245befc8d9f85fa540c7a82dc7` | 39541 / 17794 | `rs-jirachi-saves` |

The `README.md` in the saves folder lists each save and its state so it can be remade if lost.

Scenarios never save in-game, and the spike runs left both saves byte-identical to their backups. BizHawk still rewrites `SaveRAM` on close, so the harness should compare a save's hash before and after a run and fail if it changed. The exception is `rs-jirachi-soak`, which saves in-game by design: it installs a pristine copy, restores it afterwards, and compares the restored hash with the pristine one.

## Changelog
- 2026-10-26: `rs-jirachi-saves` check, its run line, and the `rs.rev0-base` and `rs.seed-ecdd` save rows, for W10 (#195): the spike's saves left the unit project.
- 2026-10-26: `rs-jirachi-shiny` scenario (REST, a crafted `ECDD` save and a generated QA cheat freezing the moving section-0 fields), the `rs-sapphire-usa-rev2/` cheat folder, its run line and preconditions, for W6b (#189).
- 2026-10-25: `rs-jirachi-setup-refusals` and `rs-jirachi-soak` scenarios (the soak gains the FR-19 cancel during the Dolphin half), their preconditions, and the three Jirachi save rows with their ROM file and saves-folder location, for W6a (#169); the soak is the exception to "never save in-game".
- 2026-10-24: `hgss-route33-train-replace` (`Pokemon HeartGold2`, Tackle forgotten on the battle's forget-move screen) and `hgss-route33-train-free` (`Pokemon HeartGold9`) scenarios, for S2e (#154); the decline scenario's run line gains `.spec`.
- 2026-10-23: `hgss-route33-evolve-learn-replace` (`Pokemon HeartGold4`, Water Gun forgotten) and `hgss-route33-evolve-learn-free` (`Pokemon HeartGold10`) scenarios, for S2d (#152); the decline scenario's run line gains `.spec`.
- 2026-10-23: `hgss-heal-variants` (one spec, `QA_HEAL_ROM` picks `Pokemon HeartGold6`, `7` or `8`) and `hgss-poison-walk` (`Pokemon HeartGold5`) scenarios, for S4 (#141).
- 2026-10-23: `Pokemon HeartGold9` (`hgss.qa-route33-movelearn-3`) and `Pokemon HeartGold10` (`hgss.qa-route33-evolve-learn-3`) save rows, three-move leads for the free-slot paths (traced in the pointer spike, "In-battle move replace and the free-slot paths").
- 2026-10-23: `hgss-navigate-heal-return` scenario on `Pokemon HeartGold6` (`hgss.qa-heal-lead-fainted`), for S3 (#140).
- 2026-10-23: `hgss-route33-evolve-learn` scenario and the `Pokemon HeartGold4` save row (`hgss.qa-route33-evolve-learn`), for S2b (#139); the QA backend is logged into Discord and move prompts are answered by hand.
- 2026-10-22: `hgss-route33-evolve` scenario and the `Pokemon HeartGold3` save row (`hgss.qa-route33-evolve`), for the state reader's QA gate (#137).
- 2026-10-22: `hgss-route33-train` scenario and the `Pokemon HeartGold2` save row; recorder scales 2x and strips the stdin BOM that turned the video red.
- 2026-10-21: Dashboard driver section rewritten for the Playwright driver in `ShinyBotHunter.Qa/dashboard/`; `data-testid` attributes allowed in `PokemonDashboard/`.
- 2026-10-22: four PKHeX clone saves for poison and the three heal variants; `hgss-heal-variants` and `hgss-poison-walk` scenarios from the field-script spike.
- 2026-10-17: first draft.
- 2026-10-20: saves loaded by `Scripts/qa-load-save.ps1` with an agent allow rule, instead of copy logic inside the harness.
- 2026-10-20: cheats stored per game and revision under `ShinyBotHunter.Qa/Cheats/`; saves moved out of the repo to a sibling `shinybothunterqasaves/` folder.
- 2026-10-20: rewritten from the spike. Injection is now BizHawk cheat files armed by hotkey, replacing the RNG write and struct patch designs and the ApiHawk write-range opcode; recording uses `PrintWindow` instead of `gdigrab`; QA environment and databases documented; spike results, cheat file rules, and saves with trainer IDs added.
