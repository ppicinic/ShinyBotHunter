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
- **Phases**: a scenario's milestone, such as `Route 33`, exists only in this machine's Mongo. The QA agent may write it straight to the `PhaseInfos` collection with `mongosh`, in the shape the Milestone Editor saves (`GameName`, `Name`, `Pokemon` with `Name`, `SpeciesId`, `Amount`, `FormCode` and `RequireSpecificForm`, a standard-GUID `Id`, `IsStatic`), or make it in the Milestone Editor. The backend reads milestones once at startup, so a milestone written directly is seen by the next backend a scenario launches, not by one already running.
- **Naming a milestone**: a spec under `ShinyBotHunter.Qa/` may name the milestone it needs. Nothing else may: no backend or dashboard code, seed, migration, setting or `Phases/*.json` file names or creates a QA milestone, so none reaches another environment. The `Phases/*.json` files are for the static phases that ship with the app.

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
  egg:          [ShinyBotHunter/BotEvents/HgssTogepiEggEvent.cs, ShinyBotHunter/BotEvents/EggHuntConfig.cs, ShinyBotHunter/Configs/Events/TogepiEgg.json, ShinyBotHunter/BotEvents/HgssEggHatch.cs, ShinyBotHunter/BotEvents/HgssPrimoEggEvent.cs, ShinyBotHunter/BotEvents/PrimoEggConfig.cs, ShinyBotHunter/Configs/Events/PrimoEgg.json, ShinyBotHunter/Games/Hgss/HgssEasyChatInput.cs, ShinyBotHunter/Games/Hgss/PrimoPassword.cs]
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
npx playwright test hgss-headbutt.spec
npx playwright test hgss-headbutt-sideways
npx playwright test hgss-headbutt-cutin
npx playwright test hgss-headbutt-dead-tree
npx playwright test hgss-headbutt-greyed
npx playwright test hgss-headbutt-shiny
npx playwright test hgss-primo-egg-refusals
npx playwright test hgss-primo-egg-password
$env:QA_PRIMO_ROM = "SS-Slugma-TID2"; npx playwright test hgss-primo-egg-password   # PowerShell; then again with SS-Slugma-TID3
npx playwright test hgss-primo-egg-hatch
npx playwright test hgss-togepi-egg-hatch
npx playwright test hgss-primo-egg-shiny
npx playwright test hgss-pickup.spec
npx playwright test hgss-pickup-take
npx playwright test hgss-pickup-greyed
npx playwright test hgss-pickup-items-window
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

A fifteenth scenario, `hgss-primo-egg-refusals.spec.ts`, checks the [Primo egg hunt](primo-egg-hunt.md)'s Run refusals (FR-2) through the UI. It launches `SS-Slugma` (`hgss.qa-primo-full`, six Pokémon, facing Primo) with Launch Bot + Continue, records it as `refusal-full.mp4`, and runs `HGSS Primo Egg` with milestone `Primo Slugma`: the card's red message must contain `Party is full (6/6)`, the card must still offer its actions, `getDashboardState` must show `None`, and `backend.log` must hold no `[PrimoEgg]` line. One Cancel closes the bot, and `SS-Slugma.SaveRAM` must equal its `.bak`. It then launches `SS-Slugma-Room` (`hgss.qa-primo`), records `refusal-place.mp4`, steps down one tile with `runMacro` (map 158 at (12, 13) is asserted) and runs again: the message must be exactly "Stand facing Primo in the Violet City Pokémon Center: map 158, tile (12, 12), facing up.", with no event started, and that save must equal its `.bak` after the cancel. `driver.ts`'s recorder takes the file name, so the second session does not overwrite or stop the first.

A sixteenth scenario, `hgss-primo-egg-password.spec.ts`, runs `HGSS Primo Egg` until the egg on the save chosen by the `QA_PRIMO_ROM` environment variable: `SS-Slugma-Room` (`hgss.qa-primo`, the default), `SS-Slugma-TID2` (`hgss.qa-primo-tid2`) or `SS-Slugma-TID3` (`hgss.qa-primo-tid3`). Each value runs in its own process and run folder, as with `hgss-heal-variants`. After launch it reads the trainer ID at `[[0x021D4158] + 0x0C] + 0x84` (u16, R31) and computes the expected words for egg 10 with `primo-password.ts`, a port of `PrimoPassword.Encode`; no password is a constant. It polls `backend.log` every 250 ms and asserts `[PrimoEgg] trainer ID N, password a b / c d` with those words (on `SS-Slugma-Room`, 60647: 1085 1337 / 981 1062, KLUTZ, TEACHER / DARK, STEADFAST). After each `[EasyChat] confirmed` line it reads the script's special variables itself, `[[[0x021D4158] + 0x10] + 0x0C] + 0x8C`, 14 u16s, hop by hop, until 0x8000/0x8001 read words 1 and 2 (prompt 1) or 0x8002/0x8003 read words 3 and 4 (prompt 2) within 10 s, logs all 14, and asserts the event's own `[PrimoEgg] after prompt N: 0x8000=.. 0x8001=..` line. It asserts `[PrimoEgg] outcome 5 (party count)` and `[PrimoEgg] egg in slot 6, pid=P`, then reads party RAM itself (`egg-pid.ts`; `GET getParty` may not show eggs): a count of 6, and slot 6 a Slugma (218) egg with PID P and a valid checksum. It writes the four `[EasyChat] slot S, word W` lines and each prompt's summed presses and seconds to `steps.log`, asserts four word lines and two `confirmed` lines, and judges no time. It cancels the event and the bot; the save must equal its `.bak`.

A seventeenth scenario, `hgss-primo-egg-hatch.spec.ts`, runs two full not-shiny cycles on `SS-Slugma-Room` (timeout 55 minutes). Every 2 s it fails on a `[BotService] Event '…' crashed` line or any line containing `stalled` or `stopped at`. After each `[EggHatch] slot 6:` line it reads the map and tile until the soft reset has put the player back on map 158 at (12, 12), within 3 minutes; the facing is the event's own check (FR-11). After the second cycle it cancels the event and the bot, and the save must equal its `.bak`. It then checks each cycle's lines, from the previous hatch line to its own, in order: `[PrimoEgg] egg in slot 6, pid=P`, `[Navigator] Return path found:`, `[Navigator] Portal crossed:` (the Center door), `[PrimoEgg] on the line at map 73, tile (469, 258)`, at least one `[EggHatch] day care step counter`, the `[EggHatch] H.A.4:` press at script 2021's `WaitABPress`, the `[EggHatch] W.5;H.A.4:` press on No, and `[EggHatch] slot 6: Slugma (species 218), pid P`. The two egg PIDs must differ. NFR-1: cycle 1, from the Run click to hatch line 1, and cycle 2, from hatch line 1 to hatch line 2, must each be under 20 minutes, and both are logged. After the documented shutdown, `psql` must find exactly two `encounters` rows for `Primo Slugma` since the Run, species 218, with the logged PIDs in order.

An eighteenth scenario, `hgss-togepi-egg-hatch.spec.ts`, runs one Togepi cycle on the shared hatch step (FR-17) on `SS-Togepi` (`hgss.qa-togepi`, timeout 35 minutes). It fails at once, naming the save, if `SS-Togepi.SaveRAM` or its `.bak` is missing. It runs `HGSS Togepi Egg` with milestone `Togepi Egg` and asserts, in order: `[TogepiEgg] Already at the start position` (so the hunt does not save and the `.bak` check holds), `[TogepiEgg] Egg in party slot N, pid=P` from the existing receive macro, at least one `[EggHatch] day care step counter`, the `H.A.4` press, the `W.5;H.A.4` press, `[EggHatch] slot N: Togepi (species 175), pid P` within 25 minutes, and `[TogepiEgg] Hatched Togepi`. No line in `backend.log` may match `stall` in any case. After the soft reset the player must be back on map 157 at (6, 9) within 3 minutes. It cancels the event and the bot, the save must equal its `.bak`, and after the shutdown `psql` must find one `Togepi Egg` row since the Run, species 175, PID P.

A nineteenth scenario, `hgss-primo-egg-shiny.spec.ts`, proves the shiny path end to end (timeout 40 minutes).
- **Setup.** It deletes any `C:\ROMs\SS-Slugma-Shiny.nds`, `NDS\SaveRAM\SS-Slugma-Shiny.SaveRAM` and `.SaveRAM.bak` left by an earlier run, copies `SS-Slugma-Room.nds` and its SaveRAM to those names, logs the hashes, and counts the `CaughtPokemon` documents for `Primo Slugma` on `SS-Slugma-Shiny` with `mongosh`. It launches the copy from Bot Control and runs `HGSS Primo Egg` / `Primo Slugma`.
- **The rewrite.** The hatch finds its slot by the PID the event read at receipt, so the rewrite lands between `[PrimoEgg] outcome 5` (message 18, before `GiveEgg`) and `[PrimoEgg] egg in slot`. On the outcome line it polls the party count (`[0x021D4158] - 0x23F44`) until 6, then slot 6 (`- 0x23F40 + 5 × 0xEC`) until `egg-pid.ts` decodes a Slugma egg with a valid checksum, and asserts the egg's own TID/SID are 60647/55034. `shinyPidKeepingLow` keeps the PID's low half and block order, and `rewritePid` re-encrypts the record; the round trip must decode shiny, with a valid checksum and the same species, egg flag and IV word. Only the differing bytes are written, all at once with `writeU8` (ApiHawk drains its queue in one frame), the slot is read back, and the old and new PID, the byte count and the milliseconds since the outcome line are logged. If `egg in slot` then shows the old PID, the run fails with "the bot read the egg before the rewrite landed; rerun". There is no retry.
- **Asserts.** Within 25 minutes, `[EggHatch] slot 6: Slugma (species 218), pid <new>`, then:
  - `getDashboardState` shows `Complete`;
  - the shiny channel's latest Discord message contains `Shiny Slugma` and is newer than the Run;
  - one new `CaughtPokemon` for the copy, with the new PID, species 218, shiny;
  - one Cancel removes the bot and its EmuHawk closes (flushing SaveRAM);
  - after the documented shutdown, exactly one `encounters` row since the Run: the new PID, 218, shiny, `SS-Slugma-Shiny`;
  - the copy's SaveRAM differs from `SS-Slugma-Room`'s and holds a 0xEC record starting with the new PID that decodes as a hatched Slugma with a valid checksum: the bot's in-game save, found by PID with no save-layout code.
- **Teardown.** Its `finally` kills the backend, then EmuHawk, copies the copy's SaveRAM (and `.bak`, if BizHawk wrote one) into the run folder, deletes the ROM copy, its SaveRAM and `.bak`, and logs `SS-Slugma-Room.SaveRAM` against its `.bak`. The PID rewrite is QA tooling in `egg-pid.ts` and this spec; no bot code writes a PID.

The six [Headbutt](headbutt-encounter.md) scenarios share `headbutt.ts`: the FR-11 line parse, the cycle watch, the tile and facing read (`X`/`Y` and the facing byte at `[0x021D4158] + 0x1DC4`, as `HgssOverworldReader` reads them) and the `encounters` rows by `psql`, as the soak reads them. Each launches its save with Launch Bot + Continue and starts Wild Encounter from the card with the HGSS milestone whose Pokémon include Spearow, Heracross and Aipom, ShinyTarget, Headbutt and TrainType left at None. The milestone is found by those species through `phaseinfo`, so any name works, and the `encounters` rows are read for it. Before it selects Headbutt it fails if no such milestone is offered, or if the Headbutt option is disabled, with the option's text: there is no party refresh, so a "could not be read" label is a product finding, not a setup step. The cycle watch polls `backend.log` every 250 ms. A cycle starts at its first `[GameState] the headbutt script:` line, and the tile and facing are read then: that is the A's tile, since the player does not move until the swap. It counts the FR-11 lines (`[Headbutt] start=script N presses=N end=battle|nothing return=none|cached|built from (x,y,Facing)|rebuilt from (x,y,Facing)`), writes every cycle to `steps.log`, and fails at once on a `crashed` line, a `Cache miss at` line, Resume on the card, or (cycles + 1) × 60 s. A return shows on the next cycle's FR-11 line, so N cycles are N + 1 lines; the scenario cancels as soon as the last one is logged, before that battle's foe is read, and expects N rows. An extra row is a rerun, not a bug. Each cancels twice (event, then bot), checks the save against its `.bak` and stops the backend as the first scenario does, and its `finally` logs the save against its `.bak`.

A twentieth scenario, `hgss-headbutt.spec.ts`, runs ten cycles on `HG-Headbutt` (`hgss.qa-headbutt`). Before Start the tile must read (426, 464) facing Down. It asserts that the `Start tile captured` line holds `map=74 (426,464) facing=Down`, that all 11 FR-11 lines have `start=script 10014` and `end=battle`, that the returns are `none`, then `built from (426,463,Up)` (the follower's tile behind), then nine `cached`, with no `Cache miss at` line, and that all 11 A tiles are (426, 464, Down). After the shutdown there must be ten rows for the milestone and ROM since the Start click, each Spearow, Heracross or Aipom (21, 214, 190) at level 3 to 5 and not shiny, with every gap between consecutive rows under 45 s (NFR-1); the gaps go to `steps.log`. It needs no cheat, and it needs `HG-Headbutt.SaveRAM.bak` beside the save. Its name is contained in the other five, so it runs as `hgss-headbutt.spec`.

A twenty-first scenario, `hgss-headbutt-sideways.spec.ts`, is the twentieth's flow on the same save with the follower beside the player. Before Start, `runMacro` steps left to x 425 and back to 426, then presses Down (`M.Left.425.<X>;M.Right.426.<X>;W.8;H.Down.3;W.10`), and the tile must read (426, 464, Down). It runs five cycles and asserts six FR-11 lines with script 10014 and a battle, the returns `none`, `built from (425,464,Left)` and four `cached`, no cache miss, every A at the start, and five rows. It needs no cheat, and it needs `HG-Headbutt.SaveRAM.bak`.

A twenty-second scenario, `hgss-headbutt-cutin.spec.ts`, is the twentieth's flow on `HG-Headbutt-CutIn` (`hgss.qa-headbutt-cutin`): Typhlosion leads and follows and Slowpoke, second, knows Headbutt, so the cut-in plays and the player never moves. It runs five cycles and asserts that all six FR-11 lines say `return=none`, with script 10014 and a battle, that every A is at (426, 464, Down), and five rows. It needs no cheat, and it needs `HG-Headbutt-CutIn.SaveRAM.bak`.

A twenty-third scenario, `hgss-headbutt-dead-tree.spec.ts`, runs on `HG-Headbutt`. Before Start, `runMacro` steps left one tile and presses Down (`M.Left.425.<X>;W.8;H.Down.3;W.10`), so the player faces the tree at (425, 465), which gives no Pokémon for this trainer ID; the tile must read (425, 464, Down). It waits up to 10 minutes for Resume or a `crashed` line. It asserts that the `Start tile captured` line holds `(425,464)`, exactly one FR-11 line with `end=nothing`, exactly one `[Headbutt] The tree in front of you gave no Pokémon. It may give none for your trainer ID, or a Repel is active.` line (the é is matched loosely, since `backend.log` is the redirected console), the tile (425, 464, Down) at the park, Resume on the card and `eventState` `Waiting` for position 0 in `getDashboardState`, no `Exception` or `crashed` line after the Start click, and after the shutdown no row. It needs no cheat, and it needs `HG-Headbutt.SaveRAM.bak`.

A twenty-fourth scenario, `hgss-headbutt-greyed.spec.ts`, runs on `HG-Headbutt-NoMove` (`hgss.qa-headbutt-nomove`), where no party member knows Headbutt. Once in the field it opens the Wild Encounter form and asserts that the Headbutt option of `param-EncounterType` is disabled with the text exactly `Headbutt (No Pokémon in your party knows Headbutt.)`, and that Spin and Run are enabled (screenshot `02-greyed`). If the option shows the read-failed sentence instead, the run fails with that text. It then POSTs `startAction/0` with Wild Encounter, that milestone, ShinyTarget and Headbutt, and asserts a 400 whose body is `No Pokémon in your party knows Headbutt.`, `eventState` `None` for position 0, the Wild Encounter picker still on the card, and no `[Headbutt]` line. One cancel removes the bot, since there is no event. It needs no cheat, and it needs `HG-Headbutt-NoMove.SaveRAM.bak`.

A twenty-fifth scenario, `hgss-headbutt-shiny.spec.ts`, runs on `HG-Headbutt` with `hgss-usa-rev0/wild-shiny` copied disabled to `NDS\Cheats\HG-Headbutt.cht`, as the first scenario does, with the same pre-armed check of the two patch sites. Once two `, isShiny: ` foe lines are logged (3 minutes at most, failing on a crash or a park) it sends the hotkey, which must find EmuHawk in the foreground, and records the arm time. It then waits up to 10 minutes for Resume and asserts that the last foe line is `isShiny: True` with Spearow, Heracross or Aipom, `eventState` `Waiting`, that the shiny channel's latest Discord message contains `Shiny <that name> encountered` and was sent after the arm (fetched as `rs-jirachi-shiny` does, with the token and channel from the user secrets and never logged), and after the shutdown that the newest row since the Start is that species with `IsShiny` true. Its `finally` deletes `HG-Headbutt.cht`. It needs `HG-Headbutt.SaveRAM.bak`. The spike found headbutt and grass share the encounter function the cheat patches; if the patch sites never settle in Azalea, or no shiny comes within 10 minutes, that is a finding, not a scenario bug.

The three [Pickup](pickup-mode.md) scenarios share `pickup.ts`: the `[Pickup]` line parse (the count, take, trip and stop lines), the party read from RAM (the count at `[0x021D4158] - 0x23F44` and the records from `- 0x23F40`, each through `egg-pid.ts`'s `decodeMon`, which gives the held item and the ability), the Wild Encounter form (Route 33, ShinyTarget, Run, xRange 3, train Standard, target level 101 and the Pickup checkbox) and the decision windows, from each `[Orchestrator] State: BattleComplete` to the next `Encountering`, each of which must hold exactly one count line and one `trip took` line. Each launches its save with Launch Bot + Continue and fails, naming the Milestone Editor, if no Route 33 milestone is offered. `GET getParty` carries no held item or ability, so it is read for names and levels only.

A twenty-sixth scenario, `hgss-pickup.spec.ts`, runs on `HG-Pickup` (`hgss.qa-pickup`) with a 45-minute timeout. It subscribes HGSS / Route 33 and reads the encounter count as the train scenario does, steps two tiles into the grass, and checks that the lead is Typhlosion, that there are six members and that the Pickup members are slots 2 to 6, holding nothing in RAM. It starts Wild Encounter with Pickup ticked (the label exactly `Pickup`, enabled) and watches `backend.log` every 250 ms until the `[Pickup] trip took N ms` line of the visit after the 30th won battle, which comes after CANCEL and the field-free wait, so the cancel never lands in the menu. It fails at once on a `crashed` line, a `[Pickup] Pickup: ` stop, a `GameStateTimeoutException`, Resume on the card, or 3 minutes without a `BattleComplete` (it then logs the last 20 state, `[Pickup]` and `[LevelUp]` lines). On every visit's trip line it reads RAM, asserts every Pickup member at held item 0, and asserts the visit took under 15 s when its count line said `take` and under 6 s when it said `nothing to take`, logging the visit number, the count line, the time and the RAM. It cancels the event at once and asserts that the first visit is `held 0 of 5 pickup members; nothing to take`, its trip line before the first `BattleStart` and under 6 s; one visit per battle, with no `BattleStart` between its count line and its trip line; every `take` visit with exactly its count of `took item` lines in ascending Pickup slots, under 15 s; every `nothing to take` visit with held 0, no take, under 6 s; no stop and no crash; then the same names through `getParty`, no Pickup member holding in RAM, and a rise in the encounter count. Every battle is followed by a visit; `steps.log` reports the won battles, the visits, the visits with takes and the items taken, and nothing asserts on those counts, since `hgss-pickup-take` proves the take path. A Typhlosion level-up may ask Discord about a move; answer Do Not Learn. It needs no cheat, and it needs `HG-Pickup.SaveRAM.bak`.

A twenty-seventh scenario, `hgss-pickup-take.spec.ts`, runs the same action on `HG-Pickup-Held` (`hgss.qa-pickup-held`), without the subscription. Its precondition is the five Pickup members in slots 2 to 6 each holding an item in RAM; those slots and item ids are the takes expected. It watches `backend.log` for the first visit's trip line for 3 minutes, failing as the twenty-sixth does, reads RAM at once, and asserts that the first `[Pickup]` line is `held 5 of 5 pickup members; take`, no `Encountering` or `BattleStart` before the first visit's trip line, five `took item` lines in slot order with each slot's item, the trip under 15 s, every Pickup member at held item 0, and then `Encountering` with no stop (the event's own post-visit read passed). After cancelling the event, the names and levels through `getParty` are unchanged and RAM shows nothing held. It needs no cheat, and it needs `HG-Pickup-Held.SaveRAM.bak`.

A twenty-eighth scenario, `hgss-pickup-greyed.spec.ts`, runs on `Pokemon HeartGold` (`hgss.qa-route33`), where no party member has the Pickup ability (checked in RAM first). It fills the Wild Encounter form with train Standard and asserts that the Pickup checkbox is disabled with the label exactly `Pickup (No Pokémon in your party has the Pickup ability.)` (screenshot `02-greyed`); a read-failed label fails with its text. It then POSTs `startAction/0` with Wild Encounter, Route 33, ShinyTarget, Run, xRange 3 and Pickup true, once with TrainType None, which must be a 400 whose body is `Pickup needs a train mode: the Pokémon must be knocked out, not fled from.`, and once with TrainType Standard and TargetLevel 101, which must be a 400 whose body is `No Pokémon in your party has the Pickup ability.`; then `eventState` `None` for position 0, the Wild Encounter picker still on the card, and no `[Pickup]` or `[Orchestrator] State:` line. One cancel removes the bot. It needs no cheat, and it needs `Pokemon HeartGold.SaveRAM.bak`.

A twenty-ninth scenario, `hgss-pickup-items-window.spec.ts`, runs the take scenario's action on `HG-Pickup-Held` (`hgss.qa-pickup-held`) for the [Pickup items dashboard spec](pickup-items-dashboard.md). It asserts that the `Pickup items windows` switch on the Dashboard Config tab is off at start and that no window titled `Pokemon Dashboard — Pickup:` is open, before and after the launch (screenshot `01-switch-off`), and that the five Pickup members hold items 17, 26, 25, 78 and 50 in slot order. It starts Wild Encounter with Pickup on, watches for the first visit's trip line as the twenty-seventh does, and asserts the five `took item` lines with those items. With the hunt still running it turns the switch on and asserts exactly one window titled `Pokemon Dashboard — Pickup: HG-Pickup-Held`, `GET getPickupItems` as one entry for `HG-Pickup-Held` with Escape Rope, Hyper Potion, Potion, Rare Candy and Super Potion at 1 each and total 5, the window's cells and Total equal to it, all five cell images loaded (`naturalWidth` above 0; a failure points at `--parseitemsprites`), and more than one cell on the grid's first row (`04-switch-on`). It then watches `backend.log` until a later visit's trip line follows a `took item` line, failing on a crash, a stop, a stale wait, Resume, 3 minutes without a `BattleComplete`, or 10 won battles with no take (about a 0.5 % chance; a rerun, not a pass), and asserts the GET's total risen by the new takes, its counts matching every take so far, and the open window, never reopened, equal to it (`05-pushed`). Turning the switch off closes the window and leaves no Pickup window (`06-switch-off`); it then cancels the event and the bot. It needs no cheat, and it needs `HG-Pickup-Held.SaveRAM.bak`.

Preconditions:
- Postgres is started, the Route 33 milestone exists in Mongo, and the backend's user secrets point at both on localhost and hold the Discord token. The train, train-replace, evolve-learn and evolve-learn-replace scenarios need someone to answer the move prompt in Discord.
- `PokemonDashboard/override-config.json` sets `spritesRootPath`.
- The QA BizHawk install is in `C:\BizHawk-2.11` with the Route 33 save and its `.bak` in `NDS\SaveRAM`.
- Playwright's ffmpeg is installed (`npx playwright install ffmpeg`). Without it, launching with video recording leaves every window unanswered and the scenario hangs until its timeout.
- ffmpeg is on PATH (for `record-window.ps1`), the desktop is unlocked, and nothing else holds port 5000 or runs EmuHawk. The emulator must keep focus while the hotkey is sent.
- For the Primo and Togepi scenarios: `SS-Slugma.nds`, `SS-Slugma-Room.nds`, `SS-Slugma-TID2.nds`, `SS-Slugma-TID3.nds` and `SS-Togepi.nds` are in the backend's `ROMBaseDirectory`; `SS-Slugma`, `SS-Slugma-Room`, `SS-Slugma-TID2`, `SS-Slugma-TID3` and `SS-Togepi` saves and their `.bak`s are in `NDS\SaveRAM`, with pristine copies in `../shinybothunterqasaves/hgss/` (`qa-primo-tid2.SaveRAM` and `qa-primo-tid3.SaveRAM` for the two trainer-ID copies); the HGSS milestones `Primo Slugma` and `Togepi Egg` exist in Mongo, written there by the QA agent (never as `Phases/*.json`); Postgres is started, with `psql` as for the Jirachi scenarios. For `hgss-primo-egg-shiny`, the Discord token and read access to `ShinyLogChannel`, and `mongosh` under `%LOCALAPPDATA%\Programs\mongosh`, as for `rs-jirachi-shiny`.
- For the Jirachi scenarios: `Pokemon - Sapphire Version (USA, Europe) (Rev 2).gba` and `Pokemon - Sapphire Version (USA).gba` are in the backend's `ROMBaseDirectory`, and the soak's `-WISHMKR.gba` copy there is expected; `../shinybothunterqasaves/rs/` holds the three Jirachi saves; the RS milestone `Jirachi` exists in Mongo, made in the Milestone Editor (never as a `Phases/*.json`); the Dolphin user folder is deployed with `Scripts/deploy-dolphin.ps1` and the Dolphin, BIOS and Bonus Disc settings are in the user secrets; Postgres is started; no EmuHawk or Dolphin is running, and for the soak nothing else uses the machine's emulators for the hour. For the shiny scenario, three more things must be in place. The user secrets need the Discord token, and the bot needs read access to the shiny channel (`ShinyLogChannel`). `SpriteBaseDirectory` must end in a slash and hold `Shiny/jirachi.png`. `mongosh` must be under `%LOCALAPPDATA%\Programs\mongosh`.
- For the Jirachi scenarios: `Pokemon - Sapphire Version (USA, Europe) (Rev 2).gba` and `Pokemon - Sapphire Version (USA).gba` are in the backend's `ROMBaseDirectory`, and the soak's `-WISHMKR.gba` copy there is expected; `../shinybothunterqasaves/rs/` holds the three Jirachi saves; the RS milestone `Jirachi` exists in Mongo (see **Phases** under QA databases); the Dolphin user folder is deployed with `Scripts/deploy-dolphin.ps1` and the Dolphin, BIOS and Bonus Disc settings are in the user secrets; Postgres is started; no EmuHawk or Dolphin is running, and for the soak nothing else uses the machine's emulators for the hour. For the shiny scenario, three more things must be in place. The user secrets need the Discord token, and the bot needs read access to the shiny channel (`ShinyLogChannel`). `SpriteBaseDirectory` must end in a slash and hold `Shiny/jirachi.png`. `mongosh` must be under `%LOCALAPPDATA%\Programs\mongosh`.
- For the Headbutt scenarios: `HG-Headbutt.nds`, `HG-Headbutt-CutIn.nds` and `HG-Headbutt-NoMove.nds` are in the backend's `ROMBaseDirectory`, each save and its `.bak` in `NDS\SaveRAM`; an HGSS milestone of any name with Spearow, Heracross and Aipom exists in Mongo (see **Phases** under QA databases); Postgres is started and `psql` is at `C:\Tools\pgsql\bin\psql.exe`. For `hgss-headbutt-shiny`, the user secrets need the Discord token, the bot needs read access to the shiny channel (`ShinyLogChannel`), and `SpriteBaseDirectory` must end in a slash and hold the `Shiny/` sprites of Spearow, Heracross and Aipom.
- For the Pickup scenarios: `HG-Pickup.nds` and `HG-Pickup-Held.nds` (copies of `Pokemon HeartGold.nds`) are in the backend's `ROMBaseDirectory`, each save and its `.bak` in `NDS\SaveRAM`, with pristine copies `qa-pickup.SaveRAM` and `qa-pickup-held.SaveRAM` in `../shinybothunterqasaves/hgss/`; the HGSS `Route 33` milestone exists in Mongo. The backend is logged into Discord in case a Typhlosion level-up during `hgss-pickup` asks about a move.

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
| `hgss-togepi-egg-hatch` | egg | none; one full cycle on the shared hatch step ([Primo egg hunt spec](primo-egg-hunt.md), FR-17): the day care counter logged, script 2021, No at the nickname, no stall line |
| `hgss-hm-rocksmash`, `hgss-hm-cut`, `hgss-hm-surf` | navigator | none; a navigator walk on `hgss.qa-hm` past the Route 32 rock, tree and onto the water ([pathfinder spec](pathfinder-maps-flags-hms.md), FR-9, FR-11): the move's script id, `MENU_CHOICE`, `PRINTING`, field free; the far tile reached; save equals `.bak` |
| `hgss-hm-refused` | navigator | the badge byte cleared on a throwaway copy of `hgss.qa-hm` (FR-10): no route through the rock, no script 10001 |
| `hgss-hm-strength`, `hgss-hm-waterfall`, `hgss-hm-rockclimb` | navigator | none; navigator walks on `hgss.qa-hm-mortar` through Mt. Mortar: the boulder pushed one tile (script 10002), up and back down the 1F waterfall (10005), across the rock-climb wall (10003); save equals `.bak` |
| `hgss-hm-whirlpool` | navigator | none; a surf on `hgss.qa-hm-r27` through the Route 27 whirlpool (script 10016, field task 021F2DD1); save equals `.bak` |
| `hgss-flag-route`, `hgss-map-rebuild` | pathfinding | none; no emulator: the Dijkstra with and without a flag on the committed graph, and `--generatemapdatahgss` on `C:\Code\pokeheartgold` byte-equal to `Data/HGSS` (FR-1, FR-7) |
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
| `hgss-headbutt` | dashboard, bot-engine, encounter, headbutt | none; ten cycles on the tree in front, one return macro built from the follower's tile and nine cached, every A at the start facing the tree ([headbutt encounter spec](headbutt-encounter.md)) |
| `hgss-headbutt-sideways` | encounter, headbutt | none; `runMacro` puts the follower beside the player before Start; one macro built from (425, 464, Left), no cache miss |
| `hgss-headbutt-cutin` | encounter, headbutt | none; Headbutt known by a non-following party member, so no swap and no return macro |
| `hgss-headbutt-dead-tree` | encounter, headbutt | none; `runMacro` faces the tree at (425, 465), which gives no Pokémon; the event parks in Waiting with the "gave no Pokémon" line and no exception |
| `hgss-headbutt-greyed` | dashboard, headbutt | none; no party member knows Headbutt; the option is disabled with its reason and an API Start with Headbutt is a 400 |
| `hgss-headbutt-shiny` | encounter, persistence, headbutt | `hgss-usa-rev0/wild-shiny` armed after two cycles; proves the cheat reaches headbutt encounters and the shiny parks with the alert |
| `hgss-primo-egg-refusals` | dashboard, egg | none; a full party and a player off Primo's tile are each refused at Run with the spec's sentence ([Primo egg hunt spec](primo-egg-hunt.md)) |
| `hgss-primo-egg-password` | dashboard, egg | none; the computed password entered through the easy-chat screen, the script variables after each prompt, a Slugma egg in the party |
| `hgss-primo-egg-hatch` | dashboard, bot-engine, egg, persistence | none; two full cycles: the navigator walk to the line, the day care counter logged until the hatch script, No at the nickname question, two Slugma rows with different PIDs, back at Primo after each soft reset |
| `hgss-primo-egg-shiny` | egg, persistence, discord | the egg's PID rewritten in party RAM to a shiny one (QA tooling, on a throwaway copy of the save); Discord alert, catch recorded, save written, event Complete |
| `hgss-pickup` | dashboard, bot-engine, encounter, pickup | none; train Standard to level 101 with Pickup on for 30 won battles: a visit after every won battle, its `[Pickup]` count line at the party list, a visit with takes under 15 s and one with nothing to take under 6 s, nothing held after each visit ([Pickup mode spec](pickup-mode.md)) |
| `hgss-pickup-take` | bot-engine, pickup | none; five items in hand: the first visit, before any encounter, five takes in slot order, nothing held in RAM, under 15 s |
| `hgss-pickup-greyed` | dashboard, pickup | none; no Pickup member: the checkbox is disabled with its reason, and API Starts with Pickup and no train mode, and with no Pickup member, are 400s |
| `hgss-pickup-items-window` | dashboard, pickup | none; five items in hand: switch off and no window at start, after the first visit the switch on shows one window for HG-Pickup-Held equal to GET getPickupItems (five items at 1, total 5) in cells with loaded sprites, more than one to a row, a later take pushed into the open window equal to the GET again, switch off closes it ([Pickup items dashboard spec](pickup-items-dashboard.md)) |
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
| `hgss.qa-route33` | `Pokemon HeartGold` | Route 33 path tile above the grass, lead Slowpoke Lv 25, full party | 15613 / 35374 | the `hgss-route33-*` scenarios, `hgss-pickup-greyed` |
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
| `hgss.qa-headbutt` | `HG-Headbutt` | Azalea Town (426, 464) facing Down at headbutt tree 0 (common for trainer ID 15613); lead Slowpoke Lv 25 knowing Headbutt, following; the owner's save | 15613 / 35374 | `hgss-headbutt`, `-sideways`, `-dead-tree`, `-shiny` |
| `hgss.qa-headbutt-cutin` | `HG-Headbutt-CutIn` | `hgss.qa-headbutt` with party slots 1 and 2 swapped by PKHeX: Typhlosion leads and follows, Slowpoke second knows Headbutt | 15613 / 35374 | `hgss-headbutt-cutin` |
| `hgss.qa-headbutt-nomove` | `HG-Headbutt-NoMove` | `hgss.qa-headbutt` with Slowpoke's Headbutt replaced by Tackle (PKHeX); no party member knows Headbutt | 15613 / 35374 | `hgss-headbutt-greyed` |
| `hgss.qa-pickup` | `HG-Pickup` (a copy of `Pokemon HeartGold.nds`) | the owner's Route 33 save: path tile (460, 472) facing Down, lead Typhlosion Lv 37, slots 2 to 6 rewritten to Aipom with Pickup (ability 53) at levels 15 / 11 / 22 / 36 / 1, nothing held; written by the spike's script ([research](../research/pickup.md), "The save"), never saved in-game. `HG-Pickup.SaveRAM` and its `.bak` in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-pickup.SaveRAM`; sha256 `1604ff04537a4d274f5e42b0a9fcee365d2cea8b5d7f9f3564d9491d2c29ed56` | 15613 / 35374 | `hgss-pickup` |
| `hgss.qa-pickup-held` | `HG-Pickup-Held` (a copy of `Pokemon HeartGold.nds`) | as `hgss.qa-pickup` with Potion, Super Potion, Hyper Potion, Escape Rope and Rare Candy in hand. `HG-Pickup-Held.SaveRAM` and its `.bak` in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-pickup-held.SaveRAM`; sha256 `ef5a5dbd25155cfb002f1b5a71419234029f0553873472b2b0bc8d10cd2b3825` | 15613 / 35374 | `hgss-pickup-take`, `hgss-pickup-items-window` |
| `hgss.qa-primo` | `SS-Slugma-Room` | Violet Pokémon Center 1F (map 158) at (12, 12) facing Primo; five Pokémon; the owner's `SS-Slugma` with the sixth member removed by PKHeX; SoulSilver rev 0 | 60647 / 55034 | `hgss-primo-egg-refusals` (moved off the tile), `-password`, `-hatch`, `-shiny` (on a throwaway copy `SS-Slugma-Shiny`, made and deleted by the scenario). `SS-Slugma-Room.SaveRAM` and its `.bak` (a byte copy, made 2026-11-02) in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-primo.SaveRAM`; sha256 `7f8b19b4a7d28dd558f36713ef5cb419539ff969c833e4713ad9df20b0db2e38` |
| `hgss.qa-hm` | `HG-HM` (a copy of `Pokemon HeartGold.nds`) | PKHeX.Core 26.7.7 on a copy of the owner's `Pokemon HeartGold.SaveRAM`, 2026-11-03: badge byte 0x0F (Zephyr, Hive, Plain, Fog), Furret (slot 3) taught Cut, Surf, Rock Smash and Strength, the player moved to the Route 32 Pokémon Center (map 169) at (8, 18), one tile above the exit mat, so the first step out loads Route 32's objects (a Continue restores the saved map's objects, not the header's; [research](../research/pathfinder-maps-flags-hms.md)). Never saved in-game. `HG-HM.SaveRAM` and its `.bak` in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-hm.SaveRAM`; sha256 `36fe6c172898191c747ef86e4038a016fe117aad4004bacecdeb337361ee2273` | 15613 / 35374 | `hgss-hm-rocksmash`, `-cut`, `-surf`, `-refused` |
| `hgss.qa-hm-mortar` | `HG-HM` | `hgss.qa-hm` plus all 16 badges (Johto and Kanto bytes 0xFF) and Pidgeot (slot 4) taught Waterfall, Whirlpool and Rock Climb; the player in the Route 42 Ecruteak gatehouse (map 132) at (9, 7). The first walk east fires the Suicune and Eusine cutscene at (434, 175) ([pathfinder spec](pathfinder-maps-flags-hms.md), open question). Never saved in-game. Pristine copy `../shinybothunterqasaves/hgss/qa-hm-mortar.SaveRAM`; sha256 `34f47129e1e6525c2298221539c9c1dd535fe3dcbca9e8e4bbad9665de369960` | 15613 / 35374 | `hgss-hm-strength`, `-waterfall`, `-rockclimb` |
| `hgss.qa-hm-r27` | `HG-HM` | Same party and badges as `hgss.qa-hm-mortar`; the player on Route 27 (header 31) at global (755, 396), below Tohjo Falls' east door; placed outdoors on purpose, since the whirlpool needs no object. Never saved in-game. Pristine copy `../shinybothunterqasaves/hgss/qa-hm-r27.SaveRAM`; sha256 `d8f5acb3bbcb6b55a6b80e58885c738021ca6613cc79a37a082c4146bcd2d8dd` | 15613 / 35374 | `hgss-hm-whirlpool` |
| `hgss.qa-primo-full` | `SS-Slugma` | as `hgss.qa-primo` with six Pokémon: the owner's save as made. `SS-Slugma.SaveRAM` and its `.bak` in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-primo-full.SaveRAM`; sha256 `66c693ef77dfd0419897acdbd38cc38bfad80f49dd54a560f4da28ba656658d3` | 60647 / 55034 | `hgss-primo-egg-refusals` |
| `hgss.qa-primo-tid2` | `SS-Slugma-TID2` (a copy of `SS-Slugma-Room.nds`) | as `hgss.qa-primo`. PKHeX on a copy of `hgss.qa-primo` set TID16 and SID16 and nothing else: it changed only 0x40074 to 0x40077 (the IDs in the active general block) and that block's checksum at 0x4F626; the backup block at 0x00000 keeps 60647 / 55034. `SS-Slugma-TID2.SaveRAM` and its `.bak` in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-primo-tid2.SaveRAM`; sha256 `029e0487c3f0630f6777c5c504cb35750485252fc0028fc8ccfff37f57439900` | 12345 / 54321 | `hgss-primo-egg-password` (`QA_PRIMO_ROM`) |
| `hgss.qa-primo-tid3` | `SS-Slugma-TID3` (a copy of `SS-Slugma-Room.nds`) | as `hgss.qa-primo-tid2`, with the other IDs. `SS-Slugma-TID3.SaveRAM` and its `.bak` in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-primo-tid3.SaveRAM`; sha256 `b47d605d7d4edc7dc9099da3aa82b328ba131c4fb5d1253bca5a6a0c4291c4e0` | 65535 / 1 | `hgss-primo-egg-password` (`QA_PRIMO_ROM`) |
| `hgss.qa-togepi` | `SS-Togepi` (a copy of `SS-Slugma-Room.nds`) | Violet Mart (map 157) at (6, 9) facing up, the Togepi hunt's start tile, Elm's aide present and the egg not yet received; five Pokémon. Made 2026-11-02 in two steps. PKHeX.Core 26.7.7 on a copy of `hgss.qa-primo` applied exactly what Elm's pick-up-egg call does (`scr_seq_0857_T22.s`, `scr_seq_T22_000`) and nothing else: flag 0x9F (`FLAG_GOT_PICK_UP_EGG_CALL_FROM_ELM`) set, var 0x4074 (`VAR_SCENE_VIOLET_CITY_OW`) 2, flag 0x197 (`FLAG_HIDE_VIOLET_SHOP_LAB_AIDE`) cleared, flag 0x246 (`FLAG_HIDE_ELMS_LAB_AIDE`) set (sha256 `701f6992c15bee4df18a3f7e56e7c8688c2cb9f87a2ceb1e2ebd3b91c788caf3`). Then one emulator session: Launch Bot + Continue, the Primo route's legs out to (469, 258), Up into the Mart, and `HGSS Togepi Egg`, whose own start-tile walk and save (`At the start position; saving`) wrote this save; the session was cancelled after `ReceiveTogepiEgg` gave the egg on this SoulSilver save, with no further save. `SS-Togepi.SaveRAM` and its `.bak` in `NDS\SaveRAM`; pristine copy `../shinybothunterqasaves/hgss/qa-togepi.SaveRAM`; sha256 `eb25eea7305e11748df9db86b38c362b1d7e35e5730586be50836e4444c1e3fc` | 60647 / 55034 | `hgss-togepi-egg-hatch` |
| `rs.qa-starter` | to make | Whatever the RS starter hunt expects | | the `rs-starter-*` scenarios |
| `rs.qa-jirachi` | `Pokemon - Sapphire Version (USA, Europe) (Rev 2)` (`AXPE` revision 2) | Trainer Phil in the field, Pokédex received (flag `0x801`), one party member, play time about 93 h, first-block counter 82; a byte copy of the spike's final hunt save, kept as `../shinybothunterqasaves/rs/qa-jirachi.SaveRAM` | 39541 / 17794 | `rs-jirachi-soak`, `rs-jirachi-shiny` (seed `ECDD` crafted by `jirachi-seed.ts` on a copy) |
| `rs.qa-jirachi-full` | `Pokemon - Sapphire Version (USA, Europe) (Rev 2)` | `rs.qa-jirachi` with six party members (PKHeX), kept as `../shinybothunterqasaves/rs/qa-jirachi-full.SaveRAM` | 39541 / 17794 | `rs-jirachi-setup-refusals` |
| `rs.qa-jirachi-nodex` | `Pokemon - Sapphire Version (USA, Europe) (Rev 2)` | `Pokemon Sapphire8.SaveRAM` as the owner made it: no Pokédex, one party member, kept as `../shinybothunterqasaves/rs/qa-jirachi-nodex.SaveRAM` | 39541 / 17794 | `rs-jirachi-setup-refusals` |
| `rs.rev0-base` | none: read in place, never installed (a rev 0 `Pokemon - Sapphire Version (USA)` save) | The spike's rev 0 base, 131088 bytes with the mGBA footer: every section of both blocks valid, section-0 counters 2 (block A) and 3 (block B, live), first-block seed `0A64`, live play time 0:15:13 frame 3, Pokédex set, one party member; kept as `../shinybothunterqasaves/rs/rs-rev0-base.SaveRAM`, sha256 `8aebc82d4453815c91ecd6a6667d3f2b1e01521f992431aabb4016b66458cc9e` | 39541 / 17794 | `rs-jirachi-saves` |
| `rs.seed-ecdd` | none: read in place, never installed | The spike's crafted shiny save, 131072 bytes with no footer: first-block seed `ECDD` (Jirachi PID `987ED636`, shiny); kept as `../shinybothunterqasaves/rs/rs-seed-ecdd.sav`, sha256 `3d86c956005d064dcf5684486502ddd002a02e245befc8d9f85fa540c7a82dc7` | 39541 / 17794 | `rs-jirachi-saves` |

The `README.md` in the saves folder lists each save and its state so it can be remade if lost.

Scenarios never save in-game, and the spike runs left both saves byte-identical to their backups. BizHawk still rewrites `SaveRAM` on close, so the harness should compare a save's hash before and after a run and fail if it changed. The exception is `rs-jirachi-soak`, which saves in-game by design: it installs a pristine copy, restores it afterwards, and compares the restored hash with the pristine one.

## Changelog
- 2026-11-10: `hgss-pickup-items-window` scenario, its run line, row and save use (#295).
- 2026-11-09: `hgss-pickup` and `hgss-pickup-take` for the visit after every won battle: the `take|nothing to take` count line, one trip line per decision window, 30 won battles, the 15 s and 6 s bounds (#289).
- 2026-11-08: the older preconditions name the backend's ROMBaseDirectory instead of a machine path (#279).
- 2026-11-08: `hgss-pickup`, `-take` and `-greyed` scenarios, their run lines and preconditions, `pickup.ts`, `egg-pid.ts`'s held item and ability, and the `hgss.qa-pickup` and `hgss.qa-pickup-held` saves, for P5 (#278).
- 2026-11-03: `hgss-hm-rocksmash`, `-cut`, `-surf`, `-refused`, `hgss-flag-route` and `hgss-map-rebuild` rows and the `hgss.qa-hm` save, for the pathfinder spec (phase 11); later the same day `hgss-hm-strength`, `-waterfall`, `-rockclimb`, `-whirlpool` and the `hgss.qa-hm-mortar` and `hgss.qa-hm-r27` saves.
- 2026-11-02: `hgss-primo-egg-password` picks its save with `QA_PRIMO_ROM` and computes the password from the trainer ID in RAM (`primo-password.ts`); the `hgss.qa-primo-tid2` and `-tid3` rows; for E9 (#261).
- 2026-11-02: `hgss-primo-egg-refusals`, `-password`, `-hatch`, `-shiny` and `hgss-togepi-egg-hatch` scenarios, their run lines and preconditions, `egg-pid.ts` and `driver.ts`'s `writeU8` and named recordings; the `hgss.qa-primo`, `hgss.qa-primo-full` and `hgss.qa-togepi` rows; the Primo and hatch files in the `egg` area; the Togepi and Primo hatch rows log the day care counter, not a countdown; for E6 (#246).
- 2026-10-28: the QA agent may write a scenario's milestone straight to Mongo, and only specs under `ShinyBotHunter.Qa/` may name one (owner, on #236); the Headbutt scenarios find their milestone by species.
- 2026-10-28: the six `hgss-headbutt*` scenarios and the shared `headbutt.ts`, their run lines and preconditions, for H4 (#228).
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
