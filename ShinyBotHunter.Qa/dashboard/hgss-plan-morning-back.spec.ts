// hgss-plan-morning-back (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1b, plan scenario 12): C3b's "a Start just
// before a time boundary and off the anchor". Plan QA Morning: default Route 32 · grass, rule Morning -> Route 30 · grass;
// goal Hoppip 1 (Route 32) and Caterpie 1 (Route 30), on HG-Plan2, from the save's tile on Route 32, (474, 294), at 09:42 on a
// Monday. Route 32's anchor is set on the save's tile first: the walk to its generated anchor, which comes south from Violet
// City, meets a story trigger.
// `back` (the default): Route 30 in the morning, then at 10:00 back to Route 32 and its day species.
// `complete-on-return`: the goal becomes Caterpie 1 alone before 10:00, so Route 32 is complete when the 10:00 walk arrives:
// the message naming Route 30 and the walk back there, the first incomplete rule standing in (FR-34, FR-34a; #593).
// Run: `npx playwright test hgss-plan-morning-back`; `$env:QA_HGSS_VARIANT = "complete-on-return"` for the second.
import * as fs from 'fs'
import { expect, test } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  assertLocalStores, cancelEventAndBot, clockConfig, launchHgss, milestoneMessages, PLAN_NOW, PLAN_STOP, PLAN_WALK, putGoal,
  putPlan, readClock, rowsFor, RUN_FROM, saveOf, seen, setAnchorHere, shutdown, stageCopy, startFrame, startWild, teardown, variant,
  writeLeadExtension, type Clock, type Frame, type Plan
} from './milestones'
import { dashboardState } from './rs-wild'
import { backendLog, MINUTE } from './static'

const which = variant(['back', 'complete-on-return'] as const)
const HANDLE = 'HG-Plan2'
const R32 = { id: '36-land', name: 'Route 32 · grass', at: ['36', '474', '294'] }
const R30 = { id: '34-land', name: 'Route 30 · grass', at: ['34', '547', '304'] }
const HOPPIP = 187
const CATERPIE = 10
// HG Route 30 in the morning and by day: Pidgey, Caterpie, Metapod. HG Route 32 by day: Bellsprout, Rattata, Mareep, Hoppip.
const R30_MORNING_DAY = ['16', '10', '11']
const R32_DAY = ['69', '19', '179', '187']
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const PLAN: Plan = { name: 'QA Morning', default: R32.id, rules: [{ times: ['Morning'], weekdays: [], milestone: R30.id }] }
const MESSAGE = `QA Morning: Route 32 · grass is complete on ${HANDLE}. Next: Route 30 · grass.`
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const BATTLE = /, isShiny: (True|False)\s*$/
const R32_MACRO = /^M\.Up\.293\.\d+(:\S+)?;M\.Down\.295\.\d+$/
const R30_MACRO = /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/

const hhmm = (c: Clock): string => `${WEEKDAYS[c.weekday]} ${String(c.hour).padStart(2, '0')}:${String(c.minute).padStart(2, '0')}`
// The walk line's weekday, milestone and destination; the map and x it walks from (a run line's y varies); the hour.
function walkOf(line: string): { fields: string[]; from: string[]; hour: number } {
  const m = PLAN_WALK.exec(line)!
  return { fields: [m[1], m[3], m[7], m[8], m[9]], from: [m[4], m[5]], hour: Number(m[2]) }
}
const battles = (): number => backendLog().filter((l) => BATTLE.test(l)).length
/** The Run lines logged after the n-th (1-based) `Plan: now hunting` line. */
function runsAfterSwitch(n: number): RegExpExecArray[] {
  const log = backendLog()
  const at = log.flatMap((l, i) => (PLAN_NOW.test(l) ? [i] : []))[n - 1]
  return log.slice(at).filter((l) => RUN_FROM.test(l)).map((l) => RUN_FROM.exec(l)!)
}

test(`hgss-plan-morning-back ${which}: Route 30 in the morning, back at 10:00`, async () => {
  test.setTimeout(45 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-30T09:42:00')
    const admin = await startFrame(frame)
    const pages = { admin }
    expect((await putGoal(HANDLE, [{ species: HOPPIP, amount: 1 }, { species: CATERPIE, amount: 1 }])).status, 'PUT goal').toBe(204)
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)

    const card = await launchHgss(admin, HANDLE, { clock: true })
    const launchClock = await readClock()
    // The walk to Route 30 and three encounters there take several minutes of the morning.
    const left = 10 * 60 - (launchClock.hour * 60 + launchClock.minute)
    expect(launchClock.hour >= 4 && left >= 8, `${hhmm(launchClock)} at launch leaves under 8 minutes of morning; set QA_CLOCK_START earlier`).toBe(true)
    await writeLeadExtension({ hp: 30 })
    const set = await setAnchorHere(0, R32.id, 0, 1)
    expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    await shotAll('01-launched', pages)

    const runStart = await startWild(card, { plan: PLAN.name, mode: 'Run', yRange: 1 })
    await seen(PLAN_STOP, 2, MINUTE)
    // The morning pick is not the default, so the first decision switches before the walk, which is logged under Route 30.
    const now1 = await seen(PLAN_NOW, 1, 2 * MINUTE)
    expect(PLAN_NOW.exec(now1.line)![1], 'the morning pick').toBe(R30.name)
    const walk1 = walkOf((await seen(PLAN_WALK, 1, MINUTE)).line)
    expect(walk1.fields, 'the first walk').toEqual([WEEKDAYS[launchClock.weekday], R30.name, ...R30.at])
    expect(walk1.from, 'the first walk from the save\'s tile').toEqual(R32.at.slice(0, 2))
    expect(walk1.hour >= 4 && walk1.hour < 10, `the first walk's hour ${walk1.hour} is not in the morning`).toBe(true)
    const arrived1 = await seen(ARRIVED, 1, 10 * MINUTE)
    const battlesAtR30 = battles()
    const from1 = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
    expect([from1[1], from1[2]], 'the first Run after the walk starts on Route 30\'s anchor').toEqual(['547', '304'])
    expect(from1[3], 'Route 30\'s run line').toMatch(R30_MACRO)
    const arrived1Iso = new Date(arrived1.at).toISOString()

    if (which === 'complete-on-return') {
      // Before 10:00 Route 32 loses its target; the hunt does not read it until the 10:00 walk's switch.
      expect(backendLog().filter((l) => PLAN_WALK.test(l)), `10:00 came before the goal change (launched at ${hhmm(launchClock)}); set QA_CLOCK_START earlier`)
        .toHaveLength(1)
      expect((await putGoal(HANDLE, [{ species: CATERPIE, amount: 1 }])).status, 'Caterpie 1 alone').toBe(204)
    }
    await seen(BATTLE, battlesAtR30 + 3, 10 * MINUTE)
    await shotAll('02-route30-morning', pages)

    // 10:00: the default again, walked to from Route 30 and switched to on arrival.
    const walk2At = await seen(PLAN_WALK, 2, 20 * MINUTE)
    const walk2 = walkOf(walk2At.line)
    const morning = rowsFor(HANDLE, R30.id, arrived1Iso).filter((r) => r.at < walk2At.at)
    if (which === 'back') {
      expect(morning.length, `${morning.length} encounters at Route 30's anchor before 10:00 (launched at ${hhmm(launchClock)}); set QA_CLOCK_START earlier`)
        .toBeGreaterThanOrEqual(3)
    }
    for (const row of morning) expect(R30_MORNING_DAY, `a ${row.species} at Route 30's anchor in the morning`).toContain(row.species)
    expect(walk2.fields, 'the walk at 10:00').toEqual([WEEKDAYS[launchClock.weekday], R32.name, ...R32.at])
    expect(walk2.from, 'the walk at 10:00 from Route 30\'s run line').toEqual(R30.at.slice(0, 2))
    expect(walk2.hour, 'the walk\'s hour').toBe(10)
    const now2 = await seen(PLAN_NOW, 2, 15 * MINUTE)
    expect(PLAN_NOW.exec(now2.line)![1], 'the switch on arrival at Route 32').toBe(R32.name)

    if (which === 'back') {
      const battlesAtR32 = battles()
      await seen(BATTLE, battlesAtR32 + 3, 10 * MINUTE)
      const runs = runsAfterSwitch(2)
      expect([runs[0][1], runs[0][2]], 'the first Run after the switch starts on Route 32\'s anchor').toEqual(R32.at.slice(1))
      for (const r of runs) expect(r[3], 'a Run line not on Route 32\'s line').toMatch(R32_MACRO)
      await shotAll('03-route32-day', pages)
      await cancelEventAndBot(card)
      const day = rowsFor(HANDLE, R32.id, new Date(now2.at).toISOString())
      expect(day.length, 'three rows under 36-land from Route 32\'s anchor').toBeGreaterThanOrEqual(3)
      for (const row of day) expect(R32_DAY, `a ${row.species} at Route 32's anchor by day`).toContain(row.species)
    } else {
      // The switch's read finds Route 32 complete: one message naming Route 30, the walk back logged under Route 32, the
      // switch there, and Route 30's day species from its anchor. The hunt is not complete.
      const walk3 = walkOf((await seen(PLAN_WALK, 3, 2 * MINUTE)).line)
      expect(walk3.fields, 'the walk back to the first incomplete rule').toEqual([WEEKDAYS[launchClock.weekday], R30.name, ...R30.at])
      expect(walk3.from, 'the walk back from Route 32\'s anchor').toEqual(R32.at.slice(0, 2))
      expect(walk3.hour, 'the walk back\'s hour').toBe(10)
      await expect.poll(async () => (await milestoneMessages(runStart)).filter((m) => m.endsWith(MESSAGE)).length,
        { message: `the message "${MESSAGE}"`, timeout: 3 * MINUTE, intervals: [5000] }).toBe(1)
      const now3 = await seen(PLAN_NOW, 3, 15 * MINUTE)
      expect(PLAN_NOW.exec(now3.line)![1], 'the switch on arrival back at Route 30').toBe(R30.name)
      const battlesBack = battles()
      await seen(BATTLE, battlesBack + 3, 10 * MINUTE)
      expect((await dashboardState())?.eventState, 'the hunt ended though Route 30 is incomplete').not.toBe('Complete')
      const runs = runsAfterSwitch(3)
      expect([runs[0][1], runs[0][2]], 'the first Run after the switch back starts on Route 30\'s anchor').toEqual(['547', '304'])
      for (const r of runs) expect(r[3], 'a Run line not on Route 30\'s line').toMatch(R30_MACRO)
      await shotAll('03-route30-day', pages)
      await cancelEventAndBot(card)
      const back = rowsFor(HANDLE, R30.id, new Date(now3.at).toISOString())
      expect(back.length, 'three rows under 34-land after the walk back').toBeGreaterThanOrEqual(3)
      for (const row of back) expect(R30_MORNING_DAY, `a ${row.species} at Route 30's anchor by day`).toContain(row.species)
      expect(await milestoneMessages(runStart), 'one milestone message in all').toHaveLength(1)
    }

    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
