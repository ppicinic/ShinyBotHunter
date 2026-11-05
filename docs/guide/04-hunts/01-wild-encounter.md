<!-- Derived from: ShinyBotHunter/BotEvents/OrchestratorEvent.cs, ShinyBotHunter/BotEvents/BattleOrchestratorEvent.cs, docs/specs/hgss-state-reader.md, ShinyBotHunter/BotEvents/ChainedOrchestratorEvent.cs, ShinyBotHunter/BotEvents/LevelUpFlow.cs, ShinyBotHunter/Games/EventRegistry.cs ("Wild Encounter", "Chained Encounter", "Resume"), ShinyBotHunter/Service/BotService.cs (ValidateAndBuildLeg, StartWildEncounter, StartChainedEncounter, ResumeAsync, GetAvailableActions), ShinyBotHunter/Service/BotDtos.cs (TargetType), ShinyBotHunter/BotEvents/GameState.cs (TrainType), ShinyBotHunter/BotEvents/Factories/EncounterParams.cs, ShinyBotHunter/BotEvents/Target/PhaseTarget.cs, ShinyBotHunter/BotEvents/Target/ShinyTarget.cs, ShinyBotHunter/BotEvents/Target/NoTarget.cs, ShinyBotHunter/BotEvents/RunEncounterEvent.cs, ShinyBotHunter/BotEvents/SpinEncounterEvent.cs, ShinyBotHunter/BotEvents/HeadbuttEncounterEvent.cs, ShinyBotHunter/BotEvents/NavigatorEvent.cs, ShinyBotHunter/BotEvents/BattleReadiness.cs, PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (field labels), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (encounter type choices), docs/specs/headbutt-encounter.md (FR-3 to FR-9), PokemonDashboard/src/renderer/src/components/admin/EmulatorCard.tsx (Run and + Leg buttons), ApiHawk/CommandServer.cs (the M action), ShinyBotHunter/Pathfinding/PathfindingService.cs (capabilities, closest Pokemon Center node), ShinyBotHunter/Data/HGSS/graph.bin and mapreverselookup.json, OrchestratorEvent.cs (CheckForCatchAsync) -->
# Wild and chained encounters

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

The bot walks back and forth, turns on the spot, or headbutts a tree, until a wild battle starts. It reads the wild Pokémon, writes it to the encounter log, and then either runs away or fights. It stops when it finds a Pokémon you are hunting for, or when it has reached the goal you set.

It works in HeartGold and SoulSilver only.

There are two actions for it on the dashboard:

- **Wild Encounter** runs one hunt.
- **Chained Encounter** runs several hunts one after another, each with its own settings, with no need for you between them. Each hunt in the chain is called a leg.

## What does your save need?

1. **An emulator that is already running with your save loaded.** These actions do not start an emulator. Start one first with **Launch Bot + Continue**, as [the dashboard page](../03-dashboard.md) describes.
2. **Your character standing on the tile you want to hunt from.** The bot remembers that tile as home when the hunt starts. It walks back there after every trip to heal, and again before the hunt finishes.
3. **For walking back and forth (Encounter Type Run):** room to walk in a straight line from that tile, the same number of tiles each way. The bot paces left and right, or up and down, centred on where you stand.
4. **For turning on the spot (Encounter Type Spin):** nothing extra. The bot turns through the four directions without leaving the tile.
5. **For headbutting a tree (Encounter Type Headbutt):** stand on foot facing a headbutt tree, not on a bike or surfing, and have a Pokémon in your party that knows Headbutt. The bot does not choose a tree or walk to one; every headbutt checks that there is a tree in front of you.
   If no Pokémon in your party knows Headbutt, the Headbutt choice is greyed out with "No Pokémon in your party knows Headbutt." after it. If it shows "The party could not be read; try again once the game is in the field." instead, get the game into the field, then start and cancel any hunt, or press the reload button beside Battler (shown when Train Type is Switch). Either makes the dashboard read your party again.
6. **If the bot will fight (any Train Type other than None):** the Pokémon that does the fighting needs damaging moves with at least 3 PP between them, and more than a quarter of its HP. That is the Pokémon in your first party slot, or in Switch training the Pokémon in the Battler slot.

The bot checks nothing about the tile or the line. It holds the direction until your character reaches the end of the range, then turns back, and keeps doing that until a battle starts, with no time limit. So stand where walking starts wild battles, on a line nothing blocks. If something blocks it, your character stays pressed against it and the bot waits for a battle that does not come.

The bot looks up the nearest Pokémon Center on its own map of HeartGold and SoulSilver, which covers the whole game. It walks only where no field move is needed: no Cut, Surf, Rock Smash, Waterfall, Whirlpool, Strength or Rock Climb.

If the bot cannot find a way from where it is to a Pokémon Center, it finishes the hunt where it stands instead of healing.

## Start a wild encounter hunt

1. On the dashboard, find the card for the emulator you started.
   You see the actions that emulator can run.
2. Pick **Wild Encounter**.
   You see its settings.
3. Fill in the settings, as the next section explains.
4. Press **Run**.
   The card shows the hunt as running.

How the action form works in general is on [the dashboard page](../03-dashboard.md).

## What do the settings mean?

| What it means | Field on the dashboard |
|---|---|
| The milestone the encounters are filed under. With Target Type PhaseTarget or ShinyTarget, it is also the list of shiny Pokémon the hunt is trying to complete. | Milestone |
| What counts as found. **PhaseTarget**: a shiny Pokémon the milestone still needs, counting only the ones caught in this game file. **ShinyTarget**: any shiny Pokémon. **NoTarget**: nothing is ever found, so the hunt is only for training and needs a Train Type other than None. With PhaseTarget and ShinyTarget, the hunt also ends once the milestone is complete. | Target Type |
| How to meet wild Pokémon. **Run**: walk back and forth in a straight line. **Spin**: turn on the spot. **Headbutt**: headbutt the tree you are facing. | Encounter Type |
| How many tiles to walk each way from home, left and right (X Range) or up and down (Y Range). Shown only for Run. Fill in exactly one of the two, with a number above 0, and leave the other at 0. | X Range, Y Range |
| Whether to fight. **None**: run from every battle. **Standard**: the Pokémon in your first party slot fights every battle. **Switch**: the Pokémon in your first slot is sent out, then the bot switches to the Battler, which does the fighting. The Pokémon in the first slot still earns experience. | Train Type |
| The level to train to. Shown when Train Type is not None. A whole number above 0. Training is done when the Pokémon in your first party slot reaches it. | Target Level |
| The party slot of the Pokémon that fights in Switch training, from 2 to 6. Shown only for Switch. It starts at 2. | Battler |

A shiny Pokémon that the hunt is not looking for is treated like any other wild Pokémon: the bot runs from it, or fights it when training.

## Start a chained hunt

1. On the emulator's card, pick **Chained Encounter**.
2. Press **+ Leg** once for each leg you want.
   You see one set of the settings above for each leg, labelled Leg 1, Leg 2 and so on.
3. Fill in each leg's settings.
4. Press **Run**.
   The card shows the chain as running.

The bot checks every leg's settings before it starts any of them, so a mistake in the last leg is refused straight away. Each leg ends by walking back home, so the next leg starts from the same tile.

For example, a chain can switch-train a Pokémon to level 20 in its first leg, then train it normally to level 30 in its second.

## What happens in each encounter?

1. The bot walks or turns until a wild battle starts. With Headbutt, it presses A at the tree, answers Yes, and presses through the text until the battle starts. After every battle or heal it walks back to the tile you started on and turns to face the tree before the next headbutt.
2. It reads the wild Pokémon and writes it to the encounter log.
3. If it is a Pokémon you are hunting for, the bot stops and waits for you, and sends a shiny alert to [Discord](../05-discord.md). See [When it finds a target](#when-it-finds-a-target-catch-it-and-press-resume).
4. Otherwise, with Train Type None, it runs away.
5. When training, it fights with the move the bot's damage calculator picks. In Switch training it switches to the Battler first. If the switch does not work three times in a row, it runs from that battle instead.
6. It runs from a battle when the Pokémon fighting is down to a quarter of its HP or less, or has no PP left on damaging moves.
7. After a win, the bot handles level-ups, new moves and evolutions:
   - A new move goes into a free move slot without asking.
   - When the Pokémon already knows four moves, the bot asks you on Discord what to do. [The Discord page](../05-discord.md) explains the question and how to answer it.
   - Evolutions are allowed to happen. For evolutions that depend on the time of day, the bot goes by your PC's clock, and counts 8 pm to 4 am as night.

If the Pokémon that knows Headbutt is the one following you, it does the headbutt and swaps places with you, so you end up on the tile it stood on, behind you or beside you. The bot steps back and faces the tree each time. If a Pokémon that is not following you knows Headbutt, there is no swap.

## How does it heal?

Only when training. Before each encounter, the bot checks the Pokémon that will fight. If it is down to a quarter of its HP or less, or has fewer than 3 PP left across its damaging moves, the bot walks to the nearest Pokémon Center, heals, and walks back home.

It runs from any wild battle on the way there and back. Those Pokémon are still written to the encounter log, and a Pokémon you are hunting for still stops the bot.

With Train Type None the bot never heals, because it never fights.

## What does it log?

Every wild Pokémon goes into the encounter log with the game, the milestone, the Pokémon, the time and the game file. A Pokémon you are hunting for is added to your caught Pokémon once you have caught it and pressed **Resume**. [Your data](../06-your-data.md) explains where these are kept.

## When it finds a target: catch it and press Resume

1. The card shows the bot waiting, and the shiny alert arrives on Discord.
   The battle is still on screen in the emulator.
2. In the emulator, catch the Pokémon yourself.
3. On the dashboard, press **Resume** on the card.
   The bot looks for the Pokémon in your party and your PC boxes. If it finds it, it saves the game and records it as caught. Then it carries on hunting, or finishes if that was the last one the milestone needed.

If the bot could not read your party and boxes, it stays waiting. Press **Resume** again.

In a chained hunt, Resume carries on with the same leg, then the rest of the chain.

Catch it first. On **Resume** the bot looks in your party and boxes. If the Pokémon is not there (you ran from it, or it fainted), nothing is recorded as caught, and the hunt carries on as if it had not been found.

## How does it stop?

- **It finishes.** The milestone's shiny Pokémon are all caught in this game file, and any Target Level is reached. The bot walks home and the hunt completes. With NoTarget, reaching the Target Level is enough.
- **It finishes where it stands.** It could not find a way home, or a way to a Pokémon Center.
- **It waits for you.** It found a Pokémon you are hunting for (see above). Or the Pokémon fighting fainted: take over in the emulator, then press **Resume**. Or the game stopped changing for too long while the bot waited for it. Or, with Headbutt, the tree gave no Pokémon, there was no headbutt tree in front of you, or the bot could not get you back to the tile you started on (see [If it stops early](#if-it-stops-early)).
- **You cancel it** from the dashboard, as [the dashboard page](../03-dashboard.md) describes.

## If it stops early

**You see** Run refused with "A x or y range must be supplied for Run Encounter Event".
**It means** Encounter Type is Run but both X Range and Y Range are 0.
**Do** put a number above 0 in one of them.

**You see** Run refused with "Only x or y range must be supplied, not both."
**It means** both X Range and Y Range have a number above 0.
**Do** set one of them back to 0.

**You see** Run refused with "X and Y range must be non-negative values".
**It means** one of the ranges is below 0.
**Do** use 0 or a number above it.

**You see** Run refused with "TargetLevel must be a positive value when a train mode is enabled."
**It means** Train Type is Standard or Switch and Target Level is empty or 0.
**Do** fill in the level to train to.

**You see** Run refused with "A train mode must be supplied when TargetType is NoTarget, otherwise the event has no stopping condition."
**It means** NoTarget with Train Type None would never end.
**Do** pick Standard or Switch and a Target Level, or pick another Target Type.

**You see** Run refused with "SwitchSlot must be between 2 and 6 for Switch train mode."
**It means** the Battler is not a slot from 2 to 6.
**Do** pick a Battler from slot 2 to 6.

**You see** the bot run from a battle in Switch training, and its log says the switch "failed 3 times; fleeing".
**It means** the switch to the Battler did not take three times in a row.
**Do** check that the Battler slot holds the Pokémon you meant, then see [When something goes wrong](../07-when-something-goes-wrong.md).

**You see** Run refused with "No Pokémon in your party knows Headbutt."
**It means** the bot read your party when you pressed Run and no Pokémon in it knows the move. The greyed-out choice can be out of date if you changed your party in the game.
**Do** teach Headbutt to a Pokémon in your party, or pick Spin or Run.

**You see** Run refused with "The party could not be read; try again once the game is in the field."
**It means** you picked Headbutt, and when you pressed Run the bot could not read your party, for example because the game was still on the title screen or in the middle of a screen change.
**Do** wait until your character is standing in the game, then press **Run** again.

**You see** the bot waiting, and the backend's window shows "Nothing in front of you starts a script; stand facing a headbutt tree."
**It means** a headbutt found nothing in front of you.
**Do** cancel the hunt, stand facing a headbutt tree, and start it again.

**You see** the bot waiting, and the backend's window shows "The thing in front of you is not a headbutt tree (script N)." with a number for N.
**It means** what you are facing starts something other than a headbutt.
**Do** cancel the hunt, stand facing a headbutt tree, and start it again.

**You see** the bot waiting, and the backend's window shows `[Headbutt] The tree in front of you gave no Pokémon. It may give none for your trainer ID, or a Repel is active.`
**It means** the headbutt ran to the end and no battle started. Some trees give nothing for some trainer IDs, and a Repel stops headbutt battles too.
**Do** cancel the hunt, stand facing another tree (or let the Repel wear off), and start it again. **Resume** would walk you back to the same tree.

**You see** the bot waiting with a message naming a tile and a facing.
**It means** after a battle the bot could not get you back to the tile you started on, facing the tree, even after a second try, or you ended up more than one step from the start tile, which a diagonal tile is too.
**Do** cancel the hunt, put your character back on the start tile facing the tree, and start it again.

For anything else, including the bot waiting because the game stopped changing, see [When something goes wrong](../07-when-something-goes-wrong.md).

## Why does it work this way?

How the bot reads what is on screen, and how long it waits before giving up, is in the [game-state reader spec](../../specs/hgss-state-reader.md).

Next: [HeartGold and SoulSilver starter hunt](02-hgss-starter.md)
