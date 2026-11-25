// The pieces the hunt-plans-and-milestone-builder scenarios share (#525, Q1a; docs/specs/hunt-plans-and-milestone-builder.md,
// QA): the local-stores guard, the throwaway ROM copies, the per-run clock config, the HGSS launch and Wild Encounter form,
// the RAM set-up (clock, Repel, the lead's level and HP, a taught move), the REST and Mongo calls, the log lines and their
// wait, the window reads in Electron's main and the milestone messages. Sixteen specs call them, so they live here once.
// Every RAM call runs with the bot idle or Waiting, one awaited request at a time; nothing touches ApiHawk while the bot hunts.
import { spawnSync } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import { expect, type ElectronApplication, type Locator, type Page } from '@playwright/test'
import {
  emuHawkRunning, killTree, readMemory, readU32, repoDir, runDir, startEmulatorRecorder, step, stopRecorder, toS16, windowByTitle
} from './driver'
import { crypt, decodeMon, MON_SIZE, rewritePid } from './egg-pid'
import { encounterRows, Y_ADDR, type Row } from './headbutt'
import { dashboardState, RUN_FROM } from './rs-wild'
import { CLEARING, DISCORD_READY, discordMessages, HEAL_TRIP, writeLogged } from './rs-training'
import { API, backendLog, CRASHED, MINUTE, readTile, saveOf, settings, sha256, slotAddr, type Frame } from './static'

export { CLEARING, HEAL_TRIP, RUN_FROM, saveOf }
export { shutdown, startFrame, type Frame } from './static'

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
export const WILD = 'Wild Encounter'
const ADMIN_TITLE = 'Pokemon Dashboard — Admin'
const MAIN_CHECKOUT = path.dirname(spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: repoDir, encoding: 'utf8' }).stdout.trim())
const QA_SAVES = path.join(MAIN_CHECKOUT, '..', 'shinybothunterqasaves')
// The read-only production export of 2026-11-24, copied here by the orchestrator (20 phases, extended JSON); never committed.
export const PHASES_FIXTURE = path.join(QA_SAVES, 'mongo', 'production-phases.json')
const MONGOSH = path.join(process.env.LOCALAPPDATA!, 'Programs', 'mongosh', 'mongosh.exe')

// --- what this run made, for the teardown. Not locked: one scenario runs per worker process, on the one test thread. ---
const made = {
  local: false,
  staged: [] as string[],
  handles: new Set<string>(),
  plans: [] as { game: Game; name: string }[],
  subscriptions: [] as Subscription[],
  phaseIds: [] as string[],
  clock: null as { config: string; aside: string; asideSha: string | null } | null
}

// --- the guard and the settings -----------------------------------------------------------------------------------

// settings() plus the two folders, read once: each read lists the user secrets. Values are never logged.
let cached: (ReturnType<typeof settings> & { romDir: string; bizHawkDir: string }) | null = null
function config(): NonNullable<typeof cached> {
  if (cached) return cached
  const secrets = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(repoDir, 'ShinyBotHunter')], { encoding: 'utf8' }).stdout
  const appsettings = fs.readFileSync(path.join(repoDir, 'ShinyBotHunter', 'appsettings.json'), 'utf8')
  // As static.ts settings() reads its keys: the user secrets first, then appsettings.json.
  const value = (key: string): string => {
    const secret = secrets.split(/\r?\n/).find((l) => l.startsWith(`ApplicationSettings:${key} = `))
    if (secret) return secret.slice(`ApplicationSettings:${key} = `.length).trim()
    const json = appsettings.match(new RegExp(`"${key}"\\s*:\\s*"([^"]+)"`))
    if (!json) throw new Error(`no ${key} in the backend user secrets or appsettings.json`)
    return json[1]
  }
  cached = { ...settings(), romDir: value('ROMBaseDirectory'), bizHawkDir: value('BizHawkBaseDirectory') }
  return cached
}

/** qa-automation NFR-4: fails unless the backend's Mongo and Postgres are on this PC (192.168.1.4 is production). Call it first. */
export function assertLocalStores(): void {
  const { mongoUri, pg } = config()
  const hosts = { Mongo: new URL(mongoUri).hostname, Postgres: pg.PGHOST ?? '' }
  for (const [store, host] of Object.entries(hosts)) {
    if (host !== 'localhost' && host !== '127.0.0.1') throw new Error(`the backend's ${store} is on "${host}", not localhost; refusing to run`)
  }
  made.local = true
  step(`stores are local: ${JSON.stringify(hosts)}`)
}

function requireLocal(): void {
  if (!made.local) throw new Error('assertLocalStores() was not called before the first write')
}

// --- staging: a throwaway ROM copy per scenario, made before the backend starts (RomInfoService scans at start-up) --------

/**
 * Copies `<ROMBaseDirectory><rom>` to `<ROMBaseDirectory><handle>.<ext>` and the pristine `<save>` (`hgss.qa-route32` is
 * `../shinybothunterqasaves/hgss/qa-route32.SaveRAM`) over the handle's SaveRAM and `.bak`; `save: null` leaves the handle
 * no SaveRAM (a title-screen launch). Fails while EmuHawk runs, or if the copy already exists (a leftover to delete by hand).
 */
export function stageCopy(handle: string, { rom, save }: { rom: string; save: string | null }): void {
  requireLocal()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  const { romDir } = config()
  const copy = path.join(romDir, handle + path.extname(rom))
  if (fs.existsSync(copy)) throw new Error(`${copy} already exists: a leftover of an earlier run? Delete it by hand`)
  made.staged.push(copy)
  made.handles.add(handle)
  if (save === null) {
    for (const file of [saveOf(handle), saveOf(handle) + '.bak']) fs.rmSync(file, { force: true })
    step(`${handle}: no SaveRAM`)
  } else {
    const pristine = path.join(QA_SAVES, ...save.split('.', 2)) + '.SaveRAM'
    expect(fs.existsSync(pristine), `missing ${pristine}`).toBe(true)
    fs.copyFileSync(pristine, saveOf(handle))
    fs.copyFileSync(pristine, saveOf(handle) + '.bak')
    step(`${handle}: ${save} installed: SaveRAM sha256 ${sha256(saveOf(handle))}, .bak sha256 ${sha256(saveOf(handle) + '.bak')}`)
  }
  fs.copyFileSync(path.join(romDir, rom), copy)
  step(`${handle}: ${rom} copied to ${copy}`)
}

/** Once EmuHawk has exited: keeps the end-state SaveRAM and .bak in the run folder, then deletes the copy, SaveRAM and .bak. */
export function unstage(handle: string): void {
  expect(emuHawkRunning(), `unstage ${handle}: EmuHawk is still running`).toBe(false)
  for (const file of [saveOf(handle), saveOf(handle) + '.bak']) {
    if (fs.existsSync(file)) {
      fs.copyFileSync(file, path.join(runDir, path.basename(file)))
      fs.rmSync(file)
    }
  }
  const copy = made.staged.find((c) => path.parse(c).name === handle)!
  fs.rmSync(copy, { force: true })
  made.staged.splice(made.staged.indexOf(copy), 1)
  step(`${handle}: unstaged (end-state SaveRAM kept in the run folder)`)
}

// --- the clock: a Sound On launch reads <BizHawk>config_sound_on.ini (BizHawkApiHawkLauncher), so that file carries it ---

/**
 * Writes `<BizHawkBaseDirectory>config_sound_on.ini` as the QA install's config.ini with, in its NDS+NDSSyncSettings section
 * only, `"UseRealTime": false` and `"InitialTime": "<initialTime>"` (QA_CLOCK_START overrides it), each replaced exactly
 * once. An existing file is moved aside to `.qa-aside` and put back by removeClockConfig; a `.qa-aside` left by an earlier
 * run, or an existing file with a fixed clock (this helper's own, left by a killed run), refuses the start. config.ini is
 * only read; its "SoundEnabled": false is kept, so a Sound On launch plays nothing.
 */
export function clockConfig(initialTime: string): void {
  requireLocal()
  const time = process.env.QA_CLOCK_START ?? initialTime
  const dir = config().bizHawkDir
  const target = path.join(dir, 'config_sound_on.ini')
  const aside = target + '.qa-aside'
  if (fs.existsSync(aside)) throw new Error(`${aside} is left from an earlier run: put it back as ${target} by hand, then run again`)
  const section = (text: string): [number, number] => {
    const from = text.indexOf('NDS+NDSSyncSettings')
    return [from, text.indexOf('}', from)]
  }
  let asideSha: string | null = null
  if (fs.existsSync(target)) {
    const existing = fs.readFileSync(target, 'utf8')
    const [from, to] = section(existing)
    if (from !== -1 && existing.slice(from, to).includes('"UseRealTime": false')) {
      throw new Error(`${target} has a fixed clock ("UseRealTime": false): a leftover of a killed run? Delete it by hand`)
    }
    asideSha = sha256(target)
    fs.renameSync(target, aside)
    step(`clock: the existing ${target} moved aside (sha256 ${asideSha})`)
  }
  made.clock = { config: target, aside, asideSha }
  const src = fs.readFileSync(path.join(dir, 'config.ini'), 'utf8')
  const [from, to] = section(src)
  expect(from, 'config.ini has no NDS+NDSSyncSettings section').toBeGreaterThan(-1)
  let body = src.slice(from, to)
  for (const [pattern, replacement] of [[/"InitialTime": "[^"]*"/g, `"InitialTime": "${time}"`], [/"UseRealTime": true/g, '"UseRealTime": false']] as const) {
    expect(body.match(pattern)?.length ?? 0, `config.ini's NDS sync settings: ${pattern} not found exactly once`).toBe(1)
    body = body.replace(pattern, replacement)
  }
  fs.writeFileSync(target, src.slice(0, from) + body + src.slice(to))
  step(`clock: ${target} written with InitialTime ${time}${process.env.QA_CLOCK_START ? ' (QA_CLOCK_START)' : ''}`)
}

/** Deletes this run's config_sound_on.ini and puts a moved-aside one back, checked byte for byte by its sha256. */
export function removeClockConfig(): void {
  if (!made.clock) return
  const { config: target, aside, asideSha } = made.clock
  fs.rmSync(target, { force: true })
  if (asideSha !== null) {
    fs.renameSync(aside, target)
    expect(sha256(target), `${target} restored with other bytes`).toBe(asideSha)
  }
  made.clock = null
  step(`clock: config_sound_on.ini ${asideSha === null ? 'deleted' : 'restored'}`)
}

// --- the frame -------------------------------------------------------------------------------------------------

/** QA_HGSS_VARIANT, one of the spec's own values; the first is the default. */
export function variant<T extends string>(allowed: readonly T[]): T {
  const value = process.env.QA_HGSS_VARIANT ?? allowed[0]
  if (!allowed.includes(value as T)) throw new Error(`QA_HGSS_VARIANT must be one of ${allowed.join(', ')}, not ${value}`)
  return value as T
}

const positions = async (): Promise<string[]> => Object.keys((await (await fetch(`${API}/bot/getDashboardState`)).json()) as object)

/**
 * Launch Bot + Continue on the handle (Launch Bot alone with `continue: false`, the title screen), with Sound On ticked
 * when `clock` is set (clockConfig first). Returns the new bot's card once the launch request has returned and, after a
 * Continue, the overworld has faded in. For position 0 it then starts the recorder and reads and logs the live clock once.
 */
export async function launchHgss(admin: Page, handle: string, { clock = false, continue: cont = true }: { clock?: boolean; continue?: boolean } = {}): Promise<Locator> {
  const before = await positions()
  const launch = admin.getByTestId(`top-level-action-${cont ? 'Launch Bot + Continue' : 'Launch Bot'}`)
  await launch.locator('select').selectOption(handle)
  if (clock) {
    // A controlled checkbox: click, then wait for React's re-render to show it checked.
    const sound = launch.locator('input[type="checkbox"]')
    await sound.click()
    await expect(sound).toBeChecked()
  }
  await launch.getByRole('button', { name: 'Start', exact: true }).click()
  // The form's button reads "Starting..." until the launch request, which waits for the free field, returns.
  await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * MINUTE })
  const pos = (await positions()).find((p) => !before.includes(p))
  expect(pos, `${handle}: no new bot position after the launch`).toBeDefined()
  const card = admin.getByTestId(`emulator-card-${pos}`)
  await expect(cont ? card.getByRole('button', { name: WILD, exact: true }) : card).toBeVisible({ timeout: 5 * MINUTE })
  if (pos !== '0') {
    step(`${handle} launched at position ${pos} (no recorder or RAM read: ApiHawk's ports are position 0's)`)
    return card
  }
  startEmulatorRecorder('emulator')
  // The position reads -1 until the overworld has faded in.
  if (cont) await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: MINUTE, intervals: [500] }).toBeGreaterThan(0)
  step(`${handle} launched at position 0: ${JSON.stringify(cont ? await readTile() : 'title screen')}, clock ${JSON.stringify(await readClock())}`)
  return card
}

export type WildForm = {
  phase?: string; plan?: string; target?: 'PhaseTarget' | 'ShinyTarget' | 'NoTarget'; mode?: 'Run' | 'Spin' | 'Headbutt'
  xRange?: number; yRange?: number; train?: { type: 'Standard' | 'Switch'; targetLevel: number }
}

/**
 * The card's Wild Encounter form through its `param-*` test ids: the milestone by its picker value (a generated id or a
 * custom phase name), or `plan` through `param-HuntPlan`; the target (PhaseTarget by default), the mode (Run by default),
 * the ranges and the training. Returns the Run click's ISO time; a refusal in the card's red box throws
 * `the Run was refused: "<the server's sentence>"`.
 */
export async function startWild(card: Locator, form: WildForm): Promise<string> {
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  const picker = card.getByRole('button', { name: WILD, exact: true })
  await picker.click()
  const choose = async (field: Locator, value: string): Promise<void> => {
    await expect(field.locator(`option[value="${value}"]`), `${value} is not offered`).toBeAttached({ timeout: 30_000 })
    await field.selectOption(value)
  }
  if (form.plan) await choose(card.getByTestId('param-HuntPlan').locator('select').first(), form.plan)
  // The milestone field holds the modality filter's select first and the milestone's last.
  else await choose(card.getByTestId('param-PhaseName').locator('select').last(), form.phase!)
  await card.getByTestId('param-TargetType').locator('select').selectOption(form.target ?? 'PhaseTarget')
  await card.getByTestId('param-EncounterType').locator('select').selectOption(form.mode ?? 'Run')
  if (form.xRange !== undefined) await card.getByTestId('param-xRange').locator('input').fill(String(form.xRange))
  if (form.yRange !== undefined) await card.getByTestId('param-yRange').locator('input').fill(String(form.yRange))
  if (form.train) {
    await card.getByTestId('param-TrainType').locator('select').selectOption(form.train.type)
    await card.getByTestId('param-TargetLevel').locator('input').fill(String(form.train.targetLevel))
  }
  const runStart = new Date().toISOString()
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  const refusal = card.locator('div[class~="bg-red-900/20"]')
  await expect.poll(async () => (await picker.count()) === 0 || (await refusal.count()) > 0, { timeout: 30_000 }).toBe(true)
  if ((await refusal.count()) > 0) throw new Error(`the Run was refused: "${(await refusal.innerText()).trim()}"`)
  step(`${WILD} ${JSON.stringify(form)} started at ${runStart}`)
  return runStart
}

async function cancelOnce(card: Locator): Promise<void> {
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name: 'Cancel', exact: true }).click()
  await card.getByRole('button', { name: 'Yes', exact: true }).click()
}

/** One Cancel for an idle or finished bot: the card goes, the recorder stops and no EmuHawk runs. */
export async function cancelBot(card: Locator): Promise<void> {
  await cancelOnce(card)
  await expect(card).toHaveCount(0, { timeout: MINUTE })
  await stopRecorder()
  await expect.poll(emuHawkRunning, { timeout: MINUTE }).toBe(false)
  step('bot cancelled; no EmuHawk running')
}

/** A running event: the first Cancel ends it and leaves the bot idle, then `between` runs, then cancelBot. */
export async function cancelEventAndBot(card: Locator, between?: () => Promise<void>): Promise<void> {
  await cancelOnce(card)
  await expect(card.getByRole('button', { name: WILD, exact: true })).toBeVisible({ timeout: MINUTE })
  step('event cancelled')
  if (between) await between()
  await cancelBot(card)
}

/** For the teardown: up to two cancels through card 0 while it shows Cancel; a missing window, card or button ends it. */
async function cancelLeftovers(app: ElectronApplication): Promise<void> {
  for (let attempt = 1; attempt <= 2; attempt++) {
    try {
      const admin = await windowByTitle(app, ADMIN_TITLE, 5000)
      const card = admin.getByTestId('emulator-card-0')
      await admin.getByRole('button', { name: 'Bot Control', exact: true }).click({ timeout: 5000 })
      const cancel = card.getByRole('button', { name: 'Cancel', exact: true })
      if (!(await cancel.isVisible())) return
      await cancel.click({ timeout: 5000 })
      await card.getByRole('button', { name: 'Yes', exact: true }).click({ timeout: 5000 })
      await expect.poll(async () => (await card.count()) === 0 || (await card.getByRole('button', { name: WILD, exact: true }).isVisible()),
        { timeout: MINUTE }).toBe(true)
      step(`teardown: cancel ${attempt} sent through the card`)
    } catch (e) {
      step(`teardown: cancel ${attempt} not made: ${(e as Error).message.split('\n')[0]}`)
      return
    }
  }
}

/** Asks EmuHawk to close (CloseMainWindow, which flushes SaveRAM), never kills it, and waits 60 s for it to exit. */
async function closeEmuHawk(): Promise<void> {
  spawnSync('powershell', ['-NoProfile', '-Command', 'Get-Process EmuHawk -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }'])
  const deadline = Date.now() + MINUTE
  while (emuHawkRunning() && Date.now() < deadline) await sleep(1000)
  step(`EmuHawk asked to close: ${emuHawkRunning() ? 'still running after 60 s' : 'exited'}`)
}

/**
 * The finally, on every outcome. Up to two cancels through the card, the recorder and the dashboard closed, EmuHawk asked
 * to close, then the scenario's own `cleanup` (a cheat file, say). Once EmuHawk has exited: the backend's tree killed, every
 * staged copy unstaged and the clock config removed. Then, from Mongo, the plans, subscriptions and GameFiles documents
 * this run wrote and the phases it imported. If EmuHawk still runs after 60 s, the backend, EmuHawk, the copies and the
 * clock config are left (EmuHawk writes its config on close) and the steps log says so. Every part is tried; the failures
 * are thrown together at the end.
 */
export async function teardown(frame: Frame, cleanup?: () => Promise<void> | void): Promise<void> {
  const failures: string[] = []
  const attempt = async (what: string, part: () => Promise<void> | void): Promise<void> => {
    try {
      await part()
    } catch (e) {
      failures.push(`${what}: ${(e as Error).message}`)
      step(`teardown: ${what} failed: ${(e as Error).message}`)
    }
  }
  if (frame.app) await cancelLeftovers(frame.app)
  await stopRecorder()
  if (frame.app) await frame.app.close().catch(() => undefined)
  if (emuHawkRunning()) await closeEmuHawk()
  if (cleanup) await attempt('the scenario cleanup', cleanup)
  if (emuHawkRunning()) {
    step(`EmuHawk still running after 60 s; the backend, EmuHawk, ${made.staged.join(', ') || 'no copies'} and the clock config left alone`)
  } else {
    if (frame.backend && frame.backend.exitCode === null) killTree(frame.backend.pid!)
    for (const copy of [...made.staged]) await attempt(`unstage ${copy}`, () => unstage(path.parse(copy).name))
    await attempt('the clock config', removeClockConfig)
  }
  if (made.plans.length > 0) {
    await attempt('the plans', () => {
      for (const { game, name } of made.plans) mongo(`db.HuntPlans.deleteMany({ Game: ${game === 'HGSS' ? 0 : 1}, Name: ${JSON.stringify(name)} }).deletedCount`)
      step(`teardown: plans deleted: ${made.plans.map((p) => p.name).join(', ')}`)
      made.plans = []
    })
  }
  if (made.subscriptions.length > 0) {
    await attempt('the subscriptions', () => {
      for (const s of made.subscriptions) {
        const element = { game: s.game === 'HGSS' ? 0 : 1, phase: s.phase, gameFileName: s.gameName ?? null, followsHunt: s.followsHunt ? true : { $exists: false } }
        mongo(`db.DashboardConfig.updateOne({ _id: 'dashboard' }, { $pull: { GamePhases: ${JSON.stringify(element)} } }).modifiedCount`)
      }
      step(`teardown: subscriptions removed: ${JSON.stringify(made.subscriptions)}`)
      made.subscriptions = []
    })
  }
  for (const handle of [...made.handles]) await attempt(`the GameFiles document of ${handle}`, () => deleteGameFile(handle))
  if (made.phaseIds.length > 0) await attempt('the imported phases', () => removePhases([...made.phaseIds]))
  if (failures.length > 0) throw new Error(`teardown: ${failures.join('; ')}`)
}

// --- RAM, HGSS: only with the bot idle (after the launch, before Run) or Waiting -------------------------------------

const ANCHOR_ADDR = 0x21d4158
const LIVE_CLOCK = 0x021d1058
// RoamerSaveData.repelSteps: the roamer section + 0x65, save data [[anchor] + 0x0C] + 0x6919 (research R4).
const REPEL_OFFSET = 0x6919
// The party extension (battle stats), encrypted with the PID as seed: level at + 4, current HP at + 6 (HgssPokemonParser).
const EXTENSION = 0x88
const BLOCK_B = 32

/** Fails unless the bot at position 0 is idle or Waiting: no scenario reads or writes the emulator while the bot hunts. */
async function idle(what: string): Promise<void> {
  const state = (await dashboardState())?.eventState
  if (!['None', 'NotStarted', 'Waiting', 'Complete'].includes(state)) throw new Error(`${what} while the bot hunts (eventState ${state})`)
}

export type Clock = { year: number; month: number; day: number; weekday: number; hour: number; minute: number; second: number }
/** The live clock, seven u32 at 0x021D1058 (research R1): weekday 0 is Sunday, hour 0 to 23. */
export async function readClock(): Promise<Clock> {
  await idle('readClock')
  const b = await readMemory(LIVE_CLOCK, 28)
  const [year, month, day, weekday, hour, minute, second] = [0, 1, 2, 3, 4, 5, 6].map((i) => b.readUInt32LE(4 * i))
  return { year, month, day, weekday, hour, minute, second }
}

// The save data moves across a soft reset (research R3), so the address is read again on every call.
const repelAddr = async (): Promise<number> => (await readU32((await readU32(ANCHOR_ADDR)) + 0x0c)) + REPEL_OFFSET

export async function readRepel(): Promise<number> {
  await idle('readRepel')
  const value = (await readMemory(await repelAddr(), 1))[0]
  step(`Repel steps read ${value}`)
  return value
}

/** The Repel counter (u8) written, logged with its address and old and new bytes, and read back. */
export async function writeRepel(steps: number): Promise<void> {
  await idle('writeRepel')
  await writeLogged(`Repel steps ${steps}`, await repelAddr(), Buffer.from([steps]))
}

const extensionOf = (raw: Buffer): Buffer => crypt(raw.subarray(EXTENSION, MON_SIZE), raw.readUInt32LE(0))

/** Party slot 1's level and current HP in its party extension: re-encrypted, only the differing bytes written, decoded back. */
export async function writeLeadExtension({ hp, level }: { hp?: number; level?: number }): Promise<void> {
  await idle('writeLeadExtension')
  const addr = await slotAddr(0)
  const raw = await readMemory(addr, MON_SIZE)
  expect(decodeMon(raw).checksumValid, 'the lead fails its checksum before the write').toBe(true)
  const ext = extensionOf(raw)
  step(`lead before: level ${ext[4]}, HP ${ext.readUInt16LE(6)}/${ext.readUInt16LE(8)}`)
  if (level !== undefined) ext[4] = level
  if (hp !== undefined) ext.writeUInt16LE(hp, 6)
  const out = Buffer.from(raw)
  crypt(ext, raw.readUInt32LE(0)).copy(out, EXTENSION)
  await writeLogged(`lead extension ${JSON.stringify({ hp, level })}`, addr, out)
  const after = extensionOf(await readMemory(addr, MON_SIZE))
  if (level !== undefined) expect(after[4], 'the lead\'s level after the write').toBe(level)
  if (hp !== undefined) expect(after.readUInt16LE(6), 'the lead\'s HP after the write').toBe(hp)
}

// Blocks A to D in that order, decrypted: under a PID whose shuffle is 0 the stored order is ABCD, so rewritePid to such a
// PID and one crypt with the checksum as seed give them.
const flatPid = (pid: number): number => (pid & ~0x3e000) >>> 0
const plainOf = (raw: Buffer): { flat: Buffer; plain: Buffer } => {
  const flat = rewritePid(raw, flatPid(raw.readUInt32LE(0)))
  return { flat, plain: crypt(flat.subarray(0x08, EXTENSION), flat.readUInt16LE(6)) }
}

/** Party slot `slot` (1-based) knows `move` with `pp` in move slot `index` (0-based): block B re-encoded with a new checksum. */
export async function teachMove(slot: number, index: number, move: number, pp: number): Promise<void> {
  await idle('teachMove')
  const addr = await slotAddr(slot - 1)
  const raw = await readMemory(addr, MON_SIZE)
  expect(decodeMon(raw).checksumValid, `slot ${slot} fails its checksum before the write`).toBe(true)
  const { flat, plain } = plainOf(raw)
  step(`slot ${slot} move ${index}: ${plain.readUInt16LE(BLOCK_B + 2 * index)} PP ${plain[BLOCK_B + 8 + index]} -> ${move} PP ${pp}`)
  plain.writeUInt16LE(move, BLOCK_B + 2 * index)
  plain[BLOCK_B + 8 + index] = pp
  // Encrypted again under the old checksum, so rewritePid back to the PID reads the changed blocks and sums them anew.
  crypt(plain, flat.readUInt16LE(6)).copy(flat, 0x08)
  await writeLogged(`slot ${slot} move ${index} = ${move}`, addr, rewritePid(flat, raw.readUInt32LE(0)))
  const after = await readMemory(addr, MON_SIZE)
  expect(decodeMon(after).checksumValid, `slot ${slot} fails its checksum after the write`).toBe(true)
  const read = plainOf(after).plain
  expect([read.readUInt16LE(BLOCK_B + 2 * index), read[BLOCK_B + 8 + index]], `slot ${slot}'s move ${index} after the write`).toEqual([move, pp])
}

// --- REST: each call logged with its status and the start of its body ---------------------------------------------

export type Game = 'HGSS' | 'RS'
export type Res = { status: number; text: string; json: any }
export type GoalEntry = { species: number; formCode?: string | null; amount: number }
export type Preset = 'None' | 'LivingDex' | 'FemaleForms' | 'VersionExclusives'
export type Weekday = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'
export type PlanRule = { times: ('Morning' | 'Day' | 'Night')[]; weekdays: Weekday[]; milestone: string }
export type Plan = { name: string; default: string; rules: PlanRule[] }
export type Subscription = { game: Game; phase: string; gameName?: string | null; followsHunt?: boolean }

async function rest(method: string, route: string, body?: unknown): Promise<Res> {
  if (method !== 'GET') requireLocal()
  const res = await fetch(`${API}/${route}`, {
    method, headers: body === undefined ? undefined : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body)
  })
  const text = await res.text()
  let json: any = null
  try {
    json = JSON.parse(text)
  } catch {
    // a plain-text refusal or an empty 204
  }
  step(`${method} ${route} -> ${res.status}${text ? ` ${text.slice(0, 300)}` : ''}`)
  return { status: res.status, text, json }
}
const q = encodeURIComponent

export async function putGoal(handle: string, entries: GoalEntry[]): Promise<Res> {
  made.handles.add(handle)
  return rest('PUT', `goal/${q(handle)}`, entries)
}
export async function applyPreset(game: Game, handle: string, preset: Preset): Promise<Res> {
  made.handles.add(handle)
  return rest('POST', `goal/${game}/${q(handle)}/preset/${preset}`)
}
export async function setHidden(handle: string, row: { milestoneId: string; key: string }, hidden: boolean): Promise<Res> {
  made.handles.add(handle)
  return rest('PUT', `goal/${q(handle)}/hidden?hidden=${hidden}`, row)
}
export async function putPlan(game: Game, plan: Plan): Promise<Res> {
  if (!made.plans.some((p) => p.game === game && p.name === plan.name)) made.plans.push({ game, name: plan.name })
  return rest('PUT', `plans/${game}/${q(plan.name)}`, { game, ...plan })
}
export async function deletePlan(game: Game, name: string): Promise<Res> {
  const res = await rest('DELETE', `plans/${game}/${q(name)}`)
  if (res.status === 204) made.plans = made.plans.filter((p) => !(p.game === game && p.name === name))
  return res
}
export async function renamePlan(game: Game, name: string, to: string): Promise<Res> {
  const res = await rest('POST', `plans/${game}/${q(name)}/rename?to=${q(to)}`)
  if (res.status === 204) made.plans = made.plans.map((p) => (p.game === game && p.name === name ? { game, name: to } : p))
  return res
}
/** Set anchor here: the bot's tile and facing become the milestone's anchor on the bot's file (FR-22). */
export const setAnchorHere = (pos: number, milestone: string, xRange: number, yRange: number): Promise<Res> =>
  rest('PUT', `bot/anchor/${pos}?milestone=${q(milestone)}&xRange=${xRange}&yRange=${yRange}`)
export const clearAnchor = (pos: number, milestone: string): Promise<Res> => rest('DELETE', `bot/anchor/${pos}?milestone=${q(milestone)}`)
/** GET bot/milestones/{pos}: the picker's milestones for the bot's file. */
export const picker = (pos: number): Promise<Res> => rest('GET', `bot/milestones/${pos}`)
/** GET milestone/{game}/{id}/{handle}: the generated milestone's rows, shares and TimeOfDay through the file's goal. */
export const rows = (game: Game, id: string, handle: string): Promise<Res> => rest('GET', `milestone/${game}/${q(id)}/${q(handle)}`)
export async function subscribe(sub: Subscription): Promise<Res> {
  made.subscriptions.push(sub)
  return rest('POST', 'subcribeGamePhase', sub)
}
export async function unsubscribe(sub: Subscription): Promise<Res> {
  const res = await rest('POST', 'unsubcribeGamePhase', sub)
  made.subscriptions = made.subscriptions.filter((s) => JSON.stringify(s) !== JSON.stringify(sub))
  return res
}
/** POST bot/startAction/{pos}, for the refusal Starts: `args` as NewEventArguments (EventName, PhaseName, HuntPlan, ...). */
export const startAction = (pos: number, args: Record<string, unknown>): Promise<Res> => rest('POST', `bot/startAction/${pos}`, args)

// --- Mongo through mongosh, on the asserted local URI -----------------------------------------------------------

function mongo(script: string): any {
  requireLocal()
  const res = spawnSync(MONGOSH, [config().mongoUri, '--quiet', '--eval', `print(EJSON.stringify(${script}))`], { encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`mongosh exited with ${res.status}: ${res.stderr}`)
  return JSON.parse(res.stdout.trim().split(/\r?\n/).at(-1)!)
}

export function deleteGameFile(handle: string): void {
  const deleted = mongo(`db.GameFiles.deleteOne({ _id: ${JSON.stringify(handle)} }).deletedCount`)
  made.handles.delete(handle)
  step(`GameFiles document of ${handle}: ${deleted} deleted`)
}

export function gameFileCount(handle: string): number {
  const count = mongo(`db.GameFiles.countDocuments({ _id: ${JSON.stringify(handle)} })`)
  step(`GameFiles documents of ${handle}: ${count}`)
  return count
}

/**
 * Inserts the export's documents (extended JSON) whose game and name the QA PhaseInfos lacks, before the backend starts (it
 * reads phases once at start-up). Returns their ids (the UUIDs' base64), which the teardown removes if removePhases does not.
 */
export function importPhases(file: string): string[] {
  const added: { $binary: { base64: string } }[] = mongo(`(() => {
    const added = []
    for (const d of EJSON.parse(require('fs').readFileSync(${JSON.stringify(file)}, 'utf8'))) {
      if (db.PhaseInfos.countDocuments({ GameName: d.GameName, Name: d.Name }) === 0) { db.PhaseInfos.insertOne(d); added.push(d._id) }
    }
    return added
  })()`)
  const ids = added.map((id) => id.$binary.base64)
  made.phaseIds.push(...ids)
  step(`phases imported from ${file}: ${ids.length}`)
  return ids
}

export function removePhases(ids: string[]): void {
  const deleted = mongo(`db.PhaseInfos.deleteMany({ _id: { $in: ${JSON.stringify(ids)}.map((b) => BinData(4, b)) } }).deletedCount`)
  made.phaseIds = made.phaseIds.filter((id) => !ids.includes(id))
  step(`phases removed: ${deleted} of ${ids.length}`)
  expect(deleted, 'phases removed').toBe(ids.length)
}

// --- Postgres -------------------------------------------------------------------------------------------------

/** The milestone's encounters rows on the file since `sinceIso`, oldest first (headbutt.ts encounterRows). */
export const rowsFor = (handle: string, milestone: string, sinceIso: string): Row[] => encounterRows(config().pg, handle, milestone, sinceIso)

// --- log lines the asserts rest on (code at 13a752c); the comment names where each is written ---------------------

// BotService, at Start: one per plan stop.
export const PLAN_STOP = /\[BotService\] Plan (.+?): (.+) at map (\d+) \((\d+), (\d+)\), (.*)$/
// BotService, at Start with an anchor (FR-23): `set on this file` or `generated` at the end.
export const ANCHOR_HUNT = /\[BotService\] (.+?): hunting from the anchor map (\d+) \((\d+), (\d+)\), (.*) \((set on this file|generated)\)\s*$/
// BotService, a headbutt milestone's trainer ID (B1h).
export const TRAINER_ID = /\[BotService\] Trainer ID (\d+) \(visible (\d+)\), (.*)$/
// HuntPlanRun: a pick that walks, and the switch once on the anchor.
export const PLAN_WALK = /\[Orchestrator\] Plan: (\w+) (\d+)h names (.+); walking from (\S+) to map (\d+) \((\d+), (\d+)\)\s*$/
export const PLAN_NOW = /\[Orchestrator\] Plan: now hunting (.+?)\s*$/
export const WALK_ENDED = /\[Orchestrator\] (.+?): the walk ended at map (\d+) \((\d+), (\d+)\), not on the anchor \((\d+), (\d+)\)\./
// OrchestratorEvent, no route to the anchor (B2a).
export const NO_PATH = /\[Orchestrator\] (.+?): no path to the anchor \((\d+), (\d+)\)\./
// RunEncounterEvent: the Repel message closed (HGSS Run and navigator; then RS), and the close that failed.
export const REPEL_CLOSED = /\[Repel\] Repel's effect wore off: closed with (\d+) A, confirmed by (.*)$/
export const RS_REPEL_CLOSED = /\[Run\] REPEL's effect wore off: closed with (\d+) A, confirmed by lock 0 \((.*)\)\s*$/
export const REPEL_NOT_CLOSED = /\[Run\] The Repel message did not close\./
// NavigatorEvent: the message on a walk, a warp wait, the nurse.
export const NAV_REPEL = /\[Navigator\] Repel message seen: (.*)$/
export const WARP = /\[Navigator\] Warp to map (\d+): on map (\d+), (.*)$/
export const HEALING = /\[Navigator\] Healing with/
// DiscordBotService with no login, and OrchestratorEvent.TellAsync's failed send (FR-34a).
export const DROPPED = /Discord is not logged in; dropping message: (.*)$/
export const NOT_SENT = /\[Orchestrator\] The milestone message was not sent: (.*)$/

/**
 * Polls backend.log every 250 ms until `pattern` has matched `n` lines; returns the n-th and the wall time it was first
 * seen (backend.log has no timestamps; a row's Timestamp is compared with it). Fails at once on a crash, a
 * GameStateTimeoutException, or position 0 Waiting with the line not logged, unless `waiting` says the park is expected.
 */
export async function seen(pattern: RegExp, n: number, ms: number, { waiting = false }: { waiting?: boolean } = {}): Promise<{ line: string; at: number }> {
  const deadline = Date.now() + ms
  const found = (): string[] => backendLog().filter((l) => pattern.test(l))
  while (true) {
    const log = backendLog()
    const lines = log.filter((l) => pattern.test(l))
    if (lines.length >= n) {
      step(`logged: ${lines[n - 1].trim()}`)
      return { line: lines[n - 1], at: Date.now() }
    }
    const crashed = log.find((l) => CRASHED.test(l) || l.includes('GameStateTimeoutException'))
    if (crashed) throw new Error(`the event crashed: ${crashed}`)
    // The log is read again after the state, since a line is written before the park it explains.
    if (!waiting && (await dashboardState())?.eventState === 'Waiting' && found().length < n) {
      throw new Error(`the bot is Waiting with ${found().length} of ${n} lines matching ${pattern}; last lines:\n${log.slice(-10).join('\n')}`)
    }
    if (Date.now() > deadline) throw new Error(`${lines.length} of ${n} lines matching ${pattern} within ${ms / 1000} s; last lines:\n${log.slice(-10).join('\n')}`)
    await sleep(250)
  }
}

// --- windows, from Electron's main ----------------------------------------------------------------------------

export type WindowInfo = { id: number; handle: string; bounds: { x: number; y: number; width: number; height: number }; title: string }
/** The BrowserWindow titled `title`: its id, getNativeWindowHandle() as hex, bounds and title. */
export async function windowInfo(app: ElectronApplication, title: string): Promise<WindowInfo> {
  const info = await app.evaluate(({ BrowserWindow }, t) => {
    const w = BrowserWindow.getAllWindows().find((x) => x.getTitle() === t)
    if (!w) throw new Error(`no window titled "${t}"`)
    return { id: w.id, handle: '0x' + w.getNativeWindowHandle().readBigUInt64LE(0).toString(16), bounds: w.getBounds(), title: w.getTitle() }
  }, title)
  step(`window ${JSON.stringify(info)}`)
  return info
}

// Admin's bounds before cover; set by cover, cleared by uncover, on the one test thread.
let adminBounds: WindowInfo['bounds'] | null = null

/** Puts the Admin window over the window titled `title`, always on top. */
export async function cover(app: ElectronApplication, title: string): Promise<void> {
  adminBounds = await app.evaluate(({ BrowserWindow }, t) => {
    const all = BrowserWindow.getAllWindows()
    const target = all.find((x) => x.getTitle() === t.title)
    const admin = all.find((x) => x.getTitle() === t.admin)
    if (!target || !admin) throw new Error(`no window titled "${target ? t.admin : t.title}"`)
    const before = admin.getBounds()
    admin.setBounds(target.getBounds())
    admin.setAlwaysOnTop(true)
    admin.moveTop()
    return before
  }, { title, admin: ADMIN_TITLE })
  step(`Admin covers "${title}" (Admin was at ${JSON.stringify(adminBounds)})`)
}

/** Admin back where cover found it, no longer on top. */
export async function uncover(app: ElectronApplication): Promise<void> {
  await app.evaluate(({ BrowserWindow }, t) => {
    const admin = BrowserWindow.getAllWindows().find((x) => x.getTitle() === t.admin)!
    admin.setAlwaysOnTop(false)
    admin.setBounds(t.bounds)
  }, { admin: ADMIN_TITLE, bounds: adminBounds! })
  adminBounds = null
  step('Admin uncovered')
}

/** Saves the window's webContents.capturePage() as `png` in the run folder; true when the image has more than one colour. */
export async function capture(app: ElectronApplication, title: string, png: string): Promise<boolean> {
  const shot = await app.evaluate(async ({ BrowserWindow }, t) => {
    const w = BrowserWindow.getAllWindows().find((x) => x.getTitle() === t)
    if (!w) throw new Error(`no window titled "${t}"`)
    const image = await w.webContents.capturePage()
    const bitmap = image.toBitmap()
    let colours = false
    for (let i = 4; i < bitmap.length && !colours; i += 4) colours = bitmap.readUInt32LE(i) !== bitmap.readUInt32LE(0)
    return { png: image.toPNG().toString('base64'), colours }
  }, title)
  fs.writeFileSync(path.join(runDir, png), Buffer.from(shot.png, 'base64'))
  step(`captured "${title}" as ${png}: ${shot.colours ? 'more than one colour' : 'one colour'}`)
  return shot.colours
}

// --- the milestone messages (FR-34a) ------------------------------------------------------------------------------

/**
 * The milestone messages since the Start: with the backend logged into Discord, the shiny log channel's messages that
 * contain `is complete on` (Discord's REST API with the user-secret token, never logged; the backend logs no line for a
 * sent message); not logged in, the `dropping message:` lines that do. A `The milestone message was not sent:` line fails.
 */
export async function milestoneMessages(sinceIso: string): Promise<string[]> {
  const log = backendLog()
  const notSent = log.find((l) => NOT_SENT.test(l))
  if (notSent) throw new Error(`a milestone message was not sent: ${notSent}`)
  if (log.some((l) => DISCORD_READY.test(l))) return discordMessages(sinceIso, 'is complete on')
  const dropped = log.flatMap((l) => DROPPED.exec(l)?.[1] ?? []).filter((m) => m.includes('is complete on'))
  step(`Discord not logged in: ${dropped.length} dropped milestone messages: ${JSON.stringify(dropped)}`)
  return dropped
}
