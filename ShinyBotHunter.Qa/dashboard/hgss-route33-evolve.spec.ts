import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU32, runDir, runMacro,
  shotAll, startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'

// A PKHeX copy of the Route 33 save: the lead is one experience point short of an evolution level, with no new
// move on that level. Its species and level are not recorded anywhere, so step 7 reads them.
const ROM = 'Pokemon HeartGold3'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const Y_ADDR = 0x21da6fc
const PHASE_TITLE = 'HGSS — Route 33'
const TEN_MINUTES = 10 * 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

type PartySlot = { slot: number; name: string; level: number; hp: number; maxHp: number; isShiny: boolean }
async function readParty(): Promise<PartySlot[]> {
  const res = await fetch('http://localhost:5000/api/bot/getParty/0')
  return res.ok ? ((await res.json()) as PartySlot[]) : []
}

const backendLines = (pattern: RegExp): string[] =>
  fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/).filter((l) => pattern.test(l))

test('HGSS Route 33 through the dashboard: train the lead one level so it evolves, cancel', async () => {
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    // 1. Build the dashboard so out/ carries the current test ids.
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)

    // 2. Preconditions.
    expect(fs.existsSync(SAVE), `missing ${SAVE}`).toBe(true)
    expect(fs.existsSync(SAVE + '.bak'), `missing ${SAVE}.bak`).toBe(true)
    step(`SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')}`)

    // 3. Backend.
    backend = await startBackend()
    step('backend up on localhost:5000')

    // 4. Dashboard and its windows.
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const recent = await windowByTitle(app, 'Pokemon Dashboard — Recent Encounters', 60_000)
    await admin.getByRole('button', { name: 'Dashboard Config', exact: true }).click()
    const subscriptions = admin.getByRole('heading', { name: 'Active Subscriptions' }).locator('..').locator('li')
    await admin.waitForTimeout(2000) // the subscription list arrives over SignalR after load
    if (!(await subscriptions.allTextContents()).some((t) => t.trim() === 'HGSSRoute 33Remove')) {
      const add = admin.getByRole('heading', { name: 'Add Subscription' }).locator('..')
      await add.locator('select').nth(0).selectOption('HGSS')
      await add.locator('select').nth(1).selectOption('Route 33')
      await add.locator('select').nth(2).selectOption('')
      await add.getByRole('button', { name: 'Add', exact: true }).click()
      step('subscribed HGSS / Route 33 through Dashboard Config')
    }
    let phase: Page
    try {
      phase = await windowByTitle(app, PHASE_TITLE, 60_000)
    } catch {
      throw new Error(`no "${PHASE_TITLE}" window; check that HGSS / Route 33 is subscribed in Dashboard Config`)
    }
    const pages = { admin, recent, phase }
    const countEl = phase.getByTestId('phase-encounter-count')
    const encounterCount = async (): Promise<number> => Number((await countEl.textContent())!.replace(/\D/g, ''))
    // The count reads 0 until the phase info and encounters arrive together; the loading line goes when they do.
    await expect(countEl).toBeVisible({ timeout: 60_000 })
    await expect(phase.getByText('Loading milestone requirements...')).toHaveCount(0, { timeout: 60_000 })
    const before = await encounterCount()
    step(`windows attached; Route 33 encounters before: ${before}`)

    // 5. Launch the bot through the top-level action.
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const wildPicker = card.getByRole('button', { name: 'Wild Encounter', exact: true })
    const resume = card.getByText('Resume', { exact: true })
    await expect(wildPicker).toBeVisible({ timeout: 5 * 60 * 1000 })
    // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch request returns.
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await shotAll('01-launched', pages)

    // 6. Two tiles down from the path into the grass. The position reads -1 until the overworld has faded in.
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    const y = toS16(await readU32(Y_ADDR))
    await runMacro(`M.Down.${y + 2}.${Y_ADDR}`)
    expect(toS16(await readU32(Y_ADDR))).toBe(y + 2)
    step(`player moved from y=${y} to y=${y + 2}`)
    await shotAll('02-in-grass', pages)

    // 7. The lead, read rather than asserted: one level from here it evolves.
    await expect.poll(async () => (await readParty()).length, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(0)
    const party = await readParty()
    step(`party: ${JSON.stringify(party)}`)
    const leadName = party[0].name
    const targetLevel = party[0].level + 1
    step(`lead ${leadName} Lv ${party[0].level}; target level ${targetLevel}`)

    // 8. Wild Encounter in train mode: fight everything until the lead reaches targetLevel.
    await wildPicker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption('Route 33')
    await card.getByTestId('param-TargetType').locator('select').selectOption('NoTarget')
    await card.getByTestId('param-EncounterType').locator('select').selectOption('Run')
    await card.getByTestId('param-xRange').locator('input').fill('3')
    await card.getByTestId('param-TrainType').locator('select').selectOption('Standard')
    await card.getByTestId('param-TargetLevel').locator('input').fill(String(targetLevel))
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step(`Wild Encounter started: train Standard to level ${targetLevel}`)
    await shotAll('03-training', pages)
    // The event is running; otherwise the wait below could pass on the idle card.
    await expect(wildPicker).toHaveCount(0, { timeout: 30_000 })

    // 9. The event ends on its own once the lead reaches targetLevel (or parks in Waiting). A crashed event
    // keeps its game state, so the card still looks busy; the backend's crash line ends the wait instead.
    // No party reads meanwhile: reads during the move-learn and evolution menus fail, and this run tests that path.
    const ended = async (): Promise<boolean> =>
      backendLines(/crashed/).length > 0 || (await wildPicker.isVisible()) || (await resume.isVisible())
    try {
      await expect.poll(ended, { timeout: TEN_MINUTES, intervals: [2000] }).toBe(true)
    } catch (e) {
      for (const line of backendLines(/\[LevelUp\]|\[Orchestrator\] State:|crashed/).slice(-20)) step(`backend: ${line}`)
      await shotAll('04-stuck', pages)
      await card.getByRole('button', { name: 'Cancel', exact: true }).click()
      await card.getByRole('button', { name: 'Yes', exact: true }).click()
      try {
        await expect(wildPicker).toBeVisible({ timeout: 60_000 })
      } catch {
        const last = backendLines(/\[Orchestrator\] State:/).pop() ?? 'no state logged'
        throw new Error(`event stuck at "${last}"; cancel did not complete within 60 s`)
      }
      throw e
    }
    await shotAll('04-event-done', pages)

    // 10. What LevelUpFlow did, then the asserts.
    for (const line of backendLines(/\[LevelUp\]|evolution|in an open move slot|already knows|crashed/)) step(`backend: ${line}`)
    step(`dashboard state: ${await (await fetch('http://localhost:5000/api/bot/getDashboardState')).text()}`)
    const log = fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
    const crashAt = log.findIndex((l) => l.includes('crashed'))
    expect(crashAt, `event crashed: ${log[crashAt + 1]}`).toBe(-1)
    const parked = await resume.isVisible()
    expect(parked, 'event parked in Waiting (lead fainted?)').toBe(false)
    const after = await readParty()
    step(`party after: ${JSON.stringify(after)}`)
    expect(after[0].level, 'lead did not gain the level').toBe(targetLevel)
    expect(after[0].name, 'lead did not evolve (its name is unchanged)').not.toBe(leadName)
    // LevelUpFlow.EvolutionInit logs both lines, so the legacy reader reported Evolution.
    expect(backendLines(/start evolution/).length, 'no "start evolution" in backend.log').toBeGreaterThan(0)
    expect(backendLines(/end evolution/).length, 'no "end evolution" in backend.log').toBeGreaterThan(0)
    const encounters = await encounterCount()
    step(`Route 33 encounters now: ${encounters}`)
    expect(encounters, 'no battle was recorded').toBeGreaterThanOrEqual(before + 1)

    // 11. One cancel removes the bot (the event has already ended) and closes the emulator.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('05-bot-cancelled', pages)

    // 12. Nothing was saved in game.
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 13. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 14. The documented shutdown.
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)
  } finally {
    // Failure path: leave nothing running. A forced kill does not flush SaveRAM.
    await stopRecorder()
    if (app) await app.close().catch(() => undefined)
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    if (emuHawkRunning()) killEmuHawk()
    // On every outcome: a forced kill must not have changed the save either.
    if (fs.existsSync(SAVE) && fs.existsSync(SAVE + '.bak')) {
      const same = fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak'))
      step(`after teardown: SaveRAM ${same ? 'equals' : 'DIFFERS FROM'} .bak (SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')})`)
    }
  }
})
