// rs-train-heal (docs/specs/rs-navigation-and-training.md, QA; FR-11, FR-18; plan of #433, Q1e): the lead's PP written to
// 0, Train Standard to 101, so the orchestrator takes the Pokémon Center trip for PP before any encounter, the nurse
// restores it, and training resumes with a move that has PP. QA_RS_ROM=RU-Wild runs it on Ruby, the only Ruby run of the
// nurse's per-game last wait (WaitForAorBPress). The walk itself is rs-nav-heal-return's to check in detail.
// QA_RS_VARIANT=battles-first leaves the lead 5 damaging PP instead: it battles until fewer than 3 are left (the
// orchestrator's CanBattle), and only then takes the trip.
import { expect, test } from '@playwright/test'
import { step } from './driver'
import {
  ARRIVED, checkHome, CHOSEN, CLEARING, FLEEING, HEAL_TRIP, indexOf, NURSE_DONE, NURSE_HP, NURSE_PRESS, PLAN, rsRom, tileOf, trainingRun, variant,
  waitLine, writeSlot
} from './rs-training'
import { ENCOUNTER, OVER } from './rs-wild'
import { backendLog, MINUTE } from './static'

const CENTER = 'OldaleTown_PokemonCenter_1F (7, 4)'
const START_TILE = 'Route101 (14, 4)'
// The nurse's last wait, WaitForAorBPress, per game (research, "Healing"), as the press line prints its read.
const WAIT_FOR_A_OR_B: Record<string, string> = { 'SA-Wild': '080670E5', 'RU-Wild': '080670E1' }

const VARIANT = variant(['empty', 'battles-first'])
const BATTLES_FIRST = VARIANT === 'battles-first'

const GO_TO_CENTER = /\[Orchestrator\] State: GoToPokemonCenter\s*$/

test(`RS heal trip for PP, ${VARIANT}: the lead's ${BATTLES_FIRST ? 'damaging PP 5, battles down to under 3' : 'PP 0'}, Train Standard to 101, the Pokémon Center, then training resumes`, async () => {
  test.setTimeout(25 * MINUTE)
  const rom = rsRom()
  step(`on ${rom}`)
  const { log, idle, lead } = await trainingRun(async () => {
    // The save's lead has three moves; the fourth slot is empty with PP 0.
    // battles-first: the first move is left as the only one with PP.
    await writeSlot(1, { pp: [BATTLES_FIRST ? 5 : 0, 0, 0, 0] })
  }, { trainType: 'Standard', targetLevel: 101 }, async () => {
    await waitLine(GO_TO_CENTER, 1, (BATTLES_FIRST ? 10 : 1) * MINUTE, '"the Pokémon Center trip": no GoToPokemonCenter')
    await waitLine(NURSE_HP, 1, 6 * MINUTE, '"the Pokémon Center trip": no "party HP … (full)" after the nurse')
    await waitLine(CLEARING, 1, 6 * MINUTE, '"the Pokémon Center trip": not back on the start tile')
    // The trip flees, so the first move choice after the nurse is the first battle after the return.
    const nurse = indexOf(backendLog(), NURSE_HP)
    const choices = backendLog().slice(0, nurse).filter((l) => CHOSEN.test(l)).length
    await waitLine(CHOSEN, choices + 1, 5 * MINUTE, '"then training resumes": no move chosen after the return')
    const sofar = backendLog()
    const overs = sofar.slice(0, indexOf(sofar, CHOSEN, nurse)).filter((l) => OVER.test(l)).length
    await waitLine(OVER, overs + 1, 3 * MINUTE, '"then training resumes": the first battle after the return is not over')
    return 'cancel'
  })

  // The trip (L7) for PP, not HP: before any encounter, or, battles-first, after battles the damage picker fought.
  const center = indexOf(log, GO_TO_CENTER)
  if (BATTLES_FIRST) {
    const fought = log.slice(0, center).filter((l) => CHOSEN.test(l)).length
    step(`move choices before the trip: ${fought}`)
    expect(fought, '"battles first": no move chosen before GoToPokemonCenter').toBeGreaterThan(0)
    expect(log.slice(0, center).some((l) => OVER.test(l)), '"battles first": no battle over before GoToPokemonCenter').toBe(true)
  } else {
    const firstEncounter = indexOf(log, ENCOUNTER)
    expect(firstEncounter === -1 || center < firstEncounter, '"the Pokémon Center trip": GoToPokemonCenter after an encounter').toBe(true)
  }
  // The plan's L7 line (T3b, #451).
  const trip = log.map((l) => HEAL_TRIP.exec(l)).find((m) => m !== null)
  expect(trip, '"the Pokémon Center trip": no "[Orchestrator] heal trip:" line (T3b)').toBeTruthy()
  if (BATTLES_FIRST) {
    // Under 3 damaging PP left of the 5 written, and the lead not too hurt (a quarter of its HP or less), so PP took it.
    expect(Number(trip![4]), `"the Pokémon Center trip" for PP, under 3 left: ${trip![0]}`).toBeLessThan(3)
    expect(Number(trip![2]), `"the Pokémon Center trip" for PP, not HP: ${trip![0]}`).toBeGreaterThan(Math.floor(Number(trip![3]) / 4))
  } else {
    expect([Number(trip![2]), trip![4]], `"the Pokémon Center trip" for PP, with the lead's HP as launched (${lead.hp}): ${trip![0]}`).toEqual([lead.hp, '0'])
  }

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
  // The last arrival on the start tile, or, with a wild battle on the last step onto it, that battle bit on it, a plan
  // from it with 0 segments and `already at start`.
  checkHome(log, healed, START_TILE, '"the Pokémon Center trip": back on the start tile')
  const cleared = indexOf(log, CLEARING, healed)

  // Training resumes: the first battle after the return chooses a move with PP, and nothing fled for want of PP.
  const chosen = CHOSEN.exec(log[indexOf(log, CHOSEN, cleared)])!
  step(`the first move choice after the return: ${chosen[0].trim()}`)
  expect(Number(chosen[5]), `"then training resumes": ${chosen[0]}`).toBeGreaterThan(0)
  const noPp = log.filter((l) => FLEEING.exec(l)?.[1] === 'no damaging PP')
  expect(noPp, '"then training resumes": a "fleeing: no damaging PP" line').toEqual([])

  // With the bot idle after the cancel: the three moves' PP above 0 (L11: the nurse restores PP, and no line logs it).
  expect(idle.party[0].pp.slice(0, 3).every((pp) => pp > 0), `"the PP restored": the lead's PP with the bot idle, ${JSON.stringify(idle.party[0].pp)}`).toBe(true)
})
