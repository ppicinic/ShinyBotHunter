<!-- Derived from: docs/specs/primo-egg-hunt.md (Summary, Non-goals, FR-1, FR-2, FR-16), ShinyBotHunter/BotEvents/HgssPrimoEggEvent.cs, ShinyBotHunter/BotEvents/HgssEggHatch.cs, ShinyBotHunter/Configs/Events/PrimoEgg.json, ShinyBotHunter/BotEvents/PrimoEggConfig.cs, ShinyBotHunter/Games/EventRegistry.cs ("HGSS Primo Egg"), ShinyBotHunter/Service/BotService.cs (StartPrimoEggAsync, EnsurePartyHasRoomForEggAsync), ShinyBotHunter/Games/GameStateWait.cs (GameStateTimeoutException), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (Milestone label), docs/guide/04-hunts/04-togepi-egg.md, docs/guide/07-when-something-goes-wrong.md -->
# Primo egg hunt

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

It gets the Slugma egg from Primo, walks until the egg hatches, and checks whether the Slugma is shiny. If it is not, it soft resets to the save in front of Primo and tries again. It stops when a shiny Slugma hatches.

The reset point is before Primo hands the egg over because what is in an egg is fixed the moment you receive it.

It works in HeartGold and SoulSilver.

## What does your save need?

1. **An emulator that is already running with your save loaded.** This action does not start an emulator. Start one first with **Launch Bot + Continue**, as [the dashboard page](../03-dashboard.md) describes.
2. **Saved standing in front of Primo in the Violet City Pokémon Center, facing him.** That is map 158, tile (12, 12), facing up. Every soft reset comes back to this spot, so the bot does not walk there or save for you. If you are anywhere else, the bot refuses to start.
3. **A free party slot.** With six Pokémon in your party, Primo cannot hand the egg over, so the bot refuses to start.
4. **The Slugma egg not yet received on that save.** Primo gives it only once.

You do not need to know the password. The bot works it out from your trainer ID.

The walk is outside in Violet City: the bot paces back and forth on the street outside the Mart, the same line as the Togepi egg hunt, so nothing should block that line.

## Start the hunt

1. On the dashboard, find the card for the emulator you started.
2. Pick **HGSS Primo Egg**.
   You see one field, **Milestone**.
3. Choose the milestone to file the hatched Pokémon under.
4. Press **Run**.
   The card shows the hunt as running.

How the action form works in general is on [the dashboard page](../03-dashboard.md).

## What do the settings mean?

| What it means | Field on the dashboard |
|---|---|
| The milestone the hatched Pokémon are filed under. The hunt does not check the milestone's list: any shiny Slugma ends it. | Milestone |

The walking route, the pacing line and the timings are in a file that comes with the bot. They are not settings for you to change.

## What happens in each attempt?

1. The bot reads your trainer ID and works out the Slugma password from it, once per run. It writes the password to the backend's window as four word numbers.
2. It talks to Primo and enters the password's two words at each of his two prompts. After each prompt it checks that the game took the words it entered.
3. It takes the egg and finds it in your party.
4. It walks out of the Pokémon Center to the street outside the Mart, one stretch at a time, checking it arrived at the end of each.
5. It walks back and forth on the street until the game itself starts the hatch. Every 30 seconds it writes the egg step count to the backend's window.
6. It presses A at "Oh?", answers No when asked whether to nickname the Pokémon, and waits until you can walk again.
7. It reads the hatched Pokémon and writes it to the encounter log.
8. If the Pokémon is shiny, the bot sends a shiny alert to [Discord](../05-discord.md), saves the game, records it as caught, and finishes.
9. If not, it soft resets and continues the game, then checks you are back in front of Primo. The waits while continuing are slightly random, so each attempt plays out differently.

An attempt takes about 14 minutes, most of it walking.

**Tip:** a Pokémon with the Flame Body ability in your party, such as Magby or Magmar, halves the steps the egg needs, so each attempt is much shorter. The egg itself does not count. The bot does not need one.

## What does it log?

- Every hatched Pokémon goes into the encounter log with the game, the milestone, the Pokémon, the time and the game file.
- The shiny one is added to your caught Pokémon.

[Your data](../06-your-data.md) explains where these are kept.

## How does it stop?

- **It finishes** when a shiny Slugma hatches. It saves first.
- **You cancel it** from the dashboard, as [the dashboard page](../03-dashboard.md) describes.
- **It gives up with a message**, as listed below.

These messages show in the backend's window, as `[BotService] Event '<name>' crashed` followed by the message; on the dashboard the emulator just goes idle. See [Where do the bot's messages appear?](../07-when-something-goes-wrong.md#where-do-the-bots-messages-appear). The two refusals at **Run** are the exception: they show on the dashboard when you press **Run**.

## If it stops early

**You see** Run refused with "Party is full (6/6). The egg needs a free party slot or the NPC will not hand it over. Deposit or release a pokemon before starting this hunt."
**It means** your party has no room for the egg.
**Do** put one Pokémon in the PC, save in front of Primo, and start the hunt again.

**You see** Run refused with "Stand facing Primo in the Violet City Pokémon Center: map 158, tile (12, 12), facing up."
**It means** your character is not on that tile, or is not facing Primo.
**Do** walk to the tile in front of Primo, face him, save, and start the hunt again.

**You see** "Primo gave no egg: this save already received the egg (FLAG_GOT_SLUGMA_EGG_FROM_PRIMO is set)."
**It means** this save has already had the Slugma egg from Primo.
**Do** load a save made before receiving the egg.

**You see** "Primo gave no egg: Primo refused the password.", "Primo gave no egg: the party is full." or "Primo gave no egg: Primo's script ended with result ..." (with a number in place of the dots).
**It means** Primo did not hand the egg over.
**Do** look at the emulator to see what Primo said, then see [When something goes wrong](../07-when-something-goes-wrong.md).

**You see** "after prompt ..., 0x8000 and 0x8001 read ...; entered ..." (with the prompt and the word numbers in place of the dots).
**It means** the words the game took at that prompt are not the ones the bot entered.
**Do** look at the emulator to see where the conversation stopped, then start again from the save in front of Primo.

**You see** "Waited for Primo's script" followed by where it was ("to start", "after prompt 1", "after the egg") and what the game showed.
**It means** the conversation with Primo did not start, or stopped moving for 20 seconds.
**Do** look at the emulator to see where the conversation stopped, then start again from the save in front of Primo.

**You see** "Waited for the easy-chat app (overlay ...) to open; the sub-app stayed ... for 10 s".
**It means** the word-entry screen did not open after Primo asked for the password.
**Do** look at the emulator, then start again from the save in front of Primo.

**You see** "No species 218 egg in the party after Primo's script; the party holds species ..." (with the species numbers in your party in place of the dots).
**It means** the conversation finished but the bot could not find the Slugma egg in your party.
**Do** check your party in the emulator, then see [When something goes wrong](../07-when-something-goes-wrong.md).

**You see** "Route leg ... stopped at map ..., tile (..., ...)" (with the stretch, the map and the tile in place of the dots).
**It means** the walk to the street stopped moving for a second and a half on one stretch. Something is in the way, or a door took longer than usual.
**Do** look at the emulator for anything in the way, then start again from the save in front of Primo.

**You see** "Waited for script 2021 while pacing; the day care step counter stayed ... for 30 s" (with the step count in place of the dots).
**It means** the step count did not change for 30 seconds, so the bot was not taking steps. Something is in the way on its walking path.
**Do** look at the path in the emulator for anything in the way, then start again.

**You see** "Waited for" followed by a hatch step, such as "the hatch app (overlay 95) to close", and what the bot saw (overlay and cursor).
**It means** a step of the hatch waited 30 seconds with nothing changing on screen, or a button press did not take after three tries.
**Do** look at the emulator to see where the hatch stopped, then see [When something goes wrong](../07-when-something-goes-wrong.md).

**You see** "No party slot holds pid ... after the hatch script ended" or "Party slot ... (pid ...) is still an egg after the hatch script ended".
**It means** the hatch finished but the bot could not find the hatched Pokémon in your party by the egg's PID, or the egg had not hatched.
**Do** check your party in the emulator, then see [When something goes wrong](../07-when-something-goes-wrong.md).

**You see** "After the soft reset the player is on map ..., tile (..., ...), facing ..., not facing Primo on map 158, tile (12, 12)."
**It means** after the soft reset your character was not back in front of Primo, so the save the game loaded is not the one the hunt started from.
**Do** load a save made in front of Primo, facing him, and start again.

**You see** "[PrimoEgg] the trainer ID read failed", "[PrimoEgg] the script's special variables read failed" or "[EggHatch] the day care step counter read failed", followed by where the read stopped.
**It means** the bot could not read a value it needs from the game's memory.
**Do** see [When something goes wrong](../07-when-something-goes-wrong.md).

For anything else, see [When something goes wrong](../07-when-something-goes-wrong.md).

Next: [Discord](../05-discord.md)
