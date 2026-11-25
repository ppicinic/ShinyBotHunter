# Spec: hunt plans and the milestone builder

Status: Draft, from the spikes of 2026-11-24, with the owner's answers of the same day written in. In progress on `feature/hunt-plans-milestones`. The open question is answered; see "Open questions".
Last updated: 2026-11-24
Owner: Phil
Related: [research: the time mover](../research/time-mover.md) (the clock, the walk, the dashboard window), [research: the milestone builder](../research/milestone-builder.md) (the data, the encounter rule, the memory reads, the anchors, the name mapping), [headbutt research](../research/headbutt.md), [pathfinder spec](pathfinder-maps-flags-hms.md)

## Summary

Two things that share one model.

**The milestone builder.** Today a milestone is typed in by hand: a name, a list of species, an amount each, and one copy per game file ("1-4 Route 30", "1-4b Route 30", "1-4c Route 30"). This replaces the typing with data. Every wild location and way of hunting it becomes a milestone generated from the game's own tables: 398 per version in HeartGold and SoulSilver once maps that share a table are merged (the spikes counted 466 and 283 with every map separate; see the changelog). What a game file still needs is its **goal**, held once per game file, not once per milestone. A milestone on screen is the game's table seen through that file's goal: the species still wanted there, their forms, how many are left, and the share of encounters each should be right now.

**Hunt plans.** A Wild Encounter can be given rules: at night go to Route 30, on Fridays go to Union Cave, otherwise hunt Route 32. The bot reads the in-game clock, walks to the milestone's hunting spot when the rule changes, and the dashboard follows.

The spikes proved the parts on HeartGold: the hunt walked between Route 32 and Route 30 on the in-game clock in three runs; every milestone and its tables were generated for both versions; a hunting spot was generated for every grass and surf milestone and one was hunted on; the Repel counter and the live clock were found in memory.

Ruby and Sapphire get the milestone builder in the same feature, with their own tables and their own encounter rule, so the two games do not drift. They have no time of day, so hunt plans are HeartGold and SoulSilver only. Nothing of Ruby and Sapphire was run on the emulator for this spec.

## Terms

| Term | Meaning |
|---|---|
| Milestone | One map (or several that share a table) and one way of hunting it: land (grass or a cave floor), surf, Old Rod, Good Rod, Super Rod, rock smash, and on HGSS headbutt. Generated. Has a name, slots, and an anchor. |
| Slot | One line of the game's table: a species, a level or level range, a weight, and when it applies (morning, day, night, Hoenn Sound, Sinnoh Sound, night fishing). |
| Goal | What one game file is collecting: for each species or form, how many. One per game file. |
| Row | One line of a milestone on screen: a species, or one form of it. |
| Anchor | Where and how a milestone is hunted: a tile, a facing, and run left-right with a range, run up-down with a range, or spin. For headbutt and fishing, a tile and a facing. |
| Hunt plan | A default milestone and ordered rules, each a set of times of day and weekdays and a milestone. |
| Custom milestone | A milestone typed by hand, as all are today. Starters, Togepi, Jirachi and training stay custom. |

## Goals

- G1: no milestone for a wild location is typed by hand.
- G2: an amount is entered once per game file, not once per place the species appears.
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
| FR-7 | A goal belongs to one game file and lists wanted entries: a species in any form, or one form, with an amount. It is stored in Mongo with the other schema-fluid data. | Must | Unit test. |
| FR-8 | A goal can be started from a preset and then edited. HGSS: living dex (every species, amount = the size of its evolution line); female forms (every species with a female form: the female form at the line's size, the male form at 0); version exclusives (species in this version's tables and not the other's, 1 each). RS: living dex and version exclusives. A preset fills entries once; it is not a mode the goal stays in, and every entry and amount it fills can be edited or removed. | Must | Unit test per preset; unit test that an edited amount survives. |
| FR-9 | Caught counts are global per game file: a shiny caught anywhere on that file counts toward its entry, whichever milestone it was caught under. | Must | Unit test. |
| FR-10 | A milestone shown for a game file has a row for every species in its slots, whether or not the goal wants it, so the statistics are kept. A species the goal has no entry for is a row at 0. Where the goal has entries for forms of a species, each form is a row under the species (female and male; the 28 Unown letters). | Must | Unit test: Route 30 for the three production goals gives the rows of today's three phases, zero rows included. |
| FR-11 | A row shows wanted, caught (global, FR-9), encounters at this milestone, and encounters on this game file, as the phase card does today. | Must | QA. |
| FR-12 | A row at 0 is shown and counted and is never a target. A row whose entry is complete stays shown, marked complete, and is not a target. A row can also be hidden by the operator: a hidden row is not shown and is not a target, and its encounters are still logged. | Must | Unit test; QA. |
| FR-13 | `PhaseTarget` asks the goal: a wild Pokémon is a target when it is shiny, its species and form match an entry with an amount above 0 that is not complete, and its row at the current milestone is not hidden. A shiny that is not a target is handled as it is today. | Must | Unit test. |
| FR-14 | A milestone is complete for a game file when none of its rows is a target. | Must | Unit test. |

### The picker

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-15 | The bot control's milestone picker lists, for the bot's game file: milestones of its version that have at least one row, and custom milestones assigned to it or to no file. It can be filtered by text, by modality, and to incomplete only. | Must | QA. |
| FR-15a | A milestone whose modality the bot cannot hunt (surf, the rods, rock smash) is listed with its rows and rates, marked "no hunt yet", and Start refuses it with that reason. | Must | Unit test; QA. |
| FR-16 | A custom milestone can be assigned to one or more game files; none means all. Generated milestones need no assignment: the goal decides. | Must | Unit test. |
| FR-17 | Archive, per milestone and game file, hides it from the picker and changes nothing else: its counts, its windows and its log are as before. | Must | Unit test; QA. |
| FR-18 | The owner's order ("1-4") is kept as an optional label on a milestone per game file and sorts the picker; without one, milestones sort by name. | Should | QA. |

### Anchors

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-19 | HGSS. Each grass, cave and surf milestone has a generated anchor: among tiles of the milestone's kind inside its own maps, the longest straight run that the pathfinder can reach from the map's entrance and that is in no trainer's line of sight and under no sign. Left-right is preferred over up-down at equal length; under three tiles, a single tile to spin on. Among equal runs, the one with the shortest route to a Pokémon Center. | Must | Unit test on the committed map data for Route 30, Route 32 and one cave. |
| FR-19a | RS. Each land milestone's anchor is a spin on the land tile with the cheapest route to a Pokémon Center's counter, by `RouteSearch` with no HM. The spike's results, to be reproduced: Route 101 (4, 2) to Oldale Town, Route 102 (4, 7) to Petalburg City, Route 103 (12, 15) to Oldale Town, Route 104 (2, 11) to Rustboro City, Route 110 (14, 97) to Slateport City. | Must | Unit test on the committed RS data for those five. |
| FR-19b | RS. A land tile is one whose tile behaviour the game marks as giving wild encounters and that is not water: in `pokeruby`'s `src/metatile_behavior.c` these are tall grass, long grass, ash grass, the footprint sand of Route 111's desert, cave floor and indoor-encounter floor. Plain sand gives none. The committed RS map data marks only grass as kind 3, so Route 111 gave no tile in the spike; the map data's generator is extended to mark every such tile, and the anchor is then chosen as FR-19a says. In a cave that is almost every floor tile. | Must | Unit test: Route 111 has an anchor on its desert; a cave has one on its floor. |
| FR-20 | The anchor is the middle tile of the run with the range that reaches both ends, since the Run encounter paces an equal distance each way and a longer line means fewer turns. | Must | Unit test: Route 30 gives (547, 304), up-down, range 6. |
| FR-21 | Headbutt, fishing and rock smash anchors are a tile and a facing: the tree, shore tile or rock with the shortest route to a Pokémon Center. For headbutt the tree must be one that gives the wanted table for the game file's trainer ID, so the anchor is worked out per game file. | Should | Unit test for Azalea Town with trainer ID 15613. |
| FR-22 | The operator can replace a milestone's anchor with where the bot stands and how it faces, per game file, and can clear the replacement. | Must | QA. |
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
| FR-27 | The phase card shows each row's expected share now, and the survive share when it is below 100 %. A row whose share is zero now (wrong time of day, cancelled by Repel) is dimmed and says why: "night only", "blocked by Repel". | Must | QA. |
| FR-28 | With no hunt running on the game file, the card shows the shares for the time of day the game file was last seen in, with no Repel and no lead effect, marked as not live. | Should | QA. |
| FR-29 | A Spin hunt is expected never to run a Repel out. A Run hunt and a plan's walk do spend steps: when "Repel's effect wore off..." comes up during a hunt, the bot closes it with A, confirmed by the field being free, and carries on. | Should | QA: the counter is unchanged across thirty spins; the message is closed on a Run hunt. |
| FR-30 | The radio sounds are a condition a row can have and a rate input, but the playing sound is not read: a milestone's sound rows are shown as "needs Hoenn Sound" and counted as off. | Should | Unit test. |

### Hunt plans

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-31 | HGSS only. Wild Encounter takes an optional plan: a default milestone and ordered rules. A rule is a set of times of day (morning 4 to 10, day 10 to 20, night 20 to 4), a set of weekdays, and a milestone. The first rule that matches the in-game clock wins; none matching is the default. | Must | Unit test. |
| FR-32 | The clock is the game's live one (`0x021D1058`: weekday, hour), read in the bot's snapshot, not the save block's copy, which is up to a minute and a battle behind. | Must | Unit test with `FakeMemoryClient`; QA. |
| FR-33 | The plan is consulted at each decision between encounters. When the milestone it names is not the one being hunted, the bot walks to that milestone's anchor with the navigator and hunts there. | Must | QA. |
| FR-33a | A move happens at the first decision after the rule stops matching. There is no minimum stay. | Must | Unit test. |
| FR-34 | A rule whose milestone is complete for the game file is skipped. When the default is complete too, the hunt ends. | Must | Unit test. |
| FR-35 | Start refuses a plan with a milestone that has no anchor, or whose anchor the pathfinder cannot reach with the party and badges as they are, naming the milestone. | Must | Unit test. |
| FR-36 | An encounter on the walk is logged under the milestone being left until the bot stands on the new anchor. | Must | Unit test. |
| FR-37 | The dashboard's form shows the rules, the in-game clock, and a 24-hour preview for a chosen weekday (the mock in the session of 2026-11-24). | Should | QA. |
| FR-37a | A plan is defined once and saved by name, per game: its default milestone and its rules. Wild Encounter picks a saved plan by name; the same plan can be used on any game file of that game. Plans are stored in Mongo, in their own collection. | Must | Unit test: a plan saved once is started on two game files. |
| FR-37b | The dashboard lists the saved plans and can add, edit, rename and delete one. A plan in use by a running hunt cannot be deleted. Editing a plan does not change a hunt already running on it; the hunt reads the plan at Start. | Must | Unit test; QA. |
| FR-37c | A card does not follow a plan by itself: with no hunt running, the in-game clock is not known. A plan reaches a card only through the hunt, by FR-39b. | Must | By design; no test. |

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
| FR-39b | A follows-the-hunt card changes when a hunt starts on its game file at another milestone and when a plan moves the hunt. With no hunt running it keeps showing the last milestone hunted, which is stored with the subscription. | Must | Unit test; QA. |
| FR-39c | Existing subscriptions are fixed cards and are read as stored (FR-42). The operator adds a follows-the-hunt card from the dashboard configuration, where a fixed card is added today. | Must | QA: `hgss-existing-unchanged`. |
| FR-39a | A phase window is painted while another window covers it, so a capture of it never shows a blank or stale card. | Must | QA: a swap with the window fully covered, captured, shows the new milestone. |
| FR-40 | A change of milestone replaces the follows-the-hunt card's milestone in the dashboard configuration in one push; a game file with no such card gets no push and no window. | Must | Unit test. |
| FR-41 | The window re-reads the in-game time of day from the bot's state, so its rows dim and its shares change at 4:00, 10:00 and 20:00 in-game without a move. | Must | QA. |

### Existing milestones and the backfill

| ID | Requirement | Priority | Verified by |
|---|---|---|---|
| FR-42 | A redeploy changes no stored data. Every existing phase is a custom milestone and behaves exactly as it does today: its species, amounts, form rows, counts, windows and targets, on both games. Nothing is migrated at start-up. | Must | Unit test: production's 20 phases, loaded as stored, give the same rows and targets before and after. QA: every existing scenario unchanged. |
| FR-42a | The existing phase documents are read as stored; their schema does not change. The phase editor keeps editing them. An existing milestone is shown with today's phase card, unchanged, with a note where a generated milestone shows a share. | Must | QA: `hgss-existing-unchanged`. |
| FR-43 | Backfill is an operator action on one existing milestone and game file: link it to a generated milestone. The mapping in the research doc ("The existing names") is offered as the suggestion and can be overridden. The link is stored; the phase's document and the encounter log are not rewritten. | Must | Unit test; QA: `hgss-backfill`. |
| FR-44 | After a link, the generated milestone's counts for that game file include the encounters and catches logged under the linked name, and the linked name no longer appears in the picker. Removing the link restores it. | Must | Unit test with production's names. |
| FR-44a | After a link, new encounters and catches on that game file are logged under the generated milestone's id. The linked name is only ever read. | Must | Unit test. |
| FR-45 | The link offers to copy the phase's amounts into the game file's goal. Nothing is copied without the operator choosing it, and an entry the goal already has is shown side by side, not overwritten. | Must | Unit test. |
| FR-46 | Hunts and the dashboard accept custom and generated milestones alike, so a game file can be part backfilled. A custom milestone has no slots and no expected shares, and its amounts stay its own, not a goal's. It has no anchor unless the operator sets one from where the bot stands (FR-22); without one it can be hunted from where the bot stands and cannot be a plan's destination (FR-35). | Must | QA. |

## Non-functional

- NFR-1: reading the clock, the Repel counter and the lead adds no read to the flee-only hunt beyond one block; encounters per hour do not fall on either game (the Route 33 speed check; `rs-wild-speed` at 265 an hour or more).
- NFR-2: the rate computation is pure and has no emulator in it.
- NFR-3: no new package, no new store, no ApiHawk change. New data files and two Mongo collections (`GameFiles`, one document per game file; the saved plans) are listed in the PR.
- NFR-4: a plan's move is as safe as a heal trip: every wait bounded, a stop naming what it saw.

## Design notes

- **One model, two features.** The plan picks a milestone; the milestone carries the anchor the navigator walks to and the slots the dashboard and the target ask about. Without generated anchors a plan would need a tile typed per rule, which is how the spike's first run ended up hunting a path.
- **Where it sits.** The milestone data loads beside the map data. The goal and the rate computation are plain classes. `PhaseTarget` and the count queries ask the goal instead of a phase's list. The plan is consulted in `OrchestratorEvent`'s decision, where the heal trip is decided, and moves with the navigator's existing walk to a start tile.
- **Game profile.** The milestone file, the rate rule and the reads are the profile's, one answer per game. Goals, rows, the picker, archive, anchors and the old-name mapping are the same code for both. Plans are offered only where the profile has a clock.
- **Save sections.** The Repel counter is reached through the save's section table (research doc), which also gives the party and the clock. One read of the table per launch.

## QA

New scenarios, on a copy of `hgss.qa-route32` unless said; the emulator's DS clock is set by an own config for the run.

| Scenario | Proves |
|---|---|
| `hgss-plan-night` | From Route 32 at 19:58 in-game: hunts Route 32, moves to Route 30 after 20:00, the window is the same handle and bounds, night species are logged under Route 30. |
| `hgss-plan-morning-back` | From 09:52: moves to Route 30, back to Route 32 after 10:00. |
| `hgss-plan-weekday` | A weekday rule matches and one for another day does not. |
| `hgss-plan-refused` | A plan naming a milestone behind Surf on a save without it is refused. |
| `hgss-milestone-anchor` | Starting "Route 30 · grass" from Route 32 walks to (547, 304) and runs up and down. |
| `hgss-milestone-bad-anchor` | A replaced anchor on a path stops the hunt with FR-24's sentence. |
| `hgss-rates-repel` | The Repel counter written during a hunt: the card's shares change, blocked rows dim; at zero the message is closed and the hunt carries on. |
| `hgss-rates-clock` | The card's rows change at 20:00 in-game with no move. |
| `hgss-goal-rows` | One milestone, three game files with the three presets: three different sets of rows. |
| `hgss-existing-unchanged` | A database holding today's phases, started on the new build with no backfill: the picker, the card and a hunt behave as before. |
| `hgss-backfill` | Linking "1-4 Route 30" to "Route 30 · grass" for one game file: its encounters and catches show under the generated milestone, the old name leaves the picker, and removing the link restores it. |
| `rs-milestone-rows` | On `rs.qa-route101`: "Route 101 · grass" shows Wurmple, Zigzagoon and Poochyena at 45, 45 and 10, and a goal decides the rows. |
| `rs-milestone-anchor` | Starting "Route 101 · grass" walks to its generated anchor and hunts there. |
| `rs-rates-repel` | The Repel counter written during a hunt changes the shares. |

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

## Open questions

Not the owner's to answer; it is the first step of the task's plan.

1. **The blank window.** In the spike's prototype of FR-39 (one dashboard window per game file, with a new milestone loaded into it), the window showed nothing after both swaps to the phase "Route 30 Night": a dark empty window, no rows, no error logged. Swaps to "Route 30 AM" and back to "Route 32" drew correctly, and "Route 30 Night" drew correctly in an ordinary phase window. It was only ever in throwaway code, never on `main`. C2's plan starts by reproducing it and finding the cause.

   **Answered 2026-11-24** ([research](../research/time-mover.md), "The blank window, explained"): it is not the phase. A window that another window fully covers is not painted by Chromium, and the prototype reloaded the page in it. C2 keeps a card window painting while covered (FR-39a).

Answered on 2026-11-24 and written into "Decided": how a living dex amount is worked out, version exclusives, the female goal and males, Unown, where a species shows, Repel and spinning, cave and sand tiles, when a plan leaves, the two old names, whether unhuntable milestones are listed, Ruby and Sapphire's anchors and presets.

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
