import { expect, test } from '@playwright/test'
import { step } from './driver'
import {
  AFTER_SHIFT, ARRIVED, CLEARING, copyLeadToSlot2, expAt, failOnStops, FLEEING, HEAL_TRIP, indexOf, NAV_BATTLE, NURSE_DONE, NURSE_HP, NURSE_PRESS, PLAN, pressCount,
  readSlot, rsRom, STATE, SWITCH_CONFIRMED, SWITCH_TRY, trainingRun, variant, waitLine, writeSlot
} from './rs-training'
import { ADVANCE, CONTROLLER, dashboardState, ENCOUNTER, MENU_FN, OVER, readBattleBit, readU32, snapshotField } from './rs-wild'
import { backendLog, hex8, MINUTE } from './static'

// rs-train-switch-battler-spent (docs/specs/rs-navigation-and-training.md, QA table; FR-17, FR-19; #433's plan, part Q1d):
// Switch training with a Lv 20 copy of the lead in slot 2 as the battler. QA_RS_VARIANT:
// - `hp` (the default): the battler's HP written to 1, so the orchestrator's rule takes the Pokémon Center trip before the
//   first encounter; the nurse heals both, the walk back, then Switch training resumes. Cancelled in that battle.
// - `pp`: the battler's four PP written to 0; the same trip, and its PP above 0 after.
// - `fainted`: the battler fainting in battle. OPEN WITH THE OWNER (#433, Question 2): the spec's row says "the battle is
//   fled and the bot heals"; the approval's default, written here, is that the bot ends in its stale stop at "Use next
//   POKéMON?" with nothing pressed after the faint text (research S11: the battle window is 0 there, so no text rule
//   presses). A different decision changes FAINTED_END, and the asserts after it.
// The Train Switch and No Target choices are enabled by T3b (#451).
const VARIANT = variant(['hp', 'pp', 'fainted'])

// The fainted variant's expected end, the one place the owner's answer goes: RsBattleEvent's stale stop (30 s) on the
// main loop's wait, naming the snapshot that stayed, at the "Use next POKéMON?" yes/no that no rule answers.
const FAINTED_END = /\[RsBattle\] stopped: Waited for a battle message, the action menu, an evolution text wait or yes\/no, or the free field; the game state stayed (GameSnapshot \{.*\})\s*$/
// gBattlescriptCurrInstr (research, "Addresses from pret's symbol tables") at "Use next POKéMON?": 0x081D8C56, 14 bytes
// before BattleScript_FaintedMonTryChooseAnother, observed on Sapphire 1.2 only (S11), so the variant runs on SA-Wild.
const SCRIPT = 0x02024c10
const USE_NEXT = 0x081d8c56

const CENTER = 'OldaleTown_PokemonCenter_1F (7, 4)'
const START = 'Route101 (14, 4)'
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

/** hp and pp: the trip, the nurse, the walk back and the next battle's switch, in that order, from backend.log. */
function checkHealTrip(log: string[]): void {
  // "the orchestrator's rule: the Pokémon Center trip" before any encounter. HEAL_TRIP is the QA plan's L7, which T3b
  // (#451) adds.
  const firstEncounter = indexOf(log, ENCOUNTER)
  const before = (i: number): boolean => i >= 0 && (firstEncounter === -1 || i < firstEncounter)
  const toCenter = log.findIndex((l) => STATE.exec(l)?.[1] === 'GoToPokemonCenter')
  expect(before(toCenter), '"the Pokémon Center trip": State GoToPokemonCenter before any encounter').toBe(true)
  const why = indexOf(log, HEAL_TRIP)
  expect(before(why), '"the orchestrator\'s rule": the heal trip line (L7, T3b) before any encounter').toBe(true)
  const [, , hp, , pp] = HEAL_TRIP.exec(log[why])!
  if (VARIANT === 'hp') expect(Number(hp), '"the battler\'s HP written to 1": the heal trip line').toBe(1)
  else expect(Number(pp), '"its PP to 0": the heal trip line').toBe(0)

  // The walk: the first plan to the Oldale Center's counter from the start tile, then arrived on it.
  const plan = indexOf(log, PLAN, toCenter)
  const p = PLAN.exec(log[plan])!
  expect([`${p[1]} (${p[2]}, ${p[3]})`, `${p[4]} (${p[5]}, ${p[6]})`], 'the plan to the Pokémon Center').toEqual([CENTER, START])
  const arrived = log.findIndex((l, i) => i > plan && ARRIVED.test(l) && l.includes(`arrived on ${CENTER}`))
  expect(arrived, `"the Pokémon Center trip": arrived on ${CENTER}`).toBeGreaterThan(plan)

  // The nurse: six presses, each confirmed, their frames to steps.log; then both members at full HP (#433: the first run
  // of the nurse with two Pokémon).
  const nurse = log.slice(arrived).filter((l) => NURSE_PRESS.test(l))
  nurse.forEach((l, i) => {
    const [, press, , , frames] = NURSE_PRESS.exec(l)!
    step(`nurse press ${i + 1}: ${press}, ${frames} frames to its confirming read`)
  })
  expect(nurse.length, '"the nurse\'s six press lines"').toBe(6)
  const done = indexOf(log, NURSE_DONE, arrived)
  expect(NURSE_DONE.exec(log[done])![1], 'the nurse\'s press total').toBe('6')
  const healed = indexOf(log, NURSE_HP, done)
  const members = NURSE_HP.exec(log[healed])![1].split(', ').map((m) => /^(.+) (\d+)\/(\d+)$/.exec(m)!)
  expect(members.map((m) => m[2] === m[3]), `"the nurse heals both": ${log[healed].trim()}`).toEqual([true, true])

  // The walk back: arrived on the start tile, then the orchestrator's arrived back at start. A wild battle on the step
  // onto the start tile (grass) instead re-plans with 0 segments: no arrived line, and `already at start`.
  const home = log.findIndex((l, i) => i > healed && ARRIVED.test(l) && l.includes(`arrived on ${START}`))
  const onStart = log.findIndex((l, i) => i > healed && NAV_BATTLE.test(l) && l.includes(`battle bit on ${START}`))
  const reached = home > healed ? home : onStart
  expect(reached, `"the walk back": neither arrived on ${START} nor a battle bit on it after the nurse`).toBeGreaterThan(healed)
  const clearing = indexOf(log, CLEARING, reached)
  const cleared = CLEARING.exec(log[clearing] ?? '')?.[1]
  step(`the walk back: ${home > healed ? `arrived on ${START}` : `a battle on the step onto ${START}, then 0 segments`}; ${cleared}`)
  expect(cleared, '"the walk back": Clearing navigation path').toBe(home > healed ? 'arrived back at start' : 'already at start')

  // "then Switch training resumes": the next battle's first switch try, to the battler's slot.
  const resumed = indexOf(log, SWITCH_TRY, clearing)
  expect(resumed, '"then Switch training resumes": a switch try after the walk back').toBeGreaterThan(clearing)
  expect(SWITCH_TRY.exec(log[resumed])!.slice(1, 3), '"then Switch training resumes": switch try 1 to slot 1').toEqual(['1', '1'])
}

/**
 * fainted: polls backend.log until the stop, noting when each BattleAdvance line first shows, then asserts the stop
 * within five battles and 45 s of the faint text, nothing pressed after it, Waiting, and the prompt in memory.
 */
async function checkFaintedStop(): Promise<void> {
  const criterion = '"the battler fainted in battle" (open with the owner: the stop at "Use next POKéMON?")'
  let advances = 0
  let advanceSeen = Date.now()
  let log = backendLog()
  const deadline = Date.now() + 6 * MINUTE
  while (indexOf(log, FAINTED_END) === -1) {
    failOnStops(log, FAINTED_END)
    if (Date.now() > deadline) {
      throw new Error(`${criterion}: no stop within 6 minutes; fleeing lines: ${JSON.stringify(log.filter((l) => FLEEING.test(l)).map((l) => l.trim()))}`)
    }
    await sleep(250)
    log = backendLog()
    const n = log.filter((l) => ADVANCE.test(l)).length
    if (n > advances) [advances, advanceSeen] = [n, Date.now()]
  }
  const stopSeen = Date.now()
  const stop = indexOf(log, FAINTED_END)
  step(`logged: ${log[stop].trim()}`)
  expect(log.slice(0, stop).filter((l) => OVER.test(l)).length, `${criterion}: within five battles`).toBeLessThan(5)

  // After the battle's SHIFT, the last press is the A at "… fainted!" (window 8), and the stop follows within 45 s. The
  // wild Pokémon acts in the switch turn, so the 1 HP battler can faint before its first action menu, with no "switch
  // confirmed"; or a turn later, after it. Both pass; steps.log says which.
  const shifted = log.flatMap((l, i) => (i < stop && AFTER_SHIFT.test(l) ? [i] : [])).at(-1)
  expect(shifted, `${criterion}: no "after SHIFT" before the stop`).toBeDefined()
  const faint = log.flatMap((l, i) => (i > shifted! && i < stop && ADVANCE.test(l) ? [i] : [])).at(-1)
  expect(faint, `${criterion}: no BattleAdvance after SHIFT`).toBeDefined()
  const confirmed = log.slice(shifted!, faint!).some((l) => SWITCH_CONFIRMED.test(l))
  step(`the battler fainted ${confirmed ? 'a turn after "switch confirmed"' : 'in the switch turn, before its first action menu'}`)
  expect(ADVANCE.exec(log[faint!])![1], `${criterion}: the last BattleAdvance ("fainted!") window`).toBe('8')
  step(`the faint text's BattleAdvance first seen ${stopSeen - advanceSeen} ms before the stop`)
  expect(stopSeen - advanceSeen, `${criterion}: the stop within 45 s of the faint text`).toBeLessThanOrEqual(45_000)
  expect(snapshotField(FAINTED_END.exec(log[stop])![1], 'Screen'), `${criterion}: the snapshot the stop names`).toBe('BattleTurn')

  await expect.poll(async () => (await dashboardState())?.eventState, { message: `${criterion}: the event is not Waiting`, timeout: MINUTE, intervals: [1000] })
    .toBe('Waiting')
  // In memory while Waiting: the battler at 0 HP (slot 2; research S11, the records in their own order), still in the
  // battle, the script at "Use next POKéMON?", and the controller not at the action menu.
  const battler = await readSlot(2)
  const battleBit = await readBattleBit()
  const script = await readU32(SCRIPT)
  const controller = await readU32(CONTROLLER)
  step(`while Waiting: slot 2 HP ${battler.hp}/${battler.maxHp}, battle bit ${battleBit}, script 0x${hex8(script)}, controller 0x${hex8(controller)}`)
  expect([battler.hp, battleBit, hex8(script)], `${criterion}: slot 2's HP, the battle bit, gBattlescriptCurrInstr`).toEqual([0, 1, hex8(USE_NEXT)])
  expect(hex8(controller), `${criterion}: the controller is at the action menu`).not.toBe(hex8(MENU_FN))

  // "nothing pressed after the faint text": no press in the lines since, read again after the memory reads.
  const since = backendLog().slice(faint! + 1)
  expect(pressCount(since), `${criterion}: presses after the faint text:\n${since.filter((l) => l.includes('[RsBattle]')).join('\n')}`).toBe(0)
}

test(`rs-train-switch-battler-spent ${VARIANT}: ${VARIANT === 'fainted' ? 'the battler faints in battle; the stop at "Use next POKéMON?"' : `the battler's ${VARIANT === 'hp' ? 'HP' : 'PP'} spent; the Center trip, then Switch training resumes`}`, async () => {
  test.setTimeout(20 * MINUTE)
  if (VARIANT === 'fainted') expect(rsRom(), 'the "Use next POKéMON?" script pointer was read on Sapphire only: run fainted on SA-Wild').toBe('SA-Wild')
  const spent = VARIANT === 'hp' ? { hp: 1 }
    : VARIANT === 'pp' ? { pp: [0, 0, 0, 0] }
    // Not too hurt to fight (1 of 1 HP), and any damaging hit faints it.
    : { maxHp: 1, hp: 1, attack: 1, spAtk: 1 }
  const { log, idle } = await trainingRun(async () => {
    await copyLeadToSlot2()
    await writeSlot(2, { level: 20, exp: expAt(20), ...spent })
  }, { trainType: 'Switch', targetLevel: 101, switchSlot: 2, expectedStop: VARIANT === 'fainted' ? FAINTED_END : undefined }, async () => {
    if (VARIANT === 'fainted') {
      await checkFaintedStop()
    } else {
      await waitLine(SWITCH_TRY, 1, 10 * MINUTE, '"then Switch training resumes": the first switch try')
    }
    return 'cancel'
  })

  if (VARIANT !== 'fainted') checkHealTrip(log)
  if (VARIANT === 'pp') {
    const pp = idle.party[1].pp.filter((_, i) => idle.party[1].moves[i] !== 0)
    expect(pp.every((v) => v > 0), `"its PP restored": slot 2's PP ${JSON.stringify(idle.party[1].pp)}, read with the bot idle`).toBe(true)
  }
})
