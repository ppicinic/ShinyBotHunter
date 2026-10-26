<!-- Derived from: ShinyBotHunter/Service/DiscordBotService.cs, ShinyBotHunter/Service/ShinyAlertService.cs, ShinyBotHunter/BotEvents/LevelUpFlow.cs, ShinyBotHunter/ApplicationSettings.cs, ShinyBotHunter/Startup.cs, ShinyBotHunter/Discord/Interactions/PingModule.cs, ShinyBotHunter/BotEvents/BattleOrchestratorEvent.cs, ShinyBotHunter/BotEvents/HgssTogepiEggEvent.cs, ShinyBotHunter/BotEvents/WishmakerJirachiEvent.cs -->
# Discord

Part of the [ShinyBotHunter guide](README.md)

## What does the bot post in Discord?

Discord is optional. The bot hunts the same way without it; see "How do I run without Discord?" below.

When Discord is set up, the bot posts two kinds of message:

- A **shiny alert**, in one channel, mentioning a role, when a hunt finds its shiny.
- A **new-move question**, in a second channel, when a Pokémon in training wants to learn a fifth move. You answer it with buttons.

The two channels are separate settings. You can use one channel for both.

## What do you need?

- A Discord account.
- A Discord server you can add a bot to.

> **Ask the owner:** which server permission you need to add a bot to a server.

## How do I create the bot in Discord?

1. Open the Discord Developer Portal in your browser and sign in.
2. Create a new application and give it a name.
3. Open the application's **Bot** section.
4. Copy the bot token. Keep it secret: anyone with the token can act as your bot. It goes in the secrets file, as [Configure](02-configure.md) shows.
5. In the same section, turn on the privileged gateway intents. The bot asks Discord for every intent when it connects, so these switches must be on.

> **Ask the owner:** the portal's exact button names for these steps, and which intent switches need turning on.

## How do I invite the bot to my server?

1. In the Developer Portal, generate an invite link for your application with the scopes `bot` and `applications.commands`.
2. Give it the permissions it needs to post what it posts: view channels, send messages, embed links and attach files. A shiny alert is a card with a picture of the Pokémon, which needs the last two.
3. Open the link, pick your server, and accept.

> **Ask the owner:** the exact permission list to tick, and the screens of the invite step.

## Which channels and role do I make?

In your server, make:

1. A text channel for shiny alerts.
2. A text channel for new-move questions, or use the shiny-alert channel for both.
3. A role for the bot to mention in each shiny alert. Give it to everyone who wants to be pinged. The mention is a role mention, so it has to be a role, not a person.

## How do I copy the ids?

1. In Discord's settings, turn on Developer Mode.
2. Right-click your server and copy its id.
3. Right-click the shiny-alert channel and copy its id. Do the same for the new-move channel.
4. Copy the role's id from the server's role settings.

> **Ask the owner:** Discord's exact menu wording for Developer Mode and for copying each id.

The shipped settings already hold example numbers for these ids. Replace each one with the id you copied.

## Where do the token and the ids go?

The bot reads five Discord settings:

- The **bot token**: the secret you copied from the Developer Portal. It goes in the secrets file, not in the shared settings.
- The **server id**: the server the bot posts in.
- The **shiny-alert channel id**: where shiny alerts go.
- The **role to mention**: the role id each shiny alert mentions.
- The **new-move channel id**: where new-move questions go.

[Configure](02-configure.md) shows the name of each setting and how to enter it. Restart the backend after you change them.

## How do I check that it worked?

1. Start the backend. Its window shows, in this order:
   - `Discord client ready. Logged in as <bot name> (<id>)`
   - `Connected guild: <server name> (<id>)`
   - `Registered interaction commands to guild <id>`
2. In your server, type `/ping` and send it. The bot answers `pong`.

If you see something else, read "What if Discord does not work?" below.

## How do I read a shiny alert?

The bot posts a shiny alert when:

- a wild or chained encounter finds the Pokémon it is hunting;
- the Togepi egg hunt hatches a shiny;
- the Wishmaker Jirachi hunt gets a shiny Jirachi.

The starter hunts post nothing to Discord.

The alert mentions your role and shows a gold card:

- Title: `✨ Shiny <name> encountered! ✨`, or `✨ Shiny <name> hatched! ✨` for the egg.
- **Nature**: the Pokémon's nature.
- **Shiny Value**: the Pokémon's shiny value.
- **IVs (<total>)**: a small table of the six IVs, HP, ATK, DEF, SPA, SPD and SPE, with their total in the heading.
- A picture of the Pokémon, taken from the sprite folder.

## How do I answer a new-move question?

**When it is asked.** Only in HeartGold and SoulSilver battles that train a Pokémon. When a Pokémon that already knows four moves wants a new one, after a level-up or an evolution, the game asks which move to forget, and the bot asks you in Discord. A Pokémon with a free move slot learns the new move without a question.

**What you see.** A card titled `<name> - New Move` with the text `<name> is trying to learn a new move, <move>. Should <name> forget a move to learn <move>`. Below it there is one button per move the Pokémon knows, named after the move, and a **Do Not Learn** button.

**What to do.**

1. Click the move to forget, or **Do Not Learn**. The card changes to `⚠️ Are you sure you want <name> to forget <old move>, and learn <new move>?`, or `⚠️ Are you sure you want <name> to stop trying to learn <new move>?`, with **Confirm Choice** and **Cancel** buttons.
2. Click **Confirm Choice**. The card changes to `<name> forget <old move>, and learned <new move>!` (the wording is the bot's own) or `<name> did not learn <new move>!`, and the buttons go. The game forgets the move you chose, or declines the new one.
3. Or click **Cancel** to get the first buttons back and choose again.

The game waits on that screen for as long as nobody answers. There is no time limit.

Anyone who can see the channel can press the buttons. The bot checks which message a button belongs to, not who pressed it.

## How do I run without Discord?

1. Leave the bot token **empty** in the secrets file. The placeholder token in the shipped settings, `your-token-here`, does not count as empty: the bot skips Discord only when the token is empty (or is exactly `YOUR_BOT_TOKEN`), so you have to empty it, not leave it as shipped.
2. Leave the example ids in place. The bot reads the server id and the new-move channel id when it starts, even without a token.

> **Ask the owner:** what happens if the token is left as `your-token-here`.

When the backend starts, its window shows `Discord bot token is not configured. Skipping Discord bot startup.`

From then on:

- Shiny alerts are dropped. The backend's window shows `Discord is not logged in; dropping message: <@&<role id>>`, where the number is the id of the role to mention. The line does not name the Pokémon.
- New-move questions are answered **Do Not Learn** for you. The backend's window shows `Discord is not logged in; <name> will not learn <move>.`, and the bot walks the game through declining the move.

## What if Discord does not work?

Each message below appears in the backend's window.

### The bot cannot reach your server

- **You see:** `Missing access to guild <id>. Ensure the bot is invited to this server with scopes 'bot' and 'applications.commands'.`
- **It means:** the bot is not in that server, or the server id is wrong.
- **Do:** invite the bot again with both scopes, or copy the server id again.

### The server id is not set

- **You see:** `Discord GuildId is not configured. Skipping interaction command registration.`
- **It means:** the server id is 0.
- **Do:** set the server id, as [Configure](02-configure.md) shows, and restart the backend.

### `/ping` is not registered

- **You see:** `Failed to register interaction commands. <details>`
- **It means:** the bot could not register its `/ping` command in your server. The details say why.
- **Do:** check that the bot was invited with the `applications.commands` scope, then restart the backend.

### A new-move question never appears

- **You see:** a new-move question never appears in Discord, while the backend's window shows `[LevelUp] asking Discord whether <name> learns <move>`.
- **It means:** the bot asked, but posted somewhere you are not looking.
- **Do:** check the new-move channel id, and that the bot can see and post in that channel.

Next: [Your data](06-your-data.md)
