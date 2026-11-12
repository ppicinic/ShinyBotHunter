<!-- Derived from: docs/specs/sinjoh-static.md (Summary, Non-goals, FR-1 to FR-15), ShinyBotHunter/BotEvents/HgssStaticEncounterEvent.cs, ShinyBotHunter/BotEvents/StaticEncounterConfig.cs, ShinyBotHunter/Configs/Events/StaticEncounter.json, ShinyBotHunter/Service/BotService.cs (StaticEncounterRefusalAsync, StartStaticEncounterAsync, GetBotActionsAsync), ShinyBotHunter/Games/EventRegistry.cs ("HGSS Static Encounter"), PokemonDashboard/src/renderer/src/components/admin/ActionParamFields.tsx (the choices dropdown), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (Milestone label), docs/guide/04-hunts/06-primo-egg.md -->
# Static encounter hunt

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

It walks onto the circle you pick on the Mystri Stage, answers Yes, waits through the cutscene, says No when asked whether to nickname the Pokémon, and presses through the dialogue after it. Then it opens the new Pokémon's summary and checks it there. If the Pokémon is not shiny, the bot soft resets and tries again. It stops at the first shiny.

The Pokémon is decided when it is handed over after the cutscene, so the reset point is the save on the stage, before you step onto the circle.

It works in HeartGold and SoulSilver. Each kind of Arceus gives one of Dialga, Palkia or Giratina, so one save gets one shiny and the hunt ends there.

## What does your save need?

1. **An emulator that is already running with your save loaded.** This action does not start an emulator. Start one first with **Launch Bot + Continue**, as [the dashboard page](../03-dashboard.md) describes.
2. **Saved on the Mystri Stage in the Sinjoh Ruins, after Cynthia has spoken, standing on the stage in front of the circles.** The bot finds its own way to the circle you pick, so the exact tile does not matter as long as there is a path to it.
3. **The Arceus alone in your party.** The game itself refuses a second Pokémon, with "It seems that only a single ARCEUS can stand here...".
4. **The event Arceus (a fateful encounter), or an Arceus met at the Hall of Origin in Diamond, Pearl or Platinum.** That is what the bot checks at **Run**. A Hall of Origin Arceus that was traded passes that check, but the circle does not respond to it, and the hunt then stops with "The circle did not respond. This save has already received this Pokémon, or this Arceus has been used."
5. **That Arceus not already used for this Pokémon on this save.**

**The bot saves once when the hunt starts**, after its checks, so every soft reset comes back to the same place. Your save file changes. It is the save you would make yourself; keep a copy first if you want the file as it was.

## Start the hunt

1. On the dashboard, find the card for the emulator you started.
2. Pick **HGSS Static Encounter**.
   You see two fields, **Milestone** and **StaticEncounter**.
3. Choose the milestone to file the Pokémon under, and the Pokémon in **StaticEncounter**. That list is read from a file that comes with the bot (`Configs/Events/StaticEncounter.json`) each time the actions are listed: Dialga (Sinjoh Ruins), Palkia (Sinjoh Ruins), Giratina (Sinjoh Ruins).
4. Press **Run**.
   The card shows the hunt as running.

How the action form works in general is on [the dashboard page](../03-dashboard.md).

## What do the settings mean?

| What it means | Field on the dashboard |
|---|---|
| The milestone the Pokémon are filed under. The hunt does not check the milestone's list: any shiny of the chosen Pokémon ends it. | Milestone |
| Which circle the bot walks onto, and so which Pokémon it hunts. | StaticEncounter |

Where the circles are, the macros and the timings are in a file that comes with the bot. They are not settings for you to change. If that file cannot be read, **StaticEncounter** shows as a text box instead of a list, and **Run** shows the file's error.

## What happens in each attempt?

Once per run, before the first attempt:

- At **Run** the bot checks your save as listed above and works out the walk from where you stand to the circle.
- It saves the game. This takes about 25 seconds.

Then each attempt:

1. The bot walks onto the circle.
2. It answers Yes.
3. It waits through the cutscene. For about two minutes there is nothing to press; this is not a stall.
4. It answers No when asked whether to nickname the Pokémon.
5. It presses through the dialogue until you can walk again. On a first visit Cynthia speaks, then you are warped to the Ruins of Alph and the researcher speaks.
6. It opens the menu, then POKéMON, slot 2, and SUMMARY.
7. It reads the Pokémon and writes it to the encounter log.
8. If the Pokémon is shiny, the bot sends a shiny alert to [Discord](../05-discord.md), closes the summary and the party list, saves the game, records it as caught, and finishes.
9. If not, it soft resets and continues the game, then checks you are back on the stage, on the same tile, facing the same way.

An attempt takes about three and a half minutes, most of it the cutscene and the dialogue after it.

## What does it log?

- Every Pokémon received goes into the encounter log with the game, the milestone, the Pokémon, the time and the game file.
- The shiny one is added to your caught Pokémon.

[Your data](../06-your-data.md) explains where these are kept.

## How does it stop?

- **It finishes** at the first shiny. It saves first.
- **You cancel it** from the dashboard, as [the dashboard page](../03-dashboard.md) describes.
- **It stops with a sentence**, as listed below.

A stop is not a crash. The bot lets go of its buttons, writes `[Static] stopped: ` followed by the sentence in the backend's window, and the emulator goes idle with **Run** offered again (not **Resume**). Fix what the sentence says and press **Run** again. Only an unexpected error shows as `[BotService] Event '<name>' crashed`; see [Where do the bot's messages appear?](../07-when-something-goes-wrong.md#where-do-the-bots-messages-appear). The refusals at **Run** show on the dashboard when you press **Run**.

If the hunt stops while closing the summary of a shiny, it stops without saving and without recording the Pokémon as caught. The shiny is still in your party and the alert was already sent, so save the game by hand in the emulator.

## If it stops early

**You see** "Stand on the Mystri Stage in the Sinjoh Ruins.", "Close any menu or text first: the player must be free to walk on the stage.", "The party could not be read; try again once the game is in the field.", "The party must be one Arceus and nothing else." or "This Arceus is not the event Arceus or the Hall of Origin one."
**It means** at **Run**, your save is not ready as listed in [What does your save need?](#what-does-your-save-need). The bot checks the same things again after every soft reset; there, one of these sentences means the save the game loaded is not the one the hunt saved.
**Do** fix what the sentence says, or load the save the hunt made, and press **Run** again.

**You see** Run refused with "No path from here to the circle. Stand on the stage in front of the circles."
**It means** the bot could not find a way from where you stand to the circle you picked.
**Do** stand on the stage in front of the circles, save, and press **Run** again.

**You see** Run refused with a message starting with the file path `./Configs/Events/StaticEncounter.json:`, or "Static encounter config not found at ...", or a message ending "is not a static encounter. Choose one of: ..." (with the choices in place of the dots).
**It means** the file that comes with the bot is broken or missing, or no Pokémon was picked in **StaticEncounter**.
**Do** pick a Pokémon from the list. For the file, see [When something goes wrong](../07-when-something-goes-wrong.md).

**You see** "The circle did not respond. This save has already received this Pokémon, or this Arceus has been used."
**It means** the circle's script ended without asking Yes or No, or the walk finished on the circle with nothing starting. This happens when the save already has this Pokémon, when the Arceus has been used, and with a traded Hall of Origin Arceus.
**Do** use a save from before that Pokémon was received, with an Arceus that can still be used.

**You see** "The walk to ... ended on map ..., tile (..., ...), facing ...; the circle is at (..., ...)." (with the Pokémon, the map, the tiles and the direction in place of the dots).
**It means** the walk did not end on the circle.
**Do** look at the emulator for anything in the way, then load the save and press **Run** again.

**You see** "At the nickname question the party holds ..., not 2." (with the party count in place of the dots).
**It means** after the cutscene your party does not hold the Arceus and the new Pokémon.
**Do** check your party in the emulator, then load the save and press **Run** again.

**You see** "Party slot 2 is species ..., not ...'s species ...." (with the species numbers and the Pokémon in place of the dots).
**It means** the summary the bot opened is not of the Pokémon you picked.
**Do** check your party in the emulator, then load the save and press **Run** again.

**You see** "After the soft reset the player is at tile (..., ...) facing ..., not (..., ...) facing ..., where the walk was made from."
**It means** after the soft reset your character was not where the hunt saved, so the save the game loaded is not the one the hunt made.
**Do** load the save the hunt made and press **Run** again.

**You see** "Waited for" followed by a step, then "; the game state stayed" and what the game showed. The steps are "the overworld ready" (after the save at **Run**, and after each soft reset), "the circle script", "the circle menu to take the Yes", "the cutscene to reach the nickname question", "the nickname menu to take the No", "the scripts after the hand-over", "the start menu after X", "the party list after POKéMON", "slot 2's context menu", "slot 2's summary after SUMMARY", and, on a shiny, "the party list after B from the summary" and "the field free after B from the party list".
**It means** that step did not happen: the game showed the same thing for too long. The cutscene may show nothing new for 200 seconds; the other steps for 10 to 20 seconds. The last two are the shiny case above.
**Do** look at the emulator to see where it stopped, then load the save and press **Run** again.

**You see** "[Static] the party count read failed: ..." (with where the read stopped in place of the dots).
**It means** the bot could not read the party count from the game's memory.
**Do** see [When something goes wrong](../07-when-something-goes-wrong.md).

For anything else, see [When something goes wrong](../07-when-something-goes-wrong.md).

Next: [Discord](../05-discord.md)
