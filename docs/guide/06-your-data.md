<!-- Derived from: ShinyBotHunter/Persistence/PostgreSQL/PersistPokemonDataDao.cs, ShinyBotHunter/Persistence/MongoDb/MongoCaughtPokemonDao.cs, docs/architecture/system-overview.md, ShinyBotHunter/Persistence/PostgreSQL/PokemonDbContext.cs, ShinyBotHunter/Migrations/* (InitialCreate, V2Schema, the model snapshot), ShinyBotHunter/Persistence/MongoDb/MongoDbMetadataDao.cs, ShinyBotHunter/Persistence/MongoDb/MongoDbConnection.cs, ShinyBotHunter/Persistence/MongoDb/BaseGameState.cs, ShinyBotHunter/Service/PhaseInfoService.cs, ShinyBotHunter/Service/StarterHuntStateStore.cs, ShinyBotHunter/Service/PokemonMetadataService.cs (GetGender), ShinyBotHunter/BotEvents/StarterHuntGameState.cs, ShinyBotHunter/BotEvents/MultiShinyHunterEvent.cs, ShinyBotHunter/BotEvents/BattleOrchestratorEvent.cs (encounter time is UTC), ShinyBotHunter/Macro/GameFlavor.cs, ShinyBotHunter/Pokemon/PokemonData.cs, ShinyBotHunter/Pokemon/Gen3PokemonParser.cs, ShinyBotHunter/Pokemon/HgssPokemonParser.cs, ShinyBotHunter/appsettings.json (placeholders), PokemonDashboard/README.md (form codes), https://www.postgresql.org/docs/current/app-pgdump.html, https://www.postgresql.org/docs/current/app-pgrestore.html, https://www.tigerdata.com/docs/self-hosted/latest/backup-and-restore/logical-backup (restore sequence; no -j), https://www.mongodb.com/docs/database-tools/mongodump/, https://www.mongodb.com/docs/database-tools/mongorestore/ (--drop), ppicinic/BizHawk@2.11.2-savepaths.2:src/BizHawk.Client.EmuHawk/MainForm.cs (save written in CloseGame through FlushSaveRAM) -->
# Your data

Part of the [ShinyBotHunter guide](README.md)

The bot keeps everything it records in two databases on your PC: PostgreSQL and MongoDB. This page says what is kept where, how to look at it, and how to back it up. Looking is safe; this page never asks you to change anything in the databases.

## What does the bot keep, and where?

| What | Where |
|---|---|
| Every encounter, one row each | PostgreSQL, table `encounters` |
| Shinies the bot actually caught | MongoDB, collection `CaughtPokemon` |
| Your milestones | MongoDB, collection `PhaseInfos` |
| How far each starter hunt has got | MongoDB, collection `PhaseStates` |
| Pokémon species and move reference data, filled in when you set the bot up (see [Install](01-install.md)) | MongoDB, collections `PokemonMetadata` and `PokemonMoveMetadata` |
| Your game files and their saves, including the copies the hunts make | Your game folder (see [Install](01-install.md)) |

The database names, addresses and user are the ones in your settings; see [Configure](02-configure.md). The commands on this page use the example values from the bot's settings file. Use your own values wherever they differ.

## What is in an encounter row?

Each row of the `encounters` table is one Pokémon the bot met. The column names are written exactly as you type them in a query.

| Column | What it means |
|---|---|
| `Id` | A random unique id for the row. |
| `Timestamp` | When the encounter happened, in UTC. |
| `PID` | The Pokémon's personality value, the hidden number the game uses to decide its nature, gender and whether it is shiny. Stored as a signed number, so large values show as negative. |
| `Species` | The Pokémon's national Pokédex number, for example 152 for Chikorita. |
| `Gender` | 1 is male, 2 is female, 0 is genderless. |
| `FormCode` | The name of the Pokémon's sprite, for example `chikorita`. A female form ends in `;f`. |
| `Name` | The species name. |
| `TID` | The trainer id of the game that met it. Stored as a signed number, so large values show as negative. |
| `SID` | The secret id of that trainer. Stored as a signed number, so large values show as negative. |
| `Ability` | In Ruby and Sapphire: which of the species' two abilities it has, 0 for the first and 1 for the second. In HeartGold and SoulSilver: the ability's own number in the game. |
| `IVs` | All six IVs packed into one number, 5 bits each, from the lowest bits up: HP, Attack, Defense, Speed, Sp. Atk, Sp. Def. The two highest bits are not IVs. The Recent Encounters window shows them unpacked. |
| `Nature` | The nature as a number from 0 to 24. |
| `Level` | The Pokémon's level. |
| `ShinyValue` | The shiny value. Below 8 means shiny. Stored as a signed number, so large values show as negative. |
| `IsShiny` | `true` if the Pokémon is shiny. |
| `Game` | 0 is HeartGold or SoulSilver, 1 is Ruby or Sapphire. |
| `Milestone` | The milestone the encounter was filed under. |
| `GameFileName` | The game file that met it, or the recording name a starter hunt entry was given. |

## Look at your encounters

1. Open a terminal (PowerShell or Command Prompt).
2. Type the following, with your own user and database name from your connection string, and press Enter:

   ```
   psql -h localhost -U postgres -d your_db
   ```

3. Type your PostgreSQL password when asked. You see a prompt ending in `=#` or `=>`.
4. Paste one of the queries below and press Enter. Column names go in double quotes, because they contain capital letters.

   The newest 20 encounters:

   ```
   SELECT "Timestamp", "Name", "Level", "IsShiny", "Milestone", "GameFileName" FROM encounters ORDER BY "Timestamp" DESC LIMIT 20;
   ```

   How many encounters each milestone has:

   ```
   SELECT "Milestone", COUNT(*) FROM encounters GROUP BY "Milestone" ORDER BY COUNT(*) DESC;
   ```

   Every shiny the bot has met:

   ```
   SELECT "Timestamp", "Name", "ShinyValue", "Milestone", "GameFileName" FROM encounters WHERE "IsShiny" ORDER BY "Timestamp" DESC;
   ```

5. When a long result stops with `:` at the bottom, press `q` to get the prompt back.
6. Type `\q` and press Enter to leave.

## Look at caught Pokémon, milestones and hunt progress

1. Open a terminal.
2. Type the following, with your own MongoDB address and database name, and press Enter:

   ```
   mongosh "mongodb://localhost:27017/ShinyBotHunter"
   ```

   You see a prompt with the database name, such as `ShinyBotHunter>`.
3. Paste one of the commands below and press Enter.

   **Caught shinies.** Each record holds the full Pokémon (under `Pokemon`), the game as 0 or 1 (`Game`), the milestone (`Phase`) and the game file (`GameFileName`).

   ```
   db.CaughtPokemon.find()
   ```

   Only the ones filed under one milestone (put your milestone's name between the quotes):

   ```
   db.CaughtPokemon.find({ Phase: "YOUR MILESTONE" })
   ```

   **Milestones.** Each record holds the game (`GameName`, `HGSS` or `RS`), the milestone's name (`Name`), its list of Pokémon with the amount wanted (`Pokemon`), and whether it came with the bot (`IsStatic`).

   ```
   db.PhaseInfos.find()
   ```

   Only the Ruby and Sapphire milestones:

   ```
   db.PhaseInfos.find({ GameName: "RS" })
   ```

   **Starter hunt progress.** Each record is one hunt, with a name (`Name`) and the game as 0 or 1 (`Game`). A Ruby and Sapphire hunt is named after its milestone.
   - A Ruby and Sapphire hunt keeps the starters already caught, which copy of the game each caught shiny's save is on, the starter it is working on, the game copies it has made a save for, the copies it will never use again because their save holds a shiny, any shinies found but not yet saved, and when the hunt started and was last updated (in UTC).
   - The HeartGold and SoulSilver hunt keeps which stage of the hunt it has reached, the starters it has found, and which of its game copies are finished.

   ```
   db.PhaseStates.find()
   ```

   Only the Ruby and Sapphire hunts:

   ```
   db.PhaseStates.find({ Game: 1 })
   ```

4. Type `exit` and press Enter to leave.

Make and change milestones in the dashboard, never here; see [The dashboard](03-dashboard.md).

## Back up your data

There are three things to copy: the PostgreSQL database, the MongoDB database, and your game folder with its saves. The commands below come with PostgreSQL and the MongoDB database tools themselves; they are not part of the bot.

1. Open a terminal in the folder where you want the backup to go.
2. Back up PostgreSQL. Type the following, with your own user and database name, and press Enter:

   ```
   pg_dump -h localhost -U postgres -d your_db -F c -f shinybothunter-postgres.dump
   ```

   Type your PostgreSQL password when asked. You get a file `shinybothunter-postgres.dump`.
3. Back up MongoDB. Type the following, with your own address and database name, and press Enter:

   ```
   mongodump --uri "mongodb://localhost:27017/ShinyBotHunter" --out shinybothunter-mongo
   ```

   You get a folder `shinybothunter-mongo` with one folder inside it named after the database.
4. Copy your game folder, with its saves, next to the two backups. Where the game folder is: see [Install](01-install.md). Close every BizHawk first: BizHawk writes a save to disk only when it closes the game.

## Restore a backup

Run these in a terminal in the folder that holds your backup.

1. Make an empty PostgreSQL database with TimescaleDB on. Open `psql` as in [Look at your encounters](#look-at-your-encounters). If your database does not exist yet, open it with `-d postgres` instead, and first run `CREATE DATABASE your_db;` with your own database name. Then run these one at a time:

   ```
   \c your_db
   CREATE EXTENSION IF NOT EXISTS timescaledb;
   SELECT timescaledb_pre_restore();
   ```

   **Expected result:** no line starting with `ERROR`.

2. Leave `psql` with `\q`, then type the following and press Enter:

   ```
   pg_restore -h localhost -U postgres -Fc -d your_db shinybothunter-postgres.dump
   ```

   Type your PostgreSQL password when asked. Do not add `-j`: TimescaleDB's documents say it does not restore TimescaleDB's own tables correctly.

3. Open `psql` on `your_db` again, as in [Look at your encounters](#look-at-your-encounters), and run:

   ```
   SELECT timescaledb_post_restore();
   ```

   **Expected result:** no line starting with `ERROR`. The steps come from TimescaleDB's page <https://www.tigerdata.com/docs/self-hosted/latest/backup-and-restore/logical-backup>.

4. Restore MongoDB. Type the following and press Enter:

   ```
   mongorestore --uri "mongodb://localhost:27017" --drop shinybothunter-mongo
   ```

   `--drop` first deletes each collection that is in the backup, so the restored data replaces what is there; collections that are not in the backup are left alone.

5. Close every BizHawk, then copy your saves back into the game folder.

Next: [When something goes wrong](07-when-something-goes-wrong.md)
