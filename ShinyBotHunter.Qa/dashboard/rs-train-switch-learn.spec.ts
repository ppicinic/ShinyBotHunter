import { expect, test } from '@playwright/test'
import {
  answerPresses, answerPrompt, copyLeadToSlot2, events, expAt, movesOf, oneShort, PARTY_AFTER, pressOf, trainingRun, variant, writeSlot
} from './rs-training'
import { OVER } from './rs-wild'
import { MINUTE } from './static'

// rs-train-switch-learn (docs/specs/rs-navigation-and-training.md, QA table; FR-15, FR-17, FR-19; #433's plan, part Q1d):
// Switch training with a Lv 20 copy of the lead in slot 2, and the trainee one short of Lv 15 with four moves, so the
// benched trainee's Bide prompt comes in a switch battle. QA_RS_VARIANT: `replace` (the default: Yes over slot 1, Growl, one
// Down) or `refuse` (No, then Stop learning). The Train Switch and No Target choices are enabled by T3b (#451).
const VARIANT = variant(['replace', 'refuse'])
const FORGET = VARIANT === 'replace' ? 1 : null
const BIDE = 117
const MOVES = VARIANT === 'replace' ? [33, BIDE, 189, 55] : [33, 45, 189, 55]

// The switch of FR-17, in log order: the cursor to POKéMON, A, the list taking keys, Down to slot 1, A, A on SHIFT, then
// 0x02024A6A reading the battler's slot at the next action menu. The é is any one or two characters, as in rs-training.ts.
const SWITCH_LINES = [
  /\[RsBattle\] Down on the action menu 0 -> 2, \d+ ms: confirmed by cursor 2 \(/,
  /\[RsBattle\] BattleConfirm on POK.{1,2}MON \(switch try 1 of 3\), \d+ ms: confirmed by the screen leaving the action menu \(/,
  /\[RsBattle\] switch try 1 of 3 to slot 1: the party list takes keys/,
  /\[RsBattle\] Down on the party list from 0, \d+ ms: confirmed by the party cursor leaving 0 \(/,
  /\[RsBattle\] BattleConfirm on party slot 1, \d+ ms: confirmed by the SHIFT submenu \(/,
  /\[RsBattle\] BattleConfirm on SHIFT at GameSnapshot \{.*\}, \d+ ms: confirmed by the submenu closing \(/,
  /\[RsBattle\] switch confirmed: 0x02024A6A reads 1 at the action menu, try 1/
]

test(`rs-train-switch-learn ${VARIANT}: the benched trainee's Bide prompt in a switch battle, ${VARIANT === 'replace' ? 'Growl replaced' : 'refused'}`, async () => {
  test.setTimeout(30 * MINUTE)
  const { log, idle } = await trainingRun(async () => {
    // The battler first, from the save's lead; then the trainee.
    await copyLeadToSlot2()
    await writeSlot(2, { level: 20, exp: expAt(20) })
    await writeSlot(1, { ...oneShort(15), ...movesOf(33, 45, 189, 55) })
  }, { trainType: 'Switch', targetLevel: 15, switchSlot: 2, discord: FORGET !== null }, async (run) => {
    await answerPrompt(run, { n: 1, learner: 0, move: BIDE, forget: FORGET })
    return 'complete'
  })

  // "a box pair for slot 0", then "asking Discord: slot 0 … Bide" (answerPrompt checked the slot and the move).
  expect(events(log), '"the level-up box for the benched trainee, then its move prompt"').toEqual(['box 0 1', 'box 0 2', 'ask 0'])
  expect(log.filter((l) => /level-up box page 1/.test(pressOf(l) ?? '')).map(pressOf), '"box page 1 confirmed by page 2"')
    .toEqual(['level-up box page 1 for slot 0: BattleConfirm | page 2'])

  // "the switch lines": each in order, before the box.
  const firstBox = log.findIndex((l) => /level-up box page/.test(pressOf(l) ?? ''))
  let at = 0
  for (const line of SWITCH_LINES) {
    const found = log.findIndex((l, i) => i >= at && line.test(l))
    expect(found, `"the switch lines": no ${line} after line ${at}`).toBeGreaterThanOrEqual(0)
    expect(found, `"the switch lines": ${line} is after the first level-up box`).toBeLessThan(firstBox)
    at = found + 1
  }

  // The answer's presses, from the asking line to the battle's end: Yes, one Down and A on slot 1; or B and Yes at Stop learning.
  const { presses, closed } = answerPresses(log, 1)
  expect(presses, `"the move prompt answered by the rule": ${VARIANT}`).toEqual(FORGET === null
    ? ['B at Delete a move? | the prompt leaving Delete a move?', 'BattleConfirm (Yes) at Stop learning? | the prompt leaving Stop learning?']
    : ['BattleConfirm (Yes) at Delete a move? | the prompt leaving Delete a move?', 'Down on the forget-a-move screen 0 -> 1 | the cursor',
      'BattleConfirm on forget slot 1 | the summary task closing'])
  expect(closed, '"the forget-a-move screen closed" lines').toBe(FORGET === null ? 0 : 1)

  // "the trainee's moves as decided": the last battle's party line, then the read with the bot idle.
  const lastOver = log.flatMap((l, i) => (OVER.test(l) ? [i] : [])).at(-1)!
  const after = log.slice(lastOver).map((l) => PARTY_AFTER.exec(l)).filter((m) => m?.[1] === '0')
  expect(after.map((m) => [m![4], m![5]]), '"party after the battle: slot 0"').toEqual([['15', MOVES.join(',')]])
  expect([idle.party[0].level, idle.party[0].moves], '"the trainee\'s moves as decided", read with the bot idle').toEqual([15, MOVES])
  expect([idle.lock, idle.battleBit], '"the field free", read with the bot idle').toEqual([0, 0])
})
