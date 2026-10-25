# Cleanup and Hardening Plan

Status: Phases 0 to 5 implemented except the event half of 5.6 and 5.9 (2026-10-22, epics #30, #67 and #118; 5.3e, the HGSS state reader, 2026-10-24, epic #136); phase 7 (Wishmaker Jirachi hunt) implemented 2026-10-25 under epic #163 except the `rs-jirachi-shiny` QA; phases 4b and 6 open
Last updated: 2026-10-25
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
| 1.5 | Setup scripts out of `Startup` | `PokeApiParser` and `Scripts/Maps/*` stay (they set up metadata and map data on a new PC). Move the CLI branching into a `Tools/SetupCommands.cs` entry that runs before the host and without starting Discord or the bot service; make the `D:\\...` input paths arguments; collapse the duplicate `--parsepokemoveapi` flag; delete `MapHeaderProcessor`; keep `ExtractMovePermissionsFile` and call it as the map command's first step (it produces the MovePermissions input). | 1.1 | M | `dotnet run -- --parsepokeapi` works on a machine with only Mongo running; `Startup.MainAsync` is the host plus one `SetupCommands.TryRunAsync(args)` early exit. |
| 1.6 | Dead members | `IBotEvent.EventState` and every throwing override; `Bot.Reader`; `ChainedOrchestratorEvent.legLock`; `MultiShinyHunterEvent._locker`; `HgssPokemonParser.cache`; `EggHuntConfig` summary fields and `ShowSummaryAsync`; `BizHawkLauncher.ArrangeWindowsAsync` (if not already gone with 1.3); unused TS types in `types/api.ts`; the no-op "expose refresh" effect in `AdminCard.tsx`. | 1.2, 1.3 | S | Builds; tests pass. |
| 1.6b | Smaller finds from the sweep | `WeatherForecast.cs`, `ShinyBotHunter.http`; `Scripts/Maps/MapHeaderProcessor.cs`; uncalled methods (`RsDataReader.GetPartyCount`, `RomInfoService.FindByFlavor`, `LegalityService.LogVerdict`, `PathfindingService.FindPortalToMap`, `StarterHuntEvent.AdvanceIntoBattleAsync`, both `WriteToQueue`); unused fields (`BotService.bizHawkMode`, `BizHawkMemoryClient.localMemoryOnly`); nested `ShinyBotState`, `GraphData`, `MapValues`; `TogepiEgg.json` summary-touch keys; commented blocks listed in the review; `Microsoft.AspNetCore.OpenApi` and direct `Npgsql` package refs; add an explicit `Newtonsoft.Json` reference; remove the duplicate `SmogonDamageService.DisposeAsync` call; move `DiscordBotService` to namespace `ShinyBotHunter.Service`. | 1.1 | S | Builds; tests pass. |
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
Design: [Count queries spec](../specs/count-queries.md). Decided 2026-10-21: the query shape changes (indexes alone are not enough); no cache invalidation, since the cache is keyed by stored row values, not phase definitions.
Outcome: a cold dashboard load of a phase is a handful of round trips, each an index scan.

| # | Task | Details | Depends on | Size | Done when |
|---|---|---|---|---|---|
| 3.1 | Measure | Baseline is known: 1 to 2 million rows, 10 to 30 s cold load. Record the exact row count and the `EXPLAIN ANALYZE` of one `LongCount` before 3.2 so the index gain is visible. Target: cold phase load under 1 s. | — | S | Numbers in the PR. |
| 3.2 | Index | EF migration adding `(Game, Milestone, GameFileName, Timestamp DESC) INCLUDE (IsShiny, FormCode, Species)` on the hypertable. Ships alone. | 3.1 | S | `EXPLAIN` shows an index-only scan for the grouped query; re-measure. |
| 3.3 | One grouped load per game | Per the spec: `GetCountsAsync` (grouped counts), `GetPhaseProgressAsync` (since-last-shiny), the existing latest-20; a pure `rows -> GameEncounterCache` builder with unit tests; delete the per-leaf loaders and the static semaphore. | 3.2 | L | Cold load ≤ 4 round trips; numbers identical to the old code on seeded data; `PokemonPersistenceService` shrinks by ~300 lines. |
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
| 4.6 | `StarterHuntStateStore` round-trip | BSON round trip of `StarterHuntGameState` (high-bit PID, Guid Id, retired and created copies, document without `CompletedFinds`); the store is Mongo-backed, so a real load/save is a QA concern. | S |
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
| 4b.7 | Remaining initial scenarios | navigation, egg, double-find race, concurrent launch, cold-load timing. The dashboard driver (Playwright Electron) landed early under #107 with the Route 33 UI scenario. | 4b.3, 4b.4 | L |
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
| 5.3e | HGSS game-state reader | **Done 2026-10-24 (epic #136, see the implementation notes below).** One small class on the game profile that reads the verified chain in one frame (ApiHawk pointer-chain opcode) and returns where the game is: field free, field task, battle at menu, move list, message waiting, script playing (which), experience or move-learn prompt, battle over (outcome), evolution, other app. Replace `0x021E76D2`, anchor `+0x230EC`, `+0x692C`, `+0x6930` reads with it; bound every wait loop by "state unchanged for N seconds"; confirm each button press by the state moving. Needs 6.2's input-ready flag for menu presses, or confirm-and-retry until then. | 5.3, 6.2 | M |
| 5.4 | `BattleOrchestratorEvent` split | Extract move-learn/evolution flow and the Discord prompt into their own classes. | M |
| 5.5 | `StarterHuntEvent` split | Extract save-file management (`EnsureSaveExists`, `WipeSaveSlot`, trainer-ID scan) into `StarterSaveFiles`. | M |
| 5.6 | Narrow `IBot` | Events receive `ICommandClient + IMemoryClient + GameFlavor`; launch/dispose stay with `BotService`. Drop `IDataReader` from `IBot` and resolve it by flavor where needed. | M |
| 5.7 | Shared DTO shapes | Register `JsonStringEnumConverter` for controllers and SignalR; drop the int/string hedges in `types/api.ts`. Optionally generate `types/api.ts` from the OpenAPI document. | S/M |
| 5.8 | Dashboard security tidy-up | REST through the main process via preload; sprites via a custom protocol; re-enable `webSecurity` and `sandbox`. | M |
| 5.9 | ApiHawk hardening | Guard `ParseFrameCommands` with try/catch that clears the pending flag and returns an error; make `/flush` report timeout; cap read length; respond outside `_completionLock`. | M |

Rows 5.3 to 5.3d and the `IBot` change in 5.6 are designed in the [game profile spec](../specs/game-profile.md) (2026-10-21); its P1 to P7 are the task split and change no behaviour. Row 5.3e is the [HGSS state reader spec](../specs/hgss-state-reader.md), implemented on `feature/hgss-state-reader` under epic #136 (2026-10-23 to 24) with its QA gate run on every task; see the implementation notes below. RS stays a wrapper profile until row 6.1.

### Phase 7: Wishmaker Jirachi hunt (its own spec, after 5.3e)
A new self-managing special event for RS that hunts the Colosseum Bonus Disc Jirachi through Dolphin and BizHawk. Spiked 2026-10-24 to 25 ([research](../research/wishmaker-jirachi.md)); designed in the [Wishmaker Jirachi spec](../specs/wishmaker-jirachi.md) with decisions in [decisions/wishmaker-jirachi.md](../decisions/wishmaker-jirachi.md). The rows are the spec's suggested tasks.

| # | Task | Deliverable | Depends on | Size |
|---|---|---|---|---|
| 7.1 | Settings and Dolphin install (W1) | `DolphinBaseDirectory`, `GbaBiosPath`, `ColosseumBonusDiscPath`; `Dolphin/Config/*.ini`; `Scripts/deploy-dolphin.ps1` installing a bot-owned Dolphin user folder. | — | S |
| 7.2 | Gen 3 save sections (W2) | `Gen3SaveFile`: section walk, live block, first-block seed, play time, Pokédex flag, party count, save completion, party slot bytes; tests on the spike's saves. | — | M |
| 7.3 | RS `ContinueGame` (W3) | Deterministic RS continue macro; `Launch Bot + Continue` for RS. Already on `main` (b2d59d6); FR-8 done in 7.5. | — | S |
| 7.4 | Dolphin pieces (W4) | `DolphinLauncher`, `DolphinMemory`, `DolphinPad` (DSU server), `DolphinGbaWindow` (reset), `BonusDiscAddresses`; unit tests over fakes. | — | M |
| 7.5 | The event (W5) | `WishmakerJirachiEvent` and config, registry entry, `HasWishmakerJirachi`, `GameFilter` and the dashboard dropdown filter, Start validation, the cycle, prediction check, shiny path. | 7.1 to 7.4 | L |
| 7.6 | QA (W6) | `rs-jirachi-setup-refusals`, `rs-jirachi-soak` (one hour), `rs-jirachi-shiny` (crafted seed, Mongo, Discord, stop); `jirachi-seed.ts`; saves `rs.qa-jirachi`, `-full`, `-nodex`. Runs on the feature branch. Split into W6a and W6b; W6b held. | 7.5 | M |
| 7.7 | Close-out (W7) | Architecture section, spec status, implementation notes here. | 7.6 | S |

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

## Implementation notes, phases 0 to 2 (2026-10-21)
Done on `feature/cleanup-hardening` under epic #30, one task issue per row or row group (#31 to #47). Where the work diverged from the rows above:
- 1.5: `ExtractMovePermissionsFile` was kept, not deleted; it is the only producer of the MovePermissions files the map generator reads, so the map command now runs it first.
- 1.6: `IBotEvent.EventState` had overrides in five more files than the review table listed; all were removed.
- 1.6b: the file in the plural `Services` namespace was `DiscordBotService`, not `ShinyAlertService`.
- 2.2: readers of the bot registry stay lock-free on a `ConcurrentDictionary`; only writers take the `SemaphoreSlim`, because events call the readers from inside their own loops. Position allocation ignored registry keys until #72 (phases 3 to 5) made it skip keys as well as reported positions.
- 2.7: the token reached `ReadValidatedPartyAsync`, `FlushSave` and the battle-state read in phase 2; `GetBattleValue`, `GetBattleMenuState` and `ReadBytesViaPointerAsync` followed in #83 (phases 3 to 5).
- 4.8 was folded in; 1.6c was folded into 1.5.
- The manual checks named in the testing strategy (one HGSS wild encounter, one RS starter wave, the map command on a real extraction) were not run by the agents and are for the owner before the merge to `main`.

## Implementation notes, phases 3 to 5 (2026-10-21)
Done on `feature/cleanup-phases-3-5` under epic #67, task issues #68 to #85 plus #91. Rows 5.3 (and 5.3b to 5.3e), 5.6 and 5.9 were out of scope and are open. Where the work diverged from the rows and the spec:
- 3.1: measured on the local QA database with about 2,000,000 synthetic rows over 366 one-day chunks (the real numbers are the owner's, after the migration runs there). The grouped query took 850 ms before and after the index, because the planner keeps per-chunk seq scans at the synthetic data's 50% per-game selectivity; with `enable_seqscan = off` it ran as 366 index-only scans in 353 ms. The latest-20 query uses the existing `Timestamp` index. Planning across 366 chunks took 0.4 to 4 s, so the one-day chunk interval is worth revisiting on the real database. The index ships anyway; the cold-load win came from the query shape (3.3).
- 3.3: the cold load is 3 Postgres queries plus the caught list, down from about 130 round trips; on the QA database a cold phase load went from 18.6 s to 5.2 s and a warm load is about 40 ms. FR-3 (under 1 s) was not met locally; the next step is `EXPLAIN ANALYZE` on the two new queries against real data. `PokemonPersistenceService` shrank from 667 to 488 lines, not by about 300, because the builder lives in the same file.
- 3.5: no Mongo aggregation. Catches are counted from the caught list the load already fetches, which drops a round trip and a row type; and the spec's `"Pokemon.IsShiny": true` filter was not applied, because that property is a computed getter the documents may not store and today's loaders count every caught document (FR-2 wins). Caught documents written before `GameFileName` existed (May 2026) count at the game, phase and form levels but create no per-ROM-copy node.
- 4.1 was done in phase 2 (#40); 4.4 is inside 3.3; 4.8 was done in phase 2 (#46).
- 4.6: the store is Mongo-backed, not file-backed as the row said; the test is a BSON round trip of `StarterHuntGameState`.
- 4.7: the planner found a latent bug in `PathfindingService.AddEdge` (the capability check reduced to "needs nothing"); fixed with tests in #91. Runtime behaviour was unchanged because every caller passes an empty capability.
- 4.9: eslint's default TypeScript config needs `@eslint/js` and `typescript-eslint` beside `eslint`; with `vitest` those are the four dev packages added. `Scripts/ci-local.ps1` now runs 8 steps.
- 5.1: `BotService` ends at about 570 lines, not 500; the remaining bulk is event dispatch that only 5.3d removes.
- 5.4: one class, `LevelUpFlow`; the Discord prompt already lived in `DiscordBotService`.
- 5.5: the class is `StarterSaveFiles`, not `SaveFileManager`.
- 5.7: enums travel as names; integers still parse on input, which the QA harness relies on.
- 5.8: sprites come through a `sprite://` scheme served from the configured folder only; REST goes through one preload call handled in the main process with an origin check; `webSecurity` and `sandbox` are back on.
- 4b.7 (pulled forward, #107): a Playwright driver for the Electron dashboard under `ShinyBotHunter.Qa/dashboard/` with one scenario that hunts Route 33 through the real UI (subscribe in Dashboard Config, Launch Bot + Continue, Wild Encounter from the card, three counted encounters, the hotkey-armed shiny, Resume, two cancels) with video of the Admin window and the emulator. Its first run found a pre-existing bug: with no Discord token the backend never exited after a shiny alert, because the alert queue is drained only once Discord is ready (#108, fixed). The rerun on the merged tip passes end to end. A second scenario (#111) trains the lead one level through the UI; its first run found that the new-move prompt threw without a Discord login (#113, now declines the move) and that a crashed event kept its game state so the card stayed busy (#114, now cleared); it passes on the merged tip.
- Owner checks before the merge to `main`: run the migration against the real database and re-measure 3.1; one wild encounter with a level-up; one RS starter wave. The dashboard click-through with both security flags on is covered by the #107 scenario, and the owner reports the real caught data already carries `GameFileName`.

## Implementation notes, game profile (2026-10-22)
Done on `feature/game-profile` under epic #118, task issues #119 to #125 (the [spec](../specs/game-profile.md)'s rows P1 to P7, PRs #126 to #132), merged in the order P1, P5, P2, P3, P4, P7, P6. Rows 5.3, 5.3b, 5.3c, 5.3d and the `IBot` part of 5.6 are closed by it; 5.3e is the [HGSS state reader spec](../specs/hgss-state-reader.md), and the "events receive clients plus profile" part of 5.6 stays open. Where the work diverged from the rows and the spec:
- P1: `GameProfile` has get-only properties set by one constructor, not `init` members, because the spec's sketch could not run its constructor assertion over `init` values; the constructor also takes an unstored `revisions` list so the `CanReadBoxes` clause can check every supported revision. FR-3 is enforced in `BotFactory.CreateBot`, so Launch Bot now refuses RS revision 0 ROMs and any unflavored ROM whose header revision is not 0 (they fall back to the HGSS profile).
- P2: the spec's `FieldSystem`, `EnemySetupOffset`, `EnemyCountOffset`, `EnemyOffset` and `BattleMenuStateOffset` are the existing members `Anchor`, `EnemyAnchor`, `EnemyCount`, `Enemy` and `BattleMenuState`, so each address has one name and the FR-9 comment carries the spec's term. `IPointerService` changed only for `BoxLayout`; the flavor-only members live on the concrete tables and an event specific to one flavor casts `bot.Addresses` once. The RS decomp symbols come from the comments already in the code; the pointer research doc has only `gBattleTypeFlags` for RS.
- P3: `GetPositionAsync` returns both coordinates, so the Togepi egg pace loop reads two per poll where it read one. The spec's FR-5 acceptance ("no `NotSupportedException` in `Bot/` or `Games/`") conflicts with its FR-3; the revision throws in the two profiles stay and only the reader stubs are gone.
- P4: `HgssProfile` holds the data directory (`DataDirectory`) while the file names stayed in `MapService` and `GraphService`; the spec's "internal test constructor" is an internal `Create(metadata, maps, graph)` overload because `HgssProfile` is static. Map and graph data now load when `GameProfiles` is first resolved rather than on first navigation.
- P5: the per-bot start route already mapped `ArgumentException` to a 400; the top-level route got the same mapping in P6.
- P6: `EventRegistration.Create` is a delegate over internal `BotService` methods holding the former branch bodies verbatim, returning the dashboard state, and `Requires` carries the capability name for the refusal. The spec typed `Create` to return an `IBotEvent`, which `Launch Bot`, `Launch Bot + Continue` and `Resume` never build, so the registry names `BotService` against the spec's responsibility table (owner's decision, 2026-10-22). `GetTopLevelOptions` keeps today's three options; `HGSS Shiny Starter Hunt` stays reachable by name only. An unknown top-level action is now a 400, not a 500.
- P7: the legacy reader classifies indicator first, then battle-state 14, then menu 1. `LevelUpFlow` used to check the evolution byte before the indicator; whether the two bytes can disagree during an evolution is unverified until an evolution scenario exists. The battle-turn polls read up to three bytes per poll where they read one (the field waits still read one); the owner accepted this, and the chain reader in the follow-up returns them to one read.
- QA: `hgss-route33` and `hgss-route33-train` passed before and after every merge; the run folders are linked on each PR. No scenario covers evolution, the HGSS starter hunt, the Togepi egg or any RS flow, so those paths rest on the reviewers' value-by-value check of every moved read.

## Implementation notes, HGSS state reader (2026-10-24)
Done on `feature/hgss-state-reader` under epic #136 (the [spec](../specs/hgss-state-reader.md)'s rows S0 to S4), task issues #137 to #141, #143, #145, #149, #152, #154, #155, PRs #142 to #156. Every task that touched a read, a wait or a press carried a QA plan and ran its scenarios on the real emulator, one at a time, with the run folders linked on its PR. Where the work diverged from the spec as first written:
- S0 was added after S1's measurement: plain reads cost one emulator frame each, so a five-read snapshot was about 80 ms; ApiHawk gained opcode `0x05` (multi-block chain read, per-block status, retry count defaulting to 0) and one snapshot is one request of about 10 ms.
- S2 was split into S2a-0 (the foe reads on `app.setup`, `EnemyAnchor` and `BattleMenuState` off the shared interface), S2a (the swap and R1 to R5) and S2b (R6 to R8 and the timeouts). The reader's precedence inside `app.state == 9` became command 45, then the getter prompts, then command 22: the experience script 276 runs while the stat boxes and move-learn prompts wait for input.
- Presses are confirmed by the next snapshot: Run and Fight by the screen leaving the menu (the first tap often lands before input-ready, the second always landed), the move slot by leaving the move list, prompts by the prompt number changing; 1000 ms × 3 for battle prompts, 1500 ms × 6 for the evolution scene, whose "evolved into" message prints for about 5 s.
- The post-evolution move learn is not the battle getter: the battle heap is freed before the evolution scene runs. S2c read the evolution scene's own state (`[app.setup + 0x198] + 0x64`, undecompiled, mapped by a spike and observed live) and drives the decline from it; S2d adds the yes path (the summary app's forget-move list) and the free slot; S2e the in-battle yes and free-slot paths (overlay 8's forget screen is driven by touch and confirmed by the getter leaving 23). The in-battle message states advance on their own; the evolution scene's wait for A.
- S3's overworld-ready wait on `FieldFree` exposed that the blind heal macros left the nurse script running (the old coordinate heuristic walked through it), and S4's heal exposed a door re-entry: the Center exit lands one tile beyond the spawn node and the navigator walked back onto the door. S3b skips the spawn node after a portal crossing.
- S4's press loop observed `ScrCmd_WaitButton` (`0x02041075`) in the nurse's Pokérus explanation; it was decomp-only until then. The other two wait-button natives stay decomp-only.
- QA additions: `hgss-route33-evolve`, `-evolve-learn`, `-evolve-learn-replace`, `-evolve-learn-free`, `-train-replace`, `-train-free`, `hgss-navigate-heal-return`, `hgss-heal-variants` (three saves), `hgss-poison-walk`; saves `Pokemon HeartGold3`, `4`, `9`, `10` were made with PKHeX by subagents. The Discord move prompt is answered by the orchestrator in the desktop app with computer use; no production hook exists for it.

## Implementation notes, Wishmaker Jirachi hunt (2026-10-25)
Done on `feature/wishmaker-jirachi` under epic #163 (the [spec](../specs/wishmaker-jirachi.md)'s rows W1 to W7), task issues #164 to #170 plus #174, #178 and #183, PRs #171 to #184, merged in the order W2, W1, W1b, W4, W5c, W5a, W5d, W5b, W6a, W5e. The detail of every divergence is in the spec's [What diverged](../specs/wishmaker-jirachi.md#what-diverged); in short:
- 7.1: W1 plus W1b, which suppresses Dolphin's usage statistics dialog; the deploy script refuses while Dolphin runs.
- 7.2: the two spike saves are unit fixtures in the repo, recorded as a [decision](../decisions/wishmaker-jirachi.md#two-spike-saves-are-unit-test-fixtures-in-the-repo).
- 7.3: no change; the macro was already on `main` and FR-8 moved to 7.5.
- 7.4: the address table is `BonusDisc.cs` with the drive loop; `-C` works on 2609a, so the `Dolphin.ini` rewrite was not built; the Return that triggers the GBA reset gets no key-up; `DolphinLauncher` is not an `ILauncher`.
- 7.5: split into W5a (the event), W5b (registry, capability, `GameFilter`, Start), W5c (the dashboard filter), W5d (`Bot.DisposeAsync` disposes the launcher after a failed flush, for every event) and W5e (the save step confirms the start menu by a cursor probe, after the first soak run stopped in the second save). The backup is `.SaveRAM.wishmaker.bak`, and the Play press cap is 30, not 10.
- 7.6: split into W6a (refusals, soak, saves) and W6b (the shiny scenario and `jirachi-seed.ts`), which is held for the shiny route (spec's Open questions).
- Owner checks before the merge to `main`: choose the route for `rs-jirachi-shiny`, and confirm the soak numbers below.
