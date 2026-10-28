<!-- Derived from: ShinyBotHunter/BotEvents/MultiStarterHuntEvent.cs, ShinyBotHunter/BotEvents/StarterHuntEvent.cs, ShinyBotHunter/Configs/Events/RsStarterHunt.json, ShinyBotHunter/BotEvents/StarterHuntConfig.cs, ShinyBotHunter/BotEvents/StarterSaveFiles.cs, ShinyBotHunter/Games/EventRegistry.cs ("Shiny Starter Hunt"); read only: ShinyBotHunter/Service/BotService.cs (StartShinyStarterHuntAsync, ResumeAsync), ShinyBotHunter/Context/GameContext.cs (GameName), ShinyBotHunter/Service/StarterHuntStateStore.cs (AllocateCopyIds, IsDone), ShinyBotHunter/Service/RomInfoService.cs (the Game list; TestedRevisions and its comment), ShinyBotHunter/Macro/Configs/RS/create_new_save.yaml (header), PokemonDashboard/src/renderer/src/components/admin/TopLevelActionForm.tsx, PokemonDashboard/src/renderer/src/components/admin/ActionParamFields.tsx, PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts -->
# Ruby and Sapphire starter hunt

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

It soft-resets Ruby or Sapphire until the starter you chose is shiny. The game only shows the starter once the first battle starts, so the bot picks the ball, plays into the battle and then reads the Pokémon. When it finds a shiny, it stops that emulator in the battle and hands it to you.

## What do the words on this page mean?

- **ROM copy**: each emulator plays its own copy of your ROM, so each copy has its own save. A copy is named after your ROM file plus a number: `<your ROM>1`, `<your ROM>2`, and so on. The bot makes the copies itself, in your ROM folder.
- **Wave**: the group of emulators hunting one starter at the same time. The first shiny ends the wave.
- **Save**: the bot makes each copy's save itself. It starts a new game and saves in front of Professor Birch's bag, and every reset goes back to that save. If a new save has the same trainer ID as another save, the bot wipes it and makes it again, at most 9 more times (10 tries in all), because two saves with one trainer ID find the same shinies.
- **Parked shiny**: an emulator left paused in the middle of the battle with a shiny, waiting for you.
- **Retired copy**: a copy whose save now holds a caught shiny. The bot never hunts on it again.
- **Resume**: the button that saves the oldest parked shiny.

## What do you need before you start?

- Your Ruby or Sapphire ROM in the ROM folder. The [Install](../01-install.md) and [Configure](../02-configure.md) pages set up the ROM folder and the BizHawk folder. You do not need a save: the bot makes one for each copy.
- A milestone for the game, made on the dashboard. See [The dashboard](../03-dashboard.md).
- Nothing else running on the dashboard. This hunt starts and closes its own emulators, so it has to be the only thing running.

The bot is tested on revision 2 (version 1.2) of Ruby and Sapphire, and its recorded button presses go with that revision; another revision needs its own recordings. The Game list shows every ROM whatever its revision, so pick a revision 2 ROM.

## Which game do you pick?

The Game list shows every ROM in your ROM folder. Once the bot has made copies, they appear in the list too, the next time the bot scans the ROM folder (when it starts, or when the ROM list is refreshed). So do the Wishmaker Jirachi hunt's `-WISHMKR` files, if you have run that hunt.

Always pick your own ROM. Do not pick a copy (your ROM's name with a number the bot added) or a `-WISHMKR` file: those are the bot's working files, and the bot would make copies of them.

## How do you start the hunt?

1. On the dashboard, find **Shiny Starter Hunt** in the list of actions.
2. Set **Game** to **Ruby / Sapphire**.
   You see a box named **Starter 1**, and the **Milestone** and **Emulators** fields.
3. In **Starter 1**, set **Game** to your ROM.
4. In **Starter**, type `Treecko`, `Torchic` or `Mudkip`, spelled exactly like that.
5. Optional: in **RecordingName**, type the name the encounters are filed under. Left empty, they are filed under the ROM's name.
6. Optional: tick **RequireFemale** if only a female shiny will do.
7. Optional: press **+ Starter** to add another starter to the queue, and fill in its box the same way. The bot hunts them in order.
8. Set **Milestone** to your milestone.
   The list is empty until a game is picked in a starter box.
9. In **Emulators**, type how many emulators hunt each starter at once: from 1 up to the GBA limit in your settings. Left empty, it is 1.
10. Press **Start**.
   The hunt appears on the dashboard as running, and the emulators open one at a time.

## What does the bot do on each emulator?

1. It waits for the emulator before it to answer before it opens the next one.
2. If the copy has no save yet, it makes one, as described above.
3. It continues the game, takes the ball and holds B until the starter is out and the battle menu is up.
4. It reads the starter and records it as an encounter.
5. Not shiny: it soft-resets and goes back to step 3.
6. Shiny, but **RequireFemale** is ticked and it is not female: it resets past it. The encounter is already recorded.
7. Shiny, but it fails the legality check (a Pokémon no real game could produce): it resets past it.
8. Otherwise it parks: the emulator stays paused in the battle, waiting for you.

## What happens when a shiny is found?

- The other emulators in that wave close.
- The dashboard shows the hunt as Waiting, with a **Resume** button.
- The next starter in the queue gets a new wave, on new copies.
- A parked emulator keeps its place, so later waves get smaller. When every place is held by a parked shiny, the hunt waits until you press Resume.

## How do you save a parked shiny?

1. Click into the paused emulator and play the battle out by hand.
2. On the dashboard, press **Resume** once.
   The bot saves through the start menu, records the catch, retires the copy and closes that emulator.

Each press of Resume handles one shiny, oldest first. Press it again for the next one.

The shiny now lives in that copy's save: the file named after the copy, ending in `.SaveRAM`, in the `GBA\SaveRAM` folder inside your BizHawk folder.

## What does it record?

- Every starter it reads is an encounter, filed under the milestone and the recording name.
- A shiny you saved with Resume is also a caught Pokémon.
- The starter is marked as done for that milestone. If you start the hunt again with the same milestone, the bot skips that starter.

See [Your data](../06-your-data.md) for where these are stored.

## How do you stop the hunt?

- Press **Resume** for every parked shiny first. The bot forgets a shiny that is still parked when the hunt or the bot stops, and the next start reports it as discarded.
- Then cancel the hunt on the dashboard. Every emulator still hunting closes.
- Never delete or reuse a retired copy's save: it holds your shiny.

## What if Start is refused?

| You see | It means | Do |
|---|---|---|
| "The shiny starter hunt manages its own emulators, so it has to be the only thing running." | Something else is running on the dashboard. | Stop it, then press Start again. |
| "A milestone must be chosen for the shiny starter hunt." | No milestone is set. | Pick a Milestone. |
| "At least one starter entry (game + starter) is required for the shiny starter hunt." | No starter box is filled in. | Fill in Game and Starter in Starter 1. |
| "Every starter entry must pick a game." or "Every starter entry must pick a starter." | One of the starter boxes is half filled in. | Fill it in, or press Remove on it. |
| "No ROM named '...' was found." | The ROM you picked is no longer in the ROM folder. | Put it back, or pick another ROM. |
| "Shiny Starter Hunt needs HasSoftResetStarterHunt, which HGSS does not have." or "Shiny Starter Hunt needs HasSoftResetStarterHunt; '...' is ..., which has no game profile." | The ROM you picked is not Ruby or Sapphire. | Pick a Ruby or Sapphire ROM. |
| "Emulators must be between 1 and ... for GBA." | The Emulators number is too high or too low. | Type a number in that range. |
| "No macro configured for starter '...'. Configured: Treecko, Torchic, Mudkip." | The Starter name is misspelled. | Type one of the names shown, exactly. |

## What if it stops during the hunt?

See also [When something goes wrong](../07-when-something-goes-wrong.md).

The log shows the first two messages after "An emulator stopped with an error:". For those two, only that one emulator stops; the rest of the wave carries on.

| You see | It means | Do |
|---|---|---|
| "Emulator for ... was not responding ... after launch, so no macro was sent" | An emulator did not start in time. The bot closed it. | If it keeps happening, hunt with fewer Emulators. |
| "Could not read the starter on ... Refusing to reset, because the pokemon on screen may be a shiny and resetting would lose it." | The bot could not read the starter, so it closed that emulator without resetting it. | Nothing: that emulator is already closed, and the other emulators keep hunting. |
| "No save appeared at ... after saving. The shiny is still on the paused emulator; save manually before closing anything." | Resume did not produce a save. | Save the game by hand in that emulator before you close anything. |
| "Start-menu cursor would not settle on position ... Refusing to press A on whatever is highlighted." | The bot could not reach SAVE in the start menu during Resume. | Save the game by hand in that emulator before you close anything. |

## Which settings can you change?

The hunt's settings are in `ShinyBotHunter/Configs/Events/RsStarterHunt.json`. Open it in a text editor; the bot reads it when the hunt starts.

| Setting | What it does | Value as shipped |
|---|---|---|
| `ConcurrentLaunches` | How many emulators may be starting at the same time. Raise it to open a wave faster; starting many at once can slow the PC down so badly that emulators fail to open. | 1 |
| `StarterOnFieldSettleMs` | A pause, in milliseconds, once the starter is on the field, so you can watch it arrive. 0 removes the pause. | 1000 |
| `LogLegalityForEveryEncounter` | Whether the legality check is logged for every starter, not only for shinies. Shinies are always checked. | true |
| `SaveTrainerIdRetries` | How many saves the bot makes in all for a copy whose new save's trainer ID belongs to another save. With 10, a save is remade at most 9 times; the last one is kept even if it still clashes. | 10 |
| `LaunchReadyTimeoutMs` | How long, in milliseconds, the bot waits for a new emulator to answer before it gives up. | 240000 |

The other settings in the file name the recorded button presses and the timings for diagnosing problems. Leave them as they are.

Next: [Togepi egg hunt](04-togepi-egg.md)
