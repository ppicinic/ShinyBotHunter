<!-- Derived from: PokemonDashboard/src/renderer/src/components/admin/*, ShinyBotHunter/Games/EventRegistry.cs, ShinyBotHunter/Service/BotDtos.cs, PokemonDashboard/README.md, PokemonDashboard/src/renderer/src/components/AdminCard.tsx, PokemonDashboard/src/renderer/src/components/PhaseCard.tsx, PokemonDashboard/src/renderer/src/components/RecentPokemonCard.tsx, PokemonDashboard/src/renderer/src/components/WindowLayoutTab.tsx, PokemonDashboard/src/renderer/src/windows/PickupWindow.tsx, PokemonDashboard/src/renderer/src/components/admin/AppearanceSection.tsx, PokemonDashboard/src/renderer/src/utils/theme.ts, PokemonDashboard/src/renderer/src/components/PokemonSprite.tsx, ShinyBotHunter/Service/BotService.cs, ShinyBotHunter/Service/BotEventRunner.cs, ShinyBotHunter/Controllers/BotController.cs, ShinyBotHunter/Controllers/AdminController.cs, ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Service/PhaseInfoService.cs, ShinyBotHunter/Milestones/MilestonePicker.cs, ShinyBotHunter/Controllers/PhaseMetadataController.cs (phaseinfo/{game}/{phase}/{gameFileName?}, RowNames, updatePhaseInfo, milestones/{game}/locations, milestones/{game}/locations/{locationId}/seed, milestones/{game}/{name}/location/{locationId} PUT, milestones/{game}/{name}/location DELETE, plans/steps, plans/{game}, PUT plans/{game}/{name} and PlanError, plans/{game}/{name}/rename), ShinyBotHunter/Service/PhaseInfoService.cs (Seed, HiddenUntil, HiddenNow), ShinyBotHunter/Service/PokemonMetadataService.cs (RowName), ShinyBotHunter/Milestones/GoalPresets.cs, ShinyBotHunter/Milestones/MilestoneRows.cs (Counted, Shares), ShinyBotHunter/Milestones/Backfill.cs (Suggestion), ShinyBotHunter/Milestones/HuntPlan.cs (Plan.StepActions, Plan.TimeOfDay), ShinyBotHunter/BotEvents/PlanEvent.cs (StepName, FollowHuntAsync), ShinyBotHunter/Service/BotService.cs (StartPlanAsync, OffFileRefusal, PlanInUseRefusal, SavePlanAsync, RenamePlanAsync, DeletePlanAsync, GetBotDashboardState), PokemonDashboard/src/renderer/src/context/AppContext.tsx, docs/guide/01-install.md (Get the BizHawk fork, step 4) -->
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
3. **Milestone Editor**: make and change milestones (see [Make a milestone](#make-a-milestone)).
4. **Plan Editor**: make and change plans, which switch a HeartGold or SoulSilver hunt by the in-game clock (see [Make a plan](#make-a-plan)).
5. **Window Layout**: set window positions and sizes by number.

A sixth tab, **Dev Mode**, appears only when macro recording is switched on in the bot's settings. It is for changing the bot, not for running hunts, and this guide does not cover it.

## Launch an emulator

1. In the Admin window, click the **Bot Control** tab.
2. Under **Start Action**, find the **Launch Bot** box.
3. In **Game**, pick the game file you want to play.
4. Optional: tick **Sound On** to hear the game (see [Turn the sound on](#turn-the-sound-on)).
5. Click **Start**.

You see a green message `Started "Launch Bot"` beside the box, and a new entry under **Active Emulators**: a number such as `#1`, then its name (once a hunt is started, the hunt's milestone, such as `Route 30 · grass`, or its plan, such as `Plan Johto`, which reads `Plan Johto: Route 30 · grass` once the plan has picked a step, or `Plan Johto: Wait`). When nothing is running, **Active Emulators** reads "No active emulators."

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

On an emulator's form, **Milestone** lists the milestones saved in the [Milestone Editor](#make-a-milestone) that are offered on that emulator's game file: the ones with that file ticked under **Game files**, or with no file ticked, and not archived. They are in name order, and numbers in a name count as numbers, so `1-4` comes before `1-10`. The wild places in the game's data are not listed here by themselves: make a milestone from one first.

- **Wild Encounter** and each leg of **Chained Encounter** list all of them.
- **HGSS Togepi Egg**, **HGSS Primo Egg** and **HGSS Static Encounter** list only the milestones with no location.

Under the list, **Incomplete only** hides the milestones that are complete on this game file.

A milestone the bot cannot hunt yet reads `(no hunt yet)` after its name: one whose location is met by surfing, a rod or rock smash. You can pick it, but Run is refused with the reason.

On **Wild Encounter**, a line under the settings shows the picked milestone's anchor, the tile its hunt starts from, or `No anchor: hunted where the bot stands`. **Set anchor here** makes where your character stands the anchor for this game file, and **Clear** removes one you set; [Where the hunt starts: the anchor](04-hunts/01-wild-encounter.md#where-the-hunt-starts-the-anchor) explains both.

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
| Wild Encounter | Emulator | **Milestone** (see [The Milestone field](#the-milestone-field)); **Target Type** (PhaseTarget, ShinyTarget or NoTarget); **Encounter Type** (Spin, Run or Headbutt); optional **Train Type** (None, Standard or Switch; starts at None). Only when Encounter Type is Run: **X Range** and **Y Range** (start at 0). Only when Train Type is not None: **Target Level (101 = no end)** and **Pickup** (a checkbox, greyed out with the reason when no Pokémon in your party has the Pickup ability). Only when Train Type is Switch: **Battler**, a list of the Pokémon in the game's party right now (slot 1 is never offered; starts at slot 2), with a reload button beside it. For Ruby and Sapphire, Headbutt and Pickup are greyed out with the reason in brackets | [Wild and chained encounters](04-hunts/01-wild-encounter.md), [RS wild encounters](04-hunts/08-rs-wild-encounter.md) |
| Chained Encounter | Emulator | The same fields as Wild Encounter, once per leg. **+ Leg** adds a leg, **Remove** drops one. Every leg must be filled in before **Run** works. HeartGold and SoulSilver only | [Wild and chained encounters](04-hunts/01-wild-encounter.md) |
| HGSS Togepi Egg | Emulator | **Milestone** (only the milestones with no location) | [Togepi egg hunt](04-hunts/04-togepi-egg.md) |
| HGSS Primo Egg | Emulator | **Milestone** (only the milestones with no location) | [Primo egg hunt](04-hunts/06-primo-egg.md) |
| HGSS Static Encounter | Emulator | **Milestone** (only the milestones with no location), **StaticEncounter** (a list: Dialga (Sinjoh Ruins), Palkia (Sinjoh Ruins), Giratina (Sinjoh Ruins)) | [Static encounter hunt](04-hunts/07-static-encounter.md) |
| Plan | Emulator | **Plan** (a list of the game's saved plans). HeartGold and SoulSilver only | [Make a plan](#make-a-plan), [Plans](04-hunts/01-wild-encounter.md#plans-change-hunts-by-the-in-game-clock) |
| Resume | Emulator | None | [Resume a hunt that is waiting](#resume-a-hunt-that-is-waiting) |

## Make a milestone

A milestone is a list of the Pokémon you want, and how many of each. When you start a hunt you pick a milestone, the hunt's encounters are filed under it, and the counts windows show them per milestone. Shinies are counted per game file: one caught on a file counts for that Pokémon in every milestone hunted on that file.

Every milestone is one you save here. There are two ways to start one, and both begin the same way:

1. In the Admin window, click the **Milestone Editor** tab.
2. In the first list, pick the game: **HGSS** or **RS**.

### From the game's data

1. Click **New from game data**. It is greyed out until the game's places have loaded.
2. In **Location**, pick the wild place. The list holds every wild place of the game, grouped by how Pokémon are met there (**Land**, **Surf**, **OldRod**, **GoodRod**, **SuperRod**, **RockSmash**, **Headbutt**), each named like `Route 30 · grass`. It starts at the first one.
3. In **Load species of**, pick **Both versions** or one version: **HeartGold only** and **SoulSilver only**, or **Ruby only** and **Sapphire only**. It starts at **Both versions**.
4. In **Preset**, pick how the amounts are filled. It starts at **Living dex**:
   - **None (all 0)**: every amount is 0.
   - **Living dex**: each Pokémon at the number of Pokémon in its evolution line. Unown is one row per letter, 28 in all, at 1 each.
   - **Female forms** (HeartGold and SoulSilver only): a Pokémon with a female form gets two rows, the female form at the number of its line that have one, and the male form at 0. Every other Pokémon is at 0.
   - **Version exclusives**: 1 of each Pokémon that only one version has in the wild (with one version picked, only that version's), and 0 for the rest.
5. The name box is filled with the place's name. Change it if you like.
6. Change the rows: type another **Amount**, click the cross at the end of a row to remove it, or click **+ Add** for a row of your own. A row from the game's data keeps its Pokémon and form; only its amount can change.
7. Click **Save Milestone**. Nothing is stored before that, as the line under the button says.

Picking another version or preset fills the amounts again, but keeps every amount you typed, every row you removed and every row you added; the hint beside **Preset** reads "Fills the amounts. An amount you typed yourself is kept." Picking another location starts over, with that place's name in the name box; the version and preset stay.

The rows have the columns **Pokémon**, **When**, **Version** and **Amount**:

- **When**: `morning`, `day` or `night` for when the Pokémon can be met there, or `all day`. It is shown only when some Pokémon of the place can be met at some times of day only, so never on Ruby and Sapphire, which have no clock.
- **Version**: the version that has the Pokémon there, when only one does, or `both`.

A milestone with no location shows both columns empty.

On a game file of one version, a row only the other version has at that place is not a target and does not hold the milestone back.

The milestone remembers its place: that gives its hunt an anchor to start from, and its counts window the shares and the time-of-day pills (see [Watch the counts](#watch-the-counts)).

### From nothing

1. Click **New empty**.
2. Type a name in the **New milestone name** box.
3. Fill in the Pokémon row:
   - The first box: the Pokémon, for example Rattata.
   - **Amount**: how many you want.
   - The form code box (optional): one particular form of the Pokémon.
   - **Require form**: tick it to count only that form.
4. Click **+ Add** for another row. The cross at the end of a row removes it.
5. Click **Save Milestone**.

A milestone made this way has no location: it is hunted where your character stands until you set an anchor for it on that game file (see [Set or clear an anchor](04-hunts/01-wild-encounter.md#set-or-clear-an-anchor)), and its counts window shows no shares and no pills. You can [attach it to a location](#game-files-archived-and-the-location) once it is saved.

### After Save

You see `Saved "NAME" successfully`, with your milestone's name, and the name is now in the second list.

- A name another milestone of the game already has is refused before anything is saved: `A milestone named "NAME" already exists; choose another name.`
- **Amount 0** means the Pokémon is counted but never required, so it never stops the bot ("Amount 0: shown and counted, never a target."). To leave a Pokémon out, set it to 0 or remove its row.
- A milestone with no Pokémon at all is allowed. It is the place to file training encounters, so they stay out of your real milestones' counts.
- To change a milestone, pick it in the second list, edit the rows, and click **Save Milestone**. Its name cannot be changed: make a new one instead.
- A milestone that shows "This milestone is loaded from disk: its Pokémon cannot be edited." came with the bot. Its game files, **Archived** and location can still be changed.

Milestones are stored in the bot's database; see [Your data](06-your-data.md). Always make and change them here.

### Game files, Archived and the location

Under the rows:

- **Game files**: one checkbox for each detected game file of the game. A milestone is offered in a game file's **Milestone** list when that file is ticked. "None ticked: offered on every game file."
- **Archived: hide from the launch form**: ticked, the milestone leaves every **Milestone** list. Its counts and encounters stay, and a counts window can still show it.

Both are saved with **Save Milestone**. A **Wild Encounter** Start, or a plan's Wild Encounter step, with a milestone that is archived or not ticked for the file, picked on a form before the change, is refused when the bot knows the game file's version from its ROM; see [When something goes wrong](07-when-something-goes-wrong.md#the-milestone-is-archived-or-not-ticked-for-this-file). The other actions do not check this.

Above the rows, a saved milestone shows its location:

- `Location: Route 30 · grass`, with **Detach**, which takes the location away.
- `Location: none. No anchor, no shares, no markers.`, with a list of the game's places and **Attach to a location**. For some milestones made before the bot had the game's data, such as `1-4 Route 30`, the bot suggests a place: it is picked already and reads `(suggested)`. Otherwise pick one; the list starts on "Pick a location...".

**Attach to a location** and **Detach** are saved at once. They change no row and no amount, and they keep any changes to the rows you have not saved yet.

## Make a plan

HeartGold and SoulSilver only. A plan is a list of hunts with time rules: the **Plan** action in Bot Control runs it, and it switches between its hunts by the in-game clock. How a running plan behaves is on [Plans: change hunts by the in-game clock](04-hunts/01-wild-encounter.md#plans-change-hunts-by-the-in-game-clock).

1. In the Admin window, click the **Plan Editor** tab.
2. In **Game**, pick **HGSS**.
3. In **Plan**, pick a saved plan to change it, or **New plan...** for a new one, and type its name in the **Plan name** box. A new name another plan has reads `already a plan`, and **Save plan** stays greyed out.
4. Click **+ Add a rule** for each rule. A rule has:
   - **When**: the chips **Morning**, **Day** and **Night**. With none on, it reads "none on: any time".
   - **Days**: the chips **Su** to **Sa**. With none on, it reads "none on: any day".
   - **Do**: **Wild Encounter** or **Wait**, and under it that step's fields. A Wild Encounter step has the same fields as Bot Control's Wild Encounter form (see [What each action asks for](#what-each-action-asks-for)); a Wait step reads "(this step has no options)".
   - **Up** and **Down** move the rule; **Remove** drops it.
5. Fill in **Default: when no rule matches** the same way.
6. In **Plan ends**, pick **Never (runs until cancelled)** or **When every shiny-hunt step is complete**.
7. Click **Save plan**.

Rules are checked from the top, and the first whose time and day both match the game's clock is run; the default runs when none matches. The times are the in-game clock: morning 4:00 to 10:00, day 10:00 to 20:00, night 20:00 to 4:00.

- A step's **Milestone** lists every milestone of the game that is not archived, whatever its game files, in name order, with no **Incomplete only** box. Start checks the milestone against the game file. An archived milestone stays listed while a step uses it.
- No emulator is asked, so **Battler** is a number box, and **Pickup** and **Headbutt** are not greyed out. Start reads your party instead.
- Nothing is written until **Save plan**, **Rename** or **Delete**. **Save plan** saves under the plan's saved name. To give a saved plan another name, type it in the box and click **Rename**, which shows while the box differs from the saved name. **Delete** asks "Delete?"; click **Yes**, or **No** to back out.
- A refused write shows the bot's reason in red, word for word, for example `The default: Wild Encounter needs PhaseName.` (no milestone picked) or `A plan named Johto already exists.`. **RS** is in the **Game** list too, but its plans are refused: `RS has no in-game clock, so it has no plans.`
- While a hunt uses the plan, running or waiting for you, **Save plan**, **Rename** and **Delete** are all refused: `Johto is in use by the hunt on bot #2; cancel that hunt first.`

A plan is saved per game and shared by every HeartGold and SoulSilver game file. Plans are stored in the bot's database; see [Your data](06-your-data.md).

## Watch the counts

1. In the Admin window, click the **Dashboard Config** tab.
2. Under **Add Subscription**, pick the game in the first list.
3. Pick the milestone in the second list.
4. Optional: pick a game file in the third list to count only that file. Leave it on **No game file** to count all of them.
5. Optional: tick **Follows the hunt (and a running plan's steps)** to make a window that follows that game file's hunt (see below). It is greyed out until you pick a game file.
6. Click **Add**.

A counts window opens for that milestone, and it is listed under **Active Subscriptions**. The window shows one row per Pokémon in the milestone, with the columns **Name**, **Milestone Encounters**, **Game Encounters** and **Shinies Caught**. A row at amount 0 reads `tracked` beside its catches. Its counts are read again at each encounter. A row for one Unown letter is named with its letter, such as `Unown (A)`, here and in the dim last row (see below); every other row shows the Pokémon's name, and a row for its female or male form adds ♀ or ♂ after the name. The Milestone Editor shows and saves the plain name. If the bot knows no milestone by that name, the window says `Milestone "NAME" was not found.`

The window's header shows the game file (or the game), the milestone, **Encounters** (every encounter filed under the milestone), **Phase** and **Rate** (encounters an hour), with `—` until there is a number. The window has no footer.

A window with a game file, on a milestone with a location, shows more:

- **The share**, in small text under each Pokémon's name: the part of the encounters there it is expected to make right now, such as `53.8 %`, for the in-game time of day, the Repel and the lead.
- **The time-of-day pills**, beside the name, when some Pokémon of the place can be met at some times of day only: `morning`, `day`, `night`, or `all day`. Ruby and Sapphire have no clock, so their windows have none.
- **Only the Pokémon you can meet right now have a row.** One that is out at another time of day is named in one dim last row instead, by the next time it can be met, such as `2 more at night: Hoothoot, Spinarak`; with two times it reads `<n> more at <time>: <names> · <n> more at <time>: <names>`. One blocked by the Repel or only heard on the radio has no row and is not in that last row. If a Repel blocks every Pokémon there, the window shows the column headings and no rows.
- A Pokémon only the other version has at that place has no row, and is not in the dim last row: it is not met on this game file. The Milestone Editor still lists it.

**Encounters**, **Phase** and **Rate** still count every Pokémon of the milestone. The shares are read again at each encounter and on the counts windows' polling interval (a minute by default), so during a hunt they change at the first encounter after 4:00, 10:00 or 20:00 in-game, within one polling interval, with no move. On HeartGold and SoulSilver, for the first encounter after a launch the bot has not yet read the Repel, so the shares are shown as if there were none, and the window cannot tell you so. Ruby and Sapphire have no clock, so their shares change with the Repel and the lead only, and they are live from the first encounter.

With no hunt running on the game file, the header says `not live (last seen night)`, and the shares and the rows shown are those for the time of day the file was last seen in, with no Repel and no lead. It says just `not live` on Ruby and Sapphire, which have no clock, and on a file the bot has not seen since it started. On such a file, a place whose Pokémon depend on the time of day shows every Pokémon and no shares.

- A headbutt place shows no shares, and every Pokémon.
- A window with no game file shows the pills and every Pokémon, with no shares and no `not live`.
- A milestone with no location, or a game file whose version the bot does not know, shows only the rows: no share, no pill, no last row and no `not live`.

There are two kinds of counts window:

- **Fixed**: shows the one milestone you picked, always. A game file can have any number of these. Its title is the game, the milestone and the game file, for example `HGSS — Route 32 (HG-TimeMove)`.
- **Follows the hunt**: shows whatever milestone the bot on that game file is hunting. When a hunt starts on the file at another milestone, a Chained Encounter moves to its next leg, or a running plan starts a step at another milestone, the same window shows the new milestone's rows: it keeps its title, place and size, and it draws the new rows even while another window covers it. With no hunt running it shows the last milestone hunted, and after a restart it shows that one again. The milestone you pick when you add it is the one it shows until a hunt starts. Its title is the game and the game file, for example `HGSS — HG-TimeMove`, and it is listed with `· follows the hunt` after it.

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

Some hunts stop and wait for you, for example after finding a shiny, or when a trainer challenges your character in Ruby or Sapphire, or on a HeartGold or SoulSilver walk. The entry under **Active Emulators** then offers only **Resume**.

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
- **You see** a red message "NAME: no hunt yet." after Run. **It means** the milestone is met by surfing, fishing or rock smash, which the bot cannot do yet. **Do** pick a grass, cave or headbutt milestone.
- **You see** "Pick a game first" in **Milestone**. **It means** no game is chosen yet. **Do** pick the game first.
- **You see** a red box under **Start Action**. **It means** the dashboard could not reach the bot. **Do** see [When something goes wrong](07-when-something-goes-wrong.md).
- **You see** a red message after Start or Run. **It means** the bot refused, and the message is its reason, word for word. For example, a message of the form "ACTION needs CAPABILITY, which GAME does not have." means that action does not exist for that game. **Do** what the message asks, then try again.
- **You see** "Waiting for the backend" under **Appearance**. **It means** the dashboard is not connected to the bot. **Do** start the bot.

For anything else, see [When something goes wrong](07-when-something-goes-wrong.md).

Next: [Wild and chained encounters](04-hunts/01-wild-encounter.md)
