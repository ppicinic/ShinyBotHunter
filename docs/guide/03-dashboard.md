<!-- Derived from: PokemonDashboard/src/renderer/src/components/admin/*, ShinyBotHunter/Games/EventRegistry.cs, ShinyBotHunter/Service/BotDtos.cs, PokemonDashboard/README.md, PokemonDashboard/src/renderer/src/components/AdminCard.tsx, PokemonDashboard/src/renderer/src/components/PhaseCard.tsx, PokemonDashboard/src/renderer/src/components/RecentPokemonCard.tsx, PokemonDashboard/src/renderer/src/components/WindowLayoutTab.tsx, ShinyBotHunter/Service/BotService.cs, ShinyBotHunter/Service/BotEventRunner.cs, ShinyBotHunter/Controllers/BotController.cs, ShinyBotHunter/Controllers/AdminController.cs, ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Service/PhaseInfoService.cs -->
# The dashboard

Part of the [ShinyBotHunter guide](README.md)

The dashboard is where you start and stop everything the bot does. This page walks through each screen. How to start the dashboard itself is on the [Install](01-install.md) page.

## What do you see when the dashboard opens?

The dashboard is a set of separate windows:

- **Admin**: the control panel. Everything on this page except the counts happens here.
- **Recent Encounters**: the latest Pokémon the bot has met.
- **One counts window per subscription**: the counts for one milestone (see [Watch the counts](#watch-the-counts)).

Each is its own window. Move and resize them like any other window; the dashboard remembers where they were and puts them back the next time you open it.

The Admin window has four tabs, in this order:

1. **Dashboard Config**: which counts windows are open, and how many recent encounters to show.
2. **Bot Control**: launch emulators, and start, resume and cancel hunts.
3. **Milestone Editor**: make and change milestones.
4. **Window Layout**: set window positions and sizes by number.

A fifth tab, **Dev Mode**, appears only when macro recording is switched on in the bot's settings. It is for changing the bot, not for running hunts, and this guide does not cover it.

## Launch an emulator

1. In the Admin window, click the **Bot Control** tab.
2. Under **Start Action**, find the **Launch Bot** box.
3. In **Game**, pick the game file you want to play.
4. Optional: tick **Sound On** to hear the game (see [Turn the sound on](#turn-the-sound-on)).
5. Click **Start**.

You see a green message `Started "Launch Bot"` beside the box, and a new entry under **Active Emulators**: a number such as `#1`, then its name. When nothing is running, **Active Emulators** reads "No active emulators."

**Launch Bot + Continue** works the same way. After the emulator opens, it waits about four seconds, then presses Continue on the game's menu for you, so the game loads your save.

**Start** stays greyed out until every field the box needs is filled in.

## Turn the sound on

**Sound On** is a checkbox on **Launch Bot** and **Launch Bot + Continue** only.

- Ticked: the emulator starts with the settings file `config_sound_on.ini` from your BizHawk folder.
- Not ticked: the emulator starts with its usual settings.

Setting up the BizHawk folder is on the [Install](01-install.md) page.

> **Ask the owner:** how is `config_sound_on.ini` made, and what does it change besides the sound?

## Start a hunt that launches its own emulators

Two more boxes under **Start Action** start a hunt that opens its own emulators.

**Shiny Starter Hunt**

This hunt has to be the only thing running, so close any emulators first.

1. In **Game**, pick the game family: **HeartGold / SoulSilver** or **Ruby / Sapphire**.
2. HeartGold / SoulSilver needs nothing else. Click **Start**.
3. Ruby / Sapphire shows more fields:
   - **Milestone**: where the encounters are filed.
   - **Emulators**: how many emulators to run at once.
   - One or more **Starter** entries, each with **Game** (the game file), **Starter**, and the optional **RecordingName** and **RequireFemale**. Click **+ Starter** to add an entry; **Remove** takes one away.
4. Click **Start**.

**Wishmaker Jirachi Hunt**

1. In **Game**, pick the game file. Only the Ruby and Sapphire game files the bot is tested on are offered.
2. In **Milestone**, pick where the encounters are filed.
3. Click **Start**.

**Milestone** stays greyed out and reads "Pick a game first" until you choose a game. It then lists only that game's milestones.

What each choice means is on the hunt's own page:

- [HeartGold and SoulSilver starter hunt](04-hunts/02-hgss-starter.md)
- [Ruby and Sapphire starter hunt](04-hunts/03-rs-starter.md)
- [Wishmaker Jirachi hunt](04-hunts/05-wishmaker-jirachi.md)

## Start an action on a running emulator

Every entry under **Active Emulators** shows the actions that emulator can take right now.

1. If there is more than one action, click its name in the row of buttons to show its form.
2. Fill in the form.
3. Click **Run**.

You see a green message such as `Started "Wild Encounter" on bot #1`, or a red message saying why it did not start.

An emulator offers only the actions its game supports, and only while it is not busy. While an action is running, the entry shows no actions; you can only cancel it.

## What each action asks for

The actions, in the order the dashboard lists them. "Start Action" is the list on the Bot Control tab; "Emulator" means under an entry in **Active Emulators**.

| Action | Where | Fields | More |
|---|---|---|---|
| Launch Bot | Start Action | **Game**; optional **Sound On** | [Launch an emulator](#launch-an-emulator) |
| Launch Bot + Continue | Start Action | **Game**; optional **Sound On** | [Launch an emulator](#launch-an-emulator) |
| Shiny Starter Hunt | Start Action | **Game** (HeartGold / SoulSilver or Ruby / Sapphire). For Ruby / Sapphire only: **Milestone**, **Emulators**, and one or more **Starter** entries, each with **Game**, **Starter**, optional **RecordingName** and optional **RequireFemale** | [HGSS starter](04-hunts/02-hgss-starter.md), [RS starter](04-hunts/03-rs-starter.md) |
| Wishmaker Jirachi Hunt | Start Action | **Game**, **Milestone** | [Wishmaker Jirachi](04-hunts/05-wishmaker-jirachi.md) |
| Wild Encounter | Emulator | **Milestone**; **Target Type** (PhaseTarget, ShinyTarget or NoTarget); **Encounter Type** (Spin or Run); optional **Train Type** (None, Standard or Switch; starts at None). Only when Encounter Type is Run: **X Range** and **Y Range** (start at 0). Only when Train Type is not None: **Target Level**. Only when Train Type is Switch: **Battler**, a list of the Pokémon in the game's party right now (slot 1 is never offered; starts at slot 2), with a reload button beside it | [Wild and chained encounters](04-hunts/01-wild-encounter.md) |
| Chained Encounter | Emulator | The same fields as Wild Encounter, once per leg. **+ Leg** adds a leg, **Remove** drops one. Every leg must be filled in before **Run** works | [Wild and chained encounters](04-hunts/01-wild-encounter.md) |
| HGSS Togepi Egg | Emulator | **Milestone** | [Togepi egg hunt](04-hunts/04-togepi-egg.md) |
| Resume | Emulator | None | [Resume a hunt that is waiting](#resume-a-hunt-that-is-waiting) |

## Make a milestone

A milestone is a list of the Pokémon you want, and how many of each. When you start a hunt you pick a milestone, the hunt's encounters are filed under it, and the counts windows show them per milestone.

1. In the Admin window, click the **Milestone Editor** tab.
2. In the first list, pick the game: **HGSS** or **RS**.
3. In the second list, choose **+ New milestone…**.
4. Type a name in the **New milestone name** box.
5. Fill in the Pokémon row:
   - **Name / Species**: the Pokémon, for example Rattata.
   - **Amount**: how many you want.
   - **Form Code** (optional): one particular form of the Pokémon.
   - **Require form**: tick it to count only that form.
6. Click **+ Add** for another row. The cross at the end of a row removes it.
7. Click **Save Milestone**.

You see `Saved "NAME" successfully`, with your milestone's name, and the name is now in the second list.

- **Amount 0** means the Pokémon is counted but never required, so it never stops the bot.
- A milestone with no Pokémon at all is allowed. It is the place to file training encounters, so they stay out of your real milestones' counts.
- To change a milestone you made, pick it in the second list, edit the rows, and click **Save Milestone**.
- A milestone that shows "This milestone is static (loaded from disk) and cannot be edited." came with the bot. Make a new one instead.

Milestones are stored in the bot's database; see [Your data](06-your-data.md). Always make and change them here.

## Watch the counts

1. In the Admin window, click the **Dashboard Config** tab.
2. Under **Add Subscription**, pick the game in the first list.
3. Pick the milestone in the second list.
4. Optional: pick a game file in the third list to count only that file. Leave it on **No game file** to count all of them.
5. Click **Add**.

A counts window opens for that milestone, and it is listed under **Active Subscriptions**. The window shows one row per Pokémon in the milestone, with the columns **Name**, **Milestone Encounters**, **Game Encounters** and **Shinies Caught**.

To close a counts window for good, click **Remove** beside its entry under **Active Subscriptions**.

Changes on this tab reach every dashboard connected to the bot.

## See the latest encounters

The **Recent Encounters** window lists the newest encounters first. Each shows the Pokémon, its gender, level and nature, its shiny value (**SV**; below 8 means shiny) and its six IVs. Shinies are highlighted. Before the first encounter it reads "No encounters yet...".

To change how many it shows:

1. In the Admin window, click the **Dashboard Config** tab.
2. Under **Recent Encounter Count**, type a number from 1 to 100.
3. Click **Update**.

Every encounter ever recorded is kept in the database; see [Your data](06-your-data.md).

## Resume a hunt that is waiting

Some hunts stop and wait for you, for example after finding a shiny. The entry under **Active Emulators** then offers only **Resume**.

1. Click **Run** beside **Resume**.

The hunt carries on. When and why a hunt waits is on its page: [Ruby and Sapphire starter hunt](04-hunts/03-rs-starter.md), [Wild and chained encounters](04-hunts/01-wild-encounter.md).

## Stop an action or close an emulator

1. Click **Cancel** on the entry under **Active Emulators**.
2. At **Confirm?**, click **Yes**. Click **No** to back out.

You see a green message such as `Cancelled position 1`. What happens depends on what the entry was doing:

- **Running an action on an emulator**: the action stops. The emulator stays open and its entry stays, ready for a new action.
- **Waiting for you on an emulator**: the waiting action is dropped. The emulator stays open.
- **Doing nothing**: the emulator closes and its entry goes away.
- **A hunt started from the Start Action list**: the hunt stops and its entry goes away, even while it is waiting for you.

## Arrange the windows

Move and resize the windows with the mouse; positions and sizes are saved as you go. The **Window Layout** tab lets you set them by number instead.

## If something on this screen goes wrong

- **You see** Start or Run greyed out. **It means** a field the action needs is empty. **Do** fill in every field.
- **You see** "Pick a game first" in **Milestone**. **It means** no game is chosen yet. **Do** pick the game first.
- **You see** a red box under **Start Action**. **It means** the dashboard could not reach the bot. **Do** see [When something goes wrong](07-when-something-goes-wrong.md).
- **You see** a red message after Start or Run. **It means** the bot refused, and the message is its reason, word for word. For example, a message of the form "ACTION needs CAPABILITY, which GAME does not have." means that action does not exist for that game. **Do** what the message asks, then try again.

For anything else, see [When something goes wrong](07-when-something-goes-wrong.md).

Next: [Wild and chained encounters](04-hunts/01-wild-encounter.md)
