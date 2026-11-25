<!-- Derived from: PokemonDashboard/src/renderer/src/components/admin/*, ShinyBotHunter/Games/EventRegistry.cs, ShinyBotHunter/Service/BotDtos.cs, PokemonDashboard/README.md, PokemonDashboard/src/renderer/src/components/AdminCard.tsx, PokemonDashboard/src/renderer/src/components/PhaseCard.tsx, PokemonDashboard/src/renderer/src/components/RecentPokemonCard.tsx, PokemonDashboard/src/renderer/src/components/WindowLayoutTab.tsx, PokemonDashboard/src/renderer/src/windows/PickupWindow.tsx, PokemonDashboard/src/renderer/src/components/admin/AppearanceSection.tsx, PokemonDashboard/src/renderer/src/utils/theme.ts, PokemonDashboard/src/renderer/src/components/PokemonSprite.tsx, ShinyBotHunter/Service/BotService.cs, ShinyBotHunter/Service/BotEventRunner.cs, ShinyBotHunter/Controllers/BotController.cs, ShinyBotHunter/Controllers/AdminController.cs, ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Service/PhaseInfoService.cs, ShinyBotHunter/Milestones/MilestonePicker.cs, ShinyBotHunter/Controllers/PhaseMetadataController.cs (goal/{gameFileName}, goal/{game}/{gameFileName}/preset/{preset}, species/{game}, goal/{gameFileName}/archived, goal/{gameFileName}/order, goal/{gameFileName}/hidden, goal/{gameFileName}/assigned, goal/{game}/{gameFileName}/link, goal/{game}/{gameFileName}/backfill/{phase}, goal/{game}/{gameFileName}/copy/{phase}, milestone/{game}/{milestoneId}/{gameFileName}), ShinyBotHunter/Milestones/Backfill.cs, ShinyBotHunter/Service/BotService.cs (RefuseLinked, LinkedRefusal), PokemonDashboard/src/renderer/src/context/AppContext.tsx, docs/guide/01-install.md (Get the BizHawk fork, step 4) -->
# The dashboard

Part of the [ShinyBotHunter guide](README.md)

The dashboard is where you start and stop everything the bot does. This page walks through each screen. How to start the dashboard itself is on the [Install](01-install.md) page.

## What do you see when the dashboard opens?

The dashboard is a set of separate windows:

- **Admin**: the control panel. Everything on this page except the counts happens here.
- **Recent Encounters**: the latest Pokémon the bot has met.
- **One counts window per subscription**: the counts for one milestone, or for whatever milestone a game file is hunting (see [Watch the counts](#watch-the-counts)).
- **One Pickup items window per game file, when switched on**: the items a Pickup hunt has taken (see [Watch the Pickup items](#watch-the-pickup-items)).

Each is its own window. Move and resize them like any other window; the dashboard remembers where they were and puts them back the next time you open it.

The Admin window has five tabs, in this order:

1. **Dashboard Config**: the look of the windows (themes), which counts windows are open, how many recent encounters to show, and the Pickup items windows.
2. **Bot Control**: launch emulators, and start, resume and cancel hunts.
3. **Milestone Editor**: make and change milestones.
4. **Goals**: set what each game file is collecting (see [Set a game file's goal](#set-a-game-files-goal)).
5. **Window Layout**: set window positions and sizes by number.

A sixth tab, **Dev Mode**, appears only when macro recording is switched on in the bot's settings. It is for changing the bot, not for running hunts, and this guide does not cover it.

## Launch an emulator

1. In the Admin window, click the **Bot Control** tab.
2. Under **Start Action**, find the **Launch Bot** box.
3. In **Game**, pick the game file you want to play.
4. Optional: tick **Sound On** to hear the game (see [Turn the sound on](#turn-the-sound-on)).
5. Click **Start**.

You see a green message `Started "Launch Bot"` beside the box, and a new entry under **Active Emulators**: a number such as `#1`, then its name. When nothing is running, **Active Emulators** reads "No active emulators."

**Launch Bot + Continue** works the same way. After the emulator opens, it waits about four seconds, then presses Continue on the game's menu for you, so the game loads your save. Its entry under **Active Emulators** only appears once the game has loaded and you are standing in the field, so nothing reads the game before then.

**Start** stays greyed out until every field the box needs is filled in.

## Turn the sound on

**Sound On** is a checkbox on **Launch Bot** and **Launch Bot + Continue** only.

- Ticked: the emulator starts with the settings file `config_sound_on.ini` from your BizHawk folder.
- Not ticked: the emulator starts with its usual settings.

Setting up the BizHawk folder is on the [Install](01-install.md) page.

How to make `config_sound_on.ini` is in [Install](01-install.md#get-the-bizhawk-fork), step 4. It is a copy of `config.ini` made while the sound was on.

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

### The Milestone field

On an emulator's form, **Milestone** lists the milestones of that emulator's game file, in this order: the ones you gave an order label, by label, then the rest by name. Numbers in a name or label count as numbers, so `1-4` comes before `1-10`. A game file you have set nothing on lists every milestone you made, as before.

- **Wild Encounter** and each leg of **Chained Encounter** list two kinds: the milestones you made in the [Milestone Editor](#make-a-milestone), and one generated milestone for each wild place and way of meeting Pokémon in the game, named like `Route 30 · grass`.
- **HGSS Togepi Egg**, **HGSS Primo Egg** and **HGSS Static Encounter** list only the milestones you made.

Above the list:

- **Filter**: type part of a name, an order label or a generated id such as `34-land`. Upper and lower case match alike.
- The second box, shown when there are generated milestones: **All**, **Custom** (the ones you made), or one way of meeting Pokémon (**Land**, **Surf**, **OldRod**, **GoodRod**, **SuperRod**, **RockSmash**, **Headbutt**).
- **Incomplete only**: hides the milestones that are complete for this game file.
- **Show archived**: shows the milestones you archived. They are hidden otherwise.

A milestone the bot cannot hunt yet reads `(no hunt yet)` after its name: surf, the three rods and rock smash. You can pick it, but Run is refused with the reason. An archived one reads `(archived)`.

Once a milestone is picked, two controls appear under the list:

- **Archive** hides it from this game file's list; **Unarchive** brings it back. Archiving changes no count and no encounter.
- **Order**: type a label such as `1-4` and click elsewhere to save it. Empty the box to remove the label.

Both are saved for this game file and the list reloads.

On **Wild Encounter**, a line under the settings shows the picked milestone's anchor, the tile its hunt starts from, or `No anchor: hunted where the bot stands`. **Set anchor here** makes where your character stands the anchor for this game file, and **Clear** removes one you set; [Where the hunt starts: the anchor](04-hunts/01-wild-encounter.md#where-the-hunt-starts-the-anchor) explains both.

#### Hide a row

When the milestone picked on **Wild Encounter** is a generated one, a line under the anchor reads **Rows (N hidden)**, N being how many of its rows you hid on this game file. Click it to list the rows: each shows the Pokémon and its form (or "any form"), how many are wanted and caught, the encounters at this milestone, and a **Hide** checkbox.

- Tick **Hide** to hide a row on this game file. A hidden row is not shown on the counts card and is never a target; its encounters are still logged. It stays in this list, greyed, so you can untick it to show it again.
- The card drops or brings back the row at its next count update.
- Hiding is per game file: the same milestone on another file keeps its rows.
- You hide rows from an emulator's form, so the game file needs a bot running on it.

An emulator offers only the actions its game supports, and only while it is not busy. While an action is running, the entry shows no actions; you can only cancel it.

While a Ruby or Sapphire wild hunt runs, its entry can show small grey lines under its name, each naming something in your save that slows the hunt. These are the [speed lines](04-hunts/08-rs-wild-encounter.md#the-speed-lines).

## What each action asks for

The actions, in the order the dashboard lists them. "Start Action" is the list on the Bot Control tab; "Emulator" means under an entry in **Active Emulators**.

| Action | Where | Fields | More |
|---|---|---|---|
| Launch Bot | Start Action | **Game**; optional **Sound On** | [Launch an emulator](#launch-an-emulator) |
| Launch Bot + Continue | Start Action | **Game**; optional **Sound On** | [Launch an emulator](#launch-an-emulator) |
| Shiny Starter Hunt | Start Action | **Game** (HeartGold / SoulSilver or Ruby / Sapphire). For Ruby / Sapphire only: **Milestone**, **Emulators**, and one or more **Starter** entries, each with **Game**, **Starter**, optional **RecordingName** and optional **RequireFemale** | [HGSS starter](04-hunts/02-hgss-starter.md), [RS starter](04-hunts/03-rs-starter.md) |
| Wishmaker Jirachi Hunt | Start Action | **Game**, **Milestone** | [Wishmaker Jirachi](04-hunts/05-wishmaker-jirachi.md) |
| Wild Encounter | Emulator | **Milestone** (made and generated; see [The Milestone field](#the-milestone-field)); **Target Type** (PhaseTarget, ShinyTarget or NoTarget); **Encounter Type** (Spin, Run or Headbutt); optional **Train Type** (None, Standard or Switch; starts at None). Only when Encounter Type is Run: **X Range** and **Y Range** (start at 0). Only when Train Type is not None: **Target Level (101 = no end)** and **Pickup** (a checkbox, greyed out with the reason when no Pokémon in your party has the Pickup ability). Only when Train Type is Switch: **Battler**, a list of the Pokémon in the game's party right now (slot 1 is never offered; starts at slot 2), with a reload button beside it. For Ruby and Sapphire, Headbutt and Pickup are greyed out with the reason in brackets | [Wild and chained encounters](04-hunts/01-wild-encounter.md), [RS wild encounters](04-hunts/08-rs-wild-encounter.md) |
| Chained Encounter | Emulator | The same fields as Wild Encounter, once per leg. **+ Leg** adds a leg, **Remove** drops one. Every leg must be filled in before **Run** works. HeartGold and SoulSilver only | [Wild and chained encounters](04-hunts/01-wild-encounter.md) |
| HGSS Togepi Egg | Emulator | **Milestone** (only the milestones you made) | [Togepi egg hunt](04-hunts/04-togepi-egg.md) |
| HGSS Primo Egg | Emulator | **Milestone** (only the milestones you made) | [Primo egg hunt](04-hunts/06-primo-egg.md) |
| HGSS Static Encounter | Emulator | **Milestone** (only the milestones you made), **StaticEncounter** (a list: Dialga (Sinjoh Ruins), Palkia (Sinjoh Ruins), Giratina (Sinjoh Ruins)) | [Static encounter hunt](04-hunts/07-static-encounter.md) |
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

When you pick a saved milestone, a **Game files** section under **Save Milestone** has one box for each detected game file of that game. Opening it saves nothing; each change below is saved when you click it, for that game file only.

### Use a milestone on some game files only

Tick **Assigned** on the game files whose **Milestone** list should offer this milestone. The line above the boxes says where it is listed:

- `Assigned to no game file: listed on every game file.` is how every milestone starts.
- `Assigned to 2 game files: listed only on those.` once you tick some. Untick them all to list it everywhere again.

Generated milestones need no assignment: the game file's goal decides their rows.

### Link a milestone you made to a generated one

A milestone you made before the generated ones existed, such as `1-4 Route 30`, can be linked on one game file to the generated milestone for the same place, such as `34-land` (`Route 30 · grass`). The box is filled with the suggested id when there is one; type another id to override it, or leave it empty for a milestone with none, such as a training one.

1. Check the id in the box. Typing an id such as `34-land` in an emulator's **Milestone** filter finds the generated milestone it names.
2. Click **Link**.

The box then reads `Linked to 34-land`. On that game file:

- The generated milestone's counts include everything logged under the linked name, and new encounters are logged under the generated milestone. Nothing already logged is rewritten.
- The linked name leaves the **Milestone** list. Hunt the generated milestone instead; a Start with the linked name is refused with, for example, `1-4 Route 30 is linked to Route 30 · grass on <file>; start Route 30 · grass, or remove the link.`
- **Remove link** undoes it: the name is back in the list, and the generated milestone counts only what is logged under its own id.

A red message says why a link is refused: `No generated milestone "<id>" for this game file's version`, or `<milestone> is already linked to <id> on <file>; remove that link first`.

### Copy its amounts into the goal

Linking copies nothing. To take the amounts saved in this milestone into the game file's goal, so the generated milestone wants the same:

1. Click **Copy amounts…** on the game file.
2. Each Pokémon of the milestone is shown with what it would copy beside what the goal has for it now. Tick the ones to copy.
3. Click **Copy chosen**.

Only the ticked Pokémon change: their entries in the goal are replaced by the milestone's, and every other entry is kept. The goal column then shows the new entries. To change amounts one by one, use [Set a game file's goal](#set-a-game-files-goal).

## Set a game file's goal

A game file's goal is what that file is collecting: for each Pokémon, or one form of it, how many. You set it once per game file, and every generated milestone, such as `Route 30 · grass`, shows its rows on that file through it. The milestones you made in the Milestone Editor keep their own amounts.

1. In the Admin window, click the **Goals** tab.
2. In **Game file**, pick the game file. Its goal is shown; a file with no goal yet shows none, and nothing is saved until you save or apply a preset.
3. Optional: under **Start from a preset**, click one:
   - **Living dex**: every species, as many as there are Pokémon in its evolution line; Unown is one of each of its 28 letters.
   - **Female forms** (HeartGold and SoulSilver only): every species with a female form, the female form at the number of its line that have one, and the male form at 0.
   - **Version exclusives**: one of each species found in this game's wild places and not in the other game's.

   A yellow box says what the preset does. Click **Apply** and the preset's name to go ahead, or **Cancel**. The goal is saved at once. A species the goal already has an entry for is kept as it is, so an amount you changed survives a preset applied later. Presets do not stack on a species: one the goal already has, including from an earlier preset, is left as it is. The presets are greyed out while you have edits not yet saved.
4. Change the entries:
   - **Filter**: type part of a name, a number such as `190`, or a form code to show only those entries.
   - Each entry shows the Pokémon, its number, the form (or "any form") and its **Amount**. An amount of 0 reads "tracked, never a target": the Pokémon is shown and counted, but the bot never stops for it. An amount that is not a whole number, 0 or more, reads "Amount is a whole number, 0 or more." in red, and the goal cannot be saved until it is fixed.
   - The cross at the end of an entry removes it.
   - To add one, type a species name or number in **Species** (the list suggests names), an optional **Form code**, and an **Amount**, then click **+ Add**.
5. Click **Save goal**.

You see `Saved the goal of FILE`. If the bot refuses the goal, a red message says why, for example `Species 201 has both an any-form entry and form entries`: a species is wanted either in any form or by its forms, never both, and each form only once. While you have edits not yet saved, **Game file** is greyed out; save them first, or leave the tab to drop them.

If a preset says "This game file is no longer in the game list: reopen the Goals tab.", the bot no longer has that game file in its list. Click another tab and then **Goals** again, and pick a file from the list it shows.

Goals are stored in the bot's database; see [Your data](06-your-data.md).

## Watch the counts

1. In the Admin window, click the **Dashboard Config** tab.
2. Under **Add Subscription**, pick the game in the first list.
3. Pick the milestone in the second list.
4. Optional: pick a game file in the third list to count only that file. Leave it on **No game file** to count all of them.
5. Optional: tick **Follows the hunt** to make a window that follows that game file's hunt (see below). It is greyed out until you pick a game file.
6. Click **Add**.

A counts window opens for that milestone, and it is listed under **Active Subscriptions**. The window shows one row per Pokémon in the milestone, with the columns **Name**, **Milestone Encounters**, **Game Encounters** and **Shinies Caught**.

A window on a generated milestone, such as `Route 30 · grass`, shows that name as its title and one row for every Pokémon that can be met there: one per form the game file wants, so the Ruins of Alph with the Unown preset shows 28. A row the game file wants none of reads `tracked`. A row you hid on that game file is not shown. Its counts are read again at each encounter. If the bot knows no milestone by that name, the window says `Milestone "NAME" was not found.`

There are two kinds of counts window:

- **Fixed**: shows the one milestone you picked, always. A game file can have any number of these. Its title is the game, the milestone and the game file, for example `HGSS — Route 32 (HG-TimeMove)`.
- **Follows the hunt**: shows whatever milestone the bot on that game file is hunting. When a hunt starts on the file at another milestone, or a Chained Encounter moves to its next leg, the same window shows the new milestone's rows: it keeps its title, place and size, and it draws the new rows even while another window covers it. With no hunt running it shows the last milestone hunted, and after a restart it shows that one again. The milestone you pick when you add it is the one it shows until a hunt starts. Its title is the game and the game file, for example `HGSS — HG-TimeMove`, and it is listed with `· follows the hunt` after it.

A game file has at most one window that follows the hunt, beside any fixed ones. When the game file you picked already has one, the checkbox is greyed out and reads **Follows the hunt (this file has one)**. Subscriptions made before this checkbox existed are fixed.

To close a counts window for good, click **Remove** beside its entry under **Active Subscriptions**.

The bot remembers your subscriptions: after a restart of the bot, the counts windows open again by themselves.

Changes on this tab reach every dashboard connected to the bot.

## See the latest encounters

The **Recent Encounters** window lists the newest encounters first. Each shows the Pokémon, its gender, level and nature, its shiny value (**SV**; below 8 means shiny) and its six IVs. Shinies are highlighted. The list is this PC's own: it holds what this bot has met since it started, so after the bot restarts it reads "No encounters yet..." until the next encounter.

Each Pokémon moves: a moving picture where one exists, otherwise its still picture with a small hop or sway. Each drops in when it appears, and a shiny twinkles.

To change how many it shows:

1. In the Admin window, click the **Dashboard Config** tab.
2. Under **Recent Encounter Count**, type a number from 1 to 100.
3. Click **Update**.

Every encounter ever recorded is kept in the database; see [Your data](06-your-data.md).

## Watch the Pickup items

A Pickup items window shows what a Pickup hunt has taken on one game file: each item as a small tile with its picture, name and count, several to a row as the window's width fits, and the total below them. There is one window per game file the bot has taken items on. The window's top line shows the game file in small grey text, then the milestone of the hunt that last took an item on it.

The pictures come from the `Items` folder in the sprite folder (see [Fill in the Pokemon data](01-install.md#fill-in-the-pokemon-data-once-per-pc)). An item without a picture there shows only its name and count.

It counts only the items the bot itself took during a Pickup hunt since you last [started the bot](01-install.md#start-the-bot-for-the-first-time) (`ShinyBotHunter.exe`). Starting another hunt does not reset the counts; it adds to them. When you restart the bot, the counts start again from zero. Items you take by hand in the emulator are not counted.

To open the windows:

1. In the Admin window, click the **Dashboard Config** tab.
2. Under **Pickup Items Windows**, tick **Pickup items windows**.

A window opens for every game file with items taken so far, and a new one opens when the bot takes its first item on another game file. Each window updates after every item the bot takes.

To close them all, untick **Pickup items windows**. A window you close yourself stays closed until you untick the box and tick it again. The box is always unticked when the dashboard starts.

## Resume a hunt that is waiting

Some hunts stop and wait for you, for example after finding a shiny, or when a trainer challenges your character in Ruby or Sapphire. The entry under **Active Emulators** then offers only **Resume**.

1. Click **Run** beside **Resume**.

The hunt carries on. When and why a hunt waits is on its page: [Ruby and Sapphire starter hunt](04-hunts/03-rs-starter.md), [Wild and chained encounters](04-hunts/01-wild-encounter.md), [Ruby and Sapphire wild encounters](04-hunts/08-rs-wild-encounter.md).

## Stop an action or close an emulator

1. Click **Cancel** on the entry under **Active Emulators**.
2. At **Confirm?**, click **Yes**. Click **No** to back out.

You see a green message such as `Cancelled position 1`. What happens depends on what the entry was doing:

- **Running an action on an emulator**: the action stops. The emulator stays open and its entry stays, ready for a new action.
- **Waiting for you on an emulator**: the waiting action is dropped. The emulator stays open.
- **Doing nothing**: the emulator closes and its entry goes away.
- **A hunt started from the Start Action list**: the hunt stops and its entry goes away, even while it is waiting for you.

## Choose a theme

A theme sets the colours and background of the windows. There are eight:

- **Midnight**: the dark blue the dashboard has always had. It is the default.
- **HGSS Gold and Silver** and **HGSS Dusk**.
- **Ruby and Sapphire**, **Ruby** and **Sapphire**.
- **Christmas Pine** and **Christmas Night**.

The game themes have a slowly moving background. The two Christmas themes have snow falling in every window. Christmas is never picked by date; you pick it.

To choose a theme:

1. In the Admin window, click the **Dashboard Config** tab.
2. **Appearance** is at the top. In the **Default** row, pick a **Theme**. Every window takes it.
3. To give one window its own theme, pick a **Theme** in that window's row. There is a row for Admin, Recent Encounters, each counts window and each open Pickup items window. A row's first choice, **Default**, follows the Default row; any other choice is for that window only.

To switch at night:

1. In a row, tick **Switch at night**.
2. Pick a **Night theme**. HGSS Dusk and Christmas Night are listed first, but any theme can be picked. Nothing changes until you pick one.
3. **Day starts** and **Night starts** appear while any row switches at night. Set the two times.

Each window switches by itself at those times, by this PC's clock, without reloading. **Switch at night** is greyed out on a row set to **Default**: pick a theme for that row first.

A theme set for a window that is closed, or no longer subscribed, shows greyed with **Remove**. It applies again when the window comes back.

To change how much the windows move:

1. In **Appearance**, find **Animations** at the top. It has three settings: **Gradients** (the moving backgrounds, glows and Christmas lights), **Snow** and **Stars** (the twinkling stars on a shiny Pokemon).
2. Pick **Off**, **Low**, **Medium** or **High** for each. **High** is the smooth movement, and what a new dashboard starts on. **Medium** moves in small jumps, about ten a second. **Low** moves in bigger jumps, a few a second, and costs the PC much less. **Off** keeps the gradients still, and hides the snow or the stars completely.

Each setting changes every window at once. The Pokemon pictures keep moving whatever you pick. If Windows is set to reduce motion (Settings, Accessibility, Visual effects, Animation effects off), the dashboard shows no snow and no stars and keeps the gradients still, whatever the settings say.

If the emulators slow down while the dashboard is open, set all three to **Low** first. If they are still slow, set them to **Off**.

The bot keeps all of this per PC: every dashboard on this PC looks the same, and it is still there after you restart the dashboard or the bot. Each PC running a bot has its own themes and animation levels, saved in `%LOCALAPPDATA%\ShinyBotHunter\appearance.json`. Deleting that file, with the bot stopped, resets them to the defaults, except right after an update, when the bot may copy the themes saved before back once. The first start after updating takes the themes saved before, once.

## Arrange the windows

Move and resize the windows with the mouse; positions and sizes are saved as you go. The **Window Layout** tab lets you set them by number instead.

## If something on this screen goes wrong

- **You see** Start or Run greyed out. **It means** a field the action needs is empty. **Do** fill in every field.
- **You see** a red message "NAME: no hunt yet." after Run. **It means** the milestone is met by surfing, fishing or rock smash, which the bot cannot do yet. **Do** pick a grass, cave or headbutt milestone; the **Land** and **Headbutt** filters show only those.
- **You see** "Pick a game first" in **Milestone**. **It means** no game is chosen yet. **Do** pick the game first.
- **You see** a red box under **Start Action**. **It means** the dashboard could not reach the bot. **Do** see [When something goes wrong](07-when-something-goes-wrong.md).
- **You see** a red message after Start or Run. **It means** the bot refused, and the message is its reason, word for word. For example, a message of the form "ACTION needs CAPABILITY, which GAME does not have." means that action does not exist for that game. **Do** what the message asks, then try again.
- **You see** "Waiting for the backend" under **Appearance**. **It means** the dashboard is not connected to the bot. **Do** start the bot.

For anything else, see [When something goes wrong](07-when-something-goes-wrong.md).

Next: [Wild and chained encounters](04-hunts/01-wild-encounter.md)
