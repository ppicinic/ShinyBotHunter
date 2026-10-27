<!-- Derived from: ShinyBotHunter/ShinyBotHunter.csproj (TargetFramework, package versions, UserSecretsId), ShinyBotHunter/Migrations/* (InitialCreate, V2Schema, EncountersCountsIndex, the model snapshot), ShinyBotHunter/Persistence/PostgreSQL/PokemonDbContext.cs, ShinyBotHunter/Startup.cs (UseTimescaleDb; AddUserSecrets; no Migrate or EnsureCreated; the "end" command), docs/specs/qa-automation.md (lines 155, 157 and 330), Scripts/deploy-apihawk.ps1, Scripts/deploy-dolphin.ps1, ApiHawk/lib/.gitkeep, ShinyBotHunter/Emulators/Dolphin/DolphinLauncher.cs, PokemonDashboard/package.json, PokemonDashboard/README.md, AGENTS.md, docs/guide/01-install.md, docs/guide/02-configure.md, docs/guide/06-your-data.md -->
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
| BizHawk | the fork `ppicinic/BizHawk`, branch `fix/4535-sticky-analog`, built as 2.11.2 | `AGENTS.md` and `ApiHawk/lib/.gitkeep` |
| Dolphin | 2609a (Wishmaker Jirachi hunt only) | the bot's Dolphin launcher (`DolphinLauncher.cs`: "checked on Dolphin 2609a") |
| Python | 3, no exact version | the ApiHawk install script (`Scripts/deploy-apihawk.ps1`; its settings edit is Python 3 code) |

> **Ask the owner:** which versions of Windows, PostgreSQL, TimescaleDB, MongoDB and Python you run the bot on.

How to install each one is on [Install](01-install.md#what-you-need).

## How do I update the bot?

Your own settings are in the secrets file on this PC, outside the repository folder, so getting new code does not change them; see [Configure](02-configure.md).

1. Stop the bot: type `end` in its window and press Enter. Close the dashboard and every BizHawk and Dolphin window.

   **Expected result:** no bot, BizHawk or Dolphin window is open.

2. Get the new code. In a terminal in the repository folder, run:

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

7. If the list from step 2 includes a new file under `ShinyBotHunter/Migrations`, your database needs a migration. See [Is a database migration needed?](#is-a-database-migration-needed) before you start the bot.

8. Start the bot and the dashboard as in [Start the bot for the first time](01-install.md#start-the-bot-for-the-first-time) and [Open the dashboard and check it worked](01-install.md#open-the-dashboard-and-check-it-worked).

   **Expected result:** the same messages as on the install page.

> **Ask the owner:** which branch operators should be on when they run git pull (main?).

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
  **It means** no migration was ever recorded in this database: its tables were made some other way, or never made.
  **Do** read the next section.

### How are the tables created and brought up to date?

Today nothing does this for you. The bot does not create or change its tables when it starts, and none of the scripts in the `Scripts` folder do either. The usual tool for applying migrations, `dotnet ef`, cannot be used on this bot as it stands: the bot has no setup for that tool, so the tool would start the whole bot instead. The bot's QA plan says its test harness applies the migrations itself, but no code in the repository does that yet. This is a gap in the bot, not a step you missed.

> **Ask the owner:** how you create the tables on a new PC, and how you apply a new migration to an existing database, step by step, until the bot does this itself.

When the bot gains a way to do this, this page will describe it.

Next: [Back to the guide](README.md)
