<!-- Derived from: the repository's git tags (v0.1.0 at 123b4c4, tagged 2026-10-19), ShinyBotHunter/Migrations/* (which migration each version carries, from `git ls-tree v0.1.0 ShinyBotHunter/Migrations/` against main), docs/guide/08-updating.md, the owner's answer of 2026-10-27 (v0.1.1 is tagged on the merge of the guide to main, 2026-10-27) -->
# Versions

Part of the [ShinyBotHunter guide](README.md)

This page lists the bot's versions and says, for each one, whether your encounter database needs a migration. How to run a migration is on [Updating and versions](08-updating.md#is-a-database-migration-needed).

## Which version are you on?

1. Open a terminal in the repository folder and run:

   ```
   git describe --tags
   ```

**Expected result:** the version, for example `v0.1.0`. If it prints something like `v0.1.0-12-gabc1234`, you are 12 changes past `v0.1.0` and on the way to the next version.

## Which versions are there?

| Version | Released | Database migration needed? | What to do |
|---|---|---|---|
| v0.1.0 | 2026-10-19 | Yes. It carries the two migrations that create the `encounters` table and set its column sizes. | On a new PC, after [Install](01-install.md), run the migration before starting the bot for the first time. |
| v0.1.1 | 2026-10-27 | Yes. It adds the migration that puts an index on the `encounters` table. | After [updating](08-updating.md#how-do-i-update-the-bot) to it, run the migration before starting the bot. |

A version needs a migration when it brings a new file under the bot's `ShinyBotHunter\Migrations` folder. [Is your database behind?](08-updating.md#is-your-database-behind) shows how to check which migrations your database has already had.

Next: [Back to the guide](README.md)
