<!-- Derived from: ShinyBotHunter/ShinyBotHunter.csproj, PokemonDashboard/README.md, ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Service/RomInfoService.cs, the README of the BizHawk fork (ppicinic/BizHawk, branch fix/4535-sticky-analog), Scripts/ci-local.ps1, Scripts/deploy-apihawk.ps1, Scripts/deploy-dolphin.ps1, ShinyBotHunter/Properties/launchSettings.json, ShinyBotHunter/Startup.cs, ShinyBotHunter/ApplicationSettings.cs, ShinyBotHunter/appsettings.json, ShinyBotHunter/Tools/SetupCommands.cs, ShinyBotHunter/Scripts/PokeApiParser.cs, ShinyBotHunter/Emulators/Dolphin/DolphinLauncher.cs, ShinyBotHunter/Service/DiscordBotService.cs, ApiHawk/lib/.gitkeep, ApiHawk/ApiHawk.csproj, PokemonDashboard/package.json, PokemonDashboard/src/main/index.ts, PokemonDashboard/src/renderer/src/components/AdminCard.tsx, PokemonDashboard/src/renderer/src/components/admin/BotControlTab.tsx, PokemonDashboard/src/renderer/src/components/admin/TopLevelActionForm.tsx, PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (gamesFor), AGENTS.md, docs/architecture/system-overview.md (sections 6 and 8) -->
# Install

Part of the [ShinyBotHunter guide](README.md)

This page takes you from an empty PC to a running bot with the dashboard open. Follow it from the top. Some steps need a setting first; the [Configure](02-configure.md) page explains every setting and how to set it.

## What you need

Install these programs first. After each one, run its check in a new terminal.

| Program | Why | Check |
|---|---|---|
| Windows | The bot and its emulators run only on Windows. | |
| The .NET 10 SDK | Builds and runs the bot. | `dotnet --list-sdks` lists a line starting with `10.` |
| Node.js 18 or newer | Builds and runs the dashboard. | `node --version` prints `v18` or higher |
| Python | The ApiHawk install script uses it to edit BizHawk's settings file. | `python --version` prints a version |
| PostgreSQL, with the TimescaleDB extension | Records every encounter. | `psql --version` prints a version |
| MongoDB, on its usual address `localhost:27017` | Holds the Pokemon data, your milestones and your caught Pokemon. | `mongod --version` prints a version |
| The Microsoft Visual C++ redistributable (x64) | BizHawk needs it to start. | |
| Dolphin 2609a | Wishmaker Jirachi hunt only. | |

> **Ask the owner:** how TimescaleDB is installed and turned on for PostgreSQL on Windows, and whether building ApiHawk (a .NET Framework 4.8 project) needs anything beyond the .NET 10 SDK.

### Game files you supply

You also need your own game files. This guide names only the formats the bot expects, never where to get them.

- HeartGold or SoulSilver, as a `.nds` file.
- Ruby or Sapphire, as a `.gba` file.
- For the Wishmaker Jirachi hunt only: a GBA BIOS file, and the Pokemon Colosseum Bonus Disc as an `.iso` file.

## Why does the bot need its own BizHawk?

The bot cannot use the BizHawk you download from the BizHawk website. It needs a special build, for two reasons:

- **Touch-screen input.** The special build has a fix that makes the bot's touch-screen presses work. Without it, every HeartGold and SoulSilver touch-screen step fails.
- **One save per game copy.** The special build names each game's save after the ROM file. That lets the bot run many copies of one game side by side, each with its own save.

The special build is the `fix/4535-sticky-analog` branch of `ppicinic/BizHawk`, version 2.11.2.

## Get and build the BizHawk fork

1. In a terminal, in the folder where you keep code, run:

   ```
   git clone --branch fix/4535-sticky-analog https://github.com/ppicinic/BizHawk.git
   ```

2. Build it with the fork's `Dist/BuildRelease.sh` script, not with a plain `dotnet build`. A plain build stamps BizHawk as version 1.0.0.0, and the bot's connector then fails to load into it. The fork's own building notes are at <https://github.com/ppicinic/BizHawk/tree/fix/4535-sticky-analog#building>.

**Expected result:** the fork folder has an `output` folder with `EmuHawk.exe` in it.

> **Ask the owner:** how to run `Dist/BuildRelease.sh` on Windows (from Git Bash?), and what it needs installed. The fork's README names only Visual Studio 2022 or `dotnet build BizHawk.sln` for Windows, and a plain build is the one that does not work.

> **Ask the owner:** whether the `output` folder is itself the BizHawk folder the bot points at (the `BizHawkBaseDirectory` setting), or is copied somewhere else.

> **Ask the owner:** whether you must open and close `EmuHawk.exe` once so that its settings file `config.ini` exists (the ApiHawk install below skips a missing one), and how `config_sound_on.ini` is made (the bot starts emulators that have sound on with it, and the repository does not ship it).

## Copy four files from the fork into ApiHawk

ApiHawk is the bot's connector: a small add-on that runs inside each BizHawk and lets the bot press buttons and read the game's memory. It is built against four files from the fork.

1. Copy these four files from the fork folder into the repository's `ApiHawk\lib` folder:
   - `output\dll\BizHawk.Client.Common.dll`
   - `output\dll\BizHawk.Common.dll`
   - `output\dll\BizHawk.Emulation.Common.dll`
   - `output\EmuHawk.exe`

**Expected result:** `ApiHawk\lib` holds those four files (and the `.gitkeep` that was already there).

## Install ApiHawk into BizHawk

The script `Scripts\deploy-apihawk.ps1` does three things:

- it builds ApiHawk;
- it copies it into the `ExternalTools` folder inside your BizHawk folder;
- it marks it as trusted in BizHawk's settings files `config.ini` and `config_sound_on.ini`. Without that, BizHawk asks a yes/no question before loading it, and an emulator the bot starts never gets an answer.

It takes two parameters:

- **Your BizHawk folder** (`-BizHawkDir`). Always pass it: its default is a folder on the owner's PC.
- **The repository folder** (`-RepoDir`). Leave it out; it defaults to the folder the script is in.

Steps:

1. Close every BizHawk window.
2. In a terminal in the repository folder, run:

   ```
   powershell -File Scripts\deploy-apihawk.ps1 -BizHawkDir "<your BizHawk folder>"
   ```

**Expected result:** the script prints `Trusted in config.ini` (and `Trusted in config_sound_on.ini` if that file exists), then a line like `Deployed <size>kb, SHA512:…`.

Run it again after every BizHawk rebuild and every bot update. Each new build of ApiHawk has a new fingerprint, and BizHawk trusts only the fingerprint it was given.

If it goes wrong:

- **You see** `EmuHawk is running. Close it first…`
  **It means** a BizHawk window is still open.
  **Do** close it and run the script again.
- **You see** `ApiHawk build failed.`
  **It means** usually that the four files in `ApiHawk\lib` are missing.
  **Do** repeat "Copy four files from the fork into ApiHawk" above.
- **You see** no `Trusted in` line.
  **It means** the script found no `config.ini` in your BizHawk folder.
  **Do** see the third "Ask the owner" note under "Get and build the BizHawk fork".

## Set up Dolphin and the GBA BIOS (Wishmaker Jirachi hunt only)

Skip this section unless you want the Wishmaker Jirachi hunt.

Before you start, set `DolphinBaseDirectory` (the folder with `Dolphin.exe`) and `GbaBiosPath` (your GBA BIOS file) as shown on the [Configure](02-configure.md) page. The script reads them from `appsettings.json` and then from your secrets file.

The script `Scripts\deploy-dolphin.ps1` creates a `User-ShinyBotHunter\Config` folder inside your Dolphin folder with three settings files. They put a GBA on controller port 2 that starts from your BIOS, let Dolphin take input while its window is in the background, and hand controller 1 to the bot. Your own Dolphin settings are not touched: the bot always starts Dolphin on this separate folder.

Steps:

1. Close Dolphin.
2. In a terminal in the repository folder, run:

   ```
   powershell -File Scripts\deploy-dolphin.ps1
   ```

**Expected result:** the script prints `Deployed Dolphin config to … with GBA BIOS …`.

The GBA BIOS and the Bonus Disc are files you supply. Put them in any folder and point the settings `GbaBiosPath` and `ColosseumBonusDiscPath` at them.

If it goes wrong:

- **You see** `Dolphin is running. Close it first…`
  **It means** Dolphin is still open, and it would undo the change when it closes.
  **Do** close Dolphin and run the script again.
- **You see** `Dolphin.exe not found in …`
  **It means** `DolphinBaseDirectory` is wrong or not set.
  **Do** fix it as shown on the [Configure](02-configure.md) page and run the script again.
- **You see** `GBA BIOS not found at …`
  **It means** `GbaBiosPath` is wrong or not set.
  **Do** fix it as shown on the [Configure](02-configure.md) page and run the script again.

## Where do the game files go?

**ROMs.** Put them directly in one folder, not in folders inside it, and set that folder as `ROMBaseDirectory`. Each file must end in `.nds` or `.gba`. Any file name works: the bot reads which game a file is from the file itself, not from its name. The name without its ending is what the dashboard shows for that game.

The bot recognises HeartGold, SoulSilver, Ruby and Sapphire. HeartGold and SoulSilver are tested on revision 0 of the game, Ruby and Sapphire on revision 2. Other revisions still run, with a warning in the bot's window. Emerald, FireRed and LeafGreen are recognised by name only; there is no hunt for them yet.

**Saves.** BizHawk keeps each game's save as `<ROM file name>.SaveRAM`, in a folder for that console inside your BizHawk folder. Each copy of a ROM has its own save.

> **Ask the owner:** the exact save folder names inside the BizHawk folder (for example `NDS\SaveRAM`).

**Sprites.** Make one folder for the Pokemon pictures, and inside it four empty folders: `Default`, `Shiny`, `Female_Default` and `Female_Shiny`. Set that folder as `SpriteBaseDirectory` and as the dashboard's `spritesRootPath`. "Fill in the Pokemon data" below fills the four folders.

## Build the bot and the dashboard

1. In a terminal in the repository folder, run:

   ```
   dotnet build ShinyBotHunter\ShinyBotHunter.csproj -c Release
   ```

   **Expected result:** the build finishes without errors.

2. Then run:

   ```
   cd PokemonDashboard
   npm ci
   ```

   **Expected result:** it finishes without errors.

## Fill in the Pokemon data (once per PC)

The bot needs a table of every Pokemon, move and sprite. It downloads them once from pokeapi.co.

You need:

- MongoDB running;
- the sprite folder and its four folders made, as described under "Where do the game files go?";
- the MongoDB and sprite settings set, as shown on the [Configure](02-configure.md) page;
- an internet connection.

Steps:

1. In a terminal in the repository folder, run:

   ```
   dotnet run --project ShinyBotHunter -c Release -- --parsepokeapi
   ```

**Expected result:** it prints progress such as `Scraping move metadata...` and `Fetching Dex entries...`, ends with `Saved to MongoDB successfully!`, and exits by itself. The sprite folders now hold pictures.

## Create the encounter database

1. Create an empty PostgreSQL database whose name matches the `Database=` part of your connection string. The connection string is set on the [Configure](02-configure.md) page.

> **Ask the owner:** how the database's tables are created on a new PC. Nothing in the bot or its scripts creates them, and the TimescaleDB extension must be turned on in the database before they are.

## Start the bot for the first time

1. Make sure PostgreSQL and MongoDB are running.
2. In a terminal in the repository folder, run:

   ```
   dotnet run --project ShinyBotHunter -c Release
   ```

   Start it this way, with `dotnet run`. It makes the bot listen on ports 5000 and 5001, which the dashboard expects.

**Expected result:** the window shows `Scanned N ROM(s) in <your ROM folder>.`, where N is the number of games found. If you set the Discord token to an empty value (see [Configure](02-configure.md)), it also shows `Discord bot token is not configured. Skipping Discord bot startup.`

To stop the bot, type `end` in its window and press Enter.

## Open the dashboard and check it worked

1. Leave the bot running.
2. In a second terminal, in the `PokemonDashboard` folder, run:

   ```
   npm run dev
   ```

**Expected result:** two windows open:

- **Admin**, with the tabs Dashboard Config, Bot Control, Milestone Editor and Window Layout;
- **Recent Encounters**.

On the Bot Control tab, the game list for a hunt shows the ROMs in your ROM folder that fit that hunt; some hunts list only the tested revision of their game. If no ROM fits, the list is empty.

> **Ask the owner:** whether operators should start the dashboard with `npm run dev`, or install it with `npm run dist`.

How to use the dashboard is on [The dashboard](03-dashboard.md) page.

## Something went wrong?

- **You see** `ROM directory … does not exist.` in the bot's window.
  **It means** the `ROMBaseDirectory` setting points at a folder that is not there.
  **Do** fix it as shown on the [Configure](02-configure.md) page, and start the bot again.
- **You see** `Could not read ROM header from …; skipping.`
  **It means** that file is not a game the bot can read.
  **Do** check the file is a complete `.nds` or `.gba` dump; the bot carries on without it.
- **You see** the dashboard does not open at all.
  **It means** `PokemonDashboard\config.json` (or `override-config.json`) is missing or is not valid JSON.
  **Do** check both files; see [Dashboard settings](02-configure.md) on the Configure page.

More problems and their fixes are on [When something goes wrong](07-when-something-goes-wrong.md).

Next: [Configure](02-configure.md)
