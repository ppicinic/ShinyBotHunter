<!-- Derived from: ShinyBotHunter/Service/BotService.cs (StartWildEncounter, WildRefusalAsync, DisabledByCapabilities, SpeedLinesAtRun, StartLaunchBotAndContinueAsync), ShinyBotHunter/BotEvents/OrchestratorEvent.cs (CheckForCatchAsync, FinishAsync, LogEncounterAndAskTargetAsync), ShinyBotHunter/Service/PokemonStateReader.cs (GetPokemonState), ShinyBotHunter/BotEvents/SpinEncounterEvent.cs, ShinyBotHunter/BotEvents/RunEncounterEvent.cs, ShinyBotHunter/BotEvents/RsBattleEvent.cs, ShinyBotHunter/Games/Rs/RsProfile.cs, ShinyBotHunter/Games/GameStateWait.cs (GameStateTimeoutException), ShinyBotHunter/Service/RsStartMenuService.cs, ShinyBotHunter/Service/LegalityService.cs, ShinyBotHunter/Macro/Configs/RS/*.yaml, ShinyBotHunter/Games/EventRegistry.cs ("Wild Encounter", "Chained Encounter"), PokemonDashboard/src/renderer/src/components/admin/EmulatorCard.tsx (speed lines), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (choiceOptions), docs/research/rs-wild-encounter.md ("Tuned for speed", "Reaching 304 an hour", "A failed escape"), docs/specs/rs-wild-encounter.md -->
# Ruby and Sapphire wild encounters

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

The bot turns on the spot, or runs back and forth along a short line in the grass, until a wild battle starts. It presses A at "Wild … appeared!" and reads the wild Pokémon at the FIGHT / BAG / POKéMON / RUN menu, once it has slid in and, if it is shiny, sparkled. It writes the Pokémon to the encounter log and runs away. It stops when it finds a Pokémon you are hunting for, or when the milestone is complete.

It runs from every battle. Ruby and Sapphire have no training, healing, Pickup or chained hunts.

It is tested on version 1.2 of Ruby and Sapphire. Version 1.1 runs, untested. Version 1.0 cannot be used: launching it fails, and the backend's window shows `RS revision 0: no address table. Hunts run on revisions 1 and 2.`

Every button press is checked against the game's memory before the next one.

## What does your save need?

1. **An emulator started with Launch Bot + Continue**, as [the dashboard page](../03-dashboard.md) describes. Its entry appears once you are standing in the field.
2. **Your character standing in tall grass, on the tile you want to hunt from.** The bot remembers that tile when the hunt starts. Water and caves are untested.
3. **A first Pokémon that has not fainted.** One faster than the Pokémon living there gets away first time. A slower one may need several tries, and after 12 the bot stops.
4. **Poké Balls**, for you to catch with.
5. **For Encounter Type Run: the Running Shoes.** The bot holds B for the whole walk, so your character runs. Without the shoes it walks, which checks for an encounter half as often.
6. **Every menu and message closed.**
7. **A milestone for the game RS**, made in the [Milestone Editor](../03-dashboard.md#make-a-milestone).

## Start a hunt

1. On the dashboard, find the entry for the emulator you started.
   You see the actions that emulator can run.
2. Pick **Wild Encounter**.
   You see its settings.
3. Fill in the settings, as the next section explains.
4. Press **Run**.
   The entry shows the hunt as running.

**Chained Encounter** is not offered for Ruby and Sapphire.

## What do the settings mean?

| What it means | Field on the dashboard |
|---|---|
| The milestone the encounters are filed under, and the list of shiny Pokémon the hunt is trying to complete. | Milestone |
| What counts as found. **PhaseTarget**: a shiny Pokémon the milestone still needs, counting only the ones caught in this game file. **ShinyTarget**: any shiny Pokémon. Either way the hunt also ends once the milestone is complete. NoTarget is greyed out. | Target Type |
| How to meet wild Pokémon. **Spin**: turn on the spot. **Run**: run back and forth in a straight line. Headbutt is greyed out. | Encounter Type |
| How many tiles to run each way from the start tile, left and right (X Range) or up and down (Y Range). Shown only for Run. Fill in exactly one of the two, with a number above 0, and leave the other at 0. | X Range, Y Range |
| Leave it at **None**. Standard and Switch are greyed out, so Target Level, Battler and Pickup never show. | Train Type |

### Greyed-out choices

A choice Ruby and Sapphire cannot run is greyed out, with the reason in brackets after it, for example "Headbutt (Ruby and Sapphire have no Headbutt trees.)". The reasons are:

| Choice | Reason |
|---|---|
| Encounter Type Headbutt | `Ruby and Sapphire have no Headbutt trees.` |
| Target Type NoTarget | `No Target needs a train mode, which Ruby and Sapphire do not have yet.` |
| Train Type Standard and Switch | `Training is not available on Ruby and Sapphire yet.` |

## Spin and Run

- **Spin** turns Up, Left, Down, Right, starting with the direction after the one you face, and never leaves the tile. Before every round of turning, the bot checks you are on the tile the hunt started on.
- **Run** runs between the two ends of the line with B held. Before every run, the bot checks you are on the line and inside its two ends.
- Both check for an encounter every 8 frames (7.5 times a second), so neither is faster than the other.
- While your character is free to walk, there is no time limit.
- If the field stops being free (a message, a script) and no battle starts within 15 s, the bot stops.

## What happens in each encounter?

1. The bot presses A at "Wild … appeared!".
2. At the FIGHT / BAG / POKéMON / RUN menu it reads the wild Pokémon, checks it if it is shiny (step 3), writes it to the encounter log, and asks whether it is a Pokémon you are hunting for.
3. If the wild Pokémon is shiny, the bot first checks it with PKHeX, the same legality check the starter hunt uses. A shiny that no real game could produce fails. A GBA emulator can make one, which is why every Gen 3 shiny is checked except the Wishmaker Jirachi. A shiny that fails is written to the encounter log but not counted as a shiny, sends no alert, and is run from like any other Pokémon.
4. If it is a Pokémon you are hunting for, the bot stops at the menu with nothing pressed, and the shiny alert goes to [Discord](../05-discord.md). See [When it finds a target](#when-it-finds-a-target-catch-it-and-press-resume).
5. Otherwise it presses Right and Down to move to RUN, then A.
6. At "Can't escape!" it presses A, and A on RUN again when the menu comes back. The cursor stays on RUN.
7. It presses A at "Got away safely!", then turns or runs again once your character is free to walk.

A trainer battle, or any battle that is not wild, stops the bot.

## When it finds a target: catch it and press Resume

1. The entry shows the bot waiting, and the shiny alert arrives on Discord.
   The battle is on screen in the emulator, at the menu.
2. In the emulator, catch the Pokémon yourself.
3. Close every message (the Pokédex page, the nickname question) until your character stands in the field.
4. On the dashboard, press **Resume** on the entry.

The bot then looks for the Pokémon in your party and in your PC boxes. It reads them from the game's memory and opens nothing on screen, so a Pokémon sent to the PC because your party was full is found too. If it is there, the bot saves the game through the start menu: it opens the menu, moves to SAVE and confirms. It records the Pokémon as caught and writes the save file. Then it carries on hunting, or finishes where it stands if the milestone is complete.

- If the Pokémon is not in your party or your PC (you ran from it, or it fainted), nothing is recorded as caught, and the hunt carries on.
- If the bot could not read your party and boxes, it stays waiting. Press **Resume** again.

## How fast is it?

About 270 encounters an hour (13.3 s each), measured on Route 101 with a shiny first Pokémon, text speed Fast and no White Flute.

What makes it faster:

- **A first Pokémon that is not shiny.** About 1.2 s saved every battle, because the sparkle plays each time it is sent out.
- **A short nickname** on the first Pokémon. A little: 8 frames for a one-letter name.
- **A first Pokémon at a higher level than what lives there.** It gets the short battle transition. At or below the wild level it gets the long one.
- **The White Flute** (1.5 times the encounters) **or a first Pokémon with Illuminate** (twice).
- **Text speed Fast.**

Measured: a first Pokémon that is not shiny, nicknamed "A", made 298 an hour, and 311 with the White Flute.

What does not help: turning Battle Scene off (a battle you run from shows no move animation), and a first Pokémon with a short cry.

## The speed lines

While the hunt runs, the emulator's entry can show small grey lines under its name, each naming something in your save that slows the hunt. They are also written to the backend's window. The bot never refuses to start because of them.

| You see | When |
|---|---|
| `A shiny first Pokémon adds about 1.2 s to every battle.` | From **Run**, when your first Pokémon is shiny. |
| `A first Pokémon at or below the wild level gets the long battle transition.` | Added the first time a wild Pokémon's level is the same as or above your first Pokémon's level when you pressed **Run**. |

## What does it log?

Every wild Pokémon goes into the encounter log with the game, the milestone, the Pokémon, the time and the game file, as on HeartGold and SoulSilver. A shiny that fails the legality check is stored with `IsShiny` false. [Your data](../06-your-data.md) explains where these are kept and how to find them.

The backend's window also shows:

- `[RsBattle]` lines for each encounter: the species, level, personality value and whether it is shiny, each press and what confirmed it, failed escapes, and how many frames the battle took.
- `[RsBattle] legality OK` or `[RsBattle] legality FAIL` for each shiny, with PKHeX's report after a FAIL.
- `[Spin] seek:` or `[Run] seek:`: how long it took to find the battle.
- `[Run] from (<x>, <y>) macro=<macro>` before every run.

## How does it stop?

- **It finishes where it stands.** The milestone is complete. There is no walk home.
- **It waits for you.** It found a Pokémon you are hunting for, or it stopped with one of the sentences below.
- **You cancel it** from the dashboard, as [the dashboard page](../03-dashboard.md) describes.

## If it stops early

Text in `code` is quoted exactly as the bot shows it; a word in angle brackets, like `<x>`, stands for the value the bot fills in.

### Run refused

**You see** Run refused with "Close any menu or message and stand in the field."
**It means** a menu, message or script was on screen when you pressed Run.
**Do** close it until your character can walk, then press **Run** again.

**You see** Run refused with "The player is not the first object on this map, which the bot watches as it turns."
**It means** the bot checks that your character is the first object on the map, because Spin watches that object's facing. On this map it is not.

**You see** Run refused with "The party could not be read; try again once the game is in the field."
**It means** the bot could not read your party when you pressed Run.
**Do** wait until your character is in the field, then press **Run** again.

**You see** Run refused with "The first Pokémon in the party has fainted."
**It means** your first Pokémon has no HP left, or the party is empty.
**Do** heal it, or put a Pokémon that can battle first, then press **Run** again.

**You see** Run refused with "Ruby and Sapphire have no Headbutt trees.", "No Target needs a train mode, which Ruby and Sapphire do not have yet." or "Training is not available on Ruby and Sapphire yet."
**It means** a greyed-out choice was sent anyway, for example from outside the dashboard. Pickup sent as ticked gets "Training is not available on Ruby and Sapphire yet." too.
**Do** pick a choice that is not greyed out.

The X Range and Y Range refusals are the same as on HeartGold and SoulSilver; see [If it stops early](01-wild-encounter.md#if-it-stops-early) on that page.

### The bot stops and waits

Each of these appears in the backend's window, and the entry shows the bot waiting, with **Resume**.

**You see** `[Spin] The player moved while turning. At (<x>, <y>), started on (<x>, <y>).`
**It means** before a round of turning, your character was not on the tile the hunt started on.

**You see** `[Run] The player is off the run line. At (<x>, <y>); the line is <x or y> +-<range> from (<x>, <y>).`
**It means** before a run, your character was not on the line, or was past one of its ends.

**You see** `[Spin] Something other than a wild battle started. Waited for a battle within 15 s of the field leaving free; the game state stayed <state>`, or the same starting with `[Run]`.
**It means** something stopped your character walking, such as a message or a script, and no battle started within 15 s.

**You see** `[RsBattle] stopped: Something other than a wild battle started.` followed by what the game showed, or by `battle type 0x<flags>, <state>`.
**It means** after the field locked, no battle showed and nothing in the game changed for 15 s; or, with `battle type`, the battle is not a wild one (a trainer battle, for example).

**You see** `[RsBattle] stopped: The first Pokémon fainted. <state>`
**It means** your first Pokémon fainted in the battle.

**You see** `[RsBattle] stopped: Could not run from this battle. <state>`
**It means** the bot pressed RUN 12 times in this battle without getting away. A wild Pokémon with Arena Trap, Shadow Tag or Magnet Pull can do this.

**You see** `[RsBattle] stopped: The wild Pokémon's record failed its checksum for 2 s. <state>`
**It means** the wild Pokémon's record in the game's memory kept failing its checksum for 2 s, so the bot could not read it.

**You see** `[RsBattle] stopped: Waited for <what>; the game state stayed <state>`
**It means** in the battle, nothing in the game changed for 30 s while the bot waited for the next screen.

**Do**, for each of these: look at that emulator to see what the game shows. Finish it yourself: run from or end the battle, close the message. Put your character back on the start tile (Spin) or on the line (Run). Then press **Resume**. Resume goes back to the field and checks the tile again.

The bot records a catch only for a Pokémon it stopped on as a target: waiting at the FIGHT / BAG / POKéMON / RUN menu, with the shiny alert sent. A Pokémon you catch after any of the stops above is not recorded by the bot.

For anything else, see [When something goes wrong](../07-when-something-goes-wrong.md).

## Why does it work this way?

What the hunt was built to do is in the [Ruby and Sapphire wild encounter spec](../../specs/rs-wild-encounter.md). How the bot reads the game, and where the speed numbers come from, is in the [research notes](../../research/rs-wild-encounter.md).

Next: [Discord](../05-discord.md)
