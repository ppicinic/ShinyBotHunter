<!-- Derived from: ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Emulators/Dolphin/DolphinLauncher.cs, ShinyBotHunter/Emulators/Dolphin/DolphinPad.cs, ShinyBotHunter/Service/BotService.cs, docs/specs/wishmaker-jirachi.md (FR-2, FR-5, FR-9, FR-14, FR-15, FR-20), docs/specs/hgss-state-reader.md (Stale-timeouts), ShinyBotHunter/Controllers/BotController.cs, ShinyBotHunter/Service/BotEventRunner.cs, ShinyBotHunter/Games/GameStateWait.cs, ShinyBotHunter/BotEvents/BattleOrchestratorEvent.cs, ShinyBotHunter/BotEvents/NavigatorEvent.cs, ShinyBotHunter/BotEvents/SpinEncounterEvent.cs, ShinyBotHunter/BotEvents/WishmakerJirachiEvent.cs, ShinyBotHunter/BotEvents/StarterHuntEvent.cs, ShinyBotHunter/BotEvents/MultiStarterHuntEvent.cs, ShinyBotHunter/Emulators/Dolphin/BonusDisc.cs, ShinyBotHunter/Emulators/Dolphin/DolphinGbaWindow.cs, Scripts/deploy-apihawk.ps1, Scripts/deploy-dolphin.ps1, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/tools/ToolManager.cs (the trust question), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/tools/ExternalToolManager.cs (TrustedExtTools), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/MainForm.cs (CheckMayCloseAndCleanup, CloseGame, FlushSaveRAM), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.Common/config/Config.cs (AutosaveSaveRAM off), ApiHawk/CommandServer.cs (flush), ShinyBotHunter/BotEvents/WishmakerJirachiConfig.cs (DsuPort), Dolphin/Config/DSUClient.ini, https://learn.microsoft.com/windows-server/administration/windows-commands/netstat, https://learn.microsoft.com/windows-server/administration/windows-commands/tasklist, ShinyBotHunter/BotEvents/OrchestratorEvent.cs (Resume, DecideOverworldAction), docs/guide/04-hunts/05-wishmaker-jirachi.md, docs/guide/01-install.md (Install from a release), ShinyBotHunter/BotEvents/RunEncounterEvent.cs, ShinyBotHunter/BotEvents/RsBattleEvent.cs, ShinyBotHunter/Service/BotService.cs (WildRefusalAsync, DisabledByCapabilities, StartWildEncounter), ShinyBotHunter/BotEvents/RsNavigatorEvent.cs (WalkToAsync, WalkAsync, HealAsync, Stop), ShinyBotHunter/BotEvents/OrchestratorEvent.cs (ResumeAsync, DecideOverworldAction), ShinyBotHunter/Games/Rs/RsProfile.cs (NoTrainingAddresses), ShinyBotHunter/Util/Overworld.cs (WaitForReadyAsync) -->
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
  2. Run the ApiHawk deploy script exactly as [Install](01-install.md) gives it. From a release install, add the `-BotServerDll` parameter as under [Install from a release](01-install.md#install-from-a-release-instead-of-building).
  3. Check its output: `Trusted in config.ini` (and `Trusted in config_sound_on.ini` if you have that file), then `Deployed <n>kb, SHA512:<hash>`.
  4. Start again.

BizHawk's question has the title `Confirm loading` and says `Trust this external tool to run on your device?`, then `If you're not 100% sure of what the tool will do, choose "No".` If you answer it yourself on an emulator you opened, **Yes** also records the trust for that build. The script is still the fix for emulators the bot starts.

If the deploy script itself stops:

| You see | It means | Do |
|---|---|---|
| `EmuHawk is running. Close it first: ...` | An EmuHawk is still open. | Close every EmuHawk and run the script again. |
| `ApiHawk build failed.` | The bot's tool did not build. | Check the build steps in [Install](01-install.md). From a release install this does not happen, because the script is given the prebuilt DLL and builds nothing. |
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

### A Ruby or Sapphire wild hunt is not ready

The bot reads the game when you press Run on a Ruby or Sapphire **Wild Encounter**. The [Ruby and Sapphire wild encounters](04-hunts/08-rs-wild-encounter.md) page has more on each.

| You see | It means | Do |
|---|---|---|
| `Close any menu or message and stand in the field.` | A menu, message or script was on screen. | Close it until your character can walk, and start again. |
| `The player is not the first object on this map, which the bot watches as it turns.` | The bot checks that your character is the first object on the map, and on this map it is not. | |
| `The party could not be read; try again once the game is in the field.` | The bot could not read your party. | Wait until your character is in the field, and start again. |
| `The first Pokémon in the party has fainted.` | Your first Pokémon has no HP left, or the party is empty. | Heal it, or put a Pokémon that can battle first, and start again. |
| `Ruby and Sapphire have no Headbutt trees.` | Encounter Type Headbutt was sent, though it is greyed out. | Pick Spin or Run. |
| `Pickup is not available on Ruby and Sapphire.` | Pickup was sent ticked, though it is greyed out. | Leave Pickup unticked. |
| `Training needs a US Ruby or Sapphire ROM (game code AXVE or AXPE).` | Train Type Standard or Switch was sent for a ROM that is not the US Ruby or Sapphire. The bot only knows where those two keep what training reads. | Leave Train Type at None, or train on a US ROM. |

### Something is missing from the Start form

| You see | It means | Do |
|---|---|---|
| `A milestone must be chosen for the shiny starter hunt.` | No milestone was picked. | Pick a milestone. |
| `A milestone must be chosen for the Wishmaker Jirachi Hunt.` | No milestone was picked. | Pick a milestone. |
| `A game must be chosen for the Wishmaker Jirachi Hunt.` | No game was picked. | Pick a game. |
| `PhaseName cannot be null for the Togepi egg hunt.` | No milestone was picked. | Pick a milestone. |
| `PhaseName cannot be null for the Primo egg hunt.` | No milestone was picked. | Pick a milestone. |
| `PhaseName cannot be null for the static encounter hunt.` | No milestone was picked. | Pick a milestone. |
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
  2. Run the Dolphin deploy script exactly as [Install](01-install.md) gives it. From a release install, add the `-AppSettings` and `-SecretsId` parameters as under [Install from a release](01-install.md#install-from-a-release-instead-of-building).
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

## A wild hunt stops with a sentence in the backend's window

On the dashboard, the bot shows as waiting, with **Resume**. A word in angle brackets stands for the value the bot fills in.

| You see | It means | Do |
|---|---|---|
| `[Spin] The player moved while turning. At (<x>, <y>), started on (<x>, <y>).` | Before a round of turning, your character was not on the tile the hunt started on. Both games. | Put your character back on the start tile, and press **Resume**. |
| `[Run] The player is off the run line. At (<x>, <y>); the line is <x or y> +-<range> from (<x>, <y>).` | Before a walk, your character was not on the line, or was past one of its ends. Both games. | Put your character back on the line, and press **Resume**. |
| `[Spin] Something other than a wild battle started. Waited for a battle within 15 s of the field leaving free; the game state stayed <state>`, or the same starting with `[Run]` | Something stopped your character walking, such as a message or a script, and no battle started within 15 s. Both games; on Ruby and Sapphire a message or a script is looked at after 4 s instead and ends in one of the `[RsBattle]` lines below. | Finish what the game shows yourself until your character can walk, put it back on the start tile or the line, and press **Resume**. |
| `[RsBattle] stopped: A trainer battle started on <map>.` followed by `trainer <number>` or `battle type 0x<flags>`, and what the game showed; the same sentence arrives once on Discord | Ruby and Sapphire. A trainer challenged your character. The bot pressed nothing and does not fight trainers. | Press through the speech and win the battle yourself. Once your character stands in the field, press **Resume**: the hunt carries on. If you press **Resume** while the speech or the battle is still waiting for you, the bot goes back to waiting with `[Orchestrator] Resume: Waited for the free field after the trainer stop; the game state stayed <state>` and sends no second message; finish, and press **Resume** again. |
| `[RsBattle] stopped: Something other than a wild battle started.` followed by what the game showed, or by `battle type 0x<flags>, <state>` | Ruby and Sapphire. The field locked (a message, a script), it was not a trainer, and no battle started within about 15 s of the lock; or, with `battle type`, the battle is not a wild one, such as a Safari Zone battle. | End the battle or close the message yourself, put your character back on the start tile or the line, and press **Resume**. |
| `[RsBattle] stopped: The first Pokémon fainted. <state>` | Ruby and Sapphire. Your first Pokémon fainted in the battle, or, when training, the Pokémon fighting fainted with no other able to battle and the battle was lost. | Finish the battle yourself, put your character back on the start tile or the line, and press **Resume**. |
| `[RsBattle] stopped: Could not run from this battle. <state>` | Ruby and Sapphire. The bot pressed RUN 12 times in this battle without getting away. A wild Pokémon with Arena Trap, Shadow Tag or Magnet Pull can do this. | Finish the battle yourself, put your character back on the start tile or the line, and press **Resume**. |
| `[RsBattle] stopped: The wild Pokémon's record failed its checksum for 2 s. <state>` | Ruby and Sapphire. The wild Pokémon's record in the game's memory kept failing its checksum, so the bot could not read it. | Finish the battle yourself, put your character back on the start tile or the line, and press **Resume**. |

After a lost battle the game sends your character to a Pokémon Center, so put it back on the start tile or the line before you press **Resume**; otherwise Resume stops again with the `[Spin]` or `[Run]` sentence at the top of this table.

## A Ruby or Sapphire hunt stops while training or walking

On the dashboard, the bot shows as waiting, with **Resume**. These come from Ruby and Sapphire hunts with Train Type Standard or Switch, which fight and walk to a Pokémon Center to heal, and from any Ruby and Sapphire hunt as it walks back to its start tile to finish. The [Ruby and Sapphire wild encounters](04-hunts/08-rs-wild-encounter.md#training) page explains training and the walks.

A `<place>` is the game's name for the map and a tile, for example `Route101 (14, 4)` or `OldaleTown_PokemonCenter_1F (7, 4)`. Most `[Navigator]` lines end with what the bot last read, in brackets.

**Before you press Resume after any of these, walk your character back to the tile the hunt started on.** A stop on a walk or at the nurse leaves your character somewhere else, and so does a lost battle. Resume checks the start tile (Spin) or the line (Run) before the next encounter, and stops again with `[Spin] The player moved while turning.` or `[Run] The player is off the run line.` if your character is not there. If a Pokémon still needs healing, the bot walks to the Pokémon Center again by itself.

### On the walk

| You see | It means | Do |
|---|---|---|
| `[Navigator] stopped: No route to a Pokémon Center on foot from <place>.` | The bot found no way from where your character stands to a Pokémon Center. It uses no HM, so a Pokémon Center behind a Cut tree, water or a rock is out of reach, and so is one behind a person who still blocks the path at this point in the story. | Heal yourself, walk back to the start tile and press **Resume**. The same stop comes back at the next heal, so to train, start the hunt from a tile with a Pokémon Center you can walk to without HMs. |
| `[Navigator] stopped: No route back to the start tile <place> from <place>.` | The bot found no way from where your character stands back to the tile the hunt started on, under the same rules. | Walk back to the start tile yourself and press **Resume**. |
| `[Navigator] stopped: Stuck walking to <place>.` | One straight stretch of the walk did not end on the tile it was heading for within 20 s. Something was in the way, such as a person walking into the path. A message or a script does not end this way: it locks the field, and see the next row. | Walk back to the start tile and press **Resume**. |
| `[Navigator] stopped: The last step to <place> did not end (running state <n>, tile transition <n>).` | Your character reached the last tile of the walk, but the game did not show it standing still within 20 s. | Look at the emulator, walk back to the start tile and press **Resume**. |
| `[Navigator] stopped: Waited for the overworld ready; the game state stayed <state>` | Before planning a walk, the bot waited for your character to be free to walk, and for 10 s nothing changed. | Close what the game shows, walk back to the start tile and press **Resume**. |
| `[RsBattle] stopped: Something other than a wild battle started.` followed by what the game showed, on a walk | The field locked on the walk (a message, a script) and stayed locked for 4 s with your character on the same tile. The walk handed it to the battle event, which found no trainer, and no battle started within about 15 s of the lock. | Close what the game shows and press **Resume**: the bot plans the walk again from where your character stands. |
| `[RsBattle] stopped: A trainer battle started on <map>.` on a walk | A trainer who has not been beaten challenged your character on the way. | As for the same line in the table above: press through the speech, win, and press **Resume**. The bot plans the walk again from where your character stands, so you need not walk back first. |

### At the nurse

| You see | It means | Do |
|---|---|---|
| `[Navigator] stopped: Waited for <what>; the game state stayed <read>.` | At the Pokémon Center counter, the nurse's talk did not reach its next step, and nothing changed for 10 s. `<what>` is one of: `the player at rest on the counter tile`, `the player at rest on the counter tile facing up`, `the facing up after SpinUp`, `the field locked after the talk`, `a page break or the Yes/No with its counter at 5`, `the window leaving <n>`, `the Yes/No task gone after Yes`, `a page break or the script's last button wait`, `the lock 0 and status 2 after the last line`. | Finish talking to the nurse yourself, walk back to the start tile and press **Resume**. |
| `[Navigator] stopped: The party's HP was not full after the nurse: <name> <hp>/<max>, ….` | The bot talked to the nurse, but afterwards a Pokémon in the party was not at full HP. | Heal at the counter yourself, walk back to the start tile and press **Resume**. |

### In a battle while training

| You see | It means | Do |
|---|---|---|
| `[RsBattle] stopped: Waited for a battle message, the action menu, an evolution text wait or yes/no, Use next POKéMON?, the party menu after a faint, or the free field; the game state stayed <state>` | Between presses, the game showed nothing the bot acts on for 30 s. A forget-a-move screen the bot did not open ends this way. So does "Use next POKéMON?" in a hunt that is not training, which does not answer it. | Finish the battle yourself, heal, walk back to the start tile and press **Resume**. |
| `[RsBattle] stopped: A Pokémon fainted, and running from the battle failed after the fastest Pokémon left was sent out.` followed by `runTries <before> -> <after>` and what the game showed; the same sentence arrives once on Discord | The Pokémon fighting fainted with another left. The bot answered No at "Use next POKéMON?" and did not get away, sent out the fastest Pokémon left and picked RUN, and did not get away again. Nothing more was pressed. `(did not rise)` after the numbers means the game did not count that Run as a try. | Finish the battle yourself. Once your character stands in the field, press **Resume**: the bot carries on, and heals first if it needs to. If you press **Resume** while the battle is still on, the bot goes back to waiting as after a trainer stop, with no second message. |
| `[RsBattle] stopped: Waited for <what>; the game state stayed <state>`, where `<what>` is one of the waits listed below | A press while fighting, switching or answering a new move was not followed by what it should change, or a screen did not come, and nothing in the game changed for 30 s. | Finish what the game shows yourself (the battle, the move question), walk back to the start tile and press **Resume**. |

The `<what>` of those stops, as the bot writes them. Party slots and move slots in them count from 0: your first Pokémon is slot 0, the dashboard's Battler 2 is slot 1, and a Pokémon's first move is move 0. A direction is Left, Up, Right or Down.

| `<what>` | The bot was |
|---|---|
| `the action cursor on FIGHT` | waiting for the battle menu with the cursor on FIGHT |
| `the move list after BattleConfirm on FIGHT` | opening the move list |
| `cursor <n> after <direction> on the move list <n> -> <n>` | moving to the chosen move |
| `the screen leaving the move list after BattleConfirm on move <n>` | choosing the move |
| `page 2 after level-up box page 1 for slot <n>: BattleConfirm`, `the box leaving page 2 after level-up box page 2 for slot <n>: BattleConfirm` | closing a level-up box |
| `the prompt leaving Delete a move? after BattleConfirm (Yes) at Delete a move?`, `the prompt leaving Delete a move? after B at Delete a move?` | answering "Delete a move?" as you chose on Discord |
| `the forget-a-move screen taking input`, `the cursor after Down on the forget-a-move screen <n> -> <n>`, `the summary task closing after BattleConfirm on forget slot <n>`, `the forget-a-move screen to close` | forgetting the move you chose |
| `Stop learning?`, `the prompt leaving Stop learning? after BattleConfirm (Yes) at Stop learning?` | declining the new move |
| `cursor <n> after <direction> on the action menu <n> -> <n>`, `the screen leaving the action menu after BattleConfirm on POKéMON (switch try <n> of 3)` | opening the party list for the switch |
| `the party list to take keys (the switch to slot <n>)`, `the party cursor leaving <n> after Down on the party list from <n>`, `the SHIFT submenu after BattleConfirm on party slot <n>`, `the submenu closing after BattleConfirm on SHIFT at <state>` | choosing the Battler and SHIFT |
| `the next action menu after SHIFT (the switch to slot <n>)` | waiting for the Battler to come out; the game refusing the switch ends here |
| `the script leaving Use next POKéMON? after B (No) at Use next POKéMON? for slot <n>` | answering No at "Use next POKéMON?" after a faint |
| `the party list to take keys (the send-out to slot <n>)`, `the party cursor leaving <n> after Down on the party list from <n>`, `the SEND OUT submenu after BattleConfirm on party slot <n>`, `the submenu closing after BattleConfirm on SEND OUT at <state>` | sending out the fastest Pokémon left, after "Can't escape!" |
| `the next action menu after SEND OUT (the send-out to slot <n>)` | waiting for that Pokémon to come out |
| `the battle window to leave <n>` | pressing A at a battle message or an evolution text |
| `the cursor on RUN`, `the cursor on RUN after the missing press`, `the screen to leave the action menu` | running from the battle: when training, because the Pokémon fighting is too hurt or out of damaging PP, the calculator found no move, the switch failed, or a Pokémon fainted |

The two refusals training can meet when you press **Run**, `Training needs a US Ruby or Sapphire ROM (game code AXVE or AXPE).` and `Pickup is not available on Ruby and Sapphire.`, are under [A Ruby or Sapphire wild hunt is not ready](#a-ruby-or-sapphire-wild-hunt-is-not-ready).

## A hunt stops with "Waited for ...; the game state stayed ..."

This is a stale timeout.

- **You see:** in the backend's window, a line that starts with `[Battle]`, `[Navigator]`, `[Spin]`, `[Run]`, `[RsBattle]` or `[Orchestrator]` and contains `Waited for <what>; the game state stayed <state>`. On the dashboard, the bot shows as waiting, with **Resume**.
- **It means:** the game did not reach the screen the bot expected, and nothing in the game changed for the time limit. The bot stops and waits for you rather than pressing on. This happens in wild and chained encounters in HeartGold and SoulSilver, and in wild encounters in Ruby and Sapphire, training and walking included. The Ruby and Sapphire training and walk stops are listed one by one under [A Ruby or Sapphire hunt stops while training or walking](#a-ruby-or-sapphire-hunt-stops-while-training-or-walking).

The time limits are:

| The bot was waiting for | Time limit |
|---|---|
| In a HeartGold or SoulSilver battle, the first battle menu once the battle has started | 30 s |
| The end of a turn | 20 s |
| A prompt, such as experience or a new move | 15 s |
| The messages after a new move is learned | 30 s |
| An evolution to finish | 30 s |
| The player to be back on the map | 10 s |
| A warp to the next map | 15 s |
| A script in the field, such as the nurse's | 20 s |
| A battle, once the field stops being free while turning or walking for an encounter, whether or not the game changes | 15 s |
| In a Ruby or Sapphire battle, the next screen | 30 s |
| In Ruby or Sapphire, the field free before a walk is planned | 10 s |
| In Ruby or Sapphire, the nurse's next step | 10 s |
| In Ruby or Sapphire, the free field when you press **Resume** after a trainer stop | 10 s |

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
