import { expect, test } from '@playwright/test'
import { step } from './driver'
import {
  boxes, CLEARING, copyLeadToSlot2, expAt, failOnStops, PARTY_AFTER, switchLines, trainingRun, waitLine, writeSlot
} from './rs-training'
import { dashboardState, OVER, START } from './rs-wild'
import { backendLog, MINUTE } from './static'

// rs-train-switch-target (#433, Q1c; spec QA table, FR-17 and FR-18). The battler as in rs-train-switch: the lead copied
// to slot 2 at Lv 20; the benched Lv 7 trainee one short of Lv 8, the target. After the battle the orchestrator walks
// home (Spin never left the start tile, so `already at start`) and the event is Complete.
// T3b (#451): Train Type Switch, its SwitchSlot option 2 and No Target are chosen by rs-training.ts's startTraining.
// Complete with No Target is approval answer 9.
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

test('rs-train-switch-target: Switch training ends at the target level, Lv 8, with Complete after the walk home', async () => {
  test.setTimeout(25 * MINUTE)
  const { log, idle } = await trainingRun(async () => {
    await copyLeadToSlot2()
    await writeSlot(2, { level: 20, exp: expAt(20) })
    await writeSlot(1, { exp: expAt(8) - 1 })
  }, { trainType: 'Switch', targetLevel: 8, switchSlot: 2 }, async () => {
    // "Training ends at the target level with Complete": within 3 minutes of the battle's end.
    await waitLine(OVER, 1, 10 * MINUTE, 'the battle that reaches the target level')
    const deadline = Date.now() + 3 * MINUTE
    while ((await dashboardState())?.eventState !== 'Complete') {
      failOnStops(backendLog())
      if (Date.now() > deadline) throw new Error('"training ends at the target level with Complete": not Complete within 3 minutes of the battle')
      await sleep(1000)
    }
    step('Complete within 3 minutes of the battle')
    return 'complete'
  })

  const firstOver = log.findIndex((l) => OVER.test(l))
  const b1 = log.slice(0, firstOver + 1)
  expect(switchLines(b1), 'the switch lines').toEqual(['try 1 to slot 1', 'confirmed: reads 1, try 1'])
  expect(boxes(b1), 'one level-up box for slot 0').toEqual(['page 1 for slot 0, confirmed by page 2', 'page 2 for slot 0, confirmed by the box leaving page 2'])
  const afterAt = log.findIndex((l, i) => i > firstOver && PARTY_AFTER.exec(l)?.[1] === '0')
  expect(PARTY_AFTER.exec(log[afterAt] ?? '')?.[4], '"training ends at the target level": party after the battle: slot 0 ... Lv 8').toBe('8')
  // "After the walk the orchestrator makes": its line after the battle.
  const clearing = log.findIndex((l) => CLEARING.test(l))
  expect(clearing > afterAt ? CLEARING.exec(log[clearing])![1] : `no line after the battle (at ${clearing})`, '"after the walk the orchestrator makes"')
    .toBe('already at start')
  expect(log.filter((l) => OVER.test(l)).length, 'one battle').toBe(1)
  expect([idle.tile, idle.party[0].level, idle.lock, idle.battleBit], 'with the bot idle: the start tile, the trainee at Lv 8, the field free')
    .toEqual([START, 8, 0, 0])
})
