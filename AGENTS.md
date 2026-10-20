# ShinyBotHunter: instructions for agents

Read this before changing anything. The long form, with the reasoning, is in [docs/engineering-principles.md](docs/engineering-principles.md). When this file and that one disagree, that one wins and this file has a bug.

## What this is

A single-operator tool that drives BizHawk emulators to shiny-hunt Pokemon (HGSS and RS today), records every encounter, and shows counts on a local Electron dashboard and in Discord. One developer, one Windows machine, local databases. Not a product, not multi-tenant, not deployed. Design for that.

Architecture: [docs/architecture/system-overview.md](docs/architecture/system-overview.md). Current plan: [docs/plans/](docs/plans/). Decisions: [docs/decisions/](docs/decisions/).

## Repo map

| Path | What |
|---|---|
| `ShinyBotHunter/` | ASP.NET Core backend (net10): controllers, bot engine (`Bot/`, `BotEvents/`), emulator clients (`Emulators/`), persistence (`Persistence/`), services |
| `ShinyBotHunter.Tests/` | xUnit, hermetic (no ROM, emulator, DB or network) |
| `ApiHawk/` | net48 BizHawk external tool. Builds only against DLLs from the custom BizHawk fork in `ApiHawk/lib/` (gitignored, see `lib/.gitkeep`). Not in the solution or tests. |
| `PokemonDashboard/` | Electron + React + TypeScript operator UI |
| `docs/` | Planning docs. Edit these in planning mode; never let them drift from the code. |

The emulator is a custom fork: `ppicinic/BizHawk`, branch `fix/4535-sticky-analog`, built as 2.11.2. Do not add features to it or to `ApiHawk/` unless the task says so.

## Build and test

```
dotnet build ShinyBotHunter.Tests/ShinyBotHunter.Tests.csproj -c Release
dotnet test  ShinyBotHunter.Tests/ShinyBotHunter.Tests.csproj -c Release
cd PokemonDashboard && npm ci && npm run build
```
When `Scripts/ci-local.ps1` exists, run that instead; it is the definition of "CI passes". There is no CI on GitHub by decision; run the checks locally before any merge and say so in the PR. ApiHawk cannot build on a clean machine; do not try to make it.

## Rules applied on every change

1. **Smallest change that solves the stated problem.** If a bigger refactor seems warranted, describe it in one paragraph and stop. Do not do it unasked.
2. **No new abstraction without three existing call sites or two real implementations.** No interface with one implementation, no factory that builds one thing, no base class for one subclass. The one exception is a test seam for a boundary that cannot run in a test (emulator, database), named in the commit.
3. **Generalise only along the two known axes:** game flavor and event type, through the game profile and the event registry described in the architecture doc. Nothing else gets a plugin point, reflection discovery, or config-driven behaviour.
4. **No new tiers.** No backend-for-frontend, gateway, message bus, CQRS, or layered architecture. The dashboard calls the controllers directly.
5. **Two stores.** Postgres for the encounter log, Mongo for schema-fluid data. No third store, cache server, or queue product.
6. **No new packages, projects, interfaces, config keys or folders without listing each one in your summary with the reason.** Specifically not wanted: MediatR, AutoMapper, FluentValidation, Polly, Hangfire/Quartz, Refit, a second DI container; on the dashboard, a state, form, CSS or component library.
7. **Delete, don't deprecate.** Dead code goes in the same PR that makes it dead. No `*Deprecated` files, no commented-out blocks.
8. **Do not defend against the impossible.** No null checks, validation or retries for inputs and states this app cannot produce. Guard the real boundaries: dashboard requests, the emulator, the databases.
9. **Concurrency is explicit.** Every shared mutable field says in a comment how it is protected. Never `lock` across an `await`; never block on async.
10. **Errors are loud.** Catch only to add context, map to an HTTP status, or keep a loop alive with the full exception logged.
11. **Size tripwire.** A bug fix over about 200 changed lines, or a task over about 10 files, stops and reports instead of continuing.
12. **Smell words need a sentence.** `Manager`, `Helper`, `Base`, `Abstract`, `Provider`, `Strategy`, `Engine`, `Core`, `Common`, `Utils`: say why a plainer name does not fit, or rename.
13. **Tests are plain.** Arrange, act, assert. No test base classes, fixture frameworks, builders, or a mock per interface. `FakeMemoryClient` is the model.
14. **Do not touch adjacent code.** No renames, reformatting, comment rewrites, or doc generation for code the task did not change.
15. **Prefer deleting.** A PR that fixes the issue with net negative lines is the default good outcome.

## Workflow

- **Features follow [docs/specs/feature-workflow.md](docs/specs/feature-workflow.md):** feature doc and epic first, task issues with disjoint file lists, planner → coder (worktree, PR to the feature branch) → reviewer, orchestrator reviews plans and merges, owner merges the feature to `main`.
- **Non-trivial work starts as a doc.** Anything beyond a few lines gets a plan or spec under `docs/` first, written interactively with the owner, and code waits for an explicit go-ahead ("go ahead", "implement", "start phase 1"). "Looks good" approves the doc, not the coding.
- **One concern per branch and PR**, off `main`. Run the local checks on the branch tip before asking for a merge.
- **The PR description lists** what was added (packages, interfaces, projects, config keys, folders) and why, what was deleted, which docs were updated, and what was manually checked against a real emulator if anything.
- **Keep the docs true.** If a change makes an architecture or plan doc wrong, fix the doc in the same PR. If a decision changes, add a section to the relevant `docs/decisions/<area>.md`; never edit history.
- **QA.** When `ShinyBotHunter.Qa` exists, run the scenarios mapped to the changed area (`qa select --changed`) and link the run folder in the PR. Nothing from `qa-runs/` is committed.
- **Secrets and machine paths** (`D:/ROMs`, `D:/BizHawk`, tokens, connection strings) are never committed beyond the placeholders already in `appsettings.json`.

## When unsure

Ask, in one short question, before building. A wrong assumption that costs an hour is worse than a question that costs a minute.
