# Code Review, October 2026

Status: Draft
Last updated: 2026-10-17
Related: [System architecture](../architecture/system-overview.md), [Engineering principles](../engineering-principles.md), [Improvement plan](../plans/2026-10-cleanup-and-hardening.md)

A full read of the repo on branch `sprint1` at commit `123b4c4`, which became `main` and tag `v0.1.0`. Findings are grouped by the areas the owner asked about. Each has a file reference and a severity:

- **P1** causes wrong behaviour or data today, or blocks the owner's stated goals.
- **P2** real cost (slow, fragile, confusing) but works.
- **P3** cleanup.

The fixes are sequenced in the [improvement plan](../plans/2026-10-cleanup-and-hardening.md); this document only states what was found and why it matters.

## 1. Query performance: slow first load of counts

### What happens on a dashboard load

A phase card asks `GET /api/phaseinfo/{game}/{phase}/{gameFileName}` (`PhaseMetadataController.cs:58`). That calls `PokemonPersistenceService.DashboardLoadRequestAsync`, which fills a per-game in-memory cache on first touch (`PokemonPersistenceService.cs:189-340`). The cache is filled one leaf at a time, and every leaf is its own round trip:

| Cache level | Queries per leaf | Source |
|---|---|---|
| Game | 2 Postgres counts + 1 Mongo find + 1 latest-20 query | `LoadGameCache` :391 |
| Game + phase | 2 Postgres counts + 2 for current phase count + 1 Mongo count | `LoadPhaseCache` :358 |
| Game + form (per Pokemon in the phase) | 2 Postgres counts + 1 Mongo count | `LoadFormCache` :375 |
| Game + phase + form (per Pokemon) | 2 Postgres counts + 1 Mongo count | `LoadPhaseFormCache` :342 |
| Then the whole tree again keyed by `gameFileName` | same again | `EnsureLoadGameLevelCache` :268 |

For a phase listing 10 Pokemon that is on the order of **130 sequential database round trips** on first load, all serialised behind one static `SemaphoreSlim` (`PokemonPersistenceService.cs:19`). A second dashboard window opening a different phase waits on the same semaphore.

### Why each query is slow

- **P1. No index on `encounters` other than the primary key.** The only migration that creates structure is `20260511164802_InitialCreate.cs`; it creates the hypertable on `Timestamp` and nothing else. Every `LongCountAsync` with a `Game`/`Milestone`/`GameFileName`/`FormCode`/`Species` filter is a full scan of every chunk. Timescale's chunk exclusion does not help because none of these filters are on time.
- **P2. Counts are issued in pairs.** Every `GetAll*` method runs one count for all rows and a second for `IsShiny` rows (`PostgreSQLEncounterDao.cs:46-115`). One query with `COUNT(*) FILTER (WHERE "IsShiny")` returns both.
- **P2. `GetCurrentPhaseCount` is two queries** (latest shiny timestamp, then count after it) (`PostgreSQLEncounterDao.cs:116-141`). It can be one window query, and it is only needed per phase, not per form.
- **P2. Caught-shiny counts go to MongoDB separately** (`MongoCaughtPokemonDao.cs:72-143`), one `CountDocuments` per leaf, and `LoadGameLevelCache` fetches all caught shinies for the game then filters in memory (`PokemonPersistenceService.cs:420-421`, with a TODO admitting it).
- **P3. Encounter rate is raw SQL plus a second LINQ query** for min/max (`PostgreSQLEncounterDao.cs:143-215`), duplicated for the `gameFileName` variant. One `DATE_BIN` query can return the bins and the bounds.

### What would fix it

In order of payoff per effort (details in the plan):

1. Add indexes: `(Game, Milestone, Timestamp)`, `(GameFileName, Milestone, Timestamp)`, both with `INCLUDE (IsShiny, FormCode, Species)` or a partial index on `IsShiny`. This alone turns full scans into index range scans and is a single migration.
2. Replace the per-leaf loads with **one grouped query per game**: `SELECT "Milestone", "GameFileName", "FormCode", "Species", COUNT(*), COUNT(*) FILTER (WHERE "IsShiny") ... GROUP BY 1,2,3,4`. The whole cache tree for a game is built from that result set in memory. First load becomes two or three round trips instead of 130.
3. Move caught shinies into Postgres (one table, one foreign key to the encounter) so the Mongo round trips disappear and the grouped query can include them. This also removes a database from the stack.
4. If counts are still slow at the row volumes you reach (tens of millions), add a Timescale **continuous aggregate** by hour on the same grouping and sum it. Do not reach for this before 1 and 2 are measured.

**Measured today (owner, 2026-10-17):** roughly 1 to 2 million rows in `encounters`; a cold dashboard load takes 10 to 30 seconds. At that size an index range scan per group is milliseconds, so the plan's target is a cold phase load under one second and a warm load that is cache-only.

## 2. API concurrency: missing locks and race conditions

### Where shared state lives

| State | Type | Mutated from | Protected by |
|---|---|---|---|
| `BotService.botEvents` | `Dictionary<int, BotAndOrEvent>` (`BotService.cs:54`) | Every controller action on `BotController`, plus `ContinueWith` callbacks on bot-event tasks (`BotService.cs:736, 773`) | Nothing |
| `BotAndOrEvent` fields (`Event`, `EventTask`, `GameState`, `CancellationTokenSource`) | plain properties (`BotService.cs:663-681`) | Request threads and task continuations | Nothing |
| `AdminService.gamePhases` | `HashSet<GamePhase>` (`AdminService.cs:9`) | Subscribe/unsubscribe requests, read by `UpdateDashboardsAsync` | Nothing |
| `PhaseInfoService.phaseInfos`, `gamePhases` | `Dictionary` (`PhaseInfoService.cs:14-15`) | `UpdatePhaseInfoAsync` requests; read by every phase request | Nothing |
| `PokemonPersistenceService.notifies` | `List` (`PokemonPersistenceService.cs:20`) | Subscribe at startup only | Single-threaded by construction, fine |
| `PokemonPersistenceService.gameCache` | `ConcurrentDictionary` tree with `Interlocked` counters | Encounter channel consumer and dashboard reads | Adequate for increments; the double-checked loads are serialised by one static semaphore |
| `BizHawkReaderWriterLock` | two `SemaphoreSlim` (`BizHawkReaderWriterLock.cs`) | Launchers | Correct, but see below |

### Concrete races

- **P1. Two "Launch Bot" requests get the same position.** `ExecuteNewBotOrSpecialEvent` computes `newPos` from `botEvents` (`BotService.cs:57-74`), then `await bot.LaunchAsync()` for several seconds (`:81`), then `botEvents.Add(newPos, ...)` (`:82`). A second request arriving during the launch computes the same `newPos`; the second `Add` throws `ArgumentException` after its emulator has already been launched, leaking a BizHawk process on the same display slot and port.
- **P1. Enumeration during mutation.** `GetBotDashboardState` enumerates `botEvents` (`BotService.cs:525`) and is called on every SignalR refresh and after every action. `CancelBotOrEventAsync` removes (`:533`) and special-event continuations remove (`:785`) on thread-pool threads. A concurrent read throws `InvalidOperationException: Collection was modified`. The same applies to `GetAvailableActions` and `ExecuteEvent`.
- **P1. Cancel vs completion.** `BotAndOrEvent.CancelAsync` (`BotService.cs:687-711`) and the `ContinueWith` in `StartEvent` (`:736-762`) both null out `EventTask`, `Event`, `CancellationTokenSource` with no ordering. A cancel that lands while the continuation is running can dereference a null `CancellationTokenSource` (`:746`) or dispose a bot the continuation still reports on.
- **P1. Over-allocation past `maxEmulators`.** The count check at `:59` and the `Add` at `:82` are separated by the launch; N parallel requests can all pass the check.
- **P2. `AdminService` subscribe/unsubscribe** can corrupt the `HashSet` under concurrent dashboard windows; `UpdateDashboardsAsync` copies it with `ToArray` while another request adds.
- **P2. `PhaseInfoService.AddOrUpdatePhaseInfoAsync`** mutates two dictionaries while any phase request reads them.
- **P2. `BizHawkReaderWriterLock.GetWriterLockAsync` can deadlock itself** if a reader holder awaits a writer (a launch inside a launch); nothing does today, but there is no comment saying so. Also the "writer" acquires every reader slot one at a time, so a writer waiting for the last slot blocks new readers forever if a reader leaks (no timeout, no cancellation token).
- **P3. Fire-and-forget tasks with no supervision**: `Init()` on `PostgreSQLEncounterDao`, `MongoCaughtPokemonDao` and `PokemonPersistenceService` start `Task.Run` loops whose faults go nowhere except `Console.WriteLine`. If `ProcessAsync` dies, encounters silently stop being written while the cache keeps counting.

### What would fix it

The cleanest shape for this project (per principle 2.9: single-writer over fine-grained locks):

1. **One `SemaphoreSlim(1,1)` in `BotService`** held across the whole of each public method that reads or mutates `botEvents` (position allocation **and** the launch **and** the `Add`), and also acquired by the continuations before they remove. Launches become serialised, which the Aug 28 and Aug 29 commits (`Boot emulators one at a time`) already chose for the starter hunt, so this matches current intent.
2. Make `botEvents` a `ConcurrentDictionary` anyway, so a missed lock degrades to a stale read rather than an exception.
3. Have `CancelAsync` and the completion continuation go through one method that owns the teardown order, under the same semaphore.
4. `AdminService` and `PhaseInfoService`: a `SemaphoreSlim` each, or `ConcurrentDictionary` + `ImmutableHashSet` swap. They are small.
5. Give the three background loops a supervising task that logs and restarts, or convert them to `BackgroundService` so the host owns their lifetime and surfaces faults.
6. A test: start two launches concurrently against a fake launcher and assert distinct positions and no exception. `EmulatorClientLifetimeTests` is the only lifetime test today and it is sequential.

**Answered (owner):** the real model is one operator clicking one thing, but nothing stops a second click or a second window from landing mid-launch and corrupting state. So the lock is a correctness guard, and the dashboard should also disable action buttons while a request is in flight (a small follow-up in the plan).

### More races in the bot engine

Found by the engine review and verified:

- **P1. `EventTask` is the wrong task.** `StartEvent` and `StartSpecialEvent` use `ContinueWith(async t => ...)` (`BotService.cs:736, 773`), which yields a `Task<Task>`. `EventTask` completes at the continuation's first `await`, so `CancelAsync`'s `await EventTask` returns before cleanup and the dashboard refresh have run, and any exception in the continuation is unobserved. Fix: `.Unwrap()` or no async lambda.
- **P1. Two event loops on one emulator.** `GameState.SwapState` is `Pop` then `Push` (`GameState.cs:67-71`); between them `Count == 0`, which `GetAvailableActions` reads as "idle" (`BotService.cs:473`) and offers Wild Encounter. Starting it replaces `Event`/`GameState`/CTS while the old task still drives the bot.
- **P1. Double Resume on the RS hunt.** `BotService.cs:301` fires `ExecuteWithResultAsync` without awaiting; two Resume clicks both take the oldest parked shiny and run `FinishShinyAsync` twice on one bot (double save, double catch record, dispose of a disposed bot).
- **P2. Wrong key removed on special-event crash.** `StartSpecialEvent`'s continuation removes `GetBotPositions()[0]` (`BotService.cs:785`); for a multi-emulator hunt that is an arbitrary position, not the registry key, so a stale entry blocks further launches.
- **P2. Dispose under in-flight reads.** `BattleOrchestratorEvent.WaitForMoveEnd` cancels losing poll tasks but never awaits them, and `ReadValidatedPartyAsync` takes no token; a cancel then disposes the `HttpClient` under an orphaned read.
- **P2. Un-cancellable retry.** Memory reads call `ExecuteWithRetryAsync` with a default token; on a dead emulator each read spins ~6.5 s through 10 retries and cancel blocks on it.
- **P2. Trainer-ID check is not atomic.** `StarterHuntEvent.EnsureSaveExistsAsync` releases `launchGate` before the save is flushed, so two wave members can both pass the duplicate-ID scan.
- **P3. `MultiShinyHunterEvent._locker` is declared and never used** while its shared placements are mutated from the channel loop and from `Task.Run` workers.

## 3. Tests and CI

### What exists
About 109 xUnit tests in 17 classes, all hermetic: no ROMs, no BizHawk, no database, no network. They cover the macro DSL, the Gen 3 parser, ROM identification, save-file selection, config schemas, grid arithmetic, DI registration validity, and the HGSS memory map via `FakeMemoryClient`. Three classes share an xUnit collection because they change the process working directory and lean on the `MacroRegistry` singleton.

### Gaps that matter
| Area | Lines | Tests | Why it matters |
|---|---|---|---|
| `BotEvents/` | ~5.8k | config validation only | The hunt logic itself: state machines, shiny decisions, park/resume. Testable with `FakeMemoryClient` plus a fake command client. |
| `Pokemon/HgssPokemonParser.cs`, `Service/BattlePokemonParser/` | ~300 | none | Gen 4 decryption has no round-trip test while Gen 3 has 22. |
| `Service/Pointers/RsPointerService.cs` | small | none | A wrong offset fails silently. HGSS has the equivalent test. |
| `Persistence/` | ~1.1k | none | Query shape regressions (section 1) would not be caught. |
| `Pathfinding/` | ~1.4k | none | Pure graph code, ideal for table-driven tests. |
| `BizHawkReaderWriterLock`, `BotService` | | none | No concurrency test anywhere. |
| Dashboard | ~5k TS | none, no lint, no typecheck script | `electron-vite build` strips types without checking them. |

### Beyond unit tests
Nothing exercises the backend-to-emulator loop automatically. The owner's direction is an agent-driven QA layer with an in-emulator shiny injection, selected by changed area, with screenshots as evidence; see the [QA spec](../specs/qa-automation.md). The earlier `ForceShinyPercent` hook was the right instinct in the wrong place and was removed in August 2026 for that reason.

### CI
The workflow runs on every push to every branch and again on every PR, on `windows-latest`, with no cache and no cancel-in-progress. Decision recorded in [the CI decision](../decisions/global.md#ci-runs-locally-before-merge-not-on-github-actions): switch to manual trigger, add a local script that is the definition of CI.

## 4. Bugs found while reading (not concurrency)

- **P1. `BattleOrchestratorEvent.WillEvolve` missing braces** (`:485-490`): the beauty condition guards only the `MinLevel is null` check, so the method returns `true` for any evolution with a beauty requirement that is not met. A TODO above it suggests this was meant as a temporary block.
- **P2. `BattleOrchestratorEvent.GetLevelUp`** (`:548-551`) indexes `newState.PartyPokemon[i]` by the old party count; throws if the party shrank.
- **P2. ApiHawk `ParseFrameCommands`** uses `short.Parse`/`uint.Parse`/`ushort.Parse` with no guard (`CommandServer.cs:293-303`) on the frame thread. A malformed `M.` op throws inside EmuHawk's tool update; the pending flag is never cleared, so `/wait-macro` reports pending forever.
- **P2. ApiHawk `/flush` always answers "flushed"** (`CommandServer.cs:655-667`) even after its 10 s wait times out, so the backend can treat an unsaved SRAM as saved.
- **P2. ApiHawk memory read length is unbounded** for opcodes `0x01`/`0x03` (`MemoryServer.cs:231`); only the party read has a `MaxSlotSize`.
- **P2. ApiHawk responds to the HTTP client while holding `_completionLock`** (`CommandServer.cs:641-650`), the same lock the frame thread takes to signal macro completion; a stalled socket stalls emulation.
- **P2. `ElectronHub.SendPokemonEncounterMessageAsync`** is a client-invokable hub method with no server-side caller (`ElectronHub.cs:23`); any process on the LAN can inject encounter counts into a group.
- **P3. Dashboard group inference for encounter pushes** matches on phase name first (`signalRClient.ts:187-201`) because the payload lacks game and phase; breaks when two games share a phase name.
- **P3. The hub method name `RecieveEncounters`** is misspelled on both sides of the contract.

## 5. Dead code

A full grep sweep of the backend, ApiHawk, tests and dashboard. "Safe" means nothing references it outside its own file or outside other dead code. "Decide" means it is unreachable in practice but deleting it is a product choice.

### Safe to delete (P3, net about 3,000 lines)
| What | Where | Note |
|---|---|---|
| ASP.NET template leftovers | `Controllers/WeatherForecastController.cs`, `WeatherForecast.cs`, `ShinyBotHunter.http` | The `.http` file hits `/weatherforecast` on a stale port. |
| Experiment harness | `Test.cs` (235 lines), `Bot/EmptyTestLauncher.cs`, `Startup.TestAsync`/`Test2Async`/`to_s16`, commented blocks at `Startup.cs:85-87, 95-97, 113-119, 188-194, 299-347`, the unused `new MapHeaderProcessor()` at `:84` | Only callers are commented out. |
| Old combined TCP client | `Emulators/BizHawkClient.cs` | Zero references. |
| Dead event chain | `BotEvents/WildPokemonEncounterEvent.cs` (293 lines) → `Factories/EncounterEventTypeFactory.cs` (+ registration `Startup.cs:421`) → `RunEncounterEventDeprecated.cs` | Move `EncounterType` and `EncounterParams` out of the factory file first; they are live. Drop the two `using static WildPokemonEncounterEvent` lines in `OrchestratorEvent` and `BattleOrchestratorEvent`. |
| `IBotEvent.EventState` and every override | `IBotEvent.cs:39`, `ChainedOrchestratorEvent.cs:33`, `NavigatorEvent.cs:15`, `OrchestratorEvent.cs:26`, `BattleOrchestratorEvent.cs:23`, `MultiStarterHuntEvent.cs:50`, `StarterHuntEvent.cs:54`, `HgssTogepiEggEvent.cs:110`, `CreateSaveEvent.cs:41` | Nothing reads it; the dashboard reads `GameState.State`. |
| Unused members | `Bot.Reader` (`Bot.cs:66`), `HgssPokemonParser.cache` (`:14`), `MultiShinyHunterEvent._locker` (`:55`) and enum `ShinyBotState` (`:33`), `BotService.bizHawkMode` (`:23`), `BizHawkMemoryClient.localMemoryOnly` (`:18`), `GraphProcessor.GraphData` (`:327`), `MapProcessor.MapValues` (`:453`) | |
| Uncalled methods | `RsDataReader.GetPartyCount` (`:42`), `RomInfoService.FindByFlavor` (`:155`), `LegalityService.LogVerdict` (`:78`), `PathfindingService.FindPortalToMap` (`:519`), `BizHawkLauncher.ArrangeWindowsAsync` (`:187`), `StarterHuntEvent.AdvanceIntoBattleAsync` (`:419`), `PostgreSQLEncounterDao.WriteToQueue` (`:292`), `InfluxDbEncounterDao.WriteToQueue` | |
| Togepi summary flow | `HgssTogepiEggEvent.ShowSummaryAsync` (`:420`), `EggHuntConfig.ValidateTouchCoordinates`/`SlotTouch` and the `*Touch`/`OpenPartyMenu*`/`Summary*` properties (`:94-146`), `Configs/Events/TogepiEgg.json:24-35` | The file's own comments call these unused. |
| Commented-out blocks over 5 lines | `Bot.cs:190-206`, `MultiShinyHunterEvent.cs:193-200`, `PathfindingService.cs:560-566`, `MapProcessor.cs:234-245`, `MigrateInfluxSchemav1Tov2.cs:88-94` | No `#if false` regions exist. |
| Unreachable map script | `Scripts/Maps/MapHeaderProcessor.cs`, `MapProcessor.ExtractMovePermissionsFile` (`:51`) | Every call is commented out. |
| InfluxDB | `Persistence/InfluxDb/InfluxDbEncounterDao.cs` (unregistered), `Scripts/MigrateInfluxToPostgres.cs`, `Scripts/MigrateInfluxSchemav1Tov2.cs`, the two `--migrateinflux*` flags, `InfluxDB3.Client` package, `InfluxToken`/`InfluxDbUrl`/`InfluxDbDatabase` settings | Assumes the Postgres migration has run (see open question). Also removes two `Startup.cs` usings that only resolve through that package. |
| Unused packages and usings | `Microsoft.AspNetCore.OpenApi` (no `AddOpenApi`/`MapOpenApi`), `Npgsql` direct reference (brought in by the EF provider); `Startup.cs` usings for Protobuf, gRPC balancer, Mongo core configuration, Influx, YamlDotNet core, `JSType` | Keep `SharpCompress`: it pins a vulnerable transitive of the Mongo driver. |
| Stale dashboard bits | unused TS types `RecentEncounterEntry`, `NewEventArguments`, `ConditionType` in `types/api.ts`; the no-op "expose refresh" effect in `AdminCard.tsx:834-836`; `electron.vite.config.1776907978089.mjs`; README grid section | |

Every one of the 39 macro YAML files is referenced; there are no dead macros. ApiHawk has nothing dead except `/ping`, which is worth keeping for diagnostics.

### Needs a decision (P3)
- **Lua path: decided, delete.** `Lua Scripts/`, `Emulators/BizHawkLuaLoader.cs` (its `InitAsync` runs on every start and writes `Loader.lua` even in ApiHawk mode), `Emulators/BizHawkLauncher.cs`, the `else` branch in `BotFactory`, and the `BizHawkMode`/`LuaBaseDirectory` settings. The path is already broken: it pairs the Lua launcher with the HTTP clients, and the code default for `BizHawkMode` is still `"LuaNet"`, so a missing config key would silently select it.
- **Setup scripts: decided, keep.** `PokeApiParser.cs` (`--parsepokeapi` and `--parsepokemoveapi` both call the same method) and `Scripts/Maps/*` with `CustomLoadZoneMappings` (`--generatemapdatahgss`) are how a new PC gets its metadata and map data, so they stay. They should stop booting Discord and the bot service first, and the hard-coded `D:\Temp`, `D:\ExtractedROMs` and `D:\matrixMaps` inputs should become arguments or settings. `MapHeaderProcessor` is still dead within them.
- **`POST /api/refreshGames`**: the dashboard only mentions it in a comment. Keep if called by hand.
- **`ElectronHub.SendPokemonEncounterMessageAsync`**: unused and client-invokable (also listed in section 4).
- **MongoDB: decided, keep.** It holds the schema-fluid data (caught shinies, metadata, phases, hunt state) and the owner wants it for exactly that. No consolidation; the fix for its part of the slow load is one aggregation per game instead of one count per leaf.

### Suspicious, not dead (P2/P3)
- `SmogonDamageService.DisposeAsync()` is called twice at shutdown (`Startup.cs:100, 106`).
- `Scripts/PokeApiParser.cs:41-98` declares its own `PokemonMetadata`, `EvolutionMetadata`, `PokemonFormMetadata` that shadow the runtime classes in `Persistence/MongoDb/` but write to the same collections: schema drift waiting to happen. P2.
- `Newtonsoft.Json` is used directly in five files but only arrives as a transitive dependency; it needs an explicit reference.
- `Service/ShinyAlertService.cs` is the only file in namespace `ShinyBotHunter.Services` (plural).

## 6. Architecture and patterns

### What is good and should be kept
Listed in the [architecture doc](../architecture/system-overview.md): keyed DI by flavor, the channel-based write path, frame-coherent memory reads, the data-driven action schema the dashboard renders forms from, up-front leg and config validation, the ApiHawk frame-thread queue design, and the dashboard's main-process SignalR with IPC fan-out.

### God classes (P2)
| File | Lines | Mixes |
|---|---|---|
| `AdminCard.tsx` | 1802 | schema helpers, action form, dashboard config tab, bot control tab (20+ `useState`s), phase editor, shell |
| `BattleOrchestratorEvent.cs` | 907 | battle state machine, move-learn and evolution flows, Discord prompts, raw HGSS memory reads with inline addresses (`:806-843`) |
| `BotService.cs` | 790 | argument parsing and validation, slot allocation, DTOs, `BotAndOrEvent` task supervision |
| `StarterHuntEvent.cs` | 734 | hunting, plus save-file management (`:557-721`) |
| `CommandServer.cs` (ApiHawk) | 816 | HTTP, macro parser, input state machine, recording |

Suggested seams: `BotService` → `EmulatorSlotRegistry` (positions, lock) + `BotEventRunner` (task supervision) + the DTOs in their own file. `AdminCard.tsx` → `admin/actionSchema.ts`, `ActionParamFields.tsx`, `DashboardConfigTab.tsx`, `BotControlTab.tsx` (further into `EmulatorCard` + `TopLevelActionForm` + one reducer for the seven parallel per-position `Record` states), `PhaseEditorTab.tsx`, and a single `apiClient.ts` (the metadata endpoint is fetched independently in four places).

### Duplication (P2)
- `WildPokemonEncounterEvent` (293 lines) is an older copy of Orchestrator + Battle; its only reference is commented out. `RunEncounterEvent` and `RunEncounterEventDeprecated` are identical, and `EncounterEventTypeFactory` returns the deprecated one while `OrchestratorEvent` bypasses the factory.
- `CheckForCatchAsync`/`UpdateMilestoneState` exist in `OrchestratorEvent`, `WildPokemonEncounterEvent`, and again as `PhaseTarget.IsTargetAsync`.
- `BizHawkLauncher` (Lua) and `BizHawkApiHawkLauncher` share about 80% including Win32 window handling and dispose; the Lua one keeps its own grid formula.
- HGSS memory addresses live in five places: `HgssDataReader`, `HgssPointerService`, `PathfindingService:256-261`, `RunEncounterEvent:370`, `HgssTogepiEggEvent:53`, plus inline in `BattleOrchestratorEvent`. The battle-detect bytes `0x41/0x97/0xC0` appear in five files.
- `types/api.ts` hand-mirrors every C# DTO, and `GAME_FLAVOR_NAMES` must be kept in sync with the C# enum by hand. Enums arrive as ints because neither the controllers nor SignalR register `JsonStringEnumConverter`.

### Abstractions adding no value (P3)
- `IBotEvent.EventState`: `OrchestratorEvent`, `NavigatorEvent` and `ChainedOrchestratorEvent` throw `NotImplementedException`; `BattleOrchestratorEvent` returns a constant; nothing reads it (the dashboard reads `GameState.State`).
- `IDataReader` on `IBot` is HGSS-shaped; `RsDataReader` throws `NotSupported` for six of seven members and `Bot.Reader` throws `NotImplementedException`.
- `IBot` exposes launch, dispose and macro recording to every event; events only need command + memory.
- `GameState` holds a back-reference to `BotService` (`GameState.cs:109`, TODO at `BotService.cs:729`).
- `HgssShinyStarterEventFactory` wraps one `new`. `EncounterEventTypeFactory` is registered and only used by dead code. `ChainedOrchestratorEvent.legLock` guards a list that is never mutated after construction.
- `IpcSignalRService` in the dashboard is an observer class with handler arrays and a no-op subscribe API kept for compatibility with a removed service; a hook over `window.api` would do.
- `BizHawkMode` setting: only `ApiHawk` works. The `LuaNet` branch pairs the Lua launcher with the HTTP clients, which cannot talk to the Lua TCP servers.
- Three singletons bypass DI: `MapService.Instance`, `GraphService.Instance`, `MacroRegistry.Instance` (loads from the working directory at static init, which is why three test classes must share a collection).

### Inconsistent async (P2)
- `BizHawkCommandClient.SendCommand` is synchronous `HttpClient.Send` with `Thread.Sleep` retry, called from async loops.
- Dozens of `WaitForMacroAsync()` calls drop the cancellation token; `Bot.FlushSave` drops it too.
- Fixed sleeps: 5 s after launch, 4 s before "continue", 10 s in `CreateSaveEvent`.
- `catch { }` swallows the whole hunt in `HgssShinyStarterEvent.cs:494`, and `Bot.DisposeAsync` swallows dispose errors.

### Dashboard specifics
- `webSecurity: false` and `sandbox: false` on every window (`windowManager.ts:135-136`) to load `file://` sprites and to allow cross-origin `fetch` to port 5000. Route REST through the main process (one preload `fetch`) and serve sprites via a custom protocol, then re-enable both.
- Every window, including each phase window, fetches phase info for **all** subscriptions on every config change (`AppContext.tsx:151-174`), and the subscription effect runs fetches inside a `setState` updater.
- Four independent polling loops (encounter rate, macro status, location, admin dashboard state) on top of the SignalR push.
- `uncaughtException` in main shows a dialog and quits every window (`main/index.ts:103-106`).
- `config.json` has two URLs for the same backend (`backendUrl`, `apiUrl`); `README.md:109-129` describes a grid layout that no longer exists.

## 7. Summary of severities

| Severity | Count | Where |
|---|---|---|
| P1 | 10 | No index on `encounters` (section 1); position double-allocation, dictionary enumeration during mutation, cancel-vs-completion, over-allocation, `Task<Task>` event task, empty-stack idle check, double Resume (section 2); `WillEvolve` braces (section 4) |
| P2 | ~30 | Paired count queries and per-leaf cache loads; unsynchronised admin and phase services; reader/writer lock edge cases; unobserved background loops; stale key on hunt crash; dispose under in-flight reads; ApiHawk parse, flush, read-length and lock-while-responding; client-invokable hub method; five god classes; duplicated events and addresses; sync HTTP inside async; dropped cancellation tokens; schema-drifting duplicate metadata classes |
| P3 | many | Dead code (section 5), single-implementation abstractions, dashboard tidy-ups |

What to do first is in the [improvement plan](../plans/2026-10-cleanup-and-hardening.md): local CI, then the dead-code pass, then the `BotService` lock and the `EventTask` fix together, then indexes and the grouped count query.

## Changelog
- 2026-10-17: first full draft from the review session.
