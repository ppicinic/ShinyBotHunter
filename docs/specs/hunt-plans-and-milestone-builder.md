# Spec: hunt plans and the milestone builder

Status: Implemented on `feature/hunt-plans-milestones` (2026-11-25, epic #508); reworked there by the [milestone builder rework](milestone-builder-rework.md) (2026-11-27), which awaits the end-of-feature QA. See "As built", "What diverged" and "Evidence".
Last updated: 2026-11-27
Owner: Phil
Related: [research: the time mover](../research/time-mover.md) (the clock, the walk, the dashboard window), [research: the milestone builder](../research/milestone-builder.md) (the data, the encounter rule, the memory reads, the anchors, the name mapping), [headbutt research](../research/headbutt.md), [pathfinder spec](pathfinder-maps-flags-hms.md)

## Summary

**2026-11-27, the rework.** The owner rejected the generated milestones in the picker and the goal per game file ([rework](milestone-builder-rework.md)). A milestone is now always a saved one: "New from game data" fills a new one from a location's species and a preset, its amounts are its own, and it is offered on the game files ticked for it. A plan is now a list of whole hunts (a Wild Encounter, or Wait) with time rules, run by its own "Plan" action and edited in its own tab. The rows below are rewritten to that; the paragraphs under this one are as written on 2026-11-24.

Two things that share one model.

**The milestone builder.** Today a milestone is typed in by hand: a name, a list of species, an amount each, and one copy per game file ("1-4 Route 30", "1-4b Route 30", "1-4c Route 30"). This replaces the typing with data. Every wild location and way of hunting it becomes a milestone generated from the game's own tables: 398 per version in HeartGold and SoulSilver once maps that share a table are merged (the spikes counted 466 and 283 with every map separate; see the changelog). What a game file still needs is its **goal**, held once per game file, not once per milestone. A milestone on screen is the game's table seen through that file's goal: the species still wanted there, their forms, how many are left, and the share of encounters each should be right now.

**Hunt plans.** A Wild Encounter can be given rules: at night go to Route 30, on Fridays go to Union Cave, otherwise hunt Route 32. The bot reads the in-game clock, walks to the milestone's hunting spot when the rule changes, and the dashboard follows.

The spikes proved the parts on HeartGold: the hunt walked between Route 32 and Route 30 on the in-game clock in three runs; every milestone and its tables were generated for both versions; a hunting spot was generated for every grass and surf milestone and one was hunted on; the Repel counter and the live clock were found in memory.

Ruby and Sapphire get the milestone builder in the same feature, with their own tables and their own encounter rule, so the two games do not drift. They have no time of day, so hunt plans are HeartGold and SoulSilver only. Nothing of Ruby and Sapphire was run on the emulator for this spec.

## Terms

| Term | Meaning |
|---|---|
| Milestone | A saved milestone: a name, rows of a species or one form with an amount each, and three optional fields: a location (a generated entry of FR-1 to FR-5, one map or several that share a table and one way of hunting it, with its slots and anchor), the game files it is ticked for, and archived. Made in the Milestone Editor from a location's data or empty (changed 2026-11-27, see the changelog; before that a milestone was the generated entry itself, now called a location; FR-1 to FR-5, FR-19 to FR-21, FR-25 and the appendix still say "milestone" for it). |
| Slot | One line of the game's table: a species, a level or level range, a weight, and when it applies (morning, day, night, Hoenn Sound, Sinnoh Sound, night fishing). |
| Goal | None: a milestone's own rows hold its amounts (changed 2026-11-27, see the changelog; before that what one game file is collecting: for each species or form, how many, one per game file). |
| Row | One line of a milestone on screen: a species, or one form of it. |
| Anchor | Where and how a milestone is hunted: a tile, a facing, and run left-right with a range, run up-down with a range, or spin. For headbutt and fishing, a tile and a facing. |
| Hunt plan | A plan: per game and name, ordered rules, each a set of times of day and weekdays and a step, and a default step; a step is a Wild Encounter, as Bot Control's form defines it, or Wait (changed 2026-11-27, see the changelog; before that a default milestone and ordered rules, each a set of times of day and weekdays and a milestone). |
| Custom milestone | Not a kind of its own: every milestone is saved, and one with no location is hunted as a custom one was (changed 2026-11-27, see the changelog; before that a milestone typed by hand, as all were; starters, Togepi, Jirachi and training stayed custom). |

## Goals

- G1: no milestone for a wild location is typed by hand.
- G2: an amount is entered once per milestone, filled by a preset and edited in the Milestone Editor (changed 2026-11-27, see the changelog; before that an amount is entered once per game file, not once per place the species appears).
- G3: the right table for the version: HeartGold and SoulSilver differ slot by slot, and Ruby and Sapphire differ on 30 of their 97 maps.
- G4: forms are first-class: a row can be the species in any form, or one form (female, male, an Unown letter).
- G5: the dashboard shows what the game will give right now: the rows available at this in-game hour, and the expected share of each, with Repel and the lead's ability counted.
- G6: nothing about the game's state is a checkbox the operator must keep true. The clock, Repel and the lead are read from memory.
- G7: a hunt can change place by in-game time of day and weekday without the operator, and a card that follows the hunt shows the new place in the same window.
- G8: the milestone picker shows what is relevant to the game file in front of the operator.
- G9: every encounter already logged keeps its place: old names are mapped, never rewritten.

## Non-goals

- Hunt plans on Ruby and Sapphire. Their wild tables do not change with the clock.
- Feebas. Its six tiles on Route 119 are chosen from the trendy phrase; the Route 119 rod milestones list it with no share.
- Swarms. Not modelled; a swarm species appearing is logged as any encounter is.
- The Safari Zone, the Bug-Catching Contest, roamers.
- The bot using a Repel or tuning the radio. The rates reflect them when they are on; turning them on is the operator's, or a later feature.
- Choosing the best place to hunt. The operator writes the rules; the bot follows them.
- Deleting custom milestones. They remain for everything that is not a wild location.

## Decided

From the owner, 2026-11-24:

- Archive hides a milestone from the bot control's picker and does nothing else.
- Wanted amounts are global per game file. Today's three goals: a living dex; a living dex of female forms only; one of each version exclusive.
- A living dex amount defaults to the number of Pokémon in the species' evolution line. Exceptions exist (six Aipom for Pickup, an HM user), so every default can be edited. The key rule: whatever a preset supplies, the owner can change.
- A version exclusive defaults to 1: it is traded to the first game and bred. A breeding milestone is a later feature.
- An amount of 0 is a row that is shown and counted and is never a target, as `PhaseTarget` already treats it. On the female file the male form is a row at 0.
- Every species in a milestone's table is a row at every milestone that has it, caught or not: the statistics are wanted.
- Unown: the goal defaults to one of each of the 28 letters, and all 28 are rows.
- A plan leaves at the first decision after its rule stops matching. No minimum stay.
- Surf, fishing and rock smash milestones are listed and marked "no hunt yet"; Start refuses them.
- Ruby and Sapphire are in this feature for milestones, goals, anchors and rates, not a follow-up. Their goal presets are the living dex and the version exclusives; there is no female preset.
- A Ruby or Sapphire anchor is a spin on the land tile with the cheapest route to a Pokémon Center: spinning is efficient there, so a long run is not looked for.
- Spinning does not spend Repel steps, so the repel trick with a Spin hunt does not run out. Confirmed by the owner; the spike only saw the counter fall while walking.
- In a cave, almost every floor tile gives encounters, on both games. A cave's land anchor is chosen from its floor tiles.
- Existing milestones keep working after a redeploy with no change to stored data. Moving one onto the generated data is a manual step.
- Old names: "2-2c Slowpoke Well" is Slowpoke Well B1F; "1-10 Ruins of Alph" is the underground hall; Dark Cave is the Route 31 side.
- Time comes from the in-game clock, never the PC's. Swarms are ignored.
- The map names in the appendix are the names milestones are built from.
- One spec for both features.

From the owner, 2026-11-25:

- A card either shows one fixed milestone, as today, with any number of them per game file, or follows the hunt on its game file. A hunt plan is defined once, saved by name and shared.

From the owner, 2026-11-27 (the [rework](milestone-builder-rework.md); decisions: [a saved milestone](../decisions/hunt-plans-and-milestones.md#a-milestone-is-a-saved-one-made-from-the-games-data-its-amounts-are-its-own), [plans](../decisions/hunt-plans-and-milestones.md#a-plan-is-a-list-of-whole-hunts-with-time-rules-in-its-own-tab-and-action), [the counts card](../decisions/hunt-plans-and-milestones.md#the-counts-card-markers-the-hidden-now-row-counts-in-the-header-no-footer)):

- A milestone is made from the game data, edited and saved; its amounts are its own. This reverses "Wanted amounts are global per game file" above. The caught counts stay global per game file.
- Game files are checkboxes on the milestone, archive is in the editor, and the launch form lists saved milestones only.
- A plan is a list of whole hunts with time rules, in its own tab, and "Plan" is one more action in Bot Control.
- The counts card has time-of-day markers, a row naming what is hidden now, its counts in the header and no footer.

## Requirements

### Milestones from the game

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-1 | The milestones are a generated data file per game under `ShinyBotHunter/Data/HGSS` and `ShinyBotHunter/Data/RS`, built by the same kind of command that builds the map data. HGSS: from the `pokeheartgold` decomp's wild tables, headbutt data, map headers, location names and map matrices. RS: from the `pokeruby` decomp's `src/data/wild_encounters.json`, whose entries name their map and their version. One entry per milestone and version: its maps, its modality, its slots (species, level or range, weight, condition), its trees for headbutt. The file is committed, as the map data is. | Must | Unit test: Route 30 grass for each version against the tables in the research doc. |
| FR-2 | A game file's version (HeartGold, SoulSilver, Ruby or Sapphire) is taken from its ROM's game code, which the game list already reads, and selects the version's milestones. | Must | Unit test. |
| FR-3 | A milestone's name is its map's name from the appendix and the modality: "Route 30 · grass", "Dark Cave (Route 31 side) · cave". The appendix is committed as data and is the only source of names. Land is written "grass" where the map has grass tiles and "cave" where it has none. Maps of one location that share a table are one milestone, named for the location without the floor ("Bell Tower · cave"). | Must | Unit test: every name is unique per version; every map with a table has a name. |
| FR-4 | A milestone has a stable id that is not its name (the first map's id and the modality), so a name can be corrected without touching the log. | Must | Unit test. |
| FR-5 | Conditions kept per slot, HGSS only: morning, day, night for land; Hoenn Sound and Sinnoh Sound (slots 2 to 5); night fishing. Swarm and mass outbreak species are not kept. A Ruby or Sapphire slot has no condition. | Must | Unit test. |
| FR-6 | Custom milestones work as milestones do today. | Must | QA: the existing scenarios. |

### Goals and rows

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-7 | A milestone is saved by name per game in `PhaseInfos`, with its rows (a species in any form, or one form, with an amount) and three optional fields: its location, its game files and archived. A stored document without them reads as no location, none ticked and not archived (changed 2026-11-27, see the changelog; before that a goal belonged to one game file and listed wanted entries, stored in Mongo). | Must | Unit tests `StoredMilestones_WithoutNewFields_ReadAsToday`, `Milestone_BsonRoundTrip_KeepsLocationFilesArchived`; QA: `hgss-milestone-editor`, `hgss-existing-milestones`. |
| FR-7a | "New from game data" takes a location from one dropdown of every location of the game, grouped by modality, and a version filter (both versions, or one), and fills a new milestone's rows from the location's slots, with its name prefilled. Nothing is stored before Save, and a name another milestone of the game has is refused in the form. Added 2026-11-27. | Must | Unit tests `Seed_Route30Grass_ByVersionFilter`, `Seed_Route30Grass_InSlotOrder_FirstVersionThenNewOnes`; QA: `hgss-milestone-editor`, `rs-milestone-seed`. |
| FR-7b | Each row is marked with the one version that has it at the location, or `both`, and with when it can be met (morning, day, night, all day), the markers drawn only where some row of the location is met at some times only, so never on Ruby and Sapphire. A row can be added and removed; a seeded row's species and form are fixed. Added 2026-11-27. | Must | Unit tests `Seed_Route30Grass_Times`, `Times_Rs_AllDay`; dashboard tests `markerLabels`, `draftRows`; QA: `hgss-milestone-editor`, `rs-milestone-seed`. |
| FR-7c | A saved milestone can be attached to a location, with the existing names' suggestion preselected, and detached; both write only its location, never a row or an amount, and apply to the seeded read-only milestones too. Added 2026-11-27. | Must | Unit tests `WithLocation_AttachThenDetach_ChangesNoRowOrAmount`, `Suggestion_ProductionNames_MatchTheResearchTable`; QA: `hgss-existing-milestones`. |
| FR-7d | A Wild Encounter Start, and each Wild Encounter step of a plan at Start, refuses an archived milestone, `<milestone> is archived.`, and one ticked only for other files, `<milestone> is not ticked for <game file>.`; one ticked for no file is accepted on every file. Added 2026-11-27. | Must | Unit tests `OffFile_Archived_Refused`, `OffFile_TickedForAnotherFile_Refused_TickedForThisOne_Accepted`, `OffFile_NoTicks_AcceptedOnAnyFile`; QA: `hgss-milestone-refusals`, `hgss-plan-refusals`. |
| FR-8 | A milestone from game data is started from a preset and then edited. HGSS: living dex (every species at the size of its evolution line; Unown one row per letter at 1); female forms (every species with a female form: the female form at the number of its line that have one, the male form at 0); version exclusives (species in this version's tables and not the other's, 1 each); none (all 0). RS: living dex, version exclusives and none. The preset is a dropdown on the editor's form, starting at living dex; choosing another preset or version refills every amount not typed by hand and keeps every typed amount, removed row and added row (changed 2026-11-25, see the changelog; before that a preset filled entries once and only added species the goal lacked) (changed 2026-11-27, see the changelog; before that the preset was one stored choice per game file's goal). | Must | Unit tests `Seed_Route30Grass_None_AllZero`, `Seed_Route30Grass_LivingDex_LineSizes`, `Seed_Route30Grass_FemaleForms_FemaleAtFamilyCount_MaleAtZero`, `Seed_Route30Grass_VersionExclusives_ExclusivesAtOne`, `Seed_RuinsOfAlph_LivingDex_28UnownRows`, `Seed_Rs_FemaleForms_Throws`; dashboard test `draftRows` (a preset change keeps typed amounts); QA: `hgss-milestone-editor`, `rs-milestone-seed`. |
| FR-9 | Caught counts are global per game file: a shiny caught on a file counts toward its species or form in every milestone hunted on that file, whichever milestone it was caught under (changed 2026-11-27, see the changelog; before that it counted toward the file's goal entry). | Must | Unit tests `Located_OwnAmounts_TargetUntilCaughtOnThisFile`, `Picker_Complete_ByItsOwnAmounts`. |
| FR-10 | A milestone's rows are its own, seeded or added, each at its own amount. On a file of one version, a row only the other version has at the milestone's location is not a target and does not hold completion back (default 5) (changed 2026-11-27, see the changelog; before that a generated milestone shown for a file had a row for every species in its slots, at the goal's amount or 0). | Must | Unit tests `Counted_Route30_LeavesOutTheOtherVersionsSpecies`, `Counted_NoLocation_EveryRow`, `Counted_HandAddedRow_Kept`, `IsPhaseComplete_OtherVersionRowDoesNotHoldItBack`. |
| FR-11 | A row shows wanted, caught (global, FR-9), encounters at this milestone, and encounters on this game file, as the phase card does today. | Must | QA. |
| FR-12 | A row at 0 is shown and counted and is never a target. A row whose amount the file has caught is not a target. There are no hidden rows: a species is left out at 0 or by removing its row (default 3) (changed 2026-11-27, see the changelog; before that the operator could also hide a row per file). | Must | Unit test `ProductionPhases_SameTargetsAsMain`; QA: `hgss-milestone-complete`. |
| FR-13 | `PhaseTarget` asks the saved milestone: a wild Pokémon is a target when it is shiny and matches, by name and form, a row that counts on the file (FR-10) whose catches on the file are fewer than its amount. A shiny that is not a target is handled as before (changed 2026-11-27, see the changelog; before that it asked the file's goal and its hidden rows). | Must | Unit tests `Located_OwnAmounts_TargetUntilCaughtOnThisFile`, `Located_OtherVersionRow_NotATarget`, `Located_HandAddedRow_StillATarget`. |
| FR-14 | A milestone is complete for a game file when none of its rows is a target. | Must | Unit test. |

### The picker

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-15 | The launch form's milestone list for the bot's game file holds the saved milestones ticked for it or for no file and not archived, in name order with digits as numbers; locations are never listed by themselves. It can be filtered to incomplete only. HGSS Togepi Egg, HGSS Primo Egg and HGSS Static Encounter list only milestones with no location (changed 2026-11-27, see the changelog; before that it listed the version's generated milestones with a row and the custom ones assigned to the file or none, filtered by text, modality and incomplete). | Must | Unit tests `Picker_GameFiles_TickedFileOnly_NoneTickedEveryFile`, `Picker_Archived_LeftOut_TargetUnchanged`, `Picker_GeneratedMilestonesNeverListed`; dashboard test `launchMilestones`; QA: `hgss-milestone-editor`, `hgss-milestone-refusals`. |
| FR-15a | A milestone whose modality the bot cannot hunt (surf, the rods, rock smash) is listed with its rows and rates, marked "no hunt yet", and Start refuses it with that reason. | Must | Unit test; QA. |
| FR-16 | A milestone is offered on the game files ticked for it in the Milestone Editor's Game files checkboxes; none ticked means every file (default 1) (changed 2026-11-27, see the changelog; before that a custom milestone was assigned to files and a generated one needed none). | Must | Unit test `Picker_GameFiles_TickedFileOnly_NoneTickedEveryFile`; dashboard test `toggleFile`; QA: `hgss-milestone-editor`. |
| FR-17 | Archived is one flag per milestone, set in the Milestone Editor (default 2). It hides the milestone from the launch form, and a Wild Encounter Start refuses it (FR-7d); its counts, its windows and its log are as before (changed 2026-11-27, see the changelog; before that archive was per milestone and game file, set on the picker). | Must | Unit test `Picker_Archived_LeftOut_TargetUnchanged`; QA: `hgss-milestone-editor`. |
| FR-18 | No order label: the list sorts by name with digits as numbers, so "1-4" comes before "1-10" (changed 2026-11-27, see the changelog; before that the owner's order was an optional label per milestone and file that sorted the picker). | Should | Unit test `Picker_ProductionNamesInTheOwnersOrder`. |

### Anchors

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-19 | HGSS. Each grass, cave and surf milestone has a generated anchor: among tiles of the milestone's kind inside its own maps, the longest straight run that the pathfinder can reach from the map's entrance and that is in no trainer's line of sight and under no sign. Left-right is preferred over up-down at equal length; under three tiles, a single tile to spin on. Among equal runs, the one with the shortest route to a Pokémon Center. | Must | Unit test on the committed map data for Route 30, Route 32 and one cave. |
| FR-19a | RS. Each land milestone's anchor is a spin on the land tile with the cheapest route to a Pokémon Center's counter, by `RouteSearch` with no HM. The spike's results, to be reproduced: Route 101 (4, 2) to Oldale Town, Route 102 (4, 7) to Petalburg City, Route 103 (12, 15) to Oldale Town, Route 104 (2, 11) to Rustboro City, Route 110 (14, 97) to Slateport City. | Must | Unit test on the committed RS data for those five. |
| FR-19b | RS. A land tile is one whose tile behaviour the game marks as giving wild encounters and that is not water: in `pokeruby`'s `src/metatile_behavior.c` these are the eight behaviours that file's land-encounter test accepts (0x02, 0x03, 0x05, 0x06, 0x08, 0x0B, 0x24, 0x25; see the B1r changelog entry). There is no "footprint sand": Route 111's desert is one of the eight. The committed RS map data marks only grass as kind 3, so Route 111 gave no tile in the spike; the map data's generator is extended to mark every such tile, and the anchor is then chosen as FR-19a says. In a cave that is almost every floor tile. | Must | Unit test: Route 111 has an anchor on its desert; a cave has one on its floor. |
| FR-20 | The anchor is the middle tile of the run with the range that reaches both ends, since the Run encounter paces an equal distance each way and a longer line means fewer turns. | Must | Unit test: Route 30 gives (547, 304), up-down, range 6. |
| FR-21 | Headbutt, fishing and rock smash anchors are a tile and a facing: the tree, shore tile or rock with the shortest route to a Pokémon Center. For headbutt the tree must be one that gives the wanted table for the game file's trainer ID, so the anchor is worked out per game file. | Should | Unit test for Azalea Town with trainer ID 15613. |
| FR-22 | The operator can replace a milestone's anchor with where the bot stands and how it faces, per game file, keyed by the saved milestone's name, and can clear the replacement; a milestone with no location is then hunted from it too (changed 2026-11-27, see the changelog; before that a replacement was keyed by the generated milestone or the custom name). | Must | Unit tests `NeedsTrainerId_OnlyAGeneratedHeadbuttWithNoReplacement`, `AnchorFor_Replacement_WinsOverGenerated`; QA: `hgss-existing-milestones`, `hgss-milestone-refusals`. |
| FR-23 | Starting a hunt at a milestone with an anchor walks there first. With no anchor (a custom milestone), the hunt starts where the bot stands, as today. | Must | QA. |
| FR-24 | At the anchor, before the first encounter, the tile under the player is checked to be of the milestone's kind. If it is not, the hunt stops with "{milestone}: the anchor ({x}, {y}) is not {grass}." | Must | Unit test; QA. |

### Expected rates

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-25 | HGSS. The expected share of each species at a milestone is computed from its slots as the game does (research doc, "How the game picks a wild Pokémon"): the slot weights; the species list for the time of day; the sound slots; night fishing; then Repel and the lead's Keen Eye or Intimidate cancelling slots by level; Static and Magnet Pull; Hustle, Vital Spirit and Pressure. It also gives the share of encounters that survive. | Must | Unit tests against the worked examples in the research doc. |
| FR-25a | RS. The expected share of each species at a milestone is computed from its slots as the game does (research doc, "Ruby and Sapphire", "The rule"): the slot weights of the modality; one species list; every slot rolls a level within its range. With the Repel counter above zero a land, surf or rock smash encounter is cancelled when the rolled level is below the level of the first party member that has HP and is not an egg, so a slot survives by the part of its level range at or above that level. Fishing does not check Repel. It also gives the share of encounters that survive. | Must | Unit tests: Route 101 plain gives 45, 45 and 10; a table with a level range against a Repel level inside the range. |
| FR-25b | RS. No lead ability changes which species appears. Stench and Illuminate change only how often an encounter comes and are not part of the share. | Must | Unit test: the shares are the same for every lead ability. |
| FR-26 | The inputs are read from memory while a hunt runs, never entered: the in-game time of day; the Repel counter; the lead's ability, level and egg flag, and the level of the first member that has not fainted and is not an egg (the Repel level). | Must | QA: the rate changes when the Repel counter is written. |
| FR-26a | RS. The Repel counter is save variable `0x4021` at `gSaveBlock1 + 0x1382` (`u16`), from the decomp's layout; it is confirmed on the emulator before B3r's plan is approved. The Repel level is taken from the party read the bot already makes. | Must | The emulator round in the research doc; QA: `rs-rates-repel`. |
| FR-27 | For a milestone with a location, on a game file, the card shows each row's expected share now under the Pokémon's name, and a time-of-day pill by the name (morning, day, night, or all day) where some row of the location is met at some times only. A row not meetable at this time of day, a row at a zero share now (blocked by Repel, a radio sound) and a row only the other version has are not drawn; the rows not meetable now are named in one dim last row, by the next time they can be met ("2 more at night: Hoothoot, Spinarak"). A form row is named `Unown (A)`. (Changed 2026-11-25, see the changelog: the share is drawn under the Pokémon's name, not in a Share column; a row at a zero share is not drawn; the survive share is not shown.) (changed 2026-11-27, see the changelog; before that only a generated milestone had shares, with no pills and no last row, and a row only the other version has was drawn with no share). | Must | Unit tests `HiddenUntil_Route30_AtMorningDayNight`, `Route32Card_HeartGoldFile_SoulSilversEkansHiddenAndNeverComesBack`, `RowNames_LetterRowsNameTheLetterAndAnyFormRowsThePlainName`; dashboard tests in `PhaseCard.test.ts`; QA: `hgss-rates-clock`, `hgss-rates-repel`, `rs-rates-repel`. |
| FR-28 | With no hunt running on the game file, the card shows the shares and rows for the time of day the file was last seen in, with no Repel and no lead effect, and its header says `not live (last seen <time>)`; `not live` alone on Ruby and Sapphire and on a file not seen since the backend started, where a location whose slots depend on the time of day has no shares and every row (changed 2026-11-27, see the changelog; before that the not-live mark was in the footer, with "Shares: not seen since the backend started" for a file not seen). | Should | Unit tests `Shares_NotLive_LastSeenHourNoRepelNoLead`, `Shares_NeverSeen_TimedSlots_Null`; dashboard tests in `PhaseCard.test.ts`; QA: `hgss-rates-clock`. |
| FR-29 | A Spin hunt is expected never to run a Repel out. A Run hunt and a plan's walk do spend steps: when "Repel's effect wore off..." comes up during a hunt, the bot closes it with A, confirmed by the field being free, and carries on. | Should | QA: the counter is unchanged across thirty spins; the message is closed on a Run hunt. |
| FR-30 | The radio sounds are a condition a row can have and a rate input, but the playing sound is not read: a milestone's sound rows are shown as "needs Hoenn Sound" and counted as off. | Should | Unit test. |

### Hunt plans

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-31 | HGSS only. The Plan action takes a saved plan: ordered rules and a default step. A rule is a set of times of day (morning 4 to 10, day 10 to 20, night 20 to 4), a set of weekdays (an empty set matches any) and a step, the body Bot Control posts for a Wild Encounter in any of its modes, or Wait. The first rule that matches the in-game clock and whose step is not done in this run (FR-34) wins; none matching is the default (changed 2026-11-27, see the changelog; before that Wild Encounter took an optional plan of a default milestone and rules naming milestones). | Must | Unit tests `PlanPick_FirstMatchingRuleWins`, `PlanPick_NoRuleMatches_IsTheDefault`, `PlanPick_Weekday_MatchesOnlyItsDay`, `PlanPick_EmptyLists_MatchAny`, `PlanPick_TimeBoundaries`, `PlanEntry_NeedsTheClock`, `IdleHgssBot_PlanTakesTheHuntPlan`; QA: `hgss-plan-refusals`. |
| FR-31a | A Wait step presses nothing and reads the clock every 60 s; it ends when the clock picks another step, or on cancellation. When the weekday and hour it reads have not changed for 65 minutes it parks in Waiting with `[Plan] <plan>: the in-game clock stayed at <weekday> <hour>h for 65 minutes; waiting for Resume.`, and Resume re-enters the plan. Added 2026-11-27. | Must | Unit tests `Wait_PressesNothing_EndsWhenTheClockPicksAStep`, `Wait_ClockStale_ParksWithTheSentence_ResumeReentersThePlan`, `Wait_Cancelled_Ends`; QA: `hgss-plan-wait` (R8c2, pending merge). |
| FR-31b | Plan ends: never, so the plan runs until cancelled and with nothing left to pick waits as a Wait does; or once every shiny-hunt step (a Wild Encounter step whose target is not NoTarget) is done in this run. Added 2026-11-27. | Must | Unit tests `EndWhenComplete_EveryShinyStepDone_EndsThePlan`, `EndWhenComplete_False_NothingOpen_Waits`; QA: `hgss-plan-ending` (R8c2, pending merge). |
| FR-31c | Start reads the plan by name (`No hunt plan named <name>.`) and refuses each Wild Encounter step as a Wild Encounter Start would, with `Rule <n>: ` or `The default: ` in front of the reason; a Wait step is not checked. Added 2026-11-27. | Must | Unit test `StartPlan_StepRefusal_NamesTheStepAndKeepsItsType`; QA: `hgss-plan-refusals`. |
| FR-32 | The clock is the game's live one (`0x021D1058`: weekday, hour), read in the bot's snapshot, not the save block's copy, which is up to a minute and a battle behind. | Must | Unit test with `FakeMemoryClient`; QA. |
| FR-33 | The plan is asked at each decision of the step's hunt, on the clock that decision read, before a heal trip or a Pickup visit. When it picks another step, the hunt ends there with nothing pressed, and the next step starts at its Init, which walks to its own anchor, or hunts where the bot stands with none. A switch never happens in a battle or a heal trip, and Pickup items are not collected at a switch (changed 2026-11-27, see the changelog; before that when the milestone the plan named was not the one hunted, the same hunt walked to its anchor). | Must | Unit tests `Switch_FleeToTrainWithPickup_AtTheBoundary`, `SameStep_AtTheDecision_DoesNotEnd`, `ClockCrossesTheBoundaryMidBattle_HuntIsNotEnded`, `Decide_StepEndsHere_CompletesWithNoPressOrPickupVisit`, `Decide_StepEndsHere_HurtLead_EndsBeforeTheHealTrip`; QA: `hgss-plan-night`, `hgss-plan-pickup-switch` (R8c2, pending merge). |
| FR-33a | A move happens at the first decision after the rule stops matching. There is no minimum stay. | Must | Unit test. |
| FR-34 | A step whose hunt ends on its own (its milestone complete, its training target reached) is done for the rest of the run, even if its amounts are raised meanwhile, and is skipped until the next Start. A step switched away from is not done; returning to it starts a new hunt with its own target level (changed 2026-11-25, see the changelog; before that the hunt ended when the default was complete and no rule matched) (changed 2026-11-27, see the changelog; before that a rule whose milestone was complete for the file was skipped, a complete default gave way to the first incomplete rule, and the hunt ended only when every milestone the plan named was complete). | Must | Unit tests `CompleteStep_IsSkipped`, `TrainingStep_ReturnedTo_KeepsItsTargetLevel`, `PlanPick_DoneRuleSkipped_ThenDefault_ThenNull`; QA: `hgss-plan-ending` (R8c2, pending merge). |
| FR-34a | When the milestone a hunt is working on becomes complete for the game file, a message goes to the shiny log channel in Discord with the mention: `<milestone> is complete on <game file>.`, the same in a plan's step. Once per milestone per hunt; a milestone already complete when the hunt starts sends nothing (changed 2026-11-27, see the changelog; before that a planned hunt named the plan and what it hunted next, or that the plan was finished). | Must | Unit test `TellIfComplete_TwoStepsOneMilestone_TellsOnce`; QA: `hgss-milestone-complete`. |
| FR-35 | Start does not check that a step's anchor can be reached: a step whose anchor has no path parks in Waiting at the walk with `<milestone>: no path to the anchor (<x>, <y>).` (changed 2026-11-27, see the changelog; before that Start refused a plan with a milestone that had no anchor, or whose anchor the pathfinder could not reach, naming the milestone). | Must | Unit test `AnchorWalkGoesOn_NoPath_ParksInsteadOfComplete`; QA: `hgss-milestone-refusals`. |
| FR-36 | An encounter on a step's walk to its anchor is logged under that step's milestone: the step is applied before its Init walks (changed 2026-11-27, see the changelog; before that an encounter on the walk was logged under the milestone being left until the bot stood on the new anchor). | Must | QA: `hgss-plan-night` (R8c2, pending merge). |
| FR-37 | The Plan Editor is its own Admin tab: the game, the plan or "New plan...", its name; rules with When and Days chips, Up, Down and Remove; each step's Do (Wild Encounter or Wait) with Bot Control's own fields, from `GET plans/steps`; the default; Plan ends; Save, Rename, and Delete with a confirmation. Every refusal shows the server's sentence; a new plan under a saved name reads `already a plan` and cannot be saved (changed 2026-11-27, see the changelog; before that the plan's form was inside Wild Encounter, with the rules, the in-game clock and a 24-hour preview for a chosen weekday, as the mock of 2026-11-24, kept as [hunt-plan-editor-mock.html](hunt-plan-editor-mock.html)). | Should | Dashboard tests in `PlanEditorTab.test.ts` (`formOf`, `planBody`, `moveRule`, `toggled`, `editorMilestones`); QA: `hgss-plan-editor-tab`. |
| FR-37a | A plan is saved by name per game, in its own collection, `Plans`: its rules, its default and Plan ends. The Plan action picks a saved plan by name, and one plan can be run on any game file of that game. `PUT plans` refuses a step that is not a step action, a parameter its action does not declare or whose condition does not hold, and a missing required one, each with its sentence; a step's milestone and party are checked at Start (FR-31c) (changed 2026-11-27, see the changelog; before that Wild Encounter picked a saved plan of a default milestone and rules, stored in `HuntPlans`). | Must | Unit tests `Plan_BsonRoundTrip`, `Plan_BsonRead_IgnoresAnArgumentFieldNoLongerDefined`, `PlanError_ValidPlan_IsNull`, `PlanError_StepNotYet_Refused`, `PlanChoices_AreTheSavedNamesSorted`; QA: `hgss-plan-editor-tab`, `hgss-plan-refusals`. |
| FR-37b | The Plan Editor lists the game's saved plans and can add, edit, rename and delete one. While a hunt uses the plan, running or parked in Waiting, its save, rename and delete are refused with `<plan> is in use by the hunt on bot #<n>; cancel that hunt first.`; a running hunt keeps the plan it read at Start (changed 2026-11-27, see the changelog; before that a plan in use by a running hunt could not be deleted, and the list and editor were under Wild Encounter). | Must | Unit tests `PlanInUse_RunningHunt_RefusedWithTheSentence`, `PlanInUse_ParkedHunt_Refused`, `RenamePlan_BlankName_Refused`; QA: `hgss-plan-editor-tab`, `hgss-plan-night` (R8c2, pending merge). |
| FR-37c | A card does not follow a plan by itself: with no hunt running, the in-game clock is not known. A plan reaches a card only through the hunt, by FR-39b. | Must | By design; no test. |

FR-36, note of 2026-11-26: at the first decision after Start nothing is being left, so the clock's pick becomes the milestone before the first walk (C3b, #550; `HuntPlanRun.cs:62-64`, pinned by `Step_FirstDecision_PickBecomesTheMilestone_ThenWalks`). An encounter on that first walk is logged under the pick. Every later walk logs under the milestone being left, as the row says.

### The navigator at a side door

The bug the spike ran into, on `main` at v0.1.3. It is not specific to plans: any walk through such a door parks.

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-38 | A door entered sideways is walked through. Violet City's gatehouse to Route 31 has its door marked on the tiles the player stands on, (511, 269) and (511, 270) on map 73, tile kind 2 in the map data; the warp fires on the next step Right, into the wall. Today the navigator walks onto (511, 270), sees no warp, and stops after 15 s with "Waited for the warp to map 73 (still on map 73)". The walk must take the step that fires the warp and confirm it by the map id changing. | Must | Unit test on the committed map data; QA: `hgss-plan-night` walks Route 32 to Route 30 through this door. |
| FR-38a | The direction of that step comes from the map data, not from the direction last walked: arriving on the door tile from above or below must still step Right. HGSS's committed data has no warp arrows (Ruby and Sapphire's has, and `RouteSearch` uses them). The planner chooses between regenerating the HGSS data with them and deriving the direction from the door tile and the wall beside it, and says which in the plan. | Must | Unit test: the door tile reached from each open side. |
| FR-38b | The wait after a door segment waits for the map the route's next node is on, not for whatever map the player is on when the walk ends. Today it is given the current map id, which is why the message names map 73 twice. | Must | Unit test. |
| FR-38c | Every other door of this kind in the HGSS data is found by the same rule and listed in the plan, so the fix is not one tile. | Should | The plan's list. |

The spike's workaround (one more step in the last walked direction when the walk ended on the door tile) carried the bot through this door on every trip, both ways, and is in `spike-code.patch`. It is not the fix, for the reason in FR-38a.

### The dashboard window

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-38d | A card is one of two kinds. A **fixed card** shows one milestone, as every card does today: its own window, any number of them for one game file, unchanged by this feature. A **follows-the-hunt card** belongs to one game file and shows whatever milestone the bot on that file is hunting. A game file has at most one follows-the-hunt card and can have fixed cards beside it. | Must | QA: one game file with a fixed card and a follows-the-hunt card is two windows. |
| FR-39 | A follows-the-hunt card is the game file's one window: its name, title, place and size do not change when the milestone does. The new milestone is shown in it. | Must | QA: the window handle and bounds before and after a move. |
| FR-39b | A follows-the-hunt card changes when a hunt starts on its game file at another milestone and when a running plan starts a Wild Encounter step. With no hunt running it keeps showing the last milestone hunted, which is stored with the subscription (changed 2026-11-27, see the changelog; before that it changed when a plan moved its one hunt). | Must | Unit test; QA: `hgss-plan-night` (R8c2, pending merge). |
| FR-39c | Existing subscriptions are fixed cards and are read as stored (FR-42). The operator adds a follows-the-hunt card from the dashboard configuration, where a fixed card is added today. | Must | QA: `hgss-existing-unchanged`. |
| FR-39a | A phase window is painted while another window covers it, so a capture of it never shows a blank or stale card. | Must | QA: a swap with the window fully covered, captured, shows the new milestone. |
| FR-40 | A change of milestone replaces the follows-the-hunt card's milestone in the dashboard configuration in one push; a game file with no such card gets no push and no window. | Must | Unit test. |
| FR-41 | The window re-reads the in-game time of day from the bot's state, so its rows, its last row and its shares change at the first encounter after 4:00, 10:00 and 20:00 in-game without a move. Its header shows the encounters, the phase count, the encounters per hour and not live; there is no footer. (Changed 2026-11-25, see the changelog: a row is left out rather than dimmed.) (changed 2026-11-27, see the changelog; before that the phase count and the rate were in a footer). | Must | Dashboard test "draws the phase count, the rate and not live in the header"; QA: `hgss-rates-clock`. |

### Existing milestones and the backfill

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-42 | A redeploy changes no stored data. Every existing phase is a custom milestone and behaves exactly as it does today: its species, amounts, form rows, counts, windows and targets, on both games. Nothing is migrated at start-up. | Must | Unit test: production's 20 phases, loaded as stored, give the same rows and targets before and after. QA: every existing scenario unchanged. |
| FR-42a | The existing phase documents are read as stored; their schema gains FR-7's three optional fields, which a document without them reads as none. The Milestone Editor keeps editing them. An existing milestone's card has no share, pill or last row until it is attached to a location (FR-7c) (changed 2026-11-27, see the changelog; before that the schema did not change, and a note was to show where a generated milestone showed a share). | Must | Unit test `StoredMilestones_WithoutNewFields_ReadAsToday`; QA: `hgss-existing-milestones`. |
| FR-43 | Backfill is Attach to a location in the Milestone Editor: one action on one saved milestone, for every game file. The mapping in the research doc ("The existing names") is the preselected suggestion and can be overridden. Only the milestone's location is written; its rows and the encounter log are not rewritten (changed 2026-11-27, see the changelog; before that it was a link per existing milestone and game file to a generated milestone, stored on the file). | Must | Unit tests `WithLocation_AttachThenDetach_ChangesNoRowOrAmount`, `Suggestion_ProductionNames_MatchTheResearchTable`; QA: `hgss-existing-milestones`. |
| FR-44 | After an attach the milestone keeps its name, so its counts are the encounters and catches logged under it, as before; it gains its location's anchor, shares, pills and Start checks, and Detach removes them (changed 2026-11-27, see the changelog; before that after a link the generated milestone's counts included those logged under the linked name, and the linked name left the picker). | Must | QA: `hgss-existing-milestones`. |
| FR-44a | New encounters and catches are logged under the saved milestone's name, attached or not (changed 2026-11-27, see the changelog; before that after a link they were logged under the generated milestone's id). | Must | By design. |
| FR-45 | Nothing is copied: a milestone's amounts are its own (changed 2026-11-27, see the changelog; before that the link offered to copy the phase's amounts into the file's goal). | Must | By design. |
| FR-46 | Hunts and the dashboard treat every saved milestone alike. One with no location has no slots and no expected shares, and no anchor unless the operator sets one from where the bot stands (FR-22); without one it is hunted from where the bot stands, and it can be a plan's step (changed 2026-11-27, see the changelog; before that a custom milestone's amounts were its own, not a goal's, and without an anchor it could not be a plan's destination). | Must | QA: `hgss-existing-milestones`. |

## Non-functional

- NFR-1: reading the clock, the Repel counter and the lead adds no read to the flee-only hunt beyond one block; encounters per hour do not fall on either game (the Route 33 speed check; `rs-wild-speed` at 265 an hour or more).
- NFR-2: the rate computation is pure and has no emulator in it.
- NFR-3: no new package, no new store, no ApiHawk change. New data files and two Mongo collections (`GameFiles`, one document per game file, now holding only its replacement anchors; `Plans`, the saved plans) are listed in the PR (changed 2026-11-27, see the changelog; before that the second collection was `HuntPlans`).
- NFR-4: a plan's move is as safe as a heal trip: every wait bounded, a stop naming what it saw.

## Design notes

- **One model, two features.** A milestone is a saved one; its location carries the anchor the navigator walks to and the slots the card's shares ask about, and its own rows give the target. A plan picks a whole hunt by the in-game clock, and each Wild Encounter step walks to its own milestone's anchor (changed 2026-11-27, see the changelog; before that the plan picked a milestone, and the goal gave its rows).
- **Where it sits.** The location data loads beside the map data. The rows and the rate computation are plain classes. `PhaseTarget` and completion ask the milestone's rows that count on the file (`MilestoneRows.Counted`). A plan runs in `PlanEvent`, a parent event over one shared state, asked at `OrchestratorEvent`'s decision through its `endsHere` hook (changed 2026-11-27, see the changelog; before that the goal answered the target, and the plan moved the hunt inside `OrchestratorEvent` with the navigator's walk to a start tile).
- **Game profile.** The location file, the rate rule and the reads are the profile's, one answer per game. Rows, the launch list, archive, anchors and the old-name suggestion are the same code for both. Plans are offered only where the profile has a clock (`HasHuntPlans`) (changed 2026-11-27, see the changelog; before that goals and the picker were the shared code).
- **Save sections.** The Repel counter is reached through the save's section table (research doc), which also gives the party and the clock. One read of the table per launch.

## QA

New scenarios, on a copy of `hgss.qa-route32` unless said; the emulator's DS clock is set by an own config for the run.

| Scenario | Rows it covers | Proves |
|---|---|---|
| `hgss-milestone-editor` | `hgss-goal-rows` | No emulator but two title-screen launches: New from game data on Route 30 · grass (the prefilled name, the version marks, the When markers, the version filter), the presets with a typed amount kept, Save and a taken name, the ticks and archive in both launch lists, the card (FR-7 to FR-7b, FR-8, FR-15 to FR-17). |
| `rs-milestone-seed` | new | No emulator: no Female forms on RS, Route 102 for both versions and each, Version exclusives, no When marker, Save (FR-7a, FR-7b, FR-8). |
| `hgss-existing-milestones` | `hgss-existing-unchanged`, `hgss-backfill` | Production's phases read as stored (FR-42, FR-42a), Attach and Detach (FR-7c, FR-43, FR-44), a hunt with no location, then from a set anchor (FR-22, FR-46), the seeded Starters. |
| `hgss-milestone-refusals` | `hgss-plan-refused`, `hgss-milestone-bad-anchor` | FR-7d's not ticked, FR-15, FR-15a, the wrong type, FR-24, FR-35's park at the walk, the all-complete ending, the egg and static forms' lists. |
| `rs-milestone-anchor` | `rs-milestone-anchor`, `rs-milestone-rows` | `anchor`: no Plan action on RS, the launch list, Route 101's shares 45, 45 and 10 with no pill, the walk to the generated anchor (4, 2) and a spin; `no-route`: Route 110's no-route line and Waiting. |
| `rs-rates-repel` | `rs-rates-repel` | FR-26a and FR-29 on RS, on a saved milestone with a location. |
| `hgss-milestone-anchor` | `hgss-milestone-anchor` | FR-23 and FR-38 on a saved milestone at Route 30 · grass. |
| `hgss-rates-repel` | `hgss-rates-repel` | FR-26, FR-27, FR-29: `run`, `spin`. |
| `hgss-rates-clock` | `hgss-rates-clock` | FR-27, FR-28, FR-30, FR-32, FR-41: `20`, `4`, `10`: the rows, the last row and the not-live header across a boundary; a row only the other version has never drawn. |
| `hgss-plan-editor-tab` | `hgss-plan-editor` | FR-37, FR-37a, FR-37b: the Plan Editor, with no emulator. |
| `hgss-plan-refusals` | new | FR-31, FR-31c and FR-7d in a step, and FR-37a's refused saves. |
| `hgss-plan-ending` | `hgss-plan-ending` | (R8c2, pending merge) FR-31b: `end`, `runs-on`. |
| `hgss-anchor-repel-walk` | `hgss-plan-repel-walk` | FR-29 on the walk to an anchor, from a plain Wild Encounter. |
| `hgss-plan-night` | `hgss-plan-night` | (R8c2, pending merge) FR-33, FR-36, FR-38d, FR-39, FR-39b: `night`, `start-on-rule`. |
| `hgss-plan-morning-back` | `hgss-plan-morning-back` | (R8c2, pending merge) FR-33: `back`. |
| `hgss-plan-weekday` | `hgss-plan-weekday` | (R8c2, pending merge) FR-31's weekday: `hg`, `ss`. |
| `hgss-plan-wait` | new | (R8c2, pending merge) FR-31a. |
| `hgss-plan-pickup-switch` | new | (R8c2, pending merge) FR-33's switch with Pickup. |
| `hgss-milestone-complete` | `hgss-milestone-complete` | FR-13, FR-34a: `no-plan`; `plan` (R8c2, pending merge). |
| `hgss-headbutt-anchor` | `hgss-headbutt-anchor` | FR-21 in a hunt, on a saved milestone at Violet City · headbutt. |

Each scenario's steps, asserts and data review are in the plan on #525; the reworked ones' in the plan on #621 (2026-11-27).

Regression: every existing HGSS and RS scenario, since each starts a hunt at a phase.

## Task split

Three slices, each useful alone, in this order.

**Slice A: milestones and goals.**
- A1 the HGSS generator, the committed milestone file and the names (FR-1 to FR-5).
- A1r the RS generator and file (FR-1, FR-2, FR-5). Beside A1; disjoint files.
- A2 goals, presets, global counts, rows, the target (FR-7 to FR-14).
- A3 the picker, the unhuntable mark, assignment, archive, order (FR-15 to FR-18).
- A4 existing milestones unchanged, the link, the copy of amounts (FR-42 to FR-46, with FR-42a and FR-44a). FR-42 is checked on every task of slice A; the link is after A1 and A2.
- A5 the dashboard's milestone and goal editing. After A2.

**Slice B: anchors and rates.**
- B1 generated HGSS anchors (FR-19, FR-20, FR-21). After A1.
- B1r generated RS anchors (FR-19a, FR-19b). After A1r.
- B2 the anchor in a hunt: walk, check, replace (FR-22 to FR-24).
- B3 the HGSS rate computation (FR-25, FR-30). After A1; no emulator.
- B3r the RS rate computation and the Repel read (FR-25a, FR-25b, FR-26a). After A1r.
- B4 the memory reads and the card (FR-26 to FR-29, FR-41).

**Slice C: plans.**
- C1 the navigator at a side door (FR-38 to FR-38c). Can start at once, and is worth merging on its own.
- C2 the cards: the follows-the-hunt card and its one window (FR-38d, FR-39 to FR-39c, FR-40). Can start at once.
- C3 the plan in the hunt (FR-31 to FR-36, FR-33a) and the saved plan's storage (FR-37a). After B2.
- C4 the plan's form and the saved plans' editor (FR-37, FR-37b).

Q1 the scenarios; D1 the guide pages, the architecture doc, the close-out.

2026-11-27: the rework's tasks, R1 to R9, are split in the [rework doc](milestone-builder-rework.md#task-split).

## As built

Implemented on `feature/hunt-plans-milestones`, epic #508, 2026-11-25. The requirement rows above stand except where "What diverged" says otherwise; the changelog has each change as it was made. Decisions: [hunt plans and milestones](../decisions/hunt-plans-and-milestones.md).

### After the rework (2026-11-27)

- `PhaseInfo` gains `Location`, `GameFiles` and `Archived` (`Service/PhaseInfoService.cs:316-320`), read as none on a document without them; attach and detach go through `SetLocationAsync` (`:124-140`); the seed, the times, the version mark and the rows hidden now are `PhaseInfoService.Seed`, `Times`, `OnlyVersion`, `HiddenNow` and `HiddenUntil` (`:209-264`).
- `GameFile` (`Milestones/Goal.cs:9-27`) holds only `Anchors`, keyed by the saved name; a pre-rework document's other fields are ignored on load.
- The rows that count on a file, `MilestoneRows.Counted` (`Milestones/MilestoneRows.cs:12-19`), feed `PhaseTarget` (`BotEvents/Target/PhaseTarget.cs:25`), completion (`OrchestratorEvent.cs:327-338`) and the launch list (`MilestonePicker.List`, `MilestonePicker.cs:18-26`).
- A Wild Encounter Start's refusals and anchor are `BotService.BuildWildLegAsync` (`Service/BotService.cs:319-359`), shared with each plan step, with `OffFileRefusal` (`:1087-1091`).
- The routes: `phaseinfo`'s one shape with `rowNames` and `location` (`Controllers/PhaseMetadataController.cs:60-132`), the locations, seed, attach and detach (`:145-216`), the plans (`:226-287`) and `PlanError` (`:298-371`).
- `Plan` and `PlanStep` (`Milestones/HuntPlan.cs:8-37`) in `Plans` (`MongoHuntPlanDao.cs:16`); the Plan entry (`Games/EventRegistry.cs:83-86`) and `PlanChoices` (`:96-97`); `BotService.StartPlanAsync` (`BotService.cs:363-384`); `PlanEvent` (`BotEvents/PlanEvent.cs`) and the orchestrator's `endsHere` hook (`OrchestratorEvent.cs:464-470`).
- The milestone message comes only from `OrchestratorEvent.UpdateMilestoneState` (`:312-325`).
- The dashboard: the Milestone Editor (`PhaseEditorTab.tsx`), the Plan Editor tab (`PlanEditorTab.tsx`; `AdminCard.tsx:15-21`), the launch list (`ActionParamFields.tsx`, `launchMilestones`), the counts card (`PhaseCard.tsx`: the row names `:82`, the pills `:117`, `:260`, the last row `:294-299`, the header `:201`).
- One new test seam, `PlanEvent`'s internal constructor (#628). No package, project or ApiHawk change; `Plans` replaces `HuntPlans`, and `PhaseInfos` gains three fields.

### As built on 2026-11-25 and 2026-11-26

- The data files `Data/HGSS/milestones.json.gz` and `Data/RS/milestones.json.gz`, written by the setup commands `--generatemilestoneshgss` and `--generatemilestoners` (`Tools/SetupCommands.cs:66-83`) and loaded as the profile's `Milestones` (`HgssProfile.cs:25`).
- `GameFile` (`Milestones/Goal.cs:12-31`), one document per game file in `GameFiles`: the goal's entries, its preset, hidden rows, archive, order labels, assignments, anchors and links. `HuntPlan` (`Milestones/HuntPlan.cs:10-12`), one document per game and plan name in `HuntPlans`. (replaced 2026-11-27)
- `MilestoneRows.Build` (`Milestones/MilestoneRows.cs:18`) for a milestone's rows on a file, `MilestonePicker.List` (`Milestones/MilestonePicker.cs:18`) for the picker, and `PhaseTarget` asking the goal. (replaced 2026-11-27)
- Anchors: generated by `HgssAnchorFinder` and `RsMilestoneProcessor.FindAnchor` (`Scripts/Milestones/RsMilestoneProcessor.cs:152`) into the data files; a file's replacement or headbutt tree through `GameFile.AnchorFor` (`Goal.cs:39`) and `HeadbuttTrees.AnchorFor` (`HeadbuttTrees.cs:32`). FR-24 is checked at Start (`BotService.AnchorTileStop`, `BotService.cs:1076-1077`).
- `profile.Rates` (`GameProfile.cs:65-67`) with `HgssRates.Compute` and `RsRates.Compute`. The rate inputs are read at each decision, beside the party read (`OrchestratorEvent.cs:487-496`; HGSS through `HgssFieldSystemStateReader.ReadClockAndRepelAsync`, `:103`).
- The Repel message is closed on an HGSS Run hunt (`RunEncounterEvent.cs:89-102`), on the HGSS navigator's walk (`NavigatorEvent.cs:492`, `:546`) and on a US RS Run hunt (`RunEncounterEvent.cs:107-121`).
- `HuntPlanRun` (`Milestones/HuntPlanRun.cs`) in the decision, whose walk line names the tile it walks from (`:111`, #602), the FR-35 refusal at Start (`HuntPlanRun.ReachRefusalAsync`, `:118-146`), and the milestone message (`OrchestratorEvent.cs:332-339` with no plan, `HuntPlanRun.cs:43-55` with one, both through `ShinyAlertService.NotifyStopAsync`). (replaced 2026-11-27)
- Fixed and follows-the-hunt cards; a phase window paints while covered (`windowManager.ts:148`).
- The dashboard: the Goals tab (`AdminCard.tsx:19`, `GoalEditorTab.tsx`), `MilestoneFiles.tsx`, `MilestoneRowsPanel.tsx`, the plan editor `HuntPlanEditor.tsx` under the Wild Encounter form, and on the card each row's share under its name, with a row at a zero share not drawn (`PhaseCard.tsx:76`, `:245-246`; #603). (replaced 2026-11-27)
- An Unown letter row is named `Unown (A)` to `Unown (?)` on the counts card (`PokemonMetadataService.RowName`, `:123-138`, used at `PhaseMetadataController.cs:278`; #605); the Goals tab and stored phases keep plain names. (replaced 2026-11-27)
- The plan list re-reads when its select gets focus and when Edit plans opens (`HuntPlanEditor.tsx:126`), and the Admin card's heading shows a generated milestone's name, not its id (`EmulatorCard.tsx`, `cardHeading`; #606). (replaced 2026-11-27)
- A trainer's challenge during an HGSS navigator walk stops the walk (#604, #608): script 3739 is recognised on the walk (`NavigatorEvent.cs:284`), nothing is pressed, one message is sent per stop, Waiting goes over the leg with `StoppedForTrainer`, and Resume plans the leg again from where the player stands (`NavigatorEvent.cs:585-603`). A second unbeaten trainer is a new stop with its own message. The message names the map by its internal name (`R30`).
- Two test seams: on the game-file store (A7, #574) and on the navigator's Discord send (`NavigatorEvent.cs:97-98`, #608); and the side door's step (C1, `PathfindingService.ExitDirection`, `PathfindingService.cs:526`).
- No package, no project, no ApiHawk change; two new Mongo collections, `GameFiles` and `HuntPlans`. (replaced 2026-11-27)

## What diverged

Each item is recorded in the changelog on its date; this list does not restate the reasons.

1. FR-1 and FR-3: 398 milestones per HGSS version, not 466, since maps of one location with identical slots merge (A1, 2026-11-24).
2. FR-19: surf anchors are deferred; 19 HGSS and 22 RS land milestones have no generated anchor (B1 and B1r, 2026-11-25).
3. FR-21: the trainer ID is the full 32-bit one, from the game's source; a headbutt stand needs no HM (B1h and B1h-c, 2026-11-25).
4. FR-23: a hunt with no path to its anchor parks in Waiting with a sentence; it does not end Complete (`OrchestratorEvent.cs:228-241`; B2, 2026-11-25).
5. FR-24: checked at Start against the committed map data, not at the anchor (B2, 2026-11-25).
6. FR-25: no shares on a headbutt card: the rows give no share, so the card draws every row with no share under the names (`MilestoneRows.cs:40`; B4b and B4c, 2026-11-25).
7. FR-26, FR-32 and NFR-1: the clock and the Repel counter are not on the snapshot. One request is added per decision, and the first decision after a launch has no Repel reading (B4a, 2026-11-25). NFR-1 is measured by the regression pass (Evidence).
8. FR-27 and FR-41: shares change at the first encounter after 4:00, 10:00 or 20:00, not on the hour (B4b and B4c, 2026-11-25).
9. FR-29: each close is gated on the byte its press changes: on HGSS script 2022 leaving `WaitABPressNative`, on RS lock 1, mode 2 and `WaitForAorBPress`, confirmed by the lock reading 0. RS ROMs other than US Ruby and Sapphire stop at the message as before (B4a, #556, #589, 2026-11-25).
10. FR-34: as rewritten on 2026-11-25 (#583), with #593's re-pick when a switch finds the stop complete and one re-read before ending (`HuntPlanRun.cs:35-42`, `:62-70`, `:85-88`).
11. FR-34a: when the last two stops complete in one decision, each gets its own "... The plan is finished." (`HuntPlanRun.cs:49-54`).
12. FR-37: the mock's in-game clock chip is not built. The editor shows only the fixed times line (`HuntPlanEditor.tsx:345`), because nothing the dashboard reads exposes the weekday and hour (C4a and C4b, 2026-11-25).
13. FR-37b: rename is refused while the plan is in use too, not only delete (C4a and C4b, 2026-11-25).
14. FR-38a: the door's direction is derived from the door tile and the wall (`PathfindingService.ExitDirection`), not from regenerated data (C1).
15. FR-42a: the note on a stored phase's card is not drawn. `PhaseCard.tsx` has no share under the name and no footer note for a stored phase (`:114`, `:245-246`, `:284-291`), and no scenario asserts a note.
16. FR-44: the linked name is refused at Start on every hunt type (A4 and A7, 2026-11-25).
17. Design notes, "the reads are the profile's": as built, the rate-input read and both Repel closes are chosen by the address table's type (`state.Bot.Addresses is HgssPointerService` / `is RsPointerService`, `OrchestratorEvent.cs:487`, `:492`; `RunEncounterEvent.cs:59`, `:107`), not through a profile part.
18. Not built: shares on a headbutt card, the clock chip, the "do nothing until the rule's time" choice, the Route 1 edge (#578).

After the rework (2026-11-27). Items 10 to 13 and 16 describe the plan's run and the link that the rework removed.

19. FR-35: the reach check at Start is dropped; an anchor with no path parks at the walk (binding answer 4 on #619; `OrchestratorEvent.cs:216-229`).
20. FR-7d: only a Wild Encounter Start, and a plan's Wild Encounter steps, refuse an archived or unticked milestone, and only when the file's ROM version is known (`BotService.cs:334-338`).
21. The wire names are kept: `NewEventArguments.HuntPlan` (`Service/BotDtos.cs:129`), the Plan action's parameter `HuntPlan` (`EventRegistry.cs:84`), `MongoHuntPlanDao` and `GameCapabilities.HasHuntPlans`.
22. Default 6: plan step types are Wild Encounter and Wait. HGSS Togepi Egg, HGSS Primo Egg, HGSS Static Encounter and Chained Encounter are refused with one sentence and no per-type reason (`PhaseMetadataController.cs:329-332`). Primo waits on the owner's open question about saving; the research task #614 Part 1 is reduced to one reading taken at QA.
23. FR-8: "New from game data" starts at Living dex and both versions, on the first location of the list (#627; `PhaseEditorTab.tsx:314-316`).
24. FR-27 and FR-28: `location` is null on a file whose ROM version is not known, so its card draws as one with no location (`PhaseMetadataController.cs:96-101`).
25. FR-37b: a plan's save is refused while it is in use too, not only its rename and delete (`BotService.cs:434-449`).
26. Default 4: a saved milestone has no name input, so it cannot be renamed; a new one's name is free and a taken name is refused in the form (`PhaseEditorTab.tsx:372-376`, `:533-543`).
27. `GET species/{game}` lost its last caller with the Goals tab and is deleted (#632).
28. The Plan Editor's game list offers RS, and its save is refused there: `RS has no in-game clock, so it has no plans.` (`PlanEditorTab.tsx:257`; `PhaseMetadataController.cs:300-303`).
29. An edit made in the Plan Editor while a Save or Rename is in flight is lost: the response puts back the form as it was sent (`PlanEditorTab.tsx:225-232`).
30. The location dropdown's groups are the backend's modalities, so grass and cave are one "Land" group (`PhaseEditorTab.tsx:43-48`, `:163-169`).
31. FR-33: Pickup items are not collected at a switch; the hook comes before the Pickup visit, and the next decision of a Pickup step collects them (`OrchestratorEvent.cs:464-482`).
32. FR-34: a step complete in a run is skipped until the next Start, even if its amounts are raised meanwhile (`PlanEvent.cs:37-38`, `:84-92`).

## Evidence

Nothing below was run for this close-out; the results are the orchestrator's QA runs on the feature branch. Run folders are under `ShinyBotHunter.Qa/dashboard/qa-runs/`, named `<stamp>-hgss-route33-dashboard`.

**The regression pass, on `4261d14`.** Final: 30 runs, all passed. HGSS Run 192.5 encounters an hour (bar 189), HGSS Spin 176.7, RS Spin 268.5 and RS Run 268.9 (floor 265, target 270). The speed runs are `20261125-191835` (`hgss-wild-speed`) and `20261125-203919` (`rs-wild-speed`); the other 28 are the folders stamped `20261125-191610` to `20261125-215224`. The regression pass predates three later changes, so it did not run them: `HuntPlanRun.cs` and a comment in `OrchestratorEvent.cs` (#593, `c243f53`), one log line in `HuntPlanRun.cs` (#602, `a91ae47`), and the counts card in `PhaseCard.tsx` (#603, `108c4fe`). `HuntPlanRun` runs only with a plan; the card change is dashboard only (`git diff --stat 4261d14..108c4fe -- ShinyBotHunter PokemonDashboard`).

**The regression pass on the final tip** (bot code of `bd96858`, after #604's trainer stop in `NavigatorEvent.cs`). `hgss-wild-speed` passed, `20261126-121036`: HGSS Run 193.1 encounters an hour (bar 189; 192.5 on `4261d14`), Spin 175.1. `rs-wild-speed` passed, `20261126-125246`: RS Spin 270.5, RS Run 268.7 (floor 265, target 270). Also passed: `hgss-navigate-heal-return` `20261126-133509`; `hgss-heal-variants`, three runs, `20261126-133652`, `20261126-133825`, `20261126-133959`; `hgss-sign-walk` `20261126-134138`; `hgss-static-giratina` `20261126-134351`; `hgss-static-dialga` `20261126-134825`. Not run, because their saves are not staged on the PC: `hgss-poison-walk`, `hgss-primo-egg-hatch`.

**The new-scenario pass.**

| Scenario | Variant | Run folder | Result |
|---|---|---|---|
| `hgss-goal-rows` | | `20261126-094833` | Pass, 1.5 min. |
| `hgss-existing-backfill` | | `20261126-120054` | Pass, 2.1 min. |
| `hgss-milestone-refusals` | | `20261126-095014` | Pass, 1.7 min. |
| `rs-milestone-anchor` | `anchor` | `20261126-095158` | Pass, 2.0 min. |
| `rs-milestone-anchor` | `no-route` | `20261126-095400` | Pass, 0.6 min. |
| `rs-rates-repel` | | `20261126-095613` | Pass, 1.2 min. The run before it, `20261126-095440`, failed on the scenario's own timing race, fixed in #610; after the fix it passed three times in the coder's worktree (`20261126-140621`, `20261126-140819`, `20261126-140937`). |
| `hgss-milestone-anchor` | | `20261126-095736` | Pass, 5.2 min. |
| `hgss-rates-repel` | `run` | `20261126-100251` | Pass, 4.3 min; again after #610, `20261126-141111`. |
| `hgss-rates-repel` | `spin` | `20261126-100712` | Pass, 2.2 min. |
| `hgss-rates-clock` | `20` | `20261126-100926` | Pass, 8.3 min. |
| `hgss-rates-clock` | `4` | `20261126-101742` | Pass, 7.2 min. |
| `hgss-rates-clock` | `10` | `20261126-102455` | Pass, 7.5 min. |
| `hgss-plan-editor` | | `20261126-120302` | Pass, 1.2 min. |
| `hgss-plan-ending` | `default-complete` | `20261126-120414` | Pass, 2.6 min; no completion message, as expected. |
| `hgss-plan-ending` | `all-complete` | `20261126-120654` | Pass, 0.7 min; no completion message, as expected. |
| `hgss-plan-repel-walk` | | `20261126-103228` | Pass, 1.7 min. |
| `hgss-plan-night` | `night` | `20261126-105048` | Pass, 16.3 min; one completion message. FR-36 mid-hunt: two Spinarak met on the walk at 20:00 are logged under `36-land`, the milestone left, and the switch comes on arrival. The run before it, `20261126-103410`, failed on the old assert (six Rattata in six encounters): the shares, 40, 30 and 30, are right against the decomp, and the fixed clock start replays the game's RNG. With #610's assert it passed again, `20261126-141542`. |
| `hgss-plan-night` | `start-on-rule` | `20261126-110705` | Pass, 2.5 min; again `20261126-143147`. The first walk logs under the pick (FR-36 note): Bellsprout on Route 31 under `34-land`. |
| `hgss-plan-morning-back` | `back` | `20261126-110936` | Pass, 23.3 min. |
| `hgss-plan-morning-back` | `complete-on-return` | `20261126-113257` | Pass, 24.4 min; one completion message. |
| `hgss-plan-weekday` | `hg` | `20261126-092747` | Pass, 14.3 min, in the N1c coder's worktree on the bot code of `bd96858`. |
| `hgss-plan-weekday` | `ss` | `20261126-085217`, `20261126-091007` | Pass twice, 17.6 and 17.5 min, in the N1c coder's worktree on the bot code of `bd96858`. |
| `hgss-milestone-complete` | `no-plan` | `20261127-030012` | Pass, about 2 min, on `0c4d563`; the hotkey sent with EmuHawk in front; the shiny parked the hunt one encounter after the arm; one milestone message, `Route 32 · grass is complete on HG-Complete.` |
| `hgss-milestone-complete` | `plan` | `20261127-030224` | Pass, about 2 min, on `0c4d563`; the hotkey sent with EmuHawk in front; one message, `QA Single: Route 32 · grass is complete on HG-Complete. The plan is finished.`; the plan's delete and rename while parked both refused with 400, "in use by the hunt on bot #0; cancel that hunt first." |
| `hgss-headbutt-anchor` | | `20261126-120738` | Pass, 2.9 min. |

The pass ran on the feature tip, whose bot code is that of `bd96858`; the stamps are UTC. Every Repel close in it was one A press. `hgss-milestone-complete` first could not run on the final tip: a Windows system dialog held the foreground, so the arming hotkey was not sent (`20261126-115731`, `20261126-115859`). Its rerun on `0c4d563` passed both variants at the first attempt (`20261127-030012`, `20261127-030224`), with the hotkey sent and EmuHawk in front, which also exercises #610's in-front path.

**Not verified on the emulator.**

- Mongo down.
- The layouts never rendered before, against the mock, in every theme. No feature scenario changes the theme, so the screenshots are one theme only and await the owner's look.
- Typing speed in the goal editor with about 520 goal lines.
- An egg in the party.
- Route 111 with and without the Go-Goggles.
- Azalea tree 3 on `HG-Headbutt`. The picker's choice of that stand is checked by `hgss-milestone-refusals`; the walk and the battle are not.
- The navigator's coincidence cases: a door entered from above or below (logged by `hgss-milestone-anchor` when the route happens to do it, not forced); the Repel box on the last tile, at arrival, or a frame late; the Repel box on a warp, sign, trainer or poison tick.
- Races: out-of-order re-reads, a late plan-list response.
- The chained hunt's card across legs.
- The Archive error with the backend stopped. `hgss-milestone-refusals` restarts the backend and checks that the archive survives; it never presses Archive while the backend is down.
- The link refusal sentence on Primo and Static: their place check answers first on this save (`hgss-existing-backfill.spec.ts:33-35`, `:233-238`).
- Resume with the RS Repel box up.
- A poison tick on the last step before the Pokémon Center.
- Two "The plan is finished." messages from one decision.
- SoulSilver beyond `hgss-plan-weekday ss`: the Repel message on a Run hunt and mid-walk; there is no SoulSilver variant of `hgss-rates-repel`.
- Ruby: every feature RS scenario runs on Sapphire (`SA-Wild`).
- Corsola on a rod milestone at night: `hgss-rates-clock` checks Staryu only.
- A headbutt milestone with no stand.
- A plan walk that ends off the anchor (`HuntPlanRun.cs:98`); `hgss-plan-repel-walk` asserts only that it does not happen.
- An HGSS Run hunt's Repel close that fails (`RunEncounterEvent.cs:94-99`); `hgss-rates-repel` asserts only that it does not happen.
- The trainer-ID mismatch refusal.
- "The milestone message was not sent" (`OrchestratorEvent.cs:421`).
- A chained hunt's milestone message.
- A planned hunt's encounters per hour against the same milestone hunted without a plan.
- The walk to Route 32 · grass's generated anchor from the QA save: it is stopped by a one-time story NPC, so the scenarios that hunt Route 32 set its anchor on the save's tile instead (#602). The HGSS navigator has no handling for a live story trigger on a walk (`NavigatorEvent.cs`).
- The Primo egg walk after #608, which changed only the navigator's constructor argument there (`HgssPrimoEggEvent.cs:62`); `hgss-primo-egg-hatch` was not run.
- The trainer stop beyond Youngster Joey on SoulSilver: no other trainer, no double battle, no HeartGold, no loss followed by the walk back.
- A second unbeaten trainer's stop: observed once (Youngster Mikey, two messages, run `20261126-080735` in the N1c coder's worktree), not asserted.
- The Friday half of `hgss-plan-weekday` on the SoulSilver save: its walk passes Lyra's one-time gatehouse scene, so `ss` is cancelled at midnight's walk line.
- Whether a counts card with more rows than fit scrolls: the 28-row Unown card shows 15 rows at 640 by 520.
- The headbutt walk with no Rock Smash (B1h-c, #572): `hgss-headbutt-anchor` moved to Violet City (#606), whose stand does not exercise it.

**The rework (2026-11-27).** Nothing below was run for this close-out; the orchestrator runs the end-of-feature QA on the feature tip and fills in each placeholder.

**The regression pass on the rework's tip.** [QA: pending]

**The rework's scenarios.**

| Scenario | Variant | Run folder | Result |
|---|---|---|---|
| `hgss-milestone-editor` | | [QA: pending] | [QA: pending] |
| `rs-milestone-seed` | | [QA: pending] | [QA: pending] |
| `hgss-existing-milestones` | | [QA: pending] | [QA: pending] |
| `hgss-milestone-refusals` | | [QA: pending] | [QA: pending] |
| `rs-milestone-anchor` | `anchor` | [QA: pending] | [QA: pending] |
| `rs-milestone-anchor` | `no-route` | [QA: pending] | [QA: pending] |
| `rs-rates-repel` | | [QA: pending] | [QA: pending] |
| `hgss-milestone-anchor` | | [QA: pending] | [QA: pending] |
| `hgss-rates-repel` | `run` | [QA: pending] | [QA: pending] |
| `hgss-rates-repel` | `spin` | [QA: pending] | [QA: pending] |
| `hgss-rates-clock` | `20` | [QA: pending] | [QA: pending] |
| `hgss-rates-clock` | `4` | [QA: pending] | [QA: pending] |
| `hgss-rates-clock` | `10` | [QA: pending] | [QA: pending] |
| `hgss-plan-editor-tab` | | [QA: pending] | [QA: pending] |
| `hgss-plan-refusals` | | [QA: pending] | [QA: pending] |
| `hgss-anchor-repel-walk` | | [QA: pending] | [QA: pending] |
| `hgss-headbutt-anchor` | | [QA: pending] | [QA: pending] |
| `hgss-milestone-complete` | `no-plan` | [QA: pending] | [QA: pending] |
| `hgss-milestone-complete` | `plan` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-night` | `night` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-night` | `start-on-rule` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-morning-back` | `back` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-weekday` | `hg` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-weekday` | `ss` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-ending` | `end` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-ending` | `runs-on` (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-wait` | (R8c2, pending merge) | [QA: pending] | [QA: pending] |
| `hgss-plan-pickup-switch` | (R8c2, pending merge) | [QA: pending] | [QA: pending] |

**Not verified on the emulator, the rework.**

- The counts card's pills and header in every theme: no reworked scenario changes the theme.
- A form row's name on a card, `Unown (A)` (#634): no scenario asserts it.
- A late seed response in the Milestone Editor, when the preset or the version is changed quickly.
- A game file whose ROM version is not known: its card draws as one with no location, and Start skips the archived and unticked check (What diverged, 20 and 24).
- An edit in the Plan Editor while a Save or Rename is in flight (What diverged, 29).
- [QA: pending]

## Open questions

Not the owner's to answer; it is the first step of the task's plan.

1. **The blank window.** In the spike's prototype of FR-39 (one dashboard window per game file, with a new milestone loaded into it), the window showed nothing after both swaps to the phase "Route 30 Night": a dark empty window, no rows, no error logged. Swaps to "Route 30 AM" and back to "Route 32" drew correctly, and "Route 30 Night" drew correctly in an ordinary phase window. It was only ever in throwaway code, never on `main`. C2's plan starts by reproducing it and finding the cause.

   **Answered 2026-11-24** ([research](../research/time-mover.md), "The blank window, explained"): it is not the phase. A window that another window fully covers is not painted by Chromium, and the prototype reloaded the page in it. C2 keeps a card window painting while covered (FR-39a).

Answered on 2026-11-24 and written into "Decided": how a living dex amount is worked out, version exclusives, the female goal and males, Unown, where a species shows, Repel and spinning, cave and sand tiles, when a plan leaves, the two old names, whether unhuntable milestones are listed, Ruby and Sapphire's anchors and presets.

Open, the owner's:

- A "do nothing until the rule's time" choice for a plan, in his words of 2026-11-25: "A do nothing is potentially correct." Not built; the first thing to add if hunting a rule's milestone outside its hours turns out wasteful.

  **Answered 2026-11-27** (the rework, default 6): the Wait step, FR-31a.
- #578: the HGSS graph has no on-foot edge between Route 1 and Viridian City.
- The order of PR #506 (Pickup in Ruby and Sapphire, still open against `main`) and this feature.
- Whether a one-time story scene on a walk should share the trainer stop: Route 32's "Wait up!" NPC, and Lyra's gatehouse scene on map 97 on the SoulSilver save. Today such a walk ends in the navigator's timeout and a Waiting with no message.

  **Answered 2026-11-26** (the owner: "Leave as is"): nothing is built; see the [decision](../decisions/hunt-plans-and-milestones.md).
- 2026-11-27, from the [rework](milestone-builder-rework.md), in its words: "may a plan save the game by itself when it arrives at an egg step? Without it the egg hunt's reset undoes the wild step's levels and Pickup items." Until it is answered, the eggs are not plan steps.

## Not proved by the spikes

Ruby and Sapphire beyond one anchor (their tables and rule are from the decomp and their Repel address from its layout; five anchors were generated and Route 101's was hunted on Sapphire); SoulSilver on the emulator; the ability rules on the emulator; anchors other than Route 30's; reachability and trainer sight in the anchor choice; the radio sound in memory; the 4:00 boundary and weekday rules; a plan across a route needing an HM. The research docs list each.

## Changelog

- 2026-11-24: first draft, from the spikes (#507).
- 2026-11-24: FR-25a, FR-25b and FR-26a written out by the orchestrator from the research doc's Ruby and Sapphire section; the task split named them and the table did not have them. The status line corrected to one open question.
- 2026-11-24: the blank window explained; FR-39a added by the orchestrator from that finding.

- 2026-11-24 (A1, #527, #528, #511), decided by the orchestrator at plan review:
  - FR-1 and FR-3: maps of one location are one milestone per modality when their slots are identical in both versions, levels included; headbutt never merges. That gives 398 milestones per version on HGSS (land 90, surf 63, Old Rod 58, Good and Super Rod 59 each, rock smash 9, headbutt 60), not 466. A merged milestone is named for the location when it holds every map of the location with that modality, else its members' names joined; "Ruins of Alph (underground hall)" is the one exception.
  - The Safari Zone (357) and the Bug-Catching Contest map (487) have no milestone (non-goals). On RS the four Safari Zone maps have none either.
  - FR-2: the version is `RomVersion` on `RomInfo`.
  - FR-3 on RS: the land word comes from the decomp's map type (underground and indoor maps are "cave", every other is "grass"), not from tiles, so FR-19b's new encounter tiles do not rename caves. RS merges by the same rule, with the region map section as the location.
  - A milestone's id is the first map's id and the modality in lower case: `34-land`, `34-oldrod`, `74-headbutt`.

- 2026-11-25 (B3, #520): FR-26's Repel level skips eggs as well as fainted members, as the game's source does. On HGSS Repel cancels land and surf encounters only, not fishing, rock smash or headbutt. Headbutt shares are computed by tree kind. `GameProfile.Rates` is the profile's rate rule, HGSS's answer first.

- 2026-11-25, from the owner ("There should be a way to define the milestone card to switch between multiple milestones by times/day or assign multiple different cards. Maybe a way to define it once and share. Or to get a display card based on the bot run."), written up by the orchestrator and confirmed by the owner the same day:
  - Cards are of two kinds: fixed, as today, any number per game file; and one follows-the-hunt card per game file (FR-38d, FR-39 to FR-39c). FR-39 no longer makes every card that names a game file that file's one window.
  - Plans are saved by name and shared between game files (FR-37a to FR-37c), in a second Mongo collection (NFR-3 amended from one collection to two).
  - Not built: a card that switches by a plan with no hunt running (FR-37c).

- 2026-11-25 (B1, #517), decided by the orchestrator:
  - FR-20's example is (547, 304), not (546, 304): columns 546 and 547 are both 13 clear grass tiles, and FR-19's tie-break (the shorter route to a Pokémon Center, 166 against 171 by the pathfinder) picks 547. The spike's 546 was the first column its loop visited.
  - FR-19: surf anchors are deferred. Start refuses surf, nothing would read one, and what a water anchor means is not defined.
  - FR-19: 71 of HGSS's 90 land milestones have a generated anchor. 19 have none, because the committed pathfinder graph gives no round trip between them and a Pokémon Center (Sprout Tower, Bell Tower, Slowpoke Well B1F and B2F, Burned Tower B1F, Ice Path B2F/B3F, Mt. Mortar B1F, Rock Tunnel B1F, Cerulean Cave 2F and B1F, Seafoam Islands B2F to B4F, Whirl Islands B3F, five Mt. Silver Cave maps; the list and each reason are in the research doc). A generated milestone with no anchor is treated as FR-46 treats a custom one: hunted from where the bot stands, given an anchor by the operator (FR-22), and not a plan's destination until it has one (FR-35). The graph is not changed in this feature.
  - FR-19: trainers and signs are read from the decomp at generation, not from the committed map data. Every trainer's sight is excluded, beaten or not.
  - FR-21 (headbutt anchors, a Should) is its own task.

- 2026-11-25 (B1r, #518), decided by the orchestrator:
  - FR-19b named a "footprint sand" behaviour that does not exist. The RS map data now marks the eight land-encounter behaviours 0x02, 0x03, 0x05, 0x06, 0x08, 0x0B, 0x24 and 0x25 as kind 3; 0x09 is no longer marked. 55 maps' data changed.
  - FR-19a on RS: a trainer's line of sight excludes a tile, a sign does not. A tile behind an HM obstacle is not an anchor; there is no fallback.
  - 24 of RS's 46 land milestones have a generated anchor and 22 have none, treated as HGSS's 19 are. Route 111's anchor is in the desert and needs the Go-Goggles; that is B2's to handle (#519).

- 2026-11-24, from the owner ("why so much QA while working through this. can we QA at the end please"): emulator and window QA runs once, on the feature branch tip, in Q1 (#525). Task PRs merge on the review and the local checks; the checks each task would have run are listed on #525.

- 2026-11-25 (B2, #519, #549), decided by the orchestrator at plan review:
  - B2 is two tasks: B2a the backend (#519) and B2b the dashboard and guide (#549).
  - FR-24 is checked at Start against the committed map data at the anchor's tile, before any walk, not "at the anchor": no memory read gives a tile's kind, so a check after the walk would read the same data. The sentence is unchanged.
  - FR-22 and FR-23: the anchor decides Run or Spin and the range, over the form's choice; "Set anchor here" stores the form's, and the form shows that the anchor decides.
  - FR-23: a hunt that finds no path to its anchor stops with "{milestone}: no path to the anchor ({x}, {y})."; it does not end Complete.
  - Start refuses Headbutt at a land milestone and Run or Spin at a headbutt one.
  - Route 111 gets no item rule; what happens there with and without the Go-Goggles is checked in Q1 (#525).

- 2026-11-25, from the owner ("yeah it can be inline of botcontrol. the spiker had a nice mock"): the plan editor is inline in the Bot Control tab under the Wild Encounter form, and follows the spike's mock, now committed beside this spec. C4 is two tasks, C4a the backend (#564) and C4b the dashboard (#524).

- 2026-11-25 (B1h, #539), decided by the orchestrator at plan review:
  - FR-21's "trainer ID 15613" means the full 32-bit trainer ID, 2318286077 on the owner's save (visible 15613, secret 35374), whose last digit is 7: the game takes `trainerId % 10` on `profile->id`. This is from the game's source, not observed on the emulator.
  - FR-21 for headbutt: each tree's stand, facing and route cost are generated; the file's tree is worked out when asked, from the kinds that hold a species still a target on the file (any tree that gives Pokémon when none is), and never stored (FR-42). Wiring it into Start is a follow-up task (B1h-b) after C3b (#550).

- 2026-11-25 (B1h-c, #572), decided by the orchestrator at plan review of #567: FR-21's headbutt stand needs no HM and is on a free or grass tile, as B1r's land anchors; there is no fallback to every HM, and a stand on water, a Cut tile or a Rock Smash tile is none. It is fixed at generation, where whether a stand needed an HM is known, not in `HeadbuttTrees.AnchorFor`, which sees only the cost. 686 of the 872 trees keep a stand (843 before); Route 1, Route 45, Pallet Town, Route 21 and Mt. Silver Cave's headbutt milestones have none and are hunted where the bot stands. The research doc lists the rest.

- 2026-11-25 (B1h-b, #567), decided by the orchestrator at plan review and in its amendment:
  - FR-21 at Start: a generated headbutt milestone the file has no replacement for gets the file's tree (`GameFile.AnchorFor` on the full trainer ID and the catches), walked to as any anchor; a plan whose stop is such a milestone gets the same anchor and the same reach check. The ID (4 bytes at `[[FS+0x0C]+0x84]`, the secret half from the source, not observed) and the catches are read only then: a stored phase, a replacement or any other milestone reads nothing new (FR-42). A failed read refuses Start. The ID is checked against the full OT ID of a party Pokémon with the same visible ID; one that differs refuses Start naming both values, and with none the Start log says the ID was not cross-checked.
  - The picker reads the 4 bytes alone, with no party read; a failed read gives no headbutt anchor, so the picker works at the title screen as before.
  - Headbutt shares on the card stay off; turning them on is a later task after B4b and B4c.

- 2026-11-25, decided by the orchestrator at plan review and in review cycles, for the tasks that have no entry above:
  - B4a (#522): the HGSS snapshot request already sends 16 blocks, ApiHawk's limit, so the clock and the Repel counter are NOT on the snapshot. The decision makes one small chain request beside its party read (clock, Repel counter, and the save section table until section 21's offset is known). The first decision after a launch has no Repel reading. The Run hunt closes "The Repel's effect wore off" with one A gated on script 2022 waiting in `WaitABPressNative` (NFR-1 is met by the snapshot being untouched; the cost is one request per encounter).
  - B3r (#521): RS's rate rule only. RS's Repel counter and message are B3r2 (#556), which waits on an emulator research round.
  - A6 (#553): the card showing a generated milestone was owned by no task and became one. `phaseinfo` answers 404 only for an unknown phase; a failure is a 500.
  - A4 (#515): the link, the copy and the Start refusal as FR-43 to FR-46 say. The linked name leaves the picker (FR-44). Completion never uses encounter counts, so a link cannot end or prolong a hunt.
  - C3b (#550): a planned hunt starts on the plan's default and the clock's pick replaces it at the first decision, before any walk; every later move is logged under the milestone being left until the bot arrives. A walk that ends off the anchor parks with a sentence. FR-34 as built: the hunt ends when the default is complete and no rule matches the hour, even if another rule's milestone is incomplete.
  - B4b and B4c (#552, #563): shares are computed from the inputs last seen at a decision, kept in memory per game file. During a hunt they change at the first encounter after 4:00, 10:00 or 20:00, not on the hour with no encounter. RS shows plain shares marked not live until #556. No shares on a headbutt card.
  - A5a and A5b (#516, #566): a fifth Admin tab, Goals; hidden rows on Bot Control; assignment, link and copy in the Milestone Editor. A preset adds species the goal lacks and keeps existing entries, so presets do not stack on a species.
  - C4a and C4b (#564, #524): rename and delete are refused while a hunt uses the plan (a hunt parked in Waiting counts). The mock's in-game clock chip is not built: nothing the dashboard can read exposes the weekday and hour. The mock's footer line is qualified: a Follows the hunt window follows the bot.
  - A7 (#574): Static Encounter refuses a linked name too, through a test seam on the game-file store (the one new seam of the feature, AGENTS rule 2's exception).
  - Not built in this feature and open: B3r2 (#556) and C5 (#562, the navigator closing the Repel message mid-walk; until then such a walk parks in Waiting), both waiting on emulator research; the missing Route 1 to Viridian City edge in the HGSS graph (#578); the shares on a headbutt card.
- **2026-11-25, the owner's answers on the built feature.** His words, on the four points put to him:
  - Presets: "Presets dont stack? I dont get what this means. That could be a dropdown that changes the default numbers." FR-8 is rewritten: the preset is a dropdown per game file that supplies the default amounts, and a hand-edited amount survives a change of preset. This replaces "a preset fills entries once; it is not a mode the goal stays in".
  - The plan's ending: "A hunt doesnt end until all parts are complete. If the default hunt is finished before the day one the day one (or whatever first rule is) becomes the default where there is no fitting rule. (There may be exceptions like if bug catching costs pokedollars it might be a do nothing until day. A do nothing is potentially correct." FR-34 is rewritten. A "do nothing until the rule's time" choice is not built: he names it as a possible exception, not a requirement. Open, and the first thing to add if hunting a rule's milestone outside its hours turns out wasteful.
  - "There should be a ping in discord if any hunt part completes." New FR-34a.
  - #578 (no on-foot edge between Route 1 and Viridian City): "There should be? Route 1 is very far off in end game though." It stays an open issue outside this feature.
  - The order of PR #506 and #556 is the orchestrator's to coordinate.
- **2026-11-25, the owner on the counts window** ("Route 32 · grass" with a Repel on). His words: "nota fan. the percentage can be below the mon. the greyed out rows can just not show. only show active mons." And on the footer line: "so we're basiaclyl showing that there are encounters that skip. no human would know this. should not be included. [...] either way i would remove it." FR-27 and FR-41 as built from #508's card task:
  - The Share column is gone; each share is drawn small under the Pokémon's name.
  - A row whose share is zero now is not drawn, so the reasons ("night only", "blocked by Repel", "needs Hoenn Sound") are shown nowhere on the card. A card without shares (a stored phase, a headbutt milestone, a file not seen since the backend started) draws every row. The header's encounter count, the footer's Phase and Rate, and completion still count every row. A Repel that blocks every species leaves the table with no rows.
  - The footer's "NN % of encounters survive" is removed. The endpoint still gives `survive` and each row's `reason`; the card does not read the reason.
- 2026-11-25, close-out (#526):
  - Since #590 (#556), RS shares are live from the first encounter: the decision reads the RS Repel counter (`OrchestratorEvent.cs:492-496`), so B4b and B4c's "RS shows plain shares marked not live until #556" no longer holds.
  - Since #591 (#562), the HGSS navigator closes the Repel message mid-walk (`NavigatorEvent.cs:492`, `:546`), so "until then such a walk parks in Waiting" no longer holds; since #592 (#589), a US RS Run hunt closes it too.
  - Since #585 (#583) and #594 (#593), FR-34 as built is the rewritten rule, so C3b's "FR-34 as built" sentence no longer holds.
  - Since #587 (#582), the preset is the file's stored choice, so A5a's "a preset adds species the goal lacks ... presets do not stack" no longer holds.
  - #593: a plan arriving at a completed stop picks again, and the plan re-reads completion once before ending.
  - The QA table renamed to the sixteen scenarios, the old row names kept in its middle column; "As built", "What diverged" and "Evidence" written; status Implemented. They describe the tip after #602 and #603.
- **2026-11-26, the owner on the Ruins of Alph card** (28 rows all named "Unown"). His words: "Letter in parentheses. Example "Unown (A)". We may later add stuff like Giratina (Altered)". A milestone row for one Unown letter is named `Unown (A)` to `Unown (Z)`, `Unown (!)` and `Unown (?)`, from the backend's row (`PokemonMetadataService.RowName`); an any-form row keeps the plain name. Only Unown gets a form name now; `Species (Form)` is the format for any later one. The Goals tab still names a line by its species, with the form code in its own column.
- 2026-11-26 (N1b, #604): on HeartGold and SoulSilver a trainer's challenge during a navigator walk (a plan's walk, the walk to an anchor, a heal trip and its return) stops the walk, as the owner decided ("A trainer block should be a discord ping and waiting state. I resume when i beat the trainer."). It is recognised by script 3739 (`std_trainer_approach`) under the script task, by the walk's stall watch or its segment end; nothing is pressed, the route is dropped, Waiting goes over the leg with `StoppedForTrainer`, and `A trainer battle started on <map>.` is sent once, with the map's internal name (`R30`). Resume waits for the free field as on Ruby and Sapphire and the leg is planned again from the tile read then; a walk to an anchor stays one across the stop. Research: [milestone-builder.md, "HeartGold and SoulSilver: a trainer's challenge during a navigator walk"](../research/milestone-builder.md#heartgold-and-soulsilver-a-trainers-challenge-during-a-navigator-walk). Route 32's one-time story trigger keeps today's behaviour; the QA scenario is N1c.
- 2026-11-26, close-out (#526): "As built", the QA table and "Evidence" brought up to the tip `0c4d563` (#605 to #610): the Unown row names, the plan list re-read and the card heading, the trainer stop, `hgss-headbutt-anchor` at Violet City; the new-scenario and regression results of the final pass written in, `hgss-milestone-complete` still to run; the open story-scene question.
- 2026-11-27, close-out (#526): `hgss-milestone-complete`'s two results on `0c4d563` written in; it and the cheat hotkey's in-front path leave the not-verified list.
- 2026-11-26, the owner on a one-time story scene met on a walk: "Leave as is". It is not a stop of its own; the walk ends in the navigator's timeout and a Waiting with no message, as before. Recorded in the decisions doc.
- **2026-11-27, the rework, from the owner.** After trying the feature by hand he rejected the generated milestones in the picker, the goal per game file, copy and link, and redefined plans; his words, the answers and the orchestrator's defaults are in the [rework doc](milestone-builder-rework.md), and the three decisions in the decisions doc. Rewritten in place, with their IDs kept: the Terms Milestone, Goal, Hunt plan and Custom milestone; G2; FR-7 to FR-10, FR-12, FR-13, FR-15 to FR-18, FR-22, FR-27, FR-28, FR-31, FR-33 to FR-37b, FR-39b, FR-41, FR-42a, FR-43 to FR-46, NFR-3 and the design notes. Added: FR-7a to FR-7d and FR-31a to FR-31c.
- 2026-11-27, decided by the orchestrator at plan review and in review cycles of the rework's tasks:
  - R1 (#613, PR #623): the per-file goal routes, the copy offer and the generated milestone's route are deleted; `phaseinfo` is one action with `live`, `timeOfDay`, `suggestedLocation` and `location`; the hidden rows name each name once.
  - R2 (#615, PR #626): the linked refusal and the generated picker entries are deleted; a location that does not resolve on the file's version keeps every row; a headbutt tree is chosen only by wanted species its slots have.
  - R4 (#616, PR #624): the re-read on an encounter push is kept, and writes the rows with their shares from one response.
  - R3 (#617, PR #627): "New from game data" starts at Living dex; the egg and static forms keep listing only milestones with no location.
  - R6a (#618, PR #625): `Plans` replaces `HuntPlans`, which is not read or migrated; a parameter holding only whitespace counts as unset.
  - R6b (#619, PR #628): the reach check at Start is dropped (binding answer 4); the archived and unticked refusals are added at Start (review item 5); Bot Control shows `Running` during a Wait and `Plan <name>` before the first pick.
  - R7 (#620, PR #629): "Incomplete only" is hidden in the Plan Editor; its game list offers every game; Rename is kept; a new plan under a saved name cannot be saved.
  - R10 (#633, PR #634): the card names a form row `Unown (A)` again, from `rowNames`.
  - R11 (#637, PR #639): a row only the other version has is not drawn on a game file's card, the orchestrator's default for the owner to correct.
- 2026-11-27, close-out of the rework (#622): status line, Summary, Decided, Task split, As built, What diverged 19 to 32, the QA table, Evidence and the open questions brought to the rework's tip. The Evidence rows wait for the end-of-feature QA.

## Appendix: the maps and their names

Generated in the spike from the decomps' map constants and, for HGSS, the game's own location names. "land" is the walking table: grass, or the floor of a cave or tower. This table is what FR-3 commits as data.

### HeartGold and SoulSilver: 139 maps with a wild table

**The "Shares a table with" column is superseded** (2026-11-24): it compared HeartGold species only and ignored levels. The grouping as built, with 35 merged milestones, is in the [research doc](../research/milestone-builder.md), "The generated file". The Map id and Name columns are the committed names.

Map id is the game's map header number, which the bot reads as the current map. "Shares a table with" lists maps of the same location whose tables are identical in HeartGold; they are one milestone. "Differs" marks the 67 maps whose tables are not the same in HeartGold and SoulSilver.

| Map id | Internal | Name | Modalities | Differs | Shares a table with |
|---|---|---|---|---|---|
| 7 | `D18R0101` | Burned Tower 1F | land |  |  |
| 9 | `R01` | Route 1 | land, headbutt | yes |  |
| 10 | `R02` | Route 2 | land, headbutt | yes |  |
| 11 | `R03` | Route 3 | land, headbutt | yes |  |
| 12 | `R04` | Route 4 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 13 | `R05` | Route 5 | land, headbutt | yes |  |
| 14 | `R06` | Route 6 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 15 | `R07` | Route 7 | land, headbutt | yes |  |
| 16 | `R08` | Route 8 | land, headbutt | yes |  |
| 17 | `R09` | Route 9 | land, surf, old rod, good rod, super rod | yes |  |
| 18 | `R10` | Route 10 | land, surf, old rod, good rod, super rod |  |  |
| 19 | `R11` | Route 11 | land, headbutt |  |  |
| 20 | `R12` | Route 12 | surf, old rod, good rod, super rod, headbutt | yes |  |
| 21 | `R13` | Route 13 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 22 | `R14` | Route 14 | land, headbutt | yes |  |
| 23 | `R15` | Route 15 | land, headbutt | yes |  |
| 24 | `R16` | Route 16 | headbutt |  |  |
| 25 | `R17` | Route 17 | land |  |  |
| 26 | `R18` | Route 18 | land, headbutt | yes |  |
| 27 | `R22` | Route 22 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 28 | `R24` | Route 24 | land, surf, old rod, good rod, super rod |  |  |
| 29 | `R25` | Route 25 | land, surf, old rod, good rod, super rod, headbutt |  |  |
| 30 | `R26` | Route 26 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 31 | `R27` | Route 27 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 32 | `R28` | Route 28 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 33 | `R29` | Route 29 | land, headbutt | yes |  |
| 34 | `R30` | Route 30 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 35 | `R31` | Route 31 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 36 | `R32` | Route 32 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 37 | `R33` | Route 33 | land, headbutt | yes |  |
| 38 | `R34` | Route 34 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 39 | `R35` | Route 35 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 40 | `R36` | Route 36 | land, headbutt | yes |  |
| 41 | `R37` | Route 37 | land, headbutt | yes |  |
| 42 | `R38` | Route 38 | land, headbutt | yes |  |
| 43 | `R39` | Route 39 | land, headbutt | yes |  |
| 44 | `R42` | Route 42 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 45 | `R43` | Route 43 | land, surf, old rod, good rod, super rod, headbutt |  |  |
| 46 | `R44` | Route 44 | land, surf, old rod, good rod, super rod, headbutt |  |  |
| 47 | `R45` | Route 45 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 48 | `R46` | Route 46 | land, headbutt |  |  |
| 49 | `T01` | Pallet Town | surf, old rod, good rod, super rod, headbutt | yes |  |
| 50 | `T02` | Viridian City | surf, old rod, good rod, super rod, headbutt | yes |  |
| 51 | `T03` | Pewter City | headbutt |  |  |
| 52 | `T04` | Cerulean City | surf, old rod, good rod, super rod, headbutt |  |  |
| 54 | `T06` | Vermilion City | surf, rock smash, old rod, good rod, super rod, headbutt |  |  |
| 55 | `T07` | Celadon City | surf, headbutt |  |  |
| 56 | `T08` | Fuchsia City | surf, old rod, good rod, super rod, headbutt | yes |  |
| 57 | `T09` | Cinnabar Island | surf, old rod, good rod, super rod |  |  |
| 60 | `T20` | New Bark Town | surf, old rod, good rod, super rod, headbutt | yes |  |
| 67 | `T21` | Cherrygrove City | surf, old rod, good rod, super rod, headbutt | yes |  |
| 73 | `T22` | Violet City | surf, old rod, good rod, super rod, headbutt |  |  |
| 74 | `T23` | Azalea Town | headbutt |  |  |
| 75 | `T24` | Cianwood City | surf, rock smash, old rod, good rod, super rod |  |  |
| 77 | `T26` | Olivine City | surf, old rod, good rod, super rod |  |  |
| 78 | `T27` | Ecruteak City | surf, old rod, good rod, super rod, headbutt | yes |  |
| 88 | `T29` | Lake of Rage | surf, old rod, good rod, super rod, headbutt |  |  |
| 89 | `T30` | Blackthorn City | surf, old rod, good rod, super rod |  |  |
| 90 | `T31` | Mt. Silver | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 91 | `W19` | Route 19 | surf, rock smash, old rod, good rod, super rod |  |  |
| 92 | `W20` | Route 20 | surf, old rod, good rod, super rod |  |  |
| 93 | `W21` | Route 21 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 94 | `W40` | Route 40 | surf, old rod, good rod, super rod |  |  |
| 95 | `W41` | Route 41 | surf, old rod, good rod, super rod | yes |  |
| 96 | `D22R0101` | National Park | land, headbutt | yes |  |
| 99 | `D25R0101` | Union Cave 1F | land, surf, old rod, good rod, super rod | yes |  |
| 106 | `D01R0101` | Diglett's Cave | land |  |  |
| 107 | `D02R0101` | Mt. Moon | land | yes | 448 |
| 108 | `D05R0101` | Rock Tunnel 1F | land |  |  |
| 113 | `D24R0101` | Ruins of Alph | land, surf, rock smash, old rod, good rod, super rod |  |  |
| 117 | `D36R0101` | Ilex Forest | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 119 | `D38R0101` | Mt. Mortar (1F entrance) | land, surf, old rod, good rod, super rod |  |  |
| 120 | `D39R0101` | Ice Path 1F | land | yes | 237, 238, 239 |
| 121 | `D40R0101` | Whirl Islands 1F | land, surf, old rod, good rod, super rod |  |  |
| 122 | `D41R0101` | Mt. Silver Cave 1F | land, surf, old rod, good rod, super rod | yes |  |
| 123 | `D42R0101` | Dark Cave (Route 45 side) | land, surf, old rod, good rod, super rod |  |  |
| 124 | `D43R0101` | Victory Road 1F | land | yes | 178 |
| 126 | `D45R0101` | Tohjo Falls | land, surf, old rod, good rod, super rod |  |  |
| 145 | `D03R0101` | Cerulean Cave 1F | land, surf, rock smash, old rod, good rod, super rod | yes |  |
| 146 | `D11R0101` | Seafoam Islands 1F | land |  |  |
| 147 | `D46R0101` | Viridian Forest | land, headbutt | yes |  |
| 151 | `R47` | Route 47 | land, surf, old rod, good rod, super rod, headbutt | yes |  |
| 152 | `R48` | Route 48 | land, headbutt | yes |  |
| 153 | `D25R0102` | Union Cave B1F | land, surf, old rod, good rod, super rod | yes |  |
| 154 | `D25R0103` | Union Cave B2F | land, surf, old rod, good rod, super rod |  |  |
| 155 | `D15R0102` | Sprout Tower 2F | land |  | 156 |
| 156 | `D15R0103` | Sprout Tower 3F | land |  | 155 |
| 174 | `D47` | Safari Zone Gate | headbutt | yes |  |
| 176 | `D42R0102` | Dark Cave (Route 31 side) | land, surf, rock smash, old rod, good rod, super rod |  |  |
| 177 | `D26R0102` | Slowpoke Well B1F | land, surf, old rod, good rod, super rod |  |  |
| 178 | `D43R0102` | Victory Road 2F | land | yes | 124 |
| 179 | `D43R0103` | Victory Road 3F | land, rock smash | yes |  |
| 181 | `D26R0103` | Slowpoke Well B2F | land, surf, old rod, good rod, super rod |  |  |
| 217 | `D18R0102` | Burned Tower B1F | land |  |  |
| 237 | `D39R0102` | Ice Path B1F | land | yes | 120, 238, 239 |
| 238 | `D39R0103` | Ice Path B2F | land | yes | 120, 237, 239 |
| 239 | `D39R0104` | Ice Path B3F | land | yes | 120, 237, 238 |
| 242 | `D40R0102` | Whirl Islands B1F | land |  | 486 |
| 243 | `D40R0104` | Whirl Islands B2F | land, surf, old rod, good rod, super rod |  |  |
| 250 | `D38R0102` | Mt. Mortar (1F back) | land |  |  |
| 251 | `D38R0103` | Mt. Mortar 2F | land, surf, old rod, good rod, super rod |  |  |
| 252 | `D38R0104` | Mt. Mortar B1F | land, surf, old rod, good rod, super rod |  |  |
| 253 | `D44R0102` | Dragon's Den | surf, old rod, good rod, super rod |  |  |
| 279 | `D48R0101` | Cliff Edge Gate | surf, old rod, good rod, super rod |  |  |
| 315 | `D24R0205` | Ruins of Alph (underground hall) | land |  | 490, 491, 492 |
| 332 | `D17R0102` | Bell Tower 2F | land |  | 333, 334, 335, 336, 337, 338, 339, 341 |
| 333 | `D17R0103` | Bell Tower 3F | land |  | 332, 334, 335, 336, 337, 338, 339, 341 |
| 334 | `D17R0104` | Bell Tower 4F | land |  | 332, 333, 335, 336, 337, 338, 339, 341 |
| 335 | `D17R0105` | Bell Tower 5F | land |  | 332, 333, 334, 336, 337, 338, 339, 341 |
| 336 | `D17R0106` | Bell Tower 6F | land |  | 332, 333, 334, 335, 337, 338, 339, 341 |
| 337 | `D17R0107` | Bell Tower 7F | land |  | 332, 333, 334, 335, 336, 338, 339, 341 |
| 338 | `D17R0108` | Bell Tower 8F | land |  | 332, 333, 334, 335, 336, 337, 339, 341 |
| 339 | `D17R0109` | Bell Tower 9F | land |  | 332, 333, 334, 335, 336, 337, 338, 341 |
| 341 | `D17R0112` | Bell Tower 10F | land |  | 332, 333, 334, 335, 336, 337, 338, 339 |
| 342 | `D50R0101` | Cliff Cave | land, rock smash |  |  |
| 357 | `D47R0102` | Safari Zone (entrance, outside) | land, surf, old rod, good rod, super rod |  |  |
| 414 | `R02R0101` | Route 2 (east) | land, headbutt | yes |  |
| 422 | `R16R0301` | Route 16 (east) | land, headbutt |  |  |
| 448 | `D02R0102` | Mt. Moon (Square entrance) | land | yes | 107 |
| 449 | `D02R0103` | Mt. Moon (Square) | surf, old rod, good rod, super rod |  |  |
| 450 | `D03R0102` | Cerulean Cave 2F | land, surf, old rod, good rod, super rod |  |  |
| 451 | `D03R0103` | Cerulean Cave B1F | land, surf, rock smash, old rod, good rod, super rod |  |  |
| 452 | `D05R0102` | Rock Tunnel B1F | land, rock smash |  |  |
| 453 | `D11R0102` | Seafoam Islands B1F | land |  | 454 |
| 454 | `D11R0103` | Seafoam Islands B2F | land |  | 453 |
| 455 | `D11R0104` | Seafoam Islands B3F | land |  |  |
| 456 | `D11R0105` | Seafoam Islands B4F | land, surf, old rod, good rod, super rod |  |  |
| 459 | `D41R0102` | Mt. Silver Cave (upper mountainside) | land, surf, old rod, good rod, super rod | yes |  |
| 460 | `D41R0103` | Mt. Silver Cave (lower mountainside) | land, headbutt | yes |  |
| 461 | `D41R0104` | Mt. Silver Cave (Expert Belt chamber) | land | yes |  |
| 462 | `D41R0105` | Mt. Silver Cave (Moltres chamber) | land, surf, old rod, good rod, super rod | yes |  |
| 463 | `D41R0106` | Mt. Silver Cave 2F | land, surf, old rod, good rod, super rod | yes |  |
| 464 | `D41R0107` | Mt. Silver Cave 3F | land, surf, old rod, good rod, super rod | yes |  |
| 486 | `D40R0106` | Whirl Islands B3F | land |  | 242 |
| 487 | `D22R0102` | National Park (Bug-Catching Contest) | land | yes |  |
| 490 | `D24R0216` | Ruins of Alph (underground hall, Sinjoh event) | land |  | 315, 491, 492 |
| 491 | `D24R0217` | Ruins of Alph (hall entrance, Sinjoh event) | land |  | 315, 490, 492 |
| 492 | `D24R0218` | Ruins of Alph (underground hall, Sinjoh event 2) | land |  | 315, 490, 491 |
| 513 | `D02R0104` | Mt. Moon (Square, Clefairy event) | surf, old rod, good rod, super rod |  |  |

### Ruby and Sapphire: 97 maps with a wild table

Map id is map group x 256 + map number, as the bot reads it. "Differs" marks the 30 maps whose tables are not the same in Ruby and Sapphire.

| Map id | Internal | Name | Modalities | Differs |
|---|---|---|---|---|
| 0 | `PetalburgCity` | Petalburg City | surf, old rod, good rod, super rod |  |
| 1 | `SlateportCity` | Slateport City | surf, old rod, good rod, super rod |  |
| 5 | `LilycoveCity` | Lilycove City | surf, old rod, good rod, super rod |  |
| 6 | `MossdeepCity` | Mossdeep City | surf, old rod, good rod, super rod |  |
| 7 | `SootopolisCity` | Sootopolis City | surf, old rod, good rod, super rod | yes |
| 8 | `EverGrandeCity` | Ever Grande City | surf, old rod, good rod, super rod |  |
| 11 | `DewfordTown` | Dewford Town | surf, old rod, good rod, super rod |  |
| 15 | `PacifidlogTown` | Pacifidlog Town | surf, old rod, good rod, super rod |  |
| 16 | `Route101` | Route 101 | land |  |
| 17 | `Route102` | Route 102 | land, surf, old rod, good rod, super rod | yes |
| 18 | `Route103` | Route 103 | land, surf, old rod, good rod, super rod |  |
| 19 | `Route104` | Route 104 | land, surf, old rod, good rod, super rod |  |
| 20 | `Route105` | Route 105 | surf, old rod, good rod, super rod |  |
| 21 | `Route106` | Route 106 | surf, old rod, good rod, super rod |  |
| 22 | `Route107` | Route 107 | surf, old rod, good rod, super rod |  |
| 23 | `Route108` | Route 108 | surf, old rod, good rod, super rod |  |
| 24 | `Route109` | Route 109 | surf, old rod, good rod, super rod |  |
| 25 | `Route110` | Route 110 | land, surf, old rod, good rod, super rod | yes |
| 26 | `Route111` | Route 111 | land, surf, rock smash, old rod, good rod, super rod |  |
| 27 | `Route112` | Route 112 | land |  |
| 28 | `Route113` | Route 113 | land |  |
| 29 | `Route114` | Route 114 | land, surf, rock smash, old rod, good rod, super rod | yes |
| 30 | `Route115` | Route 115 | land, surf, old rod, good rod, super rod |  |
| 31 | `Route116` | Route 116 | land |  |
| 32 | `Route117` | Route 117 | land, surf, old rod, good rod, super rod | yes |
| 33 | `Route118` | Route 118 | land, surf, old rod, good rod, super rod |  |
| 34 | `Route119` | Route 119 | land, surf, old rod, good rod, super rod |  |
| 35 | `Route120` | Route 120 | land, surf, old rod, good rod, super rod |  |
| 36 | `Route121` | Route 121 | land, surf, old rod, good rod, super rod | yes |
| 37 | `Route122` | Route 122 | surf, old rod, good rod, super rod |  |
| 38 | `Route123` | Route 123 | land, surf, old rod, good rod, super rod | yes |
| 39 | `Route124` | Route 124 | surf, old rod, good rod, super rod |  |
| 40 | `Route125` | Route 125 | surf, old rod, good rod, super rod |  |
| 41 | `Route126` | Route 126 | surf, old rod, good rod, super rod |  |
| 42 | `Route127` | Route 127 | surf, old rod, good rod, super rod |  |
| 43 | `Route128` | Route 128 | surf, old rod, good rod, super rod |  |
| 44 | `Route129` | Route 129 | surf, old rod, good rod, super rod |  |
| 45 | `Route130` | Route 130 | land, surf, old rod, good rod, super rod |  |
| 46 | `Route131` | Route 131 | surf, old rod, good rod, super rod |  |
| 47 | `Route132` | Route 132 | surf, old rod, good rod, super rod |  |
| 48 | `Route133` | Route 133 | surf, old rod, good rod, super rod |  |
| 49 | `Route134` | Route 134 | surf, old rod, good rod, super rod |  |
| 50 | `Underwater1` | Underwater (Route 124) | surf |  |
| 51 | `Underwater2` | Underwater (Route 126) | surf |  |
| 6144 | `MeteorFalls_1F_1R` | Meteor Falls 1F 1R | land, surf, old rod, good rod, super rod | yes |
| 6145 | `MeteorFalls_1F_2R` | Meteor Falls 1F 2R | land, surf, old rod, good rod, super rod | yes |
| 6146 | `MeteorFalls_B1F_1R` | Meteor Falls B1F 1R | land, surf, old rod, good rod, super rod | yes |
| 6147 | `MeteorFalls_B1F_2R` | Meteor Falls B1F 2R | land, surf, old rod, good rod, super rod | yes |
| 6148 | `RusturfTunnel` | Rusturf Tunnel | land |  |
| 6151 | `GraniteCave_1F` | Granite Cave 1F | land |  |
| 6152 | `GraniteCave_B1F` | Granite Cave B1F | land | yes |
| 6153 | `GraniteCave_B2F` | Granite Cave B2F | land, rock smash | yes |
| 6154 | `GraniteCave_StevensRoom` | Granite Cave Stevens Room | land |  |
| 6155 | `PetalburgWoods` | Petalburg Woods | land |  |
| 6157 | `JaggedPass` | Jagged Pass | land |  |
| 6158 | `FieryPath` | Fiery Path | land | yes |
| 6159 | `MtPyre_1F` | Mt. Pyre 1F | land | yes |
| 6160 | `MtPyre_2F` | Mt. Pyre 2F | land | yes |
| 6161 | `MtPyre_3F` | Mt. Pyre 3F | land | yes |
| 6162 | `MtPyre_4F` | Mt. Pyre 4F | land | yes |
| 6163 | `MtPyre_5F` | Mt. Pyre 5F | land | yes |
| 6164 | `MtPyre_6F` | Mt. Pyre 6F | land | yes |
| 6165 | `MtPyre_Exterior` | Mt. Pyre Exterior | land | yes |
| 6166 | `MtPyre_Summit` | Mt. Pyre Summit | land | yes |
| 6171 | `SeafloorCavern_Entrance` | Seafloor Cavern Entrance | surf, old rod, good rod, super rod |  |
| 6172 | `SeafloorCavern_Room 1` | Seafloor Cavern Room 1 | land |  |
| 6173 | `SeafloorCavern_Room 2` | Seafloor Cavern Room 2 | land |  |
| 6174 | `SeafloorCavern_Room 3` | Seafloor Cavern Room 3 | land |  |
| 6175 | `SeafloorCavern_Room 4` | Seafloor Cavern Room 4 | land |  |
| 6176 | `SeafloorCavern_Room 5` | Seafloor Cavern Room 5 | land |  |
| 6177 | `SeafloorCavern_Room 6` | Seafloor Cavern Room 6 | land, surf, old rod, good rod, super rod |  |
| 6178 | `SeafloorCavern_Room 7` | Seafloor Cavern Room 7 | land, surf, old rod, good rod, super rod |  |
| 6179 | `SeafloorCavern_Room 8` | Seafloor Cavern Room 8 | land |  |
| 6181 | `CaveOfOrigin_Entrance` | Cave of Origin Entrance | land |  |
| 6182 | `CaveOfOrigin_1F` | Cave of Origin 1F | land | yes |
| 6183 | `CaveOfOrigin_B1F` | Cave of Origin B1F | land | yes |
| 6184 | `CaveOfOrigin_B2F` | Cave of Origin B2F | land | yes |
| 6185 | `CaveOfOrigin_B3F` | Cave of Origin B3F | land | yes |
| 6187 | `VictoryRoad_1F` | Victory Road 1F | land |  |
| 6188 | `VictoryRoad_B1F` | Victory Road B1F | land, rock smash |  |
| 6189 | `VictoryRoad_B2F` | Victory Road B2F | land, surf, old rod, good rod, super rod | yes |
| 6190 | `ShoalCave_LowTideEntranceRoom` | Shoal Cave Low Tide Entrance Room | land, surf, old rod, good rod, super rod |  |
| 6191 | `ShoalCave_LowTideInnerRoom` | Shoal Cave Low Tide Inner Room | land, surf, old rod, good rod, super rod |  |
| 6192 | `ShoalCave_LowTideStairsRoom` | Shoal Cave Low Tide Stairs Room | land |  |
| 6193 | `ShoalCave_LowTideLowerRoom` | Shoal Cave Low Tide Lower Room | land |  |
| 6196 | `NewMauville_Entrance` | New Mauville Entrance | land |  |
| 6197 | `NewMauville_Inside` | New Mauville Inside | land |  |
| 6202 | `AbandonedShip_Rooms_B1F` | Abandoned Ship Rooms B1F | surf, old rod, good rod, super rod |  |
| 6209 | `AbandonedShip_HiddenFloorCorridors` | Abandoned Ship Hidden Floor Corridors | surf, old rod, good rod, super rod |  |
| 6223 | `SkyPillar_1F` | Sky Pillar 1F | land | yes |
| 6225 | `SkyPillar_3F` | Sky Pillar 3F | land | yes |
| 6227 | `ShoalCave_LowTideIceRoom` | Shoal Cave Low Tide Ice Room | land |  |
| 6228 | `SkyPillar_5F` | Sky Pillar 5F | land | yes |
| 6656 | `SafariZone_Northwest` | Safari Zone Northwest | land, surf, old rod, good rod, super rod |  |
| 6657 | `SafariZone_Northeast` | Safari Zone Northeast | land, rock smash |  |
| 6658 | `SafariZone_Southwest` | Safari Zone Southwest | land, surf, old rod, good rod, super rod |  |
| 6659 | `SafariZone_Southeast` | Safari Zone Southeast | land |  |
