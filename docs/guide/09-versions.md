<!-- Derived from: the repository's git tags (v0.1.0 at 123b4c4, tagged 2026-10-19), ShinyBotHunter/Migrations/* (which migration each version carries, from `git ls-tree v0.1.0 ShinyBotHunter/Migrations/` against main), docs/guide/08-updating.md, the owner's answer of 2026-10-27 (v0.1.1 is tagged on the merge of the guide to main, 2026-10-27), the owner's request of 2026-11-17 (v0.1.2 is tagged after the merge of Ruby and Sapphire navigation and training, #474; `git diff --name-status v0.1.1 main -- ShinyBotHunter/Migrations` is empty), the owner's request of 2026-11-19 (v0.1.2 is tagged on the merge of appearance per PC, #483, in place of the tag of 2026-11-18), docs/guide/01-install.md (Install from a release) -->
# Versions

Part of the [ShinyBotHunter guide](README.md)

This page lists the bot's versions and says, for each one, whether your encounter database needs a migration. How to run a migration is on [Updating and versions](08-updating.md#is-a-database-migration-needed).

## Which version are you on?

1. Open a terminal in the repository folder and run:

   ```
   git describe --tags
   ```

**Expected result:** the version, for example `v0.1.0`. If it prints something like `v0.1.0-12-gabc1234`, you are 12 changes past `v0.1.0` and on the way to the next version.

**Installed from a release?** There is no repository folder. Your version is in the name of the zip you unzipped, `ShinyBotHunter-<version>-win-x64.zip`, and of the installer you ran; see [Install from a release](01-install.md#install-from-a-release-instead-of-building).

## Which versions are there?

| Version | Released | Database migration needed? | What to do |
|---|---|---|---|
| v0.1.0 | 2026-10-19 | Yes. It carries the two migrations that create the `encounters` table and set its column sizes. | On a new PC, after [Install](01-install.md), run the migration before starting the bot for the first time. |
| v0.1.1 | 2026-10-27 | Yes. It adds the migration that puts an index on the `encounters` table. | After [updating](08-updating.md#how-do-i-update-the-bot) to it, run the migration before starting the bot. |
| v0.1.2 | 2026-11-19 | No. It brings no new migration. | [Update](08-updating.md#how-do-i-update-the-bot) and start the bot. From this version the dashboard's themes are saved on each bot's own PC; on its first start the bot copies the themes you had. |

A version needs a migration when it brings a new file under the bot's `ShinyBotHunter\Migrations` folder. [Is your database behind?](08-updating.md#is-your-database-behind) shows how to check which migrations your database has already had.

Next: [Back to the guide](README.md)
