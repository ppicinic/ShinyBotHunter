# Spec: Pickup items dashboard

Status: In progress (approved by the owner 2026-11-10; feature/pickup-items).
Last updated: 2026-11-10 (item sprites added, awaiting go-ahead)
Owner: Phil
Related: [Pickup mode spec](pickup-mode.md) (the takes this counts), [the dashboard guide page](../guide/03-dashboard.md), [Wild encounters guide page](../guide/04-hunts/01-wild-encounter.md), [feature workflow](feature-workflow.md)

## Summary

A dashboard window per game that shows what the Pickup hunt has taken so far: each item by name with its count, and the total. The tally lives in the backend's memory only, keeps adding across hunts on the same game, and is gone when the backend restarts. The windows are off unless the operator turns them on in the Admin window; they are never opened on their own the way Recent Encounters is.

## Goals

- G1: the operator sees, per game file, how many of each item Pickup has taken since the backend started, by item name.
- G2: the windows open only when the operator turns them on, and close when turned off.
- G3: nothing new is persisted: no table, no collection, no file. (The item sprites in G4 are downloaded pictures, like the Pokémon sprites, not session data.)
- G4: each item shows its picture, several items to a row,, fetched once from pokeapi.co the way the Pokémon sprites are (owner, 2026-11-10).

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
| FR-9 | The item sprite fetcher: `PokeApiParser` gains a step that lists every item on pokeapi.co (`item?limit=3000`), reads each item, and for an item with a `generation-iv` game index and a `sprites.default` picture downloads it to `{SpriteBaseDirectory}/Items/{game index}.png`, through the existing `DownloadImageAsync`, which skips a file already there. The game index is the HGSS item id the tally counts (checked: Potion 17, King's Rock 221). It creates the `Items` folder if missing. It runs at the end of `--parsepokeapi`, and alone as `--parseitemsprites`, so an install that already has its Pokémon data need not redo the MongoDB write. An item with no sprite, or a failed request, is logged with its name and skipped; the step keeps going. | Must | Run by hand once against pokeapi.co: `Items/17.png` is the Potion and `Items/221.png` King's Rock. No unit test: the step is the network and file boundary, as the Pokémon scrape is. |
| FR-10 | The window lays the items out several to a row, as many as its width fits (a CSS grid with `auto-fill`), to save space (owner, 2026-11-10). Each cell shows the item's sprite from `sprite://local/Items/{id}.png`, served by the dashboard's existing `sprite` protocol under `spritesRootPath`, with the name and count under it; the order stays the backend's (FR-4), read left to right. The total stays on its own line below the grid. An item with no file shows no picture and keeps its cell, name and count (the image hides on its load error). | Must | QA: the five cells show their pictures, more than one to a row, in the screenshot. |
| FR-11 | The guide: [Install](../guide/01-install.md)'s "Fill in the Pokemon data" says it also fills an `Items` folder with item pictures and gives `--parseitemsprites` for an install that only needs those; [the dashboard](../guide/03-dashboard.md)'s "Watch the Pickup items" says each row shows the item's picture when the folder has it. | Must | Reviewer check. |

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
| The `--parseitemsprites` setup command | Fetch the item pictures without rerunning the whole Pokémon scrape (FR-9). |
| The `Items` folder under the sprite folder | Where the item pictures live, beside `Default`, `Shiny` and the female folders (FR-9). It is on the operator's PC, not in the repo. |

No package, project, interface, config key or table. The fetcher reuses `SpriteBaseDirectory`; the window reuses the `sprite` protocol and `spritesRootPath`.

## QA

One scenario, `hgss-pickup-items-window`, on `hgss.qa-pickup-held` (five items in hand): the switch is off at start and no Pickup window exists; start the Pickup hunt; after the first visit turn the switch on: one window titled for `HG-Pickup-Held` shows the five items (Potion, Super Potion, Hyper Potion, Escape Rope, Rare Candy) at 1 each and a total of 5, matching `GET getPickupItems`; turn the switch off: the window closes; screenshots of both states; save equals `.bak`. A second game is covered by the unit tests, not QA.

With the item sprites (FR-10), the same scenario's screenshot shows a picture in each of the five cells, more than one to a row. QA's sprite folder must hold `Items/` first, so `--parseitemsprites` is run once on the QA PC before the run.

## Suggested task split

| Task | Scope | Files |
|---|---|---|
| I1 | FR-1 to FR-5: the tally, the names, the endpoint, the push, the call in the event; unit tests | the new tally file, `PickupTakeEvent.cs`, `BotController.cs` (or the service it calls), `IElectronClient.cs`, `Program.cs` (the registration), tests |
| I2 | FR-6 to FR-8: the window kind, the page, the toggle, the guide | `windowManager.ts`, `index.ts` (main IPC), `preload/index.ts`, `signalRClient.ts`, `PickupWindow.tsx`, the Dashboard Config tab, `types/api.ts`, a helper test, `docs/guide/03-dashboard.md` |
| I3 | The QA scenario and its row | `ShinyBotHunter.Qa/dashboard/hgss-pickup-items-window.spec.ts`, `docs/specs/qa-automation.md` |

I1 and I2 have disjoint files and run in parallel (I2 works from FR-4's JSON shape). I3 after both.

The item sprites (added 2026-11-10, after I1 to I3 merged):

| Task | Scope | Files |
|---|---|---|
| I4 | FR-9 and FR-11's Install half: the item sprite step and the command | `Scripts/PokeApiParser.cs`, `Tools/SetupCommands.cs`, `docs/guide/01-install.md` |
| I5 | FR-10, FR-11's dashboard half, and the QA screenshot check: the item grid with sprites | `PickupWindow.tsx`, the CSS file the window's rows use, `docs/guide/03-dashboard.md`, `ShinyBotHunter.Qa/dashboard/hgss-pickup-items-window.spec.ts` |

I4 and I5 have disjoint files and run in parallel. I5 works from FR-9's file name; QA runs on the tip after both.

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
- FR-9 reads every item on pokeapi.co, about 2 200 requests, because the item list does not say which items are in generation IV. It runs once per PC, like the Pokémon scrape.

## Changelog

- 2026-11-10: item sprites, FR-9 to FR-11, from the owner's ask: "let's add sprites and let's add a sprite image fetcher like we do for pokemon". FR-10 then made a grid, several items to a row: "i think items can be multiple per row to consolidate space".
- 2026-11-10: first draft, from the owner's answers: totals only, keep adding until the backend restarts, a manual switch, one window per game.
