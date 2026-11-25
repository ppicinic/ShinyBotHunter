// hgss-plan-night (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1b, plan scenario 11): FR-33, FR-36, FR-38 on a
// plan walk, FR-38d, FR-39, FR-39a, FR-39b, FR-40, FR-34a's "names the next milestone", and the C2b, C2c, C3b and C4a items.
// Plan QA Night: default Route 32 · grass, rule Night -> Route 30 · grass; goal Hoppip 1 (Route 32 only) and Spinarak 1
// (Route 30 only, at night), on HG-Plan, from the save's tile on Route 32, (474, 294).
// `night` (the default), 19:48 on a Sunday: a 2-encounter hunt at the custom Route 33 where the bot stands, then the plan:
// Route 32 until 20:00 with the plan in use, Route 32 made complete and the follows window covered, then the walk to Route 30
// with the covered window painting it, the milestone message at the next decision and Route 30's night species.
// `start-on-rule`, 21:00: the first pick is Route 30 before any walk.
// Run: `npx playwright test hgss-plan-night`; `$env:QA_HGSS_VARIANT = "start-on-rule"` for the second. A launch that leaves
// too little day: `$env:QA_CLOCK_START = "2026-11-29T19:40:00"`.
import * as fs from 'fs'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step, windowByTitle } from './driver'
import {
  assertLocalStores, cancelEventAndBot, capture, clockConfig, cover, deletePlan, launchHgss, milestoneMessages, PLAN_NOW,
  PLAN_STOP, PLAN_WALK, putGoal, putPlan, readClock, renamePlan, rows, rowsFor, RUN_FROM, saveOf, seen, shutdown, stageCopy,
  startFrame, startWild, subscribe, teardown, uncover, unsubscribe, variant, WILD, windowInfo, writeLeadExtension,
  type Clock, type Frame, type Plan, type Subscription
} from './milestones'
import { backendLog, MINUTE } from './static'

const which = variant(['night', 'start-on-rule'] as const)
const HANDLE = 'HG-Plan'
// An existing QA file's name, used only for a second card; nothing hunts on it.
const OTHER_FILE = 'HG-Route32'
const R32 = { id: '36-land', name: 'Route 32 · grass', at: ['36', '458', '339'] }
const R30 = { id: '34-land', name: 'Route 30 · grass', at: ['34', '547', '304'] }
const R29 = '33-land'
const ROUTE33 = 'Route 33'
const HOPPIP = 187
const SPINARAK = 167
// HG Route 30 at night: Spinarak, Rattata, Hoothoot.
const R30_NIGHT = ['167', '19', '163']
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const PLAN: Plan = { name: 'QA Night', default: R32.id, rules: [{ times: ['Night'], weekdays: [], milestone: R30.id }] }
const IN_USE = 'QA Night is in use by the hunt on bot #0; cancel that hunt first.'
const MESSAGE = `QA Night: Route 32 · grass is complete on ${HANDLE}. Next: Route 30 · grass.`
const FOLLOWS: Subscription = { game: 'HGSS', phase: R32.id, gameName: HANDLE, followsHunt: true }
const FIXED: Subscription = { game: 'HGSS', phase: R30.id, gameName: HANDLE }
const FIXED_OTHER: Subscription = { game: 'HGSS', phase: R30.id, gameName: OTHER_FILE }
// windowContracts.ts phaseWindowTitle.
const FOLLOWS_TITLE = `HGSS — ${HANDLE}`
const FIXED_TITLE = `HGSS — ${R30.id} (${HANDLE})`
const OTHER_TITLE = `HGSS — ${R30.id} (${OTHER_FILE})`
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const BATTLE = /, isShiny: (True|False)\s*$/
const WARP_ANY = /\[Navigator\] Warp to map (\d+(?:\/\d+)*): on map (\d+), (.*)$/
// The anchors' run lines: Route 32 up-down ±7 from (458, 339), Route 30 up-down ±6 from (547, 304).
const R32_MACRO = /^M\.Up\.332\.\d+(:\S+)?;M\.Down\.346\.\d+$/
const R30_MACRO = /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/

const hhmm = (c: Clock): string => `${WEEKDAYS[c.weekday]} ${String(c.hour).padStart(2, '0')}:${String(c.minute).padStart(2, '0')}`
const heading = (page: Page): Locator => page.locator('.card-header-title')
const timeOrigin = (page: Page): Promise<number> => page.evaluate(() => performance.timeOrigin)
/** What a fixed card counts: the encounters here of every row of the milestone on the file (GET milestone). */
async function countedHere(file: string): Promise<number> {
  const res = await rows('HGSS', R30.id, file)
  expect(res.status, `GET milestone for ${file}`).toBe(200)
  return (res.json.rows as { encountersHere: number }[]).reduce((sum, r) => sum + r.encountersHere, 0)
}
/** The walk lines' fields after the hour, and the hour. */
function walkOf(line: string): { fields: string[]; hour: number } {
  const m = PLAN_WALK.exec(line)!
  return { fields: [m[1], m[3], m[4], m[5], m[6], m[7]], hour: Number(m[2]) }
}

test(`hgss-plan-night ${which}: three milestones in one follows window, the switch at 20:00, the plan in use, the message`, async () => {
  test.setTimeout(45 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig(which === 'night' ? '2026-11-29T19:48:00' : '2026-11-29T21:00:00')
    const admin = await startFrame(frame)
    const app = frame.app!
    expect((await putGoal(HANDLE, [{ species: HOPPIP, amount: 1 }, { species: SPINARAK, amount: 1 }])).status, 'PUT goal').toBe(204)
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)
    const subscriptions = which === 'night' ? [FOLLOWS, FIXED, FIXED_OTHER] : [FOLLOWS]
    for (const s of subscriptions) expect((await subscribe(s)).status, `subscribe ${JSON.stringify(s)}`).toBe(200)
    const follows = await windowByTitle(app, FOLLOWS_TITLE, 60_000)
    const pages: Record<string, Page> = { admin, follows }
    if (which === 'night') {
      pages.fixed = await windowByTitle(app, FIXED_TITLE, 60_000)
      pages.other = await windowByTitle(app, OTHER_TITLE, 60_000)
    }
    const here = which === 'night' ? { plan: await countedHere(HANDLE), other: await countedHere(OTHER_FILE) } : null

    const card = await launchHgss(admin, HANDLE, { clock: true })
    const launchClock = await readClock()
    await writeLeadExtension({ hp: 30 })
    await shotAll('01-launched', pages)

    if (which === 'night') {
      // (a) Two encounters at the custom Route 33, a label where the bot stands: the follows window shows it (milestone 1).
      const aStart = await startWild(card, { phase: ROUTE33, target: 'ShinyTarget', mode: 'Spin' })
      await seen(BATTLE, 2, 5 * MINUTE)
      await expect.poll(() => rowsFor(HANDLE, ROUTE33, aStart).length, { message: 'two rows under Route 33', timeout: 30_000, intervals: [2000] })
        .toBeGreaterThanOrEqual(2)
      await expect(heading(follows), 'milestone 1 in the follows window').toHaveText(ROUTE33, { timeout: 30_000 })
      const first = await windowInfo(app, FOLLOWS_TITLE)
      const origin = await timeOrigin(follows)
      await shotAll('02-route33', pages)
      // The event alone, so the bot stays launched on the save's tile.
      await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
      await card.getByRole('button', { name: 'Cancel', exact: true }).click()
      await card.getByRole('button', { name: 'Yes', exact: true }).click()
      await expect(card.getByRole('button', { name: WILD, exact: true })).toBeVisible({ timeout: MINUTE })
      step('Route 33 event cancelled')

      const runClock = await readClock()
      const left = 20 * 60 - (runClock.hour * 60 + runClock.minute)
      expect(runClock.hour < 20 && left >= 4, `${hhmm(runClock)} at Run (launched at ${hhmm(launchClock)}) leaves under 4 minutes of day; set QA_CLOCK_START earlier`).toBe(true)

      // (b) The plan: the walk to Route 32's anchor, then two encounters there before 20:00 (checked at the switch below).
      const runStart = await startWild(card, { plan: PLAN.name, mode: 'Run', yRange: 1 })
      await seen(PLAN_STOP, 2, MINUTE)
      const walk1 = walkOf((await seen(PLAN_WALK, 1, 2 * MINUTE)).line)
      expect(walk1.fields, 'the first walk').toEqual([WEEKDAYS[runClock.weekday], R32.name, R32.id, ...R32.at])
      expect(walk1.hour >= 10 && walk1.hour < 20, `the first walk's hour ${walk1.hour} is not in the day`).toBe(true)
      const arrived1 = await seen(ARRIVED, 1, 10 * MINUTE)
      const battlesAtR32 = backendLog().filter((l) => BATTLE.test(l)).length
      const from1 = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
      expect([from1[1], from1[2]], 'the first Run after the walk starts on Route 32\'s anchor').toEqual(['458', '339'])
      expect(from1[3], 'Route 32\'s run line').toMatch(R32_MACRO)
      await seen(BATTLE, battlesAtR32 + 2, 8 * MINUTE)
      await expect(heading(follows), 'milestone 2 in the follows window').toHaveText(R32.name, { timeout: 30_000 })
      expect(await windowInfo(app, FOLLOWS_TITLE), 'the follows window after the first move (FR-39: the same window)').toEqual(first)
      expect(await timeOrigin(follows), 'the follows window navigated').toBe(origin)
      await shotAll('03-route32', pages)

      // (c) The plan in use: no delete, no rename; an edit saves and does not change the running hunt.
      const del = await deletePlan('HGSS', PLAN.name)
      expect([del.status, del.text], 'DELETE plan while in use').toEqual([400, IN_USE])
      const ren = await renamePlan('HGSS', PLAN.name, 'QA Night 2')
      expect([ren.status, ren.text], 'rename while in use').toEqual([400, IN_USE])
      expect((await putPlan('HGSS', { ...PLAN, rules: [{ times: ['Night'], weekdays: [], milestone: R29 }] })).status, 'the edit to Route 29').toBe(204)
      // (d) Route 32 has no target left; the plan reads completion again only at a switch, a catch check or a no-pick.
      expect((await putGoal(HANDLE, [{ species: HOPPIP, amount: 0 }, { species: SPINARAK, amount: 1 }])).status, 'Hoppip 0').toBe(204)
      // (e) The follows window under Admin, on top.
      await cover(app, FOLLOWS_TITLE)

      // (f) 20:00: the walk to Route 30 (the plan as read at Start, not the edited Route 29), the door, the switch on arrival.
      const walk2At = await seen(PLAN_WALK, 2, 20 * MINUTE)
      const walk2 = walkOf(walk2At.line)
      const before20 = rowsFor(HANDLE, R32.id, new Date(arrived1.at).toISOString()).filter((r) => r.at < walk2At.at)
      expect(before20.length, `${before20.length} encounters at Route 32's anchor before 20:00 (Run at ${hhmm(runClock)}, launched at ${hhmm(launchClock)}); set QA_CLOCK_START earlier`)
        .toBeGreaterThanOrEqual(2)
      expect(walk2.fields, 'the walk at 20:00').toEqual([WEEKDAYS[runClock.weekday], R30.name, R32.id, ...R30.at])
      expect(walk2.hour, 'the walk\'s hour').toBe(20)
      const now = await seen(PLAN_NOW, 1, 15 * MINUTE)
      expect(PLAN_NOW.exec(now.line)![1], 'the switch on arrival').toBe(R30.name)
      const battlesAtR30 = backendLog().filter((l) => BATTLE.test(l)).length
      const log = backendLog()
      const walk2Index = log.flatMap((l, i) => (PLAN_WALK.test(l) ? [i] : []))[1]
      const walkLines = log.slice(walk2Index, log.findIndex((l) => PLAN_NOW.test(l)))
      const warps = walkLines.filter((l) => WARP_ANY.test(l))
      expect(warps.length, '"the door crossed": no warp wait on the walk to Route 30').toBeGreaterThan(0)
      warps.forEach((line, i) => {
        const [, dest, on] = WARP_ANY.exec(line)!
        if (i + 1 === warps.length || WARP_ANY.exec(warps[i + 1])![1] !== dest) {
          expect(line, `the warp wait to map ${dest} did not end there with the field free`).toMatch(/Screen = FieldFree/)
          expect(dest.split('/'), `the warp wait to map ${dest} ended on map ${on}`).toContain(on)
        }
      })
      expect(await capture(app, FOLLOWS_TITLE, '04-follows-covered.png'), 'the covered follows window painted one colour').toBe(true)
      await expect(heading(follows), 'milestone 3 in the covered follows window').toHaveText(R30.name, { timeout: 30_000 })
      await uncover(app)
      expect(await windowInfo(app, FOLLOWS_TITLE), 'the follows window after the second move').toEqual(first)
      expect(await timeOrigin(follows), 'the follows window navigated').toBe(origin)
      await shotAll('05-route30', pages)

      // (g) At the next decision after the arrival, the one message naming the next milestone.
      await expect.poll(async () => (await milestoneMessages(runStart)).filter((m) => m.endsWith(MESSAGE)).length,
        { message: `the message "${MESSAGE}"`, timeout: 3 * MINUTE, intervals: [5000] }).toBe(1)

      // (h) Six encounters from Route 30's anchor on its line, all Route 30 night species, none logged there before the switch.
      await seen(BATTLE, battlesAtR30 + 6, 15 * MINUTE)
      const afterNow = backendLog()
      const runs = afterNow.slice(afterNow.findIndex((l) => PLAN_NOW.test(l))).filter((l) => RUN_FROM.test(l)).map((l) => RUN_FROM.exec(l)!)
      expect([runs[0][1], runs[0][2]], 'the first Run after the switch starts on Route 30\'s anchor').toEqual(['547', '304'])
      for (const r of runs) expect(r[3], '"the Run macro reset": a Run line not on Route 30\'s line').toMatch(R30_MACRO)
      await cancelEventAndBot(card)
      const r30Rows = rowsFor(HANDLE, R30.id, runStart)
      expect(r30Rows.length, 'six encounters rows under 34-land').toBeGreaterThanOrEqual(6)
      for (const row of r30Rows) {
        expect(R30_NIGHT, `a ${row.species} under 34-land`).toContain(row.species)
        expect(row.at, `a 34-land row at ${new Date(row.at).toISOString()}, before the switch was seen`).toBeGreaterThanOrEqual(now.at - 300)
      }
      expect(r30Rows.some((r) => r.species === '167' || r.species === '163'), 'neither a Spinarak nor a Hoothoot in six').toBe(true)
      expect(await milestoneMessages(runStart), 'one milestone message in all').toHaveLength(1)
      // Each file's own counts: HG-Plan's Route 30 card counts this run's rows, HG-Route32's does not move.
      await expect.poll(async () => (await countedHere(HANDLE)) - here!.plan, { message: 'HG-Plan\'s Route 30 card', timeout: MINUTE, intervals: [5000] })
        .toBe(r30Rows.length)
      expect(await countedHere(OTHER_FILE), 'HG-Route32\'s Route 30 card').toBe(here!.other)
      await shotAll('06-cancelled', pages)
    } else {
      expect(launchClock.hour >= 20 && launchClock.hour < 23, `the launch clock ${hhmm(launchClock)} is not night with an hour to spare; check QA_CLOCK_START`).toBe(true)
      const runStart = await startWild(card, { plan: PLAN.name, mode: 'Run', yRange: 1 })
      await seen(PLAN_STOP, 2, MINUTE)
      // The first pick before any walk: the switch, then the walk logged under Route 30.
      const now = await seen(PLAN_NOW, 1, 2 * MINUTE)
      expect(PLAN_NOW.exec(now.line)![1], 'the first pick').toBe(R30.name)
      const walk = walkOf((await seen(PLAN_WALK, 1, MINUTE)).line)
      expect(walk.fields, 'the walk from the start tile').toEqual([WEEKDAYS[launchClock.weekday], R30.name, R30.id, ...R30.at])
      const log = backendLog()
      expect(log.findIndex((l) => PLAN_NOW.test(l)), '"the first pick before any walk"').toBeLessThan(log.findIndex((l) => PLAN_WALK.test(l)))
      // The follows card is on Route 32 for that one decision at most.
      await expect(heading(follows), 'the follows window after the first pick').toHaveText(R30.name, { timeout: 30_000 })
      const arrived = await seen(ARRIVED, 1, 10 * MINUTE)
      const battles = backendLog().filter((l) => BATTLE.test(l)).length
      const from = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
      expect([from[1], from[2]], 'the first Run after the walk starts on Route 30\'s anchor').toEqual(['547', '304'])
      expect(from[3], 'Route 30\'s run line').toMatch(R30_MACRO)
      await seen(BATTLE, battles + 3, 10 * MINUTE)
      await shotAll('02-route30', pages)
      await cancelEventAndBot(card)
      // Every row from the Run click to the arrival under Route 30; none under Route 32.
      expect(rowsFor(HANDLE, R32.id, runStart), '"every row from the Run click under 34-land": a 36-land row').toEqual([])
      const fromAnchor = rowsFor(HANDLE, R30.id, new Date(arrived.at).toISOString())
      expect(fromAnchor.length, 'three rows from Route 30\'s anchor').toBeGreaterThanOrEqual(3)
      for (const row of fromAnchor) expect(R30_NIGHT, `a ${row.species} from Route 30's anchor at night`).toContain(row.species)
    }

    // The follows card moved off its subscribed milestone, so the teardown's pull by milestone would miss it.
    await unsubscribe(FOLLOWS)
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame, async () => {
      // The backend is still up here unless the scenario's own shutdown ran; unsubscribe ignores a follows card's milestone.
      if (frame.backend) await unsubscribe(FOLLOWS).catch((e: Error) => step(`teardown: follows card not unsubscribed: ${e.message}`))
    })
  }
})
