<!-- Derived from: ShinyBotHunter/Service/BotService.cs (StartWildEncounter, WildRefusalAsync, DisabledByCapabilities, SpeedLinesAtRun, StartLaunchBotAndContinueAsync), ShinyBotHunter/BotEvents/OrchestratorEvent.cs (ResumeAsync, CheckForCatchAsync, FinishAsync, LogEncounterAndAskTargetAsync), ShinyBotHunter/Service/PokemonStateReader.cs (GetPokemonState), ShinyBotHunter/BotEvents/SpinEncounterEvent.cs, ShinyBotHunter/BotEvents/RunEncounterEvent.cs, ShinyBotHunter/BotEvents/RsBattleEvent.cs (training, the switch, the level-up box, the new move, evolution, the faint, TrainerSentence), ShinyBotHunter/BotEvents/RsNavigatorEvent.cs (the walk, the nurse, every stop), ShinyBotHunter/BotEvents/OrchestratorEvent.cs (DecideOverworldAction, CanFightNextEncounter, HealTripLine), ShinyBotHunter/BotEvents/BattleReadiness.cs, ShinyBotHunter/Pathfinding/RouteSearch.cs, ShinyBotHunter/Service/DiscordBotService.cs (SendNewMoveDecisionAndWaitAsync), ShinyBotHunter/Games/Rs/RsProfile.cs (capabilities, seekLockHandOff, trainingRefusal), ShinyBotHunter/Games/GameStateWait.cs (GameStateTimeoutException), ShinyBotHunter/Service/RsStartMenuService.cs, ShinyBotHunter/Service/LegalityService.cs, ShinyBotHunter/Macro/Configs/RS/*.yaml, ShinyBotHunter/Games/EventRegistry.cs ("Wild Encounter", "Chained Encounter"), PokemonDashboard/src/renderer/src/components/admin/EmulatorCard.tsx (speed lines), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (choiceOptions), docs/research/rs-wild-encounter.md ("Tuned for speed", "Reaching 304 an hour", "A failed escape"), docs/specs/rs-wild-encounter.md, docs/specs/rs-navigation-and-training.md, ShinyBotHunter/Milestones/MilestonePicker.cs, PokemonDashboard/src/renderer/src/components/admin/ActionParamFields.tsx (the milestone picker), ShinyBotHunter/Milestones/Goal.cs (GameFile.AnchorFor), OrchestratorEvent.cs (UseAnchor), ShinyBotHunter/Service/BotService.cs (AnchorTileStop, AnchorLeg, SetAnchorAsync), ShinyBotHunter/Games/Rs/RsProfile.cs (landTileKind), ShinyBotHunter/BotEvents/RsNavigatorEvent.cs (No route back to the start tile), docs/specs/hunt-plans-and-milestone-builder.md (24 of 46 RS land anchors, Route 111), PokemonDashboard/src/renderer/src/components/admin/EmulatorCard.tsx (the anchor line, Set anchor here, Clear) -->
# Ruby and Sapphire wild encounters

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

The bot turns on the spot, or runs back and forth along a short line in the grass, until a wild battle starts. It presses A at "Wild … appeared!" and reads the wild Pokémon at the FIGHT / BAG / POKéMON / RUN menu, once it has slid in and, if it is shiny, sparkled. It writes the Pokémon to the encounter log and runs away. It stops when it finds a Pokémon you are hunting for, or when the milestone is complete.

With Train Type None it runs from every battle. With Train Type Standard or Switch it trains a Pokémon instead: it fights, and when the Pokémon is too hurt or out of PP it walks to the nearest Pokémon Center, heals and walks back. [Training](#training) explains it. Ruby and Sapphire have no Pickup, Headbutt or chained hunts.

It is tested on version 1.2 of Ruby and Sapphire. Version 1.1 runs, untested. Version 1.0 cannot be used: launching it fails, and the backend's window shows `RS revision 0: no address table. Hunts run on revisions 1 and 2.`

Every button press is checked against the game's memory before the next one.

## What does your save need?

1. **An emulator started with Launch Bot + Continue**, as [the dashboard page](../03-dashboard.md) describes. Its entry appears once you are standing in the field.
2. **Your character standing in tall grass, on the tile you want to hunt from,** unless the milestone has an anchor: then the bot walks to the anchor first (see [Where the hunt starts: the anchor](#where-the-hunt-starts-the-anchor)). The bot remembers that tile when the hunt starts. Water and caves are untested.
3. **A first Pokémon that has not fainted.** One faster than the Pokémon living there gets away first time. A slower one may need several tries, and after 12 the bot stops.
4. **Poké Balls**, for you to catch with.
5. **For Encounter Type Run: the Running Shoes.** The bot holds B for the whole walk, so your character runs. Without the shoes it walks, which checks for an encounter half as often.
6. **Every menu and message closed.**
7. **A milestone of the game file**: one you made for the game RS in the [Milestone Editor](../03-dashboard.md#make-a-milestone), or a generated one such as `Route 101 · grass`, which needs a goal for the game file.
8. **To train (Train Type Standard or Switch):** a US Ruby or Sapphire ROM, and a Pokémon Center your character can walk to from the start tile without any HM. The bot uses no Cut, Surf, Strength, Rock Smash, Waterfall or Dive. Every trainer on the way must already be beaten: the bot stops at any trainer who has not been. The fighting Pokémon needs more than a quarter of its HP and at least 3 PP across its damaging moves; otherwise the bot heals first.

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
| The milestone the encounters are filed under, and the list of shiny Pokémon the hunt is trying to complete. Generated milestones are listed beside the ones you made; surf, rod and rock smash milestones read `(no hunt yet)` and Run is refused at them. A milestone with an anchor is hunted from it; see [Where the hunt starts: the anchor](#where-the-hunt-starts-the-anchor). See [the Milestone field](../03-dashboard.md#the-milestone-field). | Milestone |
| What counts as found. **PhaseTarget**: a shiny Pokémon the milestone still needs, counting only the ones caught in this game file. **ShinyTarget**: any shiny Pokémon. Either way the hunt also ends once the milestone is complete. **NoTarget**: nothing is a target; it needs a train mode, and the hunt ends when the Pokémon reaches the target level. | Target Type |
| How to meet wild Pokémon. **Spin**: turn on the spot. **Run**: run back and forth in a straight line. Headbutt is greyed out. | Encounter Type |
| How many tiles to run each way from the start tile, left and right (X Range) or up and down (Y Range). Shown only for Run. Fill in exactly one of the two, with a number above 0, and leave the other at 0. At a milestone with an anchor, the anchor decides Run or Spin and the range instead, but these must still be filled in correctly. | X Range, Y Range |
| Whether to fight. **None**: run from every battle. **Standard**: the Pokémon in your first party slot fights every battle. **Switch**: the Pokémon in your first slot is sent out, then the bot switches to the Battler, which does the fighting; the first Pokémon still earns experience. Standard and Switch need a US Ruby or Sapphire ROM. Pickup shows with them, greyed out. | Train Type |
| The level to train to. Shown when Train Type is not None. A whole number above 0. Training is done when the Pokémon in your first party slot reaches it. 101 is never reached, so the bot keeps fighting until you cancel it. | Target Level (101 = no end) |
| The party slot of the Pokémon that fights in Switch training, from 2 to 6. Shown only for Switch. It starts at 2. | Battler |

### Greyed-out choices

A choice Ruby and Sapphire cannot run is greyed out, with the reason in brackets after it, for example "Headbutt (Ruby and Sapphire have no Headbutt trees.)". The reasons are:

| Choice | Reason |
|---|---|
| Encounter Type Headbutt | `Ruby and Sapphire have no Headbutt trees.` |
| Pickup, once a train type is chosen | `Pickup is not available on Ruby and Sapphire.` |

## Where the hunt starts: the anchor

It works as on HeartGold and SoulSilver: the card shows the picked milestone's anchor under the Wild Encounter settings, **Set anchor here** and **Clear** change it for this game file, and the anchor decides Run or Spin and the range. [Where the hunt starts: the anchor](01-wild-encounter.md#where-the-hunt-starts-the-anchor) on that page explains the card and the buttons. With no anchor, the hunt starts where your character stands.

What is different on Ruby and Sapphire:

- **The walk to the anchor** is the walk to a Pokémon Center (see [When it heals](#when-it-heals)): planned over Hoenn's maps from where your character stands, run with B held, with no HM, running from every wild battle on the way. A trainer who has not been beaten stops it, as on a heal trip. It needs no train type.
- **24 of the 46 grass and cave milestones** come with a generated anchor. The others have none until you set one.
- **Route 111's generated anchor is in the desert,** which needs the Go-Goggles. What the walk does there, with and without them, has not been checked on the emulator yet.
- **Run checks the anchor's tile** against the game's map before any walk. Every grass and cave milestone needs a tile that starts wild battles, so the sentence for a cave milestone ends `is not cave.`

## Spin and Run

- **Spin** turns Up, Left, Down, Right, starting with the direction after the one you face, and never leaves the tile. Before every round of turning, the bot checks you are on the tile the hunt started on.
- **Run** runs between the two ends of the line with B held. Before every run, the bot checks you are on the line and inside its two ends.
- Both check for an encounter every 8 frames (7.5 times a second), so neither is faster than the other.
- While your character is free to walk, there is no time limit.
- If the field stops being free (a message, a script, a trainer), the bot stops turning or running after 4 s and looks at what locked it. A trainer stops the bot at once (see [When a trainer stops you](#when-a-trainer-stops-you-win-the-battle-and-press-resume)). Anything else that does not become a battle stops the bot about 15 s after the field locked.

On a Run hunt with a US Ruby or Sapphire ROM, the bot closes "REPEL's effect wore off..." itself, about 4 s after it appears: once the text has finished and the box waits, it presses A once, waits for your character to be free to walk, and runs on. Nothing else is pressed while the box is up. On other ROMs the message stops the bot as other messages do. Spin takes no steps, so a Repel never wears off on it.

## What happens in each encounter?

1. The bot presses A at "Wild … appeared!".
2. At the FIGHT / BAG / POKéMON / RUN menu it reads the wild Pokémon, checks it if it is shiny (step 3), writes it to the encounter log, and asks whether it is a Pokémon you are hunting for.
3. If the wild Pokémon is shiny, the bot first checks it with PKHeX, the same legality check the starter hunt uses. A shiny that no real game could produce fails. A GBA emulator can make one, which is why every Gen 3 shiny is checked except the Wishmaker Jirachi. A shiny that fails is written to the encounter log but not counted as a shiny, sends no alert, and is run from like any other Pokémon.
4. If it is a Pokémon you are hunting for, the bot stops at the menu with nothing pressed, and the shiny alert goes to [Discord](../05-discord.md). See [When it finds a target](#when-it-finds-a-target-catch-it-and-press-resume).
5. Otherwise it presses Right and Down to move to RUN, then A.
6. At "Can't escape!" it presses A, and A on RUN again when the menu comes back. The cursor stays on RUN.
7. It presses A at "Got away safely!", then turns or runs again once your character is free to walk.

With Train Type Standard or Switch, step 5 is a fight instead; see [Training](#training).

A trainer stops the bot before its battle (see below). A Safari Zone battle, or any other battle that is not wild, stops the bot too.

## Training

### Standard and Switch

- **Standard**: the Pokémon in your first party slot fights every wild Pokémon, with the move the bot's damage calculator picks.
- **Switch**: your first Pokémon is sent out, and at the battle's first FIGHT / BAG / POKéMON / RUN menu the bot switches to the Battler: it picks POKéMON, moves to the Battler's slot, picks it and picks SHIFT. The Battler then fights. Your first Pokémon still earns experience for the battle. If the Battler has fainted, or the switch has not worked three times in the battle, the bot runs from that battle.
- **Target Level**: training is done when the Pokémon in your first slot reaches it. Then, with Target Type NoTarget, or once the milestone is complete, the bot walks back to the start tile and the hunt finishes. With PhaseTarget or ShinyTarget and the milestone not complete, it carries on hunting and runs from every battle.

In a battle, the bot runs instead of fighting when the Pokémon fighting is down to a quarter of its HP or less, has no PP left on damaging moves, or the calculator finds no move.

Every press is checked against the game's memory. For the presses that change something the bot can read (FIGHT, the move, a level-up box, the answers to a new move, the forget-a-move screen, and the switch's presses), if the game still shows exactly what it showed before the press a second later, the bot sends that one press once more, never a third time. So such a line in the backend's window can end in `dropped, sent again`. The A at a message and the presses at the nurse are never sent twice.

### Level-ups, new moves and evolution

- **A level-up** shows its box in two pages (the gains, then the totals); the bot presses A at each. A battle that gains several levels shows one box per level, and when two Pokémon level up from one battle (in Switch, your first Pokémon and the Battler), each gets its own boxes.
- **A new move with a free move slot** is learned with the game's own text, and the bot presses A through it.
- **A new move when the Pokémon already knows four** is a question on [Discord](../05-discord.md#how-do-i-answer-a-new-move-question): which move to forget, or Do Not Learn. The game waits at "Delete a move?" and the bot presses nothing until you answer. There is no time limit; **Cancel** on the dashboard also ends the wait. Then the bot answers the game for you: Yes, and the move you chose on the forget-a-move screen; or No, and Yes at "Stop learning?". Without Discord, the bot answers Do Not Learn on its own.
- **Evolution** is allowed to happen. While the Pokémon evolves the bot presses nothing; it never presses B there, which would stop the evolution. It presses A at the evolution's texts. A new move at the evolution is the same question as above.

### When it heals

Before each encounter the bot checks the Pokémon that fights: your first Pokémon in Standard, the Battler in Switch. If it has a quarter of its HP or less, or fewer than 3 PP across its damaging moves, the bot goes to heal. In Switch it also goes when your first Pokémon has fainted or the Battler's slot is empty. The backend's window shows `[Orchestrator] heal trip: <name> HP <hp>/<max>, damaging PP <pp>`, or `[Orchestrator] heal trip: switch slot <n> is empty`, where `<n>` is the Battler slot as the dashboard numbers it, from 2 to 6.

1. **The walk there.** The bot plans a route to the nearest Pokémon Center from where your character stands, over Hoenn's maps, through doors and across the edges between maps, and runs it with B held. It reads your save's story progress, so it does not plan through a person who still blocks a path, and it keeps off tiles that would start a story event when a way round is not much longer. It runs from any wild battle on the way and plans again from where your character stands. Those Pokémon are still written to the encounter log, and a Pokémon you are hunting for still stops the bot.
2. **The nurse.** On the tile in front of the counter the bot turns to face her if it has to, and talks to her. It presses A at each of her lines, Yes at her question, and A at her last line. Then it checks that every Pokémon in the party has full HP.
3. **The walk back** to the tile the hunt started on, planned the same way. Then the hunt carries on.

### When a trainer is on the way

A trainer who has not been beaten stops the bot on the walk, as in the hunt: see [When a trainer stops you](#when-a-trainer-stops-you-win-the-battle-and-press-resume). Once you have won and pressed **Resume**, the bot plans the walk again from where your character stands.

### When a Pokémon faints

- If the Pokémon fighting faints and no other Pokémon in your party can battle, the battle is lost. The bot stops with `[RsBattle] stopped: The first Pokémon fainted. <state>`, and the game sends your character to a Pokémon Center, as it does after any lost battle.
- If another Pokémon can battle, the game asks "Use next POKéMON?". While training (Standard or Switch), the bot answers No, which tries to run:
  1. If it gets away, the battle ends and the bot goes to heal.
  2. If it does not, the game shows "Can't escape!" and opens your party. The bot sends out the fastest Pokémon that can still battle (the highest Speed; never an Egg; on a tie, the one nearer the top of your party) and picks RUN once. If that gets away, the bot goes to heal. The backend's window shows the party as the menu lists it, `[RsBattle] the party menu after a faint, by row: row <n> slot <n> <name> HP <hp> Speed <speed>; …`, where the row is the menu's line and the slot your party's, both from 0. If that read shows none able to battle, the bot stops with `[RsBattle] stopped: A Pokémon fainted, and the party menu read showed none able to battle.` and the rows it read, presses nothing, and sends that sentence to [Discord](../05-discord.md) once; **Resume** then works as after the Run failing (below).
  3. If that Run fails too, the bot stops with `[RsBattle] stopped: A Pokémon fainted, and running from the battle failed after the fastest Pokémon left was sent out. runTries <before> -> <after>, <state>` and presses nothing more. The same sentence goes to [Discord](../05-discord.md) once, as for a trainer stop. Finish the battle yourself, and once your character stands in the field press **Resume**: the bot carries on from there, and goes to heal first if a Pokémon needs it. If you press **Resume** before the battle is over, the bot waits for the field as after a trainer stop.
- In a hunt that is not training, the bot does not answer "Use next POKéMON?": about 30 s later it stops with `[RsBattle] stopped: Waited for a battle message, the action menu, an evolution text wait or yes/no, Use next POKéMON?, the party menu after a faint, or the free field; the game state stayed <state>`.

After the lost battle, or the 30 s stop: finish the battle yourself, heal, walk your character back to the start tile, and press **Resume**. In Switch, a Battler that has already fainted when the battle starts is not a stop: the bot runs from that battle and then goes to heal.

### What is still off

- Pickup, Chained Encounter and Headbutt are not offered for Ruby and Sapphire.
- No HM is used, so a Pokémon Center that needs one is out of reach: the bot stops with `No route to a Pokémon Center on foot from <place>.`
- Training on a ROM that is not the US Ruby or Sapphire is refused at **Run** with `Training needs a US Ruby or Sapphire ROM (game code AXVE or AXPE).`

## When a trainer stops you: win the battle and press Resume

A trainer who sees your character, or whom you talk to, starts a speech, and the bot stops before anything is pressed: it does not fight trainers.

1. The entry shows the bot waiting, and the backend's window shows `[RsBattle] stopped: A trainer battle started on <map>.` with the trainer's number and what the game showed. The same sentence goes to [Discord](../05-discord.md), once, in the shiny-alert channel with the role mention. The map is the game's own name for it, for example `Route102`.
2. In the emulator, press through the trainer's speech and fight the battle yourself. The speech may not be up yet when the bot stops; it comes up on its own.
3. Once you have won and your character stands in the field, press **Resume** on the entry.

There is nothing to catch, so Resume checks nothing in your party. It waits until your character is free to walk, then carries on hunting.

- If you press **Resume** before the battle is over, the bot waits while the game changes and carries on when your character is free. If the game shows the same thing for 10 s (the speech waiting for you, a battle menu), the bot goes back to waiting and the backend's window shows `[Orchestrator] Resume: Waited for the free field after the trainer stop; the game state stayed <state>`. Nothing is pressed and no second Discord message is sent. Finish the speech or the battle, and press **Resume** again.
- If you lose the battle, the game may treat later messages as that trainer again, and the bot can stop with the trainer sentence when no trainer is there. Close the message and press **Resume**. This has not been seen on the emulator.

## When it finds a target: catch it and press Resume

1. The entry shows the bot waiting, and the shiny alert arrives on Discord.
   The battle is on screen in the emulator, at the menu.
2. In the emulator, catch the Pokémon yourself.
3. Close every message (the Pokédex page, the nickname question) until your character stands in the field.
4. On the dashboard, press **Resume** on the entry.

The bot then looks for the Pokémon in your party and in your PC boxes. It reads them from the game's memory and opens nothing on screen, so a Pokémon sent to the PC because your party was full is found too. If it is there, the bot saves the game through the start menu: it opens the menu, moves to SAVE and confirms. It records the Pokémon as caught and writes the save file. Then it carries on hunting, or, if the milestone is complete, walks back to its start tile and finishes.

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

While the hunt runs, the bot writes a line to the backend's window for each thing in your save that slows the hunt. The dashboard does not show them. The bot never refuses to start because of them.

| The backend's window shows | When |
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
- `[Run] REPEL's effect wore off: closed with <n> A, confirmed by lock 0 (<script state>)` each time the bot closes the Repel message on a Run hunt, and `[Run] lock handed over: not the Repel message (<script state>)` when a lock that lasted 4 s was something else.
- When training: `[RsBattle]` lines for each move chosen, each level-up box, each new move (`asking Discord: …; nothing is pressed until the answer`), each switch, and `party after the battle` with each Pokémon's level and moves. Party slots and move slots in these lines count from 0: your first Pokémon is slot 0, the dashboard's Battler 2 is slot 1, and a Pokémon's first move is move 0.
- `[Orchestrator] heal trip: …` when the bot goes to heal. Its `switch slot <n>` is the dashboard's number, from 2 to 6.
- `[Navigator]` lines for each walk: `plan to <place> from <place>` with its cost and number of segments, one line per segment, `arrived on <place>`, and at the Pokémon Center the nurse's presses and `nurse: party HP … (full)`.

## How does it stop?

- **It finishes on its start tile.** The milestone is complete, or training reached Target Level with Target Type NoTarget. Before it finishes, the bot walks back to the tile the hunt started on, with or without a train type: after Spin it is already there, after Run it walks there the way it walks to a Pokémon Center. That walk can stop with the `[Navigator]` sentences below.
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

**You see** Run refused with "Ruby and Sapphire have no Headbutt trees." or "Pickup is not available on Ruby and Sapphire."
**It means** a greyed-out choice was sent anyway, for example from outside the dashboard.
**Do** pick a choice that is not greyed out.

**You see** Run refused with "<milestone>: the anchor (<x>, <y>) is not grass." or the same ending `is not cave.`
**It means** the milestone's anchor is not a tile that starts wild battles on the game's map. It is checked when you press Run, before any walk. Usually it is an anchor you set while standing off the grass. Milestones you made are not checked.
**Do** stand your character in the milestone's grass and press **Set anchor here** again, or press **Clear** to go back to the generated anchor.

**You see** Run refused with "Training needs a US Ruby or Sapphire ROM (game code AXVE or AXPE)."
**It means** Train Type Standard or Switch was chosen on a ROM that is not the US Ruby or Sapphire. The bot only knows where those two keep what training reads.
**Do** leave Train Type at None, or train on a US ROM.

The X Range, Y Range, Target Level and Battler refusals are the same as on HeartGold and SoulSilver; see [If it stops early](01-wild-encounter.md#if-it-stops-early) on that page.

### The bot stops and waits

Each of these appears in the backend's window, and the entry shows the bot waiting, with **Resume**.

**You see** `[Spin] The player moved while turning. At (<x>, <y>), started on (<x>, <y>).`
**It means** before a round of turning, your character was not on the tile the hunt started on.

**You see** `[Run] The player is off the run line. At (<x>, <y>); the line is <x or y> +-<range> from (<x>, <y>).`
**It means** before a run, your character was not on the line, or was past one of its ends.

**You see** `[Run] The Repel message did not close. Waited for <what>; the script state stayed <state>`, or the same ending `the game state stayed <state>`.
**It means** on a Run hunt, "REPEL's effect wore off..." came up, and pressing A did not close it, or the box never reached the point where it waits for A, or your character was not free to walk afterwards. `<what>` is `the Repel message to wait for A`, `the Repel message to take the A` or `the field free after the Repel message`. You can close the box yourself, or simply press **Resume**: the B that Run holds closes it.

**You see** `[Spin] Something other than a wild battle started. Waited for a battle within 15 s of the field leaving free; the game state stayed <state>`, or the same starting with `[Run]`.
**It means** the game left the field for something other than a message or a script, such as a loading screen, and no battle started within 15 s.

**You see** `[RsBattle] stopped: A trainer battle started on <map>.` followed by `trainer <number>` or `battle type 0x<flags>`, and what the game showed.
**It means** a trainer challenged your character. See [When a trainer stops you](#when-a-trainer-stops-you-win-the-battle-and-press-resume).

**You see** `[RsBattle] stopped: Something other than a wild battle started.` followed by what the game showed, or by `battle type 0x<flags>, <state>`.
**It means** the field locked (a message, a script), it was not a trainer, and no battle started within about 15 s of the lock; or, with `battle type`, the battle is not a wild one (a Safari Zone battle, for example). On a Run hunt with a US ROM, the Repel message is not one of these: the bot closes it.

**You see** `[RsBattle] stopped: The first Pokémon fainted. <state>`
**It means** your first Pokémon fainted in the battle.

**You see** `[RsBattle] stopped: A Pokémon fainted, and running from the battle failed after the fastest Pokémon left was sent out.` followed by `runTries <before> -> <after>` and what the game showed.
**It means** while training, the Pokémon fighting fainted, and neither No at "Use next POKéMON?" nor RUN with the fastest Pokémon left got away. See [When a Pokémon faints](#when-a-pokémon-faints). If the line also says `(did not rise)`, the game did not count that Run as a try; the stop is the same.

**You see** `[RsBattle] stopped: Could not run from this battle. <state>`
**It means** the bot pressed RUN 12 times in this battle without getting away. A wild Pokémon with Arena Trap, Shadow Tag or Magnet Pull can do this.

**You see** `[RsBattle] stopped: The wild Pokémon's record failed its checksum for 2 s. <state>`
**It means** the wild Pokémon's record in the game's memory kept failing its checksum for 2 s, so the bot could not read it.

**You see** `[RsBattle] stopped: Waited for <what>; the game state stayed <state>`
**It means** in the battle, nothing in the game changed for 30 s while the bot waited for the next screen.

**You see** `[RsBattle] stopped: Waited for <what>; the game state stayed <state>` while training, where `<what>` names the press or the screen, for example `the move list after BattleConfirm on FIGHT`.
**It means** a press while fighting, switching or answering a new move was not followed by what it should change, and nothing in the game changed for 30 s. In a hunt that is not training, "Use next POKéMON?" ends this way (see [When a Pokémon faints](#when-a-pokémon-faints)). [When something goes wrong](../07-when-something-goes-wrong.md#a-ruby-or-sapphire-hunt-stops-while-training-or-walking) lists every `<what>`.

**You see** `[Navigator] stopped:` followed by `No route to a Pokémon Center on foot from <place>.`, `No route back to the start tile <place> from <place>.`, `Stuck walking to <place>.`, `The last step to <place> did not end (…).`, a `Waited for …` at the nurse, or `The party's HP was not full after the nurse: …`.
**It means** the walk to the Pokémon Center or back, the walk to the milestone's anchor at the start of the hunt, or the nurse, did not go as planned. On the walk to the anchor the start tile is the anchor, so a hunt that cannot reach its anchor stops with `No route back to the start tile <place> from <place>.` before anything is hunted; cancel it, walk your character nearer the anchor, and press **Run** again. A place is the game's name for the map and the tile, for example `Route101 (14, 4)`. [When something goes wrong](../07-when-something-goes-wrong.md#a-ruby-or-sapphire-hunt-stops-while-training-or-walking) explains each.

**Do**, for each of these: look at that emulator to see what the game shows. Finish it yourself: run from or end the battle, close the message. Put your character back on the start tile (Spin) or on the line (Run). Then press **Resume**. Resume goes back to the field and checks the tile again. After a lost battle, or a stop at the nurse or on a walk, your character is somewhere else, so walk it back to the start tile first; otherwise Resume stops again with `The player moved while turning` or `The player is off the run line`. If a Pokémon still needs healing, the bot then walks to the Pokémon Center again by itself.

The bot records a catch only for a Pokémon it stopped on as a target: waiting at the FIGHT / BAG / POKéMON / RUN menu, with the shiny alert sent. A Pokémon you catch after any of the stops above is not recorded by the bot.

For anything else, see [When something goes wrong](../07-when-something-goes-wrong.md).

## Why does it work this way?

What the hunt was built to do is in the [Ruby and Sapphire wild encounter spec](../../specs/rs-wild-encounter.md), and training and the walks in the [navigation and training spec](../../specs/rs-navigation-and-training.md). How the bot reads the game, and where the speed numbers come from, is in the research notes for [the wild hunt](../../research/rs-wild-encounter.md) and for [navigation and training](../../research/rs-navigation-and-training.md).

Next: [Discord](../05-discord.md)
