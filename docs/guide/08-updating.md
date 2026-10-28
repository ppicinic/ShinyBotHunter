<!-- Derived from: ShinyBotHunter/ShinyBotHunter.csproj (TargetFramework, package versions, UserSecretsId), ShinyBotHunter/Migrations/* (InitialCreate, V2Schema, EncountersCountsIndex, the model snapshot), ShinyBotHunter/Persistence/PostgreSQL/PokemonDbContext.cs, ShinyBotHunter/Startup.cs (MainAsync up to builder.Build(); UseTimescaleDb; AddUserSecrets; no Migrate or EnsureCreated; the "end" command), ShinyBotHunter/Tools/SetupCommands.cs, ShinyBotHunter/Migrations/20260511164802_InitialCreate.cs (create_hypertable), docs/specs/qa-automation.md (line 155), https://learn.microsoft.com/ef/core/cli/dbcontext-creation (From application services), https://learn.microsoft.com/ef/core/cli/dotnet (installing the tools, verify installation, dotnet ef database update), https://www.tigerdata.com/docs/self-hosted/latest/install/installation-windows (CREATE EXTENSION IF NOT EXISTS timescaledb, on each database), the fork's releases page https://github.com/ppicinic/BizHawk/releases, Scripts/deploy-apihawk.ps1, Scripts/deploy-dolphin.ps1, ApiHawk/lib/.gitkeep, ShinyBotHunter/Emulators/Dolphin/DolphinLauncher.cs, PokemonDashboard/package.json, PokemonDashboard/README.md, AGENTS.md, docs/guide/01-install.md, docs/guide/02-configure.md, docs/guide/06-your-data.md -->
# Updating and versions

Part of the [ShinyBotHunter guide](README.md)

This page lists the versions of the programs the bot is built and tested against, and how to update the bot to new code. It also shows how to tell whether your encounter database needs a migration.

## Which versions does the bot use?

Where nothing in the bot says which version it needs, the table says unknown. "Tested on" is the version on the PC the bot's QA runs on.

| Program | Version | Where this comes from |
|---|---|---|
| Windows | unknown; 64-bit, since the bot uses a 64-bit Windows-only part | the bot's project file (`ShinyBotHunter.csproj`, its `win-x64` package) |
| .NET SDK | 10 | the bot's project file (`ShinyBotHunter.csproj`, `net10.0`) |
| Node.js | 18 or newer | the dashboard's README (`PokemonDashboard/README.md`) |
| PostgreSQL | unknown; tested on 17 | the bot's QA plan (`docs/specs/qa-automation.md`, "QA databases") |
| TimescaleDB (a PostgreSQL extension) | unknown; tested on 2.30.2; required, because the bot connects with TimescaleDB turned on and the first migration makes a TimescaleDB table | the bot's startup code (`Startup.cs`), the first migration, and the bot's QA plan (`docs/specs/qa-automation.md`, "QA databases") |
| MongoDB | unknown | none |
| BizHawk | the latest release of the fork `ppicinic/BizHawk` (at the time of writing, `2.11.2-savepaths.2`) | the fork's Releases page and [Get the BizHawk fork](01-install.md#get-the-bizhawk-fork) |
| Dolphin | 2609a (Wishmaker Jirachi hunt only) | the bot's Dolphin launcher (`DolphinLauncher.cs`: "checked on Dolphin 2609a") |
| Python | 3, no exact version | the ApiHawk install script (`Scripts/deploy-apihawk.ps1`; its settings edit is Python 3 code) |

How to install each one is on [Install](01-install.md#what-you-need).

## How do I update the bot?

Your own settings are in the secrets file on this PC, outside the repository folder, so getting new code does not change them; see [Configure](02-configure.md).

1. Stop the bot: type `end` in its window and press Enter. Close the dashboard and every BizHawk and Dolphin window.

   **Expected result:** no bot, BizHawk or Dolphin window is open.

2. Get the new code. Stay on the `main` branch, which is what cloning the repository gives you; finished work is merged there. In a terminal in the repository folder, run:

   ```
   git pull
   ```

   **Expected result:** git lists the files that changed, or prints `Already up to date.` Keep that list; steps 6 and 7 use it.

3. Rebuild the bot, with the same command as in [Build the bot and the dashboard](01-install.md#build-the-bot-and-the-dashboard):

   ```
   dotnet build ShinyBotHunter\ShinyBotHunter.csproj -c Release
   ```

   **Expected result:** the build finishes without errors.

4. Rebuild the dashboard, with the same commands as in [Build the bot and the dashboard](01-install.md#build-the-bot-and-the-dashboard):

   ```
   cd PokemonDashboard
   npm ci
   ```

   **Expected result:** it finishes without errors.

5. Reinstall ApiHawk. In a terminal in the repository folder, run:

   ```
   powershell -File Scripts\deploy-apihawk.ps1 -BizHawkDir "<your BizHawk folder>"
   ```

   **Expected result:** the same result as on the install page.

   Do this after every BizHawk rebuild and every bot update. Each new build of ApiHawk has a new fingerprint, and BizHawk trusts only the fingerprint it was given. The parameters, and what to do if it goes wrong, are in [Install ApiHawk into BizHawk](01-install.md#install-apihawk-into-bizhawk).

6. For the Wishmaker Jirachi hunt only: rerun the Dolphin setup if the list from step 2 includes files under `Dolphin/Config`, or if you changed `DolphinBaseDirectory` or `GbaBiosPath`. In a terminal in the repository folder, run:

   ```
   powershell -File Scripts\deploy-dolphin.ps1
   ```

   **Expected result:** the script prints `Deployed Dolphin config to … with GBA BIOS …`. More is in [Set up Dolphin and the GBA BIOS](01-install.md#set-up-dolphin-and-the-gba-bios-wishmaker-jirachi-hunt-only).

7. If the list from step 2 includes a new file under `ShinyBotHunter/Migrations`, your database needs a migration. Apply it as in [How are the tables created and brought up to date?](#how-are-the-tables-created-and-brought-up-to-date) before you start the bot.

8. Start the bot and the dashboard as in [Start the bot for the first time](01-install.md#start-the-bot-for-the-first-time) and [Open the dashboard and check it worked](01-install.md#open-the-dashboard-and-check-it-worked).

   **Expected result:** the same messages as on the install page.

### Updating a release install

If you installed from a release rather than building, skip the steps above: download the new zip and installer from the Releases page, unzip the zip over your old folder (your settings live in the secrets file outside it, so nothing of yours is overwritten), run the installer, and reinstall the connector with the `-BotServerDll` command from [Install from a release](01-install.md#install-from-a-release-instead-of-building). Then, if the [Versions](09-versions.md) page says the new version needs a migration, run `migrate.exe` from the `backend` folder before starting the bot.

## Is a database migration needed?

### What is a migration?

The encounter database in PostgreSQL holds one table, `encounters`. When a new version of the bot changes the shape of that table (creates it, changes a column, adds an index), it ships a migration: a small, named step that makes the change. Each database records the steps it has had in a table called `__EFMigrationsHistory`. A database that is missing a step the bot ships is "behind".

### Which migrations ship with the bot?

These ship with the bot today, oldest first. The name is what your database records.

| Name | Date | What it changes |
|---|---|---|
| `20260511164802_InitialCreate` | 11 May 2026 | Creates the `encounters` table, with one column per encounter field, and makes it a TimescaleDB table stored in one-day pieces. |
| `20260601031515_V2Schema` | 1 June 2026 | Makes six columns smaller: trainer id, secret id, species and shiny value become small whole numbers, and PID and IVs become normal whole numbers instead of big ones. |
| `20261021120000_EncountersCountsIndex` | 21 October 2026 | Adds an index named `encounters_game_milestone_file_time` on game, milestone, game file and time (newest first), so the database can find a game's encounters for a milestone without reading the whole table. |

### Is your database behind?

1. Open `psql` on your database as in steps 1 to 3 of [Look at your encounters](06-your-data.md#look-at-your-encounters).

   **Expected result:** a prompt ending in `=#` or `=>`.

2. Paste this and press Enter:

   ```
   SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
   ```

   **Expected result:** one row per migration your database has had, oldest first.

3. Compare the `MigrationId` column with the Name column in the table above. Any name in the table that is missing from your result is a migration your database has not had; your database is behind.

If it goes wrong:

- **You see** `ERROR:  relation "__EFMigrationsHistory" does not exist`
  **It means** no migration was ever applied to this database.
  **Do** create the tables as in the next section.

### How are the tables created and brought up to date?

The bot does not create or change its tables when it starts. You do it with Microsoft's tool for migrations, `dotnet ef`. Steps 1 and 2 are once per PC and once per database; step 3 is on a new PC and after every update that adds a migration.

1. **Install the tool once.** In a terminal, run:

   ```
   dotnet tool install --global dotnet-ef
   ```

   Then run `dotnet ef`.

   **Expected result:** it prints a line `Entity Framework Core .NET Command-line Tools` followed by its version.

2. **Turn on TimescaleDB in the database, once.** Open `psql` on your database as in steps 1 to 3 of [Look at your encounters](06-your-data.md#look-at-your-encounters), paste this and press Enter:

   ```
   CREATE EXTENSION IF NOT EXISTS timescaledb;
   ```

   **Expected result:** no line starting with `ERROR`.

   The first migration needs it: it makes the `encounters` table a TimescaleDB table with `SELECT create_hypertable('public."encounters"', 'Timestamp', chunk_time_interval => INTERVAL '1 day');`, which fails without TimescaleDB.

3. **Create or update the tables.** In a terminal in the repository folder, run:

   ```
   dotnet ef database update --project ShinyBotHunter
   ```

   **Expected result:** it builds the bot and finishes without an error. The check in [Is your database behind?](#is-your-database-behind) now lists every migration in the table above.

   The tool reads your connection string the same way the bot does, from `appsettings.json` and then your secrets file. It does not start the bot, Discord or any emulator.

Next: [Versions](09-versions.md)
