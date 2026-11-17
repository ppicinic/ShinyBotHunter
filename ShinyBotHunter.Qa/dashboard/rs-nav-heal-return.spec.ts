// rs-nav-heal-return (docs/specs/rs-navigation-and-training.md, QA; FR-7 to FR-9, FR-11, FR-19; plan of #433, Q1e): the
// lead's HP written to 5, Train Standard to 101, so the first decision is the walk to Oldale's Pokémon Center, the nurse
// and the walk back to the start tile. QA_RS_VARIANT picks the start:
// - route101 (the default): Route 101 (14, 4), the save's tile. The walk in is the research doc's five macros ("Search
//   and macros"), and the first battle after the return is fought.
// - house: Oldale House 1's landing (3, 8), reached by the scenario's own walk with encounters off. The walk back ends
//   with the run cut at the landing that waits on x (FR-8 R3, the research's R3b), and the player ends free there.
import { expect, test } from '@playwright/test'
import { step } from './driver'
import {
  ARRIVED, CHOSEN, CLEARING, HEAL_TRIP, NAV_BATTLE, NURSE_DONE, NURSE_HP, NURSE_PRESS, PLAN, PRESS, readSlot, repel, rsRom,
  SEGMENT, SEGMENT_ON, trainingRun, variant, waitLine, walkTo, writeSlot, type Segment
} from './rs-training'
import { checkFlee, ENCOUNTER, OVER, START, type Tile } from './rs-wild'
import { backendLog, MINUTE } from './static'

const VARIANT = variant(['route101', 'house'] as const)
const HP = 5

// The tiles as the [Navigator] lines print them, `<internal map name> (x, y)`.
const CENTER = 'OldaleTown_PokemonCenter_1F (7, 4)'
const HOUSE = 'OldaleTown_House1 (3, 8)'
const ROUTE101 = 'Route101 (14, 4)'
const START_TILE = VARIANT === 'house' ? HOUSE : ROUTE101
// Oldale House 1 (BotMapId 512) is group 2, number 0.
const HOUSE_TILE: Tile = { group: 2, num: 0, x: 3, y: 8 }

// The research doc's five macros ("Search and macros"), with the map and tile each ends on.
const FIVE = [
  ['M.Left.8.33707826:H.B.1', 'Route101 (8, 4)'],
  ['M.Up.0.33707828:H.B.1', 'Route101 (8, 0)'],
  ['M.Up.17.33707828:H.B.1', 'OldaleTown (8, 17)'],
  ['M.Left.6.33707826:H.B.1', 'OldaleTown (6, 17)'],
  ['M.Up.4.33707828:H.B.1', CENTER]
]

// The scenario's own walk from Route 101 (14, 4) to the house's landing: RouteSearch's segments on the committed Data/RS,
// printed by a throwaway local test (not committed). The last is R3b's cut run, Up through the door (5, 7) from (5, 8),
// ending when x reads 3 on the landing.
const TO_HOUSE: Segment[] = [
  { macro: 'M.Left.9.33707826:H.B.1', group: 0, num: 16, x: 9, y: 4 },
  { macro: 'M.Up.0.33707828:H.B.1', group: 0, num: 16, x: 9, y: 0 },
  { macro: 'M.Up.8.33707828:H.B.1', group: 0, num: 10, x: 9, y: 8 },
  { macro: 'M.Left.5.33707826:H.B.1', group: 0, num: 10, x: 5, y: 8 },
  { macro: 'M.Up.3.33707826:H.B.1', ...HOUSE_TILE }
]
// FR-8 R3 as R3b walked it: the cut run into the house waits on x (RunWatchX, 0x02025732).
const CUT_RUN = 'M.Up.3.33707826:H.B.1'

// FR-8: one run per segment, with RunHold joined to it, so B is held on every segment.
const HELD = /^M\.(Up|Down|Left|Right)\.\d+\.(33707826|33707828):H\.B\.1$/
// An arrow warp's run: Down on a mat, waiting for the y outside (RunWatchY).
const MAT = /^M\.Down\.\d+\.33707828:H\.B\.1$/
// The nurse's last wait, WaitForAorBPress, per game (research, "Healing").
const WAIT_FOR_A_OR_B: Record<string, string> = { 'SA-Wild': '080670E5', 'RU-Wild': '080670E1' }

const GO_TO_CENTER = /\[Orchestrator\] State: GoToPokemonCenter\s*$/
const FIGHT = /\[RsBattle\] BattleConfirm on FIGHT, \d+ ms: confirmed by /
const STUCK = /\[Navigator\] stopped: Stuck walking/
const ANY_SEGMENT_END = /\[Navigator\] segment \d+\/\d+: (on|battle bit on|field locked) /

// RsNavigatorEvent's nurse read (N4b, #450), as its press lines print it.
const NURSE_READ = /^\(f (\d+): lock (\d+), callback 1 0x([0-9A-F]{8}), window (\d+), status (\d+), mode (\d+), native 0x([0-9A-F]{8}), yes\/no (\d+|none), facing 0x([0-9A-F]{2}), tile transition (\d+)\)$/
type NurseRead = { lock: number; callback1: string; window: number; status: number; mode: number; native: string; yesNo: string; facing: number; transition: number }
function nurseRead(read: string): NurseRead {
  const m = NURSE_READ.exec(read)
  if (!m) throw new Error(`not a nurse read: ${read}`)
  return {
    lock: Number(m[2]), callback1: m[3], window: Number(m[4]), status: Number(m[5]), mode: Number(m[6]), native: m[7], yesNo: m[8],
    facing: parseInt(m[9], 16), transition: Number(m[10])
  }
}

const tileOf = (m: RegExpExecArray, at: number): string => `${m[at]} (${m[at + 1]}, ${m[at + 2]})`
const indexOf = (log: string[], pattern: RegExp, from = 0): number => log.findIndex((l, i) => i >= from && pattern.test(l))

/**
 * FR-8 and FR-9 on every walk of the run: each segment's macro holds B; each segment ends on its tile with frames, or in a
 * battle bit, never stale; each battle bit is followed by a fled battle and a new plan; and NFR-2 on every plan line.
 */
function checkWalks(log: string[]): void {
  expect(log.filter((l) => STUCK.test(l)), '"no segment went stale"').toEqual([])
  let battle = 0
  log.forEach((line, i) => {
    const plan = PLAN.exec(line)
    if (plan) {
      const [p, q] = [Number(plan[13]), Number(plan[14])]
      expect(p < 100 && p + q < 1000, `NFR-2: script state ${p} ms (under 100) and search ${q} ms (the two under 1000): ${line}`).toBe(true)
      return
    }
    const segment = SEGMENT.exec(line)
    if (segment) {
      const [, n, of, macro] = segment
      expect(macro, `"B held on every segment": segment ${n}/${of}: ${line}`).toMatch(HELD)
      const end = log.slice(i + 1).find((l) => ANY_SEGMENT_END.test(l))
      const on = end ? SEGMENT_ON.exec(end) : null
      const bit = end ? NAV_BATTLE.exec(end) : null
      if (on) {
        expect([on[1], on[2], tileOf(on, 3)], `"each segment on its tile": ${line} ended ${end}`).toEqual([n, of, tileOf(segment, 4)])
        expect(Number(on[6]), `"each segment with frames": ${end}`).toBeGreaterThan(0)
      } else if (bit) {
        expect([bit[1], bit[2]], `"each segment on its tile or in a battle": ${line} ended ${end}`).toEqual([n, of])
      } else {
        throw new Error(`"each segment on its tile or in a wild battle": ${line} ended ${end ?? 'with no end line'}`)
      }
      return
    }
    if (NAV_BATTLE.test(line)) {
      const next = indexOf(log, PLAN, i + 1)
      expect(next, `"a re-plan after a battle": no plan line after ${line}`).toBeGreaterThan(i)
      checkFlee(log.slice(i, next), ++battle)
      step(`walk battle ${battle}: fled, then planned again: ${log[next].trim()}`)
    }
  })
}

/**
 * "Back on the start tile", after the nurse: the last arrival on the start tile, then `arrived back at start`; or, when a
 * wild battle starts on the last step onto the start tile, that battle bit on it, a plan from it with 0 segments, and
 * `already at start` (no arrival line).
 */
function checkHome(log: string[], healed: number, start: string): void {
  const criterion = '"back on the start tile"'
  const cleared = indexOf(log, CLEARING, healed)
  expect(cleared, `${criterion}: no "Clearing navigation path" after the nurse`).toBeGreaterThan(healed)
  const reason = CLEARING.exec(log[cleared])![1]
  const before = log.slice(healed, cleared)
  step(`back on the start tile: ${reason}`)
  if (reason === 'arrived back at start') {
    const home = before.filter((l) => ARRIVED.test(l)).pop()
    expect(home, `${criterion}: no arrival line before the clear`).toBeDefined()
    expect(tileOf(ARRIVED.exec(home!)!, 1), `${criterion}: ${home}`).toBe(start)
    return
  }
  const bit = before.map((l) => NAV_BATTLE.exec(l)).filter((m) => m !== null).pop()
  expect(bit && tileOf(bit, 3), `${criterion}: "already at start" with no battle bit on the start tile before it`).toBe(start)
  const zero = before.slice(before.indexOf(bit!.input)).map((l) => PLAN.exec(l)).find((m) => m !== null)
  expect(zero && [tileOf(zero, 1), tileOf(zero, 4), zero[10]], `${criterion}: no plan from the start tile with 0 segments after ${bit!.input.trim()}`)
    .toEqual([start, start, '0'])
}

/** FR-11: from the arrival at the counter, six presses, each sent on its gate and confirmed by the value it changes. */
function checkNurse(log: string[], from: number, rom: string): void {
  const criterion = '"the nurse\'s six presses, each on its gate"'
  const done = indexOf(log, NURSE_DONE, from)
  expect(done, `${criterion}: no "nurse: N presses" line after the arrival`).toBeGreaterThan(from)
  const presses = log.slice(from, done).flatMap((l) => {
    const m = NURSE_PRESS.exec(l)
    return m ? [{ line: l.trim(), macro: m[1], at: nurseRead(m[2]), by: nurseRead(m[3]) }] : []
  })
  expect(presses.map((p) => p.macro), `${criterion}: the presses: ${presses.map((p) => p.line).join('\n')}`).toEqual(Array(6).fill('FieldAdvance'))
  expect(NURSE_DONE.exec(log[done])![1], `${criterion}: ${log[done]}`).toBe('6')
  const pageBreak = (p: (typeof presses)[number]): boolean => p.at.lock !== 0 && [5, 8, 9].includes(p.at.window) && p.by.window !== p.at.window
  const gates: [string, (p: (typeof presses)[number]) => boolean][] = [
    ['the talk: free, at rest and facing up, then the lock', (p) => p.at.lock === 0 && p.at.callback1 !== '00000000' && p.at.transition === 0 && (p.at.facing & 0x0f) === 2 && p.by.lock !== 0],
    ['a page break: a button wait, then the window leaving it', pageBreak],
    ['a page break: a button wait, then the window leaving it', pageBreak],
    ['the Yes: the Yes/No counter at 5, then the task gone', (p) => p.at.yesNo === '5' && p.by.yesNo === 'none'],
    ['"Thank you for waiting.": a button wait, then the window leaving it', pageBreak],
    [`the last line: status 0, mode 2, native 0x${WAIT_FOR_A_OR_B[rom]}, then the lock 0 and status 2`,
      (p) => p.at.status === 0 && p.at.mode === 2 && p.at.native === WAIT_FOR_A_OR_B[rom] && p.by.lock === 0 && p.by.status === 2]
  ]
  gates.forEach(([gate, holds], k) => expect(holds(presses[k]), `${criterion}: press ${k + 1}, ${gate}: ${presses[k].line}`).toBe(true))
}

test(`RS heal trip (${VARIANT}): lead HP ${HP}, Train Standard to 101, the walk to Oldale's Pokémon Center, the nurse, the walk back`, async () => {
  test.setTimeout(25 * MINUTE)
  const rom = rsRom()
  step(`variant ${VARIANT} on ${rom}`)
  const result = await trainingRun(async () => {
    if (VARIANT === 'house') {
      // Scenario set-up, with the bot idle: encounters off through the Repel step counter for the walk, on again before Run.
      await repel(250)
      await walkTo(TO_HOUSE)
      await repel(0)
    }
    const lead = await readSlot(1)
    expect(HP, `HP ${HP} is not at or below the heal line, max HP ${lead.maxHp} / 4`).toBeLessThanOrEqual(Math.floor(lead.maxHp / 4))
    await writeSlot(1, { hp: HP })
  }, {
    trainType: 'Standard',
    targetLevel: 101,
    // Indoors Spin finds no battle and stops; the scenario cancels once back on the landing, and that stop is ignored.
    expectedStop: VARIANT === 'house' ? /\[Spin\] Something other than a wild battle/ : undefined
  }, async () => {
    await waitLine(GO_TO_CENTER, 1, MINUTE, '"the plan to Oldale\'s Pokémon Center": no GoToPokemonCenter')
    await waitLine(NURSE_HP, 1, 6 * MINUTE, '"HP full after the nurse"')
    await waitLine(CLEARING, 1, 6 * MINUTE, '"back on the start tile"')
    if (VARIANT === 'route101') {
      // GoToPokemonCenter and the return flee, so the first FIGHT is the first battle after the return.
      await waitLine(FIGHT, 1, 5 * MINUTE, '"the first battle after it is fought"')
      const log = backendLog()
      const overs = log.slice(0, indexOf(log, FIGHT)).filter((l) => OVER.test(l)).length
      await waitLine(OVER, overs + 1, 3 * MINUTE, '"the first battle after it is fought": no battle over')
    }
    return 'cancel'
  })

  const { log, idle } = result
  const criterion = (what: string): string => `"${what}"`

  // GoToPokemonCenter before any encounter, and (L7) why.
  const center = indexOf(log, GO_TO_CENTER)
  const firstEncounter = indexOf(log, ENCOUNTER)
  expect(firstEncounter === -1 || center < firstEncounter, `${criterion('the plan to Oldale\'s Pokémon Center')}: GoToPokemonCenter after an encounter`).toBe(true)
  // T3b (#451), not merged: the plan's L7 line, as the plan words it. The orchestrator checks it once T3b merges.
  const trip = log.map((l) => HEAL_TRIP.exec(l)).find((m) => m !== null)
  expect(trip, `${criterion('the plan to Oldale\'s Pokémon Center')}: no "[Orchestrator] heal trip:" line (T3b)`).toBeTruthy()
  expect(Number(trip![2]), `the heal trip's reason: ${trip![0]}`).toBe(HP)

  // The plan to the Center, from the start tile.
  const plans = log.flatMap((l, i) => (PLAN.test(l) ? [i] : []))
  const out = PLAN.exec(log[plans[0]])!
  step(`the first plan: ${log[plans[0]].trim()}`)
  expect([tileOf(out, 1), tileOf(out, 4)], `${criterion('the plan to Oldale\'s Pokémon Center')}: ${out[0]}`).toEqual([CENTER, START_TILE])
  expect(Number(out[11]), `${criterion('the plan to Oldale\'s Pokémon Center')}: flags set`).toBeGreaterThan(0)
  expect(out[12], `${criterion('the plan to Oldale\'s Pokémon Center')}: live triggers crossed`).toBe('none')
  checkWalks(log)

  // Arrived at the counter, then the nurse, then HP full.
  const arrivals = log.flatMap((l, i) => (ARRIVED.test(l) ? [i] : []))
  const atCounter = ARRIVED.exec(log[arrivals[0]])!
  expect(tileOf(atCounter, 1), `${criterion('the plan to Oldale\'s Pokémon Center')}: the first arrival: ${atCounter[0]}`).toBe(CENTER)
  checkNurse(log, arrivals[0], rom)
  const healed = indexOf(log, NURSE_HP)
  expect(healed, `${criterion('HP full after the nurse')}: no "party HP … (full)" line after the nurse`).toBeGreaterThan(arrivals[0])

  // The walk back: the return plan from the counter to the start tile, the last arrival on it, then the bot's clear.
  const back = PLAN.exec(log[indexOf(log, PLAN, healed)])!
  step(`the return plan: ${back[0].trim()}`)
  expect([tileOf(back, 1), tileOf(back, 4)], `${criterion('back on the start tile')}: the return plan`).toEqual([START_TILE, CENTER])
  checkHome(log, healed, START_TILE)
  const walkBack = log.slice(healed).filter((l) => SEGMENT.test(l)).map((l) => SEGMENT.exec(l)!)
  // The exit mat's arrow warp: Down to the mat (7, 8), then Down on it, the run waiting for a y outside.
  expect([walkBack[0][3], tileOf(walkBack[0], 4)], `${criterion('back on the start tile')}: the return's first segment`).toEqual(['M.Down.8.33707828:H.B.1', 'OldaleTown_PokemonCenter_1F (7, 8)'])
  expect(walkBack[1][3], `${criterion('back on the start tile')}: the return's second segment, the mat's arrow warp`).toMatch(MAT)
  expect(walkBack[1][4], `${criterion('back on the start tile')}: the arrow warp's map`).toBe('OldaleTown')

  if (VARIANT === 'route101') {
    // The plan with its segments: cost 45, the five macros, each on its tile; a wild battle on the way leaves some unwalked.
    expect([out[8], out[10]], `${criterion('the plan to Oldale\'s Pokémon Center with its segments')}: cost and segments`).toEqual(['45', '5'])
    // The segments walked under the first plan are the table's, in order; a wild battle on the way (FR-9) ends that plan
    // early, is fled (checkWalks), and a new plan takes the walk on to the counter.
    const walkIn = log.slice(plans[0], arrivals[0])
    const battleAt = walkIn.findIndex((l) => NAV_BATTLE.test(l))
    const walked = walkIn.slice(0, battleAt === -1 ? walkIn.length : battleAt).filter((l) => SEGMENT.test(l)).map((l) => SEGMENT.exec(l)!)
      .map((m) => [m[3], tileOf(m, 4)])
    if (battleAt === -1) {
      step('the walk in: the five macros, no battle')
      expect(walked, `${criterion('the plan to Oldale\'s Pokémon Center with its segments')}: the research table's five macros`).toEqual(FIVE)
      expect(atCounter[4], `${criterion('the plan to Oldale\'s Pokémon Center with its segments')}: ${atCounter[0]}`).toBe('5')
    } else {
      step(`the walk in: ${walked.length} of the five macros, then a wild battle: ${walkIn[battleAt].trim()}`)
      expect(walked.length, `${criterion('the plan to Oldale\'s Pokémon Center with its segments')}: no segment before the battle`).toBeGreaterThan(0)
      expect(walked, `${criterion('the plan to Oldale\'s Pokémon Center with its segments')}: the first plan's segments, a prefix of the table's`).toEqual(FIVE.slice(0, walked.length))
      const again = indexOf(walkIn, PLAN, battleAt)
      expect(again, `"a re-plan after a battle": no plan line between ${walkIn[battleAt].trim()} and the arrival`).toBeGreaterThan(battleAt)
      const replan = PLAN.exec(walkIn[again])!
      step(`the walk in planned again: ${replan[0].trim()}`)
      expect(tileOf(replan, 1), '"a re-plan after a battle": its goal').toBe(CENTER)
    }

    // The first battle after the return: FIGHT, and it ends with nothing on RUN. Its first move choice reads the lead's HP
    // from the party record at the first turn, before any damage: full.
    const cleared = indexOf(log, CLEARING)
    const fight = indexOf(log, FIGHT, cleared)
    expect(fight, `${criterion('the first battle after it is fought')}: no BattleConfirm on FIGHT after the return`).toBeGreaterThan(cleared)
    const over = OVER.exec(log[indexOf(log, OVER, fight)])!
    expect(over[4], `${criterion('the first battle after it is fought')}: ${over[0]}`).toBe('0')
    const chosen = CHOSEN.exec(log[indexOf(log, CHOSEN, cleared)])!
    expect(chosen[3], `${criterion('HP full after the nurse')}: the first move choice after the return: ${chosen[0]}`).toBe(chosen[4])
    const pressed = PRESS.exec(log[fight])!
    step(`the first battle after the return: ${pressed[1]} confirmed by ${pressed[3]}; ${over[0].trim()}`)

    // With the bot idle after the cancel: on the start tile, the field free, and the lead above the heal line.
    expect(idle.tile, `${criterion('back on the start tile')}: (14, 4) on (0, 16), read with the bot idle`).toEqual(START)
    expect(idle.party[0].hp, `${criterion('HP full after the nurse')}: the lead with the bot idle`).toBeGreaterThan(Math.floor(idle.party[0].maxHp / 4))
  } else {
    // The trip out starts with the house mat's arrow warp.
    const first = SEGMENT.exec(log[indexOf(log, SEGMENT, plans[0])])!
    expect([first[1], first[4]], `"the trip out starts with the mat's arrow warp": ${first[0]}`).toEqual(['1', 'OldaleTown'])
    expect(first[3], `"the trip out starts with the mat's arrow warp": ${first[0]}`).toMatch(MAT)
    // The return's cut run into the house, waiting on x, ends on the landing; it is the walk's last segment.
    const cut = walkBack[walkBack.length - 1]
    expect([cut[3], tileOf(cut, 4)], '"the return plan holds the cut run that waits on x" (FR-8 R3)').toEqual([CUT_RUN, HOUSE])
    const cutAt = log.indexOf(cut.input, healed)
    const landed = SEGMENT_ON.exec(log[indexOf(log, SEGMENT_ON, cutAt)])!
    expect([landed[1], tileOf(landed, 3)], `"the cut run ends on the landing": ${landed[0]}`).toEqual([cut[1], HOUSE])

    // With the bot idle after the cancel: free on the landing, and the lead's HP full (no battle indoors).
    expect(idle.tile, `"the player ends free on the landing": ${HOUSE}, read with the bot idle`).toEqual(HOUSE_TILE)
    expect(idle.party[0].hp, '"HP full after the nurse": the lead with the bot idle').toBe(idle.party[0].maxHp)
  }
  expect([idle.lock, idle.battleBit], '"the player ends free": the lock and the battle bit with the bot idle').toEqual([0, 0])
})
