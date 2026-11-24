// rs-pickup-take (docs/specs/rs-pickup.md, QA; FR-4 to FR-8, FR-10, NFR-1): the five Zigzagoon given Super Potion, Full Heal,
// Ultra Ball, Rare Candy and Full Restore in RAM with the bot idle; Wild Encounter, Spin, Train Standard, target 101, Pickup
// on; the first visit, before any battle, takes all five in slot order; Cancel. The plan's run line (#490, Q1a):
// `npx playwright test rs-pickup-take`.
import { expect, test } from '@playwright/test'
import { writeSlot, STATE } from './rs-training'
import { MINUTE } from './static'
import {
  checkBag, checkEmptyHanded, checkTrip, checkVisit, MENU_ENTRIES, PICKUP_HELD, PICKUP_SLOTS, PICKUP_TRIP, pickupRun, takes, visit, waitPickup
} from './rs-pickup'

const ITEMS = [22, 23, 2, 68, 19]
const NAMES = ['Super Potion', 'Full Heal', 'Ultra Ball', 'Rare Candy', 'Full Restore']
const ENCOUNTERING = /\[Orchestrator\] State: Encountering\s*$/

test('RS Pickup rs-pickup-take: five items in hand, the first visit before any battle takes all five in slot order, cancel', async () => {
  test.setTimeout(10 * MINUTE)
  const { log, idle, launchBag, idleBag } = await pickupRun(
    async () => {
      for (let i = 0; i < PICKUP_SLOTS.length; i++) await writeSlot(PICKUP_SLOTS[i], { item: ITEMS[i] })
    },
    async () => {
      await waitPickup(PICKUP_TRIP, 1, 2 * MINUTE, '"the first visit, before any battle"')
      // The event's own post-visit read passed: the decision goes on to Encountering.
      await waitPickup(ENCOUNTERING, 1, 30_000, '"Encountering follows with no stop"')
      return 'cancel'
    }
  )

  const v = visit(log, 1)
  const trip = log.findIndex((l) => PICKUP_TRIP.test(l))
  const held = log.findIndex((l) => PICKUP_HELD.test(l))
  const encountering = log.findIndex((l) => ENCOUNTERING.test(l))
  expect(PICKUP_HELD.exec(log[held])!.slice(1), 'the count line').toEqual(['5', '5', 'take'])
  expect(log.slice(0, trip).filter((l) => STATE.test(l) && /Encountering|BattleStart/.test(l)), '"the first visit, before any battle"').toEqual([])
  expect(encountering, '"Encountering follows"').toBeGreaterThan(trip)

  // "five takes in slot order", each with its name and Gen 3 id.
  expect(takes(v).map((t) => [t.slot, t.item, t.name]), '"five takes in slot order"')
    .toEqual(PICKUP_SLOTS.map((slot, i) => [slot, ITEMS[i], NAMES[i]]))
  expect(v.map((l) => MENU_ENTRIES.exec(l)).filter((m) => m !== null).map((m) => [m![1], m![3], m![4]]), 'each slot\'s menu entries')
    .toEqual(PICKUP_SLOTS.map((slot) => [String(slot), '0,1,2,3', '2']))

  // "each press with its confirmation, the field free": START, Down 0 -> 1, A on POKéMON, eight per take, B, B.
  checkVisit(v, { first: true, slots: PICKUP_SLOTS.map((slot) => ({ slot, item: 2 })) }, '"each press with its confirmation"')

  // "under 15 s" (NFR-1), in game frames: 900 at 60 a second.
  checkTrip(log[trip], ITEMS.length, '"under 15 s"')

  // Between the Cancels: "the party empty-handed" and "the bag holding the five" (plus any later take).
  checkEmptyHanded(log, idle.party, PICKUP_SLOTS)
  checkBag(launchBag, idleBag, log)
})
