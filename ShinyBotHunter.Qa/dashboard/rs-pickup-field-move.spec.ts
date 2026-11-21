// rs-pickup-field-move (docs/specs/rs-pickup.md, QA; FR-6): slot 6's first move written to Cut and a Nugget put in its hand
// in RAM with the bot idle; the take's action; the first visit finds ITEM after the field move, at position 3. The plan's
// run line (#490, Q1a): `npx playwright test rs-pickup-field-move`.
import { expect, test } from '@playwright/test'
import { readSlot, writeSlot } from './rs-training'
import { MINUTE } from './static'
import {
  checkBag, checkEmptyHanded, checkVisit, MENU_ENTRIES, PICKUP_HELD, PICKUP_SLOTS, PICKUP_TRIP, pickupRun, takes, visit, waitPickup
} from './rs-pickup'

// Cut (move 15, 30 PP) and a Nugget (Gen 3 item 110). Round 2 saw entries 10,0,1,2,3 with exactly this write.
const CUT = 15
const NUGGET = 110

test('RS Pickup rs-pickup-field-move: Cut first on slot 6, its menu walked past the field move to ITEM at 3, the Nugget taken', async () => {
  test.setTimeout(10 * MINUTE)
  const { log, idle, launchBag, idleBag } = await pickupRun(
    async () => {
      const six = await readSlot(6)
      await writeSlot(6, { moves: [CUT, ...six.moves.slice(1)], pp: [30, ...six.pp.slice(1)], item: NUGGET })
    },
    async () => {
      await waitPickup(PICKUP_TRIP, 1, 2 * MINUTE, '"the first visit"')
      return 'cancel'
    }
  )

  const v = visit(log, 1)
  expect(PICKUP_HELD.exec(v.find((l) => PICKUP_HELD.test(l))!)!.slice(1), 'the count line').toEqual(['1', '5', 'take'])
  // "the menu's entries logged with the field move first; ITEM found at position 3".
  const entries = v.map((l) => MENU_ENTRIES.exec(l)).filter((m) => m !== null)
  expect(entries.map((m) => [m![1], m![3], m![4]]), '"the field move first; ITEM at position 3"').toEqual([['6', '10,0,1,2,3', '3']])
  // Four Downs on the list, three on the menu (0 -> 1, 1 -> 2, 2 -> 3).
  checkVisit(v, { first: true, slots: [{ slot: 6, item: 3 }] }, '"ITEM found at position 3"')
  expect(takes(v).map((t) => [t.slot, t.item, t.name]), '"the Nugget taken"').toEqual([[6, NUGGET, 'Nugget']])

  // Between the Cancels: slot 6 empty-handed with Cut still first, and the Nugget in the bag (launch's two, plus one).
  checkEmptyHanded(log, idle.party, PICKUP_SLOTS)
  expect(idle.party[5].moves[0], 'slot 6\'s first move with the bot idle').toBe(CUT)
  checkBag(launchBag, idleBag, log)
})
