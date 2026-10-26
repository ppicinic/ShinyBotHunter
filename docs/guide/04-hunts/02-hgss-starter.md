<!-- Derived from: ShinyBotHunter/BotEvents/MultiShinyHunterEvent.cs, ShinyBotHunter/BotEvents/ShinyStarterEvent.cs, ShinyBotHunter/Games/EventRegistry.cs ("Shiny Starter Hunt" HGSS mode, "HGSS Shiny Starter Hunt"), ShinyBotHunter/Service/BotService.cs (StartShinyStarterHuntAsync, StartHgssStarterHunt, GetTopLevelOptions), ShinyBotHunter/BotEvents/CreateSaveEvent.cs, ShinyBotHunter/Context/GameContext.cs (GameName, RomExtension), ShinyBotHunter/ApplicationSettings.cs (MaxEmulatorsFor), PokemonDashboard/src/renderer/src/components/admin/TopLevelActionForm.tsx (Game choice label, Start button), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (Mode shown as Game) -->
# HeartGold and SoulSilver starter hunt

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

It starts new games of HeartGold and SoulSilver, goes to the choice of starter, and resets until a starter is shiny. It keeps going until it has a shiny of each of the three starters: Chikorita, Cyndaquil and Totodile.

It runs several emulators at once and opens and closes them itself.

## What do you need before you start?

1. **A HeartGold ROM and a SoulSilver ROM in your ROM folder.** The bot finds each one by what the file is, not by its name, so the file names do not matter. [Install](../01-install.md) explains the ROM folder.
2. **No save.** The bot makes its own copies of each ROM in the ROM folder and starts a new game in each copy. A copy is named after the ROM file with a number added: the first HeartGold copy is your HeartGold file's name followed by `1`, the second by `2`, and so on. These copies stay in your ROM folder.
3. **The number of emulators to run at once.** This is the maximum number of DS emulators, set in [Configure](../02-configure.md).
4. **Nothing else running on the dashboard.** This hunt has to be the only thing the bot is doing.

> **Ask the owner:** does a milestone named "1-1 Starters" have to exist on the dashboard for this hunt's counts to show there? (The hunt files every starter under that name and does not ask for a milestone.)

## Start the hunt

1. Make sure no emulators are running on the dashboard.
2. In the dashboard's list of actions that start their own emulators, pick **Shiny Starter Hunt**.
   You see a **Game** choice.
3. Set **Game** to **HeartGold / SoulSilver**.
   No other fields appear. This hunt takes no settings.
4. Press **Start**.
   Emulator windows open one after another, about 5 seconds apart.

How the action form works in general is on [the dashboard page](../03-dashboard.md).

## What happens in each attempt?

1. In each window, the bot goes to the choice of starter.
2. It reads all three starters and writes all three to the encounter log.
3. If none of them is shiny, it soft resets and tries again.
4. If one is shiny, it moves to that one (left for Chikorita, right for Cyndaquil, and it stays on the middle one for Totodile), saves the game with it, and records it as caught. Then it closes that window.

## The two stages

The hunt runs in two stages.

1. **HeartGold first.** The bot makes the HeartGold copies, one per window, and starts a new game in each. Then all the windows hunt until any one of them finds a shiny starter. When that happens, the other HeartGold windows are stopped.
2. **Then SoulSilver.** The bot makes SoulSilver copies and hunts in them until it has a shiny of all three starters, counting the one from HeartGold. When a SoulSilver window finds a shiny, that copy is done, and the window moves on to another copy that is not done yet, or to a new copy.

> **Ask the owner:** is this split intended (HeartGold for the first shiny, SoulSilver for the rest), and should a second shiny of a starter already found count? (The bot keeps every shiny and only checks that all three starters are among them.)

## What does it log?

- Every attempt writes three encounters, one for each starter, under the milestone "1-1 Starters", with the name of the copy it was found in.
- Every shiny starter it picks is added to your caught Pokémon.
- How far the hunt has got (which stage, which shinies it has, which copies are done) is kept in the database. If you start the hunt again after cancelling it, it carries on from there.

[Your data](../06-your-data.md) explains where these are kept.

This hunt does not send a shiny alert to Discord.

> **Ask the owner:** should the page say outright that this hunt sends no Discord alert?

## How does it stop?

- **It finishes** when it has a shiny Chikorita, Cyndaquil and Totodile.
- **You cancel it** from the dashboard, as [the dashboard page](../03-dashboard.md) describes. Allow up to about two minutes for the emulators to stop. Its progress is kept.

Once the hunt has finished, its saved progress says finished, so starting it again ends straight away.

> **Ask the owner:** how does the reader hunt the three starters again after a run has finished?

## If it stops early

**You see** the hunt stop, and the bot's log shows "No ROM in the ROM folder identifies as Pokemon HeartGold. The starter hunt needs one." (or the same for Pokemon SoulSilver).
**It means** the bot found no ROM of that game in your ROM folder.
**Do** put your HeartGold or SoulSilver ROM in the ROM folder, as [Install](../01-install.md) describes, and start the hunt again.

**You see** Start refused with "Multi shiny hunting in HGSS can be the only active bot[s]."
**It means** something else is running on the dashboard.
**Do** cancel or close everything else first, then start the hunt again.

For anything else, see [When something goes wrong](../07-when-something-goes-wrong.md).

Next: [Ruby and Sapphire starter hunt](03-rs-starter.md)
