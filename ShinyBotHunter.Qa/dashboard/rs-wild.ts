// The pieces the rs-wild scenarios share (#387): the [Spin], [Run] and [RsBattle] log lines and their parsers, the
// per-encounter flee check, the pristine save and the cheat, the launch/run/cancel/teardown frame and the few RAM reads and
// writes (docs/specs/rs-wild-encounter.md, QA). static.ts's frame is tied to HG-Static, so only its generic pieces are used.
// The memory port is touched only at the quiet moments each scenario names, one awaited request at a time.
import { spawnSync } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import { expect, type ElectronApplication, type Locator, type Page } from '@playwright/test'
import {
  emuHawkRunning, killTree, readMemory, readU16, readU32, repoDir, runDir, sendCheatHotkey, shotAll, startEmulatorRecorder,
  step, stopRecorder, windowByTitle, writeU8
} from './driver'
import { API, backendLog, hex8, MINUTE, psql, settings, sha256, shutdown, startFrame, type Frame } from './static'

// --- constants ---------------------------------------------------------------------------------------

// The ROM copies and the game names their cheats are loaded under (research doc, "A shiny on demand for QA").
export const ROMS: Record<string, string> = {
  'SA-Wild': 'Pokemon - Sapphire Version (USA, Europe) (Rev 2)',
  'RU-Wild': 'Pokemon - Ruby Version (USA, Europe) (Rev 2)'
}
const BIZHAWK = 'C:\\BizHawk-2.11'
export const saveOf = (rom: string): string => path.join(BIZHAWK, 'GBA', 'SaveRAM', `${rom}.SaveRAM`)
const cheatOf = (rom: string): string => path.join(BIZHAWK, 'GBA', 'Cheats', `${ROMS[rom]}.cht`)
const CHEAT_SOURCE = path.join(repoDir, 'ShinyBotHunter.Qa', 'Cheats', 'rs-usa-rev2', 'wild-shiny.cht')
// Beside the main checkout, found through git so a worktree resolves it too (as static.ts does).
const MAIN_CHECKOUT = path.dirname(spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: repoDir, encoding: 'utf8' }).stdout.trim())
export const PRISTINE = path.join(MAIN_CHECKOUT, '..', 'shinybothunterqasaves', 'rs', 'qa-route101.SaveRAM')
const PRISTINE_SHA_PREFIX = '9196ad2bab1c'

// The flash; the 16-byte clock footer after it is rewritten by the emulator on close (research doc, "The save file").
export const FLASH = 131072
export const PHASE = process.env.QA_RS_PHASE ?? 'Route 101'
export const ACTION = 'Wild Encounter'
export const START = { group: 0, num: 16, x: 14, y: 4 }
export const SHINY_PV = 'CD631295'
// Poochyena, Zigzagoon, Wurmple (national), Lv 2 or 3 on Route 101.
export const WILD = [261, 263, 265]
export const LEVELS = [2, 3]

// RsPointerService, and the research doc's "Memory read by the spike".
export const POS = 0x02025734 // x s16, y s16, then group u8 and number u8 at 0x02025738
export const LOCK = 0x030006a4
export const IN_BATTLE = 0x03001bad // bit 1
export const CONTROLLER = 0x03004330
export const MENU_FN = 0x0802c099
export const WINDOW = 0x03004226
export const CURSOR = 0x02024e60
export const OUTCOME = 0x02024d26
export const PARTY_COUNT = 0x03004350
export const PARTY = 0x03004360
export const SLOT_SIZE = 100
export const LEAD_HP = PARTY + 0x56
export const LEAD_SPEED = 0x030043be
export const BALL_SLOT = 0x02025d34
export const RNG = 0x03004818
export const RNG_FROZEN = 0x67f18fb9
// The battle window's button waits: 8, and 5 and 9 from the decomp.
export const BUTTON_WAITS = [5, 8, 9]

// Macro/Configs/RS/spin_*.yaml, and SpinEncounterEvent's rotation: Up, Left, Down, Right, starting after the facing.
const SPIN: Record<string, string> = {
  Up: 'M.Up.34.50350262', Left: 'M.Left.51.50350262', Down: 'M.Down.17.50350262', Right: 'M.Right.68.50350262'
}
const TURNS = ['Up', 'Left', 'Down', 'Right']
export function spinMacroFor(facing: string): string {
  const at = TURNS.indexOf(facing)
  return [1, 2, 3, 4].map((i) => SPIN[TURNS[(at + i) % 4]]).join(';')
}
// RunEncounterEvent.RunMacro for y range 1 from (14, 4), with RunHold joined to the first move.
export const RUN_MACRO = 'M.Up.3.33707828:H.B.1;M.Down.5.33707828'
// The same for x range 1: Right then Left, watching x (RsPointerService.RunWatchX, 0x02025732).
export const RUN_MACRO_X = 'M.Right.15.33707826:H.B.1;M.Left.13.33707826'

// BotService and OrchestratorEvent (W6a, #386).
export const HEADBUTT_SENTENCE = 'Ruby and Sapphire have no Headbutt trees.'
export const NO_TARGET_SENTENCE = 'No Target needs a train mode, which Ruby and Sapphire do not have yet.'
export const TRAINING_SENTENCE = 'Training is not available on Ruby and Sapphire yet.'
export const FIELD_SENTENCE = 'Close any menu or message and stand in the field.'
export const FAINTED_SENTENCE = 'The first Pokémon in the party has fainted.'
export const FREE_SLOT_SENTENCE = 'Leave a free party slot so a caught Pokémon stays in the party.'
export const SHINY_LEAD_LINE = 'A shiny first Pokémon adds about 1.2 s to every battle.'

// --- log patterns: each matches one Console.WriteLine of SpinEncounterEvent, RunEncounterEvent or RsBattleEvent ----

export const SPIN_LINE = /\[Spin\] facing=(\w+) macro=(\S+)\s*$/
export const FOUND = /\[Spin\] Found battle after (\d+) ms\s*$/
// The G1 line (bot task W2c), for rs-wild-run.
export const RUN_FROM = /\[Run\] from \((\d+), (\d+)\) macro=(\S+)\s*$/
export const ADVANCE = /\[RsBattle\] BattleAdvance at window (\d*), (\d+) ms: (.*)$/
export const ENCOUNTER = /\[RsBattle\] (.+?) species (\d+) Lv (\d+) PV ([0-9A-F]{8}) shiny value (\d+) shiny (True|False)\s*$/
export const TO_RUN = /\[RsBattle\] BattleCursorToRun from cursor (\d*), (\d+) ms: (.*)$/
export const REPAIR = /\[RsBattle\] cursor (\d*) after BattleCursorToRun; sending /
export const CONFIRM = /\[RsBattle\] BattleConfirm on RUN \(try (\d+)\), (\d+) ms: (.*)$/
export const ESCAPE_FAILED = /\[RsBattle\] escape failed \(try (\d+) of 12\), cursor (\d*), lead HP (\d+)\s*$/
export const TARGET = /\[RsBattle\] a target: waiting at the action menu, nothing pressed\s*$/
export const OVER = /\[RsBattle\] battle over: (\d+) frames from the battle bit, (\d+) ms, (\d+) presses, (\d+) on RUN\s*$/
// backend.log is written in the console's code page, so the é is matched by any one or two characters.
export const SPEED_SHINY = new RegExp(`\\[BotService\\] ${SHINY_LEAD_LINE.replace(/\./g, '\\.').replace('é', '.{1,2}')}\\s*$`)
export const STOPPED = /\[(RsBattle|Spin|Run)\] (stopped: |The player moved while turning|The player is off the run line|Something other than a wild battle)/
export const MOVED = /\[Spin\] The player moved while turning/
const CRASHED = /\[BotService\] Event '.*' crashed/
// Every line the bot logs for a press in battle.
export const PRESS_LINE = /\[RsBattle\] (BattleAdvance|BattleCursorToRun|cursor \d* after BattleCursorToRun|BattleConfirm)/

export type Encounter = { name: string; species: number; level: number; pid: string; shinyValue: number; shiny: boolean }
export function parseEncounter(line: string): Encounter {
  const m = ENCOUNTER.exec(line)
  if (!m) throw new Error(`not an encounter line: ${line}`)
  return { name: m[1], species: Number(m[2]), level: Number(m[3]), pid: m[4], shinyValue: Number(m[5]), shiny: m[6] === 'True' }
}

/** One field of a logged GameSnapshot, `GameSnapshot { Screen = BattleMenu, Outcome = , … }`; '' for a null. */
export function snapshotField(snapshot: string, field: string): string {
  const m = new RegExp(`\\b${field} = ([^,}]*)`).exec(snapshot)
  if (!m) throw new Error(`no ${field} in the snapshot: ${snapshot}`)
  return m[1].trim()
}

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
const lastBotLines = (log: string[]): string => log.filter((l) => /\[(Spin|Run|RsBattle)\]/.test(l)).slice(-10).join('\n')

/** Fails at once on a stop of the hunt (quoting its whole line), a crash or a stale-timeout in the log. */
export function failOnStop(log: string[]): void {
  const stopped = log.find((l) => STOPPED.test(l))
  if (stopped) throw new Error(`the hunt stopped: ${stopped.trim()}`)
  const crashed = log.find((l) => CRASHED.test(l) || l.includes('GameStateTimeoutException'))
  if (crashed) throw new Error(`the event crashed: ${crashed}`)
}

/**
 * Polls backend.log (a file, not the emulator) every 250 ms until `pattern` has matched `count` lines, and returns the
 * count-th. A timeout names the unmet criterion and quotes the last ten [Spin], [Run] and [RsBattle] lines.
 */
export async function waitForLine(pattern: RegExp, count: number, timeoutMs: number, criterion: string): Promise<string> {
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
      throw new Error(`${criterion}: ${found.length} of ${count} lines matching ${pattern} within ${timeoutMs / 1000} s; last lines:\n${lastBotLines(log)}`)
    }
    await sleep(250)
  }
}

/** The log split into encounters: each runs from one seek line (`[Spin] facing=` or `[Run] from`) to the next. */
export function battles(log: string[]): string[][] {
  const starts = log.flatMap((l, i) => (SPIN_LINE.test(l) || RUN_FROM.test(l) ? [i] : []))
  return starts.map((start, k) => log.slice(start, starts[k + 1]))
}

/** The bot's press lines after the target line: none may follow it. */
export function pressesAfterTarget(log: string[]): string[] {
  const at = log.findIndex((l) => TARGET.test(l))
  return at === -1 ? [] : log.slice(at + 1).filter((l) => PRESS_LINE.test(l))
}

/**
 * Encounter n's battle was a clean flee, each press confirmed by the value it changed: A at the first message and A at
 * "Got away safely!" (each window a button wait, and the snapshot after it no longer at that window), the encounter
 * line between them, the cursor from FIGHT to RUN with Prompt 3 and no repair, one A on RUN that left the action menu,
 * and "5 presses, 1 on RUN". Fails naming the spec's criterion and the encounter.
 */
export function checkFlee(b: string[], n: number): void {
  const criterion = `"per encounter exactly five battle presses, each with its confirming value": encounter ${n}`
  const at = (pattern: RegExp): number[] => b.flatMap((l, i) => (pattern.test(l) ? [i] : []))
  const advances = at(ADVANCE)
  const encounters = at(ENCOUNTER)
  const toRun = at(TO_RUN)
  const confirms = at(CONFIRM)
  const overs = at(OVER)
  const lines = b.filter((l) => /\[(Spin|Run|RsBattle)\]/.test(l)).join('\n')
  const fail = (what: string): never => {
    throw new Error(`${criterion}: ${what}; its lines:\n${lines}`)
  }
  if (encounters.length !== 1) fail(`${encounters.length} encounter lines, not 1`)
  if (advances.length !== 2) fail(`${advances.length} BattleAdvance lines, not 2`)
  for (const i of advances) {
    const [, window, , snapshot] = ADVANCE.exec(b[i])!
    if (!BUTTON_WAITS.includes(Number(window))) fail(`a BattleAdvance at window ${window}, not a button wait`)
    if (snapshotField(snapshot, 'TextPrinter') === window) fail(`a BattleAdvance whose snapshot still has TextPrinter ${window}`)
  }
  if (toRun.length !== 1) fail(`${toRun.length} BattleCursorToRun lines, not 1`)
  const [, from, , cursorSnapshot] = TO_RUN.exec(b[toRun[0]])!
  if (from !== '0') fail(`BattleCursorToRun from cursor ${from}, not 0`)
  if (snapshotField(cursorSnapshot, 'Prompt') !== '3') fail('the cursor snapshot does not have Prompt = 3')
  if (at(REPAIR).length !== 0) fail('a cursor repair line')
  if (confirms.length !== 1) fail(`${confirms.length} BattleConfirm lines, not 1`)
  const [, tryNo, , leftSnapshot] = CONFIRM.exec(b[confirms[0]])!
  if (tryNo !== '1') fail(`BattleConfirm try ${tryNo}, not 1`)
  if (snapshotField(leftSnapshot, 'Screen') === 'BattleMenu') fail('the BattleConfirm snapshot is still BattleMenu')
  if (overs.length !== 1) fail(`${overs.length} battle over lines, not 1`)
  const [, , , presses, onRun] = OVER.exec(b[overs[0]])!
  if (presses !== '5' || onRun !== '1') fail(`battle over with ${presses} presses, ${onRun} on RUN, not 5 and 1`)
  const order = [advances[0], encounters[0], toRun[0], confirms[0], advances[1], overs[0]]
  if (order.some((v, i) => i > 0 && v < order[i - 1])) fail('the lines are not in the order A, encounter, cursor, A on RUN, A, over')
}

/** Frames per ms from the `battle over` lines: their frames over their ms, the same interval. */
export function fps(log: string[]): number {
  const overs = log.flatMap((l) => {
    const m = OVER.exec(l)
    return m ? [{ frames: Number(m[1]), ms: Number(m[2]) }] : []
  })
  return overs.reduce((sum, o) => sum + o.frames, 0) / overs.reduce((sum, o) => sum + o.ms, 0)
}

// The G2 line (bot task W2c, #398), for rs-wild-run and rs-wild-speed: the macro to the field leaving free (the roll),
// then the roll to the battle screen, 0 when no poll saw the field leave free ("not observed").
export const SEEK = /\[(Spin|Run)\] seek: (\d+) ms from the macro to the field leaving free, (\d+) ms to the battle screen\s*$/
export type Seek = { toRoll: number; rollToBattle: number }
/** The encounter's seek line; null for a seek the cancel cut off. */
export function seekOf(b: string[]): Seek | null {
  const m = b.map((l) => SEEK.exec(l)).find((x) => x !== null)
  return m ? { toRoll: Number(m[2]), rollToBattle: Number(m[3]) } : null
}

/**
 * "Seeking at no more than 9 frames a check" (G4): every seek after a battle makes at least five checks (four immune, then
 * the roll), so the shortest macro-to-roll in frames over 5 bounds the frames a check. It fails falsely only when no seek
 * rolled on its first open check, (8/9)^seeks.
 */
export function seekBound(toRollMs: number[], framesPerMs: number): { perCheck: number; falseFailure: number } {
  return { perCheck: (Math.min(...toRollMs) * framesPerMs) / 5, falseFailure: (8 / 9) ** toRollMs.length }
}

/** This run's encounter rows for the ROM and the milestone, in order: PID, Species, Level, IsShiny, Timestamp. */
export function psqlRows(pg: NodeJS.ProcessEnv, runStart: string, rom: string): string[][] {
  const rows = psql(pg, `SELECT "PID","Species","Level","IsShiny","Timestamp" FROM encounters WHERE "GameFileName"='${rom}' ` +
    `AND "Milestone"='${PHASE}' AND "Timestamp" >= '${runStart}' ORDER BY "Timestamp"`)
  step(`encounter rows since the Run: ${rows.join(' ; ')}`)
  return rows.map((r) => r.split('|'))
}

// --- the save: compared on the flash only, and restored from the pristine copy, never the .bak -------------------

const flash = (file: string): Buffer => fs.readFileSync(file).subarray(0, FLASH)

/** Copies the pristine save over the ROM's SaveRAM and .bak. Only with no EmuHawk running, which would overwrite them. */
export function installPristine(rom: string): void {
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(fs.existsSync(PRISTINE), `missing ${PRISTINE}`).toBe(true)
  expect(sha256(PRISTINE).startsWith(PRISTINE_SHA_PREFIX), `${PRISTINE} is not the pristine qa-route101 save`).toBe(true)
  fs.copyFileSync(PRISTINE, saveOf(rom))
  fs.copyFileSync(PRISTINE, saveOf(rom) + '.bak')
  step(`pristine save installed for ${rom}: sha256 ${sha256(saveOf(rom))}`)
}

/** "The save's first 131,072 bytes unchanged": the ROM's SaveRAM against the pristine copy, once EmuHawk has closed. */
export function flashUnchanged(rom: string): boolean {
  const same = flash(saveOf(rom)).equals(flash(PRISTINE))
  step(`${rom} SaveRAM flash ${same ? 'equals' : 'differs from'} the pristine copy (sha256 ${sha256(saveOf(rom))})`)
  return same
}

/** For the teardown, once EmuHawk is gone: keeps the end-state save in the run folder, then installs the pristine one. */
export function keepAndRestore(rom: string): void {
  for (const file of [saveOf(rom), saveOf(rom) + '.bak']) {
    if (fs.existsSync(file)) fs.copyFileSync(file, path.join(runDir, path.basename(file)))
  }
  installPristine(rom)
}

// --- the cheat: installed under the game's name, loaded disabled, armed and disarmed only during a battle ----------

export function removeCheats(): void {
  for (const rom of Object.keys(ROMS)) {
    if (fs.existsSync(cheatOf(rom))) {
      fs.rmSync(cheatOf(rom))
      step(`deleted ${cheatOf(rom)}`)
    }
  }
}

/** Writes wild-shiny.cht with LF line ends (the checkout may have CRLF) under the ROM's game name. */
export function installCheat(rom: string): void {
  fs.mkdirSync(path.dirname(cheatOf(rom)), { recursive: true })
  fs.writeFileSync(cheatOf(rom), fs.readFileSync(CHEAT_SOURCE, 'utf8').replace(/\r\n/g, '\n'))
  step(`cheat installed as ${cheatOf(rom)}`)
}

const hex = (v: number): string => (v >>> 0).toString(16).toUpperCase().padStart(8, '0')

/** n sequential reads of gRngValue, 200 ms apart. */
export async function rngReads(n: number): Promise<number[]> {
  const values: number[] = []
  for (let i = 0; i < n; i++) {
    if (i > 0) await sleep(200)
    values.push(await readU32(RNG))
  }
  step(`gRngValue reads: ${values.map(hex).join(', ')}`)
  return values
}

export const readBattleBit = async (): Promise<number> => ((await readU8(IN_BATTLE)) >> 1) & 1

/** At launch, with the bot idle: the cheat did not load armed. */
export async function assertUnarmed(): Promise<void> {
  const values = await rngReads(3)
  expect(new Set(values).size > 1 && !values.every((v) => v === RNG_FROZEN),
    `the cheat loaded armed (DisableOnLoad off?): gRngValue read ${values.map(hex).join(', ')}`).toBe(true)
}

/** The hotkey during a battle; then the battle bit (the hotkey's Select landed in the battle) and three frozen reads. */
export async function armCheat(): Promise<void> {
  expect(sendCheatHotkey(), 'EmuHawk was not the foreground window when the arming hotkey was sent').toBe(true)
  expect(await readBattleBit(), 'the hotkey landed after the battle: the battle bit reads 0').toBe(1)
  const values = await rngReads(3)
  expect(values.every((v) => v === RNG_FROZEN), `the cheat is not armed: gRngValue read ${values.map(hex).join(', ')}`).toBe(true)
  step('cheat armed')
}

/** The hotkey again, with the bot Waiting in the battle: gRngValue moves again, and the battle bit is still set. */
export async function disarmCheat(): Promise<void> {
  expect(sendCheatHotkey(), 'EmuHawk was not the foreground window when the disarming hotkey was sent').toBe(true)
  const values = await rngReads(3)
  expect(values.every((v) => v === RNG_FROZEN), `the cheat is still armed: gRngValue read ${values.map(hex).join(', ')}`).toBe(false)
  expect(await readBattleBit(), '"the cheat disarmed during that battle": the battle bit reads 0').toBe(1)
  step('cheat disarmed in the battle')
}

// --- the frame -----------------------------------------------------------------------------------------

/**
 * Wild Encounter offered on the card. An RS bot has this one action, and the card shows a button per action only when
 * there are two or more, so the action's own form (its milestone field) is what shows it is offered.
 */
export function offered(card: Locator): Locator {
  return card.getByTestId('param-PhaseName')
}

/**
 * Launch Bot + Continue on the ROM, recorded as `<recording>.mp4` unless `record` is false (rs-wild-speed: the window
 * recorder slows the emulator); returns the card once the player is on the start tile.
 */
export async function launchRs(admin: Page, rom: string, recording: string, record = true): Promise<Locator> {
  const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
  await launch.locator('select').selectOption(rom)
  await launch.getByRole('button', { name: 'Start', exact: true }).click()
  const card = admin.getByTestId('emulator-card-0')
  await expect(offered(card)).toBeVisible({ timeout: 5 * MINUTE })
  // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch, which
  // waits for the free field, returns.
  await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * MINUTE })
  if (record) startEmulatorRecorder(recording)
  else step('emulator recorder off for the speed run')
  const tile = await readTile()
  step(`${rom} launched: ${JSON.stringify(tile)}`)
  expect(tile, 'not the save\'s start tile, (14, 4) on Route 101 (0, 16)').toEqual(START)
  return card
}

export type Mode = { mode: 'Spin' } | { mode: 'Run'; yRange: number } | { mode: 'Run'; xRange: number }

/**
 * On the card's Wild Encounter form: fails naming the milestone if it is not offered, sets ShinyTarget and the mode,
 * runs `beforeRun` on the form, clicks Run and returns the ISO time of the click.
 */
export async function startWild(card: Locator, mode: Mode, beforeRun?: () => Promise<void>): Promise<string> {
  const picker = offered(card)
  const phase = card.getByTestId('param-PhaseName').locator('select')
  try {
    await expect(phase.locator(`option[value="${PHASE}"]`)).toHaveCount(1, { timeout: 30_000 })
  } catch {
    throw new Error(`no RS \`${PHASE}\` milestone is offered; write it to Mongo (qa-automation.md, Phases)`)
  }
  await phase.selectOption(PHASE)
  await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
  await card.getByTestId('param-EncounterType').locator('select').selectOption(mode.mode)
  if ('yRange' in mode) await card.getByTestId('param-yRange').locator('input').fill(String(mode.yRange))
  if ('xRange' in mode) await card.getByTestId('param-xRange').locator('input').fill(String(mode.xRange))
  if (beforeRun) await beforeRun()
  const runStart = new Date().toISOString()
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  await expect(picker).toHaveCount(0, { timeout: 30_000 })
  step(`${ACTION} ${mode.mode} started at ${runStart}`)
  return runStart
}

async function cancelOnce(card: Locator): Promise<void> {
  // The operator may have switched the Admin window to another tab during the run; the card is only on Bot Control.
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name: 'Cancel', exact: true }).click()
  await card.getByRole('button', { name: 'Yes', exact: true }).click()
}

async function botGone(card: Locator): Promise<void> {
  await expect(card).toHaveCount(0, { timeout: MINUTE })
  await stopRecorder()
  await expect.poll(emuHawkRunning, { timeout: MINUTE }).toBe(false)
  step('bot cancelled; no EmuHawk running')
}

/** A running event: the first Cancel ends it and leaves the bot idle, then `between` runs, and the second removes the bot. */
export async function cancelEventAndBot(card: Locator, between?: () => Promise<void>): Promise<void> {
  await cancelOnce(card)
  await expect(offered(card)).toBeVisible({ timeout: MINUTE })
  step('event cancelled')
  if (between) await between()
  await cancelOnce(card)
  await botGone(card)
}

/** An idle bot: one Cancel removes it. */
export async function cancelBot(card: Locator): Promise<void> {
  await cancelOnce(card)
  await botGone(card)
}

/** Asks EmuHawk to close (CloseMainWindow, which flushes SaveRAM), never kills it, and waits 60 s for it to exit. */
async function closeEmuHawk(): Promise<void> {
  spawnSync('powershell', ['-NoProfile', '-Command', 'Get-Process EmuHawk -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }'])
  const deadline = Date.now() + MINUTE
  while (emuHawkRunning() && Date.now() < deadline) await sleep(1000)
  step(`EmuHawk asked to close: ${emuHawkRunning() ? 'still running after 60 s' : 'exited'}`)
}

/** For the teardown: at most two cancels through card 0 while it shows Cancel; a missing window, card or button ends it. */
async function cancelLeftovers(app: ElectronApplication): Promise<void> {
  for (let attempt = 1; attempt <= 2; attempt++) {
    try {
      const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 5000)
      const card = admin.getByTestId('emulator-card-0')
      await admin.getByRole('button', { name: 'Bot Control', exact: true }).click({ timeout: 5000 })
      const cancel = card.getByRole('button', { name: 'Cancel', exact: true })
      if (!(await cancel.isVisible())) return
      await cancel.click({ timeout: 5000 })
      await card.getByRole('button', { name: 'Yes', exact: true }).click({ timeout: 5000 })
      // The event's cancel leaves the bot idle with its form; the bot's removes the card.
      await expect.poll(async () => (await card.count()) === 0 || (await offered(card).isVisible()), { timeout: MINUTE }).toBe(true)
      step(`teardown: cancel ${attempt} sent through the card`)
    } catch (e) {
      step(`teardown: cancel ${attempt} not made: ${(e as Error).message.split('\n')[0]}`)
      return
    }
  }
}

/**
 * The finally. EmuHawk is the backend's child and dies with its process tree, unflushed, so it is asked to close first;
 * only once it has exited is the backend killed and the save kept and restored. If it is still running after 60 s, the
 * backend, EmuHawk and the save are left as they are. The cheat files are removed either way. On a failure path the bot
 * may still be running, so first up to two cancels go through its card (event, then bot), while it shows Cancel.
 */
export async function teardown(frame: Frame, rom: string): Promise<void> {
  if (frame.app) await cancelLeftovers(frame.app)
  await stopRecorder()
  if (frame.app) await frame.app.close().catch(() => undefined)
  if (emuHawkRunning()) await closeEmuHawk()
  removeCheats()
  if (emuHawkRunning()) {
    step('EmuHawk still running after 60 s; the backend, EmuHawk and SaveRAM left alone')
    return
  }
  if (frame.backend && frame.backend.exitCode === null) killTree(frame.backend.pid!)
  keepAndRestore(rom)
}

/** getDashboardState for position 0. */
export const dashboardState = async (): Promise<any> => (await (await fetch(`${API}/bot/getDashboardState`)).json())['0']

// --- RAM -------------------------------------------------------------------------------------------------

export type Tile = { group: number; num: number; x: number; y: number }
/** The player's tile and map, from one 6-byte read. */
export async function readTile(): Promise<Tile> {
  const b = await readMemory(POS, 6)
  return { group: b[4], num: b[5], x: b.readInt16LE(0), y: b.readInt16LE(2) }
}

export const readU8 = async (addr: number): Promise<number> => (await readMemory(addr, 1))[0]
export { readU16, readU32 }

/**
 * Writes `bytes` at `addr`: one read, then a sequential, awaited writeU8 for each byte that differs (ApiHawk has no
 * multi-byte write), then one read-back that must match. Returns the number of bytes written.
 */
export async function writeBytes(addr: number, bytes: Buffer): Promise<number> {
  const before = await readMemory(addr, bytes.length)
  let written = 0
  for (let i = 0; i < bytes.length; i++) {
    if (before[i] === bytes[i]) continue
    await writeU8(addr + i, bytes[i])
    written++
  }
  const after = await readMemory(addr, bytes.length)
  expect(after.equals(bytes), `0x${hex(addr)} does not read back as written: ${after.toString('hex')}`).toBe(true)
  step(`wrote ${written} of ${bytes.length} bytes at 0x${hex(addr)}`)
  return written
}

/**
 * Only while the bot is Waiting (or idle): one small read every 500 ms until `predicate` holds, bounded; fails naming
 * `what` and the last value.
 */
export async function pollWhileWaiting(read: () => Promise<number>, predicate: (v: number) => boolean, ms: number, what: string): Promise<number> {
  const deadline = Date.now() + ms
  let last = await read()
  while (!predicate(last)) {
    if (Date.now() > deadline) throw new Error(`${what} within ${ms / 1000} s; last read ${last}`)
    await sleep(500)
    last = await read()
  }
  step(`${what}: read ${last}`)
  return last
}

// --- rs-wild-sapphire and rs-wild-ruby -----------------------------------------------------------------------

/** "Headbutt, No Target and the train types greyed out with their reasons", on the open form before Run. */
async function checkGreyed(card: Locator): Promise<void> {
  const criterion = '"Headbutt, No Target and the train types greyed out with their reasons"'
  const greyed: [string, string, string][] = [
    ['EncounterType', 'Headbutt', HEADBUTT_SENTENCE],
    ['TargetType', 'NoTarget', NO_TARGET_SENTENCE],
    ['TrainType', 'Standard', TRAINING_SENTENCE],
    ['TrainType', 'Switch', TRAINING_SENTENCE]
  ]
  for (const [param, value, reason] of greyed) {
    const option = card.getByTestId(`param-${param}`).locator(`option[value="${value}"]`)
    await expect(option, `${criterion}: no ${param} option ${value}`).toBeAttached({ timeout: 30_000 })
    const label = (await option.textContent()) ?? ''
    step(`${param} ${value}: "${label}", disabled ${await option.isDisabled()}`)
    expect(label, `${criterion}: the ${param} ${value} option's label`).toBe(`${value} (${reason})`)
    expect(await option.isDisabled(), `${criterion}: the ${param} ${value} option is enabled`).toBe(true)
  }
}

/**
 * Spin on the ROM for `n` encounters from the UI, then two cancels with one tile read between them, and the checks of
 * the spec's rs-wild-sapphire row. rs-wild-ruby runs the same on RU-Wild, which shows one address table serves both.
 */
export async function wildSpin(rom: string, n: number): Promise<void> {
  const frame: Frame = { backend: null, app: null }
  const { pg } = settings()
  removeCheats()
  installPristine(rom)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, rom, 'emulator')
    await shotAll('01-launched', pages)
    const runStart = await startWild(card, { mode: 'Spin' }, async () => {
      await checkGreyed(card)
      await shotAll('02-greyed', pages)
    })

    // The shiny-lead speed line at Run: in the log, in the dashboard state and on the card.
    const speed = '"the shiny-lead speed line at Run"'
    await waitForLine(SPEED_SHINY, 1, MINUTE, `${speed}: not in backend.log`)
    const state = await dashboardState()
    step(`dashboard state: ${JSON.stringify(state)}`)
    expect(state?.speedLines ?? [], `${speed}: not in getDashboardState's speedLines`).toContain(SHINY_LEAD_LINE)
    await expect(card.getByText(SHINY_LEAD_LINE, { exact: true }), `${speed}: not on the emulator card`).toBeVisible()

    await waitForLine(OVER, n, n * MINUTE, `"${n} encounters rows": ${n} fled battles`)
    await shotAll('03-hunting', pages)
    let tile: Tile | null = null
    await cancelEventAndBot(card, async () => {
      tile = await readTile()
      step(`tile between the cancels: ${JSON.stringify(tile)}`)
    })
    await shotAll('04-cancelled', pages)
    expect(flashUnchanged(rom), `"the save's first 131,072 bytes unchanged": the ${rom} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    const log = backendLog()
    // "the player still on (14, 4)": the read between the cancels, and no seek that found the player off the tile.
    expect(tile, '"the player still on (14, 4)": the tile read between the cancels').toEqual(START)
    expect(log.filter((l) => MOVED.test(l)), '"the player still on (14, 4)": a moved-while-turning line').toEqual([])

    // "the turns in the order Up, Left, Down, Right": every seek's macro starts after its facing (G3).
    for (const line of log.filter((l) => SPIN_LINE.test(l))) {
      const [, facing, macro] = SPIN_LINE.exec(line)!
      expect(macro, `"the turns in the order Up, Left, Down, Right": facing ${facing}`).toBe(spinMacroFor(facing))
    }

    // "per encounter exactly five battle presses, each with its confirming value", on the n finished battles.
    const all = battles(log)
    for (let k = 0; k < n; k++) checkFlee(all[k], k + 1)

    // "ten encounters rows, species Wurmple, Zigzagoon or Poochyena, Lv 2 or 3": one row per encounter line, by PID.
    const rowsCriterion = `"${n} encounters rows, species Wurmple, Zigzagoon or Poochyena, Lv 2 or 3"`
    const encounters = log.filter((l) => ENCOUNTER.test(l)).map(parseEncounter)
    const rows = psqlRows(pg, runStart, rom)
    expect(rows.length, `${rowsCriterion}: too few rows`).toBeGreaterThanOrEqual(n)
    expect(rows.map((r) => hex8(Number(r[0]))), `${rowsCriterion}: the rows' PIDs are not the encounter lines'`)
      .toEqual(encounters.map((e) => e.pid))
    for (const [pid, species, level, shiny] of rows) {
      expect(WILD, `${rowsCriterion}: ${pid} is species ${species}`).toContain(Number(species))
      expect(LEVELS, `${rowsCriterion}: ${pid} is Lv ${level}`).toContain(Number(level))
      expect(shiny, `${rowsCriterion}: ${pid} is shiny`).toBe('f')
    }
  } finally {
    await teardown(frame, rom)
  }
}
