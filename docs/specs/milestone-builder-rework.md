# Milestone builder rework: milestones are made from the game data, not listed from it

Status: Agreed in outline, 2026-11-27; the owner: "this matches. the mock is missing some config options, but i think the jist is there. you can get started."
Reworks part of [hunt plans and the milestone builder](hunt-plans-and-milestone-builder.md) (epic #508, PR #611 not merged).

## Why

The owner tried the feature by hand on 2026-11-27 and rejected three things. His words:

- "i want markers. its really confusing cause you have AM and PM cards and it looks like you've done it manually."
- "i should be able to create a milestone from seeded data. give it a name change if I like edit numbers if i see fit. then I save and use that in the bot launch."
- "if the bot control just has milestones from the game data its gonna be insanely maxed out. yeah there is way too many options based on game data and archive is in launch, I don't like."
- "i dont like the way its assigned to game with copy amounts. i should just be able to click a checkbox if it applies to a game or not."
- "the goal per game file makes no sense since custom milestones. i want to create a milestone based on the game data adjust the numbers with the preset, then modify and save."

This reverses the decision of 2026-11-24, "Wanted amounts are global per game file".

## The model after the rework

There is one kind of milestone: a saved one, as the Milestone Editor makes today. The game data is only where a new one starts from.

1. **Create from game data.** In the Milestone Editor, "New from game data" asks for the game and a location, picked from one dropdown of every location, grouped by kind ("Route 30 · grass"; the owner, 2026-11-27: "location should be a dropdown"). It fills the form with that location's species as rows.
2. **Preset fills the numbers.** A preset dropdown on the form (none, living dex, female forms, version exclusives; no female forms on Ruby and Sapphire) fills each row's amount by the rules already built (evolution-line size, exclusives at 1, Unown 28 letters, male at 0 on the female preset). Choosing another preset replaces amounts not edited by hand.
3. **Edit and save.** The name is prefilled with the location's and can be changed. Any amount can be changed, any row removed or added. Save stores it as a milestone. Nothing is stored before Save.
4. **Game files are checkboxes.** The editor lists the game's files with one checkbox each: ticked means the milestone is offered on that file. No "copy amounts", no link to a generated milestone.
5. **Archive is in the editor.** A milestone is archived and restored in the Milestone Editor. Archive only hides it from the launch form, as decided.
6. **The launch form lists saved milestones only**: those ticked for the bot's game file and not archived. The 398 locations per version are never listed there.
7. **Time-of-day markers.** Each row on the counts card and in the editor carries a marker for when the species can be met: morning, day, night, or none when it is there all day. Rows not meetable now stay hidden on the card as decided on 2026-11-25, and the card says how many are hidden and when they return ("2 more at night").

A milestone made from game data remembers which location it came from. That is what keeps the anchor, the shares under the names, the markers and the Start checks working without the owner entering anything.

## What goes away

- The Goals tab and the goal per game file (`GameFile.Entries`, the preset per file).
- Generated milestones in the launch picker, with its modality filter, Archive button and Order label there.
- "Copy amounts" and "Link" in the editor's Game files section; the backfill offer.
- The requirement rows these implement in the feature doc (FR-7 to FR-10, FR-15 to FR-18, FR-43 to FR-45) are rewritten, not left standing.

## What stays as built

The generated data files, anchors and the walk to them, rates and shares, the Repel close, hunt plans and their walks, the trainer stop, the card that follows the hunt, the Discord messages.

## Answered by the owner, 2026-11-27

1. **Caught counts are per game file.** "yes the caught counts are global per game." A shiny caught anywhere on a file counts toward that species on every milestone ticked for that file.
2. **One milestone per location, with markers.** No separate morning and night milestones are made from the game data.
3. **Loading the data takes a version filter.** "I should be able to load the data and filter which version exclusives load." "New from game data" loads a location's species for both versions and offers: both, HeartGold only, SoulSilver only (Ruby, Sapphire). A species only one version has is marked with that version. A milestone may be ticked for files of either version; he does not expect to use that.
4. **A hunt plan is his saved milestones with the time rules.** A stop is a saved milestone. The walk between stops needs each stop's location, so a milestone with no location is refused as a stop, with that reason.
5. **"Attach to a location" for the 20 existing milestones: yes.**

He asked for UI mocks before any code: [milestone-builder-rework-mock.html](milestone-builder-rework-mock.html).

## Plans, as the owner redefined them on 2026-11-27

His words: "i think plans needs to be the encounter modal and it's time rule. not where it hunts. we might want a plan editor as it own tab. it can even probably pick things like HGSS primo egg, etc. chained encounter. then we can just load a plan. and from a plan we can load a dashboard card." Before that: "maybe i want the plan to be like train or pickup mode ... im hunting only for a morning mon and want to do pickup battle mode to get items on other times."

Agreed on 2026-11-27 (mock screens 5 to 7), with one correction, his words: "bot control should have the same options for a running bot, with plan as additional":

- A plan is a list of steps. A step is a whole hunt as Bot Control's form defines it (Wild Encounter with its milestone, type, training and Pickup; Chained Encounter; HGSS Primo Egg; and so on) plus a time rule. One step is the default.
- Plans are edited in their own Admin tab, not inside the Wild Encounter form.
- In Bot Control a running bot keeps every action it has today, and "Plan" is one more action in that list, taking a saved plan. It is not a switch between two modes.
- A dashboard card can follow a plan's current step.
- This replaces answer 4 above and the plan design in the feature doc (FR-31 to FR-37b).

Open: when a plan ends; what a switch does to a hunt in progress (a battle, an egg being hatched, a chain's leg); which hunts can be steps on the first cut.

## The counts card, from the owner on 2026-11-27

His words: "I wanted to remove the footer and have the encounter rate be below the pokemon name in the Name column." He did not see shares on the branch because his milestones are stored ones with no location; a milestone made from game data or attached to a location gets them.

- The share is under the Pokémon's name (as #603 built for generated milestones), now for every milestone with a location.
- The footer is removed. Built as the orchestrator's default, for him to correct: the phase count and encounters per hour move to the card's header; the rows hidden now are one dim last row of the table ("2 more at night: Hoothoot, Spinarak"); "not live" joins the header.

## Size

Large: the editor, the launch form, goal storage and the picker on the backend, the plan editor, the card, about ten QA scenarios, the guide. It goes through the feature workflow as new tasks on `feature/hunt-plans-milestones` once this doc is agreed.
