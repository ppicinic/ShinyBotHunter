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

Status: Accepted
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

Status: Accepted
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
