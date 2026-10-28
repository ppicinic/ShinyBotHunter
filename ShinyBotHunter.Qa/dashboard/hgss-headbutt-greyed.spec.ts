import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU32, runDir, shotAll, startBackend,
  startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import { backendLines, backendLog, MILESTONE, readTile, Y_ADDR } from './headbutt'

// Slowpoke's Headbutt replaced by Tackle: no party member knows Headbutt.
const ROM = 'HG-Headbutt-NoMove'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const API = 'http://localhost:5000/api'
const NO_HEADBUTT = 'No Pokémon in your party knows Headbutt.'

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

test('HGSS Headbutt with no party member knowing it: the option is greyed with its reason, an API Start is a 400, cancel', async () => {
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
    const pages = { admin, recent }
    step('windows attached')

    // 5. Launch the bot through the top-level action.
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const wildPicker = card.getByRole('button', { name: 'Wild Encounter', exact: true })
    await expect(wildPicker).toBeVisible({ timeout: 5 * 60 * 1000 })
    // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch request returns.
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await shotAll('01-launched', pages)

    // 6. In the field (the position reads -1 until the overworld has faded in).
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    step(`tile ${await readTile()}`)

    // 7. The Wild Encounter form: Headbutt disabled with the FR-3 sentence in its label, Spin and Run enabled.
    // The party is read with no workaround; the read-failed sentence instead is a product finding, not a pass.
    await wildPicker.click()
    const phases = await card.getByTestId('param-PhaseName').locator('option').allTextContents()
    expect(phases, `no "${MILESTONE}" milestone; make it in the Milestone Editor`).toContain(MILESTONE)
    const encounterType = card.getByTestId('param-EncounterType')
    const headbutt = encounterType.locator('option[value="Headbutt"]')
    await expect(headbutt).toBeAttached({ timeout: 30_000 })
    const label = (await headbutt.textContent()) ?? ''
    step(`Headbutt option: "${label}", disabled ${await headbutt.isDisabled()}`)
    expect(label, `the Headbutt option does not carry the no-move sentence: "${label}"`).toBe(`Headbutt (${NO_HEADBUTT})`)
    expect(await headbutt.isDisabled(), 'the Headbutt option is enabled').toBe(true)
    for (const other of ['Spin', 'Run']) {
      expect(await encounterType.locator(`option[value="${other}"]`).isDisabled(), `${other} is disabled`).toBe(false)
    }
    await shotAll('02-greyed', pages)

    // 8. A Start with Headbutt through the API is refused with the same sentence (FR-4), and no event starts.
    // The last element is the empty string after the final newline; the first line after the click replaces it.
    const from = backendLog().length - 1
    const res = await fetch(`${API}/bot/startAction/0`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ EventName: 'Wild Encounter', PhaseName: MILESTONE, TargetType: 'ShinyTarget', EncounterType: 'Headbutt' })
    })
    const body = await res.text()
    step(`startAction with Headbutt: ${res.status} "${body}"`)
    expect(res.status, 'the Start was not refused').toBe(400)
    expect(body, 'the refusal is not the no-move sentence').toBe(NO_HEADBUTT)
    const state = await (await fetch(`${API}/bot/getDashboardState`)).json()
    step(`dashboard state: ${JSON.stringify(state)}`)
    expect(state['0']?.eventState, 'an event started').toBe('None')
    await expect(wildPicker).toBeVisible()
    expect(backendLines(/\[Headbutt\]/, from), 'a [Headbutt] line after the refused Start').toEqual([])
    await shotAll('03-refused', pages)

    // 9. One cancel removes the bot (there is no event) and closes the emulator.
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

    // 12. The documented shutdown.
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
