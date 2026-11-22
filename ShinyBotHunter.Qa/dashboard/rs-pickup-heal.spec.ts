// rs-pickup-heal (docs/specs/rs-pickup.md, QA): rs-pickup with the lead's HP written low after a visit. Run 1, target 12,
// with the lead one experience point short of Lv 12: the visit, one won battle, Complete. At Complete, with the bot idle,
// the lead's HP written to a quarter of its max (IsTooHurtToFight is hp <= maxHp / 4) and its PP full. Run 2, target 101:
// the heal trip before any visit, the Pokémon Center and the walk back, the visit before the next battle, then two more
// wins each followed by a visit; Cancel. The plan's run line (#490, Q1b): `npx playwright test rs-pickup-heal`.
import { expect, test } from '@playwright/test'
import { step } from './driver'
import {
  checkHome, CLEARING, HEAL_TRIP, indexOf, NURSE_HP, oneShort, PLAN, PP, readParty, STATE, tileOf, waitComplete, writeSlot
} from './rs-training'
import { OVER } from './rs-wild'
import { backendLog, MINUTE } from './static'
import { checkDecisions, checkVisit, PICKUP_HELD, PICKUP_SLOTS, PICKUP_TRIP, pickupRun, startPickup, visit, waitPickup, WON } from './rs-pickup'

const CENTER = 'OldaleTown_PokemonCenter_1F (7, 4)'
const START_TILE = 'Route101 (14, 4)'
// The save's lead: Tackle, Growl, Mud-Slap, Water Gun. Growl does no damage, so the damaging PP is 35 + 10 + 25.
const MOVES = [33, 45, 189, 55]
const DAMAGING_PP = 70

test('RS Pickup rs-pickup-heal: Complete at Lv 12, the HP written low, then the heal trip, the visit after the walk back and training goes on', async () => {
  test.setTimeout(25 * MINUTE)
  // Run 2's first log line, its heal trip's HP and max, the Zigzagoon holding at Complete, and the trip line awaited
  // before the Cancel, counted from the first.
  let from = 0
  let hurt = { hp: 0, maxHp: 0 }
  let holders: number[] = []
  let awaited = 0
  const { log } = await pickupRun(
    async () => {
      // Mudkip learns nothing at 12 (Bide is 15), so the level-up is one box and no prompt.
      await writeSlot(1, oneShort(12, 11))
    },
    async (run) => {
      // Run 1: the visit, one won battle, then Lv 12 >= the target with NoTarget completes; the walk home is `already at start`.
      await waitPickup(PICKUP_TRIP, 1, 2 * MINUTE, 'Run 1: "the first visit"')
      await waitPickup(OVER, 1, 3 * MINUTE, 'Run 1: the battle that reaches Lv 12')
      await waitComplete(undefined, 3 * MINUTE)

      // At Complete, with the bot idle: the party (which Zigzagoon rolled an item), then the HP low and the PP full.
      const party = await readParty()
      step(`party at Complete: ${JSON.stringify(party)}`)
      const lead = party[0]
      expect([lead.level, lead.moves], 'the lead at Complete: Lv 12 with the save\'s four moves').toEqual([12, MOVES])
      holders = PICKUP_SLOTS.filter((slot) => party[slot - 1].item !== 0)
      hurt = { hp: Math.floor(lead.maxHp / 4), maxHp: lead.maxHp }
      await writeSlot(1, { hp: hurt.hp, pp: MOVES.map((id) => PP[id]) })

      // Run 2: the heal trip first, the nurse, the walk back, the visit, then two more wins each followed by a visit.
      from = backendLog().length
      const clearedBefore = backendLog().filter((l) => CLEARING.test(l)).length
      await startPickup(run.card, 101)
      await waitPickup(HEAL_TRIP, 1, 2 * MINUTE, 'Run 2: "the Pokémon Center trip"')
      await waitPickup(NURSE_HP, 1, 6 * MINUTE, 'Run 2: no "party HP … (full)" after the nurse')
      await waitPickup(CLEARING, clearedBefore + 1, 6 * MINUTE, 'Run 2: "the walk back"')
      const wonBefore = backendLog().filter((l) => WON.test(l)).length
      await waitPickup(WON, wonBefore + 2, 6 * MINUTE, 'Run 2: "training goes on": two won battles after the walk back')
      const sofar = backendLog()
      const second = sofar.flatMap((l, i) => (WON.test(l) ? [i] : []))[wonBefore + 1]
      awaited = sofar.slice(0, second).filter((l) => PICKUP_TRIP.test(l)).length + 1
      await waitPickup(PICKUP_TRIP, awaited, 2 * MINUTE, 'Run 2: the visit after the second win')
      return 'cancel'
    },
    { targetLevel: 12 }
  )

  // Run 1's visit: nothing held, nothing to take, the start-menu Down of the session's first visit.
  const v1 = visit(log, 1)
  expect(PICKUP_HELD.exec(v1.find((l) => PICKUP_HELD.test(l))!)!.slice(1), 'Run 1: the first count line').toEqual(['0', '5', 'nothing to take'])
  checkVisit(v1, { first: true, slots: [] }, 'Run 1: "the first visit"')

  // The heal trip is Run 2's first decision, before any count line of Run 2, for the HP alone.
  const run2 = log.slice(from)
  const heal = run2.findIndex((l) => HEAL_TRIP.test(l))
  expect(heal, 'Run 2: the heal trip before any visit').toBeLessThan(run2.findIndex((l) => PICKUP_HELD.test(l)))
  const [, , hp, maxHp, pp] = HEAL_TRIP.exec(run2[heal])!
  expect([Number(hp), Number(maxHp), Number(pp)], `"the Pokémon Center trip" for the HP: ${run2[heal].trim()}`).toEqual([hurt.hp, hurt.maxHp, DAMAGING_PP])

  // To the Oldale Center from the start tile, the nurse, and back on the start tile.
  const plan = PLAN.exec(log[indexOf(log, PLAN, from + heal)])!
  expect([tileOf(plan, 1), tileOf(plan, 4)], `"the Pokémon Center trip": the plan: ${plan[0]}`).toEqual([CENTER, START_TILE])
  const healed = indexOf(log, NURSE_HP, from + heal)
  checkHome(log, healed, START_TILE, '"the walk back": back on the start tile')
  const cleared = indexOf(log, CLEARING, healed)

  // "a visit before the next battle": the count line after the walk back, before the next Encountering, counting the
  // Zigzagoon that held an item at Complete.
  const next = log.findIndex((l, i) => i > cleared && STATE.exec(l)?.[1] === 'Encountering')
  const count = log.slice(cleared, next).filter((l) => PICKUP_HELD.test(l))
  expect(count.map((l) => PICKUP_HELD.exec(l)![1]), '"a visit before the next battle": its count line, against the holders at Complete')
    .toEqual([String(holders.length)])

  // Run 2's decisions: the heal window with its one visit, then one after every battle, the start menu's cursor kept from Run 1.
  const cut = log.flatMap((l, i) => (PICKUP_TRIP.test(l) ? [i] : []))[awaited - 1]
  const { decisions } = checkDecisions(log.slice(0, cut + 1), from, false)
  expect(decisions.length, '"training goes on": Run 2\'s decision windows').toBeGreaterThanOrEqual(3)
  expect(decisions[0].takes.map((t) => t.slot), 'the visit after the walk back takes from the holders at Complete').toEqual(holders)
})
