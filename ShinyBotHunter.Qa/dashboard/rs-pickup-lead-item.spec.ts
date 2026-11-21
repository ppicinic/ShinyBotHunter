// rs-pickup-lead-item (docs/specs/rs-pickup.md, QA; FR-5): the Mudkip given a Potion (which does nothing held in battle) and
// the Zigzagoon in slot 4 a Revive, in RAM with the bot idle; the take's action; two visits (the second after the first won
// battle); only the Zigzagoon's item is taken. The plan's run line (#490, Q1a): `npx playwright test rs-pickup-lead-item`.
import { expect, test } from '@playwright/test'
import { writeSlot } from './rs-training'
import { MINUTE } from './static'
import {
  checkBag, checkEmptyHanded, checkVisit, PICKUP_HELD, PICKUP_PRESS, PICKUP_SLOTS, PICKUP_TRIP, pickupRun, takes, visit, visitedSlots,
  waitPickup
} from './rs-pickup'

const POTION = 13
const REVIVE = 24

test('RS Pickup rs-pickup-lead-item: the Mudkip keeps its Potion, the Zigzagoon\'s Revive is taken, no stop', async () => {
  test.setTimeout(12 * MINUTE)
  const { log, idle, launchBag, idleBag } = await pickupRun(
    async () => {
      await writeSlot(1, { item: POTION })
      await writeSlot(4, { item: REVIVE })
    },
    async () => {
      await waitPickup(PICKUP_TRIP, 2, 4 * MINUTE, '"two visits": the visit after the first won battle')
      return 'cancel'
    }
  )

  // Both count lines count the five Zigzagoon only; the first holds the Revive.
  const counts = log.map((l) => PICKUP_HELD.exec(l)).filter((m) => m !== null)
  expect(counts.slice(0, 2).map((m) => m![2]), '"of 5": the Mudkip is not a Pickup member').toEqual(['5', '5'])
  expect(counts[0]![1], 'the first count line').toBe('1')

  // "only the Zigzagoon's is taken": visit 1 takes the Revive from slot 4; visit 2 takes what the battle gave, if anything.
  const v1 = visit(log, 1)
  expect(takes(v1).map((t) => [t.slot, t.item, t.name]), 'visit 1\'s takes').toEqual([[4, REVIVE, 'Revive']])
  checkVisit(v1, { first: true, slots: [{ slot: 4, item: 2 }] }, 'visit 1')
  const v2 = visit(log, 2)
  checkVisit(v2, { first: false, slots: visitedSlots(v2) }, 'visit 2')

  // No line names slot 1 and no press opens it.
  const slot1 = log.filter((l) => l.includes('[Pickup]') && (/slot 1 \(/.test(l) || PICKUP_PRESS.exec(l)?.[1] === 'A on cursor 0'))
  expect(slot1, '"the Mudkip still holds its item": a [Pickup] line for slot 1').toEqual([])

  // Between the Cancels: the Mudkip still holds the Potion, the Zigzagoon empty-handed, the bag with the Revive and the
  // Potion as at launch.
  expect(idle.party[0].item, '"the Mudkip still holds its item" with the bot idle').toBe(POTION)
  checkEmptyHanded(log, idle.party, PICKUP_SLOTS)
  checkBag(launchBag, idleBag, log)
})
