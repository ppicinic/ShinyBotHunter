<!-- Derived from: docs/specs/wishmaker-jirachi.md, ShinyBotHunter/Configs/Events/WishmakerJirachi.json, ShinyBotHunter/BotEvents/WishmakerJirachiEvent.cs, ShinyBotHunter/BotEvents/WishmakerJirachiConfig.cs, ShinyBotHunter/Games/EventRegistry.cs ("Wishmaker Jirachi Hunt"); read only: ShinyBotHunter/Service/BotService.cs (StartWishmakerJirachiAsync), ShinyBotHunter/Service/RomInfoService.cs (the Game list), PokemonDashboard/src/renderer/src/components/admin/TopLevelActionForm.tsx, PokemonDashboard/src/renderer/src/components/admin/ActionParamFields.tsx, PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts, ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs (CloseMainWindow), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/MainForm.cs (CheckMayCloseAndCleanup, CloseGame, FlushSaveRAM), docs/guide/03-dashboard.md -->
# Wishmaker Jirachi hunt

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

- The Pokémon Colosseum Bonus Disc gives you a Jirachi. Whether it is shiny is decided by your save.
- So the bot keeps playing your game for a random while, between 2 and 20 seconds, and saving it. Each save makes the disc give a different Jirachi.
- The bot never edits your save. It only plays the game and saves, as you would.
- One cycle takes about three and a half to four minutes. A cycle that takes more than five minutes is noted in the log, and the hunt carries on.

## What do you need before you start?

- Your own Ruby or Sapphire ROM, version 1.2, in the ROM folder. Only those ROMs appear in this hunt's Game list.
- Your own save for that ROM, made in BizHawk. BizHawk names the save after the ROM file and keeps it in the `GBA\SaveRAM` folder inside your BizHawk folder.
- In that save:
  - you already have the Pokédex;
  - at least one party slot is free, for the Jirachi;
  - you have saved in-game at least once.
- Dolphin, the GBA BIOS and the Bonus Disc image, set up as on the [Install](../01-install.md) page.
- The three Dolphin settings filled in, as on the [Configure](../02-configure.md) page.

To make that save:

1. On the dashboard, press **Launch Bot** with **Game** set to your ROM, as in [Launch an emulator](../03-dashboard.md#launch-an-emulator).

   **Expected result:** BizHawk opens the game.

2. Play until you have the Pokédex, with a party slot free.
3. Save in the game.
4. **Cancel** the emulator's entry on the dashboard, which closes BizHawk, as in [Stop an action or close an emulator](../03-dashboard.md#stop-an-action-or-close-an-emulator).

   **Expected result:** `<game>.SaveRAM` is in the `GBA\SaveRAM` folder. BizHawk writes the save file when it closes the game.

## Which game do you pick?

Pick your own ROM. Once the hunt has run, a copy of your ROM ending in `-WISHMKR` sits in the ROM folder and appears in the Game list too, the next time the bot scans the ROM folder (when it starts, or when the ROM list is refreshed). Do not pick it: it is the bot's working copy, and its save holds the disc's last Jirachi, not your game.

## How do you start the hunt?

1. On the dashboard, find **Wishmaker Jirachi Hunt** in the list of actions.
2. Set **Game** to your ROM.
3. Set **Milestone** to your milestone.
   The list is empty until a game is picked.
4. Press **Start**.
   The hunt appears on the dashboard as running, then BizHawk opens.

Other hunts may keep running beside it, but only one Jirachi hunt can run at a time.

## What does the bot do each cycle?

1. It continues your game in BizHawk and checks that it is in the field.
2. It plays for a random while.
3. It saves in-game. If the save landed in the wrong half of the save file, it saves again.
4. It closes BizHawk.
5. Dolphin starts the Bonus Disc with a copy of your save on its Game Boy Advance. The bot works the disc's Jirachi menu with a virtual controller, and resets the Game Boy Advance when the disc asks for it.
6. The disc writes the Jirachi into the copy.
7. BizHawk opens the copy and shows the Jirachi's summary screen for 3 seconds.
8. The bot reads the Jirachi, checks it is the one your save predicts, and records it.
9. Not shiny: it goes back to the field, closes BizHawk and starts the next cycle.

No step needs your mouse or keyboard. Dolphin may sit behind other windows.

## What files does it make, and what do you do with them?

`<game>` is your ROM's file name without `.gba`.

| File | Where | What it is | What to do |
|---|---|---|---|
| `<game>.SaveRAM.wishmaker.bak` | the `GBA\SaveRAM` folder in your BizHawk folder | A copy of your save from before the first cycle, made once. | Keep it. It is your save as it was. |
| `<game>.SaveRAM` | the same folder | Your own save. The bot continues it and saves over it every cycle, so its play time grows. | Nothing. It stays your game. |
| `<game>-WISHMKR.gba` | your ROM folder | A copy of your ROM, made once. | Nothing. Do not pick it in the Game list. |
| `<game>-WISHMKR.SaveRAM` | the `GBA\SaveRAM` folder in your BizHawk folder | The save the disc gave back, overwritten every cycle. After a shiny, it holds the shiny Jirachi in the party. | After a shiny, copy it somewhere safe. |

> **Warning:** copy `<game>-WISHMKR.SaveRAM` somewhere safe before you start this hunt again on the same game. The next cycle overwrites it, and the shiny Jirachi with it.

## What happens when it finds a shiny?

You see:

- BizHawk left open at the shiny Jirachi's summary screen;
- no Dolphin;
- the hunt on the dashboard stopped and waiting;
- an alert in your Discord shiny channel (see [Discord](../05-discord.md));
- the catch recorded under `<game>-WISHMKR`.

When you are done looking, press **Cancel** on the hunt. It closes that BizHawk. There is no Resume for this hunt.

## What does it record?

- One encounter per cycle, under the milestone and the ROM's name.
- On a shiny, also a caught Pokémon, under `<game>-WISHMKR`.

See [Your data](../06-your-data.md) for where these are stored.

## How do you stop the hunt?

Press **Cancel** on the hunt. The bot stops at its next step and closes BizHawk and Dolphin. Your save is never left half written: if an in-game save is under way, the bot waits for it to finish first.

## What if Start is refused?

The bot checks these in this order and stops at the first one that fails. In the messages, "..." stands for your ROM, your save's path or the path in your settings.

| You see | It means | Do |
|---|---|---|
| "A Wishmaker Jirachi Hunt is already running." | Only one Jirachi hunt can run at a time. | Cancel the other one first, or leave it running. |
| "A game must be chosen for the Wishmaker Jirachi Hunt." | No Game is set. | Pick your ROM in Game. |
| "A milestone must be chosen for the Wishmaker Jirachi Hunt." | No Milestone is set. | Pick a Milestone. |
| "No ROM named '...' was found." | The ROM is no longer in the ROM folder. | Put it back, or pick another ROM. |
| "Wishmaker Jirachi Hunt runs only on a tested Ruby or Sapphire ROM (revision 2); ... is ...." | The ROM is not Ruby or Sapphire version 1.2. The dashboard normally does not list such ROMs. | Pick a Ruby or Sapphire version 1.2 ROM. |
| "Wishmaker Jirachi Hunt needs HasWishmakerJirachi, which RS does not have." | The bot itself is set up wrong; you should never see this. | Tell the owner. |
| "Wishmaker Jirachi config not found at ..." or a message that starts with "./Configs/Events/WishmakerJirachi.json:" | The hunt's settings file is missing, or a value in it was changed to one the bot cannot use. The message names the setting. | Put the file back, or fix the value it names (see the settings below). |
| "No save at ...: start the game in BizHawk and save once before hunting Jirachi." | There is no save for this ROM. | Make a save in BizHawk, then start again. |
| "The save at ... is ... bytes, shorter than a 131072-byte Gen 3 save." | The file is not a whole save. | Use a save made by BizHawk for this ROM. |
| "The save at ... has no complete save block: save again in-game before hunting Jirachi." | The save is incomplete. | Open the game in BizHawk, save in-game, then start again. |
| "The save at ... has no Pokédex yet: the Bonus Disc needs one." | You do not have the Pokédex yet. | Play until you get the Pokédex, save, then start again. |
| "The save at ... has a full party: leave a slot free for Jirachi." | All six party slots are full. | Put a Pokémon in the PC, save, then start again. |
| "Dolphin is not at ...: check DolphinBaseDirectory." | Dolphin is not where the settings say. | Fix the setting, as on the Configure page. |
| "The GBA BIOS is not at ...: check GbaBiosPath." | The BIOS is not where the settings say. | Fix the setting, as on the Configure page. |
| "The Colosseum Bonus Disc is not at ...: check ColosseumBonusDiscPath." | The disc image is not where the settings say. | Fix the setting, as on the Configure page. |

## What if it stops in the middle of a cycle?

BizHawk and Dolphin close, and your save is still the last complete one. See also [When something goes wrong](../07-when-something-goes-wrong.md).

| You see | It means | Do |
|---|---|---|
| "EmuHawk for ... was not responding ... ms after launch" | BizHawk did not start in time. | Start the hunt again. |
| "Not in the field after 10 start-menu checks" | After continuing, the game never reached a place where the start menu opens. | Check that your save continues straight into the field, then start again. |
| "The start menu did not open after 12 Start presses (the cursor never moved)" | The bot could not open the start menu to save. | Start the hunt again. |
| "Start-menu cursor would not settle on position ... Refusing to press A on whatever is highlighted." | The bot could not reach the right entry in the start menu. | Start the hunt again. |
| "In-game save: no complete block with counters above ... within ... ms" | The in-game save did not finish in time. | Start the hunt again. Your save is the last complete one. |
| A message about a Bonus Disc step that ran out of presses, with the values it saw | The disc did not reach the screen the bot was waiting for. | Check the Dolphin setup on the Install page, then start again. |
| "Jirachi not in the RAM party" | The Jirachi was not in the party after the disc. | Start the hunt again. If it happens again, tell the owner. |
| "... the disc read a different save than the hunt wrote. Stopping." | The Jirachi is not the one your save predicts. The bot stops on purpose. | Tell the owner before you start again. |

## Which settings can you change?

The hunt's settings are in `ShinyBotHunter/Configs/Events/WishmakerJirachi.json`. Open it in a text editor; the bot reads it when the hunt starts.

| Setting | What it does | Value as shipped |
|---|---|---|
| `MinPlayMs` and `MaxPlayMs` | The shortest and longest random play before each save, in milliseconds. `MinPlayMs` must be below `MaxPlayMs`. | 2000 and 20000 |
| `LaunchReadyTimeoutMs` | How long, in milliseconds, BizHawk or Dolphin may take to answer after it opens. | 240000 |
| `SaveTimeoutMs` | How long, in milliseconds, one in-game save may take. | 40000 |
| `TransferTimeoutMs` | How long, in milliseconds, the disc may take to send the Jirachi after the Game Boy Advance resets. | 90000 |
| `DsuPort` | The network port of the virtual controller. It must match the Dolphin settings the install script set up, so leave it as it is. | 26760 |

The other settings in the file name the recorded button presses and the timings for diagnosing problems. Leave them as they are.

## Why does it work this way?

The reasons behind each step are in the [Wishmaker Jirachi spec](../../specs/wishmaker-jirachi.md).

Next: [Discord](../05-discord.md)
