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
const SAVERAM = path.join('C:\\BizHawk-2.11', 'GBA', 'SaveRAM')
const HUNT_SAVE = path.join(SAVERAM, `${ROM}.SaveRAM`)
const BACKUP = HUNT_SAVE + '.wishmaker.bak'
const RESULT_SAVE = path.join(SAVERAM, `${ROM}-WISHMKR.SaveRAM`)
// Beside the main checkout, found through git so a worktree resolves it too.
const MAIN_CHECKOUT = path.dirname(spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: repoDir, encoding: 'utf8' }).stdout.trim())
const PRISTINE = path.join(MAIN_CHECKOUT, '..', 'shinybothunterqasaves', 'rs', 'qa-jirachi.SaveRAM')
const PSQL = 'C:\\Tools\\pgsql\\bin\\psql.exe'
const ACTION = 'Wishmaker Jirachi Hunt'
const PHASE = 'Jirachi'
const MINUTE = 60 * 1000
const HOUR = 60 * MINUTE
const CYCLE_LIMIT_MS = 300 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const backendLines = (pattern: RegExp): string[] =>
  fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/).filter((l) => pattern.test(l))
const hex8 = (pid: number): string => (pid >>> 0).toString(16).toUpperCase().padStart(8, '0')

// --- Gen 3 sections: 14 of 0x1000 per block; footer id 0xFF4, checksum 0xFF6, signature 0xFF8, counter 0xFFC ---
const SECTION_DATA_SIZES = [0xf2c, 0xf80, 0xf80, 0xf80, 0xf08, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0x7d0]
function sections(save: Buffer, block: number): { id: number; stored: number; counter: number; signed: boolean; valid: boolean }[] {
  return Array.from({ length: 14 }, (_, i) => {
    const start = block + i * 0x1000
    const id = save.readUInt16LE(start + 0xff4)
    const stored = save.readUInt16LE(start + 0xff6)
    const signed = save.readUInt32LE(start + 0xff8) === 0x08012025
    let sum = 0
    if (id < 14) for (let w = 0; w < SECTION_DATA_SIZES[id]; w += 4) sum = (sum + save.readUInt32LE(start + w)) >>> 0
    const valid = signed && id < 14 && stored === (((sum >>> 16) + (sum & 0xffff)) & 0xffff)
    return { id, stored, counter: save.readUInt32LE(start + 0xffc), signed, valid }
  })
}
const firstBlockMaxCounter = (save: Buffer): number => Math.max(...sections(save, 0).map((s) => s.counter))
const firstBlockSeed = (save: Buffer): number => sections(save, 0).find((s) => s.id === 0)!.stored
// The live block is the one whose section 0 has the higher counter; on a tie the second, as Gen3SaveFile reads it.
function liveBlockComplete(save: Buffer): boolean {
  const zero = (block: number): number | undefined => sections(save, block).find((s) => s.signed && s.id === 0)?.counter
  const [a, b] = [zero(0), zero(0xe000)]
  const live = b !== undefined && (a === undefined || b >= a) ? 0xe000 : 0
  return sections(save, live).every((s) => s.valid)
}

// --- Postgres through psql, with the backend's own connection string; its values are never logged ---
function pgEnv(): NodeJS.ProcessEnv {
  const secrets = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(repoDir, 'ShinyBotHunter')], { encoding: 'utf8' }).stdout
  const prefix = 'ConnectionStrings:DefaultConnection = '
  const line = secrets.split(/\r?\n/).find((l) => l.startsWith(prefix))
  if (!line) throw new Error('no ConnectionStrings:DefaultConnection in the backend user secrets')
  const parts = Object.fromEntries(line.slice(prefix.length).split(';').filter((p) => p.includes('='))
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
const jirachiQuery = (since: string): string =>
  `SELECT "PID","Timestamp","IsShiny" FROM encounters WHERE "Species"=385 AND "Milestone"='${PHASE}' AND "Timestamp" >= '${since}' ORDER BY "Timestamp"`
// psql prints timestamptz under PGTZ=UTC as "2026-10-25 13:34:29.123+00".
const toMs = (text: string): number => Date.parse(text.replace(' ', 'T').replace(/([+-]\d\d)$/, '$1:00'))

test('RS Wishmaker Jirachi through the dashboard: one hour of cycles, cancel during the Dolphin half', async () => {
  test.setTimeout(90 * MINUTE)
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator or restores over a save this run did not touch.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(dolphinRunning(), 'a Dolphin is already running; close it first').toBe(false)
  expect(fs.existsSync(PRISTINE), `missing ${PRISTINE}`).toBe(true)
  const pg = pgEnv()
  try {
    // 1. Install the pristine hunt save; the backup, the result save and BizHawk's .bak of it left by an earlier run are QA artefacts.
    for (const stale of [BACKUP, RESULT_SAVE, RESULT_SAVE + '.bak']) {
      if (fs.existsSync(stale)) {
        fs.rmSync(stale)
        step(`deleted stale ${stale}`)
      }
    }
    fs.copyFileSync(PRISTINE, HUNT_SAVE)
    const pristineHash = sha256(PRISTINE)
    expect(sha256(HUNT_SAVE), 'installed hunt save differs from the pristine copy').toBe(pristineHash)
    const before = firstBlockMaxCounter(fs.readFileSync(HUNT_SAVE))
    step(`hunt save installed: sha256 ${pristineHash}, first-block counter ${before}`)

    // 2. Build the dashboard, start the backend and the dashboard.
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)
    backend = await startBackend()
    step('backend up on localhost:5000')
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const pages = { admin }

    // 3. Start the hunt from Bot Control. There is no emulator recorder: both emulators restart every cycle.
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    const form = admin.getByTestId(`top-level-action-${ACTION}`)
    await form.locator('select').nth(0).selectOption(ROM)
    await form.locator('select').nth(1).selectOption(PHASE)
    const runStart = new Date().toISOString()
    const startedAt = Date.now()
    await form.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.locator('[data-testid^="emulator-card-"]').first()
    await expect(card).toBeVisible({ timeout: 5 * MINUTE })
    await expect(form.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * MINUTE })
    step(`hunt started at ${runStart}`)
    await shotAll('01-started', pages)

    // 4. The hour: fail at once on a crashed event; report progress every 30 s and a screenshot every 15 min.
    let shot = 1
    while (Date.now() < startedAt + HOUR) {
      await admin.waitForTimeout(30_000)
      expect(backendLines(/\[BotService\] Event '.*' crashed/), 'the event crashed; see backend.log').toEqual([])
      const last = backendLines(/\[Wishmaker\] predicted/).at(-1) ?? 'none yet'
      step(`${psql(pg, jirachiQuery(runStart)).length} Jirachi rows; last: ${last.trim()}`)
      if (Date.now() >= startedAt + shot * 15 * MINUTE) await shotAll(`02-hunting-${shot++}`, pages)
    }

    // 5. FR-19: cancel during the Dolphin half of the next cycle.
    const entriesBefore = backendLines(/Bonus Disc: on the Jirachi entry/).length
    await expect.poll(() => backendLines(/Bonus Disc: on the Jirachi entry/).length, { timeout: 5 * MINUTE, intervals: [500] })
      .toBeGreaterThan(entriesBefore)
    const snapshot = fs.readFileSync(HUNT_SAVE)
    const snapshotHash = sha256(HUNT_SAVE)
    const lastSeed = backendLines(/\[Wishmaker\] Seed [0-9A-F]{4} predicts/).at(-1)!.match(/Seed ([0-9A-F]{4})/)![1]
    step(`on the Jirachi entry: hunt save sha256 ${snapshotHash}, seed ${firstBlockSeed(snapshot).toString(16).toUpperCase()}, last logged seed ${lastSeed}`)
    expect(firstBlockSeed(snapshot), 'the hunt save is not the one the cycle predicted from').toBe(parseInt(lastSeed, 16))
    expect(dolphinRunning(), 'Dolphin is not running at the Jirachi entry').toBe(true)
    expect(emuHawkRunning(), 'EmuHawk is running during the Dolphin half').toBe(false)
    const predictedAtCancel = backendLines(/\[Wishmaker\] predicted/).length
    const cancelAt = new Date().toISOString()
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(admin.locator('[data-testid^="emulator-card-"]')).toHaveCount(0, { timeout: 60_000 })
    await expect.poll(() => emuHawkRunning() || dolphinRunning(), { timeout: 60_000 }).toBe(false)
    step('cancelled during the Dolphin half; no EmuHawk or Dolphin running')
    await shotAll('03-cancelled', pages)
    expect(sha256(HUNT_SAVE), 'the cancel changed the hunt save').toBe(snapshotHash)
    expect(liveBlockComplete(fs.readFileSync(HUNT_SAVE)), 'the hunt save has no complete live block after the cancel').toBe(true)
    expect(backendLines(/\[Wishmaker\] predicted/).length, 'a cycle finished after the cancel').toBe(predictedAtCancel)

    // 6. The documented shutdown, so every queued encounter is written before Postgres is read.
    await app.close()
    app = null
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)

    // 7. Postgres: the rows, then every assert over them and the log.
    const rows = psql(pg, jirachiQuery(runStart)).map((l) => {
      const [pid, at, shiny] = l.split('|')
      return { pid: hex8(Number(pid)), at: toMs(at), shiny: shiny === 't' }
    })
    for (const row of rows) step(`row ${row.pid} ${new Date(row.at).toISOString()}${row.shiny ? ' SHINY' : ''}`)
    expect(psql(pg, `SELECT 1 FROM encounters WHERE "Species"=385 AND "Milestone"='${PHASE}' AND "Timestamp" >= '${cancelAt}'`),
      'an encounter row was written after the cancel').toEqual([])
    expect(rows.length, 'fewer than 20 Jirachi rows in the hour').toBeGreaterThanOrEqual(20)
    expect(new Set(rows.map((r) => r.pid)).size, 'a PID repeated').toBe(rows.length)

    const predictions = backendLines(/\[Wishmaker\] predicted [0-9A-F]{8} got [0-9A-F]{8}/)
      .map((l) => l.match(/predicted ([0-9A-F]{8}) got ([0-9A-F]{8})/)!)
    for (const [, predicted, got] of predictions) expect(got, `predicted ${predicted} got ${got}`).toBe(predicted)
    expect(predictions.map((m) => m[2]), 'the logged PIDs are not the rows, in order').toEqual(rows.map((r) => r.pid))
    expect(backendLines(/in the field after/).length, 'a cycle has no "in the field" line').toBeGreaterThanOrEqual(rows.length)
    expect(backendLines(/Bonus Disc: power prompt/).length, 'a cycle has no "power prompt" line').toBeGreaterThanOrEqual(rows.length)
    expect(backendLines(/\[Wishmaker\] summary on view/).length, 'a cycle has no "summary on view" line').toBeGreaterThanOrEqual(rows.length)

    const gaps = rows.map((r, i) => r.at - (i === 0 ? startedAt : rows[i - 1].at))
    step(`cycle gaps (s): ${gaps.map((g) => (g / 1000).toFixed(0)).join(', ')}; max ${(Math.max(...gaps) / 1000).toFixed(0)}`)
    expect(Math.max(...gaps), 'a cycle took over 300 s').toBeLessThanOrEqual(CYCLE_LIMIT_MS)
    expect(backendLines(/over the 300 s budget/), 'the backend logged a cycle over budget').toEqual([])

    expect(backendLines(/Backed up the hunt save/).length, 'not exactly one backup line').toBe(1)
    expect(sha256(BACKUP), 'the .wishmaker.bak is not the pristine save').toBe(pristineHash)
    const after = firstBlockMaxCounter(fs.readFileSync(HUNT_SAVE))
    step(`first-block counter ${before} -> ${after} (delta ${after - before}) over ${rows.length} rows`)
    expect(after - before, 'first-block counter delta below the row count').toBeGreaterThanOrEqual(rows.length)
    expect(after - before, 'first-block counter delta above 2 x (rows + 1)').toBeLessThanOrEqual(2 * (rows.length + 1))
    expect(fs.existsSync(RESULT_SAVE), `missing ${RESULT_SAVE}`).toBe(true)
  } finally {
    // Leave nothing running, keep the end state as evidence, then put the pristine save back. This hunt saves
    // in-game by design, so the restored hash replaces the usual "save equals .bak" check.
    if (emuHawkRunning()) killEmuHawk()
    if (dolphinRunning()) killDolphin()
    if (app) await app.close().catch(() => undefined)
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    for (const file of [HUNT_SAVE, BACKUP, RESULT_SAVE]) {
      if (fs.existsSync(file)) fs.copyFileSync(file, path.join(runDir, path.basename(file)))
    }
    fs.copyFileSync(PRISTINE, HUNT_SAVE)
    step(`hunt save restored: sha256 ${sha256(HUNT_SAVE)}, pristine sha256 ${sha256(PRISTINE)}`)
    expect(sha256(HUNT_SAVE), 'the restored hunt save differs from the pristine copy').toBe(sha256(PRISTINE))
  }
})
