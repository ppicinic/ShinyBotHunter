import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU32, runDir, shotAll, startBackend,
  startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import { backendLines, backendLog, readTile, Y_ADDR } from './headbutt'
import { ABILITY_PICKUP, fillPickupForm, readPartyRam } from './pickup'

// The Route 33 save: no party member has the Pickup ability.
const ROM = 'Pokemon HeartGold'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const API = 'http://localhost:5000/api'
const NO_PICKUP = 'No Pokémon in your party has the Pickup ability.'
const NO_TRAIN = 'Pickup needs a train mode: the Pokémon must be knocked out, not fled from.'

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

async function startWithPickup(extra: Record<string, unknown>): Promise<{ status: number; body: string }> {
  const res = await fetch(`${API}/bot/startAction/0`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      EventName: 'Wild Encounter', PhaseName: 'Route 33', TargetType: 'ShinyTarget', EncounterType: 'Run', xRange: 3, Pickup: true, ...extra
    })
  })
  return { status: res.status, body: await res.text() }
}

test('HGSS Pickup with no Pickup member: the checkbox is greyed with its reason, both API Starts are 400s, cancel', async () => {
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

    // 6. In the field (the position reads -1 until the overworld has faded in), and no Pickup member in RAM:
    // otherwise the save is wrong, not the bot.
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    step(`tile ${await readTile()}`)
    const party = await readPartyRam()
    step(`party RAM: ${JSON.stringify(party)}`)
    expect(party.filter((m) => m.ability === ABILITY_PICKUP).map((m) => m.slot), `${SAVE} has a Pickup member`).toEqual([])

    // 7. The Wild Encounter form with train Standard: Pickup disabled with the FR-3 sentence in its label.
    // The party is read with no workaround; the read-failed sentence instead is a product finding, not a pass.
    await wildPicker.click()
    const { label, disabled } = await fillPickupForm(card, { pickup: false })
    step(`Pickup checkbox: "${label}", disabled ${disabled}`)
    expect(label, `the Pickup label does not carry the no-Pickup sentence: "${label}"`).toBe(`Pickup (${NO_PICKUP})`)
    expect(disabled, 'the Pickup checkbox is enabled').toBe(true)
    await shotAll('02-greyed', pages)

    // 8. Two Starts with Pickup through the API are refused with the FR-2 sentences, and no event starts.
    // The last element is the empty string after the final newline; the first line after the POST replaces it.
    const from = backendLog().length - 1
    const noTrain = await startWithPickup({ TrainType: 'None' })
    step(`startAction with Pickup and TrainType None: ${noTrain.status} "${noTrain.body}"`)
    expect(noTrain.status, 'the Start without a train mode was not refused').toBe(400)
    expect(noTrain.body, 'the refusal is not the train-mode sentence').toBe(NO_TRAIN)
    const noMember = await startWithPickup({ TrainType: 'Standard', TargetLevel: 101 })
    step(`startAction with Pickup and TrainType Standard: ${noMember.status} "${noMember.body}"`)
    expect(noMember.status, 'the Start with no Pickup member was not refused').toBe(400)
    expect(noMember.body, 'the refusal is not the no-Pickup sentence').toBe(NO_PICKUP)
    const state = await (await fetch(`${API}/bot/getDashboardState`)).json()
    step(`dashboard state: ${JSON.stringify(state)}`)
    expect(state['0']?.eventState, 'an event started').toBe('None')
    await expect(wildPicker).toBeVisible()
    expect(backendLines(/\[Pickup\]|\[Orchestrator\] State:/, from), 'a [Pickup] or state line after the refused Starts').toEqual([])
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
