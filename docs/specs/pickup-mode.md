# Spec: Pickup mode (HGSS)

Status: In progress (epic #273). P1 to P6 landed on feature/pickup-mode (PRs #280 to #286; QA passed on f72c233, run folders in the research doc's [Evidence](../research/pickup.md#evidence) and on the epic). Revised 2026-11-09 for the owner's rule that [hunts act as a human would](../decisions/global.md#hunts-act-as-a-human-would-the-result-is-seen-before-it-is-logged): the first build decided from the post-battle party read alone; now the Pokémon menu is opened after every won battle and the party is read while the list is on screen (FR-5, FR-6, FR-9; tasks P7 and P8). Divergences are under [What diverged](#what-diverged).
Last updated: 2026-11-09
Owner: Phil
Related: [research: the spike](../research/pickup.md) (the source for every value, timing and rule here), [headbutt encounter spec](headbutt-encounter.md) (the greyed-out choice and the party-read cache this reuses), [HGSS state reader spec](hgss-state-reader.md) (the sub-app reads), [QA automation](qa-automation.md), [feature workflow](feature-workflow.md), the guide page [Wild encounters](../guide/04-hunts/01-wild-encounter.md)

## Summary

A **Pickup** checkbox on the HGSS Wild Encounter and Chained Encounter actions. With it on, the bot fights as the train mode already does, and after every won battle it opens the Pokémon menu from the field and looks at the party list, as a player would (a held item shows as an icon on the Pokémon's card); it takes every held item into the bag, closes the menu, and goes back to hunting. Pickup only rolls for an empty-handed Pokémon (10 % each per won battle, never on a flee), so taking the items is what keeps the farm running; with five Aipom that is about one item every two battles while all five are empty.

The checkbox is offered only while a train mode is selected, and it is greyed out, with the reason, when no party member has the Pickup ability; Start refuses it with the same sentence. An indefinite farm is the existing target level set above 100, which the dashboard's field must allow.

The spike proved the three parts on a copy of the owner's Route 33 save: the roll's rule from the decomp, 40 won battles in the existing train mode with five pickups (and none once every Aipom held one), and the take loop driven from memory in 10.4 s for five Pokémon.

## Goals

- G1: Pickup is one checkbox on the actions that already fight; no new action, no new encounter type.
- G2: the checkbox is visible only with a train mode, greyed out with the reason when the party has no Pickup member, and Start refuses it with the same sentence.
- G3: the bot looks before it knows. After every won battle the Pokémon menu is opened and the party list is on screen before the party is read from memory, per the global decision that hunts act as a human would; items are taken when a Pickup member holds one, and every step of the menu visit is confirmed by state; no blind macro.
- G4: a trip that goes wrong (a wrong screen, a held item that is not taken, a stuck message) stops the hunt with a plain message instead of looping or pressing on.
- G5: the farm can run indefinitely: a target level of 101 or more never completes.

## Non-goals

- Levelling the Pickup Pokémon. The operator picks the levels (the tier picks the loot); switch training exists for that.
- Honey Gather, Pay Day, or any other end-of-battle item. The loop takes whatever a Pickup member holds; it does not identify how it got there.
- Giving items, or deciding what to do with the bag. Items go to the bag and stay there.
- A bag at 999 of one item. Not measured in the spike; the message, if any, is a stop (G4).
- RS. The party menu read here is HGSS's.
- Mail. A Pickup Pokémon never holds mail; the Mail entry that replaces Item is not handled (stop, G4).

## Requirements

| ID | Requirement | Priority | Acceptance |
|---|---|---|---|
| FR-1 | `NewEventLegArguments` and `NewEventArguments` gain `bool? Pickup`. `EncounterParameters` in `EventRegistry` adds the conditional parameter `Pickup` with the condition `TrainType` `Neq` `None`, so the dashboard shows it only with a train mode. `GameState` gains `Pickup` (bool), set per leg by `BotService` and `ChainedOrchestratorEvent` as `TrainType` is. | Must | Unit test: the schema carries the conditional; a leg with Pickup sets the state; `EventRegistryTests`' schema snapshot updated. |
| FR-2 | Validation in `BotService` (`StartWildEncounter`, `StartChainedEncounter`): a leg with `Pickup` true and `TrainType` `None` is a 400, "Pickup needs a train mode: the Pokémon must be knocked out, not fled from." A leg with `Pickup` true and no party member whose `Ability` is 53 (`ABILITY_PICKUP`, `include/constants/abilities.h`) is a 400, "No Pokémon in your party has the Pickup ability." Both from a fresh party read, like the headbutt guard (headbutt spec FR-4), since the greyed-out box can be stale. | Must | Unit tests on the leg validation; QA: `hgss-pickup-greyed` posts both through the API and sees the texts. |
| FR-3 | Availability: `GetBotActionsAsync` fills `DisabledChoices["Pickup"]["true"]` with the FR-2 ability sentence when no party member has ability 53, on the same cached party read and the same slot-entry cache as the headbutt refusal (`EmulatorSlotRegistry`, the headbutt spec's FR-3 rules: one read, cleared when an event starts or ends and on `getParty`). A read that fails gives the headbutt spec's "could not be read" sentence. | Must | Unit tests: a party with ability 53 in slot 4 enables it; none disables it with the sentence; one read for both refusals. |
| FR-4 | Dashboard: `Pickup` renders as a checkbox (`ActionParamFields.tsx`, beside `SoundOn` and `RequireFemale`; the label "Pickup"), disabled with the reason appended when `disabledChoices.Pickup.true` is set; a checked box that becomes disabled on refresh is cleared. `LEG_PARAM_KEYS` gains `Pickup` and `buildLegBody` sends it as a boolean. The Target Level field's `max` becomes 101, so 101 is the way to farm without end (G5); the label gets the hint "101 = no end". `types/api.ts`: `pickup?: boolean` on the leg, and the field on `disabledChoices`. | Must | Vitest on the checkbox helper and `buildLegBody`; QA: `hgss-pickup-greyed` sees the disabled box and its text; `hgss-pickup` sets 101. |
| FR-5 | `OrchestratorEvent`: on every `DecideOverworldAction` decision with `Pickup` on that goes on to `Encountering` (the first one before any battle included, and so after every won battle), run the `PickupTakeEvent` visit (FR-6) before `Encountering`. The decision whether anything is held is made inside the visit, with the party list on screen: the party is read from memory there and members with ability 53 and a non-zero `HeldItem` are the slots to take. Nothing is decided from the post-battle party read; no threshold. (Revised 2026-11-09: the first build counted from the read in `DecideOverworldAction` and visited only when all held, which the global decision forbids.) | Must | Unit test: a decision with Pickup on runs the visit; Pickup off never does. Inside the visit (FR-6 tests): held slots are those read at the list, not before. |
| FR-6 | `PickupTakeEvent`, HGSS only, from the field free: press X, wait for the start menu (`app == 0 && taskman != 0`, stale 5 s); touch POKéMON (44, 77), wait for the party menu (`app != 0 && app.state == 1`, stale 10 s); with the list on screen, read the party (`PokemonStateReader.GetPartyState`) and log the held count (FR-9); when no Pickup member holds an item, touch CANCEL and leave as below; otherwise for each held slot `i` (0-based): touch the slot ((63, 27), (191, 27), (63, 77), (191, 77), (63, 123), (191, 123)), wait for `app.state == 2 && partyMonIndex == i`; touch ITEM (191, 106), wait for `app.state == 15`; touch TAKE (191, 66), wait for `app.state == 17` and the text printer at the arrow (slot 0, `pr.state` 2 or 3); press A, wait for `app.state == 1`; then touch CANCEL (227, 178) and wait for the field free (`app == 0 && taskman == 0`, stale 10 s). Every touch is `S.x.y.3` after `W.5` (the menu input-ready gap). Each wait is `GameStateWait.UntilAsync` on a snapshot that carries the party menu's `app.state` and `partyMonIndex` (FR-8). | Must | Unit tests over scripted snapshots: the whole loop with two held slots read at the list; a visit with nothing held is X, POKéMON, CANCEL and no slot touch; QA: `hgss-pickup` visits after every battle and takes what is held. |
| FR-7 | After the trip, read the party again: any Pickup member still holding an item is a stop, "Pickup: {name} still holds {item} after the take; check the Pokémon menu." A stale-timeout in any step is a stop with the step's name and the last snapshot, like the headbutt stops; the event clears its macro first. An Item entry that is Mail (`ItemIdIsMail`) is not expected (non-goal) and surfaces as the FR-7 "still holds" stop. | Must | Unit tests for both stops. |
| FR-8 | State reader: an `OtherApp` snapshot gains `AppState` (`app + 0x14`) and `PartyMonIndex` (`[app + 0x1C] + 0xC65`, null when the data pointer is NULL), read in the existing `app` block (grow it to `AppDataOffset + 4`) and one new chain block `partyMenu` (`0, 4, 0x1C, 0xC65`, 1 byte). `HgssPointerService` gains `PartyMenuMonIndexOffset => 0xC65` and the state constants (`PartyMenuList = 1`, `PartyMenuContextMenu = 2`, `PartyMenuItemMenu = 15`, `PartyMenuTakeMessage = 17`) with the address table's comment format (symbols `PartyMenuState`, `PartyMenu.partyMonIndex`; status observed; scenario `hgss-pickup`). The HGSS state reader spec's read table gains the next free rows (R39, R40) and the block table the new block; `app.ovyId` is `0xFFFFFFFF` for this app and is not used to identify it. The battle and field classifications are unchanged. | Must | Unit test: a scripted `app` block with state 15 and index 3 classifies `OtherApp` with both fields; existing reader tests unchanged. |
| FR-9 | Logging: one `[Pickup]` line per visit, written while the party list is on screen, with the held count and the decision (take or nothing to take), one per take naming the slot and the item id, one for the visit's total ms, and the stop lines of FR-7. | Should | QA logs. |
| FR-10 | Persistence: nothing new. Encounters are recorded by the battle path as today; the item taken is in the log only. | Must | Reviewer check. |
| FR-11 | The guide page [Wild encounters](../guide/04-hunts/01-wild-encounter.md) documents Pickup in the same PR as the dashboard change: what the party needs (a lead that wins, Pokémon with the Pickup ability, the level tiers and what they give, in plain words), the checkbox and why it is greyed out, target level 101 for no end, what a trip looks like, and the stops. | Must | Reviewer check (AGENTS.md, the guide rule). |
| NFR-1 | A visit that takes five items under 15 s on the dev PC (spike: 10.4 s; first build: 11.0 to 11.4 s); a visit with nothing to take under 6 s (open about 1.5 s, close about 1.8 s). | Should | QA timings. |
| NFR-2 | Thirty consecutive won battles with Pickup on the spike save with no stall, a menu visit after every one, every visit ending field free, every held item taken. | Must | QA: `hgss-pickup`. |

## Design

### The cycle

```mermaid
stateDiagram-v2
  [*] --> DecideOverworldAction: BattleComplete, battle won (or the first decision)
  DecideOverworldAction --> PickupTake: Pickup on
  PickupTake --> StartMenu: X
  StartMenu --> PartyList: touch POKéMON, app.state 1
  PartyList --> PartyList: the list is on screen, read the party, log the held count
  PartyList --> Context: a held slot, touch it, app.state 2, partyMonIndex = slot
  Context --> ItemMenu: touch ITEM, app.state 15
  ItemMenu --> Message: touch TAKE, app.state 17, printer at the arrow
  Message --> PartyList: A, app.state 1 (next held slot, or CANCEL)
  PartyList --> FieldFree: nothing held, or every held slot done, touch CANCEL, app == 0, taskman == 0
  FieldFree --> Encountering: party re-read, nothing held
  FieldFree --> [*]: something still held: stop
```

### Where it lives

- `BotEvents/PickupTakeEvent.cs`: the visit (FR-6, FR-7), an `AbstractBotEvent` awaited by `OrchestratorEvent` and done when the field is free again. It reads the party at the list; `PokemonData.Ability` and `HeldItem` are already parsed by `HgssPokemonParser`.
- `OrchestratorEvent.DecideOverworldAction`: runs the visit on every decision with Pickup on that goes on to `Encountering` (FR-5).
- `BotService`: the two refusals (FR-2) and the greyed-out reason (FR-3), beside the headbutt ones.
- `HgssFieldSystemStateReader` and `HgssPointerService`: the party menu's two values (FR-8).
- Dashboard: the checkbox, the level cap, the leg body (FR-4).
- `Configs/Events/Pickup.json`: the touch targets (FR-6) and the stale-timeouts, so a measured value changes without a build, like the other event configs. (`PickupTakeAt` was removed on 2026-11-09 with the threshold.)

### Why these choices

- **One checkbox, not a mode.** The fight path is the train mode; Pickup only adds the trip. Tying it to `TrainType` in the schema keeps the UI honest without a new dropdown value.
- **Look after every battle, take what is there.** A player cannot know whether an Aipom picked something up without opening the Pokémon menu, and the global decision says the bot does what a human would do, visibly. So the menu is opened after every won battle, the party list (with its held-item icons) is on screen, and only then is the party read from memory and acted on. A look costs about 3.3 s against 30 s per battle; taking every held item at once keeps every Aipom rolling. The first build counted from the post-battle read and visited only when all held, which was faster and wrong by that rule.
- **Memory-gated touches.** The spike's first loop pressed A before the "Received" message reached its arrow; the press was dropped and the next touches landed on Switch. Every step waits on `app.state`, `partyMonIndex` or the printer, as the field presses do.
- **Target level 101.** `BotService` already accepts it and `DecideOverworldAction` never completes it. Raising the dashboard cap by one is the smallest change that gives "no end" without a new parameter; the guide says what it means.

## QA scenarios

| Scenario | Save | Steps | Pass when |
|---|---|---|---|
| `hgss-pickup` | `hgss.qa-pickup` (Typhlosion Lv 37 leading, five Aipom with Pickup, empty-handed) | Launch + Continue; Wild Encounter, Route 33, ShinyTarget, Run, xRange 3, train Standard, target level 101, Pickup on; run for 30 won battles | every won battle is followed by a menu visit (the `[Pickup]` count line written at the list, and the recording shows the party list after every battle); every visit ends field free with every held item taken (the party in RAM read through `egg-pid.ts`'s `decodeMon` at each visit's end; `getParty` carries no held item or ability); no stop; save equals `.bak`; a visit with takes under 15 s, one without under 6 s |
| `hgss-pickup-take` | `hgss.qa-pickup-held` (the same, five items in hand) | Launch + Continue; the same action; the first visit, before the first encounter, must take all five | five takes logged in order, field free, party empty, under 15 s |
| `hgss-pickup-greyed` | `hgss.qa-route33` (no Pickup member) | Dashboard: Wild Encounter with train Standard; API: Start with Pickup and with `TrainType` `None` | the checkbox is disabled with the ability sentence; the two API Starts are 400 with the FR-2 texts |

## What diverged

Where the build differs from this spec as approved (2026-11-07). The requirements and design above stay as written; this list keeps the record.

- FR-5, FR-9 (superseded by the 2026-11-09 revision above, which moved the decision onto the screen): the first build had no flee skip and no won or fled flag; `OrchestratorEvent.PickupSlotsDue` counted from the post-battle read on every decision that goes on to `Encountering` and visited only when all held (#283). The revision keeps "every decision that goes on to `Encountering`" as the moment of the visit.
- Where it lives, FR-5: `PickupTakeEvent` is not pushed and popped. `DecideOverworldAction` awaits it inline with the event's cancellation token, and there is no new `EventState`. A stop clears the macro and parks the orchestrator in `Waiting`, with the sentence logged as a `[Pickup]` line. Resume returns to the decision, which reads the party again (#283).
- FR-6: the trip first waits for the field free (`fieldFreeStaleMs`), because `BattleComplete` can come while a field task runs, and that task would confirm the X. The spec gives no stale-timeout for the per-slot waits; they use `stepStaleMs`, 5 s. `Pickup.json` holds `startMenuStaleMs` 5000, `partyMenuStaleMs` 10000, `stepStaleMs` 5000 and `fieldFreeStaleMs` 10000 (#283).
- FR-7: `{item}` reads `item {id}`, the item number, because there is no item-name table: "Pickup: {name} still holds item {id} after the take; check the Pokémon menu." A stale-timeout stop is `Pickup: Waited for {step}; the game state stayed {snapshot}` (`GameStateTimeoutException`) (#283).
- FR-9: the lines are `[Pickup] held H of M pickup members; take|nothing to take`, written at the party list on every visit, `[Pickup] took item I from slot N (name)`, `[Pickup] trip took T ms`, the visit's total, and the stop lines (#288). A slot holding mail is not visited. It logs `[Pickup] slot N (name) holds mail, item I; not visited` and then gets the FR-7 still-holds stop (#283).
- FR-8: `OtherApp` also carries `TextPrinter` (`pr.state` of slot 0), which is what FR-6's take-message gate reads. The `app` block was already 0x20 bytes, which is `AppDataOffset + 4`, so it was not grown (#280).
- FR-4: the checkbox's helper `pickupChoice` is in `components/admin/actionSchema.ts`, and `ActionParamFields.tsx` calls it. `buildLegBody` sends `pickup` only while Train Type is not None, so a box that was ticked and then hidden is not sent (#282).
- QA: the scenarios read party RAM through `pickup.ts` and `egg-pid.ts`'s `decodeMon`, not through `getParty`, because `getParty` carries no held item or ability. The QA table above now says so (#284).

## Open questions

- Whether a fainted Pickup member still rolls (the decomp checks species, egg and item only). Not needed for the mode; worth a line in the research doc when seen.
- The threshold's unit: a count of held members (as written) or a share of the Pickup members. Withdrawn 2026-11-09: there is no threshold; the bot looks after every won battle and takes what it sees.

## Changelog

- 2026-11-07: first draft, from the spike.
- 2026-11-08: Implemented (PRs #280 to #284, QA tooling #285); What diverged added; the QA table's party check reads RAM, not getParty.
- 2026-11-09: owner's review of the QA video: deciding from the post-battle party read breaks the rule that hunts act as a human would. FR-5, FR-6, FR-9, NFR-1, NFR-2, G3, the cycle, Where it lives and Why these choices revised: the menu is opened after every won battle, the party is read with the list on screen, every held item is taken; `PickupTakeAt` removed. Tasks P7 (bot, guide) and P8 (QA, docs).
