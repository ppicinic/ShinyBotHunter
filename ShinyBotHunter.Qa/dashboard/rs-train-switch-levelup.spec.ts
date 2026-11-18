import { expect, test } from '@playwright/test'
import {
  boxes, copyLeadToSlot2, expAt, oneShort, PARTY_AFTER, SWITCH_CONFIRMED, switchLines, trainingRun, variant, waitLine, writeSlot
} from './rs-training'
import { OVER } from './rs-wild'
import { MINUTE } from './static'

// rs-train-switch-levelup (#433, Q1c; spec QA table, FR-14 and FR-17), QA_RS_VARIANT `one` (the default), and
// rs-train-switch-double-levelup as `two`. The battler as in rs-train-switch: the lead copied to slot 2 at Lv 20; the
// benched trainee one short of Lv 8 (`one`, target 101: a cancel once battle 2 has switched), or at Lv 11 one short of
// Lv 13, which teaches Mudkip no move at 12 or 13 (`two`, target 13: Complete).
// T3b (#451): Train Type Switch, its SwitchSlot option 2 and No Target are chosen by rs-training.ts's startTraining.
// `two`'s Complete with No Target is approval answer 9.
const VARIANT = variant(['one', 'two'] as const)
const LEVELS = { one: { from: 7, to: 8, target: 101 }, two: { from: 11, to: 13, target: 13 } }[VARIANT]

const PAIR = ['page 1 for slot 0, confirmed by page 2', 'page 2 for slot 0, confirmed by the box leaving page 2']

test(`rs-train-switch-${VARIANT === 'one' ? 'levelup' : 'double-levelup'}: the benched trainee from Lv ${LEVELS.from} to ${LEVELS.to} in one Switch battle`, async () => {
  test.setTimeout(25 * MINUTE)
  const { log, idle } = await trainingRun(async () => {
    await copyLeadToSlot2()
    await writeSlot(2, { level: 20, exp: expAt(20) })
    await writeSlot(1, oneShort(LEVELS.to, LEVELS.from))
  }, { trainType: 'Switch', targetLevel: LEVELS.target, switchSlot: 2 }, async () => {
    if (VARIANT === 'two') return 'complete'
    // "Training goes on": battle 2 switches too. The cancel waits for its switch to be confirmed, so the party records
    // are back in order (they are swapped for about 22 frames after SHIFT) when they are read with the bot idle.
    await waitLine(SWITCH_CONFIRMED, 2, 10 * MINUTE, '"training goes on": battle 2\'s switch confirmed')
    return 'cancel'
  })

  const firstOver = log.findIndex((l) => OVER.test(l))
  const b1 = log.slice(0, firstOver + 1)
  expect(switchLines(b1), 'battle 1: the switch lines').toEqual(['try 1 to slot 1', 'confirmed: reads 1, try 1'])
  // "The level-up box for the benched trainee cleared by its two confirmed presses", one pair a level.
  expect(boxes(b1), `battle 1: "the level-up box for the benched trainee", one pair for each of ${LEVELS.to - LEVELS.from} levels`)
    .toEqual(VARIANT === 'one' ? PAIR : [...PAIR, ...PAIR])
  // "The trainee's level is one higher" (`two`: two higher), in the bot's read after the battle and with the bot idle.
  const after = log.slice(firstOver).map((l) => PARTY_AFTER.exec(l)).find((m) => m?.[1] === '0')
  expect(after?.[4], `battle 1: "party after the battle: slot 0 ... Lv ${LEVELS.to}"`).toBe(String(LEVELS.to))
  expect(idle.party[0].level, `the trainee's level with the bot idle`).toBe(LEVELS.to)
  if (VARIANT === 'one') {
    expect(switchLines(log.slice(firstOver + 1)), '"training goes on": battle 2\'s switch').toEqual(['try 1 to slot 1', 'confirmed: reads 1, try 1'])
  }
})
