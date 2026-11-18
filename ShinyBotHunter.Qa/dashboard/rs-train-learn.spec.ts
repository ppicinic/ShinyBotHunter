// rs-train-learn-slot1 to -slot4 and rs-train-learn-refuse (docs/specs/rs-navigation-and-training.md, QA; FR-14, FR-15,
// FR-19): the lead at Lv 14 one point short of 15 with four moves (Tackle, Growl, Mud-Slap, Water Gun), Standard training
// to 15, so Bide is offered in battle. QA_RS_VARIANT picks the answer: slot1 to slot4 learn it over that slot (slot3, the
// spike's, by default), refuse does not learn it. The operator answers in Discord (rs-training.ts's answerPrompt); with no
// Discord login only refuse runs, and the bot declines at once. QA_RS_ROM=RU-Wild runs it on Ruby (slot1 and refuse are
// the plan's recommended Ruby runs).
//
// Depends on T3b (#451): Train Type Standard and No Target in the form (rs-training.ts's startTraining), and
// HasOverworldNavigation for RS, without which Complete comes with no `already at start` line.
import { expect, test } from '@playwright/test'
import { snapshotField, OVER } from './rs-wild'
import { MINUTE } from './static'
import {
  answerPrompt, ASKING, CLEARING, FORGET_CLOSED, MOVE_PROMPT, movesOf, oneShort, PARTY_AFTER, PRESS, trainingRun, variant, writeSlot
} from './rs-training'

const VARIANT = variant(['slot3', 'slot1', 'slot2', 'slot4', 'refuse'] as const)
// The forgotten move's slot as the log counts it, from 0; null for refuse.
const FORGET = VARIANT === 'refuse' ? null : Number(VARIANT.slice(4)) - 1
const BIDE = 117
const OLD = [33, 45, 189, 55]
const NEW = FORGET === null ? OLD : OLD.map((id, i) => (i === FORGET ? BIDE : id))

test(`RS training rs-train-learn-${VARIANT}: Bide offered in battle with four moves, ${FORGET === null ? 'refused' : `learned over slot ${FORGET + 1}`}`, async () => {
  test.setTimeout(30 * MINUTE)
  const { idle, log } = await trainingRun(
    async () => {
      await writeSlot(1, { ...oneShort(15), ...movesOf(...OLD) })
    },
    { trainType: 'Standard', targetLevel: 15, discord: FORGET !== null },
    async (run) => {
      await answerPrompt(run, { n: 1, learner: 0, move: BIDE, forget: FORGET })
      return 'complete'
    }
  )

  const at = (pattern: RegExp): number[] => log.flatMap((l, i) => (pattern.test(l) ? [i] : []))
  const overs = at(OVER)
  expect(overs.length, 'one battle: battle over lines').toBe(1)
  const presses = log.map((l, i) => ({ i, m: PRESS.exec(l) })).filter((p) => p.m !== null).map((p) => ({ i: p.i, what: p.m![1], by: p.m![3] }))
  const boxes = presses.filter((p) => p.what.startsWith('level-up box'))
  expect(boxes.map((p) => `${p.what} / ${p.by}`), 'one level-up box, two confirmed presses').toEqual([
    'level-up box page 1 for slot 0: BattleConfirm / page 2',
    'level-up box page 2 for slot 0: BattleConfirm / the box leaving page 2'
  ])

  // "the prompt seen waiting (0x0201601A reads 1)": the bot's snapshot at the prompt (the plan's L2, logged by T2), the
  // battle's "Delete a move?" with the tracker 1, for slot 0. answerPrompt checked the asking line (slot 0, Bide) and the
  // answer against the click, and read the edited Discord message back.
  const prompts = at(MOVE_PROMPT)
  expect(prompts.length, '"the prompt seen waiting": move prompt lines').toBe(1)
  const snapshot = MOVE_PROMPT.exec(log[prompts[0]])![1]
  expect(['Screen', 'Prompt', 'Question', 'PartyMonIndex'].map((f) => snapshotField(snapshot, f)), '"the prompt seen waiting": its snapshot')
    .toEqual(['BattleMoveLearnPrompt', '1', '5', '0'])
  expect(at(ASKING).length, 'one asking line').toBe(1)

  // The answer's presses, in order, each confirmed by what it changed: Yes, Down to the slot (none for slot 1), A on it; or
  // B, then Yes at "Stop learning?".
  const answer = presses.filter((p) => /Delete a move\?|Stop learning\?|forget/.test(p.what))
  const expected = FORGET === null
    ? ['B at Delete a move? / the prompt leaving Delete a move?', 'BattleConfirm (Yes) at Stop learning? / the prompt leaving Stop learning?']
    : [
        'BattleConfirm (Yes) at Delete a move? / the prompt leaving Delete a move?',
        ...Array.from({ length: FORGET }, (_, k) => `Down on the forget-a-move screen ${k} -> ${k + 1} / the cursor`),
        `BattleConfirm on forget slot ${FORGET} / the summary task closing`
      ]
  expect(answer.map((p) => `${p.what} / ${p.by}`), `"answered ${FORGET === null ? 'No' : 'Yes'}": the presses`).toEqual(expected)
  const closed = at(FORGET_CLOSED)
  expect(closed.length, FORGET === null ? 'no forget-a-move screen' : '"the forget-a-move screen closed"').toBe(FORGET === null ? 0 : 1)
  // In the order the game asks: the box, the prompt, the answer's presses, the screen closing, the battle over.
  const order = [boxes[1].i, prompts[0], answer[0].i, answer.at(-1)!.i, ...closed, overs[0]]
  expect(order.every((v, k) => k === 0 || v > order[k - 1]), `the box, the prompt, the answer and the battle over in order: lines ${order}`).toBe(true)

  // "the party's moves afterwards": in the log, then in memory with the bot idle.
  const party = log.map((l) => PARTY_AFTER.exec(l)).filter((m) => m !== null)
  expect(party.map((m) => [m![1], m![4], m![5]]), '"party after the battle": slot 0 at Lv 15 with the moves as decided').toEqual([['0', '15', NEW.join(',')]])
  expect(idle.party[0].moves, 'the moves as decided, with the bot idle').toEqual(NEW)
  expect(idle.party[0].level, 'Lv 15 with the bot idle').toBe(15)
  expect([idle.lock, idle.battleBit], '"the field free": the lock and the battle bit with the bot idle').toEqual([0, 0])

  // Complete: the walk home is `already at start`, after the battle.
  const clearing = at(CLEARING)
  expect(clearing.map((i) => CLEARING.exec(log[i])![1]), 'Complete: the walk home').toEqual(['already at start'])
  expect(clearing[0], 'Complete: the walk home after the battle').toBeGreaterThan(overs[0])
})
