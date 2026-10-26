import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, dolphinRunning, emuHawkRunning, killDolphin, killEmuHawk, killTree, launchDashboard, repoDir, runDir,
  shotAll, startBackend, step, stopBackend, windowByTitle
} from './driver'

const ROM = 'Pokemon - Sapphire Version (USA, Europe) (Rev 2)'
const REV0_ROM = 'Pokemon - Sapphire Version (USA)'
const HUNT_SAVE = path.join('C:\\BizHawk-2.11', 'GBA', 'SaveRAM', `${ROM}.SaveRAM`)
// Beside the main checkout, found through git so a worktree resolves it too.
const MAIN_CHECKOUT = path.dirname(spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: repoDir, encoding: 'utf8' }).stdout.trim())
const SAVES = path.join(MAIN_CHECKOUT, '..', 'shinybothunterqasaves', 'rs')
const PRISTINE = path.join(SAVES, 'qa-jirachi.SaveRAM')
const FULL = path.join(SAVES, 'qa-jirachi-full.SaveRAM')
const NODEX = path.join(SAVES, 'qa-jirachi-nodex.SaveRAM')
const ACTION = 'Wishmaker Jirachi Hunt'
const PHASE = 'Jirachi'

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

test('RS Wishmaker Jirachi through the dashboard: rev 2 only in the dropdown, three Start refusals, nothing launched', async () => {
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator or restores over a save this run did not touch.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(dolphinRunning(), 'a Dolphin is already running; close it first').toBe(false)
  for (const save of [PRISTINE, FULL, NODEX]) expect(fs.existsSync(save), `missing ${save}`).toBe(true)
  // The spec deletes the hunt save below, so it must be one it can put back.
  if (fs.existsSync(HUNT_SAVE)) {
    expect(sha256(HUNT_SAVE), `${HUNT_SAVE} differs from ${PRISTINE}; move it aside first`).toBe(sha256(PRISTINE))
  }
  try {
    // 1. Build the dashboard so out/ carries the current test ids.
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)

    // 2. Backend, dashboard, Bot Control.
    backend = await startBackend()
    step('backend up on localhost:5000')
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const pages = { admin }
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    const form = admin.getByTestId(`top-level-action-${ACTION}`)
    const gameSelect = form.locator('select').nth(0)
    const phaseSelect = form.locator('select').nth(1)

    // 3. FR-4: the dropdown lists rev 2 RS only. The soak's -WISHMKR copy is rev 2 too, so it may be listed.
    await expect.poll(async () => (await gameSelect.locator('option').count()), { timeout: 60_000 }).toBeGreaterThan(1)
    const handles = (await gameSelect.locator('option').evaluateAll((options) => options.map((o) => (o as HTMLOptionElement).value)))
      .filter((v) => v !== '')
    step(`GameName options: ${JSON.stringify(handles)}`)
    expect(handles, 'the rev 2 Sapphire ROM is not offered').toContain(ROM)
    expect(handles, 'the rev 0 Sapphire ROM is offered').not.toContain(REV0_ROM)
    await shotAll('01-dropdown', pages)

    // 4. FR-5: each refusal is the form's red message, and nothing launches.
    const cases: [string, string | null, string][] = [
      ['02-no-save', null, 'start the game in BizHawk and save once before hunting Jirachi.'],
      ['03-full-party', FULL, 'has a full party: leave a slot free for Jirachi.'],
      ['04-no-pokedex', NODEX, 'has no Pokédex yet: the Bonus Disc needs one.']
    ]
    for (const [name, source, sentence] of cases) {
      if (source) fs.copyFileSync(source, HUNT_SAVE)
      else fs.rmSync(HUNT_SAVE, { force: true })
      step(`${name}: hunt save ${source ? `${path.basename(source)} (sha256 ${sha256(HUNT_SAVE)})` : 'deleted'}`)
      await gameSelect.selectOption(ROM)
      await phaseSelect.selectOption(PHASE)
      await form.getByRole('button', { name: 'Start', exact: true }).click()
      const error = form.locator('.text-red-400')
      await expect(error).toContainText(sentence, { timeout: 60_000 })
      step(`${name}: ${await error.textContent()}`)
      await expect(admin.locator('[data-testid^="emulator-card-"]')).toHaveCount(0)
      expect(emuHawkRunning(), `${name}: an EmuHawk was launched`).toBe(false)
      expect(dolphinRunning(), `${name}: a Dolphin was launched`).toBe(false)
      await shotAll(name, pages)
    }

    // 5. The documented shutdown.
    await app.close()
    app = null
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)
  } finally {
    // Put the hunt save back on every outcome, then leave nothing running.
    fs.copyFileSync(PRISTINE, HUNT_SAVE)
    step(`hunt save restored: sha256 ${sha256(HUNT_SAVE)}, pristine sha256 ${sha256(PRISTINE)}`)
    if (app) await app.close().catch(() => undefined)
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    if (emuHawkRunning()) killEmuHawk()
    if (dolphinRunning()) killDolphin()
  }
})
