# Engineering Principles

Status: Draft
Last updated: 2026-10-17
Related: [System architecture](architecture/system-overview.md), [Code review 2026-10](reviews/2026-10-code-review.md)

Ground rules for anyone, human or AI, changing this repo. They exist because an AI assistant given a vague brief will reliably add abstraction, configuration and "future-proofing" that nobody asked for, and because a one-person hobby project cannot carry that weight. When a rule here conflicts with a request, say so and follow the request only if it is restated.

## 1. What this project is

A single-operator tool that drives BizHawk emulators to hunt shiny Pokemon, records encounters, and shows them on a local Electron dashboard and in Discord. One developer, one machine, local databases. It is not a product, not multi-tenant, and not deployed anywhere. Every design decision should be made for that reality, not for a hypothetical team or scale.

## 2. Rules against over-engineering

Each rule has a test you can apply before writing code.

### 2.1 Three strikes before an abstraction
Do not introduce an interface, base class, factory, generic, or "service" until there are **three concrete call sites** (or two real implementations) that need it. One implementation behind an interface is a cost with no benefit: it hides the code, doubles the files, and makes navigation worse.

- Test: can you name the second implementation that exists *today*? If not, no interface.
- Allowed exception: a seam needed by a test that cannot otherwise run without an emulator or database (e.g. `IMemoryClient` + `FakeMemoryClient`). Name the test in the commit.

### 2.2 Generalise along the two known axes, and no others
The project will grow in exactly two directions: **more game flavors** (across generations and consoles) and **more event types** (hunts, eggs, navigation, training). Those two axes get a real seam each, described in the [architecture doc](architecture/system-overview.md#11-extension-axes): a game profile per flavor that is the only home for game-specific knowledge, and an event registry that `BotService` dispatches through. Adding a game or an event means adding to those seams and nothing else.

Everything else stays concrete. No "any emulator" abstraction, no plugin loading, no reflection discovery, no config-driven behaviour that exists to avoid a future code edit.

- Test: does the change add a flavor or an event type through the seam? Good. Does it add a third axis nobody has asked for? Stop.
- Flavors may support different capabilities. A flavor with no overworld pathfinding or no egg mechanic says so in its profile; events check the capability and refuse up front instead of discovering a `NotSupportedException` mid-run.

### 2.3 No backend-for-frontend, API gateway, or extra tiers
There is one backend and one client on the same machine. A BFF, gateway, GraphQL layer, CQRS split, message bus, or "domain/application/infrastructure" layering adds hops with nothing on the other side. The dashboard calls the controllers directly and that is correct.

- Test: count the processes a request passes through. If a change raises it, it needs a decision recorded in `docs/decisions/global.md`.
- When it *would* apply: a second, differently shaped client (e.g. a mobile app needing different payloads) or a remote deployment. Neither is planned. Record in the roadmap if that changes.

### 2.4 Two stores, each with a job
PostgreSQL/Timescale holds the high-volume, fixed-shape encounter log. MongoDB holds schema-fluid data: caught Pokemon, species metadata, phase definitions, hunt state. Do not add a third store, a cache server, or a queue product to solve a problem an index or a query would solve.

- Test: before adding Redis/RabbitMQ/anything, show the measured number that the existing engine cannot hit.
- Placement test: will the shape change as features are added, and is the row count small? Mongo. Is it append-only and counted in the millions? Postgres.

### 2.5 Configuration only for things that actually change
A value goes in `appsettings.json` only if it differs between the dev machine and another real machine, or is changed at runtime by the operator. Everything else is a `const` next to the code that uses it, with a comment saying why it is that value.

- Test: has anyone changed this value in the last year? Would they need to without a rebuild?
- Do not add feature flags for in-progress work; use a branch.

### 2.6 Straight-line code over clever code
Prefer a 40-line method you can read top to bottom over three classes that implement a pattern. Patterns (strategy, visitor, mediator, pipeline) require a stated reason in the PR. LINQ is fine; LINQ that needs a comment to explain it is not.

- Test: can a newcomer find where a dashboard button's behaviour is implemented in under two file hops?

### 2.7 Keep files under about 500 lines and methods under about 60
Not a hard limit, a smell detector. When a file passes it, the next change to that file should split it along a real seam (not into `Helpers.cs`). Several files are already well over (see review); they are listed in the plan.

### 2.8 Delete, don't deprecate
Dead code is removed in the same PR that makes it dead. Git has the history. No `*Deprecated.cs`, no commented-out blocks, no `#if false`, no "keep for reference". One-off scripts that ran once (migrations, scrapers) are removed after they run, or moved to a `tools/` project that does not ship.

### 2.9 Concurrency is explicit
Any shared mutable state touched by more than one request, task or bot must say how it is protected in a comment at the declaration: a lock, a `SemaphoreSlim`, a `Concurrent*` collection, a channel, or "single-threaded by construction because X". If you cannot write that sentence, the state is a bug.

- Prefer single-writer designs (one background loop owning the state, requests post messages to it) over fine-grained locks.
- Never `lock` across an `await`; use `SemaphoreSlim`.
- Never block on async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`).

### 2.10 Errors are loud and typed
Catch an exception only to add context, translate it to an HTTP status, or keep a long-running loop alive (and then log it with the full exception). `catch (Exception) { Console.WriteLine(...); return NotFound(); }` hides bugs behind a 404. Prefer `ILogger` over `Console.WriteLine` for anything that is not a CLI.

### 2.11 Unit tests for logic, QA scenarios for the loop
Parsers, pathfinding, config loading, DAO query shapes, cache arithmetic and state machines are testable without BizHawk and should have unit tests. The backend-to-emulator loop is not, and that is where the bugs are, so it gets automated QA scenarios that drive the real app and real emulators through the public API with a shiny injected into the game (see the [QA spec](specs/qa-automation.md)). Scenarios are selected by what changed; the full set runs on request. No QA switch ever lives in `ShinyBotHunter/`: the shiny is forced inside the emulator so production code sees a real one.

### 2.12 AI assistant working agreement
When an AI assistant (Claude or similar) works in this repo:
- Read this file and the architecture doc before proposing structure.
- Propose the smallest change that solves the stated problem. If a bigger refactor seems warranted, describe it in one paragraph and stop; do not do it unasked.
- Do not add: new NuGet/npm packages, new projects, new interfaces, new config keys, or new folders without saying so explicitly in the summary and why.
- Do not "improve" code adjacent to the change (renames, reformatting, comment rewrites) unless asked.
- Do not generate docs, READMEs or XML comments for code that was not changed.
- When asked to plan, write the plan in `docs/` and wait for a go-ahead before coding.
- Prefer deleting over adding. A PR that removes net lines while fixing the issue is the default good outcome.

### 2.13 Size tripwire
A bug fix that passes about 200 changed lines, or any task that touches more than about 10 files, stops. Report what was found, what the small fix is, and what the bigger change would be, and wait. An AI does not feel the cost of a large diff; this rule is the substitute.

### 2.14 Smell words need a sentence
A new type or member named with `Manager`, `Helper`, `Base`, `Abstract`, `Provider`, `Strategy`, `Engine`, `Core`, `Common`, `Utils`, `Generic`, or `Framework` needs one sentence in the PR saying what it is for and why a plainer name does not fit. Not a ban; a prompt. Most of the time the sentence cannot be written and the type goes away.

### 2.15 Do not defend against the impossible
This is a single-operator tool with known inputs. No null checks for values that cannot be null here, no validation of states the app cannot reach, no retry around a local call, no "in case this is called from another thread" when it is not. Guard the boundaries that exist: the dashboard request, the emulator, the database. Everything inside those is trusted.

- Test: can you describe the real input or state the guard protects against? If it needs "someday" or "in theory", delete the guard.

### 2.16 Dependencies, by name
Before adding a package, the question is "what does the code look like without it". Specifically not wanted here, because a method or a built-in does the job: MediatR, AutoMapper, Mapster, FluentValidation, Polly (the retry in `BizHawkClientBase` is twenty lines), Serilog sinks beyond console, Hangfire or Quartz, Refit, a DI framework other than the built-in one, and on the dashboard a state library (Redux, Zustand, MobX), a form library, a CSS framework, or a component library. A package already in the project is preferred over a second one that does the same thing. If one of these turns out to be needed, the reason goes in the decisions file.

### 2.17 Tests are plain too
No test base classes, no fixture frameworks, no builder pattern for test data, no mock for every interface. A test reads top to bottom: arrange with a few lines, act, assert. `FakeMemoryClient` is the model: a small hand-written fake for the one boundary that needs it. If a helper is needed by three tests, it is a static method in the test file, not a framework.

### 2.18 Plans are small too
A plan or spec is subject to the same rules as code. More than about five phases, or a task list longer than the problem statement, is a sign the plan has grown its own requirements. The planning mode exists for the owner to cut the plan, not only to approve it. A doc that nobody will read next month should not be written.

## 3. Things we do want

So the rules above do not read as "never improve anything":

- **Indexes and query shape** over caches, and measured caches over speculative ones.
- **Explicit state machines** for bot events (the `EventState` stack is a good start) over implicit booleans.
- **The game profile per flavor and the event registry** (`ShinyBotHunter/Games/`) as the two extension points.
- **Channels for producer/consumer** (already used for persistence) over hand-rolled queues.
- **Small records for request/response shapes** shared between controller and dashboard types, kept in sync by hand and checked by a test.

## 4. Enforcement

A document nobody loads enforces nothing. These rules reach an agent through three things:

1. **`AGENTS.md` at the repo root** holds the short form: the project in one paragraph, how to build and test, the rules an agent must apply on every change, and the workflow (plan in `docs/` first for anything non-trivial, wait for a go-ahead, run the local checks, state what was added). `CLAUDE.md` imports it, so Claude Code and any other agent read the same text. The full reasoning stays here.
2. **The PR description** states every new package, interface, project, config key or folder, and the reason. A reviewer who sees none listed can assume none were added.
3. **Correction in review.** An agent generalises from being told no far better than from a document. When a rule is broken, say which one; the next session will carry it.

## 5. How to change these rules

Edit this file in the PR that needs the exception, with a one-line reason in the section. If it is a real design change, record it in the relevant `docs/decisions/<area>.md`.

## Changelog
- 2026-10-17: first draft from the 2026-10 code review.
