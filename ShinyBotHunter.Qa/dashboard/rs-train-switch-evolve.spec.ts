import { expect, test } from '@playwright/test'
import {
  ASKING, copyLeadToSlot2, expAt, MARSHTOMP, movesOf, oneShort, PARTY_AFTER, PRESS, SCENE, SWITCH_CONFIRMED, SWITCH_TRY, trainingRun, writeSlot
} from './rs-training'
import { OVER } from './rs-wild'
import { MINUTE } from './static'

// rs-train-switch-evolve (#433, Q1c; spec QA table, FR-16 and FR-17). The battler as in rs-train-switch: the lead copied
// to slot 2 at Lv 20, which does not level up from a Lv 2 or 3 wild, so it does not evolve. The benched trainee one short
// of Lv 16 with the save's three moves: it evolves after the switch battle and learns Mud Shot into the free slot. Target
// 16, so the event ends in Complete.
// T3b (#451): Train Type Switch, its SwitchSlot option 2 and No Target are chosen by rs-training.ts's startTraining;
// check that the form's wording matches once T3b merges. Complete with No Target is approval answer 9.

// The battle's switch: its try and the action menu that confirmed it.
const switchLines = (b: string[]): string[] => b.flatMap((l) => {
  const t = SWITCH_TRY.exec(l)
  const c = SWITCH_CONFIRMED.exec(l)
  return t ? [`try ${t[1]} to slot ${t[2]}`] : c ? [`confirmed: reads ${c[1]}, try ${c[2]}`] : []
})
// The level-up box presses: page, slot and what confirmed each.
const boxes = (b: string[]): string[] => b.flatMap((l) => {
  const m = PRESS.exec(l)
  const box = m && /^level-up box page (\d) for slot (\d): BattleConfirm$/.exec(m[1])
  return box ? [`page ${box[1]} for slot ${box[2]}, confirmed by ${m![3]}`] : []
})

test('rs-train-switch-evolve: the benched trainee evolves into Marshtomp after a Switch battle, with no B during the scene', async () => {
  test.setTimeout(25 * MINUTE)
  const { log, idle } = await trainingRun(async () => {
    await copyLeadToSlot2()
    await writeSlot(2, { level: 20, exp: expAt(20) })
    await writeSlot(1, { ...oneShort(16), ...movesOf(33, 45, 189) })
  }, { trainType: 'Switch', targetLevel: 16, switchSlot: 2 }, async () => 'complete')

  const firstOver = log.findIndex((l) => OVER.test(l))
  const b1 = log.slice(0, firstOver + 1)
  expect(switchLines(b1), 'the switch lines').toEqual(['try 1 to slot 1', 'confirmed: reads 1, try 1'])
  expect(boxes(b1), 'one level-up box for slot 0').toEqual(['page 1 for slot 0, confirmed by page 2', 'page 2 for slot 0, confirmed by the box leaving page 2'])
  // The scene's text waits, all for the benched trainee (data[12] 0), none at its first text (data[0] 2), which does not wait.
  const scene = b1.map((l) => SCENE.exec(l)).filter((m) => m !== null)
  expect(scene.length, '"the trainee evolves after the battle": scene lines').toBeGreaterThan(0)
  expect(scene.filter((m) => m[1] !== '0' || m[2] === '2').map((m) => m[0]), 'scene lines not of slot 0, or at data[0] 2').toEqual([])
  // "With no B during the scene": no B line in the battle, and checkPresses (in the frame) holds every press to its line.
  expect(log.filter((l) => ASKING.test(l)), 'no move prompt: Mud Shot goes into the free slot').toEqual([])
  expect(b1.filter((l) => /^B /.test(PRESS.exec(l)?.[1] ?? '')), '"no B during the scene": B lines').toEqual([])
  // Marshtomp is 259 in the log (national), 284 in the record (internal): QA plan L9.
  const after = log.slice(firstOver).map((l) => PARTY_AFTER.exec(l)).find((m) => m?.[1] === '0')
  expect(after?.slice(2, 6), 'party after the battle: slot 0').toEqual(['Marshtomp', '259', '16', '33,45,189,341'])
  // "The party reads the new species in slot 1", with the bot idle.
  expect([idle.party[0].species, idle.party[0].level, idle.party[0].moves], 'slot 1 with the bot idle').toEqual([MARSHTOMP, 16, [33, 45, 189, 341]])
})
