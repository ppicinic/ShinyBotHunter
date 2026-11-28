// hgss-plan-morning-back (docs/specs/milestone-builder-rework.md, "Plans, as the owner redefined them"; #525 Q1b, #621 R8c2):
// a Start just before a time boundary and off the rule's anchor, through Bot Control's Plan action. Plan QA Morning: rule
// Morning -> QA Route 30 (Route 30 · grass), default QA Route 32 (Route 32 · grass), each a Wild Encounter step (PhaseTarget,
// Run, y 1), both Living dex and ticked for HG-Plan2, from the save's tile on Route 32, (474, 294), at 09:42 on a Monday.
// QA Route 32's anchor is set on the save's tile first: the walk to its generated anchor, which comes south from Violet City,
// meets a story trigger. The morning picks QA Route 30, whose step walks there; the first decision past 10:00 picks the
// default, whose step walks back to (474, 294) and hunts Route 32's day species.
// Run: `npx playwright test hgss-plan-morning-back`.
import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  assertLocalStores, cancelEventAndBot, cardWindow, clockConfig, launchHgss, PLAN_PICK, putMilestone, putPlan, readClock, rowsFor, RUN_FROM,
  saveOf, seen, setAnchorHere, shutdown, stageCopy, startFrame, START_TILE, startPlan, subscribe, teardown, WILD, writeLeadExtension,
  type Clock, type Frame, type Plan
} from './milestones'
import { dashboardState } from './rs-wild'
import { backendLog, MINUTE } from './static'

const HANDLE = 'HG-Plan2'
const R32 = { name: 'QA Route 32', location: '36-land', at: ['36', '474', '294'] }
const R30 = { name: 'QA Route 30', location: '34-land', at: ['34', '547', '304'] }
// HG Route 30 in the morning and by day: Pidgey, Caterpie, Metapod. HG Route 32 by day: Bellsprout, Rattata, Mareep, Hoppip
// (milestones.json.gz, 34-land and 36-land).
const R30_MORNING_DAY = ['16', '10', '11']
const R32_DAY = ['69', '19', '179', '187']
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const stepOf = (phaseName: string): Record<string, unknown> => ({ eventName: WILD, phaseName, targetType: 'PhaseTarget', encounterType: 'Run', yRange: 1 })
const PLAN: Plan = { name: 'QA Morning', rules: [{ times: ['Morning'], weekdays: [], arguments: stepOf(R30.name) }], default: stepOf(R32.name), endWhenComplete: false }
const FOLLOWS_TITLE = `HGSS — ${HANDLE}`
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const RETURNING = /\[Navigator\] Returning to start map=(\d+) \((\d+),(\d+)\) from map=(\d+) \((\d+),(\d+)\)\s*$/
const BATTLE = /, isShiny: (True|False)\s*$/
const STATE = /\[Orchestrator\] State: (\w+)/
const R32_MACRO = /^M\.Up\.293\.\d+(:\S+)?;M\.Down\.295\.\d+$/
const R30_MACRO = /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/

const hhmm = (c: Clock): string => `${WEEKDAYS[c.weekday]} ${String(c.hour).padStart(2, '0')}:${String(c.minute).padStart(2, '0')}`
const count = (pattern: RegExp): number => backendLog().filter((l) => pattern.test(l)).length
const fields = (pattern: RegExp, line: string): string[] => pattern.exec(line)!.slice(1)
/** The Run lines logged after `line`. */
function runsAfter(line: string): RegExpExecArray[] {
  const log = backendLog()
  return log.slice(log.indexOf(line)).filter((l) => RUN_FROM.test(l)).map((l) => RUN_FROM.exec(l)!)
}

test('hgss-plan-morning-back: QA Route 30 in the morning, back to QA Route 32 at the first decision past 10:00', async () => {
  test.setTimeout(45 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-30T09:42:00')
    const admin = await startFrame(frame)
    for (const m of [R32, R30]) {
      expect((await putMilestone({ game: 'HGSS', name: m.name, location: m.location, gameFiles: [HANDLE], archived: false })).status, `${m.name} saved`).toBe(200)
    }
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)
    expect((await subscribe({ game: 'HGSS', phase: R32.name, gameName: HANDLE, followsHunt: true })).status, 'the follows card').toBeLessThan(300)
    const follows = await cardWindow(frame.app!, FOLLOWS_TITLE)
    const pages: Record<string, Page> = { admin, follows }

    const card = await launchHgss(admin, HANDLE, { clock: true })
    const launchClock = await readClock()
    // The walk to Route 30 and three encounters there take several minutes of the morning.
    const left = 10 * 60 - (launchClock.hour * 60 + launchClock.minute)
    expect(launchClock.hour >= 4 && left >= 8, `${hhmm(launchClock)} at launch leaves under 8 minutes of morning; set QA_CLOCK_START earlier`).toBe(true)
    await writeLeadExtension({ hp: 30 })
    const set = await setAnchorHere(0, R32.name, 0, 1)
    expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    await shotAll('01-launched', pages)

    const runStart = await startPlan(card, PLAN.name)
    // The morning picks the rule; its step's Init captures the save's tile and walks from it to Route 30's anchor.
    const pick1 = await seen(PLAN_PICK, 1, MINUTE)
    expect(fields(PLAN_PICK, pick1.line), 'the morning pick').toEqual([PLAN.name, WEEKDAYS[launchClock.weekday], '9', R30.name])
    expect(fields(START_TILE, (await seen(START_TILE, 1, MINUTE)).line), 'the first step\'s start tile').toEqual([...R32.at, 'Up'])
    expect(fields(RETURNING, (await seen(RETURNING, 1, MINUTE)).line), 'the first walk, from the save\'s tile').toEqual([...R30.at, ...R32.at])
    await expect(follows.locator('.card-header-title'), 'the follows window on the morning step').toHaveText(R30.name, { timeout: 30_000 })
    const arrived1 = await seen(ARRIVED, 1, 10 * MINUTE)
    const battlesAtR30 = count(BATTLE)
    const from1 = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
    expect([from1[1], from1[2]], 'the first Run after the walk starts on Route 30\'s anchor').toEqual(R30.at.slice(1))
    expect(from1[3], 'Route 30\'s run line').toMatch(R30_MACRO)
    await seen(BATTLE, battlesAtR30 + 3, 10 * MINUTE)
    await shotAll('02-route30-morning', pages)

    // The first decision past 10:00 picks the default: the rule's hunt ends there, between battles, and the default's step
    // walks back from Route 30's run line to Route 32's anchor.
    const pick2 = await seen(PLAN_PICK, 2, 20 * MINUTE)
    expect(fields(PLAN_PICK, pick2.line), 'the pick at 10:00').toEqual([PLAN.name, WEEKDAYS[launchClock.weekday], '10', R32.name])
    const log = backendLog()
    const before = log.slice(0, log.indexOf(pick2.line))
    // The second step's lines are counted from the pick, so a line repeated on the first walk is not taken for them.
    const [tiles, walks, arrivals] = [START_TILE, RETURNING, ARRIVED].map((p) => before.filter((l) => p.test(l)).length)
    expect(before.filter((l) => STATE.test(l)).at(-1), 'the last state before the switch').toMatch(/State: DecideOverworldAction/)
    const morning = rowsFor(HANDLE, R30.name, new Date(arrived1.at).toISOString()).filter((r) => r.at < pick2.at)
    expect(morning.length, `${morning.length} encounters at Route 30's anchor before 10:00 (launched at ${hhmm(launchClock)}); set QA_CLOCK_START earlier`)
      .toBeGreaterThanOrEqual(3)
    for (const row of morning) expect(R30_MORNING_DAY, `a ${row.species} at Route 30's anchor in the morning`).toContain(row.species)
    const tile2 = fields(START_TILE, (await seen(START_TILE, tiles + 1, MINUTE)).line)
    expect(tile2.slice(0, 2), 'the second step starts on Route 30\'s run line').toEqual(R30.at.slice(0, 2))
    const y2 = Number(tile2[2])
    expect(y2 >= 298 && y2 <= 310, `the second step's start y ${y2}, off Route 30's line`).toBe(true)
    const walk2 = fields(RETURNING, (await seen(RETURNING, walks + 1, MINUTE)).line)
    expect(walk2.slice(0, 5), 'the walk back to Route 32\'s anchor, from Route 30\'s run line').toEqual([...R32.at, ...R30.at.slice(0, 2)])
    const arrived2 = await seen(ARRIVED, arrivals + 1, 15 * MINUTE)
    const battlesAtR32 = count(BATTLE)
    await seen(BATTLE, battlesAtR32 + 3, 10 * MINUTE)
    const runs = runsAfter(arrived2.line)
    expect([runs[0][1], runs[0][2]], 'the first Run after the walk back starts on Route 32\'s anchor').toEqual(R32.at.slice(1))
    for (const r of runs) expect(r[3], 'a Run line not on Route 32\'s line').toMatch(R32_MACRO)
    await expect(follows.locator('.card-header-title'), 'the follows window on the default step').toHaveText(R32.name, { timeout: 30_000 })
    expect((await dashboardState())?.name, 'Bot Control\'s entry').toBe(`Plan ${PLAN.name}: ${R32.name}`)
    expect(count(PLAN_PICK), 'a third pick').toBe(2)
    await shotAll('03-route32-day', pages)
    await cancelEventAndBot(card)
    const day = rowsFor(HANDLE, R32.name, new Date(arrived2.at).toISOString())
    expect(day.length, 'three rows under QA Route 32 from Route 32\'s anchor').toBeGreaterThanOrEqual(3)
    for (const row of day) expect(R32_DAY, `a ${row.species} at Route 32's anchor by day`).toContain(row.species)
    expect(rowsFor(HANDLE, R32.name, runStart).filter((r) => r.at < pick2.at), 'a QA Route 32 row in the morning').toEqual([])

    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
