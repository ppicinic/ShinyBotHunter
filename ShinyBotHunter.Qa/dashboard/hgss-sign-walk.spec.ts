import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU16, readU32, runDir,
  shotAll, startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'

// hgss.qa-route32: Route 32 grass, lead Metapod at 1/30 HP, so the first decision is the heal trip to Violet City;
// the walk back steps Up under the town sign at (488, 263) on map 73.
const ROM = 'HG-Route32'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const LEAD_NAME = 'Metapod'
// Only a label here: any encounter on the walk is filed under it in the QA database.
const PHASE = 'Route 33'
const START_TILE = 'map=36 (474,294)'
const ANCHOR_ADDR = 0x21d4158
const MAP_ID_OFFSET = -0x22da4
const X_ADDR = 0x21da6f4
const Y_ADDR = 0x21da6fc
const TEN_MINUTES = 10 * 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')

type PartySlot = { slot: number; name: string; level: number; hp: number; maxHp: number; isShiny: boolean }
async function readParty(): Promise<PartySlot[]> {
  const res = await fetch('http://localhost:5000/api/bot/getParty/0')
  return res.ok ? ((await res.json()) as PartySlot[]) : []
}

const backendLines = (pattern: RegExp): string[] =>
  fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/).filter((l) => pattern.test(l))

// Map id and tile, read the way the backend's overworld reader does, in its "Start tile captured" format.
async function readTile(): Promise<string> {
  const map = await readU16((await readU32(ANCHOR_ADDR)) + MAP_ID_OFFSET)
  return `map=${map} (${toS16(await readU32(X_ADDR))},${toS16(await readU32(Y_ADDR))})`
}

test('HGSS Route 32 through the dashboard: heal trip to Violet City, close the town sign on the walk back, cancel', async () => {
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
    expect(startTile, `${SAVE} is not on the Route 32 start tile`).toBe(START_TILE)

    // 7. The lead this save was made for: at 1 HP, so the first decision is the heal leg.
    await expect.poll(async () => (await readParty()).length, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(0)
    const party = await readParty()
    step(`party: ${JSON.stringify(party)}`)
    expect(`${party[0].name} HP ${party[0].hp}`, `${SAVE} does not hold the expected lead`).toBe(`${LEAD_NAME} HP 1`)
    const targetLevel = party[0].level + 1

    // 8. Wild Encounter in train mode with Spin, so after the return the bot turns in place on the start tile.
    await wildPicker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption(PHASE)
    await card.getByTestId('param-TargetType').locator('select').selectOption('NoTarget')
    await card.getByTestId('param-EncounterType').locator('select').selectOption('Spin')
    await card.getByTestId('param-TrainType').locator('select').selectOption('Standard')
    await card.getByTestId('param-TargetLevel').locator('input').fill(String(targetLevel))
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step(`Wild Encounter started: Spin, train Standard to level ${targetLevel}`)
    await shotAll('02-running', pages)
    // The event is running; otherwise the wait below could pass on the idle card.
    await expect(wildPicker).toHaveCount(0, { timeout: 30_000 })

    // 9. The heal leg ends when the navigator is back on the start tile, or the event crashes, parks or stale-times out.
    const ended = async (): Promise<boolean> =>
      backendLines(/Clearing navigation path \(arrived back at start\)|crashed|GameStateTimeoutException/).length > 0 || (await resume.isVisible())
    try {
      await expect.poll(ended, { timeout: TEN_MINUTES, intervals: [2000] }).toBe(true)
    } catch (e) {
      for (const line of backendLines(/\[Navigator\]|\[GameState\]|\[Orchestrator\] State:/).slice(-30)) step(`backend: ${line}`)
      await shotAll('03-stuck', pages)
      throw e
    }
    // The party first: within one poll of the arrival, before a Spin battle on the grass can have run a turn.
    const after = await readParty()
    step(`party after: ${JSON.stringify(after)}`)
    // Back on the start tile is judged by Spin's own check, which reads the position against the captured start
    // before its first press; the RAM tile reads (0,0) once a battle is up, so it is logged only.
    const spinCheck = /\[Spin\] facing=|\[Spin\] The player moved while turning\./
    if (backendLines(/Clearing navigation path \(arrived back at start\)/).length > 0) {
      try {
        await expect.poll(() => backendLines(spinCheck).length > 0, { timeout: 60_000, intervals: [1000] }).toBe(true)
      } catch {
        throw new Error(`no [Spin] facing= or moved-while-turning line within 60 s of the arrival; last lines: ${backendLines(/\[Spin\]|\[Orchestrator\] State:/).slice(-5).join(' | ')}`)
      }
    }
    await shotAll('03-returned', pages)
    step(`end tile ${await readTile()} (not asserted)`)

    // 10. What the navigator did, then the asserts.
    for (const line of backendLines(/\[Navigator\]|\[GameState\]|\[Spin\]|\[Orchestrator\]|crashed/)) step(`backend: ${line}`)
    const log = fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
    const at = (text: string): number => log.findIndex((l) => l.includes(text))
    const indicesOf = (text: string): number[] => log.flatMap((l, i) => (l.includes(text) ? [i] : []))

    // Every battle between the start tile and the arrival, before or after the heal.
    const captureAt = at('[Orchestrator] Start tile captured:')
    const healAt = at('[Navigator] Healing with')
    const ret = at('[Navigator] Returning to start')
    const home = at('Clearing navigation path (arrived back at start)')
    const battles = indicesOf('[Orchestrator] State: BattleStart').filter((i) => i > captureAt && (home === -1 || i < home))
    for (const i of battles) step(`battle at line ${i}, ${healAt !== -1 && i > healAt ? 'after' : 'before'} the heal`)
    const battlesAfterHeal = battles.filter((i) => healAt !== -1 && i > healAt)

    const crashAt = log.findIndex((l) => l.includes('crashed'))
    expect(crashAt, `event crashed: ${log[crashAt + 1]}`).toBe(-1)
    expect(backendLines(/GameStateTimeoutException/), 'a stale-timeout fired').toEqual([])
    expect(indicesOf('[Spin] The player moved while turning.'), 'Spin found the player off the start tile').toEqual([])
    expect(await resume.isVisible(), 'event parked in Waiting').toBe(false)
    expect(backendLines(/\[Navigator\] Healing with/).length, 'not exactly one heal').toBe(1)
    expect(backendLines(/Movement blocked|abandoning the route/), 'the stall fired').toEqual([])

    // Each warp wait logs every (Screen, map) change; its last line must be on its destination map with the field free.
    const warps = backendLines(/\[Navigator\] Warp to map/)
    expect(warps.filter((l) => l.includes('Screen = FieldTask')).length, 'fewer than two door warps seen as FieldTask').toBeGreaterThanOrEqual(2)
    const destOf = (line: string): string => /Warp to map (\d+(?:\/\d+)*):/.exec(line)![1]
    warps.forEach((line, i) => {
      if (i + 1 === warps.length || destOf(warps[i + 1]) !== destOf(line)) {
        expect(line, `warp wait to map ${destOf(line)} did not end there with the field free`).toMatch(/on map \d+, .*Screen = FieldFree/)
        expect(destOf(line).split('/'), `warp wait to map ${destOf(line)} did not end there with the field free`).toContain(/on map (\d+),/.exec(line)![1])
      }
    })

    // The town sign on the walk back: seen once, one B at its wait native, closed with the field free, the tile penalised.
    const seen = indicesOf('[Navigator] Sign seen:')
    const battleOnReturn = battles.filter((i) => i > ret)
    expect(seen.length, battleOnReturn.length > 0 && seen.length === 0
      ? `no sign seen, and a battle started on the walk back (line ${battleOnReturn[0]}): rerun, this is not a fix failure`
      : 'not exactly one sign seen').toBe(1)
    expect(seen[0] > ret && seen[0] < home, 'the sign was not seen on the walk back').toBe(true)
    const pressed = indicesOf('[Navigator] Sign: ')
    expect(pressed.length, 'not exactly one sign press line').toBe(1)
    expect(log[pressed[0]]).toMatch(/Sign: T\.B at native 0x020415E1 after 1 press\(es\), confirmed by /)
    const closed = indicesOf('[Navigator] Sign closed:')
    expect(closed.length, 'not exactly one sign closed line').toBe(1)
    expect(log[closed[0]], 'the sign did not close to the free field').toContain('Screen = FieldFree')
    const walkedUnder = indicesOf('[Navigator] Walked under a sign at map=73 (488,264)')
    expect(walkedUnder.length, 'not exactly one penalty on the sign tile').toBe(1)
    expect([seen[0], pressed[0], closed[0], walkedUnder[0], home], 'the sign lines are out of order')
      .toEqual([seen[0], pressed[0], closed[0], walkedUnder[0], home].slice().sort((a, b) => a - b))

    // Back on the tile the run started from and spinning, with the party healed.
    const captured = backendLines(/\[Orchestrator\] Start tile captured:/)
    expect(captured.length, 'no start tile captured').toBeGreaterThan(0)
    expect(captured[0], 'captured start tile differs from the live start tile').toContain(startTile)
    expect(indicesOf('[Spin] facing=').some((i) => i > home), 'no Spin turn on the start tile after the arrival').toBe(true)
    if (battlesAfterHeal.length === 0) {
      for (const p of after) expect(p.hp, `slot ${p.slot} ${p.name} not healed`).toBe(p.maxHp)
    } else {
      step(`a battle after the heal (${battlesAfterHeal.length}): full HP not asserted; the heal is judged by the nurse's script`)
      expect(backendLines(/\[Navigator\] the nurse's script: Yes at the menu native/).length, 'not exactly one Yes in the nurse\'s script').toBe(1)
    }

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
