# Spec: cheaper dashboard animations and an off switch

Status: In progress (approved for build by the owner, 2026-11-10: "Implement both"; epic on feature/dashboard-animation-cost).
Last updated: 2026-11-10
Owner: Phil
Related: [Livelier dashboard](dashboard-alive.md) (the animations and themes this changes), [the dashboard guide page](../guide/03-dashboard.md), [feature workflow](feature-workflow.md)

## Summary

The theme animations cost too much on the prod PC (old hardware): with them on, the emulators drop frames. Two changes. First, an **Animations** switch in the Admin window's Appearance section, saved with the dashboard config, so the theme animations (the moving gradients, the snow and the shiny stars) can be turned off in one click. The GIF sprites, the drop-in and the idle movement are not part of it; they cause no trouble (owner, 2026-11-10). Second, the costly animations are rewritten to update a few times a second instead of on every frame, with the cost measured before and after.

## Why

Owner, 2026-11-10: "prod PC can't handle these animations the gradient change or the stars", "add a disable animations in the admin so its easy to unfuck this if performance sucks". Today the only way to stop them is the Windows setting "Animate controls and elements inside windows", which is per PC, outside the dashboard, and also removes the snow from the Christmas themes.

## Measured, 2026-11-10

The built dashboard from `main` (eeb05ea) on the dev PC (Ryzen 9 9950X3D, RTX 5080, 2560x1440 at 143 Hz), two windows (Recent Encounters 480x700, Admin 804x861), no bot running. One 10 second sample per row. CPU is the dashboard's own processes, in percent of one core. "Stepped" is the same animation with a `steps()` timing function, injected at run time; no source file was changed.

| What moves | CPU, smooth | CPU, stepped | GPU, smooth | GPU, stepped |
|---|---|---|---|---|
| Nothing | 0.0 | | 0.0 | |
| Gradient drift (HGSS Gold and Silver), about 2 steps a second | 45.7 | 0.0 | 4.9 | 0.1 |
| Glow orbit (HGSS Dusk), about 2 steps a second | 73.9 | 38.9 | 6.4 | 0.1 |
| Stars on 6 shiny stills | 1.1 | 1.2 | 4.2 | 0.1 |
| Snow (Christmas Pine), about 8 steps a second | 55.3 | 22.6 | 5.2 | 0.2 |
| 6 GIF sprites, nothing else | 7.0 | | 0.8 | |
| Everything (Christmas Night, 6 shiny GIFs) | 62.5 | 39.5 | 5.7 | 1.2 |
| Christmas Night, 6 shiny GIFs, every CSS animation off | 8.0 | | 0.6 | |

What the numbers say:

- The gradient drift is the clearest win: stepping takes it from 46 percent of a core to nothing measurable.
- The stars cost almost no CPU. Their cost is that they keep the graphics card presenting every frame; stepped, that goes to nothing too.
- Stepping alone is not enough for the glow orbit and the snow. The graphics side drops, but the page's own process stays at 35 and 13 percent of a core. These two need a different technique, not only a slower clock.
- The GIF sprites are cheap next to any of the smooth animations.

Limits: one sample each, on a PC far faster than prod and at 143 Hz (prod's screen is probably 60 Hz, so its smooth figures will differ). These show the relative saving, not what prod will do. The prod PC is the real test.

## Goals

- G1: one switch in the Admin window turns the gradients, the snow and the stars off and on, and the choice is saved.
- G2: with animations on, no theme costs much more than its GIF sprites alone.
- G3: the cost is measured by a script that can be run on any PC, prod included, so a change is judged by numbers.

## Non-goals

- A switch per animation or per window. One switch.
- Switching off the GIF sprites, the sprite drop-in or the idle movement of a still sprite. They stay as they are.
- Any change to the themes' colours, or to which sprites are shown.
- Changing the hunts, the bot engine or the emulator client.

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | The saved `appearance` gains `animations` (on or off; a saved config without it reads as on). It is posted with the rest of `appearance` and pushed to every dashboard, as the themes are. | Must | Unit test: round trip, and a document without the field loads as on. |
| FR-2 | The Appearance section shows an **Animations** checkbox above the Default row. Unticking it takes effect in every window at once, with no reload. | Must | QA: untick, then no gradient, snow or star animation is running in any window; tick, they run again; the choice is back after a dashboard restart and a bot restart. |
| FR-3 | With animations off, three things stop: the theme gradients (the header and card drift, the glow orbit and glow pulse, the light pulse on Christmas Night), the snow fall and the star twinkle. Nothing else changes: the GIF sprites keep playing, and the drop-in and the idle movement of a still sprite keep running. | Must | QA: none of those animations is running; a GIF sprite's `src` is still its `.gif`; a still sprite keeps its idle class and animation. |
| FR-4 | With animations off by the switch, the Christmas snow and the shiny stars stay drawn, still. Under Windows' "reduce motion" the snow also stays drawn, still, where today it is removed altogether. | Should | QA: the snow layers are present and not animating. |
| FR-5 | Windows' own "reduce motion" keeps working as today, except for FR-4: it still stops the drop-in and the idle movement as well, which the switch does not. The gradients, snow and stars run only when both allow them. | Must | The existing reduced-motion check in `dashboard-themes`, updated for FR-4. |
| FR-6 | The gradient drift (header and card fill, five themes) updates about twice a second. | Must | The measurement script: no more than 2 percent of a core over the still theme on the dev PC. |
| FR-7 | The star twinkle moves in steps, as the sprite drop-in and idle movements already do. | Must | The script: GPU use no more than 1 percent over the still stars. |
| FR-8 | The glow orbit (HGSS Dusk) and the snow (both Christmas themes) are rewritten so the page's own process is not busy between updates. The technique is the planner's to find and measure; stepping alone is shown above not to be enough. If none is found, the planner reports the best measured option and stops. | Must | The script: each no more than 5 percent of a core over the still theme on the dev PC. |
| FR-9 | The measurement script is committed under `ShinyBotHunter.Qa/dashboard/` with a line in the QA doc on how to run it. It needs no bot and no emulator, and prints the table above for the build it is pointed at. | Must | It runs on the dev PC and its output is pasted in the PR. |
| FR-10 | The guide's "Choose a theme" gains the Animations switch and when to use it; the Livelier dashboard spec gets a dated note that its animation rules are changed by this spec. | Must | Reviewer check (the guide rule; decisions are added, not edited). |

## Design

- The switch travels the path the themes already use: `Appearance` on the bot, `POST /api/admin/appearance`, the push, `utils/theme.ts` in each window. A window marks its root element (for example `data-animations="off"`) and one CSS block turns everything off, next to the existing reduced-motion block. No new endpoint, store, package or config key.
- For the first paint, the window remembers the last value in `localStorage` beside its remembered theme, so a restart with animations off does not flash them on.
- Because the switch is saved in the bot's dashboard config, it applies to every dashboard connected to that bot. Each PC runs its own bot, so prod can be off while the dev PC stays on.

### New things

| Thing | Reason |
|---|---|
| `animations` on `appearance` | The saved switch. |
| The measurement script under `ShinyBotHunter.Qa/dashboard/` | G3: judge the cost by numbers, on prod too. |

## QA

- `dashboard-themes` (no emulator): the reduced-motion check changes for FR-4; a new check for the switch (FR-2, FR-3).
- `dashboard-alive` (no emulator): the switch is back after both restarts.
- The measurement script, run on the dev PC before and after, both tables in the PR. Then the owner runs it, or simply watches the emulators' frame rate, on prod.

## Suggested task split

| Task | Scope | Files |
|---|---|---|
| C1 | FR-1: the field on the bot; unit tests | `Service/IElectronClient.cs` (`Appearance`), `Service/AdminService.cs` if its fallback needs it, tests |
| C2 | FR-2 to FR-5: the checkbox, the off state, still snow | `AppearanceSection.tsx`, `utils/theme.ts`, `types/api.ts`, `assets/sprites.css`, `assets/themes.css` (the off and reduced-motion blocks only), unit tests |
| C3 | FR-9: the measurement script and its doc line | `ShinyBotHunter.Qa/dashboard/` (new script), `docs/specs/qa-automation.md` |
| C4 | FR-6 to FR-8: the cheaper animations, measured with C3's script | `assets/themes.css` (the keyframes and theme animation lines), `assets/sprites.css` (the twinkle), `assets/index.css` if the card layers change |
| C5 | QA scenarios, the guide, the note in the Livelier dashboard spec | `dashboard-themes.spec.ts`, `dashboard-alive.spec.ts`, `docs/guide/03-dashboard.md`, `docs/specs/dashboard-alive.md` |

C1 and C3 run in parallel with C2. C4 comes after C2 and C3, since it shares the two CSS files with C2 and needs the script. C5 last.

The switch (C1, C2) can ship first on its own if prod needs relief before the cheaper animations are ready.

## Open questions

- FR-3: the light pulse and glow pulse on Christmas Night are counted with the gradients (built that way; the owner gave the go-ahead without changing it).
- FR-4: still snow and still stars when animations are off. Proposed on 2026-11-10 and not yet answered.
- FR-6 to FR-8: the limits (2, 1 and 5 percent on the dev PC) are a first guess. Prod's own numbers should set them once the script has run there.

## Changelog

- 2026-11-10: the switch covers only the gradients, the snow and the stars: "Gif doesnt cause issues. Animations should only be for gradients, snow, and stars".
- 2026-11-10: first draft, with the dev PC measurements, from the owner's ask for a measurement, a cheaper way and an off switch in Admin, as its own spec.
