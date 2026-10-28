# User Guide Plan

Status: Implemented (2026-10-27) on `feature/user-guide`, epic #198; PR to `main` opened by the orchestrator for the owner to merge
Last updated: 2026-10-27
Owner: Phil
Related: [feature workflow](../specs/feature-workflow.md), [architecture](../architecture/system-overview.md), [decisions](../decisions/global.md)

## Goal

Someone who is not the owner can set up the bot on their own Windows PC, run every supported hunt from the dashboard, and understand what it logs and where, from documents that are reviewed with the code and stay true as the code changes. Today the repo has no root `README.md`; the only entry points are `AGENTS.md` (written for agents), the planning docs under `docs/` (written for the owner and the agents), and the dashboard's own README.

## Where the docs live

Decided 2026-10-26: a folder of markdown pages in this repository, `docs/guide/`, read on GitHub. The repository is private, GitHub wikis and Pages are not available to it on the current plan, and a Pages site would be public anyway. The pages are reviewed with the code, so the keep-true rule (U14) reaches them.

Navigation is by convention, since there is no sidebar:
- The root `README.md` is one screen: what the bot is, and a link to the guide. GitHub renders relative links, so it points at `docs/guide/README.md`.
- `docs/guide/README.md` is the table of contents and renders automatically when the folder is opened: Start here, Install, Configure, The dashboard, Hunts (one line per hunt), Discord, Your data, When something goes wrong, Contributing, each a link with a one-line description.
- File names are numbered in reading order (`01-install.md`, `02-configure.md`, `03-dashboard.md`, `04-hunts/01-wild-encounter.md`, ...) so the folder listing reads top to bottom even without the index.
- Every page starts with a line `Part of the [ShinyBotHunter guide](README.md)` and ends with `Next: [page](...)`, so a reader who arrived from a search or a link can always get back and go forward.
- Headings are plain questions or tasks ("What you need", "Install BizHawk", "Start a hunt"), so the page outline GitHub shows at the top right of a markdown file is a usable mini-sidebar.
- A reader never needs `docs/plans/`, `docs/specs/` or `docs/research/`; the guide links out to them only under a "Why it works this way" line where someone might want the reasoning.

Writing rules for guide pages, so a non-programmer can follow them:
- Say what to click, type or copy, in order; one action per step; the expected result after each step.
- No code identifiers, file paths or class names in the text unless the reader has to type or open them, and then exactly as they must type them. Settings keys appear once, in the configuration page's table, with the plain-English meaning first.
- The files a page is derived from are listed in an HTML comment at the top, invisible when GitHub renders the page, for writers and reviewers.
- Troubleshooting entries are written as "you see X" then "it means Y" then "do Z".

## Audience

Operators: a person running the bot on their own PC with their own game files (owner's answer, 2026-10-26). The owner is the only contributor, so there is no contributor page; the root README has one line pointing at `AGENTS.md` for anyone who wants to change the code.

## Pages

Each page names, in a comment at the top, the files it is derived from, so a writer takes every statement from code or config and a reviewer can check it. Nothing in the guide is written from memory of how the bot "probably" works.

| # | Page | What it covers | Derived from |
|---|---|---|---|
| U0 | `docs/guide/README.md` and the numbered skeleton | The table of contents in reading order, with a one-line description per page; the numbered file skeleton with each page holding only its title, the "Part of the guide" line and the "Next" line, so later tasks fill pages in without changing the structure; the "Start here" text, written for someone who has never seen the repository: what the bot does in three sentences, what you need, and where to begin. | this plan |
| U1 | `README.md` (root) | One screen: what the bot is, the link to the guide, and one line pointing at `AGENTS.md` for anyone changing the code. | the guide's README |
| U2 | `docs/guide/01-install.md` | Prerequisites (Windows, .NET 10, Node, PostgreSQL with TimescaleDB, MongoDB); the BizHawk fork: why the bot needs it (`ppicinic/BizHawk`, branch `fix/4535-sticky-analog`, built as 2.11.2), how to get and build it following the fork's own build instructions, and installing ApiHawk into it (`Scripts/deploy-apihawk.ps1`); Dolphin and the GBA BIOS (`Scripts/deploy-dolphin.ps1`); folders for ROMs, saves and sprites and how files are named; building the backend and the dashboard; first run and how to tell it worked. | `Scripts/*.ps1`, `ShinyBotHunter.csproj`, `PokemonDashboard/README.md`, `BizHawkApiHawkLauncher.cs`, `RomInfoService.cs`, the fork's README |
| U3 | `docs/guide/02-configure.md` | Every `ApplicationSettings` key and connection string: what it does, an example, which are secrets. The secrets file: its path (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`), that it is read in Release too, that `appsettings.Production.json` and environment variables are not read. | `ApplicationSettings.cs`, `appsettings.json`, `Startup.cs` |
| U4 | `docs/guide/03-dashboard.md` | The Electron dashboard: launching emulators, milestones (made in the UI, stored in Mongo), the actions list and parameters, counts and the encounter log, Resume, cancel, sound. | `PokemonDashboard/src/renderer/src/components/admin/*`, `EventRegistry.cs`, `BotDtos.cs` |
| U5 | `docs/guide/04-hunts/01-wild-encounter.md` | HGSS wild and chained encounters: the save state it needs (where to stand), parameters (target, train settings, ranges), what the bot does, healing, what it logs, stopping. | `OrchestratorEvent.cs`, `BattleOrchestratorEvent.cs`, `hgss-state-reader.md` |
| U6 | `docs/guide/04-hunts/02-hgss-starter.md` | HGSS shiny starter hunt. | `MultiShinyHunterEvent.cs`, `ShinyStarterEvent.cs` |
| U7 | `docs/guide/04-hunts/03-rs-starter.md` | RS soft-reset starter hunt: ROM copies, waves, saves, retired copies, parked shinies, Resume. | `MultiStarterHuntEvent.cs`, `StarterHuntEvent.cs`, `RsStarterHunt.json` |
| U8 | `docs/guide/04-hunts/04-togepi-egg.md` | HGSS Togepi egg hunt. | `HgssTogepiEggEvent.cs`, `TogepiEgg.json` |
| U9 | `docs/guide/04-hunts/05-wishmaker-jirachi.md` | The Jirachi hunt: what the save needs (Pokédex, free slot, rev 1.2 ROM), the Dolphin setup, the cycle in plain words, the `-WISHMKR` files, the shiny stop. | `docs/specs/wishmaker-jirachi.md`, `WishmakerJirachi.json` |
| U10 | `docs/guide/05-discord.md` | Creating the Discord bot and getting the guild, channel and mention ids; what it posts (shiny alerts, new-move prompts) and how to answer a prompt. | `DiscordBotService.cs`, `ShinyAlertService.cs` |
| U11 | `docs/guide/06-your-data.md` | What is stored where: Postgres encounters (the row's fields), Mongo caught Pokémon, milestones and hunt state; how to look at them; backups. | `PersistPokemonDataDao.cs`, `MongoCaughtPokemonDao.cs`, `system-overview.md` |
| U12 | `docs/guide/07-when-something-goes-wrong.md` | The failures an operator meets and what they mean: ApiHawk trust prompt, SaveRAM not appearing, a refused action (capability or revision), Dolphin not subscribing to the pad, a stale-timeout message. | the launchers, `BotService.cs`, the specs' timeout tables |
| U13 | `docs/guide/08-updating.md` | Keeping the bot current: the versions the bot is built and tested against (.NET 10, Node 18+, the BizHawk fork built as 2.11.2, Dolphin 2609a, PostgreSQL with TimescaleDB, MongoDB) and where each is read from; how to update the bot (pull, rebuild backend and dashboard, redeploy ApiHawk after a BizHawk rebuild); the database migrations: the list that ships in `Migrations/`, how to tell whether the database is behind, and how the tables are created and brought up to date (today nothing in the bot or its scripts applies migrations, which the page states as the code gap it is). | `ShinyBotHunter.csproj`, `Migrations/*`, `PokemonDbContext.cs`, `Startup.cs`, `Scripts/*.ps1`, `PokemonDashboard/package.json`, `DolphinLauncher.cs`, `ApiHawk/lib/.gitkeep` |
| U14 | The keep-true rule | `AGENTS.md` gains one line under "Keep the docs true": a change to a registry entry, a settings key, a script under `Scripts/`, or a dashboard action updates the guide page in the same PR, and the reviewer checks it. The reviewer prompt in `feature-workflow.md` gets the same line. | `AGENTS.md`, `docs/specs/feature-workflow.md` |

## How the pages are written and checked

- One task per page or pair of pages, disjoint files, so up to four run in parallel. U1 and U14 go last, after the pages exist.
- The writer is an agent reading the named source files; it may run the backend and the dashboard to confirm what a screen shows, and it must not describe a step it could not find in code or config. Where the truth lives only in the owner's head (how the BizHawk fork is built, where to get a GBA BIOS), the page says "ask the owner" in a marked line, and the owner fills it in at review.
- Setup is not verified by a walk-through (owner, 2026-10-26). The install and configure pages say how to run everything, and the keep-true rule keeps them current.
- Screenshots: allowed where a picture saves a paragraph (the dashboard's action form, a Discord prompt), under `docs/guide/images/`, named after the page; never required, and a page must still make sense with the images removed, since the keep-true rule can check a sentence but not a picture (owner: no preference, 2026-10-26).
- Game files: the guide says the operator supplies their own dumps and saves and names the file formats and folders. It never says where to obtain a ROM, a BIOS or a disc image.

## Not in scope

- A wiki, a hosted site, search or versioned docs. Markdown on GitHub for the current `main`; a site generator over `docs/guide/` is a later, mechanical step if the repository ever goes public.
- Rewriting the planning docs for operators. They stay as they are and the guide links to them where a reader wants the reasoning.
- Documenting QA. It is covered by its spec and is a contributor concern.

## Rows

| # | Task | Deliverable | Depends on | Size |
|---|---|---|---|---|
| 8.0 | The skeleton | U0: the guide's table of contents, the numbered placeholder pages with their navigation lines, the "Start here" text; merged first so every later task fills a page that already has its place | — | S |
| 8.1 | Setup and configuration | U2, U3 | 8.0 | M |
| 8.2 | Dashboard and data | U4, U11 | 8.0 | M |
| 8.3 | HGSS hunts | U5, U6, U8 | 8.0 | M |
| 8.4 | RS hunts | U7, U9 | 8.0 | M |
| 8.5 | Discord and troubleshooting | U10, U12 | 8.0 | S |
| 8.6 | Front door and the rule | U1, U14 | 8.1 to 8.5 | S |
| 8.7 | Updating and versions | U13, plus its line in the guide README and the Next link from U12; the install page's "how are the tables created" question points at it. Replaces the fresh-clone walk-through (owner, 2026-10-26: no walk-through; the docs say how to run everything) | 8.1, 8.6 | S |

## Implementation notes (2026-10-27)

- Rows 8.0 to 8.7 merged in PRs #207, #208, #211, #210, #209, #212, #213 and #215 (task issues #199 to #205 and #214); `Scripts/ci-local.ps1` passed 8/8 on every tip. #206, the fresh-clone walk-through, was closed unplanned (owner: not necessary).
- What diverged: 8.7 is the updating-and-versions page U13, not a walk-through. No screenshots were taken and `docs/guide/images/` does not exist. Each page cites more read-only sources than its page-table row names; the HTML comment on the page is the authoritative list.
- Owner review: the pages carry marked "Ask the owner" lines (`grep "Ask the owner" docs/guide`), mostly the BizHawk fork build and install, Discord's own screens, in-game facts for the hunts, and troubleshooting actions. They stay until the owner fills them in.
- Code gaps the pages exposed, listed on the epic's handoff comment as candidate issues: nothing applies the EF migrations on a new PC (and `docs/specs/qa-automation.md` line 157 claims the QA harness does, which no code backs); the shipped Discord placeholder `your-token-here` is not the value the skip check looks for; three errors reach the dashboard only as a generic server error; `deploy-apihawk.ps1` defaults to an owner path; ROM copies and `-WISHMKR` ROMs appear in the Game lists; a missing SoulSilver ROM is never reported; the Togepi hunt can end silently; `system-overview.md` sections 6 and 9 and the dashboard README have drifted.

## Open questions

All answered by the owner on 2026-10-26: a markdown folder in the repository; operators only, no contributor page; screenshots allowed but never required; the install page documents the BizHawk fork and how to build it; nothing about the owner's own machines (dev and streaming PCs are the same setup twice, which the secrets file explains per machine). None remain.

## Changelog

- 2026-10-26: first draft from the interview; the five questions answered the same day.
- 2026-10-26: 8.7 is no longer a walk-through (owner: not necessary); it is U13, an updating-and-versions page that also tracks whether an EF migration is needed. 8.0 to 8.6 merged on `feature/user-guide` the same day.
- 2026-10-27: 8.7 (U13) merged; status Implemented; implementation notes added.
