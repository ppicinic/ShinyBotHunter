// hgss-plan-pickup-switch (docs/specs/milestone-builder-rework.md, "Plans, as the owner redefined them"; #621 R8c2): a plan
// switching from a flee step to a training step with Pickup at a time boundary, and a follows card moving with it. On
// HG-PlanPickup (hgss.qa-pickup-held: Route 33 path tile (460, 472) facing Down, Typhlosion Lv 37 leading, five Aipom with
// Pickup in slots 2 to 6, each holding an item) at 09:54 on a Monday. Plan QA Pickup: rule Morning -> QA R33 flee (Route 33 ·
// grass, PhaseTarget, Run, no training), default QA R33 pickup (Route 33 · grass, PhaseTarget, Run, Standard to 100, Pickup),
// both Living dex and ticked for the file, started through Bot Control's Plan action. Each step walks to Route 33 · grass's
// generated anchor (460, 473) at its Init. The flee step's decisions visit no Pickup; the first decision past 10:00 ends its
// hunt between battles, and the pickup step's first decision takes the five items in slot order before any encounter. The
// switch takes nothing: the items are collected by the next Pickup step's decision.
// Run: `npx playwright test hgss-plan-pickup-switch`. The backend is logged into Discord in case a Typhlosion level-up asks
// about a move.
import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  ANCHOR_HUNT, assertLocalStores, cancelEventAndBot, cardWindow, clockConfig, launchHgss, PLAN_PICK, putMilestone, putPlan, readClock, rowsFor,
  RUN_FROM, saveOf, seen, shutdown, stageCopy, startFrame, START_TILE, startPlan, subscribe, teardown, WILD, type Frame, type Plan
} from './milestones'
import { ABILITY_PICKUP, parseHeld, parseTook, PICKUP_HELD, PICKUP_STOP, PICKUP_TOOK, PICKUP_TRIP, pickupHolders, readPartyRam } from './pickup'
import { dashboardState } from './rs-wild'
import { backendLog, MINUTE } from './static'

const HANDLE = 'HG-PlanPickup'
const FLEE = 'QA R33 flee'
const PICKUP = 'QA R33 pickup'
const PICKUP_SLOTS = [2, 3, 4, 5, 6]
const stepOf = (phaseName: string, extra: Record<string, unknown> = {}): Record<string, unknown> =>
  ({ eventName: WILD, phaseName, targetType: 'PhaseTarget', encounterType: 'Run', xRange: 3, ...extra })
const PLAN: Plan = {
  name: 'QA Pickup', rules: [{ times: ['Morning'], weekdays: [], arguments: stepOf(FLEE, { trainType: 'None' }) }],
  default: stepOf(PICKUP, { trainType: 'Standard', targetLevel: 100, pickup: true }), endWhenComplete: false
}
// Route 33 · grass's generated anchor (milestones.json.gz, 37-land): (460, 473), run left-right ±6.
const ANCHOR = ['37', '460', '473']
const R33_MACRO = /^M\.Right\.466\.\d+(:\S+)?;M\.Left\.454\.\d+$/
// HG Route 33 in the morning: Hoppip, Rattata, Spearow, Zubat; by day: Hoppip, Rattata, Spearow.
const R33_MORNING = ['187', '19', '21', '41']
const R33_DAY = ['187', '19', '21']
const BATTLE = /, isShiny: (True|False)\s*$/
const STATE = /\[Orchestrator\] State: (\w+)/
const fields = (pattern: RegExp, line: string): string[] => pattern.exec(line)!.slice(1)
const count = (pattern: RegExp): number => backendLog().filter((l) => pattern.test(l)).length

test('hgss-plan-pickup-switch: flee to train with Pickup at 10:00, the items taken at the Pickup step\'s first decision, the follows card moved', async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    // HG-Route32.nds is a byte copy of Pokemon HeartGold.nds, the ROM the save was made on.
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-pickup-held' })
    clockConfig('2026-11-30T09:54:00')
    const admin = await startFrame(frame)
    for (const name of [FLEE, PICKUP]) {
      expect((await putMilestone({ game: 'HGSS', name, location: '37-land', gameFiles: [HANDLE], archived: false })).status, `${name} saved`).toBe(200)
    }
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)
    expect((await subscribe({ game: 'HGSS', phase: FLEE, gameName: HANDLE, followsHunt: true })).status, 'the follows card').toBeLessThan(300)
    const follows = await cardWindow(frame.app!, `HGSS — ${HANDLE}`)
    const pages: Record<string, Page> = { admin, follows }
    const heading = follows.locator('.card-header-title')

    const card = await launchHgss(admin, HANDLE, { clock: true })
    const clock = await readClock()
    expect(clock.weekday === 1 && clock.hour === 9 && clock.minute <= 56, `the clock ${JSON.stringify(clock)} at launch is not Monday 9:56 or earlier; set QA_CLOCK_START earlier`).toBe(true)
    // The save's five Pickup members, each holding an item: those slots and items are the takes expected.
    const ram = await readPartyRam()
    step(`party RAM: ${JSON.stringify(ram)}`)
    expect(ram.filter((m) => m.ability === ABILITY_PICKUP).map((m) => m.slot), 'the save\'s Pickup members').toEqual(PICKUP_SLOTS)
    expect(pickupHolders(ram), 'the save\'s Pickup members holding an item').toEqual(PICKUP_SLOTS)
    const expected = PICKUP_SLOTS.map((slot) => ({ slot, item: ram[slot - 1].heldItem }))
    await shotAll('01-launched', pages)

    const runStart = await startPlan(card, PLAN.name)
    const anchors = (): string[][] => backendLog().filter((l) => ANCHOR_HUNT.test(l)).map((l) => fields(ANCHOR_HUNT, l))
    await expect.poll(() => anchors().length, { message: 'the steps\' anchor lines at Start', timeout: MINUTE, intervals: [500] }).toBe(2)
    expect(anchors(), 'the steps\' anchor lines at Start').toEqual([0, 1].map(() => ['Route 33 · grass', ...ANCHOR, 'run left-right ±6', 'generated']))
    expect(fields(PLAN_PICK, (await seen(PLAN_PICK, 1, MINUTE)).line), 'the morning pick').toEqual([PLAN.name, 'Monday', '9', FLEE])
    expect(fields(START_TILE, (await seen(START_TILE, 1, MINUTE)).line), 'the flee step\'s start tile').toEqual(['37', '460', '472', 'Down'])
    const from1 = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
    expect([from1[1], from1[2]], 'the first Run starts on the anchor').toEqual(ANCHOR.slice(1))
    expect(from1[3], 'Route 33\'s run line').toMatch(R33_MACRO)
    await expect(heading, 'the follows window on the flee step').toHaveText(FLEE, { timeout: 30_000 })
    await expect.poll(async () => (await dashboardState())?.name, { message: 'Bot Control\'s entry', timeout: 30_000, intervals: [1000] })
      .toBe(`Plan ${PLAN.name}: ${FLEE}`)
    await shotAll('02-flee', pages)

    // The first decision past 10:00: the flee step's hunt ends there, between battles, and the pickup step begins.
    const pick2 = await seen(PLAN_PICK, 2, 15 * MINUTE)
    expect(fields(PLAN_PICK, pick2.line), 'the pick at 10:00').toEqual([PLAN.name, 'Monday', '10', PICKUP])
    const log = backendLog()
    const pick2At = log.indexOf(pick2.line)
    const before = log.slice(0, pick2At)
    expect(before.filter((l) => STATE.test(l)).at(-1), 'the last state before the switch').toMatch(/State: DecideOverworldAction/)
    expect(before.findLastIndex((l) => /State: BattleStart/.test(l)), 'a battle in flight at the switch').toBeLessThan(before.findLastIndex((l) => /State: BattleComplete/.test(l)))
    expect(before.filter((l) => l.includes('[Pickup]')), 'a [Pickup] line on the flee step').toEqual([])
    const flee = rowsFor(HANDLE, FLEE, runStart)
    expect(flee.length, `no encounter under ${FLEE} before 10:00 (launched at 9:${String(clock.minute).padStart(2, '0')}); set QA_CLOCK_START earlier`).toBeGreaterThanOrEqual(1)
    for (const row of flee) expect(R33_MORNING, `a ${row.species} under ${FLEE} in the morning`).toContain(row.species)

    // The pickup step's Init walks to the anchor from the run line, then its first decision visits Pickup before any encounter.
    const tile2 = fields(START_TILE, (await seen(START_TILE, 2, MINUTE)).line)
    expect([tile2[0], tile2[2]], 'the pickup step starts on Route 33\'s run line').toEqual([ANCHOR[0], ANCHOR[2]])
    const held = await seen(PICKUP_HELD, 1, 3 * MINUTE)
    expect(parseHeld(held.line), 'the first [Pickup] line').toEqual({ held: 5, members: 5, take: true })
    await seen(PICKUP_TOOK, 5, MINUTE)
    const visit = backendLog().slice(pick2At)
    const lastTake = visit.findLastIndex((l) => PICKUP_TOOK.test(l))
    expect(visit.slice(0, lastTake).filter((l) => /State: (Encountering|BattleStart)/.test(l)), 'an encounter before the takes').toEqual([])
    expect(visit.map(parseTook).filter((t) => t !== null).map((t) => ({ slot: t!.slot, item: t!.item })), 'the takes, in slot order').toEqual(expected)
    expect(backendLog().filter((l) => PICKUP_STOP.test(l)), 'a Pickup stop').toEqual([])

    // Won battles under the pickup step's milestone; the follows card and the entry moved with the step.
    const battles = count(BATTLE)
    await seen(BATTLE, battles + 2, 10 * MINUTE)
    await expect(heading, 'the follows window on the pickup step').toHaveText(PICKUP, { timeout: 30_000 })
    expect((await dashboardState())?.name, 'Bot Control\'s entry').toBe(`Plan ${PLAN.name}: ${PICKUP}`)
    expect(count(PLAN_PICK), 'a third pick').toBe(2)
    await shotAll('03-pickup', pages)
    // Cancelled at a visit's end: a member may find an item in any won battle, so RAM shows nothing held only when no battle
    // has ended since the last visit's takes.
    await seen(PICKUP_TRIP, count(PICKUP_TRIP) + 1, 3 * MINUTE)
    await cancelEventAndBot(card, async () => {
      const after = await readPartyRam()
      step(`party RAM after the cancel: ${JSON.stringify(after)}`)
      expect(pickupHolders(after), 'Pickup members holding an item after the cancel').toEqual([])
    })
    const trained = rowsFor(HANDLE, PICKUP, new Date(pick2.at).toISOString())
    expect(trained.length, `two rows under ${PICKUP}`).toBeGreaterThanOrEqual(2)
    for (const row of trained) expect(R33_DAY, `a ${row.species} under ${PICKUP} by day`).toContain(row.species)
    expect(rowsFor(HANDLE, PICKUP, runStart).filter((r) => r.at < pick2.at), `a ${PICKUP} row before the switch`).toEqual([])

    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
