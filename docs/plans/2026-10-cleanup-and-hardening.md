# Cleanup and Hardening Plan

Status: Draft
Last updated: 2026-10-17
Related: [Code review 2026-10](../reviews/2026-10-code-review.md), [System architecture](../architecture/system-overview.md), [Engineering principles](../engineering-principles.md), [the CI decision](../decisions/global.md#ci-runs-locally-before-merge-not-on-github-actions)

## Goal

Turn the October 2026 review into shippable work, and lay the two foundations for growth (a game profile per flavor and an event registry, see architecture section 11) once the cleanup has made room for them. Order is what the owner asked for: a safety net first (local CI), then dead code out, then the two correctness/performance problems (API locking, slow count loads), then tests, then structural splits. Each phase leaves `main` working and is a separate PR or small set of PRs.

Nothing here adds a tier, a store, or a framework. Where a task deletes code, that is the deliverable.

## Scope

In: the .NET backend, the Electron dashboard, ApiHawk fixes that affect correctness, test and CI scripts.
Out: new hunt features, new game flavors, UI redesign, Discord changes.

## Phases

### Phase 0: Safety net (do first, half a day)
Outcome: one command runs everything CI would, and GitHub stops billing.

| # | Task | Details | Depends on | Size | Done when |
|---|---|---|---|---|---|
| 0.1 | Make the GitHub workflow manual-only | `on: workflow_dispatch` only in `.github/workflows/build-and-test.yml`. Keep the steps. | — | S | No run triggers on push or PR. |
| 0.2 | Add `Scripts/ci-local.ps1` | restore, build Release, test on `ShinyBotHunter.Tests`; then `npm ci`, typecheck, build in `PokemonDashboard`. Fail fast, print a one-line summary. Workflow calls this script. | — | S | Script passes on `main`. |
| 0.3 | Dashboard `typecheck` script | `tsc --noEmit -p tsconfig.node.json && tsc --noEmit -p tsconfig.web.json` in `package.json`. Fix whatever it finds. | — | S | `npm run typecheck` is clean. |
| 0.4 | Run the script before every merge | Note in the PR template or description. | 0.2 | — | Habit. |

### Phase 1: Dead code removal (one or two PRs)
Outcome: the repo only contains code that runs. Net negative lines. Each bullet is one commit so a mistake is one revert.

| # | Task | Details | Depends on | Size | Done when |
|---|---|---|---|---|---|
| 1.1 | Template and experiment leftovers | Delete `Controllers/WeatherForecastController.cs`, `Test.cs`, `EmptyTestLauncher`, `Startup.TestAsync`/`Test2Async`/`to_s16`, the commented block in `MainRunnerAsync`, unused usings in `Startup.cs`. | 0.2 | S | Builds; tests pass. |
| 1.2 | Duplicate events | Delete `WildPokemonEncounterEvent`, `RunEncounterEventDeprecated`, `EncounterEventTypeFactory` (and its registration); point anything using the factory at `RunEncounterEvent`. | 1.1 | S | Builds; a wild encounter still runs (manual check). |
| 1.3 | Lua path | Delete `Lua Scripts/`, `BizHawkLuaLoader`, `BizHawkLauncher` (the Lua one), `Emulators/BizHawkClient.cs`, `BizHawkMode` and `LuaBaseDirectory` settings and the branch in `BotFactory`. `BizHawkApiHawkLauncher` becomes the only launcher. | 1.1 | M | Builds; launch works; `ApplicationSettings` has no Lua keys. |
| 1.4 | InfluxDB | Delete `Persistence/InfluxDb/`, `Scripts/MigrateInfluxToPostgres.cs`, the `MigrateInfluxSchemav1Tov2` class, both `--migrateinflux*` CLI modes, the `InfluxDB3.Client` package, `InfluxDbUrl`/`InfluxDbDatabase`/`InfluxToken` settings. | 1.1 | S | Builds; `IEncounterDao` has one implementation. |
| 1.5 | Setup scripts out of `Startup` | `PokeApiParser` and `Scripts/Maps/*` stay (they set up metadata and map data on a new PC). Move the CLI branching into a `Tools/SetupCommands.cs` entry that runs before the host and without starting Discord or the bot service; make the `D:\\...` input paths arguments; collapse the duplicate `--parsepokemoveapi` flag; delete `MapHeaderProcessor` and `ExtractMovePermissionsFile`. | 1.1 | M | `dotnet run -- --parsepokeapi` works on a machine with only Mongo running; `Startup.MainAsync` is the host plus one `if (SetupCommands.TryRun(args))`. |
| 1.6 | Dead members | `IBotEvent.EventState` and every throwing override; `Bot.Reader`; `ChainedOrchestratorEvent.legLock`; `MultiShinyHunterEvent._locker`; `HgssPokemonParser.cache`; `EggHuntConfig` summary fields and `ShowSummaryAsync`; `BizHawkLauncher.ArrangeWindowsAsync` (if not already gone with 1.3); unused TS types in `types/api.ts`; the no-op "expose refresh" effect in `AdminCard.tsx`. | 1.2, 1.3 | S | Builds; tests pass. |
| 1.6b | Smaller finds from the sweep | `WeatherForecast.cs`, `ShinyBotHunter.http`; `Scripts/Maps/MapHeaderProcessor.cs` and `MapProcessor.ExtractMovePermissionsFile`; uncalled methods (`RsDataReader.GetPartyCount`, `RomInfoService.FindByFlavor`, `LegalityService.LogVerdict`, `PathfindingService.FindPortalToMap`, `StarterHuntEvent.AdvanceIntoBattleAsync`, both `WriteToQueue`); unused fields (`BotService.bizHawkMode`, `BizHawkMemoryClient.localMemoryOnly`); nested `ShinyBotState`, `GraphData`, `MapValues`; `TogepiEgg.json` summary-touch keys; commented blocks listed in the review; `Microsoft.AspNetCore.OpenApi` and direct `Npgsql` package refs; add an explicit `Newtonsoft.Json` reference; remove the duplicate `SmogonDamageService.DisposeAsync` call; move `ShinyAlertService` to namespace `ShinyBotHunter.Service`. | 1.1 | S | Builds; tests pass. |
| 1.6c | Duplicate metadata classes | Delete the `PokemonMetadata`/`EvolutionMetadata`/`PokemonFormMetadata` copies in `Scripts/PokeApiParser.cs` and have the scraper use the runtime classes from `Persistence/MongoDb/`. Keeps the scraper and the app writing the same shape. | 1.5 | S | One definition of each class. |
| 1.7 | Stale docs and files | `PokemonDashboard/docs/MULTI_WINDOW_EXECUTION_PLAN.md` header to "Implemented"; `README.md` grid section; stray `electron.vite.config.*.mjs`; the `ApiHawk.csproj` "manually copy" comment. | — | S | |

Decided 2026-10-17: the setup scripts stay (new-PC provisioning), the Lua path goes. The Influx migration scripts go with 1.4; a new PC has no Influx data to migrate.

Note for 1.3: the Lua removal does not touch the BizHawk fork; ApiHawk still needs it (architecture section 8).

### Phase 2: API concurrency (one PR, the correctness fix)
Outcome: two simultaneous dashboard actions cannot corrupt the bot registry, double-launch, or double-resume.

| # | Task | Details | Depends on | Size | Done when |
|---|---|---|---|---|---|
| 2.1 | Fix `EventTask` | Replace `ContinueWith(async ...)` with a proper async wrapper method (`RunEventAsync`) so `EventTask` is the real task and continuation exceptions are observed. `BotService.cs:730-788`. | 1.x | S | `CancelAsync` returns after cleanup and refresh. |
| 2.2 | Serialise `BotService` | One `SemaphoreSlim(1,1)` field, `AcquireAsync` helper. Hold it across each public method that touches `botEvents` or a `BotAndOrEvent`'s fields, including the whole of "compute position → launch → add", and in the completion path from 2.1. Document the rule at the field per principle 2.9. | 2.1 | M | A test launching two bots concurrently against a fake launcher gets positions 0 and 1 and no exception. |
| 2.3 | Atomic `SwapState` | Compute then replace under the same lock, or make `GetAvailableActions` treat an in-flight swap as busy. | 2.2 | S | The idle check cannot see an empty stack mid-swap. |
| 2.4 | Await Resume | `BotService.cs:301`: await the RS hunt's resume, or guard with a per-hunt "resume in progress" flag under the lock. | 2.2 | S | Two Resume clicks produce one catch. |
| 2.5 | Correct key on special-event completion | Remove by the registry key captured at start, not `GetBotPositions()[0]`. | 2.1 | S | A crashed hunt frees its slot. |
| 2.6 | `AdminService` and `PhaseInfoService` | A `SemaphoreSlim` each, or `ConcurrentDictionary` + copy-on-write for the phase name lists. | — | S | |
| 2.7 | Cancellation tokens through reads | Thread `ct` through `WaitForMacroAsync`, `FlushSave`, `ReadValidatedPartyAsync` and `ExecuteWithRetryAsync`; await the losing poll tasks in `WaitForMoveEnd` before dispose. | — | M | Cancel on a dead emulator returns in under a second. |
| 2.8 | Supervise background loops | Convert the three `Task.Run` loops (`PostgreSQLEncounterDao`, `MongoCaughtPokemonDao`, `PokemonPersistenceService`) to `BackgroundService` or wrap them so a fault is logged and the loop restarts. | — | S | Killing Postgres mid-run logs an error and resumes writing when it is back. |
| 2.9 | Close the hub hole | Remove `ElectronHub.SendPokemonEncounterMessageAsync` or make it server-only. | — | S | |
| 2.10 | Dashboard: disable while in flight | Action buttons in `BotControlTab` disable until the start/cancel/resume request returns, and show the 400 message inline. The lock is the real guard; this stops the double-click from being possible in normal use. | 2.2 | S | A fast double-click sends one request. |

### Phase 3: Count query performance (one or two PRs)
Outcome: a cold dashboard load of a phase is a handful of round trips, each an index scan.

| # | Task | Details | Depends on | Size | Done when |
|---|---|---|---|---|---|
| 3.1 | Measure | Baseline is known: 1 to 2 million rows, 10 to 30 s cold load. Record the exact row count and the `EXPLAIN ANALYZE` of one `LongCount` before 3.2 so the index gain is visible. Target: cold phase load under 1 s. | — | S | Numbers in the PR. |
| 3.2 | Indexes | EF migration adding `(Game, Milestone, Timestamp DESC)` and `(GameFileName, Milestone, Timestamp DESC)`, each `INCLUDE (IsShiny, FormCode, Species)`; consider a partial index `WHERE IsShiny`. On Timescale, create them on the hypertable so chunks inherit. | 3.1 | S | `EXPLAIN` shows index scans; re-measure. |
| 3.3 | One grouped load per game | Replace the ~25 `IEncounterDao.GetAll*` methods with two: `GetCountsForGame(game)` returning `(Milestone, GameFileName, FormCode, Species, Count, ShinyCount)` rows via `COUNT(*) FILTER (WHERE "IsShiny")` grouped, and `GetCurrentPhaseCounts(game)` as one window query. Build the whole `GameEncounterCache` tree from those rows in `LoadGameCache`; delete the per-leaf loaders and the per-leaf semaphore dance. | 3.2 | L | First load is ≤ 4 Postgres queries; `PokemonPersistenceService` shrinks by ~300 lines; a unit test builds the tree from a fake row set. |
| 3.4 | Encounter rate in one query | Merge the bins query and the min/max query; delete the `gameFileName` duplicate by making the filter optional. | 3.2 | S | |
| 3.5 | One Mongo aggregation for caught shinies | Replace the eleven `GetAllCaught*CountAsync` methods with one `$group` aggregation per game keyed by phase, game file, form code and species, consumed by the same tree builder as 3.3. Keep the per-game `GetAllCaughtShiniesAsync` list for the dashboard. | 3.3 | S | First load issues one Mongo query. `MongoCaughtPokemonDao` shrinks. |

Decided 2026-10-17: MongoDB stays for schema-fluid data (caught shinies, metadata, phases, hunt state). Postgres is for the high-volume encounter log only. See [decisions](../decisions/global.md#storage-postgres-for-encounters-mongodb-for-schema-fluid-data).

### Phase 4: Tests (ongoing, interleave with 2 and 3)
Outcome: the parts that are testable without an emulator have tests, in value order.

| # | Task | Details | Size |
|---|---|---|---|
| 4.1 | `BotService` concurrency test | Fake `BotFactory`/launcher; two concurrent launches; cancel during completion. Written as part of 2.2. | M |
| 4.2 | `HgssPokemonParser` round-trip | Mirror `Gen3PokemonParserTests`. | M |
| 4.3 | `RsPointerService` memory map | Mirror `PokemonStateReaderTests` for RS using `FakeMemoryClient`. | S |
| 4.4 | Counts cache from rows | Table-driven test for 3.3's tree builder. | S |
| 4.5 | `BizHawkReaderWriterLock` | Reader/writer exclusion and no self-deadlock. | S |
| 4.6 | `StarterHuntStateStore` round-trip | Temp directory load/save of parked and retired copies. | S |
| 4.7 | Pathfinding | Small hand-built graphs; shortest path, blocked tiles, capability gating. | M |
| 4.8 | Fix the two found bugs with tests | `WillEvolve` braces; `GetLevelUp` bounds. | S |
| 4.9 | Dashboard | Add vitest; test `actionSchema.ts` helpers once split out (5.2). Add eslint with the default TS config. | M |

### Phase 4b: QA automation (its own spec, start after phase 2)
Outcome: an agent can run the real app against real emulators, force a shiny, and leave evidence. Full design in the [QA spec](../specs/qa-automation.md).

| # | Task | Details | Depends on | Size |
|---|---|---|---|---|
| 4b.1 | `ShinyBotHunter.Qa` skeleton | Console project, scenario YAML loader, step vocabulary for launch/start/wait/cancel/assert over the REST API, run folder with log and `result.json`. Port the spike harness's steps (it proved launch, positioning, flee counting and park detection). | 2.2 | M |
| 4b.3 | Cheat files and arming | Store cheats under `ShinyBotHunter.Qa/Cheats/<game>-<region>-<revision>/`, pick the folder from the ROM's game code and revision, copy the scenario's cheat into the QA install, loaded disabled, with `Equal` guards; arm with the Toggle All Cheats hotkey; `assert-cheat` by reading the patched bytes. HGSS rev 0 wild patch and RNG freeze from the spike. First scenario: `hgss-route33-shiny-park`. | 4b.1 | M |
| 4b.4 | RS cheats | Map the RS RNG state and wild generator from `pret/pokeruby` (6.1), then an RNG-freeze starter cheat. First scenario: `rs-starter-wave-shiny-park`. | 4b.3, 6.1 | M |
| 4b.5 | Scenario selection | `qa-map.yaml` areas, `select --changed` against `main`, `ci-local.ps1 -Qa`. | 4b.1 | S |
| 4b.6 | QA environment | `Scripts/qa-load-save.ps1` (copies a save from `../shinybothunterqasaves/` into the configured BizHawk Save RAM folder) plus its allow rule in `.claude/settings.json`; QA BizHawk install config (hotkey, cheat auto-save off, Lua off), user-secrets QA config with a localhost guard, migrations applied by the harness, save hash check, teardown that kills stray EmuHawk and backend. Setup is written down in the QA spec. | 4b.1 | S |
| 4b.7 | Remaining initial scenarios | navigation, egg, double-find race, concurrent launch, cold-load timing, dashboard smoke (Playwright Electron). | 4b.3, 4b.4 | L |
| 4b.8 | External capture | `PrintWindow` with full-content rendering for screenshots and a 15 fps recording piped into ffmpeg (`gdigrab` records BizHawk blank); Playwright for dashboard screenshots. `qa-runs/` gitignored. | 4b.1 | S |

### Phase 6: Pointer discovery spike (research, no code)
Outcome: every observed constant the bot reads has a name and a meaning, and the QA injector has its RNG addresses. Full brief in the [spike doc](../research/pointer-discovery.md).

| # | Task | Details | Depends on | Size |
|---|---|---|---|---|
| 6.1 | Gen 3 symbols from `pret/pokeruby` | Table of every RS address the bot uses → symbol, for the RS revisions in use (1.1 and 1.2; 1.0 only when a hunt needs it); `gRngValue` and the starter PID frame window. | — | M |
| 6.2 | HGSS symbols from `pret/pokeheartgold` | Done 2026-10-20 for battle and field state: the `FieldSystem` chain, battle app states, server commands, menu states, scripts, experience and move-learn prompts, outcome, verified live. See the spike doc. Remaining: the battle UI input-ready flag. | — | L |
| 6.3 | Reverse Engineer Anything MCP pass | Only where the decomp is incomplete: the post-battle evolution module (`asm/unk_020755E8.s`, state inferred at `+0x64`) and games without a decomp. HGSS battle bytes are already explained by 6.2. | 6.2 | S |
| 6.4 | Feed into the game profile | Replace constants with named symbols as part of 5.3. | 5.3 | — |

### Phase 5: Structural splits (after 1-3, one file per PR)
Outcome: no file over ~500 lines without a reason, and one definition per fact.

| # | Task | Details | Size |
|---|---|---|---|
| 5.1 | `BotService` split | `EmulatorSlotRegistry` (positions + lock from 2.2), `BotEventRunner` (task supervision), DTOs to `Service/BotDtos.cs`. | M |
| 5.2 | `AdminCard.tsx` split | Per the review's table: `admin/actionSchema.ts`, `ActionParamFields.tsx`, `DashboardConfigTab.tsx`, `BotControlTab.tsx` (+ `EmulatorCard`, `TopLevelActionForm`, one reducer), `PhaseEditorTab.tsx`, `apiClient.ts`. No behaviour change. | L |
| 5.3 | Game profile per flavor | Introduce `IGameProfile` (pointers, parser, battle parser, battle-state constants, capability flags, macro set) registered keyed by flavor; `GameServices` returns it. Move every inline HGSS address and the battle-detect bytes into the HGSS profile. Register one Gen 3 parser for RS now, so Emerald or FireRed later is a pointer service plus a registration. Address tables are keyed by ROM revision where they differ (`RomInfo.Revision` is already known at launch): current hunts are on 1.1/1.2; other revisions are added only when a hunt needs them. Events read only from the profile. | L |
| 5.3b | Capabilities instead of `NotSupported` | Replace the throwing members of `RsDataReader` with capability flags on the profile (`HasOverworldNavigation`, `HasEggs`, `CanReadBoxes`, ...); `BotService` refuses an unsupported action with a 400 before the event starts. | S |
| 5.3c | Map and graph services per flavor | `MapService` and `GraphService` stop being singletons; the HGSS profile owns its instances loaded from `Data/HGSS`. A flavor without map data reports `HasOverworldNavigation = false`. | M |
| 5.3d | Event registry | Each event type (`WildEncounter`, `ChainedEncounter`, `TogepiEgg`, `StarterHunt`, `HgssStarterHunt`, `LaunchBot`) becomes a registry entry: name, `EventArgumentOptions`, required capabilities, owns-emulators flag, factory. `BotService.ExecuteEvent`/`ExecuteNewBotOrSpecialEvent` become a lookup; `GetAvailableActions` filters the registry by the bot's profile; `SpecialEvents.json` either feeds the registry or is deleted. Do this after 5.1 so the registry lands in a small `BotService`. | L |
| 5.3e | HGSS game-state reader | One small class on the game profile that reads the verified chain in one frame (ApiHawk pointer-chain opcode) and returns where the game is: field free, field task, battle at menu, move list, message waiting, script playing (which), experience or move-learn prompt, battle over (outcome), evolution, other app. Replace `0x021E76D2`, anchor `+0x230EC`, `+0x692C`, `+0x6930` reads with it; bound every wait loop by "state unchanged for N seconds"; confirm each button press by the state moving. Needs 6.2's input-ready flag for menu presses, or confirm-and-retry until then. | 5.3, 6.2 | M |
| 5.4 | `BattleOrchestratorEvent` split | Extract move-learn/evolution flow and the Discord prompt into their own classes. | M |
| 5.5 | `StarterHuntEvent` split | Extract save-file management (`EnsureSaveExists`, `WipeSaveSlot`, trainer-ID scan) into `SaveFileManager`. | M |
| 5.6 | Narrow `IBot` | Events receive `ICommandClient + IMemoryClient + GameFlavor`; launch/dispose stay with `BotService`. Drop `IDataReader` from `IBot` and resolve it by flavor where needed. | M |
| 5.7 | Shared DTO shapes | Register `JsonStringEnumConverter` for controllers and SignalR; drop the int/string hedges in `types/api.ts`. Optionally generate `types/api.ts` from the OpenAPI document. | S/M |
| 5.8 | Dashboard security tidy-up | REST through the main process via preload; sprites via a custom protocol; re-enable `webSecurity` and `sandbox`. | M |
| 5.9 | ApiHawk hardening | Guard `ParseFrameCommands` with try/catch that clears the pending flag and returns an error; make `/flush` report timeout; cap read length; respond outside `_completionLock`. | M |

## Testing strategy
- Phases 0, 1, 2, 4: `Scripts/ci-local.ps1` green, plus the new tests named in each row.
- Once 4b.1 and 4b.3 exist: `ci-local.ps1 -Qa` runs the scenarios mapped to the change, and the PR links the run folder.
- Phase 3: before/after numbers from 3.1 in the PR description.
- Anything touching launch, encounter or resume: one manual run of a wild encounter on HGSS and one RS starter wave, noted in the PR.

## Rollout
All on feature branches off `main`, merged after the local script passes. Database migrations (3.2, 3.5) are additive; back up the Postgres volume before 3.5.

## Risks
| Risk | Mitigation |
|---|---|
| Deleting something that was still used by a config file or script | Phase 1 is one commit per item; grep the config folders before each. |
| Serialising `BotService` makes a slow launch block the dashboard state endpoint | Hold the lock only for the registry mutation around the launch if measured latency is a problem; keep the launch itself serialised (it already is for hunts). |
| Grouped query is slow on huge tables | Indexes from 3.2 first; continuous aggregate as the fallback, not the default. |
| `AdminCard.tsx` split changes behaviour | No logic edits in 5.2; typecheck and a manual click-through of each tab. |

## Open questions
Collected from above:
All answered on 2026-10-17 and recorded in [decisions](../decisions/global.md). None remain.
