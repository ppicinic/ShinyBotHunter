import { expect, test } from '@playwright/test'
import { step } from './driver'
import {
  AFTER_SHIFT, ARRIVED, CLEARING, copyLeadToSlot2, discordMessages, expAt, failOnStops, HEAL_TRIP, indexOf, NAV_BATTLE, NURSE_DONE, NURSE_HP,
  NURSE_PRESS, PLAN, PRESS, pressCount, readSlot, STATE, SWITCH_CONFIRMED, SWITCH_TRY, trainingRun, variant, waitLine, writeLogged, writeSlot
} from './rs-training'
import { ADVANCE, CONFIRM, dashboardState, ENCOUNTER, OUTCOME, OVER, readBattleBit, readU16, readU8, TO_RUN, WINDOW } from './rs-wild'
import { backendLog, MINUTE } from './static'

// rs-train-switch-battler-spent (docs/specs/rs-navigation-and-training.md, QA table; FR-17 to FR-19; #433's plan, part Q1d;
// #475): Switch training with a Lv 20 copy of the lead in slot 2 as the battler. QA_RS_VARIANT:
// - `hp` (the default): the battler's HP written to 1, so the orchestrator's rule takes the Pokémon Center trip before the
//   first encounter; the nurse heals both, the walk back, then Switch training resumes. Cancelled in that battle.
// - `pp`: the battler's four PP written to 0; the same trip, and its PP above 0 after.
// - `fainted`, `fainted-run`, `fainted-stop`: the battler fainting in battle, and the owner's rule of 2026-11-17 (#475;
//   research "A battler faints: the readings"). Each battle's wild Pokémon gets its battle Speed written at its first action
//   menu, the write F4 used to force a failed escape. `fainted` (1): B at "Use next POKéMON?" gets away, then the heal trip.
//   `fainted-run` (999, then 1 at the send-out's line): the flee fails, the fastest alive (the lead, slot 0, row 1 after the
//   switch) is sent out, the one Run gets away, then the heal trip. `fainted-stop` (999): the flee fails, the send-out, the
//   Run fails too: Waiting with the run-failed sentence and one Discord message. At the second try about 1 in 8 Runs still
//   get away (30 is added per try), and that run is run again.
// The Train Switch and No Target choices are enabled by T3b (#451).
const VARIANT = variant(['hp', 'pp', 'fainted', 'fainted-run', 'fainted-stop'])
const FAINTED = VARIANT.startsWith('fainted')

// gBattleMons[1].speed (u16), the wild Pokémon's battle Speed (research "A battler faints: the readings", the round's
// set-up and F4).
const WILD_SPEED = 0x02024ade

// RsBattleEvent's lines for the faint (#475). The é of POKéMON is any one or two characters (backend.log is in the console's
// code page).
const SENTENCE = 'A Pokémon fainted, and running from the battle failed after the fastest Pokémon left was sent out.'
const RUN_FAILED = /\[RsBattle\] stopped: A Pok.{1,2}mon fainted, and running from the battle failed after the fastest Pok.{1,2}mon left was sent out\. runTries (\d+) -> (\d+)( \(did not rise\))?, (GameSnapshot \{.*\})\s*$/
const USE_NEXT = /\[RsBattle\] Use next POK.{1,2}MON\? for slot (\d): fleeing\s*$/
// The forced menu's read, one entry per row (round six, B: the records are in row order while the menu is up).
const ROWS = /\[RsBattle\] the party menu after a faint, by row: (.*?)\s*$/
const ROW = /^row (\d) slot (\d) (.*) HP (\d+) Speed (\d+)$/
const ORDER = /\[RsBattle\] party menu order ([0-9A-F]{2}) ([0-9A-F]{2}) ([0-9A-F]{2}), case byte (\d+): slot (\d) is on row (\d)\s*$/

const CENTER = 'OldaleTown_PokemonCenter_1F (7, 4)'
const START = 'Route101 (14, 4)'
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

/**
 * The trip, the nurse, the walk back and the next battle's switch, in that order, from backend.log, from line `from` (the
 * fainted variants: the faint battle's `battle over`).
 */
function checkHealTrip(log: string[], from = 0): void {
  // "the orchestrator's rule: the Pokémon Center trip", for hp and pp before any encounter. HEAL_TRIP is the QA plan's L7,
  // which T3b (#451) adds.
  const firstEncounter = indexOf(log, ENCOUNTER)
  const before = (i: number): boolean => i >= from && (FAINTED || firstEncounter === -1 || i < firstEncounter)
  const toCenter = log.findIndex((l, i) => i >= from && STATE.exec(l)?.[1] === 'GoToPokemonCenter')
  expect(before(toCenter), `"the Pokémon Center trip": State GoToPokemonCenter${FAINTED ? ' after the faint battle' : ' before any encounter'}`).toBe(true)
  const why = indexOf(log, HEAL_TRIP, from)
  expect(before(why), `"the orchestrator's rule": the heal trip line (L7, T3b)${FAINTED ? ' after the faint battle' : ' before any encounter'}`).toBe(true)
  const [, , hp, , pp] = HEAL_TRIP.exec(log[why])!
  if (VARIANT === 'hp') expect(Number(hp), '"the battler\'s HP written to 1": the heal trip line').toBe(1)
  else if (VARIANT === 'pp') expect(Number(pp), '"its PP to 0": the heal trip line').toBe(0)
  else expect(Number(hp), '"the battler fainted in battle": the heal trip line').toBe(0)

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

/** Writes the wild Pokémon's battle Speed, logged to steps.log with why. */
async function writeWildSpeed(speed: number, why: string): Promise<void> {
  const bytes = Buffer.alloc(2)
  bytes.writeUInt16LE(speed)
  await writeLogged(`the wild Pokémon's battle Speed (gBattleMons[1].speed) ${speed}, ${why}`, WILD_SPEED, bytes)
}

/**
 * The fainted variants' watch: polls backend.log every 250 ms and writes the wild Pokémon's battle Speed at each new
 * encounter line (1 for `fainted`, and for `fainted-run` once it has sent out, so the walk's battles get away; 999
 * otherwise) and, for `fainted-run`, 1 at each forced menu's rows line, until `done` holds. Fails on a stop other than
 * `expectedStop`, and after `ms`.
 */
async function watchFaint(done: (log: string[]) => boolean, ms: number, criterion: string, expectedStop?: RegExp): Promise<void> {
  let encounters = 0
  let sendOuts = 0
  const deadline = Date.now() + ms
  while (true) {
    const log = backendLog()
    failOnStops(log, expectedStop)
    if (VARIANT === 'fainted-run') {
      for (const n = log.filter((l) => ROWS.test(l)).length; sendOuts < n;) {
        sendOuts++
        await writeWildSpeed(1, `at send-out ${sendOuts}, before the Run`)
      }
    }
    for (const n = log.filter((l) => ENCOUNTER.test(l)).length; encounters < n;) {
      encounters++
      await writeWildSpeed(VARIANT === 'fainted' || sendOuts > 0 ? 1 : 999, `at battle ${encounters}'s first action menu`)
    }
    if (done(log)) return
    if (Date.now() > deadline) {
      const last = log.filter((l) => /\[(RsBattle|Navigator|Orchestrator)\]/.test(l)).slice(-10).join('\n')
      throw new Error(`${criterion}: not within ${ms / MINUTE} minutes; last lines:\n${last}`)
    }
    await sleep(250)
  }
}

/** The faint's lines from line `from` on: each press as its line names it, the order line, and nothing else. */
const faintEvents = (lines: string[]): string[] => lines.flatMap((l) => {
  const press = PRESS.exec(l)
  const advance = ADVANCE.exec(l)
  const order = ORDER.exec(l)
  return press ? [`${press[1].replace(/ at GameSnapshot \{.*\}$/, '')} | ${press[3]}`]
    : advance ? [`BattleAdvance at window ${advance[1]}`]
    : TO_RUN.test(l) ? ['BattleCursorToRun']
    : CONFIRM.test(l) ? ['BattleConfirm on RUN']
    : order ? [`order ${order[1]} ${order[2]}, case byte ${order[4]}: slot ${order[5]} on row ${order[6]}`]
    : []
})

/**
 * The faint, from the log: the last BattleAdvance after the battle's SHIFT at window 8 ("… fainted!"), then the question
 * for slot 1 answered by B, confirmed by the script leaving it, with nothing pressed between. Returns the B line's index.
 */
function checkQuestion(log: string[]): number {
  const criterion = '"the battler fainted in battle"'
  const no = log.findIndex((l) => /B \(No\) at Use next POK.{1,2}MON\? for slot 1$/.test(PRESS.exec(l)?.[1] ?? ''))
  expect(no, `${criterion}: no B at "Use next POKéMON?" for slot 1`).toBeGreaterThan(-1)
  step(`logged: ${log[no].trim()}`)
  expect(PRESS.exec(log[no])![3], `${criterion}: what confirmed the B`).toMatch(/^the script leaving Use next POK.{1,2}MON\?$/)
  // Round six, A: B is sent only once the box takes keys, so it is never dropped and sent again.
  expect(PRESS.exec(log[no])![5], `${criterion}: the B at "Use next POKéMON?" was dropped and sent again: ${log[no].trim()}`).toBeUndefined()
  const question = log.flatMap((l, i) => (i < no && USE_NEXT.test(l) ? [i] : [])).at(-1)
  expect(question, `${criterion}: no "Use next POKéMON? for slot 1" line before the B`).toBeDefined()
  expect(USE_NEXT.exec(log[question!])![1], `${criterion}: the fainted slot`).toBe('1')
  // The wild Pokémon acts in the switch turn, so the 1 HP battler can faint before its first action menu, with no "switch
  // confirmed"; or a turn later, after it. Both pass; steps.log says which.
  const shifted = log.flatMap((l, i) => (i < no && AFTER_SHIFT.test(l) ? [i] : [])).at(-1)
  expect(shifted, `${criterion}: no "after SHIFT" before the question`).toBeDefined()
  const faint = log.flatMap((l, i) => (i > shifted! && i < no && ADVANCE.test(l) ? [i] : [])).at(-1)
  expect(faint, `${criterion}: no BattleAdvance after SHIFT`).toBeDefined()
  const confirmed = log.slice(shifted!, faint!).some((l) => SWITCH_CONFIRMED.test(l))
  step(`the battler fainted ${confirmed ? 'a turn after "switch confirmed"' : 'in the switch turn, before its first action menu'}`)
  expect(ADVANCE.exec(log[faint!])![1], `${criterion}: the last BattleAdvance ("fainted!") window`).toBe('8')
  expect(pressCount(log.slice(faint! + 1, no)), `${criterion}: presses between "fainted!" and the B`).toBe(0)
  return no
}

// The presses after the B when the flee fails, to the A on RUN: A at "Can't escape!", the order (slot 1 came in for slot
// 0), one Down to the lead's row, A on it, SEND OUT, then RUN from the action cursor 0.
const SEND_OUT_PRESSES = [
  'BattleAdvance at window 8',
  'order 10 23, case byte 1: slot 0 on row 1',
  'Down on the party list from 0 | the party cursor leaving 0',
  'BattleConfirm on party slot 0 | the SEND OUT submenu',
  'BattleConfirm on SEND OUT | the submenu closing',
  'BattleCursorToRun',
  'BattleConfirm on RUN'
]

/**
 * The forced menu's rows line, once between `from` and `to`: row 0 the fainted battler (slot 1, HP 0), row 1 the trainee
 * (slot 0, alive), the one sent out.
 */
function checkRows(log: string[], from: number, to: number, criterion: string): void {
  const lines = log.slice(from, to).filter((l) => ROWS.test(l))
  expect(lines.length, `${criterion}: the forced menu's rows line`).toBe(1)
  step(`logged: ${lines[0].trim()}`)
  const rows = ROWS.exec(lines[0])![1].split('; ').map((r) => ROW.exec(r)!)
  expect(rows.map((r) => [r[1], r[2], r[4] === '0']), `${criterion}: row, slot and fainted in the rows line`)
    .toEqual([['0', '1', true], ['1', '0', false]])
}

/** fainted and fainted-run: the faint battle ends fled with the presses of the variant; returns its `battle over` index. */
function checkFled(log: string[]): number {
  const criterion = VARIANT === 'fainted' ? '"the battle is fled"' : '"the fastest alive sent out, then the Run gets away"'
  const no = checkQuestion(log)
  const over = indexOf(log, OVER, no)
  expect(over, `${criterion}: no "battle over" after the B`).toBeGreaterThan(no)
  const events = faintEvents(log.slice(no + 1, over))
  step(`after the B: ${JSON.stringify(events)}`)
  if (VARIANT === 'fainted') {
    expect(log.slice(no, over).filter((l) => ROWS.test(l)), `${criterion}: a send-out with the wild Pokémon's Speed at 1`).toEqual([])
    expect(events, `${criterion}: the one press after the B is the A at "Got away safely!"`).toEqual(['BattleAdvance at window 8'])
    return over
  }
  checkRows(log, no, over, criterion)
  expect(events, `${criterion}: the presses after the B`).toEqual([...SEND_OUT_PRESSES, 'BattleAdvance at window 8'])
  return over
}

/**
 * fainted-stop: the stop line with the sentence and runTries rising by one, Waiting, one Discord message, the bot left at
 * "Can't escape!" in memory, and nothing pressed after the A on RUN.
 */
async function checkRunFailed(runStart: string): Promise<void> {
  const criterion = '"the Run fails: Waiting and one Discord message"'
  const log = backendLog()
  const no = checkQuestion(log)
  const stop = indexOf(log, RUN_FAILED, no)
  step(`logged: ${log[stop].trim()}`)
  const [, before, after, notRisen] = RUN_FAILED.exec(log[stop])!
  expect([Number(after) - Number(before), notRisen], `${criterion}: runTries in the stop line`).toEqual([1, undefined])
  checkRows(log, no, stop, criterion)
  expect(faintEvents(log.slice(no + 1, stop)), `${criterion}: the presses after the B`).toEqual(SEND_OUT_PRESSES)
  await expect.poll(async () => (await dashboardState())?.eventState, { message: `${criterion}: the event is not Waiting`, timeout: MINUTE, intervals: [1000] })
    .toBe('Waiting')
  await expect.poll(async () => (await discordMessages(runStart, SENTENCE)).length,
    { message: `${criterion}: Discord messages "${SENTENCE}" since the Run click`, timeout: 30_000, intervals: [3000] }).toBe(1)
  // In memory while Waiting: still in the battle, "Can't escape!" waiting at window 8 with the outcome 0 (F6).
  const read = { battleBit: await readBattleBit(), window: await readU16(WINDOW), outcome: await readU8(OUTCOME), slot2: (await readSlot(2)).hp }
  step(`while Waiting: ${JSON.stringify(read)}`)
  expect(read, `${criterion}: the battle bit, the battle window, the outcome, slot 2's HP`).toEqual({ battleBit: 1, window: 8, outcome: 0, slot2: 0 })
  const since = backendLog().slice(stop + 1)
  expect(pressCount(since), `${criterion}: presses after the stop:\n${since.filter((l) => l.includes('[RsBattle]')).join('\n')}`).toBe(0)
}

/** The fainted variants' wait, run while the bot trains: to the heal trip's next switch try, or to the run-failed stop. */
async function waitFaint(): Promise<void> {
  const criterion = '"the battler fainted in battle"'
  // A faint can take up to five battles, as before #475.
  if (VARIANT === 'fainted-stop') {
    await watchFaint((log) => {
      const no = log.findIndex((l) => USE_NEXT.test(l))
      const over = no === -1 ? -1 : indexOf(log, OVER, no)
      if (over !== -1) {
        throw new Error(`${criterion}: ${log.slice(no, over).some((l) => ROWS.test(l))
          ? 'the Run got away (about 1 in 8 at the second try): run again'
          : 'the flee at the question got away with the wild Pokémon\'s Speed at 999: run again'}`)
      }
      return indexOf(log, RUN_FAILED) !== -1
    }, 8 * MINUTE, criterion, RUN_FAILED)
    return
  }
  await watchFaint((log) => {
    const clearing = indexOf(log, CLEARING, Math.max(0, log.findIndex((l) => USE_NEXT.test(l))))
    return log.some((l) => USE_NEXT.test(l)) && clearing !== -1 && indexOf(log, SWITCH_TRY, clearing) !== -1
  }, 12 * MINUTE, `${criterion}: the flee, the heal trip and the next switch try`)
}

test(`rs-train-switch-battler-spent ${VARIANT}: ${
  VARIANT === 'fainted' ? 'the battler faints in battle; B at "Use next POKéMON?" gets away, then the heal trip'
  : VARIANT === 'fainted-run' ? 'the battler faints in battle; the flee fails, the fastest alive is sent out, the Run gets away, then the heal trip'
  : VARIANT === 'fainted-stop' ? 'the battler faints in battle; the flee and the Run after the send-out fail: Waiting and one Discord message'
  : `the battler's ${VARIANT === 'hp' ? 'HP' : 'PP'} spent; the Center trip, then Switch training resumes`}`, async () => {
  test.setTimeout(20 * MINUTE)
  const spent = VARIANT === 'hp' ? { hp: 1 }
    : VARIANT === 'pp' ? { pp: [0, 0, 0, 0] }
    // Not too hurt to fight (1 of 1 HP), and any damaging hit faints it.
    : { maxHp: 1, hp: 1, attack: 1, spAtk: 1 }
  const expectedStop = VARIANT === 'fainted-stop' ? RUN_FAILED : undefined
  const { log, idle } = await trainingRun(async () => {
    await copyLeadToSlot2()
    await writeSlot(2, { level: 20, exp: expAt(20), ...spent })
  }, { trainType: 'Switch', targetLevel: 101, switchSlot: 2, discord: VARIANT === 'fainted-stop', expectedStop }, async ({ runStart }) => {
    if (FAINTED) {
      await waitFaint()
      if (VARIANT === 'fainted-stop') await checkRunFailed(runStart)
    } else {
      await waitLine(SWITCH_TRY, 1, 10 * MINUTE, '"then Switch training resumes": the first switch try')
    }
    return 'cancel'
  })

  if (!FAINTED) checkHealTrip(log)
  if (VARIANT === 'fainted' || VARIANT === 'fainted-run') checkHealTrip(log, checkFled(log))
  if (VARIANT === 'pp') {
    const pp = idle.party[1].pp.filter((_, i) => idle.party[1].moves[i] !== 0)
    expect(pp.every((v) => v > 0), `"its PP restored": slot 2's PP ${JSON.stringify(idle.party[1].pp)}, read with the bot idle`).toBe(true)
  }
})
