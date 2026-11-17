// rs-train-heal (docs/specs/rs-navigation-and-training.md, QA; FR-11, FR-18; plan of #433, Q1e): the lead's PP written to
// 0, Train Standard to 101, so the orchestrator takes the Pokémon Center trip for PP before any encounter, the nurse
// restores it, and training resumes with a move that has PP. QA_RS_ROM=RU-Wild runs it on Ruby, the only Ruby run of the
// nurse's per-game last wait (WaitForAorBPress). The walk itself is rs-nav-heal-return's to check in detail.
import { expect, test } from '@playwright/test'
import { step } from './driver'
import { ARRIVED, CHOSEN, CLEARING, FLEEING, HEAL_TRIP, NURSE_DONE, NURSE_HP, NURSE_PRESS, PLAN, rsRom, trainingRun, waitLine, writeSlot } from './rs-training'
import { ENCOUNTER, OVER } from './rs-wild'
import { backendLog, MINUTE } from './static'

const CENTER = 'OldaleTown_PokemonCenter_1F (7, 4)'
const START_TILE = 'Route101 (14, 4)'
// The nurse's last wait, WaitForAorBPress, per game (research, "Healing"), as the press line prints its read.
const WAIT_FOR_A_OR_B: Record<string, string> = { 'SA-Wild': '080670E5', 'RU-Wild': '080670E1' }

const GO_TO_CENTER = /\[Orchestrator\] State: GoToPokemonCenter\s*$/

const tileOf = (m: RegExpExecArray, at: number): string => `${m[at]} (${m[at + 1]}, ${m[at + 2]})`
const indexOf = (log: string[], pattern: RegExp, from = 0): number => log.findIndex((l, i) => i >= from && pattern.test(l))

test('RS heal trip for PP: the lead\'s PP 0, Train Standard to 101, the Pokémon Center, then training resumes', async () => {
  test.setTimeout(25 * MINUTE)
  const rom = rsRom()
  step(`on ${rom}`)
  const { log, idle } = await trainingRun(async () => {
    // The save's lead has three moves; the fourth slot is empty with PP 0.
    await writeSlot(1, { pp: [0, 0, 0, 0] })
  }, { trainType: 'Standard', targetLevel: 101 }, async () => {
    await waitLine(GO_TO_CENTER, 1, MINUTE, '"the Pokémon Center trip": no GoToPokemonCenter')
    await waitLine(NURSE_HP, 1, 6 * MINUTE, '"the Pokémon Center trip": no "party HP … (full)" after the nurse')
    await waitLine(CLEARING, 1, 6 * MINUTE, '"the Pokémon Center trip": not back on the start tile')
    // The trip flees, so the first move choice is the first battle after the return.
    await waitLine(CHOSEN, 1, 5 * MINUTE, '"then training resumes": no move chosen after the return')
    const sofar = backendLog()
    const overs = sofar.slice(0, indexOf(sofar, CHOSEN)).filter((l) => OVER.test(l)).length
    await waitLine(OVER, overs + 1, 3 * MINUTE, '"then training resumes": the first battle after the return is not over')
    return 'cancel'
  })

  // The trip, before any encounter, and (L7) for PP, not HP.
  const center = indexOf(log, GO_TO_CENTER)
  const firstEncounter = indexOf(log, ENCOUNTER)
  expect(firstEncounter === -1 || center < firstEncounter, '"the Pokémon Center trip": GoToPokemonCenter after an encounter').toBe(true)
  // T3b (#451), not merged: the plan's L7 line, as the plan words it. The orchestrator checks it once T3b merges.
  const trip = log.map((l) => HEAL_TRIP.exec(l)).find((m) => m !== null)
  expect(trip, '"the Pokémon Center trip": no "[Orchestrator] heal trip:" line (T3b)').toBeTruthy()
  expect([trip![2] === trip![3], trip![4]], `"the Pokémon Center trip" for PP, with HP full: ${trip![0]}`).toEqual([true, '0'])

  // The plan, the arrival, the nurse's six presses with the game's last wait, and the party's HP full.
  const plan = PLAN.exec(log[indexOf(log, PLAN)])!
  expect([tileOf(plan, 1), tileOf(plan, 4)], `"the Pokémon Center trip": the plan: ${plan[0]}`).toEqual([CENTER, START_TILE])
  const arrived = indexOf(log, ARRIVED)
  expect(tileOf(ARRIVED.exec(log[arrived])!, 1), `"the Pokémon Center trip": the first arrival: ${log[arrived]}`).toBe(CENTER)
  const done = indexOf(log, NURSE_DONE, arrived)
  const presses = log.slice(arrived, done).filter((l) => NURSE_PRESS.test(l))
  expect(presses.length, `"the nurse's six presses": ${presses.join('\n')}`).toBe(6)
  expect(NURSE_DONE.exec(log[done])![1], `"the nurse's six presses": ${log[done]}`).toBe('6')
  const last = NURSE_PRESS.exec(presses[5])!
  expect(last[2], `"the nurse's last press on the ${rom} WaitForAorBPress": ${presses[5]}`).toContain(`status 0, mode 2, native 0x${WAIT_FOR_A_OR_B[rom]},`)
  const healed = indexOf(log, NURSE_HP, done)
  expect(healed, '"the Pokémon Center trip": no "party HP … (full)" after the nurse').toBeGreaterThan(done)

  // The return to the start tile.
  const back = PLAN.exec(log[indexOf(log, PLAN, healed)])!
  expect([tileOf(back, 1), tileOf(back, 4)], `"the Pokémon Center trip": the return plan: ${back[0]}`).toEqual([START_TILE, CENTER])
  const cleared = indexOf(log, CLEARING)
  const home = ARRIVED.exec(log.slice(0, cleared).filter((l) => ARRIVED.test(l)).pop()!)!
  expect([tileOf(home, 1), CLEARING.exec(log[cleared])![1]], '"the Pokémon Center trip": back on the start tile').toEqual([START_TILE, 'arrived back at start'])

  // Training resumes: the first battle after the return chooses a move with PP, and nothing fled for want of PP.
  const chosen = CHOSEN.exec(log[indexOf(log, CHOSEN, cleared)])!
  step(`the first move choice after the return: ${chosen[0].trim()}`)
  expect(Number(chosen[5]), `"then training resumes": ${chosen[0]}`).toBeGreaterThan(0)
  const noPp = log.filter((l) => FLEEING.exec(l)?.[1] === 'no damaging PP')
  expect(noPp, '"then training resumes": a "fleeing: no damaging PP" line').toEqual([])

  // With the bot idle after the cancel: the three moves' PP above 0 (L11: the nurse restores PP, and no line logs it).
  expect(idle.party[0].pp.slice(0, 3).every((pp) => pp > 0), `"the PP restored": the lead's PP with the bot idle, ${JSON.stringify(idle.party[0].pp)}`).toBe(true)
})
