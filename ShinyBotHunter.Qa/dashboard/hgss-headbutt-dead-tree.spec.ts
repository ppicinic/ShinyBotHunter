import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU32, runDir, runMacro, shotAll, startBackend,
  startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import {
  backendLines, backendLog, CRASHED, encounterRows, MILESTONE, parseFr11, pgEnv, readTile, X_ADDR, Y_ADDR
} from './headbutt'

const ROM = 'HG-Headbutt'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const START = '(425,464,Down)'
// One step left, then Down: the player faces the tree at (425, 465), which gives no Pokémon for this trainer ID.
const DEAD_TREE = `M.Left.425.${X_ADDR};W.8;H.Down.3;W.10`
// The FR-8 park line. "Pok.{1,2}mon": backend.log is the redirected console, whose code page can split the é.
const NO_POKEMON = /\[Headbutt\] The tree in front of you gave no Pok.{1,2}mon\. It may give none for your trainer ID, or a Repel is active\./
const TEN_MINUTES = 10 * 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

test('HGSS Headbutt on a tree with no Pokémon: one cycle, the player put back, the event parks in Waiting, cancel', async () => {
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

    // 6. Face the dead tree, once the overworld has faded in (the position reads -1 until then).
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    step(`save's tile ${await readTile()}`)
    await runMacro(DEAD_TREE)
    const startTile = await readTile()
    step(`start tile after ${DEAD_TREE}: ${startTile}`)
    expect(startTile, 'the setup macro did not leave the player facing the dead tree').toBe(START)
    await shotAll('01-dead-tree', pages)

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

    // 8. The park (FR-8), or a crash.
    const ended = async (): Promise<boolean> => backendLines(CRASHED, from).length > 0 || (await resume.isVisible())
    try {
      await expect.poll(ended, { timeout: TEN_MINUTES, intervals: [2000] }).toBe(true)
    } catch (e) {
      for (const line of backendLines(/\[Headbutt\]|\[Orchestrator\] State:/, from).slice(-30)) step(`backend: ${line}`)
      await shotAll('03-stuck', pages)
      throw e
    }
    await shotAll('03-parked', pages)
    for (const line of backendLines(/\[Headbutt\]|\[Orchestrator\] Start tile captured:/, from)) step(`backend: ${line}`)

    // 9. The asserts while parked: one cycle with no battle, the park line, the player back at the start, Waiting.
    const fr11 = backendLines(/\[Headbutt\] start=/, from)
    expect(fr11.length, 'not exactly one FR-11 line').toBe(1)
    expect(parseFr11(fr11[0])?.end, 'the cycle did not end with nothing').toBe('nothing')
    expect(backendLines(NO_POKEMON, from).length, 'not exactly one "gave no Pokémon" line').toBe(1)
    const captured = backendLines(/\[Orchestrator\] Start tile captured:/, from)
    expect(captured.length, 'no start tile captured').toBeGreaterThan(0)
    expect(captured[0], 'the captured start is not the dead tree tile').toContain('(425,464)')
    const parkedTile = await readTile()
    step(`tile at the park ${parkedTile}`)
    expect(parkedTile, 'the player was not put back before the park').toBe(START)
    expect(await resume.isVisible(), 'no Resume on the card').toBe(true)
    const state = await (await fetch('http://localhost:5000/api/bot/getDashboardState')).json()
    step(`dashboard state: ${JSON.stringify(state)}`)
    expect(state['0']?.eventState, 'the event is not in Waiting').toBe('Waiting')
    expect(backendLines(/Exception|crashed/, from), 'an exception or crash after the Start').toEqual([])

    // 10. First cancel clears the event; the position stays and is idle again.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    step('event cancelled')
    await shotAll('04-event-cancelled', pages)

    // 11. Second cancel removes the bot and closes the emulator.
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

    // 14. The documented shutdown, then no row: the tree gave no battle.
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)
    expect(encounterRows(pg, ROM, runStart).length, 'a row from a tree with no Pokémon').toBe(0)
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
