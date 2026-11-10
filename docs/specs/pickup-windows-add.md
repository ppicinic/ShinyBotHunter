# Spec: add Pickup items windows per game file

Status: In progress (approved for build by the owner, 2026-11-10: "Implement both"; epic on feature/pickup-windows-add, branched from feature/dashboard-animation-cost because the two change the same files).
Last updated: 2026-11-10
Owner: Phil
Related: [Pickup items dashboard](pickup-items-dashboard.md) (the windows this changes), [Livelier dashboard](dashboard-alive.md) (the saved dashboard config this reuses), [the dashboard guide page](../guide/03-dashboard.md), [feature workflow](feature-workflow.md)

## Summary

Pickup items windows are added the way counts windows are: pick a game file in the Admin window and click Add. The window opens at once, is listed with a Remove, and is saved with the rest of the dashboard config, so it comes back after a restart of the dashboard or the bot. The `Pickup items windows` checkbox goes away. The window itself is pared down to the item pictures with their counts: no total line.

## Why

Today one checkbox turns the windows on. It is unticked at every dashboard start, and ticking it opens nothing until the bot has taken its first item on a game file, with no sign that it worked (owner, 2026-11-10: "this isn't intuitive to wait", "there needs to be a better way to add the pickup cards").

## Goals

- G1: the operator adds a Pickup items window for a game file and sees it straight away, before any item is taken.
- G2: the added windows survive a dashboard restart and a bot restart.
- G3: one way to do it. The checkbox and its code are deleted, not kept beside the new list.
- G4: the window shows only the item pictures and their counts (owner, 2026-11-10).

## Non-goals

- Any change to the tally: it still lives in the bot's memory, keeps adding across hunts and starts from zero when the bot restarts.
- Opening a window by itself when a Pickup hunt starts. A window exists because the operator added it.
- A change to the order of the items or to how many fit in a row.

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | The saved dashboard config gains the list of game files that have a Pickup items window (`pickupGameFiles`, game file handles, no duplicates). It is saved in the existing `DashboardConfig` document and pushed in `ReceiveDashboardConfig` with the rest. A saved document without the field loads as an empty list. | Must | Unit test: add, add again (no duplicate), remove; a document saved before this change loads with an empty list. |
| FR-2 | Two endpoints beside the subscription ones: add a game file to the list, remove one. Each saves and pushes the config, as subscribing does. Adding a handle the bot does not know is a 400. | Must | Unit tests on the service and the 400. |
| FR-3 | Admin window, Dashboard Config tab: the `Pickup Items Windows` section becomes a list of the added game files, each with a Remove, and an add row with the game-file selector the subscription row already uses and an Add button. Game files already added are not offered again. | Must | QA: Add shows the entry and opens the window; Remove closes it and drops the entry. |
| FR-4 | The dashboard opens one Pickup items window per game file in the pushed list and closes a window whose game file left it. The list, not the tally, decides which windows exist. | Must | QA: a window opens for a game file with no items taken. |
| FR-5 | A window with no tally shows the game file in its header and `No items yet`, as it does today for a missing tally; the milestone appears in the header with the first item. The window still loads the current tally when it opens and updates on each push. | Must | QA: the empty window, then the same window after a take. |
| FR-6 | A window closed by hand behaves as a counts window closed by hand does (the planner confirms what that is and the guide states it). | Must | Reviewer check against the counts windows. |
| FR-7 | Delete the checkbox, the `set-pickup-windows` and `get-pickup-windows` IPC calls, the switch flag in the main process and anything only they used. | Must | Reviewer check: no dead code left (AGENTS.md rule 7). |
| FR-8 | The Appearance section lists a row for each added Pickup items window, from the list, whether or not the window is open. | Should | QA: the row is there and its theme applies. |
| FR-10 | The window drops the total line under the items. Each tile is the item's picture with its count; the item's name is not printed, but shows as the tile's tooltip, and is printed in place of the picture for an item whose picture file is missing, so no tile is ever blank. | Must | QA: no total line; five tiles, each a picture and a count. |
| FR-11 | The `total` in the tally's JSON and push is deleted if nothing else reads it once the window stops showing it (the planner checks the QA scenario and the tests). | Should | Reviewer check: no unread field left. |
| FR-9 | The guide's "Watch the Pickup items" is rewritten for Add and Remove and for what the window now shows; the Pickup items spec gets a dated section recording that its switch (its G2, G3 and FR-7) is replaced by this spec. | Must | Reviewer check (the guide rule; decisions are added, not edited). |

## Design

- The bot side follows the subscriptions exactly: the list lives in `AdminService` beside them, under the same lock and the same save gate, and goes out in the same push. No new store, collection, interface, package or config key.
- The dashboard side keeps `syncPickupWindows` but feeds it the pushed list. The main process still holds the tallies (first load from `GET /api/bot/getPickupItems`, then each `ReceivePickupItems` push); when it fetches them is the planner's to settle now that no checkbox triggers it.
- The bot does not announce a hunt's start. The empty window needs only the game file, which the list already carries.

### New things

| Thing | Reason |
|---|---|
| `pickupGameFiles` on the saved and pushed dashboard config | Which windows exist, kept across restarts. |
| Two admin endpoints (add, remove) | The Add and Remove buttons. |

### Deleted

The checkbox, its two IPC calls and the main-process switch flag; the window's total line and printed item names; the `total` field if FR-11 finds it unread.

## QA

- `dashboard-alive` (no emulator): the Pickup items window is opened with Add instead of by hand; the empty window shows `No items yet`; its Appearance row is present and themes it; it is back after a dashboard restart and after a bot restart; Remove closes it.
- `hgss-pickup-items-window` (emulator): Add replaces the checkbox steps; the five items are checked as five tiles, each a picture with a count of 1, with no total line; a later take pushed in is unchanged.

## Suggested task split

| Task | Scope | Files |
|---|---|---|
| W1 | FR-1, FR-2, FR-11: the list, the endpoints, save and push; the `total` field; unit tests | `Service/IElectronClient.cs` (the config class), `Service/AdminService.cs`, `Controllers/AdminController.cs`, the dashboard config DAO if its mapping needs the field, `Service/PickupTally.cs`, tests |
| W2 | FR-3 to FR-10: the Admin section, the pared-down window, the window sync, the deletions, the Appearance row, the guide, the note in the Pickup items spec | `DashboardConfigTab.tsx`, `AppearanceSection.tsx`, `windows/PickupWindow.tsx`, `main/windowManager.ts`, `main/index.ts`, `main/signalRClient.ts`, `preload/index.ts`, `shared/windowContracts.ts`, `types/api.ts`, `types/global.d.ts`, `services/apiClient.ts`, `docs/guide/03-dashboard.md`, `docs/specs/pickup-items-dashboard.md` |
| W3 | The two QA scenarios and their rows | `dashboard-alive.spec.ts`, `hgss-pickup-items-window.spec.ts`, `docs/specs/qa-automation.md` |

W1 and W2 have disjoint files and run in parallel (W2 works from FR-1's field name). W3 after both.

## Open questions

None open. Settled at the go-ahead, which changed none of the draft's choices: a window closed by hand behaves as a counts window does (FR-6); Add lives on Dashboard Config only; the item names come off the tiles (tooltip, and printed when a picture is missing, FR-10).

## Changelog

- 2026-11-10: FR-10 and FR-11, the window shows only pictures and counts: "remove the bottom line that is total. i just want the item sprites with their counts as well".
- 2026-11-10: first draft, from the owner's choice of "add per game file" over a saved checkbox or fully automatic windows.
