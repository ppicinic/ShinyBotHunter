# Decisions: Global

Status: Living document
Last updated: 2026-10-17

Project-wide decisions that are not tied to one feature. Feature-specific decisions live in their own file in this folder (for example `persistence.md`, `bot-engine.md`, `dashboard.md`), created when the first decision for that area is made. A decision is never deleted; when it changes, mark the old section **Superseded** and link to the new one.

Each decision: context, options considered, what was chosen and why, consequences.

---

## CI runs locally before merge, not on GitHub Actions

Status: Accepted
Date: 2026-10-17

### Context
`.github/workflows/build-and-test.yml` runs a `windows-latest` job on every push to every branch and again on every pull request. Windows runners bill at twice the Linux rate on private repos, there is no NuGet cache and no cancel-in-progress, so a typical branch with an open PR costs roughly 12 to 20 billed minutes per push. The owner does not want to spend on hosted runners until self-hosted local runners exist.

The test suite itself is cheap and hermetic: about 109 xUnit tests, none needing a ROM, BizHawk, a database or the network. The dashboard has no tests, no lint and no typecheck script at all.

### Options
- **A. Keep the workflow as is.** Nothing to do; PRs show a green check. Ongoing spend on every push; doubled runs on PR branches.
- **B. Delete the workflow, run checks by hand.** Zero spend. No record of what was run; easy to forget.
- **C. Manual-only workflow plus a local script that runs the same steps.** Zero spend (a `workflow_dispatch`-only workflow never runs on its own); the workflow stays as a reference and is ready for a self-hosted runner; the script is the single definition of "what CI means". Green checks on PRs disappear until a runner exists.

### Decision
Option C.

- Change the workflow triggers to `workflow_dispatch` only. Do not add `push` or `pull_request` back until a self-hosted runner is registered.
- Add `Scripts/ci-local.ps1` that runs, in order: `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release` on `ShinyBotHunter.Tests`, then `npm ci`, a TypeScript typecheck and `npm run build` in `PokemonDashboard`. It exits non-zero on the first failure. The workflow calls the same script so the two cannot drift.
- Add `typecheck` (and later `lint`) scripts to `PokemonDashboard/package.json`.
- Rule for merging into `main`: run `Scripts/ci-local.ps1` on the branch tip first and say so in the PR description.

### Consequences
- No GitHub Actions spend.
- When a local runner exists, re-enabling CI is a one-line trigger change plus `runs-on: self-hosted`.
- ApiHawk stays outside CI because it builds against uncommitted BizHawk assemblies; `Scripts/deploy-apihawk.ps1` remains the manual path for it.
- Follow-up work is in the [improvement plan](../plans/2026-10-cleanup-and-hardening.md).

---

## Storage: Postgres for encounters, MongoDB for schema-fluid data

Status: Accepted
Date: 2026-10-17

### Context
The app runs two databases. PostgreSQL with TimescaleDB holds the encounter log (1 to 2 million rows and growing). MongoDB holds caught shinies, species and move metadata scraped from PokeAPI, phase definitions, and starter-hunt state. The October 2026 review asked whether to consolidate onto Postgres to remove a database and let one grouped query return every count.

### Options
- **A. Consolidate onto Postgres.** One engine, one query for all counts. Costs a migration for four collections and fixes the shape of data that still changes as hunts are added.
- **B. Keep both, with a stated rule for what goes where.** No migration; the slow-load fix becomes one Postgres grouped query plus one Mongo aggregation.

### Decision
Option B. Postgres is for append-only, fixed-shape, high-volume data. MongoDB is for data whose shape is still evolving and whose volume is small: caught Pokemon, metadata, phases, hunt state. New data goes to whichever matches; nothing moves between them without a decision here.

### Consequences
- Principle 2.4 restated to match.
- Plan phase 3 drops the Mongo-to-Postgres tasks and adds a single caught-shinies aggregation.
- The persistence service keeps two DAOs; the counts cache builder consumes both result sets.

---

## Lua BizHawk integration is removed

Status: Accepted
Date: 2026-10-17

### Context
Before ApiHawk, the backend drove BizHawk through Lua scripts over raw TCP. ApiHawk replaced it, but the Lua path is still compiled in: `BizHawkLuaLoader` writes `Loader.lua` on every start, `BotFactory` selects the Lua launcher whenever `BizHawkMode` is not `ApiHawk` (and the code default is `LuaNet`), and that branch is already broken because it pairs the Lua launcher with the HTTP clients.

### Decision
Delete it: `Lua Scripts/`, `BizHawkLuaLoader`, the Lua `BizHawkLauncher`, the legacy TCP `BizHawkClient`, the `BotFactory` branch, and the `BizHawkMode` and `LuaBaseDirectory` settings. `BizHawkApiHawkLauncher` is the only launcher.

### Consequences
- One launcher, one client pair, one grid formula.
- A missing config key can no longer silently select a dead mode.
- Macro recording and the validated party read, which already required ApiHawk, lose their mode checks.

---

## Setup scripts stay in the repo as provisioning tools

Status: Accepted
Date: 2026-10-17

### Context
`--parsepokeapi` (PokeAPI scraper into Mongo metadata) and `--generatemapdatahgss` (HGSS map and graph data under `Data/HGSS/`) run from CLI flags in `Startup`. They looked like one-off scripts, but they are how a new PC is provisioned.

### Decision
Keep them. Move the CLI dispatch out of `Startup.MainAsync` into a small `SetupCommands` entry that runs without starting Discord or the bot service, turn the hard-coded `D:\` input paths into arguments, and delete the parts that are unreachable (`MapHeaderProcessor`, the duplicate move-API flag). The Influx migration scripts are not provisioning and go with the Influx removal.

### Consequences
- A fresh machine can be set up with two commands and a Mongo instance.
- `Startup` shrinks to host composition.

---

## Extension axes: game flavor and event type, through two seams

Status: Accepted
Date: 2026-10-17

### Context
More games across generations and consoles, and more event types, are expected. Today a new game means editing five files that carry HGSS addresses plus the keyed registrations; a new event type means a new branch in two `BotService` methods and a static map. Principle 2.2 originally said "build for the flavors we have", which would have blocked fixing this.

### Options
- **A. Keep adding branches and copies.** Cheapest per game until the third one.
- **B. Two seams, nothing more.** A game profile per flavor as the sole home of game knowledge, and an event registry that `BotService` dispatches through.
- **C. Plugin architecture.** Assemblies or config discovered at runtime. Over-engineering for one operator.

### Decision
Option B. Flavors may support different capabilities, and that is declared on the profile rather than discovered through `NotSupportedException`. Parsers are registered per generation and reused across flavors. No third axis (emulator, platform, runtime discovery) is generalised.

### Consequences
- Principle 2.2 reworded; architecture section 11 added; plan tasks 5.3 to 5.3d.
- `IDataReader` is kept, but a flavor that cannot read something says so through a capability flag, and the action is refused before an event starts.

---

## QA captures from outside the emulator; nothing from a run is committed

Status: Accepted
Date: 2026-10-17

### Context
The QA harness needs screenshots and, optionally, recordings of emulator runs as evidence. BizHawk has screenshot and movie APIs that the fork or ApiHawk could expose.

### Decision
Do not add capture features to ApiHawk or the BizHawk fork. Capture the EmuHawk windows from outside with Win32 window capture and `ffmpeg gdigrab`, and the dashboard with Playwright. Run output lives in a gitignored `qa-runs/` folder and is never committed. The fork stays limited to what the bot needs to function.

### Consequences
- The fork's diff from upstream stays small and rebase-able.
- ffmpeg becomes a dev-machine prerequisite for `--record` only.
- Evidence is shared by copying a run folder, not through git.

---

## QA uses a separate database on the same local servers

Status: Accepted
Date: 2026-10-17

### Decision
`appsettings.Qa.json` points at a separate Postgres database and a separate Mongo database on the same local servers as development. No container orchestration. The dev PC is not the streaming PC, so a QA run cannot disturb a live hunt.

---

## Memory addresses are keyed by ROM revision

Status: Accepted
Date: 2026-10-17

### Context
The pointer services hold one address set per game. The ROMs hunted today are revision 1.1 or 1.2. Gen 3 RAM layouts differ between revisions, and a future hunt (Jirachi, for example) may need a different one. `RomInfoService` already reports the revision at launch.

### Decision
The game profile (plan 5.3) keys address tables by `(flavor, revision)` where a revision differs. Only the revision in use today is mapped; adding another is a separate decision when a hunt needs it. A profile with no table for the launched revision refuses the launch with a clear message rather than running with the wrong addresses.

---

## Default branch is `main`; first tag is `v0.1.0`

Status: Accepted
Date: 2026-10-19

### Context
Work had been merged into `sprint1`, a long-lived branch named after a sprint, with `sprint1.init` as the GitHub default. The agent workflow needs one stable base branch that feature branches start from and the owner merges into.

### Decision
`main` is the default branch, created from the tip of `sprint1` (`123b4c4`) and tagged `v0.1.0`. Feature branches start from `main` and the owner merges them back into it. `sprint1` and the `sprint1.*` branches stay on GitHub as history and receive no new work.

### Consequences
- Every doc that named `sprint1` as the base now names `main`.
- Versions are tagged on `main` as `vMAJOR.MINOR.PATCH` when the owner decides a release point.

---

## QA forces shinies with BizHawk cheat files armed by hotkey

Status: Accepted
Date: 2026-10-20

### Context
QA scenarios need a shiny on demand, after some normal encounters. The October spec proposed an RNG seed write through ApiHawk and a battle-struct patch through a new ApiHawk write-range opcode. The owner does not want cheat or injection features in the built tooling. A spike found that HeartGold already has a shiny wild-generation branch, and that BizHawk's own cheat system can apply a guarded code patch and be toggled from a hotkey.

### Options
- **A. ApiHawk endpoints for cheats or memory injection.** Precise and scriptable; puts QA features into tooling that ships with the hunting setup.
- **B. Cheat files plus BizHawk's Toggle All Cheats hotkey.** No code in any repo project; depends on window focus for the keypress; cheats cannot be cleanly turned off once applied.

### Decision
Option B. The QA harness writes the cheat file for the ROM under test, loaded disabled, and arms it with the hotkey. Wild shinies use a two-halfword code patch that redirects grass generation to the game's shiny branch; starters use an RNG freeze. Every code-patch line is guarded with an `Equal` compare. The QA install turns off cheat auto-save.

### Consequences
- `ShinyBotHunter/`, ApiHawk and the fork contain nothing for QA; plan task 4b.2 (write-range opcode) is dropped.
- Arming needs the EmuHawk window focused, so QA runs need the dev PC's desktop and do not run in the background.
- Cheat addresses are per game and ROM revision and live in the QA project, keyed like the game profile.

---

## QA recording uses PrintWindow, not ffmpeg gdigrab

Status: Accepted; refines "QA captures from outside the emulator"
Date: 2026-10-20

### Context
`ffmpeg -f gdigrab` on the EmuHawk window recorded blank white frames, because BizHawk draws the game with OpenGL. Win32 `PrintWindow` with the full-content flag captures it correctly, including when the window is partly covered.

### Decision
Screenshots and recordings both use `PrintWindow`; the recorder pipes 15 fps raw frames into ffmpeg for encoding. Capture stays outside the emulator, and run output stays uncommitted, as decided on 2026-10-17.

---

## QA cheats live in the QA project; QA saves live beside the repo

Status: Accepted
Date: 2026-10-20

### Context
Each game, generation and ROM revision needs its own cheat, so cheats are versioned assets that should be reviewed like code. Saves are personal game data tied to one owner's playthrough.

### Decision
- Cheats are stored per game and revision under `ShinyBotHunter.Qa/Cheats/<game>-<region>-<revision>/`. No deployable project (`ShinyBotHunter/`, `ApiHawk/`, `PokemonDashboard/`, the fork) contains QA code or cheats, and none references the QA project.
- Saves live outside the repo in a sibling folder, `../shinybothunterqasaves/` relative to the repo root, with a README describing each one. `Scripts/qa-load-save.ps1` copies one into the configured BizHawk install's Save RAM folder; an allow rule in `.claude/settings.json` lets the QA agent run that script unprompted. Nothing writes back to the source.

### Consequences
- Adding a game or revision to QA means adding a cheat folder and a save; no deployable code changes.
- A fresh machine needs the saves folder copied over by hand; the repo alone is not enough to run QA.
