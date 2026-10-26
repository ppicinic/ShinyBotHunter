<!-- Derived from: docs/plans/2026-10-user-guide.md (Goal, Audience, Where the docs live, Pages), AGENTS.md (What this is) -->
# ShinyBotHunter guide

This guide is for anyone running ShinyBotHunter on their own Windows PC. Read the pages in order; each one ends with a link to the next.

## What is in the guide

1. [Start here](#start-here): what the bot does, what you need, and where to begin.
2. [Install](01-install.md): install the programs the bot needs, set up the emulator, and run the bot for the first time.
3. [Configure](02-configure.md): every setting the bot reads, what it means, and which ones are secret.
4. [The dashboard](03-dashboard.md): launch emulators, set up milestones, start, resume and cancel hunts, and read the counts.
5. Hunts, one page per hunt:
   - [Wild and chained encounters](04-hunts/01-wild-encounter.md): hunt wild Pokémon in HeartGold and SoulSilver.
   - [HeartGold and SoulSilver starter hunt](04-hunts/02-hgss-starter.md): hunt a shiny starter in HeartGold and SoulSilver.
   - [Ruby and Sapphire starter hunt](04-hunts/03-rs-starter.md): hunt a shiny starter in Ruby and Sapphire by soft resetting.
   - [Togepi egg hunt](04-hunts/04-togepi-egg.md): hunt a shiny Togepi from the egg in HeartGold and SoulSilver.
   - [Wishmaker Jirachi hunt](04-hunts/05-wishmaker-jirachi.md): hunt a shiny Wishmaker Jirachi, using the Dolphin emulator.
6. [Discord](05-discord.md): set up the Discord bot, what it posts, and how to answer it.
7. [Your data](06-your-data.md): what the bot stores, where, how to look at it, and how to back it up.
8. [When something goes wrong](07-when-something-goes-wrong.md): what you see, what it means, and what to do.

## Start here

### What does the bot do?

ShinyBotHunter plays Pokémon HeartGold, SoulSilver, Ruby and Sapphire for you in an emulator, over and over, until it finds a shiny Pokémon. It records every Pokémon it encounters along the way. It shows the counts on a dashboard on your PC and in Discord.

### What do you need?

- A Windows PC.
- Your own game files: dumps of the games you want to hunt in and their save files. This guide tells you which formats and folders the bot expects, never where to get them.
- Some free programs, each installed step by step on the Install page: .NET 10, Node.js, PostgreSQL with TimescaleDB, MongoDB, and a special build of the BizHawk emulator. The Wishmaker Jirachi hunt also needs the Dolphin emulator and a GBA BIOS.

### Where do you begin?

1. Open [Install](01-install.md) and follow it from the top. When you finish, the dashboard is open.
2. Follow [Configure](02-configure.md) and [The dashboard](03-dashboard.md), using the Next link at the bottom of each page.
3. Pick the hunt you want from the list above and follow its page.

Next: [Install](01-install.md)
