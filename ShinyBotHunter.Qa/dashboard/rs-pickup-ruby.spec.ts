// rs-pickup-ruby (docs/specs/rs-pickup.md, QA): rs-pickup-take on RU-Wild, then ten won battles, each followed by a visit;
// Cancel after the visit that follows the tenth. Ruby's own start-menu task and CB2_Overworld gate every press, so on a
// wrong address every gate goes stale. The spec sets QA_RS_ROM to RU-Wild for its own run, so the plan's run line (#490,
// Q1b) needs no variable: `npx playwright test rs-pickup-ruby`.
import { expect, test } from '@playwright/test'
import { STATE, writeSlot } from './rs-training'
import { backendLog, MINUTE } from './static'
import {
  checkBag, checkDecisions, checkEmptyHanded, MENU_ENTRIES, PICKUP_SLOTS, PICKUP_TRIP, pickupRun, takes, visit, waitPickup, WON
} from './rs-pickup'

const ROM = 'RU-Wild'
const BATTLES = 10
const ITEMS = [22, 23, 2, 68, 19]
const NAMES = ['Super Potion', 'Full Heal', 'Ultra Ball', 'Rare Candy', 'Full Restore']

test('RS Pickup rs-pickup-ruby: on Ruby, the first visit takes all five in slot order, then ten won battles each followed by a visit', async () => {
  test.setTimeout(20 * MINUTE)
  // trainingRun reads QA_RS_ROM when it starts; put back what the shell had after the run.
  const shell = process.env.QA_RS_ROM
  process.env.QA_RS_ROM = ROM
  try {
    // The trip line awaited before the Cancel, counted from the first.
    let awaited = 0
    const { log, idle, launchBag, idleBag } = await pickupRun(
      async () => {
        for (let i = 0; i < PICKUP_SLOTS.length; i++) await writeSlot(PICKUP_SLOTS[i], { item: ITEMS[i] })
      },
      async (run) => {
        expect(run.rom, 'the run\'s ROM').toBe(ROM)
        await waitPickup(PICKUP_TRIP, 1, 2 * MINUTE, '"the first visit, before any battle"')
        for (let n = 1; n <= BATTLES; n++) await waitPickup(WON, n, 6 * MINUTE, `"ten won battles": won battle ${n} of ${BATTLES}`)
        const sofar = backendLog()
        const tenth = sofar.flatMap((l, i) => (WON.test(l) ? [i] : []))[BATTLES - 1]
        awaited = sofar.slice(0, tenth).filter((l) => PICKUP_TRIP.test(l)).length + 1
        await waitPickup(PICKUP_TRIP, awaited, 6 * MINUTE, `the visit after won battle ${BATTLES}`)
        return 'cancel'
      }
    )

    // rs-pickup-take's checks on visit 1: before any battle, five takes in slot order, each menu [0,1,2,3] with ITEM at 2.
    const v = visit(log, 1)
    const trip = log.indexOf(v.at(-1)!)
    expect(log.slice(0, trip).filter((l) => /^(Encountering|BattleStart)$/.test(STATE.exec(l)?.[1] ?? '')), '"the first visit, before any battle"').toEqual([])
    expect(takes(v).map((t) => [t.slot, t.item, t.name]), '"five takes in slot order"').toEqual(PICKUP_SLOTS.map((slot, i) => [slot, ITEMS[i], NAMES[i]]))
    expect(v.map((l) => MENU_ENTRIES.exec(l)).filter((m) => m !== null).map((m) => [m![1], m![3], m![4]]), 'each slot\'s menu entries')
      .toEqual(PICKUP_SLOTS.map((slot) => [String(slot), '0,1,2,3', '2']))

    // Every decision up to the visit after the tenth win: the count line, the takes, the presses (START, Down 0 -> 1 on
    // the first visit only, ...) with the field free at the end, and NFR-1 (under 15 s for the five).
    const cut = log.flatMap((l, i) => (PICKUP_TRIP.test(l) ? [i] : []))[awaited - 1]
    const { decisions } = checkDecisions(log.slice(0, cut + 1), 0, true)
    expect(decisions.length, '"each followed by a visit": the decision windows').toBeGreaterThanOrEqual(BATTLES + 1)
    expect(decisions[0].held, 'the first visit\'s count line').toBe(5)

    // Between the Cancels: the party empty-handed and the bag holding every take.
    checkEmptyHanded(log, idle.party, PICKUP_SLOTS)
    checkBag(launchBag, idleBag, log)
  } finally {
    if (shell === undefined) delete process.env.QA_RS_ROM
    else process.env.QA_RS_ROM = shell
  }
})
