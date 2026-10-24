import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU16, readU32, runDir,
  shotAll, startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'

type PartySlot = { slot: number; name: string; level: number; hp: number; maxHp: number; isShiny: boolean }

// A PKHeX copy of the Route 33 save (hgss.qa-poison): on the path tile, lead Slowpoke poisoned at 6 HP.
// 6 is under a quarter of its max HP, so the first decision is the walk to the Azalea Pokémon Center, and
// poison takes it to 1 HP on the way, where std_survive_poisoning (script 2003) starts.
const ROM = 'Pokemon HeartGold5'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const LEAD_NAME = 'Slowpoke'
const ANCHOR_ADDR = 0x21d4158
const MAP_ID_OFFSET = -0x22da4
const X_ADDR = 0x21da6f4
const Y_ADDR = 0x21da6fc
const PHASE_TITLE = 'HGSS — Route 33'
const TEN_MINUTES = 10 * 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

async function readParty(): Promise<PartySlot[]> {
  const res = await fetch('http://localhost:5000/api/bot/getParty/0')
  return res.ok ? ((await res.json()) as PartySlot[]) : []
}

const backendLog = (): string[] => fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
const backendLines = (pattern: RegExp): string[] => backendLog().filter((l) => pattern.test(l))

// Map id and tile, read the way the backend's overworld reader does, in its "Start tile captured" format.
async function readTile(): Promise<string> {
  const map = await readU16((await readU32(ANCHOR_ADDR)) + MAP_ID_OFFSET)
  return `map=${map} (${toS16(await readU32(X_ADDR))},${toS16(await readU32(Y_ADDR))})`
}

test('HGSS poison walk through the dashboard: survive-poisoning script cleared mid-walk, heal, walk back, cancel', async () => {
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

    // 7. The lead this save was made for. Poison is not in the API; the first party snapshot line is its evidence.
    await expect.poll(async () => (await readParty()).length, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(0)
    const party = await readParty()
    step(`party: ${JSON.stringify(party)}`)
    expect(`${party[0].name} HP ${party[0].hp}`, `${SAVE} does not hold the expected lead`).toBe(`${LEAD_NAME} HP 6`)
    const targetLevel = party[0].level + 1

    // 8. Wild Encounter in train mode with Spin, so after the return the bot turns in place on the start tile.
    await wildPicker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption('Route 33')
    await card.getByTestId('param-TargetType').locator('select').selectOption('NoTarget')
    await card.getByTestId('param-EncounterType').locator('select').selectOption('Spin')
    await card.getByTestId('param-TrainType').locator('select').selectOption('Standard')
    await card.getByTestId('param-TargetLevel').locator('input').fill(String(targetLevel))
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step(`Wild Encounter started: Spin, train Standard to level ${targetLevel}`)
    await shotAll('02-running', pages)
    // The event is running; otherwise the wait below could pass on the idle card.
    await expect(wildPicker).toHaveCount(0, { timeout: 30_000 })

    // 9. The heal leg ends when the navigator is back on the start tile, or the event crashes or parks.
    const ended = async (): Promise<boolean> =>
      backendLines(/Clearing navigation path \(arrived back at start\)|crashed/).length > 0 || (await resume.isVisible())
    try {
      await expect.poll(ended, { timeout: TEN_MINUTES, intervals: [2000] }).toBe(true)
    } catch (e) {
      for (const line of backendLines(/\[Navigator\]|\[GameState\]|\[Orchestrator\] State:/).slice(-30)) step(`backend: ${line}`)
      await shotAll('03-stuck', pages)
      throw e
    }
    await shotAll('03-returned', pages)
    const endTile = await readTile()
    step(`end tile ${endTile}`)
    const after = await readParty()
    step(`party after: ${JSON.stringify(after)}`)

    // 10. What the navigator did, then the asserts.
    for (const line of backendLines(/\[Navigator\]|\[GameState\]|\[Spin\]|\[Orchestrator\]|crashed/)) step(`backend: ${line}`)
    const log = backendLog()
    const crashAt = log.findIndex((l) => l.includes('crashed'))
    expect(crashAt, `event crashed: ${log[crashAt + 1]}`).toBe(-1)
    expect(backendLines(/GameStateTimeoutException/), 'a stale-timeout fired').toEqual([])
    expect(await resume.isVisible(), 'event parked in Waiting').toBe(false)
    expect(backendLines(/\[Navigator\] Healing with/).length, 'not exactly one heal').toBe(1)
    expect(backendLines(/Movement blocked|abandoning the route/), 'the stall fired').toEqual([])

    // The poison message: the party diff saw it once, the reader saw script 2003 before the Center,
    // the press loop pressed only A and ended on FieldFree, and the walk resumed after it.
    expect(backendLines(/\[Navigator\] Party snapshot: .*poisoned: Slowpoke/).length, 'no poisoned lead in a party snapshot').toBeGreaterThan(0)
    expect(backendLines(/\[Navigator\] Poison event confirmed/).length, 'not exactly one poison event').toBe(1)
    expect(backendLines(/\[Navigator\] Poison event confirmed: Slowpoke .*\(poison cleared\)/).length, 'the poison event is not the lead surviving').toBe(1)
    const scriptAt = log.findIndex((l) => l.includes('[GameState] the survive-poisoning script:') && l.includes('Script = 2003'))
    const centerAt = log.findIndex((l) => l.includes('arrived at pokemon center'))
    expect(scriptAt, 'no Script = 2003 line: the party diff missed the script or the walk was too short').toBeGreaterThan(-1)
    expect(centerAt, 'no Script = 2003 line before arriving at the pokemon center').toBeGreaterThan(scriptAt)
    const poisonPresses = backendLines(/the survive-poisoning script: T\.[AB] at native/)
    step(`${poisonPresses.length} press lines in the survive-poisoning script`)
    expect(poisonPresses.length, 'no press on the survive-poisoning script').toBeGreaterThan(0)
    expect(poisonPresses.filter((l) => !l.includes('T.A at native')), 'a press other than A on the survive-poisoning script').toEqual([])
    const poisonFreeAt = log.findIndex((l) => /\[GameState\] the survive-poisoning script: .*Screen = FieldFree/.test(l))
    expect(poisonFreeAt, 'the survive-poisoning script never ended on FieldFree').toBeGreaterThan(scriptAt)
    const resumedAt = log.findIndex((l, i) => i > poisonFreeAt && l.includes('[Navigator] Segment '))
    expect(resumedAt, 'the walk did not resume after the poison message').toBeGreaterThan(poisonFreeAt)
    expect(resumedAt, 'the walk resumed only after the Center').toBeLessThan(centerAt)

    // Then the heal, with one Yes and no A after it, as in hgss-heal-variants.
    expect(backendLines(/the nurse's script: Yes at the menu native/).length, 'not exactly one Yes').toBe(1)
    const yesAt = log.findIndex((l) => l.includes("the nurse's script: Yes at the menu native"))
    expect(log.slice(yesAt + 1).filter((l) => l.includes("the nurse's script: T.A at native")), 'an A after the Yes').toEqual([])

    // Back on the tile the run started from, with the party healed.
    const captured = backendLines(/\[Orchestrator\] Start tile captured:/)
    expect(captured.length, 'no start tile captured').toBeGreaterThan(0)
    expect(captured[0], 'captured start tile differs from the live start tile').toContain(startTile)
    expect(endTile, 'not back on the start tile').toBe(startTile)
    for (const p of after) expect(p.hp, `slot ${p.slot} ${p.name} not healed`).toBe(p.maxHp)

    // 11. First cancel clears the event; the position stays and is idle again.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    step('event cancelled')
    await shotAll('04-event-cancelled', pages)

    // 12. Second cancel removes the bot and closes the emulator.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('05-bot-cancelled', pages)

    // 13. Nothing was saved in game.
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 14. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 15. The documented shutdown.
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
