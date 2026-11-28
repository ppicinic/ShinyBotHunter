// hgss-plan-wait (docs/specs/milestone-builder-rework.md, "Plans, as the owner redefined them"; #621 R8c2): a Wait step, and the
// in-game hour turning over while the player stands idle in the field, which R5 left to QA. Plan QA Wait: rule Morning ->
// Wait, default QA Route 32 (Route 32 · grass, Living dex, its anchor set on the save's tile on Route 32, (474, 294)), a Wild
// Encounter step (PhaseTarget, Run, y 1), started through Bot Control's Plan action at 09:56 on a Monday. The morning picks
// Wait: nothing is pressed and the clock is polled every 60 s; the first poll past 10:00 picks the default, whose step starts
// on the tile and facing read before Run (nothing moved the player) and hunts there.
// If the hour does not turn over while idle, the plan would park with the stale sentence only after 65 minutes: the scenario
// fails instead a few minutes after 10:00 should have come, naming the last pick line.
// Run: `npx playwright test hgss-plan-wait`.
import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  assertLocalStores, cancelEventAndBot, cardWindow, clockConfig, launchHgss, PLAN_PICK, PLAN_STALE, putMilestone, putPlan, readClock, rowsFor,
  RUN_FROM, saveOf, seen, setAnchorHere, shutdown, stageCopy, startFrame, START_TILE, startPlan, subscribe, teardown, WILD, writeLeadExtension,
  type Frame, type Plan
} from './milestones'
import { dashboardState } from './rs-wild'
import { backendLog, MINUTE, readTile } from './static'

const HANDLE = 'HG-PlanWait'
const R32 = 'QA Route 32'
const PLAN: Plan = {
  name: 'QA Wait', rules: [{ times: ['Morning'], weekdays: [], arguments: { eventName: 'Wait' } }],
  default: { eventName: WILD, phaseName: R32, targetType: 'PhaseTarget', encounterType: 'Run', yRange: 1 }, endWhenComplete: false
}
// HG Route 32 by day: Bellsprout, Rattata, Mareep, Hoppip (milestones.json.gz, 36-land).
const R32_DAY = ['69', '19', '179', '187']
const R32_MACRO = /^M\.Up\.293\.\d+(:\S+)?;M\.Down\.295\.\d+$/
const BATTLE = /, isShiny: (True|False)\s*$/
// Every line the HGSS bot logs for a state it runs, a press or a macro it sends, a walk, a seek or a battle.
const ACTS = [/\[Orchestrator\] State:/, /\[Run\]/, /\[Spin\]/, /\[Navigator\]/, /seek:/, /\[Repel\]/, /\[Battle\]/, /\[Pickup\]/, /\[Headbutt\]/, BATTLE]
const fields = (pattern: RegExp, line: string): string[] => pattern.exec(line)!.slice(1)

test('hgss-plan-wait: a Wait presses nothing, and the idle in-game clock turning 10:00 picks the default', async () => {
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-30T09:56:00')
    const admin = await startFrame(frame)
    expect((await putMilestone({ game: 'HGSS', name: R32, location: '36-land', gameFiles: [HANDLE], archived: false })).status, `${R32} saved`).toBe(200)
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)
    expect((await subscribe({ game: 'HGSS', phase: R32, gameName: HANDLE, followsHunt: true })).status, 'the follows card').toBeLessThan(300)
    const follows = await cardWindow(frame.app!, `HGSS — ${HANDLE}`)
    const pages: Record<string, Page> = { admin, follows }

    const card = await launchHgss(admin, HANDLE, { clock: true })
    await writeLeadExtension({ hp: 30 })
    const set = await setAnchorHere(0, R32, 0, 1)
    expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    // The tile and facing before Run, read with the bot idle; the default's step reads them again at its Init.
    const tile = await readTile()
    const clock = await readClock()
    expect(clock.weekday === 1 && clock.hour === 9 && clock.minute <= 58, `the clock ${JSON.stringify(clock)} at Run is not Monday 9:58 or earlier; set QA_CLOCK_START earlier`).toBe(true)
    await shotAll('01-launched', pages)

    const runStart = await startPlan(card, PLAN.name)
    const wait = await seen(PLAN_PICK, 1, MINUTE)
    expect(fields(PLAN_PICK, wait.line), 'the morning pick').toEqual([PLAN.name, 'Monday', '9', 'Wait'])
    await expect.poll(async () => (await dashboardState())?.name, { message: 'Bot Control\'s entry', timeout: 30_000, intervals: [1000] })
      .toBe(`Plan ${PLAN.name}: Wait`)
    expect((await dashboardState())?.eventState, 'the Wait\'s state').toBe('Running')
    await shotAll('02-waiting', pages)

    // The first 60 s poll past 10:00 picks the default. Fails a few minutes after that instead of waiting for the stale park.
    const minutes = 60 - clock.minute + 3
    let day: Awaited<ReturnType<typeof seen>>
    try {
      day = await seen(PLAN_PICK, 2, minutes * MINUTE)
    } catch (e) {
      const last = backendLog().filter((l) => PLAN_PICK.test(l) || PLAN_STALE.test(l)).at(-1)
      throw new Error(`the in-game hour did not turn over from Monday 9h within ${minutes} minutes of Run while the player stood idle in the Wait `
        + `(the stale sentence would come only after 65 minutes); the last [Plan] line: ${last?.trim()}; ${(e as Error).message.split('\n')[0]}`)
    }
    expect(fields(PLAN_PICK, day.line), 'the pick at 10:00').toEqual([PLAN.name, 'Monday', '10', R32])
    const log = backendLog()
    const waitLines = log.slice(log.indexOf(wait.line) + 1, log.indexOf(day.line))
    step(`${waitLines.length} lines logged during the Wait`)
    expect(waitLines.filter((l) => ACTS.some((p) => p.test(l))), '"a Wait presses nothing": a state, press, walk or battle line during the Wait').toEqual([])
    expect(rowsFor(HANDLE, R32, runStart).filter((r) => r.at < day.at), 'an encounter during the Wait').toEqual([])
    expect(log.filter((l) => PLAN_STALE.test(l)), 'the stale park').toEqual([])

    // The step's Init reads where the player stands: the tile and facing of before Run.
    const start = fields(START_TILE, (await seen(START_TILE, 1, MINUTE)).line)
    expect(start, 'the step\'s start tile and facing, against the read before Run').toEqual([String(tile.map), String(tile.x), String(tile.y), tile.facing.replace(/^./, (c) => c.toUpperCase())])
    const from = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
    expect([from[1], from[2]], 'the first Run, on the anchor set on that tile').toEqual([String(tile.x), String(tile.y)])
    expect(from[3], 'Route 32\'s run line').toMatch(R32_MACRO)
    await seen(BATTLE, 3, 10 * MINUTE)
    await expect(follows.locator('.card-header-title'), 'the follows window on the default step').toHaveText(R32, { timeout: 30_000 })
    expect((await dashboardState())?.name, 'Bot Control\'s entry').toBe(`Plan ${PLAN.name}: ${R32}`)
    await shotAll('03-route32', pages)
    await cancelEventAndBot(card)
    const rows = rowsFor(HANDLE, R32, runStart)
    expect(rows.length, `three rows under ${R32}`).toBeGreaterThanOrEqual(3)
    for (const row of rows) expect(R32_DAY, `a ${row.species} under ${R32} by day`).toContain(row.species)

    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
