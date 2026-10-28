import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU32, runDir, shotAll, startBackend,
  startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import { backendLines, backendLog, encounterRows, MILESTONE, pgEnv, readTile, watchCycles, Y_ADDR, type Cycle } from './headbutt'

// Typhlosion leads and follows, Slowpoke second knows Headbutt: the cut-in plays and the player never moves.
const ROM = 'HG-Headbutt-CutIn'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const START = '(426,464,Down)'
const CYCLES = 5

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

test('HGSS Headbutt by a non-following member: five cycles, no swap and no return macro, cancel', async () => {
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  const pg = pgEnv()
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

    // 4. Dashboard and its windows. Counts come from backend.log and Postgres, so no milestone window.
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const recent = await windowByTitle(app, 'Pokemon Dashboard — Recent Encounters', 60_000)
    const pages = { admin, recent }
    step('windows attached')

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

    // 6. The start tile, once the overworld has faded in (the position reads -1 until then).
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    const startTile = await readTile()
    step(`start tile ${startTile}`)
    expect(startTile, `${SAVE} does not stand at the tree`).toBe(START)

    // 7. Wild Encounter, ShinyTarget, Headbutt, TrainType left at None. Headbutt must be offered with no workaround.
    await wildPicker.click()
    const phases = await card.getByTestId('param-PhaseName').locator('option').allTextContents()
    expect(phases, `no "${MILESTONE}" milestone; make it in the Milestone Editor`).toContain(MILESTONE)
    await card.getByTestId('param-PhaseName').locator('select').selectOption(MILESTONE)
    await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
    const headbutt = card.getByTestId('param-EncounterType').locator('option[value="Headbutt"]')
    await expect(headbutt).toBeAttached({ timeout: 30_000 })
    const label = (await headbutt.textContent()) ?? ''
    step(`Headbutt option: "${label}", disabled ${await headbutt.isDisabled()}`)
    expect(await headbutt.isDisabled(), `Headbutt is not offered: "${label}"`).toBe(false)
    await card.getByTestId('param-EncounterType').locator('select').selectOption('Headbutt')
    const runStart = new Date().toISOString()
    // The last element is the empty string after the final newline; the first line after the click replaces it.
    const from = backendLog().length - 1
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step(`Wild Encounter started at ${runStart}: ${MILESTONE}, ShinyTarget, Headbutt`)
    await shotAll('02-running', pages)
    await expect(wildPicker).toHaveCount(0, { timeout: 30_000 })

    // 8. N cycles are N + 1 FR-11 lines, since a return shows on the next cycle's line. Cancel as soon as the last
    // one is logged, before that battle's foe is read, so the run leaves N rows.
    let cycles: Cycle[]
    try {
      cycles = await watchCycles(from, CYCLES + 1, resume)
    } catch (e) {
      for (const line of backendLines(/\[Headbutt\]|\[Orchestrator\] State:/, from).slice(-30)) step(`backend: ${line}`)
      await shotAll('03-stuck', pages)
      throw e
    }
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    step('event cancelled')
    await shotAll('03-event-cancelled', pages)

    // 9. Second cancel removes the bot and closes the emulator.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('04-bot-cancelled', pages)

    // 10. Nothing was saved in game.
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 11. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 12. The documented shutdown, so the encounters are written.
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)

    // 13. The asserts. Every cycle: the headbutt script, a battle, the A on the start tile facing the tree.
    for (const [i, c] of cycles.entries()) {
      expect(c.fr11!.script, `cycle ${i + 1} ran another script`).toBe(10014)
      expect(c.fr11!.end, `cycle ${i + 1} gave no battle`).toBe('battle')
      expect(c.aTile, `cycle ${i + 1}'s A was not at the start`).toBe(START)
    }
    // No swap, so every return is none.
    expect(cycles.map((c) => c.fr11!.ret), 'a return that is not none').toEqual(Array(CYCLES + 1).fill('none'))
    expect(encounterRows(pg, ROM, runStart).length, 'not one row per cycle').toBe(CYCLES)
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
