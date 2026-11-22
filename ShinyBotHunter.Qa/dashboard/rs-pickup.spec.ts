// rs-pickup (docs/specs/rs-pickup.md, QA; NFR-1, NFR-2, FR-13): the qa-pickup save as saved, the lead's HP and PP written
// full; Wild Encounter, Spin, Train Standard, target 101, Pickup on; thirty won battles, a visit before the first and after
// every one; Cancel after the visit that follows the thirtieth; between the Cancels the party, the bag and the Pickup items
// window. The plan's run line (#490, Q1b): `npx playwright test rs-pickup.spec`.
import { expect, test } from '@playwright/test'
import { step } from './driver'
import { expAt, readSlot } from './rs-training'
import { backendLog, MINUTE } from './static'
import {
  checkBag, checkDecisions, checkEmptyHanded, checkItemsWindow, ITEMS_POCKET, PICKUP_ITEMS, PICKUP_SLOTS, PICKUP_TRIP, pickupRun,
  takes, waitPickup
} from './rs-pickup'

const BATTLES = 30
// A won battle: the lead knocked the wild Pokémon out, nothing pressed on RUN.
const WON = /\[RsBattle\] battle over: (\d+) frames from the battle bit, (\d+) ms, (\d+) presses, 0 on RUN\s*$/
// Ultra Ball, the one Pickup item of the Poké Balls pocket.
const ULTRA_BALL = 2
// The most experience a Route 101 wild gives: floor(60 x 3 / 7), a Lv 3 Zigzagoon; Mudkip learns Bide at Lv 15.
const MOST_EXP = 25

test('RS Pickup rs-pickup: thirty won battles, a visit before the first and after every one, every item taken, the items window', async () => {
  test.setTimeout(35 * MINUTE)
  // The trip line awaited before the Cancel, counted from the first.
  let awaited = 0
  const { log, idle, launchBag, idleBag } = await pickupRun(
    async (launch) => {
      // Thirty-one wins cannot reach Lv 15, so no move prompt comes; the Zigzagoon never battle and gain nothing.
      const lead = await readSlot(1)
      step(`lead before Run: Lv ${lead.level}, exp ${lead.exp}`)
      expect(lead.exp + (BATTLES + 1) * MOST_EXP, `the lead's exp ${lead.exp} plus ${BATTLES + 1} wins of ${MOST_EXP} reaches Lv 15 (${expAt(15)}), Bide's prompt`)
        .toBeLessThan(expAt(15))
      // Room in the bag for every Pickup item: each Items-pocket kind not yet there needs an empty slot.
      const present = launch.items.map((r) => r.item)
      const missing = PICKUP_ITEMS.filter((id) => id !== ULTRA_BALL && !present.includes(id))
      expect(present.filter((id) => id === 0).length, `the Items pocket at 0x${ITEMS_POCKET.toString(16)} has no room for ${missing.join(', ')}`)
        .toBeGreaterThanOrEqual(missing.length)
      expect(launch.balls.some((r) => r.item === ULTRA_BALL || r.item === 0), 'the Poké Balls pocket has no room for an Ultra Ball').toBe(true)
    },
    async () => {
      // A heal trip (the walk, the nurse and back) can fall between two wins, so each win gets 6 minutes.
      for (let n = 1; n <= BATTLES; n++) await waitPickup(WON, n, 6 * MINUTE, `"thirty won battles": won battle ${n} of ${BATTLES}`)
      const sofar = backendLog()
      const thirtieth = sofar.flatMap((l, i) => (WON.test(l) ? [i] : []))[BATTLES - 1]
      awaited = sofar.slice(0, thirtieth).filter((l) => PICKUP_TRIP.test(l)).length + 1
      await waitPickup(PICKUP_TRIP, awaited, 6 * MINUTE,`"a visit after every won one": the visit after won battle ${BATTLES}`)
      return 'cancel'
    },
    { whileIdle: (run, now) => checkItemsWindow(run.pages.admin, run.rom, takes(now)) }
  )

  // NFR-2 and NFR-1 up to the visit after the thirtieth win: one window before the first battle and one after every
  // battle, each with its count line, its takes and its visit, under the bound.
  const cut = log.flatMap((l, i) => (PICKUP_TRIP.test(l) ? [i] : []))[awaited - 1]
  const { decisions, retries } = checkDecisions(log.slice(0, cut + 1), 0, true)
  expect(decisions.length, '"a visit before the first and after every won one": the decision windows').toBeGreaterThanOrEqual(BATTLES + 1)
  expect(decisions[0].held, 'the first visit, before any battle').toBe(0)
  // Rule 16: no drop in 509 presses in the rounds, so a kind sent again more than twice is a gate on the wrong byte.
  expect(Object.entries(retries).filter(([, n]) => n > 2), 'a press kind dropped and sent again more than twice in the run').toEqual([])

  // "every take logged with its item": each a Pickup item, from a Zigzagoon.
  const all = takes(log)
  step(`${all.length} takes: ${JSON.stringify(all.map((t) => [t.slot, t.item, t.name]))}`)
  expect(all.filter((t) => !PICKUP_ITEMS.includes(t.item) || !PICKUP_SLOTS.includes(t.slot)), 'a take that is not a Pickup item from a Zigzagoon').toEqual([])

  // Between the Cancels: the party empty-handed and the bag holding every take.
  checkEmptyHanded(log, idle.party, PICKUP_SLOTS)
  checkBag(launchBag, idleBag, log)
})
