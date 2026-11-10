# Spec: Pickup items dashboard

Status: In progress (approved by the owner 2026-11-10; feature/pickup-items).
Last updated: 2026-11-10
Owner: Phil
Related: [Pickup mode spec](pickup-mode.md) (the takes this counts), [the dashboard guide page](../guide/03-dashboard.md), [Wild encounters guide page](../guide/04-hunts/01-wild-encounter.md), [feature workflow](feature-workflow.md)

## Summary

A dashboard window per game that shows what the Pickup hunt has taken so far: each item by name with its count, and the total. The tally lives in the backend's memory only, keeps adding across hunts on the same game, and is gone when the backend restarts. The windows are off unless the operator turns them on in the Admin window; they are never opened on their own the way Recent Encounters is.

## Goals

- G1: the operator sees, per game file, how many of each item Pickup has taken since the backend started, by item name.
- G2: the windows open only when the operator turns them on, and close when turned off.
- G3: nothing new is persisted: no table, no collection, no file.

## Non-goals

- A timeline of takes (which slot, when). Totals only (owner, 2026-11-10).
- Keeping the tally across a backend restart, or clearing it when a new hunt starts. It keeps adding until the backend restarts (owner, 2026-11-10).
- Items the bot did not take: anything taken by hand in the emulator, mail (never visited, Pickup spec non-goal), or items given to a Pokémon.
- RS. Pickup mode is HGSS only.

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | The tally: a backend singleton holding, per game file name (the ROM handle, `IBot`'s game file as the encounter log uses it), a count per item id and the total. One shared field, guarded by one lock, with the comment that says so (AGENTS.md rule 9); no `await` under the lock. Empty at backend start; never cleared otherwise. | Must | Unit test: two takes of one item and one of another on game A, one on game B give the right counts per game; a read returns copies. |
| FR-2 | Recording: `PickupTakeEvent` records each take beside its existing `[Pickup] took item …` line, after the A that dismissed the take message was confirmed, so only items the bot took on screen are counted. | Must | Unit test over the existing scripted visit: two takes record two items on the bot's game; a stopped visit records only the takes that completed. |
| FR-3 | Item names: the English HGSS item list from PKHeX.Core, already a dependency (no new package). An id with no name reads `Item {id}`. | Must | Unit test: 17 is Potion, 50 Rare Candy; an unknown id gives `Item {id}`. |
| FR-4 | Read: `GET /api/bot/getPickupItems` returns every game's tally: game file name, total, and the items sorted by count, then name, each with id, name and count. | Must | Unit test on the controller or service; QA reads it. |
| FR-5 | Push: `IElectronClient` gains `ReceivePickupItems`, sent with that game's tally after each recorded take, as encounters are pushed today. | Must | Unit test that a take sends one push for its game; QA sees the window update. |
| FR-6 | The windows: a card kind `pickup`, one window per game file with a tally, titled `Pokemon Dashboard — Pickup: {game file}`, opened and closed like the phase windows (`syncPhaseWindows`'s pattern: the desired set is the games with a tally; a window the operator closed by hand stays closed until the toggle is turned off and on). Each window shows the item rows (name, count) and the total, and updates on the push. Bounds are saved per window like the others. | Must | Vitest on the row sort and total helper; QA: two games give two windows. |
| FR-7 | The toggle: a `Pickup items windows` switch in the Admin window's Dashboard Config tab. Off at every dashboard start (not saved). On opens the windows for every game that has a tally and keeps opening them as new games get one; off closes them all. No `config.json` key. | Must | QA: the switch opens and closes the windows; a dashboard restart starts with it off. |
| FR-8 | The guide page [the dashboard](../guide/03-dashboard.md) gets a section "Watch the Pickup items" in plain words: what the window shows, that it counts only what the bot took since the backend started, how to turn it on and off. | Must | Reviewer check (AGENTS.md, the guide rule). |

## Design

- The tally records even while the windows are off, so turning them on mid-hunt shows the whole session so far. The cost is a dictionary entry per take.
- The window kind reuses `openCardWindow`, the saved-bounds store and the IPC broadcast; the toggle calls a new IPC handler that sets a flag in the main process and runs the sync. The renderer page is a new `PickupWindow.tsx` beside `PhaseWindow.tsx`.
- Where the game file name comes from is the planner's to confirm: the same value `PokemonPersistenceService` writes as `GameFileName`, so a window's title matches the encounter rows.

### New things

| Thing | Reason |
|---|---|
| The tally singleton (name chosen at planning, no smell word) | The session's counts, shared by the event, the endpoint and the push. |
| `GET /api/bot/getPickupItems` | The window's first load. |
| `IElectronClient.ReceivePickupItems` | The live update. |
| Card kind `pickup`, `PickupWindow.tsx`, the toggle's IPC handler | The windows and the switch. |

No package, project, interface, config key, table or folder.

## QA

One scenario, `hgss-pickup-items-window`, on `hgss.qa-pickup-held` (five items in hand): the switch is off at start and no Pickup window exists; start the Pickup hunt; after the first visit turn the switch on: one window titled for `HG-Pickup-Held` shows the five items (Potion, Super Potion, Hyper Potion, Escape Rope, Rare Candy) at 1 each and a total of 5, matching `GET getPickupItems`; turn the switch off: the window closes; screenshots of both states; save equals `.bak`. A second game is covered by the unit tests, not QA.

## Suggested task split

| Task | Scope | Files |
|---|---|---|
| I1 | FR-1 to FR-5: the tally, the names, the endpoint, the push, the call in the event; unit tests | the new tally file, `PickupTakeEvent.cs`, `BotController.cs` (or the service it calls), `IElectronClient.cs`, `Program.cs` (the registration), tests |
| I2 | FR-6 to FR-8: the window kind, the page, the toggle, the guide | `windowManager.ts`, `index.ts` (main IPC), `preload/index.ts`, `signalRClient.ts`, `PickupWindow.tsx`, the Dashboard Config tab, `types/api.ts`, a helper test, `docs/guide/03-dashboard.md` |
| I3 | The QA scenario and its row | `ShinyBotHunter.Qa/dashboard/hgss-pickup-items-window.spec.ts`, `docs/specs/qa-automation.md` |

I1 and I2 have disjoint files and run in parallel (I2 works from FR-4's JSON shape). I3 after both.

## What diverged

Where the build differs from this spec as approved (2026-11-10). The requirements above stay as written; this list keeps the record.

- FR-1: the tally is `Service/PickupTally.cs`, registered in `Startup.cs` (the spec's task table said `Program.cs`). Its push is a delegate set by the DI constructor from `IHubContext<ElectronHub, IElectronClient>`; an `internal` constructor takes the delegate as the test seam (#296).
- FR-3: the names are `GameInfo.GetStrings("en").GetItemStrings(EntityContext.Gen4, GameVersion.HG)` from PKHeX.Core 26.7.7 (#296).
- FR-4: `BotController` takes the tally by action injection on `GetPickupItems`, not its constructor, so the thirteen existing `new BotController(...)` test calls stay unchanged (#296).
- FR-6: no dashboard sort or total helper and no Vitest for one. The backend sends the items sorted with the total (FR-4), so the window shows them as received (#297).
- FR-6, FR-7: the main process holds the tallies (fetched by the GET when the switch goes on, then each push) and the switch's state; the windows read main's copy over the IPC invoke `get-pickup-items` and the broadcast `pickup-items`, and the Dashboard Config tab reads the switch over `get-pickup-windows` on mount, because the tab unmounts when it is not shown (#297).
- FR-6: Pickup windows closed by hand have their own set, apart from the phase windows', so a phase sync cannot reopen them (#297).
- QA: `hgss-pickup-items-window` also waits for a later take with the window open and checks the push reached it, for at most 10 won battles (#298).

## Open questions

- None blocking. The planner confirms the PKHeX.Core call for the HGSS item list and the game-file value.

## Changelog

- 2026-11-10: first draft, from the owner's answers: totals only, keep adding until the backend restarts, a manual switch, one window per game.
