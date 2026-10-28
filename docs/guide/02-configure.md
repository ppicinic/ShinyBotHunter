<!-- Derived from: ShinyBotHunter/ApplicationSettings.cs, ShinyBotHunter/appsettings.json, ShinyBotHunter/Startup.cs, ShinyBotHunter/ShinyBotHunter.csproj, ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Emulators/EmulatorGrid.cs, ShinyBotHunter/Bot/BotFactory.cs, ShinyBotHunter/Service/ShinyAlertService.cs, ShinyBotHunter/Service/DiscordBotService.cs, ShinyBotHunter/Persistence/MongoDb/MongoDbConnection.cs, Scripts/deploy-dolphin.ps1, PokemonDashboard/config.json, PokemonDashboard/README.md, PokemonDashboard/src/main/index.ts, PokemonDashboard/src/renderer/src/components/PhaseCard.tsx, .gitignore, docs/guide/01-install.md (Install from a release) -->
# Configure

Part of the [ShinyBotHunter guide](README.md)

This page lists every setting the bot and the dashboard read, what each one means, and where to put your own values.

## Where do my settings go?

The bot reads its settings from two places:

- **`ShinyBotHunter\appsettings.json`** ships with the repository and holds placeholder values. Do not put your own values in it: it is shared with everyone who has the code.
- **A secrets file on this PC** holds your own values: your folders, your database password, your Discord token.

When both name the same setting, the secrets file wins. The bot reads both every time it starts, in a Release build too.

For the settings on this page, nothing else is read: not `appsettings.Production.json`, not `appsettings.Development.json`, and not environment variables.

## Find the secrets file

1. Open File Explorer.
2. Type this into the address bar and press Enter:

   ```
   %APPDATA%\Microsoft\UserSecrets\90bed6b9-0481-4f35-8c92-163ea1cba3cf
   ```

3. The file in that folder is `secrets.json`.

The folder name is the bot project's secrets id; every copy of the bot uses the same one.

**Expected result:** the folder and `secrets.json` exist. They are created the first time you change a setting as shown in the next section, so they are missing until then.

## Change a setting

1. Open a terminal in the repository folder.
2. Run `dotnet user-secrets set` with the setting's full name and its value. For example, to set the ROM folder:

   ```
   dotnet user-secrets set "ApplicationSettings:ROMBaseDirectory" "C:/ShinyBot/ROMs/" --project ShinyBotHunter
   ```

Always use this form, with the parts of the name joined by `:`. The Dolphin install script reads only entries written this way.

**Installed from a release?** Without the .NET SDK there is no `dotnet user-secrets` command, and the folder and file may not exist yet. Create them yourself: make the folder named under [Find the secrets file](#find-the-secrets-file), create a file called `secrets.json` in it, and type the entries in the shape shown below, one `"ApplicationSettings:<Key>": value` line per setting. The bot from the zip reads that file exactly as a built one does; see [Install from a release](01-install.md#install-from-a-release-instead-of-building).

A setting that has one value per console has a third part, the console, `DS` or `GBA`. For example:

```
dotnet user-secrets set "ApplicationSettings:MaxEmulators:DS" "4" --project ShinyBotHunter
```

The database connection is not under `ApplicationSettings`; its full name is `ConnectionStrings:DefaultConnection`.

**Expected result:** the setting appears in `secrets.json`. After a few settings the file looks like this (example values):

```json
{
  "ApplicationSettings:ROMBaseDirectory": "C:/ShinyBot/ROMs/",
  "ApplicationSettings:BizHawkBaseDirectory": "C:/ShinyBot/BizHawk/",
  "ApplicationSettings:SpriteBaseDirectory": "C:/ShinyBot/Sprites/",
  "ApplicationSettings:MaxEmulators:DS": "4",
  "ConnectionStrings:DefaultConnection": "Host=localhost;Port=5432;Database=your_db;Username=postgres;Password=your_password"
}
```

You can also edit `secrets.json` by hand, as long as you keep this flat shape. A change takes effect the next time the bot starts.

## Every setting

Every setting name below starts with `ApplicationSettings:` unless it says otherwise. The last column says what kind of value it is:

- **secret:** never share it or commit it.
- **this PC's:** a path or id that differs from PC to PC; set it in the secrets file.
- **tuning:** the shipped value works; change it only if you want to.

### Folders and files

| What it means | Setting name | Example | Kind |
|---|---|---|---|
| The folder holding your game files (ROMs). It must end with `/`. | `ROMBaseDirectory` | `C:/ShinyBot/ROMs/` | this PC's |
| The folder holding the bot's BizHawk, the one with `EmuHawk.exe` in it. It must end with `/`. | `BizHawkBaseDirectory` | `C:/ShinyBot/BizHawk/` | this PC's |
| The folder holding `Dolphin.exe`. Wishmaker Jirachi hunt only. | `DolphinBaseDirectory` | `C:/ShinyBot/Dolphin/` | this PC's |
| The GBA BIOS file. Wishmaker Jirachi hunt only. | `GbaBiosPath` | `C:/ShinyBot/Dolphin/gba_bios.bin` | this PC's |
| The Pokemon Colosseum Bonus Disc file (`.iso`). Wishmaker Jirachi hunt only. | `ColosseumBonusDiscPath` | `C:/ShinyBot/ROMs/colosseum-bonus-disc.iso` | this PC's |
| The folder holding the Pokemon pictures (sprites). It must end with `/`, and it must be the same folder as the dashboard's `spritesRootPath`. | `SpriteBaseDirectory` | `C:/ShinyBot/Sprites/` | this PC's |

The three folders that must end with `/` are joined straight onto a file name, so without the `/` the bot looks for a file that does not exist.

### Databases

| What it means | Setting name | Example | Kind |
|---|---|---|---|
| How to reach the PostgreSQL database that records every encounter: host, port, database name, user and password. | `ConnectionStrings:DefaultConnection` (not under `ApplicationSettings`) | `Host=localhost;Port=5432;Database=your_db;Username=postgres;Password=your_password` | secret, because of the password |
| Where MongoDB runs, as host and port. Leave out `mongodb://`; the bot adds it. | `MongoDbUrl` | `localhost:27017` | this PC's |
| The name of the MongoDB database the bot uses. | `MongoDbDatabase` | `ShinyBotHunter` | tuning |

### Discord

| What it means | Setting name | Example | Kind |
|---|---|---|---|
| The Discord bot's token. | `DiscordToken` | (your token) | secret |
| The id of your Discord server. | `DiscordGuildId` | `123456789012345678` | this PC's |
| The channel where shinies are announced. | `ShinyLogChannel` | `123456789012345678` | this PC's |
| The role mentioned in a shiny announcement. | `ShinyLogMention` | `123456789012345678` | this PC's |
| The channel where the bot asks whether to learn a new move. | `DiscordNewMoveChannelId` | `123456789012345678` | this PC's |

**To run without Discord**, set the token to an empty value. Edit `secrets.json` by hand and add this line inside the braces:

```json
"ApplicationSettings:DiscordToken": ""
```

The shipped value `your-token-here` does not count as "not set": with it, the bot still tries to log in to Discord.

`DiscordGuildId` and `DiscordNewMoveChannelId` must hold a number even when you run without Discord, or the bot does not start. The shipped values are enough for that.

How to make the Discord bot and find these ids is on the [Discord](05-discord.md) page.

### Emulator windows

Each of these has one value per console, `DS` and `GBA`. Every console needs an entry in each.

| What it means | Setting name | Example | Kind |
|---|---|---|---|
| How many emulators of that console may run at once. All consoles together share one pool of window places and ports. | `MaxEmulators:DS`, `MaxEmulators:GBA` | `8`, `10` | tuning |
| The width of one emulator window, in pixels. | `DisplayWidth:DS`, `DisplayWidth:GBA` | `257`, `240` | tuning |
| The height of one emulator window, in pixels. | `DisplayHeight:DS`, `DisplayHeight:GBA` | `478`, `255` | tuning |
| How far right of the screen's left edge the windows start, in pixels. | `WidthOffset:DS`, `WidthOffset:GBA` | `0`, `0` | tuning |
| How many rows the emulator windows are tiled in. | `DisplayRows:DS`, `DisplayRows:GBA` | `2`, `2` | tuning |

### Recording macros

These are for developing the bot. Leave them as they are.

| What it means | Setting name | Example | Kind |
|---|---|---|---|
| Turns the macro recorder on. Off by default; when on, the dashboard shows a Dev Mode tab. | `EnableMacroRecording` | `false` | tuning |
| A recorded button press this many frames long or shorter is saved as a tap. | `MacroRecordTapMaxFrames` | `5` | tuning |
| How close, in pixels, a recorded touch must be to a known spot to snap to it. | `MacroRecordTouchSnapRadius` | `6` | tuning |

## Dashboard settings

The dashboard reads `PokemonDashboard\config.json`:

| What it means | Field | Shipped value |
|---|---|---|
| Where the dashboard gets live updates from the bot. Port 5001. | `backendUrl` | `http://localhost:5001` |
| Where the dashboard sends its button presses and loads its lists from. Port 5000. | `apiUrl` | `http://localhost:5000` |
| The folder holding the Pokemon pictures. The same folder as the bot's `SpriteBaseDirectory`. | `spritesRootPath` | a placeholder folder |
| How often the encounter rate refreshes, in milliseconds. | `encounterRatePollingIntervalMs` | `60000` (once a minute) |
| Whether each kind of window opens: the milestone windows, Recent Encounters, and Admin. | `cards.phaseCard.enabled`, `cards.recentPokemonCard.enabled`, `cards.adminCard.enabled` | `true` |

Do not edit `config.json` for your own values. Put them in `PokemonDashboard\override-config.json` instead. That file is not committed, and the dashboard lays it over `config.json` when it starts, so it only needs the fields you change. For example:

```json
{
  "spritesRootPath": "C:/ShinyBot/Sprites"
}
```

## Something wrong with a setting?

- **You see** `appsettings has no MaxEmulators entry for console 'GBA'` (or `'DS'`).
  **It means** that console has no `MaxEmulators` value.
  **Do** add one as shown under "Change a setting" above, and start the bot again.
- **You see** `Dolphin.exe not found in …` or `GBA BIOS not found at …` when you set up Dolphin.
  **It means** `DolphinBaseDirectory` or `GbaBiosPath` is wrong or not set.
  **Do** fix the setting, then repeat the Dolphin step on the [Install](01-install.md) page.

Next: [The dashboard](03-dashboard.md)
