import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU16, readU32, repoDir, runDir, shotAll,
  startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'

// hgss.qa-togepi: on the Togepi hunt's start tile in the Violet Mart (map 157, (6, 9), facing up) with Elm's aide
// there and the egg not yet received; the save the hunt itself made there, so it does not save again.
const ROM = 'SS-Togepi'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const ANCHOR_ADDR = 0x21d4158
const MAP_ID_OFFSET = -0x22da4
const X_ADDR = 0x21da6f4
const Y_ADDR = 0x21da6fc
const PSQL = 'C:\\Tools\\pgsql\\bin\\psql.exe'
const ACTION = 'HGSS Togepi Egg'
const PHASE = 'Togepi Egg'
const MINUTE = 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const backendLog = (): string[] => fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
const backendLines = (pattern: RegExp): string[] => backendLog().filter((l) => pattern.test(l))
const crashed = (): string[] => backendLines(/\[BotService\] Event '.*' crashed/)
const hex8 = (pid: number): string => (pid >>> 0).toString(16).toUpperCase().padStart(8, '0')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

async function readTile(): Promise<{ map: number; x: number; y: number }> {
  const map = await readU16((await readU32(ANCHOR_ADDR)) + MAP_ID_OFFSET)
  return { map, x: toS16(await readU32(X_ADDR)), y: toS16(await readU32(Y_ADDR)) }
}

/** Waits up to timeoutMs for the first line matching pattern, failing at once on a crash line. */
async function waitForLine(pattern: RegExp, timeoutMs: number): Promise<string> {
  const deadline = Date.now() + timeoutMs
  while (true) {
    expect(crashed(), 'the event crashed; see backend.log').toEqual([])
    const line = backendLines(pattern)[0]
    if (line) return line.trim()
    if (Date.now() > deadline) throw new Error(`no ${pattern} line within ${timeoutMs / 1000} s`)
    await sleep(2000)
  }
}

// --- Postgres: the connection from the backend's user secrets, then appsettings.json; never logged ---
function pgEnv(): NodeJS.ProcessEnv {
  const secrets = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(repoDir, 'ShinyBotHunter')], { encoding: 'utf8' }).stdout
  const appsettings = fs.readFileSync(path.join(repoDir, 'ShinyBotHunter', 'appsettings.json'), 'utf8')
  const key = 'ConnectionStrings:DefaultConnection'
  const secret = secrets.split(/\r?\n/).find((l) => l.startsWith(`${key} = `))
  const json = appsettings.match(/"DefaultConnection"\s*:\s*"([^"]+)"/)
  const conn = secret ? secret.slice(key.length + 3).trim() : json?.[1]
  if (!conn) throw new Error(`no ${key} in the backend user secrets or appsettings.json`)
  const parts = Object.fromEntries(conn.split(';').filter((p) => p.includes('='))
    .map((p) => [p.slice(0, p.indexOf('=')).trim().toLowerCase(), p.slice(p.indexOf('=') + 1).trim()]))
  return {
    ...process.env, PGTZ: 'UTC', PGHOST: parts.host ?? parts.server, PGPORT: parts.port ?? '5432', PGDATABASE: parts.database,
    PGUSER: parts.username ?? parts['user id'], PGPASSWORD: parts.password
  }
}
function psql(env: NodeJS.ProcessEnv, sql: string): string[] {
  const res = spawnSync(PSQL, ['-At', '-F', '|', '-c', sql], { env, encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`psql exited with ${res.status}: ${res.stderr}`)
  return res.stdout.split(/\r?\n/).filter((l) => l !== '')
}

test('HGSS Togepi Egg through the dashboard: one full cycle on the shared hatch step, no stall line', async () => {
  test.setTimeout(35 * MINUTE)
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(fs.existsSync(SAVE), `missing ${SAVE}: hgss.qa-togepi is not installed (docs/specs/qa-automation.md, "QA save files")`).toBe(true)
  expect(fs.existsSync(SAVE + '.bak'), `missing ${SAVE}.bak: hgss.qa-togepi's .bak is not installed`).toBe(true)
  const pg = pgEnv()
  try {
    // 1. Build the dashboard so out/ carries the current test ids.
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)
    step(`SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')}`)

    // 2. Backend, dashboard, Bot Control.
    backend = await startBackend()
    step('backend up on localhost:5000')
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const pages: Record<string, Page> = { admin }
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()

    // 3. Launch Bot + Continue on the save at the start tile.
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const picker = card.getByRole('button', { name: ACTION, exact: true })
    await expect(picker).toBeVisible({ timeout: 5 * 60 * 1000 })
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    step(`launched at ${JSON.stringify(await readTile())}`)
    await shotAll('01-launched', pages)

    // 4. Run HGSS Togepi Egg.
    await picker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption(PHASE)
    const runStart = new Date().toISOString()
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    await expect(picker).toHaveCount(0, { timeout: 30_000 })
    step(`HGSS Togepi Egg started at ${runStart}`)

    // 5. The start tile with no save, then the egg from the existing receive macro.
    const atStart = await waitForLine(/\[TogepiEgg\] (Already at|At map=|At the start position; saving)/, 2 * MINUTE)
    step(`logged: ${atStart}`)
    expect(atStart, 'the save is not on the start tile, so the hunt walks and saves').toContain('[TogepiEgg] Already at the start position')
    const eggLine = await waitForLine(/\[TogepiEgg\] Egg in party slot/, 3 * MINUTE)
    step(`logged: ${eggLine}`)
    const [, slot, pid] = eggLine.match(/Egg in party slot (\d), pid=([0-9A-F]{8})/)!
    await shotAll('02-egg', pages)

    // 6. Pacing to the hatch, then the hatch on the game's own signals (FR-17).
    const hatchLine = await waitForLine(/\[EggHatch\] slot \d+: /, 25 * MINUTE)
    step(`logged: ${hatchLine}`)
    expect(hatchLine, 'not the Togepi from the egg').toContain(`[EggHatch] slot ${slot}: Togepi (species 175), pid ${pid}`)
    step(`logged: ${await waitForLine(/\[TogepiEgg\] Hatched Togepi/, MINUTE)}`)
    await shotAll('03-hatched', pages)

    // 7. The soft reset puts the player back on the start tile.
    const deadline = Date.now() + 3 * MINUTE
    let tile = { map: -1, x: -1, y: -1 }
    while (!(tile.map === 157 && tile.x === 6 && tile.y === 9)) {
      expect(crashed(), 'the event crashed after the hatch; see backend.log').toEqual([])
      if (Date.now() > deadline) throw new Error(`not back on map 157 (6, 9) within 3 min; last read ${JSON.stringify(tile)}`)
      await sleep(1000)
      try {
        tile = await readTile()
      } catch {
        // The soft reset: nothing reads until the save is loaded again.
      }
    }
    step(`back on the start tile ${JSON.stringify(tile)}`)
    await shotAll('04-back-at-start', pages)

    // 8. The gates in order, and no stall line anywhere.
    const log = backendLog()
    const at = (pattern: RegExp): number => log.findIndex((l) => pattern.test(l))
    const gates: [string, number][] = [
      ['already at the start', at(/\[TogepiEgg\] Already at the start position/)],
      ['egg in party', at(/\[TogepiEgg\] Egg in party slot/)],
      ['day care step counter', at(/\[EggHatch\] day care step counter \d+/)],
      ['A at "Oh?" (script 2021)', at(/\[EggHatch\] H\.A\.4: /)],
      ['A on No', at(/\[EggHatch\] W\.5;H\.A\.4: /)],
      ['hatch line', at(/\[EggHatch\] slot \d+: Togepi/)],
      ['hatched', at(/\[TogepiEgg\] Hatched Togepi/)]
    ]
    for (const [name, i] of gates) step(`${name}: ${i >= 0 ? log[i].trim() : 'MISSING'}`)
    for (const line of log.filter((l) => /\[EggHatch\] W\.5;H\.Down\.4: /.test(l))) step(`cursor to No: ${line.trim()}`)
    expect(gates.filter(([, i]) => i < 0).map(([name]) => name), 'gate lines missing').toEqual([])
    const order = gates.map(([, i]) => i)
    expect([...order].sort((a, b) => a - b), 'the gates are out of order').toEqual(order)
    expect(backendLines(/stall/i), 'a stall line in backend.log').toEqual([])

    // 9. Cancel the event, then the bot; nothing was saved in game.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(picker).toBeVisible({ timeout: 60_000 })
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('event and bot cancelled; no EmuHawk running')
    await shotAll('05-cancelled', pages)
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 10. Closing Electron finalises the videos, then the documented shutdown, so the encounter is written.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)

    // 11. One Togepi row for the milestone, with the egg's PID.
    const rows = psql(pg, `SELECT "PID","Species" FROM encounters WHERE "Milestone"='${PHASE}' AND "Timestamp" >= '${runStart}'`)
    step(`encounter rows since the Run: ${rows.join(' ; ')}`)
    expect(rows.map((r) => { const [p, species] = r.split('|'); return [hex8(Number(p)), species] }), 'the encounter rows').toEqual([[pid, '175']])
  } finally {
    // Failure path: leave nothing running. A forced kill does not flush SaveRAM.
    await stopRecorder()
    if (app) await app.close().catch(() => undefined)
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    if (emuHawkRunning()) killEmuHawk()
    if (fs.existsSync(SAVE) && fs.existsSync(SAVE + '.bak')) {
      const same = fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak'))
      step(`after teardown: SaveRAM ${same ? 'equals' : 'DIFFERS FROM'} .bak (SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')})`)
    }
  }
})
