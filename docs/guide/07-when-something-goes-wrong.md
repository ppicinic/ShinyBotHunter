<!-- Derived from: ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Emulators/Dolphin/DolphinLauncher.cs, ShinyBotHunter/Emulators/Dolphin/DolphinPad.cs, ShinyBotHunter/Service/BotService.cs, docs/specs/wishmaker-jirachi.md (FR-2, FR-5, FR-9, FR-14, FR-15, FR-20), docs/specs/hgss-state-reader.md (Stale-timeouts), ShinyBotHunter/Controllers/BotController.cs, ShinyBotHunter/Service/BotEventRunner.cs, ShinyBotHunter/Games/GameStateWait.cs, ShinyBotHunter/BotEvents/BattleOrchestratorEvent.cs, ShinyBotHunter/BotEvents/NavigatorEvent.cs, ShinyBotHunter/BotEvents/SpinEncounterEvent.cs, ShinyBotHunter/BotEvents/WishmakerJirachiEvent.cs, ShinyBotHunter/BotEvents/StarterHuntEvent.cs, ShinyBotHunter/BotEvents/MultiStarterHuntEvent.cs, ShinyBotHunter/Emulators/Dolphin/BonusDisc.cs, ShinyBotHunter/Emulators/Dolphin/DolphinGbaWindow.cs, Scripts/deploy-apihawk.ps1, Scripts/deploy-dolphin.ps1, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/tools/ToolManager.cs (the trust question), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/tools/ExternalToolManager.cs (TrustedExtTools), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/MainForm.cs (CheckMayCloseAndCleanup, CloseGame, FlushSaveRAM), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.Common/config/Config.cs (AutosaveSaveRAM off), ApiHawk/CommandServer.cs (flush), ShinyBotHunter/BotEvents/WishmakerJirachiConfig.cs (DsuPort), Dolphin/Config/DSUClient.ini, https://learn.microsoft.com/windows-server/administration/windows-commands/netstat, https://learn.microsoft.com/windows-server/administration/windows-commands/tasklist, ShinyBotHunter/BotEvents/OrchestratorEvent.cs (Resume, DecideOverworldAction), docs/guide/04-hunts/05-wishmaker-jirachi.md -->
# When something goes wrong

Part of the [ShinyBotHunter guide](README.md)

Each problem below has three parts: what you see, what it means, and what to do. Text in `code` is quoted exactly as the bot shows it; a word in angle brackets, like `<name>`, stands for the value the bot fills in.

## Where do the bot's messages appear?

- **When a Start is refused**, the dashboard shows the bot's message as it is, under the action you started. [The dashboard](03-dashboard.md) shows where the actions are.
- **Everything else** appears in the backend's window.
- **When a running hunt fails**, the backend's window shows `[BotService] Event '<name>' crashed. State: <state>`, followed by the error. On the dashboard, the bot goes idle, or a hunt that starts its own emulators disappears.

If the dashboard shows only a general server error, look in the backend's window for the full message.

## EmuHawk stops on a question about an external tool

This is the ApiHawk trust prompt.

- **You see:** EmuHawk waiting on a yes/no question before the game starts. Then:
  - In a starter hunt, the backend's window shows `[StarterHunt] An emulator stopped with an error: Emulator for <name> was not responding <n>ms after launch, so no macro was sent (<detail>).` The bot closes that emulator itself, and the rest of the hunt carries on without it.
  - In the Jirachi hunt, the hunt stops with `EmuHawk for <name> was not responding <n> ms after launch (<detail>).`
- **It means:** BizHawk only loads the bot's tool without asking when it trusts that exact build. A new build of the tool has to be trusted again. An emulator the bot started never gets its answer, so the tool never starts and the bot gives up on it.
- **Do:**
  1. Close every EmuHawk.
  2. Run the ApiHawk deploy script exactly as [Install](01-install.md) gives it.
  3. Check its output: `Trusted in config.ini` (and `Trusted in config_sound_on.ini` if you have that file), then `Deployed <n>kb, SHA512:<hash>`.
  4. Start again.

BizHawk's question has the title `Confirm loading` and says `Trust this external tool to run on your device?`, then `If you're not 100% sure of what the tool will do, choose "No".` If you answer it yourself on an emulator you opened, **Yes** also records the trust for that build. The script is still the fix for emulators the bot starts.

If the deploy script itself stops:

| You see | It means | Do |
|---|---|---|
| `EmuHawk is running. Close it first: ...` | An EmuHawk is still open. | Close every EmuHawk and run the script again. |
| `ApiHawk build failed.` | The bot's tool did not build. | Check the build steps in [Install](01-install.md). |
| `Config trust update failed.` | The script could not run the Python step that writes BizHawk's settings. | Check that `python` runs in a terminal, then run the script again. |

## The emulator window never moves into place

- **You see:** in the backend's window, `Timed out waiting for EmuHawk main window.` about a minute after the launch. The dashboard may show only a general server error.
- **It means:** EmuHawk did not open its window within a minute. This happens when many emulators start at once.
- **Do:** start fewer emulators at once. If it happens with one, check the BizHawk folder and ROM folder settings in [Configure](02-configure.md).

## A hunt says there is no save

This is the case where BizHawk's save file (its SaveRAM) does not appear.

- **You see:** one of these, when you start the Jirachi hunt:
  - `No save at <path>: start the game in BizHawk and save once before hunting Jirachi.`
  - `The save at <path> is <n> bytes, shorter than a 131072-byte Gen 3 save.`
  - `The save at <path> has no complete save block: save again in-game before hunting Jirachi.`
- **It means:** the save file is not in BizHawk's SaveRAM folder, or it is not a full save. The path in the message is where the bot looks: the bot's BizHawk names the save after the ROM file. A BizHawk that is not the bot's own build names it differently, so the bot does not find it.
- **Do:**
  1. Open the game in the bot's BizHawk.
  2. Save in the game.
  3. Close EmuHawk.
  4. Start the hunt again.

BizHawk writes the save file when the game closes: when you close EmuHawk, or when it loads another game. The Jirachi hunt also asks it to write straight away, through the bot's tool. So save in the game, then close EmuHawk, before starting the hunt.

## A Start is refused

When you start an action and the bot refuses it, the dashboard shows one of these messages. Nothing has started; fix the cause and start again.

### The game cannot run that action

| You see | It means | Do |
|---|---|---|
| `<action> needs <capability>, which <game> does not have.` | That game does not support the action. The dashboard only offers the actions a game supports, so you should not normally see this. | Pick a game the action supports. |
| `Shiny Starter Hunt needs HasSoftResetStarterHunt; '<game>' is <name>, which has no game profile.` | The bot does not know that game, so it cannot run the starter hunt on it. | Pick a game the starter hunt supports. |
| `Wishmaker Jirachi Hunt runs only on a tested Ruby or Sapphire ROM (revision 2); <game> is <name>.` | The Jirachi hunt runs only on revision 2 of Ruby or Sapphire. | Use a revision 2 Ruby or Sapphire ROM. |

### The Jirachi hunt's save or files are not ready

| You see | It means | Do |
|---|---|---|
| `The save at <path> has no Pokédex yet: the Bonus Disc needs one.` | The game has not reached the Pokédex yet. | Play until you have the Pokédex, save, and start again. |
| `The save at <path> has a full party: leave a slot free for Jirachi.` | Jirachi needs a free party slot. | Put a Pokémon in the PC, save, and start again. |
| `Dolphin is not at <path>: check DolphinBaseDirectory.` | The Dolphin folder setting is wrong. | Fix the Dolphin folder in [Configure](02-configure.md). |
| `The GBA BIOS is not at <path>: check GbaBiosPath.` | The GBA BIOS setting is wrong. | Fix the GBA BIOS path in [Configure](02-configure.md). |
| `The Colosseum Bonus Disc is not at <path>: check ColosseumBonusDiscPath.` | The Bonus Disc setting is wrong. | Fix the Bonus Disc path in [Configure](02-configure.md). |

### Something is missing from the Start form

| You see | It means | Do |
|---|---|---|
| `A milestone must be chosen for the shiny starter hunt.` | No milestone was picked. | Pick a milestone. |
| `A milestone must be chosen for the Wishmaker Jirachi Hunt.` | No milestone was picked. | Pick a milestone. |
| `A game must be chosen for the Wishmaker Jirachi Hunt.` | No game was picked. | Pick a game. |
| `PhaseName cannot be null for the Togepi egg hunt.` | No milestone was picked. | Pick a milestone. |
| `PhaseName cannot be null for Wild Pokemon Encounter Event.` | No milestone was picked. | Pick a milestone. |
| `At least one starter entry (game + starter) is required for the shiny starter hunt.` | The starter hunt has no starters to hunt. | Add at least one game and starter. |
| `Every starter entry must pick a game.` | A starter row has no game. | Pick a game in every row. |
| `Every starter entry must pick a starter.` | A starter row has no starter. | Pick a starter in every row. |
| `No ROM named '<game>' was found.` | The game is not in the ROM folder. | Check the ROM folder in [Configure](02-configure.md). |
| `Emulators must be between 1 and <n> for <console>.` | Too many or too few emulators were asked for. | Pick a number in that range. |
| `EncounterType must be supplied for Wild Pokemon Encounter Event` | No encounter type was picked. | Pick an encounter type. |
| `X and Y range must be non-negative values` | A range is below zero. | Enter a range of zero or more. |
| `A x or y range must be supplied for Run Encounter Event` | No range was entered. | Enter an x range or a y range. |
| `Only x or y range must be supplied, not both.` | Both ranges were entered. | Enter only one of them. |
| `TargetLevel must be a positive value when a train mode is enabled.` | Training needs a level to stop at. | Enter a target level above zero. |
| `A train mode must be supplied when TargetType is NoTarget, otherwise the event has no stopping condition.` | With no target, the hunt would never stop. | Pick a target, or a train mode. |
| `SwitchSlot must be between 2 and 6 for Switch train mode.` | The switch slot is out of range. | Pick a slot from 2 to 6. |
| `At least one leg must be supplied for Chained Encounter.` | The chained encounter has no legs. | Add at least one leg. |
| `Party is full (<n>/<max>). The egg needs a free party slot or the NPC will not hand it over. Deposit or release a pokemon before starting this hunt.` | The egg needs a free party slot. | Put a Pokémon in the PC, save, and start again. |

### Something else is already running

| You see | It means | Do |
|---|---|---|
| `A Wishmaker Jirachi Hunt is already running.` | Only one Jirachi hunt can run. | Wait for it, or cancel it first. |
| `<action> is not a valid action to run currently.` | That bot is busy, or cannot run that action now. | Wait for the bot to be idle, or cancel what it is doing. |
| `There is not bot at position: <n>` | No emulator is at that position any more. | Refresh the dashboard and pick a running emulator. |

Two refusals come back as a plain error, so the dashboard may show only a general server error. Their text is in the backend's window:

| You see | It means | Do |
|---|---|---|
| `The shiny starter hunt manages its own emulators, so it has to be the only thing running.` | Other bots are running. | Stop the other bots first. |
| `Multi shiny hunting in HGSS can be the only active bot[s].` | Other bots are running. | Stop the other bots first. |

## Dolphin does not respond to the bot's presses

This is the case where Dolphin is not connected to the bot's virtual controller.

- **You see:** in the backend's window, `Bonus Disc: <n> presses did not reach the Jirachi menu entry; last <bytes>.` The same message can end in `the gift flow` or `the power prompt` instead of `the Jirachi menu entry`.
- **It means:** the presses never reached the disc. The bot presses buttons through a virtual controller on port 26760, and Dolphin has to be set up to listen to it. Two settings files that the Dolphin deploy script installs do that.
- **Do:**
  1. Close Dolphin.
  2. Run the Dolphin deploy script exactly as [Install](01-install.md) gives it.
  3. Check its output: `Deployed Dolphin config to <folder> with GBA BIOS <path>`.
  4. Start the hunt again.

If another program holds the port:

- **You see:** in the backend's window, `[BotService] Event '<name>' crashed` followed by a `System.Net.Sockets.SocketException`, when the Jirachi hunt reaches the disc.
- **It means:** another program holds UDP port 26760.
- **Do:**
  1. In a terminal, run `netstat -ano -p udp | findstr :26760`. The last number on the line it prints is the program's process id.
  2. Run `tasklist /fi "PID eq <number>"` with that number. It prints the program's name.
  3. Close that program and start the hunt again.

  Leave `DsuPort` as it is: the Dolphin settings the install script set up use 26760.

If the deploy script itself stops:

| You see | It means | Do |
|---|---|---|
| `Dolphin is running. Close it first: ...` | Dolphin is still open. | Close Dolphin and run the script again. |
| `Dolphin.exe not found in <folder>; ...` | The Dolphin folder setting is wrong. | Fix the Dolphin folder in [Configure](02-configure.md). |
| `GBA BIOS not found at <path>; ...` | The GBA BIOS setting is wrong. | Fix the GBA BIOS path in [Configure](02-configure.md). |

## Dolphin does not start, shows the wrong disc, or will not close

Each message appears in the backend's window and stops the Jirachi hunt.

| You see | It means | Do |
|---|---|---|
| `Timed out after <n> s waiting for Dolphin's memory mapping dolphin-emu.<id>.` | Dolphin started but did not get ready in time. | Close Dolphin, check the Dolphin folder in [Configure](02-configure.md), and start again. |
| `Timed out after <n> s waiting for game id PC6E01 at 0x80000000; last read '<id>'.` | The disc Dolphin loaded is not the Colosseum Bonus Disc. | Check the Bonus Disc path in [Configure](02-configure.md). |
| `Timed out after <n> s waiting for <what> of Dolphin process <id>; its visible windows: [<list>].` | A Dolphin window the bot needs, such as the GBA window, did not appear. | Close Dolphin, run the Dolphin deploy script again, and start again. |
| `Dolphin process <id> did not exit within 10 s of two kills.` | Dolphin would not close. | Close Dolphin yourself, from Task Manager if needed, then start again. |

## A hunt stops with "Waited for ...; the game state stayed ..."

This is a stale timeout.

- **You see:** in the backend's window, a line that starts with `[Battle]`, `[Navigator]` or `[Spin]` and contains `Waited for <what>; the game state stayed <state>`. On the dashboard, the bot shows as waiting, with **Resume**.
- **It means:** the game did not reach the screen the bot expected, and nothing in the game changed for the time limit. The bot stops and waits for you rather than pressing on. This happens in wild and chained encounters in HeartGold and SoulSilver.

The time limits are:

| The bot was waiting for | Time limit |
|---|---|
| A battle to start | 30 s |
| The end of a turn | 20 s |
| A prompt, such as experience or a new move | 15 s |
| The messages after a new move is learned | 30 s |
| An evolution to finish | 30 s |
| The player to be back on the map | 10 s |
| A warp to the next map | 15 s |
| A script in the field, such as the nurse's | 20 s |

- **Do:** look at that emulator to see what the game shows. Then press **Resume** on the dashboard, or **Cancel**.

**Resume** does not pick up where the game stopped. It goes back to the field: the next encounter, or the walk to or from the Pokémon Center it was on, worked out again from where your character stands. So first finish what the game shows yourself (end or run from the battle, close any message) until your character can walk. If a Pokémon you are hunting is on screen, catch it first ([Wild and chained encounters](04-hunts/01-wild-encounter.md)).

## The Jirachi hunt stops in its save or result step

Each message appears in the backend's window and stops the hunt. For each one: look at the emulator to see what the game shows, then start the hunt again. The [Wishmaker Jirachi page](04-hunts/05-wishmaker-jirachi.md) explains each step of the hunt.

| You see | It means |
|---|---|
| `In-game save: no complete block with counters above <n> within <n> ms; last seen <counters>.` | The in-game save did not finish within 40 seconds. |
| `The start menu did not open after <n> Start presses (the cursor never moved); last start menu <menu>.` | The start menu would not open, after up to 12 presses of Start. |
| `Start-menu cursor would not settle on position <n> after <n> presses (<menu>). Refusing to press A on whatever is highlighted.` | The bot could not move the start-menu cursor to the entry it needed, so it stopped rather than press the wrong one. |
| `Jirachi not in the RAM party: party size <n>, slot <n> is species <n>.` | After the transfer, Jirachi was not in the party. |
| `Jirachi PID <x> is not the <y> that seed <s> predicts: the disc read a different save than the hunt wrote. Stopping.` | The Bonus Disc read a different save from the one the hunt just wrote. |
| `Bonus Disc: timed out after <n> s waiting for the transfer (the .sav to change and phase >= <n>); .sav mtime <time> before the reset, <time> last; <bytes>.` | The Bonus Disc did not finish sending Jirachi to the game within 90 seconds. |

The Bonus Disc steps have their own time limits: about a minute to reach the Jirachi entry and the gift, about a minute to reach the "turn on the power" prompt, and 90 seconds for the transfer itself.

Nothing else by hand. Starting the hunt again checks your save and the Dolphin files first (the refusals under [The Jirachi hunt's save or files are not ready](#the-jirachi-hunts-save-or-files-are-not-ready)). If any cycle ever found a shiny, copy `<game>-WISHMKR.SaveRAM` somewhere safe first, as the [Wishmaker Jirachi page](04-hunts/05-wishmaker-jirachi.md) warns.

## Discord problems

See [Discord](05-discord.md).

Next: [Updating and versions](08-updating.md)
