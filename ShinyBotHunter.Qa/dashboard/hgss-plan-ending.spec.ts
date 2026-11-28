// hgss-plan-ending (docs/specs/milestone-builder-rework.md, "Plans, as the owner redefined them"; #525 Q1b, #621 R8c2): what a
// plan does when its one step is done. Plan QA Ending: a default only, a Wild Encounter step (PhaseTarget, Run, y 1) on QA Done
// (Route 32 · grass with every amount 0, so complete from the start), its anchor set on the save's tile on Route 32,
// (474, 294), started through Bot Control's Plan action at noon on a Sunday from that tile.
// `end` (the default), endWhenComplete true: the step is Complete at its first decision, then the plan is Complete. No message.
// `runs-on`, endWhenComplete false: after the step's Complete nothing is open, so the plan Waits: it presses nothing for 90 s,
// and Cancel ends it.
// Run: `npx playwright test hgss-plan-ending`; `$env:QA_HGSS_VARIANT = "runs-on"` for the second.
import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  assertLocalStores, cancelBot, cancelEventAndBot, cardWindow, CLEARING, clockConfig, launchHgss, milestoneMessages, PLAN_PICK, PLAN_STALE,
  putMilestone, putPlan, readClock, rowsFor, saveOf, seed, seen, setAnchorHere, shutdown, stageCopy, startFrame, START_TILE, startPlan,
  subscribe, teardown, variant, WILD, writeLeadExtension, type Frame, type MilestoneRow, type Plan
} from './milestones'
import { dashboardState } from './rs-wild'
import { backendLog, MINUTE } from './static'

const which = variant(['end', 'runs-on'] as const)
const HANDLE = 'HG-Ending'
const DONE = 'QA Done'
const PLAN: Plan = {
  name: 'QA Ending', rules: [], default: { eventName: WILD, phaseName: DONE, targetType: 'PhaseTarget', encounterType: 'Run', yRange: 1 },
  endWhenComplete: which === 'end'
}
const BATTLE = /, isShiny: (True|False)\s*$/
const STATE = /\[Orchestrator\] State:/
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
const fields = (pattern: RegExp, line: string): string[] => pattern.exec(line)!.slice(1)
const picks = (): string[][] => backendLog().filter((l) => PLAN_PICK.test(l)).map((l) => fields(PLAN_PICK, l))

test(`hgss-plan-ending ${which}: a plan whose one step is complete ${which === 'end' ? 'ends Complete' : 'Waits until Cancel'}`, async () => {
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  // Before the try, so the teardown never closes an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-29T12:00:00')
    const admin = await startFrame(frame)
    // QA Done: the location's seed rows, every amount 0.
    const seeded = await seed('HGSS', '36-land', 'None')
    expect(seeded.status, 'the seed of 36-land').toBe(200)
    const pokemon = (seeded.json.rows as MilestoneRow[]).map(({ name, speciesId, formCode, requireSpecificForm }) => ({ name, speciesId, amount: 0, formCode, requireSpecificForm }))
    expect((await putMilestone({ game: 'HGSS', name: DONE, location: '36-land', gameFiles: [HANDLE], archived: false, pokemon })).status, `${DONE} saved`).toBe(200)
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)
    expect((await subscribe({ game: 'HGSS', phase: DONE, gameName: HANDLE, followsHunt: true })).status, 'the follows card').toBeLessThan(300)
    const pages: Record<string, Page> = { admin, follows: await cardWindow(frame.app!, `HGSS — ${HANDLE}`) }

    const card = await launchHgss(admin, HANDLE, { clock: true })
    // The run stays in the day: no rule, and the pick lines' hour is checked.
    const clock = await readClock()
    expect(clock.hour >= 10 && clock.hour < 19, `the launch clock ${JSON.stringify(clock)} is not between 10:00 and 19:00; check QA_CLOCK_START`).toBe(true)
    await writeLeadExtension({ hp: 30 })
    const set = await setAnchorHere(0, DONE, 0, 1)
    expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    await shotAll('01-launched', pages)

    const clicked = new Date().toISOString()
    const runStart = await startPlan(card, PLAN.name).catch(async (e: Error) => {
      // `end` may be Complete before the form's first poll, which then sees the actions offered again: accepted only with the
      // pick logged and the plan Complete (a refusal logs no pick).
      if (which !== 'end' || !backendLog().some((l) => PLAN_PICK.test(l)) || (await dashboardState())?.eventState !== 'Complete') throw e
      step(`the Plan form's wait ended unseen, the plan already Complete: ${e.message.split('\n')[0]}`)
      return clicked
    })
    expect(fields(PLAN_PICK, (await seen(PLAN_PICK, 1, MINUTE)).line), 'the first pick').toEqual([PLAN.name, 'Sunday', String(clock.hour), DONE])
    expect(fields(START_TILE, (await seen(START_TILE, 1, MINUTE)).line), 'the step\'s start tile').toEqual(['36', '474', '294', 'Up'])

    if (which === 'end') {
      // Nothing is left at the first decision: the walk home from the anchor, which is the start tile, then Complete.
      await expect.poll(async () => (await dashboardState())?.eventState, { message: 'the plan is not Complete', timeout: 3 * MINUTE, intervals: [2000] })
        .toBe('Complete')
      expect(picks(), 'the picks: a Wait after the step').toHaveLength(1)
      await shotAll('02-complete', pages)
    } else {
      // After the step's Complete nothing is open: the plan Waits, pressing nothing.
      const wait = await seen(PLAN_PICK, 2, 3 * MINUTE)
      expect(fields(PLAN_PICK, wait.line), 'the pick after the step').toEqual([PLAN.name, 'Sunday', String(clock.hour), 'Wait'])
      await expect.poll(async () => (await dashboardState())?.name, { message: 'Bot Control\'s entry', timeout: 30_000, intervals: [1000] })
        .toBe(`Plan ${PLAN.name}: Wait`)
      const states = backendLog().filter((l) => STATE.test(l)).length
      await shotAll('02-waiting', pages)
      await sleep(90_000)
      expect(backendLog().filter((l) => STATE.test(l)).length, 'an [Orchestrator] State: line in the 90 s of the Wait').toBe(states)
      expect((await dashboardState())?.eventState, 'the Wait after 90 s').toBe('Running')
      expect(picks(), 'a pick in the 90 s of the Wait').toHaveLength(2)
      expect(backendLog().filter((l) => PLAN_STALE.test(l)), 'the stale park within 90 s').toEqual([])
    }
    const log = backendLog()
    // Both walks the step made, to the anchor at Init and home at its end, start on the anchor.
    expect(log.flatMap((l) => CLEARING.exec(l)?.[1] ?? []), '"Complete": only the walks from the start tile').toEqual(['already at start', 'already at start'])
    expect(log.filter((l) => BATTLE.test(l)), 'a battle').toEqual([])
    expect(rowsFor(HANDLE, DONE, runStart), `an encounter under ${DONE}`).toEqual([])

    // A Complete event has already ended (BotEventRunner keeps only its state for the card), so there one Cancel closes the bot.
    await (which === 'end' ? cancelBot(card) : cancelEventAndBot(card))
    // FR-34a: no message for a milestone complete at Start, read after the cancel so a late send has had time to land.
    expect(await milestoneMessages(runStart), '"none for a milestone complete at Start"').toEqual([])
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
