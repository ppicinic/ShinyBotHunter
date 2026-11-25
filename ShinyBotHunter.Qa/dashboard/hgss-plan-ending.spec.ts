// hgss-plan-ending (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1b, plan scenario 9): FR-34 as rewritten
// 2026-11-25 (#583) and FR-34a's "none for a milestone complete at Start". Plan QA Ending: default Route 32 · grass, rule
// Night -> Route 30 · grass, started at noon on a Sunday from the save's tile on Route 32, (474, 294).
// `default-complete` (the default): goal Caterpie 1 (in HG Route 30's day table, not in Route 32's), so Route 32 is complete
// at Start and Route 30 is not: the first incomplete rule stands in for the complete default, whatever its times.
// `all-complete`: goal [], so both are complete: Complete at the first decision, no walk, no message.
// Run: `npx playwright test hgss-plan-ending`; `$env:QA_HGSS_VARIANT = "all-complete"` for the second.
import * as fs from 'fs'
import { expect, test } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  assertLocalStores, cancelEventAndBot, CLEARING, clockConfig, launchHgss, milestoneMessages, PLAN_NOW, PLAN_STOP, PLAN_WALK,
  putGoal, putPlan, readClock, rowsFor, RUN_FROM, saveOf, seen, shutdown, stageCopy, startFrame, startWild, teardown, variant,
  writeLeadExtension, type Frame, type Plan
} from './milestones'
import { dashboardState } from './rs-wild'
import { backendLog, MINUTE } from './static'

const which = variant(['default-complete', 'all-complete'] as const)
const HANDLE = 'HG-Ending'
const R32 = { id: '36-land', name: 'Route 32 · grass', at: ['36', '458', '339'] }
const R30 = { id: '34-land', name: 'Route 30 · grass', at: ['34', '547', '304'] }
const CATERPIE = 10
// HG Route 30 by day: Pidgey, Caterpie, Metapod.
const R30_DAY = ['16', '10', '11']
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const PLAN: Plan = { name: 'QA Ending', default: R32.id, rules: [{ times: ['Night'], weekdays: [], milestone: R30.id }] }
// NavigatorEvent.FinishReturn after a walk, and the HGSS battle event's line for each wild Pokémon it reads.
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const BATTLE = /, isShiny: (True|False)\s*$/
// Route 30's anchor, run up-down ±6: RunEncounterEvent.RunMacro from (547, 304).
const R30_MACRO = /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/

test(`hgss-plan-ending ${which}: a complete default gives way to the first incomplete rule; all complete ends at once`, async () => {
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  // Before the try, so the teardown never closes an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-29T12:00:00')
    const admin = await startFrame(frame)
    const pages = { admin }
    const goal = which === 'default-complete' ? [{ species: CATERPIE, amount: 1 }] : []
    expect((await putGoal(HANDLE, goal)).status, 'PUT goal').toBe(204)
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)

    const card = await launchHgss(admin, HANDLE, { clock: true })
    // The rule is Night only, so the whole run must stay in the day (10:00 to 20:00).
    const clock = await readClock()
    expect(clock.hour >= 10 && clock.hour < 19, `the launch clock ${JSON.stringify(clock)} is not between 10:00 and 19:00; check QA_CLOCK_START`).toBe(true)
    await writeLeadExtension({ hp: 30 })
    await shotAll('01-launched', pages)

    const runStart = await startWild(card, { plan: PLAN.name, mode: 'Run', yRange: 1 })
    // One line per stop at Start, the default first.
    await seen(PLAN_STOP, 2, MINUTE)
    const stops = backendLog().flatMap((l) => PLAN_STOP.exec(l) ?? []).map((m) => [m[1], m[2], m[3], m[4], m[5]])
    expect(stops, 'the plan\'s stops at Start').toEqual([[PLAN.name, R32.name, ...R32.at], [PLAN.name, R30.name, ...R30.at]])

    if (which === 'default-complete') {
      // The first decision switches to Route 30 before any walk (the pick is not the default), then walks from it.
      const now = await seen(PLAN_NOW, 1, 2 * MINUTE)
      expect(PLAN_NOW.exec(now.line)![1], '"the first incomplete rule stands in": the milestone switched to').toBe(R30.name)
      const walk = PLAN_WALK.exec((await seen(PLAN_WALK, 1, MINUTE)).line)!
      const hour = Number(walk[2])
      expect([walk[1], walk[3], walk[4], walk[5], walk[6], walk[7], walk[8], walk[9]], 'the walk line, from the save\'s tile')
        .toEqual([WEEKDAYS[clock.weekday], R30.name, '36', '474', '294', ...R30.at])
      expect(hour >= 10 && hour < 20, `the walk line's hour ${hour} is not in the day`).toBe(true)
      const log = backendLog()
      expect(log.findIndex((l) => PLAN_NOW.test(l)), 'the switch before the walk').toBeLessThan(log.findIndex((l) => PLAN_WALK.test(l)))

      // Arrival: the walk's end, then the first Run line from the anchor with Route 30's line.
      const arrived = await seen(ARRIVED, 1, 10 * MINUTE)
      const battlesAtArrival = backendLog().filter((l) => BATTLE.test(l)).length
      const from = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
      expect([from[1], from[2]], 'the first Run after the walk starts on the anchor').toEqual(['547', '304'])
      expect(from[3], 'Route 30\'s run line').toMatch(R30_MACRO)

      // Three encounters from the anchor, logged under Route 30, each of HG Route 30's day species.
      await seen(BATTLE, battlesAtArrival + 3, 10 * MINUTE)
      const arrivedIso = new Date(arrived.at).toISOString()
      await expect.poll(() => rowsFor(HANDLE, R30.id, arrivedIso).length, { message: 'three encounters rows under 34-land', timeout: 30_000, intervals: [2000] })
        .toBeGreaterThanOrEqual(3)
      for (const row of rowsFor(HANDLE, R30.id, arrivedIso)) expect(R30_DAY, `a ${row.species} under 34-land from Route 30's anchor by day`).toContain(row.species)
      for (const l of backendLog().filter((x) => RUN_FROM.test(x))) expect(RUN_FROM.exec(l)![3], `a Run line not on Route 30's line: ${l}`).toMatch(R30_MACRO)
      await shotAll('02-hunting-route30', pages)
    } else {
      // Nothing is left at the first decision: the walk home from the start tile, then Complete.
      await expect.poll(async () => (await dashboardState())?.eventState, { message: 'the event is not Complete', timeout: 3 * MINUTE, intervals: [2000] })
        .toBe('Complete')
      const log = backendLog()
      expect(log.filter((l) => PLAN_WALK.test(l) || PLAN_NOW.test(l)), '"no walk": a plan walk or switch line').toEqual([])
      expect(log.flatMap((l) => CLEARING.exec(l)?.[1] ?? []), '"Complete": only the walk home, from the start tile').toEqual(['already at start'])
      for (const stop of [R32, R30]) expect(rowsFor(HANDLE, stop.id, runStart), `an encounter under ${stop.id}`).toEqual([])
      await shotAll('02-complete', pages)
    }

    await cancelEventAndBot(card)
    // FR-34a: no message for a milestone complete at Start, read after the cancel so a late send has had time to land.
    expect(await milestoneMessages(runStart), '"none for a milestone complete at Start"').toEqual([])
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
