// rs-pickup-bag-full (docs/specs/rs-pickup.md, QA; FR-6, FR-9): the Items pocket's empty slots filled in RAM, a Revive in
// slot 2's hand (not the spec's PP Up: round 2 found one already in the save's bag, where a held one stacks) and Orange
// Mail in slot 5's, all with the bot idle. The first visit dismisses the take message, leaves the mail unvisited and
// stops with the still-holds sentence; in Waiting the scenario puts the pocket back and takes the mail away, then Resume,
// and the next visit takes the Revive. The plan's run line (#490, Q1a): `npx playwright test rs-pickup-bag-full`.
import { expect, test } from '@playwright/test'
import { step } from './driver'
import { dashboardState, OVER } from './rs-wild'
import { readSlot, writeSlot } from './rs-training'
import { API, MINUTE } from './static'
import {
  checkBag, checkEmptyHanded, checkVisit, ITEMS_POCKET, MAIL, PICKUP_HELD, PICKUP_ITEMS, PICKUP_SLOTS, PICKUP_STOP,
  PICKUP_TRIP, pickupRun, readPocket, STILL_HOLDS_AFTER_TAKE, STILL_HOLDS_STOP, takes, visit, waitPickup, writePocket, type BagSlot
} from './rs-pickup'

const REVIVE = 24
const ORANGE_MAIL = 121
// The stop names the first Pickup member still holding, in slot order: slot 2's Revive before slot 5's mail.
const STOP = /\[Pickup\] Pickup: (.+?) still holds Revive after the take; check the bag and the Pok.{1,2}mon menu\.\s*$/

test('RS Pickup rs-pickup-bag-full: a full Items pocket stops on the Revive, mail not visited; Resume after the pocket is cleared takes it', async () => {
  test.setTimeout(12 * MINUTE)
  let filled: BagSlot[] = []
  const { log, idle, launchBag, idleBag } = await pickupRun(
    async (launch) => {
      const present = launch.items.map((r) => r.item).filter((item) => item !== 0)
      expect(present, 'the Items pocket already holds a Revive: a held one would stack and the take succeed').not.toContain(REVIVE)
      // Distinct fillers at quantity 1: Potion (13) and up, never an item present or one of the Pickup ten.
      const fillers = Array.from({ length: 39 }, (_, i) => 13 + i).filter((id) => !present.includes(id) && !PICKUP_ITEMS.includes(id))
      filled = launch.items.map((r) => (r.item !== 0 ? r : { item: fillers.shift()!, quantity: 1 }))
      await writePocket(ITEMS_POCKET, filled)
      await writeSlot(2, { item: REVIVE })
      await writeSlot(5, { item: ORANGE_MAIL })
    },
    async (run, launch) => {
      await waitPickup(STOP, 1, 2 * MINUTE, '"the still-holds stop with the item\'s name"', STOP)
      // Park clears the macro and logs the stop before it swaps to Waiting, so with both the bot is not mid-visit.
      await expect.poll(async () => (await dashboardState())?.eventState, { message: '"the bot Waiting"', timeout: MINUTE, intervals: [1000] })
        .toBe('Waiting')
      await expect(run.card.getByText('Resume', { exact: true }), '"the bot Waiting": no Resume on the card').toBeVisible({ timeout: MINUTE })
      const tally = (await (await fetch(`${API}/bot/getPickupItems`)).json()) as { gameFileName: string }[]
      step(`GET getPickupItems before Resume: ${JSON.stringify(tally)}`)
      expect(tally.map((t) => t.gameFileName), 'a take recorded before Resume').not.toContain(run.rom)

      // In Waiting: the Revive and the mail still held and the pocket as filled; then the pocket put back and the mail taken away.
      expect([(await readSlot(2)).item, (await readSlot(5)).item], 'slots 2 and 5 in Waiting').toEqual([REVIVE, ORANGE_MAIL])
      expect(await readPocket(ITEMS_POCKET, 20), 'the Items pocket in Waiting').toEqual(filled)
      await writePocket(ITEMS_POCKET, launch.items)
      await writeSlot(5, { item: 0 })

      await run.card.getByRole('button', { name: 'Run', exact: true }).click()
      step('Resume run')
      await waitPickup(PICKUP_TRIP, 2, 2 * MINUTE, '"after the scenario clears the pocket and presses Resume, the next visit takes it"', STOP)
      return 'cancel'
    },
    { expectedStop: STOP }
  )

  // Visit 1: Revive and mail both counted; slot 2 through A at its message (back on cursor 1), then the still-holds line;
  // the mail holder not visited; B and B to the field free; no take.
  const v1 = visit(log, 1)
  expect(PICKUP_HELD.exec(v1.find((l) => PICKUP_HELD.test(l))!)!.slice(1), 'visit 1\'s count line').toEqual(['2', '5', 'take'])
  expect(v1.filter((l) => MAIL.test(l)).map((l) => MAIL.exec(l)![1]), '"mail not visited"').toEqual(['5'])
  checkVisit(v1, { first: true, slots: [{ slot: 2, item: 2 }] }, '"the take message dismissed"')
  const still = v1.map((l) => STILL_HOLDS_AFTER_TAKE.exec(l)).filter((m) => m !== null)
  expect(still.map((m) => [m![1], m![3], m![4]]), 'the still-holds line after TAKE').toEqual([['2', 'Revive', String(REVIVE)]])
  expect(takes(v1), 'a take in visit 1').toEqual([])

  // The one stop, after visit 1's trip line.
  const stops = log.filter((l) => PICKUP_STOP.test(l))
  expect(stops.length, '"the still-holds stop": [Pickup] stop lines').toBe(1)
  expect(STILL_HOLDS_STOP.exec(stops[0])?.[2], 'the stop\'s item').toBe('Revive')
  expect(log.indexOf(stops[0]), 'the stop after visit 1').toBeGreaterThan(log.indexOf(v1.at(-1)!))

  // Visit 2, the decision after Resume, before any battle: the Revive alone, no start-menu Down, taken.
  const v2 = visit(log, 2)
  expect(log.slice(log.indexOf(stops[0]), log.indexOf(v2.at(-1)!)).filter((l) => OVER.test(l)), 'a battle between Resume and visit 2').toEqual([])
  expect(PICKUP_HELD.exec(v2.find((l) => PICKUP_HELD.test(l))!)!.slice(1), 'visit 2\'s count line').toEqual(['1', '5', 'take'])
  checkVisit(v2, { first: false, slots: [{ slot: 2, item: 2 }] }, '"the next visit takes it"')
  expect(takes(v2).map((t) => [t.slot, t.item, t.name]), '"the next visit takes it"').toEqual([[2, REVIVE, 'Revive']])

  // Between the Cancels: slots 2 and 5 empty-handed, and the Revive in the bag against launch.
  checkEmptyHanded(log, idle.party, PICKUP_SLOTS)
  checkBag(launchBag, idleBag, log)
})
