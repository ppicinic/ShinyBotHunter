// hgss-plan-weekday (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1b, plan scenario 13): C3b's midnight check.
// Plan QA Friday: a default, and a rule for Friday at any time; started at 23:50 on a Thursday. Before midnight the rule does
// not match and the default is hunted; after midnight the weekday and the hour roll together (research R2) and the rule
// matches. One launch shows a weekday rule matching and not matching.
// `hg` (the default): HeartGold from the save's tile on Route 32, (474, 294): default Route 32 · grass, Friday -> Route 30 · grass,
// goal Wooper 1 (Route 32 at night) and Spinarak 1 (Route 30 at night). Route 32's anchor is set on the save's tile, so
// Thursday hunts there with no walk: the walk south to its generated anchor meets a story trigger.
// `ss`: SoulSilver from the qa-ss-route30 save's tile on Route 30, (556, 374): the two milestones swapped, so the default is the
// route the save stands on (default Route 30 · grass, Friday -> Route 32 · grass), goal Hoothoot 1 (SS Route 30 at night, not
// Route 32) and Wooper 1 (SS Route 32 at night, not Route 30). Without the save it fails at once naming the missing file.
// Run: `npx playwright test hgss-plan-weekday`; `$env:QA_HGSS_VARIANT = "ss"` for SoulSilver.
import * as fs from 'fs'
import { expect, test } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  assertLocalStores, cancelEventAndBot, clockConfig, launchHgss, PLAN_NOW, PLAN_STOP, PLAN_WALK, putGoal, putPlan, readClock,
  rowsFor, RUN_FROM, saveOf, seen, setAnchorHere, shutdown, stageCopy, startFrame, startWild, teardown, variant, writeLeadExtension,
  type Clock, type Frame, type Plan
} from './milestones'
import { API, backendLog, MINUTE } from './static'

const which = variant(['hg', 'ss'] as const)
const R32 = { id: '36-land', name: 'Route 32 · grass', at: ['36', '458', '339'], macro: /^M\.Up\.332\.\d+(:\S+)?;M\.Down\.346\.\d+$/ }
const R30 = { id: '34-land', name: 'Route 30 · grass', at: ['34', '547', '304'], macro: /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/ }
// Route 32's anchor as the hg variant sets it on the save's tile.
const R32_HERE = { ...R32, at: ['36', '474', '294'], macro: /^M\.Up\.293\.\d+(:\S+)?;M\.Down\.295\.\d+$/ }
type Stop = typeof R32
// Each version's night tables: HG Route 32 Wooper, Rattata, Bellsprout, Mareep, Zubat; HG Route 30 Spinarak, Rattata,
// Hoothoot; SS Route 30 Hoothoot, Rattata; SS Route 32 Wooper, Ekans, Bellsprout, Mareep, Zubat.
const CASES: Record<typeof which, { handle: string; rom: string; save: string; before: Stop; after: Stop; goal: number[]; beforeNight: string[]; afterNight: string[] }> = {
  hg: {
    handle: 'HG-Weekday', rom: 'HG-Route32.nds', save: 'hgss.qa-route32', before: R32_HERE, after: R30, goal: [194, 167],
    beforeNight: ['194', '19', '69', '179', '41'], afterNight: ['167', '19', '163']
  },
  ss: {
    handle: 'SS-Weekday', rom: 'Pokemon SoulSilver.nds', save: 'hgss.qa-ss-route30', before: R30, after: R32, goal: [163, 194],
    beforeNight: ['163', '19'], afterNight: ['194', '23', '69', '179', '41']
  }
}
const run = CASES[which]
const PLAN: Plan = { name: 'QA Friday', default: run.before.id, rules: [{ times: [], weekdays: ['Friday'], milestone: run.after.id }] }
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const BATTLE = /, isShiny: (True|False)\s*$/

const hhmm = (c: Clock): string => `${['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][c.weekday]} ${c.hour}:${String(c.minute).padStart(2, '0')}`
// The walk line's weekday, hour, milestone, the map and x it walks from (a run line's y varies), and the destination.
function walkOf(line: string): string[] {
  const m = PLAN_WALK.exec(line)!
  return [m[1], m[2], m[3], m[4], m[5], m[7], m[8], m[9]]
}

test(`hgss-plan-weekday ${which}: Thursday 23:50 to Friday, a weekday rule not matching, then matching after midnight`, async () => {
  test.setTimeout(40 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(run.handle, { rom: run.rom, save: run.save })
    clockConfig('2026-12-03T23:50:00')
    const admin = await startFrame(frame)
    const pages = { admin }
    expect((await putGoal(run.handle, run.goal.map((species) => ({ species, amount: 1 })))).status, 'PUT goal').toBe(204)
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)

    const card = await launchHgss(admin, run.handle, { clock: true })
    const launchClock = await readClock()
    expect(launchClock.weekday === 4 && launchClock.hour === 23 && launchClock.minute <= 55,
      `${hhmm(launchClock)} at launch is not Thursday 23:55 or earlier; set QA_CLOCK_START earlier on that Thursday`).toBe(true)
    if (which === 'hg') {
      await writeLeadExtension({ hp: 30 })
      const set = await setAnchorHere(0, R32.id, 0, 1)
      expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    } else {
      // No QA record of this save's party: logged, not written.
      step(`SoulSilver party: ${await (await fetch(`${API}/bot/getParty/0`)).text()}`)
    }
    await shotAll('01-launched', pages)

    const runStart = await startWild(card, { plan: PLAN.name, mode: 'Run', yRange: 1 })
    await seen(PLAN_STOP, 2, MINUTE)
    // Thursday: the rule does not match, so the default: on HeartGold hunted on its anchor where the bot stands, on SoulSilver
    // walked to from the save's tile.
    let thursdayFrom = runStart
    if (which === 'ss') {
      expect(walkOf((await seen(PLAN_WALK, 1, 2 * MINUTE)).line), 'the Thursday walk')
        .toEqual(['Thursday', '23', run.before.name, '34', '556', ...run.before.at])
      thursdayFrom = new Date((await seen(ARRIVED, 1, 10 * MINUTE)).at).toISOString()
    }
    const walksThursday = which === 'ss' ? 1 : 0
    const from1 = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
    expect([from1[1], from1[2]], `the first Run starts on ${run.before.name}'s anchor`).toEqual(run.before.at.slice(1))
    expect(from1[3], `${run.before.name}'s run line`).toMatch(run.before.macro)

    // Midnight: Friday 0h names the rule's milestone, walked to under the default, switched to on arrival.
    const walk2At = await seen(PLAN_WALK, walksThursday + 1, 20 * MINUTE)
    expect(walkOf(walk2At.line), 'the Friday walk').toEqual(['Friday', '0', run.after.name, ...run.before.at.slice(0, 2), ...run.after.at])
    const thursday = rowsFor(run.handle, run.before.id, thursdayFrom).filter((r) => r.at < walk2At.at)
    expect(thursday.length, `no encounter at ${run.before.name}'s anchor before midnight (launched at ${hhmm(launchClock)}); set QA_CLOCK_START earlier`)
      .toBeGreaterThanOrEqual(1)
    for (const row of thursday) expect(run.beforeNight, `a ${row.species} at ${run.before.name}'s anchor on Thursday night`).toContain(row.species)
    const log = backendLog()
    const walk2Index = log.flatMap((l, i) => (PLAN_WALK.test(l) ? [i] : []))[walksThursday]
    expect(log.slice(0, walk2Index).filter((l) => (PLAN_WALK.test(l) || PLAN_NOW.test(l)) && l.includes(run.after.name)),
      `a plan line naming ${run.after.name} before midnight`).toEqual([])

    const now = await seen(PLAN_NOW, 1, 15 * MINUTE)
    expect(PLAN_NOW.exec(now.line)![1], 'the switch on arrival').toBe(run.after.name)
    const battles = backendLog().filter((l) => BATTLE.test(l)).length
    await seen(BATTLE, battles + 3, 10 * MINUTE)
    const after = backendLog()
    const runs = after.slice(after.findIndex((l) => PLAN_NOW.test(l))).filter((l) => RUN_FROM.test(l)).map((l) => RUN_FROM.exec(l)!)
    expect([runs[0][1], runs[0][2]], `the first Run after the switch starts on ${run.after.name}'s anchor`).toEqual(run.after.at.slice(1))
    for (const r of runs) expect(r[3], `a Run line not on ${run.after.name}'s line`).toMatch(run.after.macro)
    await shotAll('02-friday', pages)
    await cancelEventAndBot(card)
    const friday = rowsFor(run.handle, run.after.id, new Date(now.at).toISOString())
    expect(friday.length, `three rows under ${run.after.id} on Friday`).toBeGreaterThanOrEqual(3)
    for (const row of friday) expect(run.afterNight, `a ${row.species} at ${run.after.name}'s anchor on Friday night`).toContain(row.species)

    expect(fs.readFileSync(saveOf(run.handle)).equals(fs.readFileSync(saveOf(run.handle) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
