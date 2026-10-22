import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU16, readU32, repoDir, runDir, runMacro,
  sendCheatHotkey, shotAll, startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'

const BIZHAWK = 'C:\\BizHawk-2.11'
const SAVE = path.join(BIZHAWK, 'NDS', 'SaveRAM', 'Pokemon HeartGold.SaveRAM')
const CHEAT_SOURCE = path.join(repoDir, 'ShinyBotHunter.Qa', 'Cheats', 'hgss-usa-rev0', 'wild-shiny.cht')
const CHEAT_TARGET = path.join(BIZHAWK, 'NDS', 'Cheats', 'Pokemon HeartGold.cht')
// [address, original, patched] for the two halfwords of the wild-shiny cheat.
const PATCH_SITES: [number, number, number][] = [[0x02247d90, 0x2300, 0x682b], [0x02247d98, 0xfe3f, 0xfdbb]]
const Y_ADDR = 0x21da6fc
const PHASE_TITLE = 'HGSS — Route 33'
const TEN_MINUTES = 10 * 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

test('HGSS Route 33 through the dashboard: hunt, forced shiny, Resume, cancel', async () => {
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
    // The cheat file is loaded disabled; BizHawk reads it when the ROM opens.
    fs.writeFileSync(CHEAT_TARGET, fs.readFileSync(CHEAT_SOURCE, 'utf8').replace(/\r\n/g, '\n'))
    step(`cheat copied to ${CHEAT_TARGET}`)

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
    await launch.locator('select').selectOption('Pokemon HeartGold')
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const wildPicker = card.getByRole('button', { name: 'Wild Encounter', exact: true })
    await expect(wildPicker).toBeVisible({ timeout: 5 * 60 * 1000 })
    // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch request returns.
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await shotAll('01-launched', pages)

    // 6. The patch must not be active before the hotkey. Other overlays share these addresses while
    // the game is still loading, so wait until the site holds either the original or the patched value.
    for (const [addr, original, patched] of PATCH_SITES) {
      await expect.poll(async () => [original, patched].includes(await readU16(addr)), { timeout: 60_000, intervals: [500] }).toBe(true)
      const value = await readU16(addr)
      step(`patch site ${addr.toString(16)} = ${value.toString(16)}`)
      expect(value, 'patch active at launch').toBe(original)
    }

    // 7. Two tiles down from the path into the grass. The position reads -1 until the overworld has faded in.
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    const y = toS16(await readU32(Y_ADDR))
    await runMacro(`M.Down.${y + 2}.${Y_ADDR}`)
    expect(toS16(await readU32(Y_ADDR))).toBe(y + 2)
    step(`player moved from y=${y} to y=${y + 2}`)
    await shotAll('02-in-grass', pages)

    // 8. Wild Encounter on position 0.
    await wildPicker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption('Route 33')
    await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
    await card.getByTestId('param-EncounterType').locator('select').selectOption('Run')
    await card.getByTestId('param-xRange').locator('input').fill('3')
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step('Wild Encounter started')
    await shotAll('03-hunting', pages)

    // 9. Three encounters through the real loop.
    await expect.poll(encounterCount, { timeout: TEN_MINUTES, intervals: [2000] }).toBeGreaterThanOrEqual(before + 3)
    step(`Route 33 encounters now: ${await encounterCount()}`)
    await shotAll('04-three-encounters', pages)

    // 10. Arm the wild-shiny patch. EmuHawk must hold the foreground for the hotkey to land.
    expect(sendCheatHotkey(), 'EmuHawk was not the foreground window when the hotkey was sent').toBe(true)
    for (const [addr] of PATCH_SITES) step(`patch site ${addr.toString(16)} after hotkey = ${(await readU16(addr)).toString(16)}`)
    await shotAll('05-armed', pages)

    // 11. The forced shiny parks the bot, and the card offers Resume.
    await expect(card.getByText('Resume', { exact: true })).toBeVisible({ timeout: TEN_MINUTES })
    step(`Resume shown; Route 33 encounters now: ${await encounterCount()}`)
    await shotAll('06-resume', pages)

    // 12. First cancel clears the event; the position stays and is idle again.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    await expect(card).toBeVisible()
    step('event cancelled')
    await shotAll('07-event-cancelled', pages)

    // 13. Second cancel removes the bot and closes the emulator.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('08-bot-cancelled', pages)

    // 14. Nothing was saved in game.
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 15. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 16. The documented shutdown.
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
