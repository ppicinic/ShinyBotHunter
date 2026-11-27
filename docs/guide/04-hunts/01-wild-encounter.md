<!-- Derived from: ShinyBotHunter/BotEvents/OrchestratorEvent.cs, ShinyBotHunter/BotEvents/BattleOrchestratorEvent.cs, docs/specs/hgss-state-reader.md, ShinyBotHunter/BotEvents/ChainedOrchestratorEvent.cs, ShinyBotHunter/BotEvents/LevelUpFlow.cs, ShinyBotHunter/Games/EventRegistry.cs ("Wild Encounter", "Chained Encounter", "Resume"), ShinyBotHunter/Service/BotService.cs (ValidateAndBuildLeg, StartWildEncounter, StartChainedEncounter, ResumeAsync, GetAvailableActions), ShinyBotHunter/Service/BotDtos.cs (TargetType), ShinyBotHunter/BotEvents/GameState.cs (TrainType), ShinyBotHunter/BotEvents/Factories/EncounterParams.cs, ShinyBotHunter/BotEvents/Target/PhaseTarget.cs, ShinyBotHunter/BotEvents/Target/ShinyTarget.cs, ShinyBotHunter/BotEvents/Target/NoTarget.cs, ShinyBotHunter/BotEvents/RunEncounterEvent.cs, ShinyBotHunter/BotEvents/SpinEncounterEvent.cs, ShinyBotHunter/BotEvents/HeadbuttEncounterEvent.cs, ShinyBotHunter/BotEvents/NavigatorEvent.cs, ShinyBotHunter/BotEvents/BattleReadiness.cs, PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (field labels), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (encounter type choices), docs/specs/headbutt-encounter.md (FR-3 to FR-9), docs/specs/pickup-mode.md (FR-2 to FR-7), docs/research/pickup.md, PokemonDashboard/src/renderer/src/components/admin/EmulatorCard.tsx (Run and + Leg buttons), ApiHawk/CommandServer.cs (the M action), ShinyBotHunter/Pathfinding/PathfindingService.cs (capabilities, closest Pokemon Center node), ShinyBotHunter/Data/HGSS/graph.bin and mapreverselookup.json, OrchestratorEvent.cs (CheckForCatchAsync), ShinyBotHunter/Milestones/MilestonePicker.cs, ShinyBotHunter/Service/BotService.cs (NoHuntYet), PokemonDashboard/src/renderer/src/components/admin/ActionParamFields.tsx (the milestone picker), ShinyBotHunter/Milestones/Goal.cs (GameFile.AnchorFor, MilestoneAnchor), ShinyBotHunter/Milestones/HeadbuttTrees.cs, OrchestratorEvent.cs (UseAnchor, AnchorWalkGoesOn), ShinyBotHunter/Service/BotService.cs (EncounterTypeRefusal, AnchorTileStop, AnchorLeg, SetAnchorAsync, ClearAnchorAsync), ShinyBotHunter/Controllers/BotController.cs (bot/anchor), ShinyBotHunter/Milestones/MilestonePicker.cs (Anchor, AnchorSet), PokemonDashboard/src/renderer/src/components/admin/EmulatorCard.tsx (the anchor line, Set anchor here, Clear), PokemonDashboard/src/renderer/src/components/admin/HuntPlanEditor.tsx, PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (planPick, planPreview), ShinyBotHunter/Milestones/HuntPlan.cs, ShinyBotHunter/Milestones/HuntPlanRun.cs (ReachRefusalAsync), ShinyBotHunter/Controllers/PhaseMetadataController.cs (plans, PlanError), ShinyBotHunter/Service/BotService.cs (StartWildEncounter with a plan, DeletePlanAsync, RenamePlanAsync), RunEncounterEvent.cs (CloseRepelMessageAsync), ShinyBotHunter/Milestones/HuntPlanRun.cs (StepAsync, the walk that ends off the anchor), ShinyBotHunter/Milestones/HgssRates.cs (Repel applies to land and surf only) -->
# Wild and chained encounters

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

The bot walks back and forth, turns on the spot, or headbutts a tree, until a wild battle starts. It reads the wild Pokémon, writes it to the encounter log, and then either runs away or fights. It stops when it finds a Pokémon you are hunting for, or when it has reached the goal you set.

This page is for HeartGold and SoulSilver. Ruby and Sapphire have their own page: [Ruby and Sapphire wild encounters](08-rs-wild-encounter.md).

There are two actions for it on the dashboard:

- **Wild Encounter** runs one hunt.
- **Chained Encounter** runs several hunts one after another, each with its own settings, with no need for you between them. Each hunt in the chain is called a leg.

## What does your save need?

1. **An emulator that is already running with your save loaded.** These actions do not start an emulator. Start one first with **Launch Bot + Continue**, as [the dashboard page](../03-dashboard.md) describes.
2. **Your character standing on the tile you want to hunt from,** unless the milestone has an anchor: then the bot walks to the anchor first, from wherever your character stands (see [Where the hunt starts: the anchor](#where-the-hunt-starts-the-anchor)). The bot remembers that tile as home when the hunt starts. It walks back there after every trip to heal, and again before the hunt finishes.
3. **For walking back and forth (Encounter Type Run):** room to walk in a straight line from that tile, the same number of tiles each way. The bot paces left and right, or up and down, centred on where you stand.
4. **For turning on the spot (Encounter Type Spin):** nothing extra. The bot turns through the four directions without leaving the tile.
5. **For headbutting a tree (Encounter Type Headbutt):** stand on foot facing a headbutt tree, not on a bike or surfing, and have a Pokémon in your party that knows Headbutt. At a generated headbutt milestone, such as `Azalea Town · headbutt`, the bot walks to a tree that gives the Pokémon this game file still wants and faces it (see [Where the hunt starts: the anchor](#where-the-hunt-starts-the-anchor)), so you need not stand anywhere. When there is no such tree it can reach without field moves (always at Route 1, Route 45, Pallet Town, Route 21 and Mt. Silver Cave, and at some milestones for some trainer IDs and goals), the card shows no anchor and you stand facing a tree as before. At a milestone you made, or one whose anchor you set, stand facing a tree as before. Every headbutt checks that there is a tree in front of you.
   If no Pokémon in your party knows Headbutt, the Headbutt choice is greyed out with "No Pokémon in your party knows Headbutt." after it. If it shows "The party could not be read; try again once the game is in the field." instead, get the game into the field, then start and cancel any hunt, or press the reload button beside Battler (shown when Train Type is Switch). Either makes the dashboard read your party again.
6. **If the bot will fight (any Train Type other than None):** the Pokémon that does the fighting needs damaging moves with at least 3 PP between them, and more than a quarter of its HP. That is the Pokémon in your first party slot, or in Switch training the Pokémon in the Battler slot.

Before each walk the bot checks that your character is on the line, inside its two ends. Before each round of turning it checks that you are on the tile you started on. Otherwise it stops. It holds the direction until your character reaches the end of the range, then turns back, and keeps doing that until a battle starts. While your character is free to walk there is no time limit, so stand where walking starts wild battles, on a line nothing blocks. If something blocks it, your character stays pressed against it and the bot waits for a battle that does not come. If the field stops being free (a message, a script) and no battle starts within 15 s, the bot stops.

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
| The milestone the encounters are filed under. With Target Type PhaseTarget or ShinyTarget, it is also the list of shiny Pokémon the hunt is trying to complete. It is one you made, or a generated one such as `Route 30 · grass`, whose Pokémon come from the game's own tables and whose amounts come from the game file's goal; with no goal set for the file, a generated milestone wants nothing, so it is already complete. Surf, rod and rock smash milestones read `(no hunt yet)` and Run is refused at them. A grass or cave milestone is hunted with Run or Spin, a headbutt milestone with Headbutt. A milestone with an anchor is hunted from it; see [Where the hunt starts: the anchor](#where-the-hunt-starts-the-anchor). The list and its filters are on [the dashboard page](../03-dashboard.md#the-milestone-field). | Milestone |
| What counts as found. **PhaseTarget**: a shiny Pokémon the milestone still needs, counting only the ones caught in this game file. **ShinyTarget**: any shiny Pokémon. **NoTarget**: nothing is ever found, so the hunt is only for training and needs a Train Type other than None. With PhaseTarget and ShinyTarget, the hunt also ends once the milestone is complete. | Target Type |
| How to meet wild Pokémon. **Run**: walk back and forth in a straight line. **Spin**: turn on the spot. **Headbutt**: headbutt the tree you are facing. | Encounter Type |
| How many tiles to walk each way from home, left and right (X Range) or up and down (Y Range). Shown only for Run. Fill in exactly one of the two, with a number above 0, and leave the other at 0. At a milestone with an anchor, the anchor decides Run or Spin and the range instead, but these must still be filled in correctly. | X Range, Y Range |
| Whether to fight. **None**: run from every battle. **Standard**: the Pokémon in your first party slot fights every battle. **Switch**: the Pokémon in your first slot is sent out, then the bot switches to the Battler, which does the fighting. The Pokémon in the first slot still earns experience. | Train Type |
| The level to train to. Shown when Train Type is not None. A whole number above 0. Training is done when the Pokémon in your first party slot reaches it. 101 is never reached, so the bot keeps fighting until you cancel it; use that to farm items with Pickup. | Target Level (101 = no end) |
| The party slot of the Pokémon that fights in Switch training, from 2 to 6. Shown only for Switch. It starts at 2. | Battler |
| Whether to take the items your Pickup Pokémon find. Shown when Train Type is not None. Greyed out when no Pokémon in your party has the Pickup ability. See [Farming items with Pickup](#farming-items-with-pickup). | Pickup |
| A saved plan that picks the milestone by the game's clock, or **No plan**. With a plan, the plan sets the milestone. HeartGold and SoulSilver only. See [Hunt plans: move by the in-game clock](#hunt-plans-move-by-the-in-game-clock). | Hunt Plan |

A shiny Pokémon that the hunt is not looking for is treated like any other wild Pokémon: the bot runs from it, or fights it when training.

## Where the hunt starts: the anchor

A milestone can have an anchor: a tile where its hunt starts, and how it is hunted there (Run along a line, or Spin). Most generated grass and cave milestones come with one. A generated headbutt milestone gets one from your trainer ID: which table a tree gives depends on it, so the bot picks the closest tree to a Pokémon Center whose table has a Pokémon your goal still wants (any tree that gives Pokémon when nothing is wanted), and stands beside it facing it. A few headbutt milestones have no tree the bot can reach without field moves, and have no anchor. A milestone you made has none until you set one.

- **With an anchor,** pressing **Run** first walks your character to the anchor, the way the bot walks back from a Pokémon Center: without field moves, running from every wild battle on the way. Those Pokémon are still written to the encounter log, and a Pokémon you are hunting for still stops the bot. The anchor is then the hunt's home: heal trips come back to it, and the bot walks back to it before the hunt finishes.
- **The anchor decides Run or Spin and the range,** whatever the form's Encounter Type, X Range and Y Range say. The form must still be filled in correctly: Run with both ranges at 0 is refused before the anchor is used. An anchor does not change Headbutt.
- **With no anchor,** the hunt starts where your character stands.

Under the Wild Encounter settings, the card shows the picked milestone's anchor:

| The card shows | It means |
|---|---|
| `Anchor: map 34 (547, 304), run up-down ±6, generated` | The milestone's own anchor: game map 34, tile (547, 304), walking up and down 6 tiles each way. `run left-right ±<n>` and `spin` are the other two ways; a headbutt milestone's anchor reads `headbutt`. |
| The same ending `set on this file` | An anchor you set for this game file. It replaces the generated one on this file only. |
| `No anchor: hunted where the bot stands` | The hunt starts where your character stands. A generated headbutt milestone shows this when no tree that gives what this file still wants has a stand the bot can reach without field moves, and while the game is not in the field, because its tree comes from your trainer ID, which the bot can read only then. |

With an anchor at a grass or cave milestone (and Encounter Type other than Headbutt), an orange line under it says what the hunt will do, for example `The anchor decides Run or Spin and the range: this hunt will run up-down ±6, whatever the form says.`

### Set or clear an anchor

1. In the emulator, stand your character on the tile you want, facing the way you want, with every menu and message closed.
2. On the card, pick **Wild Encounter**, the milestone, and Encounter Type **Run** with its range, or **Spin**.
   **Set anchor here** stays greyed out until Encounter Type is picked, and for Run until one range is above 0.
3. Press **Set anchor here**.
   The anchor line now shows your tile and range, ending `set on this file`.

The anchor stores where your character stands and faces, and the form's Run or Spin and range. With Headbutt picked it stores the tile and facing, and the hunt still headbutts. It is kept with the game file, so it is still there after a reload or a restart. Nothing is pressed in the game. The tile is not checked when you set it; **Run** checks it (see [If it stops early](#if-it-stops-early)).

**Clear** shows only for an anchor you set. It removes it, so the milestone goes back to its generated anchor, or to none.

A chained hunt does not use anchors: each leg starts where the bot stands.

## Hunt plans: move by the in-game clock

HeartGold and SoulSilver only. A hunt plan sends one hunt to a different milestone by the game's own clock: for example Route 30 at night, Union Cave on Fridays, Route 32 the rest of the time. Ruby and Sapphire have no clock, so their Wild Encounter form has no **Hunt Plan** field.

A plan is saved by name and shared by every HeartGold and SoulSilver game file. The **Hunt Plan** list and **Edit plans** read the saved plans again each time you go to them, so a plan saved, renamed or deleted on another card shows there. It has:

- **Otherwise hunt**: the default milestone, hunted when no rule matches.
- **Rules, first match wins**: each rule has **When** (Morning, Day, Night), **Days** (Su to Sa) and **Go to**, a milestone. The first rule, from the top, whose time and day both match the game's clock is hunted. A rule with no When chip on matches any time, and one with no Days chip on matches any day; the editor says so beside the row.
- The game's time of day: morning 4:00 to 10:00, day 10:00 to 20:00, night 20:00 to 4:00.

At each decision between encounters the hunt reads the game's clock. When the plan names another milestone, the bot walks to that milestone's anchor and hunts there, from the next decision after the rule stops matching. A milestone already complete on the game file is skipped. When the default is complete and no rule matches the hour, the first rule from the top whose milestone is not complete is hunted, whatever its When and Days. The hunt ends only when every milestone of the plan is complete.

### Make, change, rename or delete a plan

1. On an idle HeartGold or SoulSilver card, pick **Wild Encounter**.
2. Next to **Hunt Plan**, press **Edit plans**.
   You see your saved plans as buttons and **+ New plan**.
3. Press a plan to change it, or **+ New plan** and type its name.
4. Pick **Otherwise hunt**. Press **Add rule** for each rule, switch its chips on, and pick its **Go to**. **Up** and **Down** move a rule; **Remove** drops it.
   A milestone with no anchor on this game file, or one with no hunt yet, has an orange hint beside it, `no anchor on this file: Start refuses the plan` or `no hunt yet: Start refuses the plan`.
5. Under **Preview**, pick a weekday. The bar shows the 24 hours split at 04:00, 10:00 and 20:00, coloured by the milestone the rules pick, with a legend. The preview ignores completion: the hunt itself skips complete milestones.
6. Press **Save**. Nothing is written before that. A refused save shows the server's reason in red, such as "The plan has no default milestone" or "Rule 2 names no milestone".

While a plan has unsaved changes, the other plans, **+ New plan**, closing the editor, the card's other actions and **Run** are greyed out; press **Save** or **Discard**. **Rename** gives the saved plan the name in the box. **Delete** asks Confirm? Yes or No.

A plan in use by a hunt, running or waiting for you, cannot be deleted or renamed: "Johto is in use by the hunt on bot #2; cancel that hunt first." You can still change and save it, but a hunt already running keeps the plan as it was when you pressed **Run**.

### Hunt with a plan

1. Pick the plan in **Hunt Plan**.
   The **Milestone** field reads `Set by the plan "Johto": it starts on its default and follows the in-game clock.` The anchor line is not shown, and **Run** works with no milestone picked.
2. Pick an **Encounter Type** that suits every milestone of the plan: Run or Spin for grass and caves, Headbutt for trees. The form's Target Type, Encounter Type and training settings apply at every stop.
3. Press **Run**. The card names the hunt `Plan Johto`. If this game file has a **Follows the hunt** window (see [Watch the counts](../03-dashboard.md#watch-the-counts)), it follows the bot.

Run is refused when the plan cannot be hunted from this game file, naming the milestone:

| You see | It means |
|---|---|
| "No hunt plan named Johto." | The plan was deleted or renamed since you picked it. Pick it again. |
| "Johto: Union Cave has no anchor." | A milestone of the plan has no anchor on this game file. Set one (see [Set or clear an anchor](#set-or-clear-an-anchor)) or change the plan. |
| "Johto: Union Cave's anchor (12, 7) cannot be reached from here without an HM." or "... from Route 32 without an HM." | The bot cannot walk from where it stands, or from another stop, to that anchor with the party and badges as they are. |
| The milestone refusals of a plain hunt, such as "Route 30 · headbutt is not hunted with Run." | The form's Encounter Type does not suit one of the plan's milestones. |

When "Repel's effect wore off..." comes up during a walk between two milestones, a walk to a Pokémon Center or the walk to the anchor, the bot closes it with A and walks on from where your character stands. If the message does not close, the hunt waits; see [When something goes wrong](../07-when-something-goes-wrong.md).

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
   On a Run hunt, when "Repel's effect wore off..." comes up, the bot closes it itself: it waits for the message to wait for A, presses A once (and again only if the game shows the press was dropped), waits until your character is free to walk, and walks on along the line. The backend's window shows `[Repel] Repel's effect wore off: closed with <n> A, confirmed by <state>`. Spin takes no steps, so the message does not come up while turning.
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

Only when training. Before each encounter, the bot checks the Pokémon that will fight. If it is down to a quarter of its HP or less, or has fewer than 3 PP left across its damaging moves, the bot walks to the nearest Pokémon Center, heals, and walks back home. The backend's window shows why, as `[Orchestrator] heal trip: <name> HP <hp>/<max>, damaging PP <pp>`, or `[Orchestrator] heal trip: switch slot <n> is empty`, with the Battler slot as the dashboard numbers it.

It runs from any wild battle on the way there and back. Those Pokémon are still written to the encounter log, and a Pokémon you are hunting for still stops the bot.

With Train Type None the bot never heals, because it never fights.

## Farming items with Pickup

Pokémon with the Pickup ability sometimes find an item after a battle. With **Pickup** ticked, the bot fights as Train Type says, and takes the items into your bag so the Pokémon can find more.

What your party needs:

- **A lead that wins.** The Pokémon in your first party slot (or the Battler, in Switch training) must knock the wild Pokémon out. Nothing is found after a battle the bot ran from.
- **Pokémon with the Pickup ability,** in any party slot. They do not have to fight.

How finding works:

- After each won battle, each Pickup Pokémon that holds nothing has a 1 in 10 chance to find an item.
- A Pokémon that already holds an item finds nothing. That is why the bot takes the items.
- What it finds depends on its level. At low levels it is mostly Potions, Antidotes, Super Potions, Great Balls, Repels and Escape Ropes. Higher levels give better items: Rare Candy starts to appear from level 21 and is more common from level 41. Leftovers appear, rarely, from level 81.

To farm:

1. Pick a Train Type other than None.
   You see the **Pickup** box.
2. Tick **Pickup**.
3. Set **Target Level (101 = no end)** to 101, so the hunt does not end on its own.
4. Press **Run**.

The box is greyed out with "No Pokémon in your party has the Pickup ability." after it when no Pokémon in your party has the ability. If it shows "The party could not be read; try again once the game is in the field." instead, get the game into the field, then start and cancel any hunt, or press the reload button beside Battler (shown when Train Type is Switch). Either makes the dashboard read your party again.

What a trip looks like: after every won battle, and once when the hunt starts, the bot opens the Pokémon menu and looks at the party list as you would (a Pokémon holding an item has an item icon on its card). It takes each held item into the bag, closes the menu, and goes back to the grass. That costs about 3 s per battle when nothing was found, and about 11 s when five items are taken. The items stay in your bag.

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

- **It finishes.** The milestone's shiny Pokémon are all caught in this game file, and any Target Level is reached. The bot walks home and the hunt completes. With NoTarget, reaching the Target Level is enough. With a [hunt plan](#hunt-plans-move-by-the-in-game-clock), it finishes only when every milestone of the plan is complete.
- **It finishes where it stands.** It could not find a way home, or a way to a Pokémon Center.
- **It waits for you.** It found a Pokémon you are hunting for (see above). Or the Pokémon fighting fainted: take over in the emulator, then press **Resume**. Or the game stopped changing for too long while the bot waited for it. Or your character is off the run line, or off the tile it turns on. Or the field stayed locked for 15 s with no battle. Or it found no way to the milestone's anchor. Or, with Headbutt, the tree gave no Pokémon, there was no headbutt tree in front of you, or the bot could not get you back to the tile you started on. Or, with Pickup, a Pokémon still held an item after a trip, or a step of the trip took too long (see [If it stops early](#if-it-stops-early)).
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

**You see** Run refused with "Route 30 · grass: the anchor (550, 304) is not grass." with the milestone's name, the anchor's tile, and `grass` or `cave`.
**It means** the milestone's anchor is not a tile of the milestone's grass, or of its cave floor, on the game's own map, so the milestone's Pokémon would not appear there. It is checked when you press Run, before any walk. Usually it is an anchor you set while standing on a path. Milestones you made are not checked.
**Do** stand your character in the milestone's grass and press **Set anchor here** again, or press **Clear** to go back to the generated anchor.

**You see** Run refused with "Route 30 · grass is not hunted with Headbutt.", "Route 30 · headbutt is not hunted with Run." or the same with Spin.
**It means** Headbutt was picked at a grass or cave milestone, or Run or Spin at a headbutt milestone.
**Do** pick Run or Spin at a grass or cave milestone, and Headbutt at a headbutt one.

**You see** **Set anchor here** refused with "Close any menu or message and stand in the field.", "Only x or y range must be supplied, not both." or "X and Y range must be non-negative values".
**It means** the game was not in the free field when you pressed it, or the range is one Run would refuse. Nothing was stored.
**Do** close the menu or message, or fix the range, and press **Set anchor here** again.

**You see** the bot waiting, and the backend's window shows `[Orchestrator] Route 30 · grass: no path to the anchor (547, 304).` with the milestone's name and the anchor's tile, after a `[Navigator] No path back to start from map=<map> (<x>,<y>) -- completing in place` line that says where your character stood.
**It means** at the start of the hunt the bot found no way from where your character stood to the anchor without field moves. The hunt does not finish there: it waits, and nothing was hunted.
**Do** cancel the hunt, walk your character nearer the anchor (onto its map, or anywhere the bot can walk from without field moves), and press **Run** again. Or stand where you want to hunt and press **Set anchor here**.

**You see** the bot waiting in a hunt with a plan, and the backend's window shows `[Orchestrator] Route 32 · grass: the walk ended at map <m> (<x>, <y>), not on the anchor (<x>, <y>).` with the milestone's name, where your character stands and the anchor's tile.
**It means** the bot walked to the next milestone of the plan, but the walk stopped short of that milestone's anchor. The hunt does not carry on from where your character stands: it waits, and nothing was hunted there.
**Do** cancel the hunt, or walk your character onto the anchor and press **Resume**.

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

**You see** Run refused with "The trainer ID could not be read; start the hunt once the game is in the field."
**It means** the milestone is a generated headbutt one, whose tree is chosen from your trainer ID, and when you pressed Run the bot read your party but then could not read the ID, for example because the game changed screens in between.
**Do** wait until your character is standing still in the field, then press **Run** again.

**You see** Run refused with "The trainer ID read as 2318286077, but a party Pokémon with the visible ID 15613 has the full ID 81149; no headbutt tree can be chosen until the read is fixed." with your numbers.
**It means** the bot's read of your trainer ID disagrees with a Pokémon in your party that you caught yourself, so the tree it would pick could be the wrong one. The second half of the ID is read from an address taken from the game's source and not yet checked on the emulator; this sentence means it is wrong.
**Do** report it with the numbers. Meanwhile stand facing a tree and press **Set anchor here** with Headbutt picked; a hunt from an anchor you set does not read the trainer ID.

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
**It means** the headbutt ran to the end and no battle started. Some trees give nothing for some trainer IDs. A Repel does not stop headbutt battles: on HeartGold and SoulSilver a Repel applies to grass, caves and surfing only. The log line's "or a Repel is active" is wrong, and the Repel is not the cause.
**Do** cancel the hunt, stand facing another tree, and start it again. **Resume** would walk you back to the same tree.

**You see** the bot waiting with a message naming a tile and a facing.
**It means** after a battle the bot could not get you back to the tile you started on, facing the tree, even after a second try, or you ended up more than one step from the start tile, which a diagonal tile is too.
**Do** cancel the hunt, put your character back on the start tile facing the tree, and start it again.

**You see** Run refused with "Pickup needs a train mode: the Pokémon must be knocked out, not fled from."
**It means** Pickup is ticked but Train Type is None, so every battle would be run from and nothing would be found.
**Do** pick Standard or Switch, or untick Pickup.

**You see** Run refused with "No Pokémon in your party has the Pickup ability."
**It means** the bot read your party when you pressed Run and no Pokémon in it has the ability. The greyed-out box can be out of date if you changed your party in the game.
**Do** put a Pokémon with Pickup in your party, or untick Pickup.

**You see** the bot waiting, and the backend's window shows "Pickup: P still holds item N after the take; check the Pokémon menu." with a Pokémon for P and the item's number for N.
**It means** the bot went through the Pokémon menu, but that Pokémon still has its item.
**Do** take the item yourself in the emulator's Pokémon menu, close the menu, and press **Resume**. The bot reads your party again and carries on.

**You see** the bot waiting, and the backend's window shows a `[Pickup]` message naming a step of the trip and what the game showed.
**It means** the game did not reach the next screen of the trip in time, for example a message stayed on screen.
**Do** close any menu or message in the emulator until your character stands in the field, and press **Resume**. The bot reads your party again and carries on.

**You see** the bot waiting, and the backend's window shows `[Run] The player is off the run line. At (<x>, <y>); the line is <x or y> +-<range> from (<x>, <y>).`
**It means** before a walk, your character was not on the line, or was past one of its ends. A word in angle brackets stands for the value the bot fills in.
**Do** put your character back on the line, and press **Resume**.

**You see** the bot waiting, and the backend's window shows `[Spin] The player moved while turning. At (<x>, <y>), started on (<x>, <y>).`
**It means** before a round of turning, your character was not on the tile the hunt started on.
**Do** put your character back on the start tile, and press **Resume**.

**You see** the bot waiting, and the backend's window shows `[Spin] Something other than a wild battle started. Waited for a battle within 15 s of the field leaving free; the game state stayed <state>`, or the same starting with `[Run]`.
**It means** something stopped your character walking, such as a message or a script, and no battle started within 15 s. On Ruby and Sapphire a locked field (a message, a script, a trainer) is handed to the battle after 4 s instead, and a lock that is not a trainer stops with `[RsBattle] stopped: Something other than a wild battle started.` about 15 s after it began; see [Ruby and Sapphire wild encounters](08-rs-wild-encounter.md#the-bot-stops-and-waits).
**Do** look at that emulator, finish what the game shows yourself (end or run from the battle, close the message) until your character can walk, put your character back on the start tile or the line, and press **Resume**.

For anything else, including the bot waiting because the game stopped changing, see [When something goes wrong](../07-when-something-goes-wrong.md).

## Why does it work this way?

How the bot reads what is on screen, and how long it waits before giving up, is in the [game-state reader spec](../../specs/hgss-state-reader.md).

Next: [HeartGold and SoulSilver starter hunt](02-hgss-starter.md)
