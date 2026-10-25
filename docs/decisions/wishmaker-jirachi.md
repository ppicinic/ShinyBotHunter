# Decisions: Wishmaker Jirachi hunt

Decisions about hunting the Bonus Disc Jirachi through Dolphin. Project-wide decisions are in [global.md](global.md). Design: [spec](../specs/wishmaker-jirachi.md); evidence: [research](../research/wishmaker-jirachi.md).

---

## Mainline Dolphin, driven from outside; no fork

Status: Accepted
Date: 2026-10-25

### Context
Dolphin has no scripting hook. The bot needs to press buttons on the disc, know which screen the disc is on, and reboot the integrated GBA at one moment. The emulator fork pattern exists for BizHawk (ApiHawk), so a Dolphin fork with a command socket was the obvious shape.

### Options
1. Fork Dolphin and add a command socket (reset the GBA, read memory, inject input).
2. Mainline Dolphin: its DSU (cemuhook) client for input over UDP, its `dolphin-emu.<pid>` shared memory mapping for GameCube RAM, and Win32 messages to the GBA window for the reset.
3. Move the GBA side to standalone mGBA with its Lua scripting, linked to Dolphin over TCP.

### Decision
Option 2. The spike proved all three pieces work with no keyboard focus and with another application in the foreground, and ran a one-hour soak on them. A fork would be a second emulator build to maintain for one hunt; mGBA over TCP is the path Dolphin's own documentation calls unreliable.

### Consequences
- The GBA reset is the one GUI-shaped step: a posted context-menu message and posted key presses to the menu that appears. It is observed (the wait-for-power byte clears, the save file changes) and times out loudly. If a Dolphin update changes the menu, the fallback is UI Automation by the item's name.
- Disc state comes from four bytes found by snapshot diff, stable across seven boots of Dolphin 2609a. A Dolphin or disc revision change means re-running the capture, which the research doc describes.
- The bot owns a Dolphin user folder (`-u`), installed by `Scripts/deploy-dolphin.ps1` from `Dolphin/Config`, so the operator's Dolphin is untouched and the config is in the repo.

---

## The hunt varies the save by playing it, not by editing it

Status: Accepted
Date: 2026-10-25

### Context
The Jirachi is a pure function of the save: the disc seeds it from the first block's section-0 checksum, and a crafted play time yields a chosen shiny in one cycle. A "hunt" could therefore be a calculator.

### Decision
The event only plays the game and saves: a random stretch in the field, then one or two in-game saves, and the file the game wrote is what the disc gets. The bot never writes save bytes. Each cycle is a real, logged encounter with odds set by the seed band of that save (about 1 in 6500 per cycle for the spike's save). The QA shiny scenario is the one place a seed is crafted, in QA tooling, to prove the shiny path end to end.

### Consequences
- About 11 days of unattended cycles for a shiny, visible on the dashboard like any phase.
- Two saves per cycle, since the disc reads block A and the game alternates blocks; a cycle whose first block did not change would be a duplicate encounter, so the event saves again rather than running the disc.
- The operator's save is the hunt save, continued in place; Dolphin gets a copy; the result lives under the `-WISHMKR` suffix with its own ROM copy.

---

## Every cycle is checked against the save's prediction

Status: Accepted
Date: 2026-10-25

### Context
The pipeline moves one file between two emulators and a parser. A wrong copy, a half-written save or a stale read would still produce "a Jirachi".

### Decision
Before the disc runs, the event computes the PID the save predicts (first-block checksum through two LCRNG steps). After the disc, the parsed PID must equal it or the hunt stops and reports both values. The rule was verified on every one of the spike's cycles and on nine seed and PID pairs from the community tables.

### Consequences
- A silent pipeline fault cannot be logged as an encounter.
- The prediction is a 40-line pure function in `Gen3SaveFile` with unit tests on real saves; it is not a second RNG implementation in the bot.

---

## Two spike saves are unit-test fixtures in the repo

Status: Accepted
Date: 2026-10-25

### Context
FR-6's acceptance needs tests on the spike's real saves (seed `0A64` for the rev 0 base, `ECDD` for the crafted shiny save), while the global decision [QA cheats live in the QA project; QA saves live beside the repo](global.md#qa-cheats-live-in-the-qa-project-qa-saves-live-beside-the-repo) keeps saves out of the repo.

### Decision
`rs-rev0-base.SaveRAM` and `rs-seed-ecdd.sav` are committed, frozen, in `ShinyBotHunter.Tests/Saves/` and read through `RepoPaths.Root` (W2, #171). The global rule is about QA saves, which change as scenarios need; these two are test inputs that never change.

### Consequences
- The `Gen3SaveFile` tests stay hermetic and run on real bytes.
- QA saves are unchanged: they still live beside the repo.

---

## The result is shown at its summary screen and read from BizHawk's RAM before it is logged

Status: Accepted
Date: 2026-10-25 (owner, after the first soak)

### Context
W5 logged each cycle's Jirachi by parsing the save file Dolphin returned, and opened the result in BizHawk only on a shiny. The first recorded cycle showed the pipeline closing EmuHawk before anyone could see the Jirachi.

### Decision
Every cycle does what a human would: EmuHawk loads the `-WISHMKR` result, the game is continued, the Jirachi's summary screen is opened and held on view so the operator sees it and whether it is shiny, and only then is the party slot read from BizHawk's RAM, checked against the save's prediction and logged. The save file still predicts the result and keeps it.

### Consequences
- About 40 s more per cycle; NFR-1's budget rises from 240 s to 300 s.
- The party and summary screens have no mapped RS byte, so those presses are held presses with waits; the RAM read is the gate, and a slot that does not decode as Jirachi stops the hunt.
- On a shiny the EmuHawk already open at the summary is the one the event parks on (FR-18); no second launch.
- The "Every cycle is checked against the save's prediction" decision stands; the value compared now comes from RAM.
