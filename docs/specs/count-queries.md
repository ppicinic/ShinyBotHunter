# Count Queries Spec

Status: Implemented 2026-10-21 (plan phase 3, epic #67) with the divergences in the Implementation section
Last updated: 2026-10-21
Related: [Code review 2026-10, section 1](../reviews/2026-10-code-review.md#1-query-performance-slow-first-load-of-counts), [Improvement plan, phase 3](../plans/2026-10-cleanup-and-hardening.md), [Decisions](../decisions/global.md)

## Summary

A cold dashboard load of one phase takes 10 to 30 seconds against 1 to 2 million encounter rows. The cause is the shape of the load, not the row count: the counts cache is filled one leaf at a time, each leaf is its own round trip (about 130 for a phase of ten Pokemon), every count is a full scan because the table has no secondary index, and all of it is serialised behind one static semaphore.

This spec replaces the per-leaf loads with **four round trips per game**: one grouped count query, one phase-progress query, the existing latest-20 query, and one Mongo aggregation for caught shinies. The cache tree is built in memory from those result sets. The owner's call (2026-10-21): the indexes alone are not enough, the query shape changes; and there is no cache invalidation, because the cache is keyed by the values stored in the rows, not by phase definitions.

Target: cold load of any phase under one second; warm loads cache-only, as today.

## Goals

- G1: first load of a game's counts is a fixed, small number of queries regardless of how many phases, Pokemon or game files it has.
- G2: no change to what the dashboard receives (`GameEncounterResponse` and friends keep their shape) and no change to the write path.
- G3: the in-memory increments on new encounters and catches keep working unchanged.
- G4: `IEncounterDao` shrinks from about 25 count methods to three.

## Non-goals

- Changing the storage split. Postgres keeps encounters; Mongo keeps caught Pokemon (decided 2026-10-17).
- Continuous aggregates or any Timescale feature beyond the hypertable that exists. Reconsider only if the measured result misses the target.
- Cache invalidation. Phase definitions decide what the dashboard asks for, not what the data holds; a key with no rows reads as zero.

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | Loading a game's counts issues at most 4 database round trips (3 Postgres, 1 Mongo) plus the caught-shinies list. | Must | Counted in the backend log, or in the QA scenario. |
| FR-2 | Every number the dashboard shows today is identical after the change. | Must | A test builds the tree from a fixed row set and checks every level; the QA `counts-cold-load` scenario compares against the old implementation's numbers on the same seeded data. |
| FR-3 | Cold load of a phase on a 1M-row table is under 1 second. | Must | Measured by the `counts-cold-load` QA scenario. |
| FR-4 | Increments on new encounters and catches behave as today. | Must | Existing behaviour, covered by the Route 33 QA scenario. |
| NFR-1 | No new packages, no new stores, no new config keys. | Must | |

## Current shape (for reference)

`PokemonPersistenceService` holds, per game, a tree:

```
GameEncounterCache
  AllPokemonCounts                        totals for the game
  PokemonEncounterCache[key]              per Pokemon key (form code, or "#<species>")
  PhaseEncounterCache[phase]
    AllPokemonCounts                      totals for the phase
    PokemonEncounterCache[key]            per Pokemon key within the phase
    PhaseCount                            encounters since the last shiny in the phase
  LatestEncounters                        last 20
  CaughtPokemon                           caught list
  GameLevelCache[gameFileName]            the same tree again, per ROM copy (no LatestEncounters)
```

Each `PokemonCounts` is `Encounters`, `ShinyEncounters`, `ShiniesCaught`. Today every node is filled by its own pair of `LongCountAsync` calls (`PostgreSQLEncounterDao.cs:46-115`), each `PhaseCount` by two more (`:116-141`), and each `ShiniesCaught` by a Mongo `CountDocuments`.

## Design

### Postgres: one grouped count query per game

```sql
SELECT "Milestone", "GameFileName", "FormCode", "Species",
       COUNT(*)                                AS encounters,
       COUNT(*) FILTER (WHERE "IsShiny")       AS shiny_encounters
FROM encounters
WHERE "Game" = @game
GROUP BY 1, 2, 3, 4;
```

One row per distinct (phase, ROM copy, form, species). That is hundreds to a few thousand rows for a game, not millions. Every count in the tree is a sum over a subset of these rows:

| Tree node | Rows summed |
|---|---|
| game `AllPokemonCounts` | all |
| game `PokemonEncounterCache[form]` | `FormCode = form` |
| game `PokemonEncounterCache["#id"]` | `Species = id` |
| phase `AllPokemonCounts` | `Milestone = phase` |
| phase `PokemonEncounterCache[form / #id]` | `Milestone = phase` and the key match |
| `GameLevelCache[file]` and everything under it | the same, with `GameFileName = file` added |

The builder is a pure function `rows -> GameEncounterCache`, which is what the unit test covers.

### Postgres: one phase-progress query per game

`PhaseCount` is "encounters in this phase since its last shiny", for the phase across all ROM copies and for each ROM copy. One query gives both:

```sql
WITH last_shiny AS (
    SELECT "Milestone", "GameFileName",
           MAX("Timestamp") FILTER (WHERE "IsShiny") AS last_shiny_at
    FROM encounters
    WHERE "Game" = @game
    GROUP BY 1, 2
)
SELECT e."Milestone", e."GameFileName", COUNT(*) AS since_last_shiny
FROM encounters e
JOIN last_shiny l ON l."Milestone" = e."Milestone" AND l."GameFileName" = e."GameFileName"
WHERE e."Game" = @game
  AND (l.last_shiny_at IS NULL OR e."Timestamp" > l.last_shiny_at)
GROUP BY 1, 2;
```

That is the per-ROM-copy value. The phase-level value today (`GetCurrentPhaseCount(game, phase)`) counts since the last shiny across all copies, which is not the sum of the per-copy values. It needs the same query grouped by `"Milestone"` alone; the two can be one query with `GROUPING SETS (("Milestone", "GameFileName"), ("Milestone"))`, or two cheap queries. Either is fine; the builder reads both results.

### Postgres: latest encounters

`GetLatestEncounters(game)` stays as it is: one `ORDER BY "Timestamp" DESC LIMIT 20`. With the index below it is an index scan.

### Postgres: encounter rate

`GetEncountersPerHour` (two variants) is not part of the cold load, but it runs a raw bins query and then a second LINQ query for the first and last timestamps. Merge them into one query that returns the bins plus `MIN`/`MAX` over the same window, and make `GameFileName` an optional filter so the two methods become one.

### Indexes

One migration:

```sql
CREATE INDEX encounters_game_milestone_file_time
    ON encounters ("Game", "Milestone", "GameFileName", "Timestamp" DESC)
    INCLUDE ("IsShiny", "FormCode", "Species");
```

It can serve the grouped query as an index-only scan, but on the 2M-row QA measurement the planner kept per-chunk seq scans (see Implementation); the latest-20 query uses the existing `Timestamp` index. On a Timescale hypertable the index is created per chunk; the `WHERE "Game" = @game` predicate still has to visit every chunk, since chunks are by time, which is why the grouped query is issued once per game and cached rather than per request.

### Mongo: one aggregation per game for caught shinies

Replace the eleven `GetAllCaught*CountAsync` methods with one `$group`:

```
[{ $match: { Game: <game>, "Pokemon.IsShiny": true } },
 { $group: { _id: { phase: "$Phase", file: "$GameFileName", form: "$Pokemon.FormCode", species: "$Pokemon.Species" }, n: { $sum: 1 } } }]
```

`ShiniesCaught` at every tree node is a sum over these rows with the same subset rules as above. `GetAllCaughtShiniesAsync(game)` (the list the dashboard shows) stays; the builder no longer filters it in memory per ROM copy, it groups it once.

### Cache loading

`GetOrLoadCacheAsync` becomes: if the game is not loaded, take the per-game lock, run the four queries, build the tree, store it. The per-form, per-phase and per-ROM-copy lazy loaders and their semaphore blocks (`PokemonPersistenceService.cs:189-340`) are deleted. A key the dashboard asks for that has no rows is created on demand with zero counts, exactly as `TryAdd` with a fresh `PokemonCounts` does today, so increments for a brand-new form or phase still have a node to land on.

The static semaphore becomes one `SemaphoreSlim` per game (or one global, held only for the load), so two games loading do not serialise.

### What does not change

- The write path: channel consumer, `Increment` on the tree, DAO queue, notifications.
- The DTOs the controllers return and the dashboard reads.
- `Increment` logic on `PokemonCounts`, `PhaseEncounterCache` and `GameEncounterCache`.

### `IEncounterDao` after

```
Task<List<CountRow>>      GetCountsAsync(GameFlavor game);          // grouped counts
Task<List<ProgressRow>>   GetPhaseProgressAsync(GameFlavor game);   // since-last-shiny, per phase and per phase+file
Task<List<PokemonData>>   GetLatestEncounters(GameFlavor game);     // unchanged
Task<double>              GetEncountersPerHour(GameFlavor game, string phase, string? gameFileName);
void Init(); Task WriteToQueueAsync(...)                            // unchanged
```

`MongoCaughtPokemonDao` loses the eleven count methods; the catches are counted from `GetAllCaughtShiniesAsync(game)` in the builder (see Implementation), so no `GetCaughtCountsAsync` was added.

## Test cases

| Case | Setup | Expected |
|---|---|---|
| Tree builder, game level | 6 rows across 2 phases, 2 files, 3 forms, one shiny | every node's three counts match hand-computed sums |
| Tree builder, missing key | dashboard asks for a form with no rows | node exists with zeros; an increment on it works |
| Phase progress | rows with shinies at known timestamps | since-last-shiny per phase and per phase+file match; phase with no shiny counts all rows |
| Species key | two forms of one species | `#species` sums both forms; each form counts only itself |
| Encounter rate | 24 hourly bins with gaps | same result as the current two-query implementation |
| Cold load (QA) | 1M seeded rows, cheat-free | under 1 s; numbers equal to the old implementation on the same data |

## Rollout

1. Migration with the index (ships alone; already a win).
2. New DAO methods alongside the old ones; the tree builder with its tests.
3. Switch `GetOrLoadCacheAsync` to the builder; run the QA cold-load comparison.
4. Delete the old DAO methods and the lazy loaders.

## Open questions
None. The dashboard's phase card (`PhaseCard.tsx:24-32`) reads the per-ROM-copy phase count when a copy is selected and the game-level count otherwise, so both values stay and the phase-progress query returns both groupings.

## Implementation (2026-10-21)
Shipped as plan rows 3.2 to 3.5 under epic #67 (PRs #94, #101, #104). Divergences from the design above:
- No Mongo aggregation. The builder counts catches from the caught list the load already fetches, so a cold load is 3 Postgres queries plus that list (FR-1 is met with one fewer round trip) and no `CaughtCountRow` exists.
- No `"Pokemon.IsShiny": true` filter. `IsShiny` is a computed getter the documents may not store, and the old loaders counted every caught document; FR-2 (identical numbers) wins.
- Caught documents without a `GameFileName` (written before May 2026) count at the game, phase and form levels and create no per-ROM-copy node, as the old loaders did.
- Phase progress is one statement with `UNION ALL` for the two groupings, because `GROUPING SETS` would apply one "since last shiny" filter to both.
- Measurement on the local QA database (2M synthetic rows, 366 chunks): the grouped query ran in 850 ms before and after the index (planner kept seq scans; 353 ms index-only with `enable_seqscan = off`); a cold phase load went from 18.6 s to 5.2 s and a warm load is about 40 ms. FR-3 is measured by the owner on the real database.

## Changelog
- 2026-10-21: first draft from the review findings and the owner's direction (query shape over indexes alone; no cache invalidation).
- 2026-10-21: implemented; divergences recorded in the Implementation section.
