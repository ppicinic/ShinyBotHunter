# Spec: a livelier dashboard (animated sprites, themes)

Status: Implemented (2026-11-10, epic #308; see "What diverged" and "Evidence"). The spike's prototype, scripts and mocks are on the `spike/dashboard-alive-prototype` branch, which is not for merge.
Last updated: 2026-11-10
Owner: Phil
Related: [research: the spike](../research/dashboard-alive.md) (the source for every count here), [pickup items dashboard](pickup-items-dashboard.md) (the item sprite fetcher this sits beside), [feature workflow](feature-workflow.md), the guide pages [Install](../guide/01-install.md) and [Dashboard](../guide/03-dashboard.md).

## Summary

Two changes to the dashboard, both in the look only.

**Animated sprites.** The Recent Encounters and milestone windows show a moving sprite for every Pokemon. Generations 1 to 5 use Black/White's animated pixel sprites; generations 6 to 9 use Pokemon Showdown's animated sprites (accepted by the owner on 2026-11-10 so that coverage is complete), then the fan-made pixel animations where Showdown has none. Forms that look like their base form take its animation, and the Alcremie variants come from Showdown's own site. The last 11 (six Iron Pokemon, Ogerpon and its three masks, Pecharunt), which neither has, come from Wikidex's clips of the Scarlet/Violet models, converted to the same small GIF. On this PC that animates all 1,395 forms, normal and shiny. A form that ever lacks an animation keeps its still sprite and is moved in CSS, DS-style, so nothing stands still. Female sprites are kept: every gender difference of generations 1 to 5 has its own female animation. Every sprite drops in when it appears, and a shiny twinkles.

**Themes.** Eight themes (Midnight, two HeartGold SoulSilver, three Ruby and Sapphire, two Christmas), the game themes with a slow moving gradient, the Christmas ones with snow in every window. All of it is set in one Appearance section of the Admin window's Dashboard Config tab (owner, 2026-11-10):

- a **default** theme for every window;
- an **override per window**: Recent Encounters, each milestone window, each Pickup Items window, so a Ruby game's windows can be Ruby while a HeartGold game's are Gold and Silver;
- an **optional night theme** wherever a theme is chosen. A row is one permanent theme unless its night switch is turned on; then it has a second theme, and the two switch times, set once, turn Gold and Silver to Dusk or Christmas Pine to Christmas Night by themselves.

The settings are part of the backend's dashboard config, so every dashboard shows the same, and the whole dashboard config (subscriptions, recent count, appearance) is saved, so it survives a restart of the dashboard and of the backend. Christmas is chosen by hand, not by date.

## Goals

- G1: every Pokemon moves: an animated sprite where the sources have one, a moved still sprite where they do not.
- G2: no female sprite is lost. A form shown with its female still today is shown with a female animation, or stays on the female still; never the male animation.
- G3: one look per generation: generations 1 to 5 pixel, 6 to 9 Showdown, with the other source only as a fill.
- G6: coverage is complete: a form is left on its still only when no animation of it exists in any of the sources, and the fetcher prints that list.
- G4: a theme is one block of colour values. Adding one touches the stylesheet and the theme list, no component.
- G5: Midnight looks as the dashboard does today.

## Non-goals

- Back sprites, item sprites, trainer sprites.
- Light themes. Status colours (red, green, amber, shiny gold) are tuned for a dark ground and are not themed.
- Per-window themes, a theme editor, or a theme set from the backend or Discord.
- Sound, fonts, or new layout. No package is added.
- Drawing animations for a form that has none; it gets the CSS movement.
- Switching themes by date. Christmas is chosen by hand.

## Requirements

### Sprites

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-1 | The Pokemon data fetcher (`PokeApiParser`, the "Fill in the Pokemon data" step) also downloads animated sprites into `{SpriteBaseDirectory}/Animated/{Default,Shiny,Female_Default,Female_Shiny}/{form}.gif`, named exactly as the still it sits beside. A file that exists is not downloaded again. | Must | Test on the URL choice; the folder counts after a run. |
| FR-2 | Source per form, in order, first hit wins. Species of generations 1 to 5 (national dex 649 or lower): (a) the form's `versions.generation-v.black-white.animated` URL, (b) its `other.showdown` URL. Species of generations 6 to 9: (b) then (a). Then for any generation: (c) the same two for the Pokemon the form belongs to, when the form is its default form or has no sprite of its own (`flabebe-red`, `gimmighoul-chest`, `koraidon-apex-build`); (d) Showdown's own `sprites/ani` folder by Showdown's name for the form (the Alcremie variants, `furfrou-la-reine`); (e) Wikidex's Scarlet/Violet model clip for the form (`<Spanish name> EP.gif` or `.webm`, and `EP variocolor` for the shiny), found through its MediaWiki API from a table of the forms that need it, cropped to the Pokemon, scaled to the height in the table and saved as a transparent GIF with `ffmpeg`. None: no file. | Must | Test: Pikachu and Deoxys-Attack take (a); Sprigatito takes (b); Iron Moth takes (a) as the fill; `flabebe-red` takes (c); an Alcremie variant takes (d); Iron Boulder gets none. |
| FR-2a | The fetcher ends by printing the forms left without an animation. On the data of 2026-11-10 that is none in `Default` and `Shiny`, and Eevee in the two female folders (FR-3). Step (e) needs `ffmpeg` on the PATH; without it the fetcher skips (e), says so, and lists the 11 forms it left. | Must | The run's output, with and without `ffmpeg`. |
| FR-3 | Female: a form with a female still gets the source's female animation (`front_female`, `front_shiny_female`) by the same order. Where there is none and the species is female-only (`gender_rate` 8), the ordinary animation is saved as the female one. Where the species has both genders, no female animation is saved. | Must | Test: Unfezant female takes the female GIF; Chansey takes the ordinary one; Eevee gets none. |
| FR-4 | The dashboard asks for `sprite://local/Animated/{folder}/{form}.gif` first, falls back to today's still on a load error, and shows nothing if that fails too. The `;f` rule for the folder is unchanged. | Must | Unit test on the two URLs; QA scenario. |
| FR-5 | A sprite is drawn at its own size, standing on the bottom edge of its box, and only shrinks to fit (96 px in Recent Encounters, the row's 24 to 96 px in a milestone window). It is never stretched. | Must | QA screenshot: Joltik smaller than Wailord. |
| FR-6 | Entry movement on every sprite when it appears: drop, squash, settle, 0.55 s, stepped. | Should | QA recording. |
| FR-7 | Idle movement on still sprites only: one of four stepped loops (hop, squash, sway, bob), picked by a hash of the form name so the same Pokemon always moves the same way. Animated sprites get none. | Should | Unit test: the pick is stable; QA recording. |
| FR-8 | A shiny sprite has two twinkling stars over it. | Could | QA screenshot. |
| FR-9 | With `prefers-reduced-motion`, the entry, idle, twinkle and snow are off; animated GIFs still play. | Should | Read in review. |

### Themes

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-10 | Theme colours are CSS variables holding RGB triplets on `:root`, overridden under `:root[data-theme='<id>']`. `tailwind.config.js` maps `slate` (100 to 900), `indigo` (500 to 700), `white` and the three `card-*` colours to them; `index.css` uses them in place of its hex values. No component's class names change. | Must | Build; Midnight screenshots match `main`. |
| FR-11 | Eight themes: `midnight` (default, today's values), `hgss-drift`, `hgss-dusk`, `rs-drift`, `ruby`, `sapphire`, `christmas`, `christmas-night`, with the values in the spike branch's `index.css` (generated by the run folder's `themes2.py` from the mocks). A theme may set a moving background (a wide gradient or two glows, drifting over 30 to 45 s), a header fill and a header title colour; rows and cells sit on it as translucent panels. Christmas adds falling snow in every window, as a fixed overlay of three layers at different sizes and speeds that takes no clicks. | Must | QA screenshots and a recording per theme. |
| FR-12 | The dashboard config the backend pushes gains `appearance`: `dayStart` and `nightStart` (local times, `HH:mm`, defaults `07:00` and `19:00`), `default` (a theme choice: `theme`, and an optional `night`), and `windows` (a map from window name to a theme choice). A window name is the one the dashboard already uses: `admin`, `recent`, `phase:<game>:<phase>:<gameFile>`, `pickup:<gameFile>`. A window with no entry uses `default`; the Admin window can have an entry like any other. | Must | Backend test on the shape; unit test on the lookup. |
| FR-13 | Persistence. The backend saves the whole dashboard config (subscriptions, recent count, `appearance`) as one MongoDB document on every change and loads it at start; today it lives in memory and a restart loses it. Each window also keeps the last theme it applied in `localStorage` and applies it before first paint, so it does not flash Midnight while connecting and keeps its theme while the backend is down. | Must | Backend test on save and load; QA: restart the backend and the dashboard, subscriptions and themes are back. |
| FR-16 | Day and night, optional per choice. Each window resolves its choice. With no `night` it shows `theme` permanently and arms no timer. With a `night` it shows `theme` from `dayStart` until `nightStart` and `night` otherwise, by the dashboard PC's clock, and switches at the boundary without a reload (one timer to the next boundary, re-armed after it fires and when the config changes). | Must | Unit test with a fake clock on both sides of each boundary and across midnight; QA with the times set a minute ahead. |
| FR-17 | The Appearance section, in the Dashboard Config tab of the Admin window and nowhere else: the two times; a Default row and one row per window (Admin, Recent Encounters, each active subscription's milestone window, each open Pickup Items window). A row has a Theme selector, whose first choice on a window row is "Default", and a "Switch at night" checkbox, off unless set; ticking it shows a Night selector. The two times are shown only while some row has the switch on. Each change posts the whole `appearance` to `POST /api/admin/appearance` and applies in every window on the pushed config. | Must | QA: set Ruby on one milestone window, the others keep the default. |
| FR-18 | The night theme is set by hand and nothing is filled in for the operator: ticking "Switch at night" shows the Night selector with no theme chosen, and the switch takes effect once one is picked. To make it quick, the Night selector lists the night themes first (HGSS Dusk, Christmas Night), then the rest; so Christmas Pine by day and Christmas Night by night is a tick and one pick. Any theme can be a night theme. | Must | Unit test on the list order; QA: tick, pick Christmas Night, set the time a minute ahead, the window switches. |
| FR-19 | An override whose window is gone (unsubscribed milestone, closed Pickup window) stays saved and applies again when the window returns. An unknown theme id or a malformed time in a loaded document falls back to Midnight and the default times, with a warning logged. | Should | Backend test. |
| FR-14 | Text on the accent colour (buttons) and on headers meets 4.5:1 contrast in every theme; the snow does not repeat on a visible grid. (Both failed by eye in the spike.) | Should | Review of the screenshots. |
| FR-15 | The guide's Dashboard page describes the selector and the themes; the Install page's sprite paragraph names the `Animated` folder and says the data step fills it (about 210 MB). | Must | Reviewer reads the pages. |

### Non-functional

| ID | Requirement | Verified by |
|---|---|---|
| NFR-1 | No package, project, interface or config key. New: one folder under the sprite folder (`Animated`), one MongoDB collection holding one document (the saved dashboard config), one admin endpoint, one object on the pushed dashboard config, one `localStorage` key per window. | PR description. |
| NFR-2 | A milestone window with 40 rows of animated sprites stays responsive (scrolling and resizing do not stutter). | Tried by hand on the largest milestone; recorded. |
| NFR-3 | The animated download adds no API calls: it uses fields of responses the fetcher already reads. | Review. |

## Design

### Where it lives

Backend (the fetcher only):

- `ShinyBotHunter/Scripts/PokeApiParser.cs`: the sprite records gain the two nested sources; one method picks the URL per FR-2 and FR-3; the existing `DownloadImageAsync` saves it. Species `gender_rate` is read from the species response the parser already fetches (to confirm in planning; if it does not fetch it, FR-3's female-only case needs one call per female-only species, and NFR-3 is amended).

Dashboard:

- `utils/sprites.ts`: `getAnimatedSpriteUrl` beside `getSpriteUrl`, sharing the folder rule.
- `components/PokemonSprite.tsx` (new): the animated-then-still fallback, the box, the movement classes. Used by `RecentPokemonCard` and `PhaseCard`, which lose their own `<img>` and error handler. Two call sites, not three; it is a component, not an abstraction, and it removes the duplicated fallback.
- `utils/theme.ts` (new): the theme list, `initTheme` (called in `main.tsx` before render: applies the remembered theme), `applyTheme` (called when a dashboard config arrives).
- `AppContext.tsx`, `types/api.ts`, `shared/windowContracts.ts`, `main/signalRClient.ts`, `services/apiClient.ts`: the `theme` field through the existing dashboard-config path, and the one request.
- `admin/DashboardConfigTab.tsx`: the selector.
- `tailwind.config.js`, `assets/index.css`: FR-10, FR-11, the movement keyframes.

Backend, for the theme: `AdminService` (the field, under the lock it already has, and the save), `AdminController` (the endpoint), `IElectronClient.cs` (`DashboardConfig.Theme`), and one small MongoDB class beside the existing ones for the one-document collection.

The `sprite` protocol and the preload are untouched. The protocol already serves any file under `spritesRootPath`.

### Where the settings are kept

The owner wants the same appearance on every dashboard and wants it to last. The backend's dashboard config is what every dashboard already receives on connect and on every change, so `appearance` rides on it. That config is held in memory in `AdminService` and is lost when the backend restarts, so the whole of it is now saved: one document in MongoDB (the store for schema-fluid data), written under the lock's snapshot on every change and read once at start. Subscriptions surviving a restart is a change in behaviour the owner asked for: after a restart the milestone windows reopen by themselves.

```json
{
  "gamePhases": [{ "game": "HGSS", "phase": "Johto Starters", "gameFileName": "HeartGold" }],
  "recentEncounterCount": 10,
  "appearance": {
    "dayStart": "07:00",
    "nightStart": "19:00",
    "default": { "theme": "hgss-drift", "night": "hgss-dusk" },
    "windows": {
      "admin": { "theme": "christmas" },
      "recent": { "theme": "midnight" },
      "phase:RS:Hoenn%20Starters:Ruby": { "theme": "ruby" },
      "pickup:Ruby": { "theme": "ruby" }
    }
  }
}
```

### Who decides the theme of a window

The window does, from the pushed config and its own name and clock: `choice = windows[name] ?? default`, `theme = choice.night && !isDay(now) ? choice.night : choice.theme`. The backend stores and pushes; it runs no clock and knows nothing of day and night. This keeps the switch exact on the PC that shows it, needs no push at the boundary, and keeps working while the backend is down. `isDay` handles a night that starts before the day (for example day 20:00, night 06:00) by comparing within the 24 hours.

`localStorage` holds only the last theme each window applied (key `dashboard-theme:<window name>`), for the first paint. The spike used `localStorage` as the store and the broadcast; that is replaced.

### The Appearance section

```
APPEARANCE
                                    Theme                   Switch at night
Default                             [HGSS Gold and Silver]  [x] [HGSS Dusk]
Admin                               [Christmas Pine]        [ ]
Recent Encounters                   [Default]               [ ]
HGSS / Johto Starters (HeartGold)   [Default]               [ ]
RS / Hoenn Starters (Ruby)          [Ruby]                  [ ]
Pickup Items (Ruby)                 [Ruby]                  [ ]

Day starts [07:00]   Night starts [19:00]      (shown while a row switches at night)
```

It sits at the top of the Dashboard Config tab, above Active Subscriptions. The window rows are the windows that exist now, plus any saved override whose window is gone (shown greyed, with a Remove).

### The moving backgrounds

A theme's background is a gradient several times wider than the window, moved by animating `background-position` over 30 to 45 s, or two soft radial glows that circle each other. Both are compositor work on one element per window; rows and cells are translucent so the colour shows through. `prefers-reduced-motion` stops the movement and leaves the gradient where it is.

2026-11-11: changed by the [dashboard animation cost spec](dashboard-animation-cost.md): Admin's Appearance section has three Animations settings, Gradients, Snow and Stars, each Off, Low, Medium or High. High is the CSS animation described here; Low and Medium move the same parts with a timer in each window; Off leaves the gradients still and does not draw the snow or the stars. Under reduced motion the snow and the stars are not drawn either.

### Why remap the palettes

About 290 class uses across 16 files name `slate`, `indigo` or `white`. Renaming them to semantic classes is a 16-file change for no visible gain; the remap is one config file. The cost is that `slate-400` no longer means Tailwind's slate in this project; the config comment says so.

## Tasks (proposed, for the epic)

| Task | Scope | Files | Depends on | Size |
|---|---|---|---|---|
| A1 | Fetcher: FR-1 to FR-3, NFR-3 | `PokeApiParser.cs`, its test file | none | M |
| A2 | Dashboard sprites: FR-4 to FR-9 | `utils/sprites.ts`, `PokemonSprite.tsx`, `RecentPokemonCard.tsx`, `PhaseCard.tsx`, a `sprites.css` of their own, a unit test | none (the spike's folder is already on this PC) | M |
| A3 | Config in the backend: `appearance` on the config, the endpoint, the save and load of the whole config (the backend half of FR-12, FR-13, FR-17, FR-19) | `AdminService.cs`, `AdminController.cs`, `IElectronClient.cs`, the new MongoDB class, `Program.cs` if the load needs wiring, tests | none | M |
| A4 | Theme colours: FR-10, FR-11, FR-14 | `tailwind.config.js`, `index.css`, a `themes.css` for the eight blocks, a screenshot check | none | M |
| A4b | Theme choice in the dashboard: the lookup, day and night, first paint, the Appearance section (FR-12, FR-13, FR-16 to FR-19, dashboard half) | `utils/theme.ts`, `main.tsx`, `AppContext.tsx`, `types/api.ts`, `shared/windowContracts.ts`, `main/signalRClient.ts`, `services/apiClient.ts`, `admin/AppearanceSection.tsx` (new), `DashboardConfigTab.tsx`, unit tests | A4 for the theme ids; works from this spec's JSON for A3 | M |
| A5 | Guide and QA: FR-15, the scenario below | `docs/guide/01-install.md`, `docs/guide/03-dashboard.md`, `ShinyBotHunter.Qa/dashboard/dashboard-alive.spec.ts`, `docs/specs/qa-automation.md` | A1 to A4b | S |

The pickup-items work in flight touches `PokeApiParser` (the item fetcher) and adds the Pickup Items windows this spec themes; this epic starts after it has merged.

## QA scenario

`dashboard-alive`, no emulator: the backend and the built dashboard with its own user-data folder, sample encounters fed over the SignalR IPC channels (as the spike's `demo3.js` does), and check:

- an animated form, a female form and a form with no animation each load the expected file (read from the page), and the last has an idle class;
- choosing a default theme in Admin changes `data-theme` in the Recent and milestone windows;
- an override on one milestone window changes that window only, and "Default" puts it back;
- with the switch times set a minute ahead, a window with the night switch on changes at the boundary without a reload;
- after a restart of the dashboard, and after a restart of the backend, the subscriptions, the default and the overrides are all back;
- screenshots of Recent, Admin and a milestone window per theme, and a recording, in the run folder.

## Open questions

None.

## Decided

- Showdown's sprites are used for generations 6 to 9, so that coverage is complete (owner, 2026-11-10, after first preferring pixel only).
- The theme list (owner, 2026-11-10): Midnight, HGSS Gold and Silver (H1), HGSS Dusk (H3), Ruby and Sapphire (S1), Ruby (S4), Sapphire (S5), Christmas Pine (C1), Christmas Night (C2). That H1 and H3, and C1 and C2, are separate entries rather than one of each is the spike's reading of "I like both"; to confirm.
- HeartGold SoulSilver: H1 and H3 liked. Christmas: C1 and C2 both liked. No emerald in the Hoenn theme: Ruby and Sapphire only (owner, 2026-11-10).
- Christmas snow falls in every window (owner, 2026-11-10).
- Christmas is chosen by hand, not by date (owner, 2026-11-10).
- The theme is in the backend's dashboard config and persists between sessions (owner, 2026-11-10).
- The whole dashboard config is saved, so subscriptions come back after a restart (owner, 2026-11-10).
- A theme can be overridden per window: Recent Encounters, each milestone window, each Pickup Items window (owner, 2026-11-10).
- Day and night themes switch by time, with defaults; all of it is configured in the Admin window (owner, 2026-11-10).
- No night versions of Ruby, Sapphire or Ruby and Sapphire; the night partners are HGSS Dusk and Christmas Night only (owner, 2026-11-10).
- The night theme is picked by hand, with no automatic pairing, and must be quick to set; Christmas Pine to Christmas Night is the example (owner, 2026-11-10).
- The data step uses `ffmpeg` for the 11 Wikidex forms when it is on the PATH and skips them, saying so, when it is not (owner, 2026-11-10).
- Pecharunt uses the open-shell pose for both normal and shiny (owner, 2026-11-10).
- No further themes in the first build (owner, 2026-11-10).
- The night switch is optional per row; a row without it is one permanent theme. The Admin window has its own override row (owner, 2026-11-10).

## What diverged

- **FR-3, female-only species.** The parser does not read `pokemon-species`, so there is no `gender_rate`. A species is female-only when its `front_female` still equals its `front_default` in the record already fetched (45 of the 147 female stills). No call is added (NFR-3 stands); the result is the one FR-3 asks for: Chansey takes the ordinary animation, Eevee gets none.
- **FR-2 (c).** On the data of 2026-11-10 `flabebe-red`, `gimmighoul-chest` and `koraidon-apex-build` carry their own animated URLs and take (a) or (b). (c) is "the form's own record has none and the form has no Showdown name", which today is `arceus-unknown` only. `maushold-family-of-three` is a (d) name (Showdown's `maushold`).
- **FR-15, size.** The `Animated` folder is 230 MB after the run on the dev PC, not about 210; the Install page says 230.
- **A3, files.** The registration and the load at start are in `Startup.cs`; `Program.cs` only calls it. A `SemaphoreSlim` orders the saves; the lock is still never held across an await. The fallback of FR-19 runs on load only, not on the POST body.
- **A4, the screenshot check.** It is its own scenario, `dashboard-themes` (Midnight byte-equal to `origin/main`, per-theme screenshots, the contrast table, snow and reduced motion). `dashboard-alive` does not repeat the comparison; QA runs both.
- **A4, FR-14.** Midnight keeps `main`'s values, including 4.47:1 for button text on its hover colour (G5 outranks FR-14). The other seven themes meet 4.5:1 on accents and on every header stop under the title; each theme's `--accent-500` is darker than the spike's, which also deepens focus borders, the active tab line and checkboxes. Not changed: on HGSS Gold and Silver the connection pill's label in the Admin header is about 2.1:1.
- **A4b, files.** `main/windowManager.ts` gained two lines: the window list is broadcast when a card window opens and closes, so the Appearance section's rows follow the windows.
- **FR-17.** "Switch at night" is disabled on a window row set to "Default" (there is no entry to carry a night theme, and making one would pre-fill it). Equal day and night times mean night all day. A saved override whose window is gone shows greyed with its theme as text and a Remove.
- **QA, the Pickup Items window.** Main opens one only from a running Pickup hunt's tally, so the scenario opens it by hand; its theme lookup is checked, its Appearance row is not.

## Evidence

QA on the feature tip 5824d0b, 2026-11-10. The run folders are under `ShinyBotHunter.Qa/dashboard/qa-runs/` on the dev PC (gitignored).

- `dashboard-themes`: `20261110-132318-dashboard-themes`, green; Midnight byte-equal to `origin/main` on Recent, Admin and a milestone window.
- `dashboard-alive`, five cycles green in a row: `20261110-132455`, `-132746`, `-133030`, `-133231`, `-133548` (`-dashboard-alive`). Recent switched to Christmas Night 57 to 179 ms after the boundary with no reload; subscriptions, the default, the overrides (one on a window name holding a dot) and the night switch were back after the dashboard restart and after the backend restart. Screenshots per theme and the recordings are in each folder.
- Two earlier runs on the task branch failed (`20261110-130417`, `-130919`): during the wait for the boundary the Appearance settings changed. The recordings show the focus ring and the selects stepping one option at a time, which is keys typed elsewhere on the PC reaching the focused Admin window; the review found no path in the code that posts without a control event. The scenario now moves the focus to Recent before the wait and asserts the settings are unchanged after it.
- The data step on the dev PC, on an empty `Animated` folder. Without `ffmpeg` on the PATH: the skip line names the 11 forms; 1,384 of 1,395 in Default and Shiny, 142 of 147 in the female folders. With `ffmpeg`: 1,395 of 1,395 in Default and Shiny, 146 of 147 in the female folders (Eevee), 230 MB.

## 2026-11-10: Pickup rows from the saved list

Since [Add Pickup items windows per game file](pickup-windows-add.md), the Appearance section lists a row for each added Pickup items window from the pushed `pickupGameFiles`, open or not, and no longer reads the window list (FR-17 said "each open Pickup Items window"). The two window-list broadcasts of A4b stay: the Window Layout tab reads them.

## Changelog

- 2026-11-10: drafted from the spike.
- 2026-11-10: owner review. Showdown dropped, pixel only (FR-2, G3); theme moved to the backend config and saved (FR-12, FR-13, A3); snow everywhere and Christmas manual decided; moving gradients and the mocks added (FR-11, Q1).
- 2026-11-10: second owner review. Showdown back in for generations 6 to 9 with the fills (FR-2, FR-2a, G6): 1,384 of 1,395 animated. H1, H3, C1, C2 liked; Hoenn theme is Ruby and Sapphire only, second mocks added.
- 2026-11-10: third owner review. S1, S4 and S5 picked; the eight themes are in the prototype (FR-11); Q1 closed.
- 2026-11-10: fourth owner review. Whole config saved (FR-13); per-window overrides (FR-12, FR-17, FR-19); day and night by time (FR-16, FR-18); Appearance section in Admin; A4 split, A3 widened.
- 2026-11-10: fifth owner review. Night switch optional per row (FR-12, FR-16, FR-17); Admin gets an override row.
- 2026-11-10: sixth owner review. No automatic night pairing; the night theme is picked by hand from a selector that lists the night themes first (FR-18).
- 2026-11-10: the 11 edge cases found on Wikidex and converted (FR-2 step (e), FR-2a, Q6, Q7): 1,395 of 1,395.
- 2026-11-10: ffmpeg optional and Pecharunt's open pose decided; Q6 and Q7 closed.
- 2026-11-10: approved for build; no further themes; Q5 closed.
- 2026-11-10: implemented (epic #308); "What diverged" and "Evidence" added.
- 2026-11-11: dated note under "The moving backgrounds": the Animations settings of the [dashboard animation cost spec](dashboard-animation-cost.md) change these animation rules.
