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

// One run per save, each in its own process (driver.ts fixes the run folder at load, startBackend truncates
// backend.log). PKHeX copies of the Route 33 save, all on the path tile. The precondition reads the party the
// way the expected string is written; Pokérus is not in the API, so on HeartGold8 the log is its evidence.
const VARIANTS: Record<string, { save: string; precondition: (p: PartySlot[]) => string; expected: string }> = {
  'Pokemon HeartGold6': { save: 'hgss.qa-heal-lead-fainted', precondition: (p) => `lead HP ${p[0].hp}`, expected: 'lead HP 0' },
  'Pokemon HeartGold7': {
    save: 'hgss.qa-heal-slot2-fainted',
    precondition: (p) => `lead HP ${p[0].hp}, slot 2 HP ${p[1]?.hp}`,
    expected: 'lead HP 15, slot 2 HP 0'
  },
  'Pokemon HeartGold8': { save: 'hgss.qa-heal-pokerus', precondition: (p) => `lead HP ${p[0].hp}`, expected: 'lead HP 15' }
}
const ROM = process.env.QA_HEAL_ROM ?? ''
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const ANCHOR_ADDR = 0x21d4158
const MAP_ID_OFFSET = -0x22da4
const X_ADDR = 0x21da6f4
const Y_ADDR = 0x21da6fc
const PHASE_TITLE = 'HGSS — Route 33'
const TEN_MINUTES = 10 * 60 * 1000
// GameSnapshot prints decimals: 0x020477C1 the menu native, 0x0224BB91 the heal animation task.
const MENU_NATIVE = 'Native = 33847233'
const HEAL_ANIMATION = 'TaskFunction = 35961745'

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

test('HGSS heal variants through the dashboard: walk to the Pokémon Center, one nurse conversation, walk back, cancel', async () => {
  // Before anything starts: the variant must be one of the three saves.
  expect(Object.keys(VARIANTS), `set QA_HEAL_ROM to one of ${Object.keys(VARIANTS).join(', ')}`).toContain(ROM)
  const variant = VARIANTS[ROM]
  step(`variant ${ROM} (${variant.save})`)
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

    // 7. The party this save was made for: a lead at or under a quarter HP, so the first decision is the heal leg.
    await expect.poll(async () => (await readParty()).length, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(0)
    const party = await readParty()
    step(`party: ${JSON.stringify(party)}`)
    expect(variant.precondition(party), `${SAVE} does not hold the expected party`).toBe(variant.expected)
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

    // Exactly one nurse conversation: one menu, one Yes, B only after it, the heal animation after it,
    // and no field task between the loop's FieldFree and the return path.
    expect(backendLines(/the nurse's script: Yes at the menu native/).length, 'not exactly one Yes').toBe(1)
    expect(backendLines(/\[GameState\] the nurse's script:/).filter((l) => l.includes(MENU_NATIVE)).length, 'not exactly one menu').toBe(1)
    const yesAt = log.findIndex((l) => l.includes("the nurse's script: Yes at the menu native"))
    const afterYes = log.slice(yesAt + 1)
    expect(afterYes.filter((l) => l.includes("the nurse's script: T.A at native")), 'an A after the Yes').toEqual([])
    expect(afterYes.some((l) => l.includes(HEAL_ANIMATION)), 'no heal animation task after the Yes').toBe(true)
    const freeAt = log.findIndex((l) => /\[GameState\] the nurse's script: .*Screen = FieldFree/.test(l))
    expect(freeAt, "the nurse's script never ended on FieldFree").toBeGreaterThan(yesAt)
    const returnAt = log.findIndex((l, i) => i > freeAt && l.includes('Return path found'))
    expect(returnAt, 'no return path after the heal').toBeGreaterThan(freeAt)
    expect(log.slice(freeAt + 1, returnAt).filter((l) => l.includes('[GameState]') && l.includes('FieldTask')),
      'a field task between the heal and the return (a second conversation)').toEqual([])
    const pressA = backendLines(/the nurse's script: T\.A at native/).length
    const pressB = backendLines(/the nurse's script: T\.B at native/).length
    step(`${ROM}: ${pressA} T.A and ${pressB} T.B press lines in the nurse's script`)

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
