// hgss-plan-night (docs/specs/milestone-builder-rework.md, "Plans, as the owner redefined them"; #525 Q1b, #621 R8c2): a plan
// started through Bot Control's Plan action. Plan QA Night: rule Night -> QA Route 30 (Route 30 · grass), default QA Route 32
// (Route 32 · grass), each a Wild Encounter step (PhaseTarget, Run, y 1), both milestones Living dex and ticked for HG-Plan,
// from the save's tile on Route 32, (474, 294).
// `night` (the default), 19:48 on a Sunday: a 2-encounter hunt at the stored Route 33 where the bot stands, then the plan: QA
// Route 32 from its anchor set on the save's tile (the walk south to the generated anchor meets a story trigger) until the
// first decision past 20:00, with the plan in use and the follows window covered; there the step's hunt ends and QA Route 30's
// step walks to its anchor, with the covered window painting it, and hunts Route 30's night species.
// `start-on-rule`, 21:00: the first pick is QA Route 30, before any walk.
// Run: `npx playwright test hgss-plan-night`; `$env:QA_HGSS_VARIANT = "start-on-rule"` for the second. A launch that leaves
// too little day: `$env:QA_CLOCK_START = "2026-11-29T19:40:00"`.
import * as fs from 'fs'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  ANCHOR_HUNT, assertLocalStores, cancelEventAndBot, capture, cardWindow, clockConfig, cover, deletePlan, launchHgss, milestoneMessages,
  PLAN_PICK, putMilestone, putPlan, readClock, renamePlan, rowsFor, RUN_FROM, saveOf, seen, setAnchorHere, shutdown, stageCopy, startFrame,
  START_TILE, startPlan, startWild, subscribe, teardown, uncover, variant, WILD, windowInfo, writeLeadExtension,
  type Clock, type Frame, type Plan
} from './milestones'
import { dashboardState } from './rs-wild'
import { backendLog, MINUTE } from './static'

const which = variant(['night', 'start-on-rule'] as const)
const HANDLE = 'HG-Plan'
const R32 = { name: 'QA Route 32', location: '36-land', at: ['36', '474', '294'] }
const R30 = { name: 'QA Route 30', location: '34-land', at: ['34', '547', '304'] }
const ROUTE33 = 'Route 33'
// HG Route 30 at night: Spinarak, Rattata, Hoothoot, Lv 2 to 4; by day: Pidgey, Caterpie, Metapod (milestones.json.gz, 34-land).
const R30_NIGHT = ['167', '19', '163']
const R30_DAY = ['16', '10', '11']
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const stepOf = (phaseName: string): Record<string, unknown> => ({ eventName: WILD, phaseName, targetType: 'PhaseTarget', encounterType: 'Run', yRange: 1 })
const PLAN: Plan = { name: 'QA Night', rules: [{ times: ['Night'], weekdays: [], arguments: stepOf(R30.name) }], default: stepOf(R32.name), endWhenComplete: false }
const IN_USE = 'QA Night is in use by the hunt on bot #0; cancel that hunt first.'
// windowContracts.ts phaseWindowTitle for a follows-the-hunt card.
const FOLLOWS_TITLE = `HGSS — ${HANDLE}`
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const RETURNING = /\[Navigator\] Returning to start map=(\d+) \((\d+),(\d+)\) from map=(\d+) \((\d+),(\d+)\)\s*$/
const BATTLE = /, isShiny: (True|False)\s*$/
const STATE = /\[Orchestrator\] State: (\w+)/
// OrchestratorEvent's state line once a battle is over, then the Spin's Overworld.WaitForReadyAsync, ended on the free field.
const BATTLE_OVER = /\[Orchestrator\] State: BattleComplete/
const FIELD_READY = /\[GameState\] the overworld ready: GameSnapshot \{ Screen = FieldFree/
const WARP_ANY = /\[Navigator\] Warp to map (\d+(?:\/\d+)*): on map (\d+), (.*)$/
// The anchors' run lines: Route 32 up-down ±1 from (474, 294), Route 30 up-down ±6 from (547, 304).
const R32_MACRO = /^M\.Up\.293\.\d+(:\S+)?;M\.Down\.295\.\d+$/
const R30_MACRO = /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/

const hhmm = (c: Clock): string => `${WEEKDAYS[c.weekday]} ${String(c.hour).padStart(2, '0')}:${String(c.minute).padStart(2, '0')}`
const heading = (page: Page): Locator => page.locator('.card-header-title')
const timeOrigin = (page: Page): Promise<number> => page.evaluate(() => performance.timeOrigin)
const count = (pattern: RegExp): number => backendLog().filter((l) => pattern.test(l)).length
const fields = (pattern: RegExp, line: string): string[] => pattern.exec(line)!.slice(1)
const entryName = async (): Promise<string> => (await dashboardState())?.name
/** The ANCHOR_HUNT lines logged from line `from`: the location, map, tile, how hunted and where the anchor comes from. */
const anchorLines = (from: number): string[][] => backendLog().slice(from).filter((l) => ANCHOR_HUNT.test(l)).map((l) => fields(ANCHOR_HUNT, l))

test(`hgss-plan-night ${which}: three milestones in one follows window, the switch at the first decision past 20:00, the plan in use`, async () => {
  test.setTimeout(45 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig(which === 'night' ? '2026-11-29T19:48:00' : '2026-11-29T21:00:00')
    const admin = await startFrame(frame)
    const app = frame.app!
    for (const m of [R32, R30]) {
      expect((await putMilestone({ game: 'HGSS', name: m.name, location: m.location, gameFiles: [HANDLE], archived: false })).status, `${m.name} saved`).toBe(200)
    }
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)
    expect((await subscribe({ game: 'HGSS', phase: R32.name, gameName: HANDLE, followsHunt: true })).status, 'the follows card').toBeLessThan(300)
    const follows = await cardWindow(app, FOLLOWS_TITLE)
    const pages: Record<string, Page> = { admin, follows }

    const card = await launchHgss(admin, HANDLE, { clock: true })
    const launchClock = await readClock()
    await writeLeadExtension({ hp: 30 })
    await shotAll('01-launched', pages)

    if (which === 'night') {
      // (a) Two encounters at the stored Route 33, a label where the bot stands: the follows window shows it (milestone 1).
      await startWild(card, { phase: ROUTE33, target: 'ShinyTarget', mode: 'Spin' })
      await seen(BATTLE, 2, 5 * MINUTE)
      // The cancel only cancels the token and leaves the game where it is (the isShiny line is written when a battle starts),
      // and Set anchor here and the plan's Start both want the free field: so the event is cancelled only once the second
      // battle is over and the field is free again, before the next spin finds one.
      await seen(BATTLE_OVER, 2, 2 * MINUTE)
      const aLog = backendLog()
      const secondOver = aLog.map((l, i) => (BATTLE_OVER.test(l) ? i : -1)).filter((i) => i >= 0)[1]
      await seen(FIELD_READY, aLog.slice(0, secondOver).filter((l) => FIELD_READY.test(l)).length + 1, MINUTE)
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

      // (b) The plan: the day picks the default, QA Route 32, hunted from its anchor, set here, so no walk.
      const set = await setAnchorHere(0, R32.name, 0, 1)
      expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
      const logAtRun = backendLog().length
      const [tilesBefore, runsBefore, battlesBefore] = [count(START_TILE), count(RUN_FROM), count(BATTLE)]
      const runStart = await startPlan(card, PLAN.name)
      // One anchor line per step at Start, the rule's first.
      await expect.poll(() => anchorLines(logAtRun).length, { message: 'the steps\' anchor lines at Start', timeout: MINUTE, intervals: [500] }).toBe(2)
      expect(anchorLines(logAtRun), 'the anchor lines at Start').toEqual([
        ['Route 30 · grass', ...R30.at, 'run up-down ±6', 'generated'], ['Route 32 · grass', ...R32.at, 'run up-down ±1', 'set on this file']
      ])
      expect(fields(PLAN_PICK, (await seen(PLAN_PICK, 1, MINUTE)).line), 'the first pick').toEqual([PLAN.name, WEEKDAYS[runClock.weekday], '19', R32.name])
      expect(fields(START_TILE, (await seen(START_TILE, tilesBefore + 1, MINUTE)).line).slice(0, 3), 'the first step\'s start tile').toEqual(R32.at)
      const from1 = RUN_FROM.exec((await seen(RUN_FROM, runsBefore + 1, 2 * MINUTE)).line)!
      expect([from1[1], from1[2]], 'the first Run starts on Route 32\'s anchor').toEqual(R32.at.slice(1))
      expect(from1[3], 'Route 32\'s run line').toMatch(R32_MACRO)
      await seen(BATTLE, battlesBefore + 2, 8 * MINUTE)
      await expect(heading(follows), 'milestone 2 in the follows window').toHaveText(R32.name, { timeout: 30_000 })
      expect(await windowInfo(app, FOLLOWS_TITLE), 'the follows window after the first move (FR-39: the same window)').toEqual(first)
      expect(await timeOrigin(follows), 'the follows window navigated').toBe(origin)
      await expect.poll(entryName, { message: 'Bot Control\'s entry', timeout: 30_000, intervals: [1000] }).toBe(`Plan ${PLAN.name}: ${R32.name}`)
      await shotAll('03-route32', pages)

      // (c) The plan in use: no delete, no rename, no save.
      const del = await deletePlan('HGSS', PLAN.name)
      expect([del.status, del.text], 'DELETE plan while in use').toEqual([400, IN_USE])
      const ren = await renamePlan('HGSS', PLAN.name, 'QA Night 2')
      expect([ren.status, ren.text], 'rename while in use').toEqual([400, IN_USE])
      const put = await putPlan('HGSS', { ...PLAN, endWhenComplete: true })
      expect([put.status, put.text], 'PUT plan while in use').toEqual([400, IN_USE])
      // (d) The follows window under Admin, on top.
      await cover(app, FOLLOWS_TITLE)

      // (e) The first decision past 20:00 picks the rule: the step's hunt ends there, between battles, and QA Route 30's
      // step walks to its anchor through the door.
      const pick2 = await seen(PLAN_PICK, 2, 20 * MINUTE)
      expect(fields(PLAN_PICK, pick2.line), 'the second pick').toEqual([PLAN.name, WEEKDAYS[runClock.weekday], '20', R30.name])
      const log = backendLog()
      const pick2At = log.indexOf(pick2.line)
      const before = log.slice(0, pick2At)
      expect(before.filter((l) => STATE.test(l)).at(-1), 'the last state before the switch').toMatch(/State: DecideOverworldAction/)
      expect(before.findLastIndex((l) => /State: BattleStart/.test(l)), 'a battle in flight at the switch').toBeLessThan(before.findLastIndex((l) => BATTLE_OVER.test(l)))
      const before20 = rowsFor(HANDLE, R32.name, runStart).filter((r) => r.at < pick2.at)
      expect(before20.length, `${before20.length} encounters at Route 32's anchor before 20:00 (Run at ${hhmm(runClock)}, launched at ${hhmm(launchClock)}); set QA_CLOCK_START earlier`)
        .toBeGreaterThanOrEqual(2)
      const tile2 = fields(START_TILE, (await seen(START_TILE, tilesBefore + 2, MINUTE)).line)
      expect(tile2.slice(0, 2), 'the second step starts on Route 32\'s run line').toEqual(R32.at.slice(0, 2))
      expect(['293', '294', '295'], `the second step's start y ${tile2[2]}`).toContain(tile2[2])
      const arrived = await seen(ARRIVED, before.filter((l) => ARRIVED.test(l)).length + 1, 15 * MINUTE)
      const walkLog = backendLog()
      const walkLines = walkLog.slice(pick2At, walkLog.indexOf(arrived.line))
      const walk2 = walkLines.find((l) => RETURNING.test(l))
      expect(walk2 && fields(RETURNING, walk2).slice(0, 4), 'the walk to Route 30\'s anchor, from Route 32').toEqual([...R30.at, R32.at[0]])
      const warps = walkLines.filter((l) => WARP_ANY.test(l))
      expect(warps.length, '"the door crossed": no warp wait on the walk to Route 30').toBeGreaterThan(0)
      warps.forEach((line, i) => {
        const [, dest, on] = WARP_ANY.exec(line)!
        if (i + 1 === warps.length || WARP_ANY.exec(warps[i + 1])![1] !== dest) {
          expect(line, `the warp wait to map ${dest} did not end there with the field free`).toMatch(/Screen = FieldFree/)
          expect(dest.split('/'), `the warp wait to map ${dest} ended on map ${on}`).toContain(on)
        }
      })
      const battlesAtR30 = count(BATTLE)
      expect(await capture(app, FOLLOWS_TITLE, '04-follows-covered.png'), 'the covered follows window painted one colour').toBe(true)
      await expect(heading(follows), 'milestone 3 in the covered follows window').toHaveText(R30.name, { timeout: 30_000 })
      await uncover(app)
      expect(await windowInfo(app, FOLLOWS_TITLE), 'the follows window after the second move').toEqual(first)
      expect(await timeOrigin(follows), 'the follows window navigated').toBe(origin)
      await expect.poll(entryName, { message: 'Bot Control\'s entry', timeout: 30_000, intervals: [1000] }).toBe(`Plan ${PLAN.name}: ${R30.name}`)
      await shotAll('05-route30', pages)

      // (f) Six encounters from Route 30's anchor on its line, all Route 30 night species, none logged there before the switch.
      await seen(BATTLE, battlesAtR30 + 6, 15 * MINUTE)
      const afterArrival = backendLog()
      const runs = afterArrival.slice(afterArrival.indexOf(arrived.line)).filter((l) => RUN_FROM.test(l)).map((l) => RUN_FROM.exec(l)!)
      expect([runs[0][1], runs[0][2]], 'the first Run after the switch starts on Route 30\'s anchor').toEqual(R30.at.slice(1))
      for (const r of runs) expect(r[3], '"the Run macro reset": a Run line not on Route 30\'s line').toMatch(R30_MACRO)
      expect(count(PLAN_PICK), 'a pick after the switch').toBe(2)
      await cancelEventAndBot(card)
      const r30Rows = rowsFor(HANDLE, R30.name, runStart)
      expect(r30Rows.length, `six encounters rows under ${R30.name}`).toBeGreaterThanOrEqual(6)
      for (const row of r30Rows) {
        expect(R30_NIGHT, `a ${row.species} under ${R30.name}`).toContain(row.species)
        expect(row.level >= 2 && row.level <= 4, `a Lv ${row.level} ${row.species} under ${R30.name}, outside the night table's Lv 2 to 4`).toBe(true)
        expect(row.at, `a ${R30.name} row at ${new Date(row.at).toISOString()}, before the switch was seen`).toBeGreaterThanOrEqual(pick2.at - 300)
      }
      // The fixed clock start replays the game's RNG, so which night species come up is the stream's, not a sample.
      expect(r30Rows.filter((r) => R30_DAY.includes(r.species)), `a day species under ${R30.name} after the switch`).toEqual([])
      // No step's milestone completed, so no milestone message.
      expect(await milestoneMessages(runStart), 'a milestone message').toEqual([])
      await shotAll('06-cancelled', pages)
    } else {
      expect(launchClock.hour >= 20 && launchClock.hour < 23, `the launch clock ${hhmm(launchClock)} is not night with an hour to spare; check QA_CLOCK_START`).toBe(true)
      const runStart = await startPlan(card, PLAN.name)
      await expect.poll(() => anchorLines(0).length, { message: 'the steps\' anchor lines at Start', timeout: MINUTE, intervals: [500] }).toBe(2)
      expect(anchorLines(0), 'the anchor lines at Start').toEqual([
        ['Route 30 · grass', ...R30.at, 'run up-down ±6', 'generated'], ['Route 32 · grass', '36', '458', '339', 'run up-down ±7', 'generated']
      ])
      // The first pick before any walk: the rule's step, whose Init captures the save's tile and walks from it.
      const pick = await seen(PLAN_PICK, 1, MINUTE)
      expect(fields(PLAN_PICK, pick.line), 'the first pick').toEqual([PLAN.name, WEEKDAYS[launchClock.weekday], String(launchClock.hour), R30.name])
      const tile = await seen(START_TILE, 1, MINUTE)
      expect(fields(START_TILE, tile.line), 'the step\'s start tile').toEqual([...R32.at, 'Up'])
      const walk = await seen(RETURNING, 1, MINUTE)
      expect(fields(RETURNING, walk.line), 'the walk to Route 30\'s anchor').toEqual([...R30.at, ...R32.at])
      const log = backendLog()
      expect([log.indexOf(pick.line) < log.indexOf(tile.line), log.indexOf(tile.line) < log.indexOf(walk.line)], '"the first pick before any walk"').toEqual([true, true])
      await expect(heading(follows), 'the follows window after the first pick').toHaveText(R30.name, { timeout: 30_000 })
      await expect.poll(entryName, { message: 'Bot Control\'s entry', timeout: 30_000, intervals: [1000] }).toBe(`Plan ${PLAN.name}: ${R30.name}`)
      const arrived = await seen(ARRIVED, 1, 10 * MINUTE)
      const battles = count(BATTLE)
      const from = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
      expect([from[1], from[2]], 'the first Run after the walk starts on Route 30\'s anchor').toEqual(R30.at.slice(1))
      expect(from[3], 'Route 30\'s run line').toMatch(R30_MACRO)
      await seen(BATTLE, battles + 3, 10 * MINUTE)
      expect(count(PLAN_PICK), 'a second pick').toBe(1)
      await shotAll('02-route30', pages)
      await cancelEventAndBot(card)
      // Every row from the Run click under QA Route 30; none under QA Route 32.
      expect(rowsFor(HANDLE, R32.name, runStart), `"every row from the Run click under ${R30.name}": a ${R32.name} row`).toEqual([])
      const fromAnchor = rowsFor(HANDLE, R30.name, new Date(arrived.at).toISOString())
      expect(fromAnchor.length, 'three rows from Route 30\'s anchor').toBeGreaterThanOrEqual(3)
      for (const row of fromAnchor) expect(R30_NIGHT, `a ${row.species} from Route 30's anchor at night`).toContain(row.species)
    }

    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
