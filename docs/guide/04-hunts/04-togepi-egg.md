<!-- Derived from: ShinyBotHunter/BotEvents/HgssTogepiEggEvent.cs, ShinyBotHunter/Configs/Events/TogepiEgg.json, ShinyBotHunter/BotEvents/EggHuntConfig.cs, ShinyBotHunter/Games/EventRegistry.cs ("HGSS Togepi Egg"), ShinyBotHunter/Service/BotService.cs (StartTogepiEggAsync, EnsurePartyHasRoomForEggAsync), PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (Milestone label), PokemonDashboard/src/renderer/src/components/admin/EmulatorCard.tsx (Run button) -->
# Togepi egg hunt

Part of the [ShinyBotHunter guide](../README.md)

## What does this hunt do?

It takes the Togepi egg, walks until the egg hatches, and checks whether the Togepi is shiny. If it is not, it soft resets to before the egg was handed over and tries again. It stops when a shiny Togepi hatches.

The reset point is before the hand-over because what is in an egg is fixed the moment you receive it.

It works in HeartGold and SoulSilver.

## What does your save need?

1. **An emulator that is already running with your save loaded.** This action does not start an emulator. Start one first with **Launch Bot + Continue**, as [the dashboard page](../03-dashboard.md) describes.
2. **Saved before receiving the egg, in the area where it is given.** The bot walks to the exact tile and facing it starts every attempt from, and saves there once, so that every soft reset comes back to that spot. If you are already standing there facing the right way, it does not save. If the save is in a different area, the bot stops at once (see [If it stops early](#if-it-stops-early)).
3. **A free party slot.** With six Pokémon in your party, the egg cannot be handed over, so the bot refuses to start.

> **Ask the owner:** where in the game is this (the bot knows it only as map 157), and what must already be true in the story for the egg to be offered?

> **Ask the owner:** where is the walking path in the game (the bot knows it as a straight east-west line the hand-over ends on), so the reader can check nothing blocks it?

## Start the hunt

1. On the dashboard, find the card for the emulator you started.
2. Pick **HGSS Togepi Egg**.
   You see one field, **Milestone**.
3. Choose the milestone to file the hatched Pokémon under.
4. Press **Run**.
   The card shows the hunt as running. If you are not on the starting tile, the bot walks there and saves first.

How the action form works in general is on [the dashboard page](../03-dashboard.md).

## What do the settings mean?

| What it means | Field on the dashboard |
|---|---|
| The milestone the hatched Pokémon are filed under. The hunt does not check the milestone's list: any shiny Togepi ends it. | Milestone |

The walking path and the timings are in a file that comes with the bot. They are not settings for you to change.

## What happens in each attempt?

1. The bot takes the egg. It waits up to 60 seconds for the egg to show up in your party.
2. It walks back and forth along a straight path until it can no longer move, which is the hatch starting.
3. It lets the hatch play.
4. It checks that the egg really hatched. If it is still an egg, something else stopped it moving, and it goes back to walking.
5. It reads the hatched Pokémon and writes it to the encounter log.
6. If the Pokémon is shiny, the bot sends a shiny alert to [Discord](../05-discord.md), saves the game, records it as caught, and finishes.
7. If not, it soft resets and continues the game. The waits while continuing are slightly random, so each attempt plays out differently.

## What does it log?

- Every hatched Pokémon goes into the encounter log with the game, the milestone, the Pokémon, the time and the game file.
- The shiny one is added to your caught Pokémon.

[Your data](../06-your-data.md) explains where these are kept.

## How does it stop?

- **It finishes** when a shiny Togepi hatches. It saves first.
- **You cancel it** from the dashboard, as [the dashboard page](../03-dashboard.md) describes.
- **It stops** when it cannot read your party after a hatch to confirm it.
- **It gives up with a message**, as listed below.

> **Ask the owner:** where does the reader see these messages: in the window the bot runs in, or on the dashboard?

## If it stops early

**You see** Run refused with "Party is full (6/6). The egg needs a free party slot or the NPC will not hand it over. Deposit or release a pokemon before starting this hunt."
**It means** your party has no room for the egg.
**Do** put one Pokémon in the PC, save, and start the hunt again.

**You see** "Bot is on map ... but the hunt starts on map 157. Load a save in the right area before starting this hunt." (with the map the bot is on in place of the dots).
**It means** your save is not in the area where the egg is given.
**Do** load a save made in that area, before receiving the egg, and start the hunt again.

**You see** "No route from ... to the start tile ... on map 157."
**It means** the bot is in the right area but cannot find a way to the starting tile.
**Do** walk closer to where the egg is given, save, and start the hunt again.

**You see** "No species 175 egg appeared in the party 60000ms after the 'ReceiveTogepiEgg' macro finished."
**It means** the bot did not get the egg within 60 seconds. The save is out of step with what the bot expects, or it was not made before the hand-over.
**Do** load a save made before receiving the egg, in the area where it is given, and start again.

**You see** "Position never stalled within 1800s of pacing."
**It means** the bot walked for 30 minutes and the egg never hatched.
**Do** check that the bot is walking where you expect, then see [When something goes wrong](../07-when-something-goes-wrong.md).

**You see** "Position stalled 5 times without the egg ever hatching. Something other than the hatch cutscene keeps blocking movement on this corridor."
**It means** something kept stopping the bot on its walking path, five times, and it was never the hatch.
**Do** look at the path in the emulator for anything in the way, then start again.

For anything else, see [When something goes wrong](../07-when-something-goes-wrong.md).

Next: [Wishmaker Jirachi hunt](05-wishmaker-jirachi.md)
