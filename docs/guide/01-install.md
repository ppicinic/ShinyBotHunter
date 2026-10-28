<!-- Derived from: ShinyBotHunter/ShinyBotHunter.csproj, PokemonDashboard/README.md, ShinyBotHunter/Emulators/BizHawkApiHawkLauncher.cs, ShinyBotHunter/Service/RomInfoService.cs, the fork's release notes at https://github.com/ppicinic/BizHawk/releases/tag/2.11.2-savepaths.2 and the file list of its BizHawk-2.11.2-savepaths.2-win-x64.zip, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/Program.cs, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/MainForm.cs, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/MainForm.Designer.cs, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/config/SoundConfig.Designer.cs, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.Common/config/Config.cs, ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.Common/config/PathEntryCollection.cs, https://www.tigerdata.com/docs/self-hosted/latest/install/installation-windows, https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies and https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props (neither says the SDK fetches .NET Framework reference assemblies by itself), ShinyBotHunter/Bot/StarterSaveFiles.cs, ShinyBotHunter/BotEvents/WishmakerJirachiEvent.cs, Scripts/ci-local.ps1, Scripts/deploy-apihawk.ps1, Scripts/deploy-dolphin.ps1, ShinyBotHunter/Properties/launchSettings.json, ShinyBotHunter/Startup.cs, ShinyBotHunter/ApplicationSettings.cs, ShinyBotHunter/appsettings.json, ShinyBotHunter/Tools/SetupCommands.cs, ShinyBotHunter/Scripts/PokeApiParser.cs, ShinyBotHunter/Emulators/Dolphin/DolphinLauncher.cs, ShinyBotHunter/Service/DiscordBotService.cs, ApiHawk/lib/.gitkeep, ApiHawk/ApiHawk.csproj, PokemonDashboard/package.json, PokemonDashboard/src/main/index.ts, PokemonDashboard/src/main/windowManager.ts, PokemonDashboard/src/renderer/src/components/AdminCard.tsx, PokemonDashboard/src/renderer/src/components/admin/BotControlTab.tsx, PokemonDashboard/src/renderer/src/components/admin/TopLevelActionForm.tsx, PokemonDashboard/src/renderer/src/components/admin/actionSchema.ts (gamesFor), AGENTS.md, docs/architecture/system-overview.md (sections 6 and 8), Scripts/build-release.ps1, PokemonDashboard/src/main/index.ts (config.json beside the packaged app), the owner's answers of 2026-10-27 -->
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

If building ApiHawk fails on a PC without Visual Studio because the .NET Framework 4.8 reference assemblies are missing, install the .NET Framework 4.8 Developer Pack and build again. (A later release may ship ApiHawk prebuilt, which removes this step.)

### Turn on TimescaleDB

TimescaleDB is an add-on for PostgreSQL. Install it with TimescaleDB's own Windows steps, at <https://www.tigerdata.com/docs/self-hosted/latest/install/installation-windows>. In short:

1. Download the TimescaleDB installer for your PostgreSQL version, from the releases page that page links to.
2. Unzip it. In its `timescaledb` folder, right-click `setup.exe` and choose **Run as Administrator**, then finish the steps it shows. It offers to run `timescaledb-tune`; TimescaleDB recommends accepting.
3. Restart PostgreSQL. In PowerShell, run `Restart-Service postgresql-x64-18`, with `18` replaced by your PostgreSQL version.

TimescaleDB's page warns that these steps can give errors if PostgreSQL was installed before; it says what to do in that case.

Turning TimescaleDB on inside the bot's own database is a separate command, run once per database. It is on [Updating and versions](08-updating.md#how-are-the-tables-created-and-brought-up-to-date).

### Game files you supply

You also need your own game files. This guide names only the formats the bot expects, never where to get them.

- HeartGold or SoulSilver, as a `.nds` file.
- Ruby or Sapphire, as a `.gba` file.
- For the Wishmaker Jirachi hunt only: a GBA BIOS file, and the Pokemon Colosseum Bonus Disc as an `.iso` file.

## Install from a release instead of building

Every version of the bot is also published as ready-built files on the repository's Releases page, so you can skip the build steps on this page. You still need, from [What you need](#what-you-need): Windows, PostgreSQL with TimescaleDB, MongoDB, Python, the BizHawk fork, and the .NET 10 runtime (the "ASP.NET Core Runtime", which is smaller than the SDK). You do not need Node.js. Game files are the same as for a build.

1. Open <https://github.com/ppicinic/ShinyBotHunter/releases> and, from the newest release, download `ShinyBotHunter-<version>-win-x64.zip` and `Pokemon.Dashboard.Setup.<version>.exe` (GitHub shows the installer's name with dots in place of spaces).
2. Unzip the zip into a folder of your choice.

   **Expected result:** the folder holds `backend`, `apihawk`, `Scripts`, `Dolphin`, `docs` and `README.md`.

3. Get the BizHawk fork as described under [Get the BizHawk fork](#get-the-bizhawk-fork), and open and close `EmuHawk.exe` once so its `config.ini` exists.
4. Install the bot's connector into BizHawk. Close every EmuHawk, open a terminal in the unzipped folder and run:

   ```
   powershell -File Scripts\deploy-apihawk.ps1 -BizHawkDir "<your BizHawk folder>" -BotServerDll "<the unzipped folder>\apihawk\BotServer.dll"
   ```

   **Expected result:** `Trusted in config.ini` and a `Deployed <size>kb, SHA512:…` line, as under [Install ApiHawk into BizHawk](#install-apihawk-into-bizhawk). Nothing is built.

5. Put your settings in the secrets file as the [Configure](02-configure.md) page shows. Without the .NET SDK there is no `dotnet user-secrets` command, so create the folder and `secrets.json` yourself at the path on that page and type the JSON shape it shows. The bot in the zip reads that file exactly as a built one does.
6. For the Wishmaker Jirachi hunt only, set up Dolphin as under [Set up Dolphin and the GBA BIOS](#set-up-dolphin-and-the-gba-bios-wishmaker-jirachi-hunt-only), but with two extra parameters so the script finds the shipped settings and your secrets file:

   ```
   powershell -File Scripts\deploy-dolphin.ps1 -AppSettings "<the unzipped folder>\backend\appsettings.json" -SecretsId 90bed6b9-0481-4f35-8c92-163ea1cba3cf
   ```

7. Create or update the database tables. Open a terminal in the `backend` folder and run:

   ```
   migrate.exe
   ```

   **Expected result:** it finishes without a line starting with `ERROR`. It applies every migration your database has not had yet, reading your connection string the same way the bot does. Run it again after every update that the [Versions](09-versions.md) page marks as needing a migration.

8. Fill in the Pokemon data once, from the `backend` folder:

   ```
   ShinyBotHunter.exe --parsepokeapi
   ```

   **Expected result:** as under [Fill in the Pokemon data](#fill-in-the-pokemon-data-once-per-pc).

9. Start the bot from the `backend` folder (it must be started from there, because it reads its macros, hunt settings and milestones from that folder):

   ```
   ShinyBotHunter.exe
   ```

   **Expected result:** as under [Start the bot for the first time](#start-the-bot-for-the-first-time).

10. Run `Pokemon.Dashboard.Setup.<version>.exe` and start **Pokemon Dashboard** from the Start menu.

    **Expected result:** the same windows as under [Open the dashboard and check it worked](#open-the-dashboard-and-check-it-worked). The dashboard's own settings file is `resources\config.json` inside the folder the installer created; it points at this PC, so you only edit it if the bot runs elsewhere.

To update a release install, see [How do I update the bot?](08-updating.md#how-do-i-update-the-bot).

## Why does the bot need its own BizHawk?

The bot cannot use the BizHawk you download from the BizHawk website. It needs a special build, for two reasons:

- **Touch-screen input.** The special build has a fix that makes the bot's touch-screen presses work. Without it, every HeartGold and SoulSilver touch-screen step fails.
- **One save per game copy.** The special build names each game's save after the ROM file. That lets the bot run many copies of one game side by side, each with its own save.

The special build is the `fix/4535-sticky-analog` branch of `ppicinic/BizHawk`, published as releases on that fork. The notes of release `2.11.2-savepaths.2` list the sticky-analog fix that the touch-screen presses need (#1) and Save RAM named after the ROM file (#4).

## Get the BizHawk fork

1. Open <https://github.com/ppicinic/BizHawk/releases/latest> and download the file `BizHawk-<release>-win-x64.zip` (at the time of writing, `BizHawk-2.11.2-savepaths.2-win-x64.zip`).

   **Expected result:** the zip is in your downloads.

2. Unzip it into a folder of your choice. That folder is your BizHawk folder, the one the `BizHawkBaseDirectory` setting points at (see [Configure](02-configure.md)).

   **Expected result:** `EmuHawk.exe` and an `ExternalTools` folder sit directly in that folder.

3. Double-click `EmuHawk.exe`, then close it.

   **Expected result:** a settings file `config.ini` now sits in your BizHawk folder. The ApiHawk install below needs it.

4. Optional, only if you will tick **Sound On** on the dashboard. The bot starts those emulators with a second settings file, `config_sound_on.ini`, and starts every other emulator with `config.ini`. So `config.ini` must end with the sound off, and the copy with the sound on. BizHawk's sound is on when it is first installed.
   1. Open `EmuHawk.exe`, choose **Config > Sound...**, tick **Sound Master Enable**, click **OK**, and close EmuHawk.
   2. Copy `config.ini`, rename the copy `config_sound_on.ini`, and keep it in the same folder.
   3. Open `EmuHawk.exe` again, choose **Config > Sound...**, untick **Sound Master Enable**, click **OK**, and close EmuHawk.

   **Expected result:** `config.ini` and `config_sound_on.ini` are both in your BizHawk folder.

## Copy four files from the fork into ApiHawk

ApiHawk is the bot's connector: a small add-on that runs inside each BizHawk and lets the bot press buttons and read the game's memory. It is built against four files from the fork.

1. Copy these four files from your BizHawk folder into the repository's `ApiHawk\lib` folder:
   - `dll\BizHawk.Client.Common.dll`
   - `dll\BizHawk.Common.dll`
   - `dll\BizHawk.Emulation.Common.dll`
   - `EmuHawk.exe`

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
  **Do** open and close `EmuHawk.exe` once (step 3 of [Get the BizHawk fork](#get-the-bizhawk-fork)) and run the script again.

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

**Saves.** BizHawk keeps each game's save as `<ROM file name>.SaveRAM`, in a folder for that console inside your BizHawk folder. Each copy of a ROM has its own save. The folders are BizHawk's standard ones, `NDS\SaveRAM` for HeartGold and SoulSilver and `GBA\SaveRAM` for Ruby and Sapphire, and there is nothing to set up.

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

The bot does not make its tables when it starts. You make them with one command, after turning TimescaleDB on in the database, as shown on [Updating and versions](08-updating.md#how-are-the-tables-created-and-brought-up-to-date).

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
   npm run build
   ```

   **Expected result:** it builds the dashboard into the `out` folder in three parts, each ending with a line like `✓ built in 740ms`, and finishes without errors.

3. Then run:

   ```
   npm run start
   ```

**Expected result:** two windows open:

- **Admin**, with the tabs Dashboard Config, Bot Control, Milestone Editor and Window Layout;
- **Recent Encounters**.

On the Bot Control tab, the game list for a hunt shows the ROMs in your ROM folder that fit that hunt; some hunts list only the tested revision of their game. If no ROM fits, the list is empty.

After every update, run `npm run build` again before `npm run start`.

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
