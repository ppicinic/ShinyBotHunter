# Spec: dashboard appearance and recent encounters per PC

Status: Implemented on feature/appearance-per-pc (epic #478), 2026-11-18. Not yet merged to main. Read "As built" first.
Last updated: 2026-11-18
Owner: Phil
Related: [Livelier dashboard](dashboard-alive.md) (FR-12, FR-13, FR-19: the themes and where they are saved today), [Cheaper dashboard animations](dashboard-animation-cost.md) (the animation levels), [the dashboard guide page](../guide/03-dashboard.md), [storage decision](../decisions/global.md)

## Summary

Two bots on two PCs can point at the same MongoDB. Today the dashboard's appearance (themes, day and night times, animation levels) is part of one shared MongoDB document, so the PCs cannot have different settings and each one's save wipes the other's. This spec moves `appearance`, and only `appearance`, out of MongoDB into a JSON file on the bot's own PC. Subscriptions and the recent-encounter count stay in MongoDB, shared, as they are.

A second change for the same case: the Recent Encounters list starts empty when the bot starts, where today it starts with the latest 20 rows of the shared `encounters` table, which may be the other PC's.

## Why

Owner, 2026-11-18: "i want the config to be on the bot side but preferably not on the mongodb database", then "only appearance per-PC". The case is the prod PC, which is old hardware and wants the animations low or off, next to the dev PC, which does not.

How it behaves today (read from the code, 2026-11-18):

- `MongoDashboardConfigDao` saves the whole `DashboardConfig` (subscriptions, recent count, `appearance`) as one document, `_id` `dashboard`, in the `DashboardConfig` collection. Nothing in the key names the PC.
- `AdminService.InitAsync` loads it once at start. The bot never reads it again.
- Every change saves the bot's whole in-memory config over the document.

Recent encounters (owner, 2026-11-18: "from cache they should be unique per PC, but on first load its gonna grab from other games"):

- On a game's first use `PokemonPersistenceService.LoadGameCacheAsync` runs `GetLatestEncounters`: the latest 20 `encounters` rows for that game by time, whichever PC wrote them. A row has the ROM copy's file name and nothing that names the PC.
- After that the bot adds only the encounters it records itself. So after a restart a PC shows the other PC's latest until its own hunts push them out.

So with bots A and B on one database: a theme set on A shows on A only; B picks it up at its next restart; and any save from B in between writes B's stale `appearance` back, undoing A's.

## Goals

- G1: each PC keeps its own themes, times and animation levels, whatever database its bot points at.
- G2: the bot still owns the setting and pushes it to its dashboards, as today. The dashboard does not change.
- G3: `appearance` is no longer in MongoDB.
- G4: the Recent Encounters list shows only what this PC's bot has met since it started.

## Non-goals

- Subscriptions and the recent-encounter count. They stay one shared MongoDB document, loaded at start and saved whole. Two bots still overwrite each other's subscriptions there; that is today's behaviour and is not fixed here (Open questions, 2).
- Live sync of anything between bots.
- Any change to the dashboard, the SignalR push, the `admin/appearance` endpoint or what the Appearance section shows.
- A setting for where the file goes.

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | `appearance` is saved to and loaded from one JSON file on the bot's PC: `%LOCALAPPDATA%\ShinyBotHunter\appearance.json`. The folder is created on the first save. The file holds exactly what `appearance` holds today. | Must | Unit test: round trip through the file's text, including a per-window override and the animation levels. |
| FR-2 | A change to `appearance` writes the file and nothing else. A change to subscriptions or the recent count writes the MongoDB document and nothing else. | Must | Unit test with the two saves as seams: `SetAppearanceAsync` calls only the file save; subscribe, unsubscribe and the recent count call only the MongoDB save. |
| FR-3 | The MongoDB document no longer has an `appearance` element. A document saved by an older version, which has one, still loads. | Must | Unit test: a document with `appearance` loads its subscriptions and count; a saved document has no `appearance`. |
| FR-4 | No file at start: `appearance` is the defaults (Midnight, 07:00 and 19:00, every animation level high), as a new install is today. | Must | Unit test. |
| FR-5 | A file that cannot be read as `appearance` (hand-edited, cut short): a warning is logged with the path and the error, the defaults are used, and the bot starts. The file is left as it is until the next appearance change overwrites it. | Must | Unit test. |
| FR-6 | The fallbacks of [dashboard-alive](dashboard-alive.md) FR-19 (unknown theme id, malformed time) apply to the file as they did to the document. | Must | The existing `AdminService` tests pass with the file as the source. |
| FR-7 | The pushed `DashboardConfig` is unchanged: subscriptions, recent count and `appearance` in one object. | Must | The dashboard builds and its tests pass untouched; QA `dashboard-alive` and `dashboard-themes`. |
| FR-8 | The first start after the update, with no file and an `appearance` in the MongoDB document: that `appearance` is used and written to the file. From then on the document's copy is ignored, and it disappears at the next save of the shared document. | Must | Unit test: no file plus a document with `appearance` gives that `appearance` and one file save; with a file, the document's copy is not read. Kept by the owner, 2026-11-18. |
| FR-9 | A game's recent-encounters list is empty when its cache is loaded and fills only from encounters this bot records. The counts, the phase progress and the caught list still load from the databases as today. | Must | Unit test: a built cache has no latest encounters; after one recorded encounter it has that one. The existing count tests pass. |

## Design

`AdminService` already takes its storage as functions (`load`, `save`), so the change sits behind them.

- **`AdminService`** takes two pairs where it takes one: the shared load and save (MongoDB: subscriptions and count) and the appearance load and save (the file). `InitAsync` loads both. `SetAppearanceAsync` calls the appearance save; the other three mutators call the shared save. Both saves keep going through the existing `saveGate`, after the push, so a failed save still leaves every dashboard showing the state in memory. `Snapshot()` and the push are unchanged.
- **`MongoDashboardConfigDao`** saves and loads subscriptions and the count only. For FR-8 its load also returns the old document's `appearance` when there is one.
- **The file** is read and written by a small class in `Persistence/`, with `System.Text.Json` and the web naming the endpoint already uses, so the file reads like the POST body. The whole file is written on each change. `ThemeChoice.Night` keeps its "omit when null".
- **Path.** `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`, then `ShinyBotHunter\appearance.json`. It is outside the install folder, so replacing the bot's files on an update keeps it. It is fixed, not a setting.

Concurrency: no new shared field. `appearance` stays under `gamePhasesLock`; the file is touched only inside `saveGate` and once at start before the host runs.

**Recent encounters (FR-9).** `LoadGameCacheAsync` stops calling `GetLatestEncounters`, and `BuildGameCache` loses its `latest` parameter; the queue starts empty and `Increment` fills it as today. The load goes from three Postgres queries to two, and its log line says so. Owner, 2026-11-18: "Yes, start empty", over saving the list per PC or adding a PC column to `encounters`.

### New things

- One class, the appearance file (name at the planner's choice; a plain name, not `Provider` or `Manager`).
- One folder on disk, `%LOCALAPPDATA%\ShinyBotHunter\`, created by the bot.
- No package, interface, project or config key.

### The two-stores rule

[Engineering principles](../engineering-principles.md) 2.4 says two stores and no third. This file is one PC's settings, a few hundred bytes, read once and written on a click: configuration, not a store. The PR adds a dated section to [decisions/global.md](../decisions/global.md) under the storage decision saying so, and that the rule stands for data.

### Deleted

- `appearance` from the MongoDB document's shape, and the three DAO tests that round-trip it through BSON (replaced by the file's tests).
- After FR-8's copy, nothing reads the document's `appearance`.
- `GetLatestEncounters` from `IEncounterDao` and `PostgreSQLEncounterDao`, and the `latest` argument at `BuildGameCache`'s call sites in `GameEncounterCacheBuilderTests`.

## Docs to update in the PR

- [dashboard-alive](dashboard-alive.md): a changelog line that FR-13's "whole config as one document" is superseded for `appearance` by this spec. Its history is not edited.
- [system-overview](../architecture/system-overview.md): the SignalR paragraph and the MongoDB row (`DashboardConfig` holds subscriptions and count; `appearance` is a file on the bot's PC).
- [The dashboard guide](../guide/03-dashboard.md), "Choose a theme": themes and animation levels are per PC; where the file is; deleting it resets to the defaults.
- [The dashboard guide](../guide/03-dashboard.md), "See the latest encounters": the list is this PC's and is empty after the bot restarts until the next encounter. [system-overview](../architecture/system-overview.md): the counts cache loads with two Postgres queries, not three.
- [Updating](../guide/08-updating.md) and [Versions](../guide/09-versions.md), if the version that carries this needs a line: on the first start after the update each PC takes the saved themes once (FR-8), then keeps its own.
- [decisions/global.md](../decisions/global.md): the dated section above.

## QA

| Scenario | What it shows |
|---|---|
| `dashboard-alive`, `dashboard-themes` (existing) | Themes and animation levels still apply, survive a backend restart, and reset through `admin/appearance`. |
| By hand, once, on the two PCs | Set different themes and animation levels on each; restart both bots; each PC still has its own. |
| By hand, once | Restart the bot with a dashboard open: Recent Encounters is empty; start a hunt: it fills from that hunt only. The counts windows still show the totals. |

QA runs on the dev PC with the dev settings, so it reads and writes the dev PC's `appearance.json`, as it reads and writes the dev database's document today. No change in isolation.

## Suggested task split

Two tasks with disjoint files, both size S:

| # | Task | Files |
|---|---|---|
| P1 | Appearance in a file (FR-1 to FR-8) | `AdminService.cs`, `MongoDashboardConfigDao.cs`, the new file class, `Startup.cs`, `AdminServiceTests.cs`, `dashboard-alive.md`, `decisions/global.md`, the guide's "Choose a theme" |
| P2 | Recent encounters start empty (FR-9) | `PokemonPersistenceService.cs`, `IEncounterDao.cs`, `PostgreSQLEncounterDao.cs`, `GameEncounterCacheBuilderTests.cs`, the guide's "See the latest encounters" |

`system-overview.md` is touched by both; P2 goes second and takes it. No dashboard file in either.

## As built, 2026-11-18

- **P1 (#479, PR #482).** `AppearanceFile` (`ShinyBotHunter/Persistence/AppearanceFile.cs`) loads and saves `appearance` at `%LOCALAPPDATA%\ShinyBotHunter\appearance.json` with the controllers' JSON options, indented. `AdminService` takes the shared pair (MongoDB) and the appearance pair (the file); each mutator saves only its own store, through the one `saveGate`, after the push. `MongoDashboardConfigDao` removes `Appearance` from the document it saves and hands an old document's copy to `InitAsync`, which uses the file first, then that copy (written to the file once), then the defaults.
- **P2 (#480, PR #481).** `GetLatestEncounters` is deleted; `BuildGameCache` takes no `latest`; the list is filled in one place, `GameEncounterCache.Increment`, capped at 20. The cache load is two Postgres queries and one Mongo find.
- New: one class, one folder on disk. No package, interface, project or config key. No file under `PokemonDashboard/` changed.

## What diverged

1. **FR-5 is wider than a parse error.** A file whose whole content is `null`, and a null value under `windows`, are also "cannot be read": the warning, the defaults, the file left alone. `RespectNullableAnnotations` catches a null where the type has none (`"default": null`) but not those two, so `AppearanceFile.LoadAsync` has one condition for them (review of #482).
2. **Deleting the file does not always reset.** Right after the update the MongoDB document still has its `appearance` until the next save of the shared document, so a deleted file is filled from it once more (FR-8). The guide says so.
3. **P1 was size M, not S**: 391 added and 119 removed lines over eight files, most of it `AdminServiceTests.cs`.
4. **The 20 cap moved** from `AddEncounterToCacheAsync` into `Increment`, so the FR-9 test needs no database. Behaviour is the same.

## Evidence

| What | Result |
|---|---|
| `Scripts/ci-local.ps1` on the feature tip after both merges | PASS, 8/8 steps; 1108 tests |
| QA `dashboard-themes`, run `20261119-023358` | passed |
| QA `dashboard-alive`, run `20261119-023705` | passed: themes and animation levels apply and come back after the backend restarts |
| The dev PC after the QA runs | `%LOCALAPPDATA%\ShinyBotHunter\appearance.json` exists; the MongoDB `DashboardConfig` document has `GamePhases` and `RecentEncounterCount` and no `Appearance` |

Not run: the by-hand check on two PCs (different themes on each, both bots restarted); the Recent Encounters window after a restart with a hunt running (unit test only); FR-8's one-time copy on a real document (unit tests only: the QA scenarios reset `appearance` before it could be seen). The Updating and Versions guide pages have no line for this yet; that belongs to the version that carries it.

## Open questions

1. **FR-8, the one-time copy.** Answered 2026-11-18: "Keep the copy". Known limit: a second PC that updates after the first has saved a subscription change finds no copy left and starts on the defaults.
2. **Shared subscriptions still overwrite each other.** Two bots on one database each save their whole subscription set. Out of scope by the owner's "only appearance per-PC"; recorded so it is not a surprise. A fix would be a follow-up of its own.

## Changelog

- 2026-11-18: written at the owner's request ("write the spec, only appearance per-PC").
- 2026-11-18: the owner kept the one-time copy; FR-8 is a Must.
- 2026-11-18: recent encounters start empty at bot start (FR-9, G4), at the owner's choice; the task split is now two.
- 2026-11-18: implemented (epic #478, PRs #481 and #482); As built, What diverged and Evidence added.
