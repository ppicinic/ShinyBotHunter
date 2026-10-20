# QA Automation Spec

Status: Draft
Last updated: 2026-10-17
Related: [Improvement plan](../plans/2026-10-cleanup-and-hardening.md), [System architecture](../architecture/system-overview.md), [Engineering principles](../engineering-principles.md), [Pointer discovery spike](../research/pointer-discovery.md)

## Summary

Unit tests cover parsers and arithmetic, but the thing that breaks is the loop between the backend, ApiHawk and a running emulator. This spec adds a QA layer where an agent (a Claude Code session or a script) drives the real app and real emulators through the public API, forces a shiny inside the emulator so the real code path runs to completion in minutes rather than hours, picks which scenarios to run from what changed, and leaves screenshots and logs as evidence.

The previous attempt at this, a `ForceShinyPercent` switch inside `StarterHuntEvent` that treated a normal find as shiny, was removed in August 2026 because a QA branch in production code can park fake finds in a real hunt. This design keeps production code free of QA branches: the shiny is real as far as the backend is concerned, because it is injected into the game.

## Goals

- G1: a scenario like "wild encounter on HGSS finds a shiny, parks, resume records the catch" runs unattended end to end in under 10 minutes.
- G2: no QA-only behaviour in `ShinyBotHunter/`. QA lives in its own project and in ApiHawk endpoints that are harmless in normal use.
- G3: a change to area X runs only the scenarios mapped to X, by default. The full set runs on request.
- G4: every run leaves a folder with a log, screenshots at each checkpoint, and the final dashboard state, so a passing run is reviewable and a failing one is diagnosable.
- G5: scenarios are plain data plus small C# steps, so adding one does not mean writing a framework.

## Non-goals

- Replacing unit tests. They stay the first line for anything testable without an emulator.
- Testing the Electron UI pixel by pixel. The harness drives the REST and SignalR API; the dashboard is covered by one smoke scenario that opens it and screenshots each window.
- Running in GitHub Actions. This needs a Windows box with BizHawk and ROMs. It runs on the dev machine or a local runner (see the CI decision).

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | A `ShinyBotHunter.Qa` console project runs a named scenario against a running backend. | Must | `dotnet run --project ShinyBotHunter.Qa -- run hgss-wild-shiny-park-resume` exits 0 on pass, 1 on fail. |
| FR-2 | The harness can force the next encounter or starter to be shiny for the bot at position N. | Must | After the force, the backend reports the encounter as shiny with `ShinyValue < 8` computed from real TID/SID/PID. |
| FR-3 | The harness can capture an emulator screenshot and a dashboard screenshot at any checkpoint, using external capture tools, not emulator or fork changes. | Must | PNG files appear in the run folder named by checkpoint. |
| FR-4 | `qa select --changed` lists the scenarios mapped to files changed versus `main`. | Must | Editing `PostgreSQLEncounterDao.cs` selects the counts scenarios; editing `NavigatorEvent.cs` selects the navigation scenario. |
| FR-5 | Each run writes `qa-runs/<timestamp>-<scenario>/` with `log.txt`, screenshots, `dashboard-state.json`, and `result.json`. | Must | |
| FR-6 | The harness launches and tears down its own backend and emulators, against a QA database, so a crashed run does not leave a process or a row behind. | Should | After any exit, no `EmuHawk.exe` from the run remains; the QA database is empty or reset. |
| FR-7 | Optional screen recording of the emulator windows for the whole run, via an external recorder. | Could | An `.mp4` in the run folder when `--record` is passed. |
| NFR-1 | A scenario file is under 60 lines and readable without knowing the harness. | Must | |
| NFR-2 | The harness adds no dependency to `ShinyBotHunter/`. | Must | |
| NFR-3 | No capture or recording feature is added to ApiHawk or the BizHawk fork; capture is done from outside both repos. Run output is never committed. | Must | `qa-runs/` is gitignored; `git status` is clean after a run. |

## User-facing behaviour

### Running
```
dotnet run --project ShinyBotHunter.Qa -- list
dotnet run --project ShinyBotHunter.Qa -- select --changed          # which scenarios the diff touches
dotnet run --project ShinyBotHunter.Qa -- run hgss-wild-shiny-park-resume [--record] [--keep]
dotnet run --project ShinyBotHunter.Qa -- run --changed              # run the selected set
dotnet run --project ShinyBotHunter.Qa -- run --all
```
`Scripts/ci-local.ps1 -Qa` calls `run --changed` after the unit tests.

### A scenario
Scenarios are YAML under `ShinyBotHunter.Qa/Scenarios/`. Steps are a fixed vocabulary implemented in C#; no scripting language.

```yaml
name: hgss-wild-shiny-park-resume
description: Spin encounter on HGSS; a forced shiny parks the bot; Resume records the catch and the counts move.
areas: [bot-engine, encounter, persistence, dashboard]
game: HGSS
rom: Pokemon HeartGold           # handle from /api/getAllGames
timeoutMinutes: 10
steps:
  - launch-bot: { continueGame: true }
  - start-action:
      name: Wild Encounter
      args: { PhaseName: QA Route 29, TargetType: ShinyTarget, EncounterType: Spin }
  - wait-state: { state: Running, timeoutSec: 60 }
  - screenshot: running
  - force-shiny: { mode: next-wild }
  - wait-state: { state: Waiting, timeoutSec: 240 }
  - screenshot: parked
  - assert-latest-encounter: { isShiny: true }
  - start-action: { name: Resume }
  - wait-state: { state: Complete, timeoutSec: 120 }
  - assert-counts: { phase: QA Route 29, shinyEncounters: 1, shiniesCaught: 1 }
  - screenshot: done
  - cancel-bot
```

### Step vocabulary (initial)
| Step | Does |
|---|---|
| `launch-bot`, `cancel-bot` | `POST /api/startTopLevelAction`, `POST /api/bot/cancelAction` |
| `start-action` | `POST /api/bot/startAction/{pos}` |
| `start-special` | `POST /api/startTopLevelAction` for multi-emulator hunts |
| `wait-state` | poll `GET /api/bot/getDashboardState` until the position reports the state, or fail |
| `force-shiny` | see Shiny injection below |
| `screenshot` | emulator PNG by capturing the EmuHawk window from outside (see Evidence), dashboard PNG via the harness's Electron driver if the dashboard is up |
| `assert-latest-encounter`, `assert-counts`, `assert-party` | read `/api/phaseinfo/...` and `/api/bot/getParty/{pos}` and compare |
| `assert-log-contains` | search the backend's captured stdout |
| `sleep` | last resort, must carry a reason |

## Technical design

### Projects and placement
- `ShinyBotHunter.Qa/` (net10 console). References `ShinyBotHunter` only for the DTO types and `Gen3PokemonParser`; talks to the backend over HTTP. Owns process lifetime of the backend and uses the backend's own launcher through the API.
- `ApiHawk/` gains one thing: memory opcode `0x05`, write a byte range in one frame, so the injector can write a struct atomically. Byte writes (`0x02`) already exist. No capture endpoints: screenshots and recordings are taken from outside the emulator (decided 2026-10-17, see Evidence).
- `ShinyBotHunter/` gets no QA code. The one accommodation is a QA `appsettings.Qa.json` the harness passes via environment (`DOTNET_ENVIRONMENT=Qa`): separate Postgres database and Mongo database names, a QA ROM directory with the QA save files, Discord disabled.

### Shiny injection
Two mechanisms, chosen per scenario. Both produce a Pokemon the real parser reads as shiny from real TID/SID/PID, so the backend's own logic decides what to do with it.

**A. RNG seed write (preferred for soft-reset hunts, Gen 3 first).** Gen 3 generates a starter's PID from consecutive LCRNG outputs (`seed = seed * 0x41C64E6D + 0x6073`, PID high and low halves from two calls). Given the save's TID and SID (readable from SaveRAM, `Gen3SaveFile` already finds the block), the harness searches seeds until one yields `TID ^ SID ^ PIDhi ^ PIDlo < 8`, and writes it to `gRngValue` right after the game resets and before the starter is generated. The game then generates a legitimately shiny starter. Nothing is patched after the fact, no checksum to fix, legality passes.
- Needs: the `gRngValue` address per Gen 3 game (from the pret symbol maps, see the [pointer spike](../research/pointer-discovery.md)) and the exact frame window. The harness writes repeatedly each frame between "reset" and "starter on field" until the PID is observed.
- HGSS: the same idea with the Gen 4 LCRNG and the "Method 1 / J" PID derivation, plus the delay and seed-from-date mechanics. Harder; do Gen 3 first and treat HGSS RNG injection as a follow-up.

**B. Struct patch (for mid-battle wild encounters).** When a wild battle starts, the foe's data is in the battle Pokemon struct. The harness reads the current encrypted block through the existing pointer reads, decodes it with `Gen3PokemonParser.Decode` (or the HGSS parser), picks a PID that is shiny for the trainer's TID/SID and keeps the same nature and gender bits where the scenario wants them, re-encrypts with the new PID as the key (the substructure order also depends on PID, so re-scramble), fixes the checksum, and writes it back with the new write-range opcode in one frame. The tests already prove round-trip encode/decode for Gen 3 (`Gen3PokemonParserTests.RoundTrips_UnderEverySubstructurePermutation`), so the encoder is half-written.
- For HGSS the battle struct is unencrypted (`BattlePokemon` reads it directly), which makes B simpler there: write the PID field.
- Caveat: a patched foe is not "legal" in the RNG sense; `LegalityService` may flag it. Scenarios that assert legality must use A.

Both mechanisms live in `ShinyBotHunter.Qa/Injection/`, one class per game flavor, behind a small `IShinyInjector` with `ForceNextWildAsync(pos)` and `ForceNextStarterAsync(pos)`.

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
`select --changed` diffs the working tree and the current branch against `main`, maps files to areas, and returns every scenario sharing an area. A change outside every glob (docs, scripts) selects nothing; `--all` overrides. The `apihawk` and `bot-engine` areas should map to a broad set, since everything passes through them.

### Evidence
Decided 2026-10-17: capture is done with external tools, with no changes to ApiHawk or the BizHawk fork, and nothing from a run is committed.

- **Emulator screenshots**: the harness finds each EmuHawk window by process id (it launched them through the backend, and the launcher already locates windows by handle) and captures its client area with a Win32 `PrintWindow`/`BitBlt` call from the harness itself, or shells out to a small PowerShell capture. Either is a few lines and needs nothing installed.
- **Dashboard screenshots**: Playwright's Electron page screenshot.
- **Recording (`--record`)**: `ffmpeg -f gdigrab` on each EmuHawk window title, started at launch and stopped at teardown, writing `bot<pos>.mp4`. ffmpeg is a dev-machine prerequisite, not a repo dependency. OBS is an alternative if a scene is already set up, but the harness only automates ffmpeg.
- **Nothing is committed.** `qa-runs/` is gitignored, including `history.csv`. Keep runs locally or copy a folder somewhere else when sharing a failure.

Run folder layout:
```
qa-runs/2026-10-17T21-04-11-hgss-wild-shiny-park-resume/
  log.txt                 harness log with timestamps per step
  backend.log             backend stdout
  01-running.png          emulator screenshot (per bot position if several)
  01-running-dashboard.png
  02-parked.png ...
  dashboard-state.json    final /api/bot/getDashboardState
  counts.json             final /api/phaseinfo for the scenario's phase
  result.json             pass/fail, duration, failing step, assertion detail
  bot0.mp4                screen recording when --record
```
The folder is gitignored. A summary line per scenario is appended to `qa-runs/history.csv` so trends are visible.

### Dashboard driver
One scenario (`dashboard-smoke`) starts the Electron app with Playwright's Electron support, waits for the admin window, subscribes to the scenario's phase, and screenshots each window after an encounter is pushed over SignalR. All other scenarios run headless against the API; the dashboard is optional.

### Initial scenario set
| Scenario | Areas | Mechanism |
|---|---|---|
| `hgss-wild-shiny-park-resume` | encounter, bot-engine, persistence | B (HGSS battle struct) |
| `hgss-wild-phase-target-count` | encounter, persistence | none (counts after N encounters) |
| `hgss-navigate-heal-return` | navigation | none |
| `hgss-togepi-egg-hatch` | egg | B at hatch, or none for a plain hatch |
| `rs-starter-wave-shiny-park` | starter-hunt, bot-engine | A (seed write) |
| `rs-starter-double-find` | starter-hunt (the race the review found) | A on two emulators |
| `api-concurrent-launch` | bot-engine (plan task 2.2) | none; two launches at once |
| `counts-cold-load` | persistence (plan phase 3) | none; measures first-load time against the QA database seeded with 1M rows |
| `dashboard-smoke` | dashboard | none |

## Test cases for the harness itself
| Case | Setup | Expected |
|---|---|---|
| Seed search finds a shiny seed | TID/SID fixed | PID from the seed has `ShinyValue < 8` |
| Gen 3 re-encrypt round-trips | random block, new PID | decode(encode(x)) == x with new PID, checksum valid |
| Area mapping | changed `NavigatorEvent.cs` | selects `hgss-navigate-heal-return` only |
| Teardown on failure | step throws | no EmuHawk from the run remains |

## Open questions
### QA save files (owner is producing these)
Decided 2026-10-17: the owner will make the saves. They live outside the repo in the QA ROM directory (`appsettings.Qa.json` → `ROMBaseDirectory`), named `<rom handle>.qa-<scenario>.sav`, and the harness copies the one a scenario names next to the ROM copy before launch. What each scenario needs:

| Save | Game | State when loaded | Used by |
|---|---|---|---|
| `hgss.qa-route29` | HGSS | Standing in Route 29 grass, Repel active or a high-level lead so nothing else interrupts, party has a free slot, a battler that can faint or flee a level 2-4 wild | `hgss-wild-shiny-park-resume`, `hgss-wild-phase-target-count` |
| `hgss.qa-heal` | HGSS | A few tiles outside the Cherrygrove Pokemon Center, lead Pokemon at low HP, party poisoned if easy | `hgss-navigate-heal-return` |
| `hgss.qa-togepi` | HGSS | Just before receiving the Togepi egg, party of 5 or fewer | `hgss-togepi-egg-hatch` |
| `rs.qa-starter` | RS | Fresh save at the point `CreateSaveEvent` expects, or none if the hunt creates its own (confirm which the RS hunt uses) | `rs-starter-wave-shiny-park`, `rs-starter-double-find` |
| `hgss.qa-idle` | HGSS | Anywhere safe, used only for launch/cancel and dashboard scenarios | `api-concurrent-launch`, `dashboard-smoke` |

Each save's trainer ID and secret ID get recorded in the scenario file once known, since the shiny injector needs them. A short `README` in the QA ROM directory lists the saves and their state so they can be re-made if lost.

Decided 2026-10-17: no capture support in the fork or ApiHawk; external tools only, results not committed. The QA database is a separate database on the same local Postgres and Mongo (the dev PC is not the streaming PC, so sharing the server costs nothing); the cold-load scenario seeds its 1M rows into that database once and reuses them.

## Changelog
- 2026-10-17: first draft.
