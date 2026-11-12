// The pieces the hgss-static scenarios share (#350): the [Static] log lines and their parsers, the pristine save restore,
// the launch/run/cancel/teardown frame, the RAM reads and the per-attempt log check. Four specs call each of them, so
// unlike headbutt.ts the frame lives here once (docs/specs/sinjoh-static.md, QA).
import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, type ElectronApplication, type Locator, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killTree, launchDashboard, readMemory, readU16, readU32, repoDir, runDir, startBackend,
  startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import { MON_SIZE } from './egg-pid'

// --- constants ---------------------------------------------------------------------------------------

export const ROM = 'HG-Static'
// hgss.qa-route33, off the stage, for the map refusal.
export const ROUTE33_ROM = 'Pokemon HeartGold'
export const saveOf = (rom: string): string => path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${rom}.SaveRAM`)
// Beside the main checkout, found through git so a worktree resolves it too (as rs-jirachi-setup-refusals does).
const MAIN_CHECKOUT = path.dirname(spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: repoDir, encoding: 'utf8' }).stdout.trim())
export const PRISTINE = path.join(MAIN_CHECKOUT, '..', 'shinybothunterqasaves', 'hgss', 'qa-sinjoh.SaveRAM')
const PRISTINE_SHA_PREFIX = '828531f2b0e4'

export const API = 'http://localhost:5000/api'
export const ACTION = 'HGSS Static Encounter'
export const PHASE = 'Sinjoh Static'
export const DIALGA = 'Dialga (Sinjoh Ruins)'
export const PALKIA = 'Palkia (Sinjoh Ruins)'
export const GIRATINA = 'Giratina (Sinjoh Ruins)'
export const TID = 53416
export const SID = 50630
export const MINUTE = 60 * 1000

// The overworld and the party from [anchor] (the FieldSystem *), as the backend's HGSS readers take them.
const ANCHOR_ADDR = 0x21d4158
const PARTY_COUNT_OFFSET = -0x23f44
const PARTY_OFFSET = -0x23f40
const MAP_ID_OFFSET = -0x22da4
const FACING_OFFSET = 0x1dc4
const FACINGS = ['up', 'down', 'left', 'right']
const X_ADDR = 0x21da6f4
const Y_ADDR = 0x21da6fc
// HgssPointerService.SummaryInit and PrintingNative.
const SUMMARY_INIT = 0x02088299
const PRINTING_NATIVE = '021EF349'

// The save's start tile on the Mystri Stage, and the pathfinder's walk from it, measured on the emulator.
export const START_TILE = { map: 522, x: 16, y: 14, facing: 'up' }
export const MACROS: Record<string, string> = {
  [DIALGA]: 'M.Left.12.35497716;M.Down.15.35497724',
  [PALKIA]: 'M.Right.20.35497716;M.Down.15.35497724',
  [GIRATINA]: 'M.Up.8.35497724'
}

// BotService.NotOneArceus and OffTheStage.
export const PARTY_SENTENCE = 'The party must be one Arceus and nothing else.'
export const MAP_SENTENCE = 'Stand on the Mystri Stage in the Sinjoh Ruins.'

// --- log patterns: each matches one Console.WriteLine of HgssStaticEncounterEvent or NavigatorEvent ---

// HuntAsync, once at Run (FR-12).
export const RUN_LINE = /\[Static\] (.+?): species (\d+), circle \((\d+), (\d+)\) on map (\d+); walk from \((\d+), (\d+)\) facing (\w+): (\S+)\s*$/
// HuntAsync, after SaveGame and the flush (FR-3).
export const SAVED = /\[Static\] saved at Run\s*$/
// HuntAsync, at the top of each attempt. Anchored so the result line "attempt N: ..." does not match.
export const ATTEMPT = /\[Static\] attempt (\d+)\s*$/
// WalkAsync, once the circle's script has started.
export const WALK = /\[Static\] walk (\d+) ms, script (\d+)\s*$/
// PromptAsync, after the native left the menu.
export const YES = /\[Static\] Yes\s*$/
// CutsceneAsync, at the nickname question with the party count at 2.
export const CUTSCENE = /\[Static\] cutscene (\d+) ms \(Yes to party count 2\)\s*$/
// CutsceneAsync, after the native left the menu.
export const NO = /\[Static\] No to the nickname\s*$/
// HuntAsync, after PressThroughScriptAsync("the scripts after the hand-over").
export const AFTER = /\[Static\] after the hand-over (\d+) ms, field free on map (\d+)\s*$/
// SummaryAsync, with the summary's init function read.
export const SUMMARY = /\[Static\] summary on screen (\d+) ms\s*$/
// HuntAsync, after AddEncounterAsync.
export const RESULT = /\[Static\] attempt (\d+): (.+) species (\d+), pid ([0-9A-F]{8}), shinyValue (\d+), shiny (True|False)\s*$/
// ResetAsync, back on the free field.
export const RESET = /\[Static\] reset (\d+) ms to the free field\s*$/
// HuntAsync, after FinishAsync.
export const SHINY_SAVED = /\[Static\] Shiny (.+)\. Saved\.\s*$/
// Stop.
export const STOPPED = /\[Static\] stopped: (.*)$/
// PressThroughScriptAsync: an arrow press "(printer N) after K press(es)", or a wait-button press, maybe "(decomp-only)".
export const NAV_PRESS = /\[Navigator\] (.+?): (T\.A|T\.B) at native 0x([0-9A-F]{8})(.*)$/
// PressThroughScriptAsync: its own Yes at a menu native.
export const NAV_YES = /\[Navigator\] (.+?): Yes at the menu native\s*$/
export const CRASHED = /\[BotService\] Event '.*' crashed/

export type RunLine = { name: string; species: number; circle: number[]; map: number; from: number[]; facing: string; macro: string }
export function parseRunLine(line: string): RunLine {
  const m = RUN_LINE.exec(line)
  if (!m) throw new Error(`not a Run line: ${line}`)
  return {
    name: m[1], species: Number(m[2]), circle: [Number(m[3]), Number(m[4])], map: Number(m[5]),
    from: [Number(m[6]), Number(m[7])], facing: m[8], macro: m[9]
  }
}

export type Result = { attempt: number; name: string; species: number; pid: number; shinyValue: number; shiny: boolean }
export function parseResult(line: string): Result {
  const m = RESULT.exec(line)
  if (!m) throw new Error(`not a result line: ${line}`)
  return { attempt: Number(m[1]), name: m[2], species: Number(m[3]), pid: parseInt(m[4], 16), shinyValue: Number(m[5]), shiny: m[6] === 'True' }
}

export const hex8 = (pid: number): string => (pid >>> 0).toString(16).toUpperCase().padStart(8, '0')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

/** The complete lines of backend.log: the last element of the split is a partial line, or '' after the final newline. */
export const backendLog = (): string[] => fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/).slice(0, -1)
export const backendLines = (pattern: RegExp): string[] => backendLog().filter((l) => pattern.test(l))

/** Fails at once on a stop (quoting its whole line), a crash or a stale-timeout in the log. */
export function failOnStop(log: string[]): void {
  const stopped = log.find((l) => STOPPED.test(l))
  if (stopped) throw new Error(`the hunt stopped: ${stopped.trim()}`)
  const crashed = log.find((l) => CRASHED.test(l) || l.includes('GameStateTimeoutException'))
  if (crashed) throw new Error(`the event crashed: ${crashed}`)
}

/** Polls backend.log every 250 ms until `pattern` has matched `count` lines, and returns the count-th. */
export async function waitForLine(pattern: RegExp, count: number, timeoutMs: number): Promise<string> {
  const deadline = Date.now() + timeoutMs
  while (true) {
    const log = backendLog()
    const found = log.filter((l) => pattern.test(l))
    if (found.length >= count) {
      step(`logged: ${found[count - 1].trim()}`)
      return found[count - 1]
    }
    failOnStop(log)
    if (Date.now() > deadline) {
      const last = log.filter((l) => l.includes('[Static]') || l.includes('[GameState]')).slice(-5)
      throw new Error(`${found.length} of ${count} lines matching ${pattern} within ${timeoutMs / 1000} s; last lines:\n${last.join('\n')}`)
    }
    await sleep(250)
  }
}

// --- the save: the bot saves at Run, and BizHawk's BackupSaveram moves the old SaveRAM over .bak on every flush, so
// the reference is the pristine copy, never the .bak ---------------------------------------------------

export const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
export const savedSha = (): string => sha256(saveOf(ROM))
export const pristineSha = (): string => sha256(PRISTINE)

/** Copies the pristine save over HG-Static's SaveRAM and .bak. Only with no EmuHawk running, which would overwrite them. */
export function installPristine(): void {
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(fs.existsSync(PRISTINE), `missing ${PRISTINE}`).toBe(true)
  expect(pristineSha().startsWith(PRISTINE_SHA_PREFIX), `${PRISTINE} is not the pristine qa-sinjoh save`).toBe(true)
  fs.copyFileSync(PRISTINE, saveOf(ROM))
  fs.copyFileSync(PRISTINE, saveOf(ROM) + '.bak')
  step(`pristine save installed: SaveRAM sha256 ${savedSha()}, .bak sha256 ${sha256(saveOf(ROM) + '.bak')}`)
}

/** For the finally, once EmuHawk is gone: keeps the end-state save in the run folder, then installs the pristine one. */
function keepAndRestore(): void {
  for (const file of [saveOf(ROM), saveOf(ROM) + '.bak']) {
    if (fs.existsSync(file)) fs.copyFileSync(file, path.join(runDir, path.basename(file)))
  }
  installPristine()
}

/** Asks EmuHawk to close (CloseMainWindow, which flushes SaveRAM), never kills it, and waits 60 s for it to exit. */
async function closeEmuHawk(): Promise<void> {
  spawnSync('powershell', ['-NoProfile', '-Command', 'Get-Process EmuHawk -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }'])
  const deadline = Date.now() + MINUTE
  while (emuHawkRunning() && Date.now() < deadline) await sleep(1000)
  step(`EmuHawk asked to close: ${emuHawkRunning() ? 'still running after 60 s' : 'exited'}`)
}

// --- the frame -----------------------------------------------------------------------------------------

// Filled as the frame starts, so the finally block stops exactly what this run started.
export type Frame = { backend: ChildProcess | null; app: ElectronApplication | null }

/** The dashboard build, the backend, the dashboard, Bot Control. Returns the Admin window. */
export async function startFrame(frame: Frame): Promise<Page> {
  const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
  fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
  expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)
  frame.backend = await startBackend()
  step('backend up on localhost:5000')
  frame.app = await launchDashboard(path.join(runDir, 'videos'))
  const admin = await windowByTitle(frame.app, 'Pokemon Dashboard — Admin', 60_000)
  await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
  return admin
}

/** Launch Bot + Continue on the ROM, recorded as `<recording>.mp4`; returns the card once the overworld has faded in. */
export async function launchBot(admin: Page, rom: string, recording: string): Promise<Locator> {
  const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
  await launch.locator('select').selectOption(rom)
  await launch.getByRole('button', { name: 'Start', exact: true }).click()
  const card = admin.getByTestId('emulator-card-0')
  await expect(card.getByRole('button', { name: ACTION, exact: true })).toBeVisible({ timeout: 5 * MINUTE })
  // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch request returns.
  await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * MINUTE })
  startEmulatorRecorder(recording)
  // The position reads -1 until the overworld has faded in.
  await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: MINUTE, intervals: [500] }).toBeGreaterThan(0)
  step(`${rom} launched: ${JSON.stringify(await readTile())}`)
  return card
}

/** Runs the action with the milestone and the choice from the card; returns the ISO time of the Run click. */
export async function runStatic(card: Locator, choice: string): Promise<string> {
  const picker = card.getByRole('button', { name: ACTION, exact: true })
  await picker.click()
  const phase = card.getByTestId('param-PhaseName').locator('select')
  try {
    await expect(phase.locator(`option[value="${PHASE}"]`)).toHaveCount(1, { timeout: 30_000 })
  } catch {
    throw new Error(`no \`${PHASE}\` milestone is offered; write it to Mongo (qa-automation.md, Phases)`)
  }
  await phase.selectOption(PHASE)
  await card.getByTestId('param-StaticEncounter').locator('select').selectOption(choice)
  const runStart = new Date().toISOString()
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  await expect(picker).toHaveCount(0, { timeout: 30_000 })
  step(`${ACTION} ${choice} started at ${runStart}`)
  return runStart
}

async function cancelOnce(card: Locator): Promise<void> {
  await card.getByRole('button', { name: 'Cancel', exact: true }).click()
  await card.getByRole('button', { name: 'Yes', exact: true }).click()
}

async function botGone(card: Locator): Promise<void> {
  await expect(card).toHaveCount(0, { timeout: MINUTE })
  await stopRecorder()
  await expect.poll(emuHawkRunning, { timeout: MINUTE }).toBe(false)
  step('bot cancelled; no EmuHawk running')
}

/** A running event: the first Cancel ends the event and leaves the bot idle, the second removes the bot. */
export async function cancelEventAndBot(card: Locator): Promise<void> {
  await cancelOnce(card)
  await expect(card.getByRole('button', { name: ACTION, exact: true })).toBeVisible({ timeout: MINUTE })
  step('event cancelled')
  await cancelOnce(card)
  await botGone(card)
}

/** An idle or finished bot: one Cancel removes it. */
export async function cancelBot(card: Locator): Promise<void> {
  await cancelOnce(card)
  await botGone(card)
}

/** The documented shutdown: closing Electron finalises admin.webm, then "end" to the backend. */
export async function shutdown(frame: Frame, admin: Page): Promise<void> {
  const video = admin.video()
  expect(video, 'no video for the Admin window').not.toBeNull()
  await frame.app!.close()
  frame.app = null
  await video!.saveAs(path.join(runDir, 'admin.webm'))
  step('admin.webm saved')
  const exitCode = await stopBackend(frame.backend!)
  expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
  frame.backend = null
  step(`backend exited with ${exitCode}`)
}

/**
 * The finally. EmuHawk is the backend's child and dies with its process tree, unflushed, so it is asked to close first;
 * only once it has exited is the backend killed and the save restored. If it is still running after 60 s, the backend,
 * EmuHawk and the save are all left as they are.
 */
export async function teardown(frame: Frame): Promise<void> {
  await stopRecorder()
  if (frame.app) await frame.app.close().catch(() => undefined)
  if (emuHawkRunning()) await closeEmuHawk()
  if (emuHawkRunning()) {
    step('EmuHawk still running after 60 s; the backend, EmuHawk and SaveRAM left alone')
    return
  }
  if (frame.backend && frame.backend.exitCode === null) killTree(frame.backend.pid!)
  keepAndRestore()
}

// --- RAM -------------------------------------------------------------------------------------------------

// The FieldSystem pointer reads 0 for about a second after a soft reset and through the title screen; every read that
// can run then checks it rather than reading from a negative address, which readMemory throws on.
export type Tile = { map: number; x: number; y: number; facing: string }
/** The player's map, tile and facing; map -1, which matches no tile, while there is no FieldSystem. */
export async function readTile(): Promise<Tile> {
  const fsys = await readU32(ANCHOR_ADDR)
  if (fsys === 0) return { map: -1, x: -1, y: -1, facing: 'no FieldSystem' }
  const facing = (await readU16(fsys + FACING_OFFSET)) & 0xff
  return {
    map: await readU16(fsys + MAP_ID_OFFSET), x: toS16(await readU32(X_ADDR)), y: toS16(await readU32(Y_ADDR)),
    facing: FACINGS[facing] ?? String(facing)
  }
}

export const countAddr = async (): Promise<number> => (await readU32(ANCHOR_ADDR)) + PARTY_COUNT_OFFSET
/** Party slot i, 0-based: slot 2 is i = 1. */
export const slotAddr = async (i: number): Promise<number> => (await readU32(ANCHOR_ADDR)) + PARTY_OFFSET + i * MON_SIZE
/** The party count; -1 while there is no FieldSystem. */
export async function readPartyCount(): Promise<number> {
  const fsys = await readU32(ANCHOR_ADDR)
  return fsys === 0 ? -1 : (await readMemory(fsys + PARTY_COUNT_OFFSET, 1))[0]
}
export const readSlot = async (i: number): Promise<Buffer> => readMemory(await slotAddr(i), MON_SIZE)

/** The open app's init function (app + 0x00) and state (app + 0x14), from one read; init 0 on the field or with no FieldSystem. */
async function readApp(): Promise<{ init: number; state: number }> {
  const fsys = await readU32(ANCHOR_ADDR)
  const sub0 = fsys === 0 ? 0 : await readU32(fsys)
  const app = sub0 === 0 ? 0 : await readU32(sub0 + 0x04)
  if (app === 0) return { init: 0, state: 0 }
  const bytes = await readMemory(app, 0x18)
  return { init: bytes.readUInt32LE(0), state: bytes.readUInt32LE(0x14) }
}

// The summary's app state once it is drawn; it loads in state 0 (black) and 1 first, about 1 s with the init function set.
const SUMMARY_DRAWN = 2

/**
 * Polls the app every 50 ms until the summary is drawn (its init function in state 2), logging when the init function
 * and when state 2 were first seen. At that moment it asks the recorder for the emulator frame `<shot>-emulator.png`
 * before anything else, since a not-shiny summary is on screen for well under a second. It fails if this attempt's
 * result line (one beyond `resultsBefore`) was in the log at any poll before state 2: the order proof that the summary
 * was drawn before the result was written. The bot may log the result within the poll that sees state 2, so that
 * poll's log is not held against it. The caller then calls shotPages.
 */
export async function waitForSummary(resultsBefore: number, timeoutMs: number, shot: string): Promise<void> {
  const deadline = Date.now() + timeoutMs
  let initSeenAt = 0
  let last = { init: 0, state: 0 }
  while (true) {
    last = await readApp()
    if (last.init === SUMMARY_INIT && initSeenAt === 0) initSeenAt = Date.now()
    if (last.init === SUMMARY_INIT && last.state === SUMMARY_DRAWN) {
      fs.writeFileSync(path.join(runDir, `shot-${shot}-emulator.request`), '')
      const drawnAt = Date.now()
      step(`summary init 0x${hex8(SUMMARY_INIT)} first seen ${new Date(initSeenAt).toISOString()}, state ${SUMMARY_DRAWN} first seen ` +
        `${new Date(drawnAt).toISOString()} (${drawnAt - initSeenAt} ms later); emulator screenshot ${shot} requested`)
      return
    }
    const log = backendLog()
    if (log.filter((l) => RESULT.test(l)).length > resultsBefore) {
      throw new Error(`the result line came before the summary was drawn (last init 0x${hex8(last.init)}, state ${last.state})`)
    }
    failOnStop(log)
    if (Date.now() > deadline) {
      throw new Error(`the summary not drawn within ${timeoutMs / 1000} s (last init 0x${hex8(last.init)}, state ${last.state})`)
    }
    await sleep(50)
  }
}

/** After waitForSummary: a screenshot of every dashboard window, then the wait for the requested emulator frame. */
export async function shotPages(shot: string, pages: Record<string, Page>): Promise<void> {
  for (const [window, page] of Object.entries(pages)) await page.screenshot({ path: path.join(runDir, `${shot}-${window}.png`) })
  const png = path.join(runDir, `${shot}-emulator.png`)
  const deadline = Date.now() + 5000
  while (Date.now() < deadline && !fs.existsSync(png)) await sleep(100)
  step(fs.existsSync(png) ? `screenshots ${shot}` : `screenshots ${shot}; emulator screenshot not written within 5 s`)
}

// --- the per-attempt log check -------------------------------------------------------------------------------

export type Attempt = { walkMs: number; cutsceneMs: number; afterMs: number; summaryMs: number; result: Result }

/**
 * Attempt n's lines, from its attempt line to the next one or the end of the log: walk, Yes, cutscene, No, after,
 * summary and result exactly once each, in that order. Every navigator A is at the printing native's arrow, and the
 * navigator gave no Yes of its own at either menu. The arrow presses and the retries among them go to steps.log.
 */
export function checkAttempt(log: string[], n: number): Attempt {
  const start = log.findIndex((l) => ATTEMPT.exec(l)?.[1] === String(n))
  expect(start, `no attempt ${n} line`).toBeGreaterThanOrEqual(0)
  const next = log.findIndex((l, i) => i > start && ATTEMPT.test(l))
  const lines = log.slice(start, next === -1 ? undefined : next)
  const order = [WALK, YES, CUTSCENE, NO, AFTER, SUMMARY, RESULT]
  const at = order.map((pattern) => {
    const found = lines.flatMap((l, i) => (pattern.test(l) ? [i] : []))
    expect(found.length, `attempt ${n}: not exactly one line matching ${pattern}`).toBe(1)
    return found[0]
  })
  expect(at, `attempt ${n}: the steps are out of order`).toEqual([...at].sort((a, b) => a - b))

  const arrows = lines.filter((l) => NAV_PRESS.test(l))
  for (const line of arrows) {
    const [, , button, native, rest] = NAV_PRESS.exec(line)!
    if (button === 'T.A' && (native !== PRINTING_NATIVE || !/^ \(printer \d+\) after \d+ press\(es\)/.test(rest))) {
      throw new Error(`attempt ${n}: an A not at the printing native's arrow: ${line}`)
    }
  }
  for (const line of lines.filter((l) => NAV_YES.test(l))) {
    const waitingFor = NAV_YES.exec(line)![1]
    if (waitingFor === 'the circle script' || waitingFor === 'the cutscene to reach the nickname question') {
      throw new Error(`attempt ${n}: a second answer at the menu: ${line}`)
    }
  }
  const counts = new Map<string, number>()
  for (const line of arrows) {
    const waitingFor = NAV_PRESS.exec(line)![1]
    counts.set(waitingFor, (counts.get(waitingFor) ?? 0) + 1)
  }
  step(`attempt ${n}: navigator presses ${JSON.stringify(Object.fromEntries(counts))}`)
  for (const line of arrows.filter((l) => /after ([2-9]|\d\d+) press\(es\)/.test(l))) step(`attempt ${n}: gated retry: ${line.trim()}`)

  const ms = (pattern: RegExp, i: number): number => Number(pattern.exec(lines[at[i]])![1])
  const result = parseResult(lines[at[6]])
  expect(result.attempt, `attempt ${n}: the result line names attempt ${result.attempt}`).toBe(n)
  const attempt = { walkMs: ms(WALK, 0), cutsceneMs: ms(CUTSCENE, 2), afterMs: ms(AFTER, 4), summaryMs: ms(SUMMARY, 5), result }
  step(`attempt ${n}: walk ${attempt.walkMs} ms, cutscene ${attempt.cutsceneMs} ms, after ${attempt.afterMs} ms, summary ${attempt.summaryMs} ms`)
  return attempt
}

// --- settings: user secrets first, then appsettings.json; values are never logged (copied from hgss-primo-egg-shiny) ---

// The other scenarios' path first; the PostgreSQL installer's when that one is not on this PC.
const PSQL = ['C:\\Tools\\pgsql\\bin\\psql.exe', 'C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe'].find((p) => fs.existsSync(p)) ?? 'psql'
export function settings(): { pg: NodeJS.ProcessEnv; discordToken: string; channel: string; mongoUri: string } {
  const secrets = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(repoDir, 'ShinyBotHunter')], { encoding: 'utf8' }).stdout
  const appsettings = fs.readFileSync(path.join(repoDir, 'ShinyBotHunter', 'appsettings.json'), 'utf8')
  const value = (key: string): string => {
    const secret = secrets.split(/\r?\n/).find((l) => l.startsWith(`${key} = `))
    if (secret) return secret.slice(key.length + 3).trim()
    // As text: the channel id is above Number.MAX_SAFE_INTEGER.
    const json = appsettings.match(new RegExp(`"${key.split(':').at(-1)}"\\s*:\\s*"?([^",\\r\\n]+)"?`))
    if (!json) throw new Error(`no ${key} in the backend user secrets or appsettings.json`)
    return json[1].trim()
  }
  const conn = value('ConnectionStrings:DefaultConnection')
  const parts = Object.fromEntries(conn.split(';').filter((p) => p.includes('='))
    .map((p) => [p.slice(0, p.indexOf('=')).trim().toLowerCase(), p.slice(p.indexOf('=') + 1).trim()]))
  return {
    pg: {
      ...process.env, PGTZ: 'UTC', PGHOST: parts.host ?? parts.server, PGPORT: parts.port ?? '5432', PGDATABASE: parts.database,
      PGUSER: parts.username ?? parts['user id'], PGPASSWORD: parts.password
    },
    discordToken: value('ApplicationSettings:DiscordToken'),
    channel: value('ApplicationSettings:ShinyLogChannel'),
    mongoUri: `mongodb://${value('ApplicationSettings:MongoDbUrl')}/${value('ApplicationSettings:MongoDbDatabase')}`
  }
}
export function psql(env: NodeJS.ProcessEnv, sql: string): string[] {
  const res = spawnSync(PSQL, ['-At', '-F', '|', '-c', sql], { env, encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`psql exited with ${res.status}: ${res.stderr}`)
  return res.stdout.split(/\r?\n/).filter((l) => l !== '')
}
