# Decisions: Hunt plans and milestones

Decisions about generated milestones, goals and hunt plans (epic #508). Project-wide decisions are in [global.md](global.md). Design: [spec](../specs/hunt-plans-and-milestone-builder.md); evidence: [research: the milestone builder](../research/milestone-builder.md), [research: the time mover](../research/time-mover.md).

---

## Existing milestones keep their stored data; nothing migrates at start-up

Status: Accepted
Date: 2026-11-24 (owner)

### Context
Production holds 20 hand-typed phases and every encounter logged under their names. Generated milestones replace the typing for wild locations, so the old phases could have been converted to the new model when the backend starts.

### Decision
A redeploy changes no stored data (FR-42, FR-42a). Every existing phase is read as stored and behaves as before: its species, amounts, form rows, counts, windows and targets. Moving one onto the generated data is a manual link, per phase and game file, which rewrites neither the phase nor the encounter log (FR-43).

### Consequences
- `Startup.cs` gains only the registrations of the two new DAOs, `MongoGameFileDao` and `MongoHuntPlanDao`; there is no start-up migration step.
- A game file can be part linked: hunts and the dashboard accept stored and generated milestones alike (FR-46).
- A stored phase has no anchor unless the operator sets one, so it cannot be a plan's destination until then (FR-35).

---

## A goal's preset is a dropdown that supplies the default amounts

Status: Superseded by [A milestone is a saved one, made from the game's data; its amounts are its own](#a-milestone-is-a-saved-one-made-from-the-games-data-its-amounts-are-its-own) (2026-11-27)
Date: 2026-11-25 (owner)

### Context
As first built (A5a, #516), a preset added the species the goal lacked and kept existing entries, so presets "did not stack" on a species. The owner: "Presets dont stack? I dont get what this means. That could be a dropdown that changes the default numbers."

### Decision
The preset is one choice per game file, picked from a dropdown, stored on the file, and it supplies the default amounts (FR-8, #582). Choosing another preset replaces every amount the owner has not edited by hand with the new preset's and keeps every hand edit (`GoalPresets.Change`, called from `PhaseMetadataController.ApplyPresetAsync`). This replaces A5a's "a preset adds species the goal lacks".

### Consequences
- `GameFiles` stores the file's `Preset` beside its entries.
- An entry can still be edited or removed by hand at any time.

---

## A planned hunt ends only when every milestone is complete

Status: Superseded by [A plan is a list of whole hunts with time rules, in its own tab and action](#a-plan-is-a-list-of-whole-hunts-with-time-rules-in-its-own-tab-and-action) (2026-11-27): a plan now runs until cancelled unless it is set to end once every shiny-hunt step is done
Date: 2026-11-25 (owner)

### Context
As first built (C3b, #550), a planned hunt ended when its default milestone was complete and no rule matched the hour, even if another rule's milestone was incomplete. The owner: "A hunt doesnt end until all parts are complete."

### Decision
A rule whose milestone is complete is skipped. When no rule matches the clock and the default is complete, the first rule in order whose milestone is not complete stands in for the default, whatever its times and weekdays. The hunt ends only when every milestone the plan names is complete (FR-34; #583, #593).

### Consequences
- A plan can hunt a rule's milestone outside that rule's hours.
- The owner named an exception, in his words: "A do nothing is potentially correct." A "do nothing until the rule's time" choice is not built. It is the first thing to add if hunting a rule's milestone outside its hours turns out wasteful.

---

## A Discord message when a hunt's milestone completes

Status: Accepted
Date: 2026-11-25 (owner)

### Context
A planned hunt works through several milestones, and nothing told the operator when one of them was done. The owner: "There should be a ping in discord if any hunt part completes."

### Decision
When a milestone a hunt is working on becomes complete for the game file, one message goes to the shiny log channel with the mention, naming the milestone and the game file (FR-34a, #584). A planned hunt also names the plan and what it hunts next, or that the plan is finished; a hunt with no plan sends it for its one milestone.

### Consequences
- Once per milestone per hunt. A milestone already complete when the hunt starts sends nothing.
- A failed send is logged in full and the hunt goes on.

---

## The counts card shows only what can be met now, the share under the name

Status: Accepted
Date: 2026-11-25 (owner)

### Context
As first built, the card had a Share column, dimmed rows at a zero share with their reason, and a footer line "NN % of encounters survive". The owner, on Route 32 · grass with a Repel on: "nota fan. the percentage can be below the mon. the greyed out rows can just not show. only show active mons." And on the footer line: "so we're basiaclyl showing that there are encounters that skip. no human would know this. should not be included. [...] either way i would remove it."

### Decision
Each row's share is drawn small under the Pokémon's name. A row whose share is zero now is not drawn. The "encounters survive" footer line is removed (#603).

### Consequences
- The reasons ("night only", "blocked by Repel", "needs Hoenn Sound") are shown nowhere on the card; the endpoint still gives them.
- A card without shares (a stored phase, a headbutt milestone, a file not seen since the backend started) draws every row.
- A Repel that blocks every species leaves the card's table with no rows.

---

## A form row is named "Species (Form)"

Status: Accepted
Date: 2026-11-26 (owner)

### Context
Ruins of Alph's card showed 28 rows all named "Unown". The owner: "Letter in parentheses. Example "Unown (A)". We may later add stuff like Giratina (Altered)".

### Decision
A milestone row for one Unown letter is named `Unown (A)` to `Unown (Z)`, `Unown (!)` and `Unown (?)` (#605). `Species (Form)` is the format for any later form name.

### Consequences
- Only Unown gets a form name now. An any-form row keeps the plain name.
- The Goals tab and stored phases keep plain names; the Goals tab shows the form code in its own column.

---

## A trainer's challenge on an HGSS walk is a Discord message and Waiting

Status: Accepted
Date: 2026-11-26 (owner)

### Context
On HeartGold and SoulSilver an unbeaten trainer who sees the player during a navigator walk (a plan's walk, the walk to an anchor, a heal trip and its return) starts a battle the bot does not fight. The owner: "A trainer block should be a discord ping and waiting state. I resume when i beat the trainer."

### Decision
The walk stops as a Ruby and Sapphire hunt stops on a trainer (#604; research #607, code #608, scenario #609). The challenge is recognised on the walk by script 3739 (`std_trainer_approach`). Nothing is pressed. One message is sent per stop, `A trainer battle started on <map>.`, with the map's internal name (`R30`). Waiting goes over the leg, and Resume, once the operator has won and the field is free, plans the leg again from where the player stands.

### Consequences
- A second unbeaten trainer on the same walk is a new stop with its own message.
- A one-time story scene on a walk (Route 32's "Wait up!" NPC; Lyra's gatehouse scene on map 97 on the SoulSilver save) is not a trainer stop: it ends in the navigator's timeout and a Waiting with no message. Whether it should share the stop is open for the owner.

## A one-time story scene on a walk is not a stop of its own

Status: Accepted
Date: 2026-11-26 (owner)

### Context
The trainer stop left one question open: whether a one-time story scene met on an HGSS navigator walk (Route 32's "Wait up!" NPC; Lyra's gatehouse scene on map 97 on the SoulSilver save) should send a Discord message and park in Waiting as a trainer's challenge does. Asked on 2026-11-26, the owner: "Leave as is".

### Decision
Nothing is built. Such a walk ends in the navigator's timeout and a Waiting with no message, as before.

### Consequences
- The operator plays a save past its one-time scenes before a hunt walks through them.
- The QA scenarios keep their workarounds: Route 32's anchor set on the save's tile (#602), and `hgss-plan-weekday ss` cancelled at midnight's walk line.

---

## A milestone is a saved one, made from the game's data; its amounts are its own

Status: Accepted
Date: 2026-11-27 (owner)

### Context
The owner tried the feature by hand on 2026-11-27 and rejected the generated milestones in the launch picker, the goal per game file and the Game files section's copy and link. His words, as the [rework doc](../specs/milestone-builder-rework.md) quotes them:

- "i should be able to create a milestone from seeded data. give it a name change if I like edit numbers if i see fit. then I save and use that in the bot launch."
- "if the bot control just has milestones from the game data its gonna be insanely maxed out. yeah there is way too many options based on game data and archive is in launch, I don't like."
- "i dont like the way its assigned to game with copy amounts. i should just be able to click a checkbox if it applies to a game or not."
- "the goal per game file makes no sense since custom milestones. i want to create a milestone based on the game data adjust the numbers with the preset, then modify and save."

This reverses "Wanted amounts are global per game file", decided on 2026-11-24 (the [spec](../specs/hunt-plans-and-milestone-builder.md)'s Decided list).

### Decision
There is one kind of milestone: a saved one. The game's data is only where a new one starts from. In the Milestone Editor, "New from game data" takes a location from one dropdown and a version filter, a preset fills the amounts, and the name and every row can be changed before Save; nothing is stored before Save. A saved milestone has game file checkboxes and an Archived flag in the editor, and the launch form lists only the saved milestones ticked for the bot's file and not archived. A milestone made from game data keeps its location, which gives the anchor, the shares, the markers and the Start checks.

The owner's answers of 2026-11-27: the caught counts stay global per game file ("yes the caught counts are global per game"), so a shiny caught on a file counts for that species in every milestone ticked for the file; one milestone per location, with markers; the data loads with a version filter ("I should be able to load the data and filter which version exclusives load."), and a species only one version has is marked with that version; and "Attach to a location" for the existing milestones.

### Consequences
- Defaults 1 to 5 and 11 of the rework doc, each the orchestrator's default, for the owner to correct:
  - 1: a milestone with no game file ticked is offered on every file.
  - 2: Archived is one flag per milestone.
  - 3: per-file hidden rows are gone: amount 0, or remove the row.
  - 4: a saved milestone's name is its key in the encounter log. As built, a saved milestone has no name input, so it cannot be renamed; a new one's name is free, and a name already taken is refused in the form.
  - 5: on a file of one version, a row only the other version has is not a target and does not hold completion back.
  - 11: the seeded read-only milestones can be ticked, archived and attached.
- The Goals tab named in "A form row is named "Species (Form)"" is gone.
- A `GameFiles` document written before the rework keeps its goal and picker fields; they are ignored on load, not migrated.
- It refines "Existing milestones keep their stored data": nothing migrates still holds, and the manual link is now Attach to a location, which writes only the milestone's location.

---

## A plan is a list of whole hunts with time rules, in its own tab and action

Status: Accepted
Date: 2026-11-27 (owner)

### Context
As first built, a plan was a Wild Encounter's default milestone and rules, edited inside the Wild Encounter form. The owner, as the [rework doc](../specs/milestone-builder-rework.md) quotes him: "i think plans needs to be the encounter modal and it's time rule. not where it hunts. we might want a plan editor as it own tab. it can even probably pick things like HGSS primo egg, etc. chained encounter. then we can just load a plan. and from a plan we can load a dashboard card." Before that: "maybe i want the plan to be like train or pickup mode ... im hunting only for a morning mon and want to do pickup battle mode to get items on other times." The mock was agreed with one correction, his words: "bot control should have the same options for a running bot, with plan as additional".

### Decision
A plan is a list of steps. A step is a whole hunt as Bot Control's form defines it, plus a time rule; one step is the default. Plans are edited in their own Admin tab, the Plan Editor. In Bot Control a running bot keeps every action it has, and "Plan" is one more action, taking a saved plan. A dashboard card can follow a plan's current step.

### Consequences
- Defaults 6 to 10 and 12 of the rework doc, each the orchestrator's default, for the owner to correct:
  - 6: a step is Wild Encounter in all its modes, or Wait. HGSS Togepi Egg, HGSS Primo Egg, HGSS Static Encounter and Chained Encounter are refused as steps, with one sentence for all of them.
  - 7: a party a step cannot run with is refused at Start with the reason, named by the step.
  - 8: on returning to a training step the level target carries on; a complete step is skipped.
  - 9: a plan runs until cancelled unless it is set to end when every shiny-hunt step is complete.
  - 10: saved plans in the old shape are dropped.
  - 12: "Plan" is HGSS only, and Ruby and Sapphire cards carry no markers.
- Open, the owner's, in the rework doc's words: "may a plan save the game by itself when it arrives at an egg step? Without it the egg hunt's reset undoes the wild step's levels and Pickup items."
- A switch happens only at the hunt's decision, between encounters, never in a battle or a heal trip; Pickup items are not collected at a switch.
- A step complete in a run is skipped until the next Start.
- The anchor reach check at Start (FR-35) is dropped (binding answer 4 on #619): an anchor with no path parks at the walk.
- `Plans` replaces `HuntPlans`, which nothing reads.
- It refines "A Discord message when a hunt's milestone completes": the message names the milestone and the game file only, not the plan or what comes next.

---

## The counts card: markers, the hidden-now row, counts in the header, no footer

Status: Accepted
Date: 2026-11-27 (owner)

### Context
The owner, as the [rework doc](../specs/milestone-builder-rework.md) quotes him: "i want markers. its really confusing cause you have AM and PM cards and it looks like you've done it manually." And: "I wanted to remove the footer and have the encounter rate be below the pokemon name in the Name column."

### Decision
Each row carries a marker for when it can be met (morning, day, night, or all day), where some row of the location is met at some times only. The share is under the Pokémon's name for every milestone with a location. The footer is removed. As the orchestrator's default, for the owner to correct: the phase count, the encounters per hour and "not live" move to the card's header, and the rows hidden now are one dim last row of the table ("2 more at night: Hoothoot, Spinarak").

### Consequences
- It refines "The counts card shows only what can be met now, the share under the name". Its consequence "a file not seen since the backend started draws every row" still holds for a location whose slots depend on the time of day, drawn with no shares, except a row only the other version has.
- A form row is named `Unown (A)` on the card again (#634), as "A form row is named "Species (Form)"" decided; a row for any form has the plain name, and the Milestone Editor shows and saves the plain name.
- A row only the other version has at the location is not drawn on a game file's card, nor named in the hidden row (#639); the Milestone Editor still shows it. The orchestrator's default, for the owner to correct.
