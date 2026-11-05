import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU16, readU32, repoDir, runDir, shotAll,
  startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'

// hgss.qa-primo: five Pokémon, facing Primo. Two not-shiny cycles, each ending in a soft reset back to Primo.
const ROM = 'SS-Slugma-Room'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const ANCHOR_ADDR = 0x21d4158
const MAP_ID_OFFSET = -0x22da4
const X_ADDR = 0x21da6f4
const Y_ADDR = 0x21da6fc
const PSQL = 'C:\\Tools\\pgsql\\bin\\psql.exe'
const ACTION = 'HGSS Primo Egg'
const PHASE = 'Primo Slugma'
const CYCLES = 2
const MINUTE = 60 * 1000
const CYCLE_BUDGET = 20 * MINUTE

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const backendLog = (): string[] => fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
const backendLines = (pattern: RegExp): string[] => backendLog().filter((l) => pattern.test(l))
// A crash, or a stale-timeout or the walk's stop (both end in the crash line, with the reason on the lines after it).
const failures = (): string[] => backendLines(/\[BotService\] Event '.*' crashed|stalled|stopped at/)
const hex8 = (pid: number): string => (pid >>> 0).toString(16).toUpperCase().padStart(8, '0')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

async function readTile(): Promise<{ map: number; x: number; y: number }> {
  const map = await readU16((await readU32(ANCHOR_ADDR)) + MAP_ID_OFFSET)
  return { map, x: toS16(await readU32(X_ADDR)), y: toS16(await readU32(Y_ADDR)) }
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

test('HGSS Primo Egg through the dashboard: two full not-shiny cycles, every gate, back on Primo\'s tile each time', async () => {
  test.setTimeout(55 * MINUTE)
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

    // 3. Backend, dashboard, Bot Control.
    backend = await startBackend()
    step('backend up on localhost:5000')
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const pages: Record<string, Page> = { admin }
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()

    // 4. Launch Bot + Continue on the save facing Primo.
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const picker = card.getByRole('button', { name: ACTION, exact: true })
    await expect(picker).toBeVisible({ timeout: 5 * 60 * 1000 })
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    await shotAll('01-launched', pages)

    // 5. Run HGSS Primo Egg.
    await picker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption(PHASE)
    const runStart = new Date().toISOString()
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    await expect(picker).toHaveCount(0, { timeout: 30_000 })
    step(`HGSS Primo Egg started at ${runStart}`)

    // 6. Two cycles: each ends at its hatch line, then the soft reset puts the player back on Primo's tile.
    const hatchSeen: number[] = []
    let lastProgress = ''
    for (let cycle = 1; cycle <= CYCLES; cycle++) {
      while (backendLines(/\[EggHatch\] slot \d+: /).length < cycle) {
        expect(failures(), `cycle ${cycle}: the event failed; see backend.log`).toEqual([])
        const progress = backendLines(/\[PrimoEgg\]|\[EggHatch\]/).at(-1)?.trim() ?? ''
        if (progress !== lastProgress) step(`backend: ${(lastProgress = progress)}`)
        await sleep(2000)
      }
      hatchSeen.push(Date.now())
      step(`cycle ${cycle}: ${backendLines(/\[EggHatch\] slot \d+: /)[cycle - 1].trim()}`)
      await shotAll(`0${cycle + 1}-hatched-${cycle}`, pages)

      // The soft reset and the continue, then the tile facing Primo (the event's own check covers the facing).
      const deadline = Date.now() + 3 * MINUTE
      let tile = { map: -1, x: -1, y: -1 }
      while (!(tile.map === 158 && tile.x === 12 && tile.y === 12)) {
        expect(failures(), `cycle ${cycle}: the event failed after the hatch; see backend.log`).toEqual([])
        if (Date.now() > deadline) throw new Error(`cycle ${cycle}: not back on map 158 (12, 12) within 3 min; last read ${JSON.stringify(tile)}`)
        await sleep(1000)
        try {
          tile = await readTile()
        } catch {
          // The soft reset: nothing reads until the save is loaded again.
        }
      }
      step(`cycle ${cycle}: back on Primo's tile ${JSON.stringify(tile)}`)
      await shotAll(`0${cycle + 1}-back-at-primo-${cycle}`, pages)
    }

    // 7. Cancel the event, then the bot; nothing was saved in game.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(picker).toBeVisible({ timeout: 60_000 })
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('event and bot cancelled; no EmuHawk running')
    await shotAll('04-cancelled', pages)
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 8. NFR-1: each cycle under 20 minutes (cycle 1 from the Run click, cycle 2 from hatch 1).
    const durations = [hatchSeen[0] - Date.parse(runStart), hatchSeen[1] - hatchSeen[0]]
    step(`cycle times: ${durations.map((ms) => `${(ms / MINUTE).toFixed(1)} min`).join(', ')}`)
    durations.forEach((ms, i) => expect(ms, `cycle ${i + 1} took over 20 minutes`).toBeLessThan(CYCLE_BUDGET))

    // 9. NFR-2: every gate in each cycle, in order, between the previous hatch line and this one.
    const log = backendLog()
    const hatchAt = log.flatMap((l, i) => (/\[EggHatch\] slot \d+: /.test(l) ? [i] : []))
    const pids: string[] = []
    for (let cycle = 1; cycle <= CYCLES; cycle++) {
      const start = cycle === 1 ? 0 : hatchAt[cycle - 2] + 1
      const lines = log.slice(start, hatchAt[cycle - 1] + 1)
      const at = (pattern: RegExp): number => lines.findIndex((l) => pattern.test(l))
      const egg = lines[at(/\[PrimoEgg\] egg in slot/)]?.match(/egg in slot 6, pid=([0-9A-F]{8})/)?.[1]
      expect(egg, `cycle ${cycle}: no "egg in slot 6" line`).toBeDefined()
      pids.push(egg!)
      const gates: [string, number][] = [
        ['egg in slot 6', at(/\[PrimoEgg\] egg in slot 6/)],
        ['navigator path', at(/\[Navigator\] Return path found: /)],
        ['Center door crossed', at(/\[Navigator\] Portal crossed: /)],
        ['on the line', at(/\[PrimoEgg\] on the line at map 73, tile \(469, 258\)/)],
        ['day care step counter', at(/\[EggHatch\] day care step counter \d+/)],
        ['A at "Oh?" (script 2021)', at(/\[EggHatch\] H\.A\.4: /)],
        ['A on No', at(/\[EggHatch\] W\.5;H\.A\.4: /)],
        ['hatch line', at(new RegExp(`\\[EggHatch\\] slot 6: \\S+ \\(species 218\\), pid ${egg}`))]
      ]
      for (const [name, i] of gates) step(`cycle ${cycle}: ${name}: ${i >= 0 ? lines[i].trim() : 'MISSING'}`)
      for (const line of lines.filter((l) => /\[EggHatch\] W\.5;H\.Down\.4: /.test(l))) step(`cycle ${cycle}: cursor to No: ${line.trim()}`)
      for (const line of lines.filter((l) => /\[Navigator\] Movement blocked/.test(l))) step(`cycle ${cycle}: ${line.trim()}`)
      step(`cycle ${cycle}: ${lines.filter((l) => /\[EggHatch\] day care step counter/.test(l)).length} counter lines`)
      expect(gates.filter(([, i]) => i < 0).map(([name]) => name), `cycle ${cycle}: gate lines missing`).toEqual([])
      const order = gates.map(([, i]) => i)
      expect([...order].sort((a, b) => a - b), `cycle ${cycle}: the gates are out of order`).toEqual(order)
    }
    expect(pids[0], 'the two eggs have the same PID').not.toBe(pids[1])

    // 10. Closing Electron finalises the videos, then the documented shutdown, so the encounters are written.
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

    // 11. Two Slugma rows for the milestone, with the logged PIDs in order.
    const rows = psql(pg, `SELECT "PID","Species" FROM encounters WHERE "Milestone"='${PHASE}' AND "Timestamp" >= '${runStart}' ORDER BY "Timestamp"`)
    step(`encounter rows since the Run: ${rows.join(' ; ')}`)
    expect(rows.map((r) => { const [pid, species] = r.split('|'); return [hex8(Number(pid)), species] }), 'the encounter rows')
      .toEqual(pids.map((pid) => [pid, '218']))
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
