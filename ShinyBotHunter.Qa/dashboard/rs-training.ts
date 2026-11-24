// The pieces the RS training and navigation scenarios share (#433, Q1a; docs/specs/rs-navigation-and-training.md, QA and
// FR-19): the Gen 3 party record codec and the party writes, the experience table, the [RsBattle], [Navigator] and
// [Orchestrator] lines and the press count, the Discord prompt flow, the training form and frame, and the scenario's own
// walk. It builds on rs-wild.ts's frame and RAM calls. Every write here is scenario set-up, made with the bot idle and
// logged to steps.log with its address, the old bytes and the new ones.
import * as fs from 'fs'
import * as path from 'path'
import { expect, type Locator, type Page } from '@playwright/test'
import { clearMacro, readMemory, repoDir, runMacro, shotAll, step } from './driver'
import {
  ADVANCE, cancelBot, cancelEventAndBot, CONFIRM, dashboardState, ENCOUNTER, flashUnchanged, installPristine, launchRs, LOCK,
  OVER, PARTY, PARTY_COUNT, parseEncounter, psqlRows, readBattleBit, readTile, readU32, readU8, removeCheats, ROMS, ROUTE101, type Save,
  SLOT_SIZE, startWild, STOPPED, teardown, TO_RUN, type Tile, writeBytes
} from './rs-wild'
import { backendLog, CRASHED, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// --- the party record: pret include/pokemon.h (struct Pokemon, 100 bytes) and src/pokemon_2.c (GetSubstruct) ---------

// Internal species indices (include/constants/species.h): the party record holds these, the log lines the national number.
export const MUDKIP = 283
export const MARSHTOMP = 284

// The storage slot of the growth, attacks, EVs and misc substructures, by PID % 24: GetSubstruct's SUBSTRUCT_CASE rows, the
// same as Gen3PokemonParser.SubstructOrder.
const ORDER = ['0123', '0132', '0213', '0312', '0231', '0321', '1023', '1032', '2013', '3012', '2031', '3021',
  '1203', '1302', '2103', '3102', '2301', '3201', '1230', '1320', '2130', '3120', '2310', '3210']
const CHECKSUM = 28
const SECURE = 32
const LEVEL = 84
// The party-only u16 stats after the level (struct Pokemon); speed, at 94, is not written by any scenario.
const STATS = { hp: 86, maxHp: 88, attack: 90, defense: 92, spAtk: 96, spDef: 98 } as const

/** A decoded party record. species is the internal index; moves and pp are the four slots, 0 for an empty one. */
export type Mon = {
  pid: number; otId: number; checksum: number; checksumOk: boolean; species: number; item: number; exp: number
  moves: number[]; pp: number[]; level: number; hp: number; maxHp: number; attack: number; defense: number; spAtk: number; spDef: number
}
export type Changes = Partial<Pick<Mon, 'species' | 'item' | 'exp' | 'moves' | 'pp' | 'level' | keyof typeof STATS>>

const sum16 = (plain: Buffer): number => {
  let sum = 0
  for (let i = 0; i < 48; i += 2) sum += plain.readUInt16LE(i)
  return sum & 0xffff
}

/** Moves the 48 secure bytes between storage and logical order (growth, attacks, EVs, misc), XORing each word with PID ^ OTID. */
function crypt(from: Buffer, to: Buffer, pid: number, otId: number, toLogical: boolean): void {
  const order = ORDER[pid % 24]
  const key = (pid ^ otId) >>> 0
  for (let block = 0; block < 4; block++) {
    const stored = SECURE + 12 * Number(order[block])
    const logical = 12 * block
    for (let w = 0; w < 12; w += 4) {
      const [src, dst] = toLogical ? [stored + w, logical + w] : [logical + w, stored + w]
      to.writeUInt32LE((from.readUInt32LE(src) ^ key) >>> 0, dst)
    }
  }
}

export function decodeRecord(raw: Buffer): Mon {
  const pid = raw.readUInt32LE(0)
  const otId = raw.readUInt32LE(4)
  const plain = Buffer.alloc(48)
  crypt(raw, plain, pid, otId, true)
  const checksum = raw.readUInt16LE(CHECKSUM)
  const stats = Object.fromEntries(Object.entries(STATS).map(([k, offset]) => [k, raw.readUInt16LE(offset)])) as Record<keyof typeof STATS, number>
  return {
    pid, otId, checksum, checksumOk: sum16(plain) === checksum, species: plain.readUInt16LE(0), item: plain.readUInt16LE(2),
    exp: plain.readUInt32LE(4), moves: [0, 1, 2, 3].map((i) => plain.readUInt16LE(12 + 2 * i)), pp: [0, 1, 2, 3].map((i) => plain[20 + i]),
    level: raw[LEVEL], ...stats
  }
}

/** The record with `changes` made, re-encrypted under the same PID and OTID with its checksum recomputed. */
export function encodeRecord(raw: Buffer, changes: Changes): Buffer {
  const pid = raw.readUInt32LE(0)
  const otId = raw.readUInt32LE(4)
  const plain = Buffer.alloc(48)
  crypt(raw, plain, pid, otId, true)
  if (changes.species !== undefined) plain.writeUInt16LE(changes.species, 0)
  if (changes.item !== undefined) plain.writeUInt16LE(changes.item, 2)
  if (changes.exp !== undefined) plain.writeUInt32LE(changes.exp, 4)
  changes.moves?.forEach((move, i) => plain.writeUInt16LE(move, 12 + 2 * i))
  changes.pp?.forEach((pp, i) => plain.writeUInt8(pp, 20 + i))
  const out = Buffer.from(raw)
  crypt(plain, out, pid, otId, false)
  out.writeUInt16LE(sum16(plain), CHECKSUM)
  if (changes.level !== undefined) out[LEVEL] = changes.level
  for (const [k, offset] of Object.entries(STATS)) {
    const value = changes[k as keyof typeof STATS]
    if (value !== undefined) out.writeUInt16LE(value, offset)
  }
  return out
}

// --- experience and moves ----------------------------------------------------------------------------------------

/**
 * The experience at the start of a level on the Medium Slow curve, Mudkip's and Marshtomp's (pret base_stats.h): 6/5 n³ −
 * 15 n² + 100 n − 140, which is pret's experience_tables.h column for levels 2 to 100.
 */
export const expAt = (level: number): number => Math.floor((6 * level ** 3) / 5) - 15 * level ** 2 + 100 * level - 140

/**
 * One experience point short of `level`, with the level byte at `levelByte`: a Lv 2 or 3 wild gives 15 to 25 points, so
 * the level byte is what puts the level-up in reach (the research's "How the states were reached"). A level byte further
 * down crosses more levels in one battle.
 */
export const oneShort = (level: number, levelByte = level - 1): Changes => ({ level: levelByte, exp: expAt(level) - 1 })

// The moves the scenarios write, by id, with their PP: Tackle, Growl, Mud-Slap, Water Gun, Bide, Foresight, Mud Shot.
export const PP: Record<number, number> = { 33: 35, 45: 40, 189: 10, 55: 25, 117: 10, 193: 40, 341: 15 }
/** The four move slots and their full PP; fewer ids leave the last slots empty. */
export function movesOf(...ids: number[]): Changes {
  const moves = [0, 1, 2, 3].map((i) => ids[i] ?? 0)
  return { moves, pp: moves.map((id) => (id === 0 ? 0 : PP[id])) }
}

// --- the party writes: each made with the bot idle, logged, then decoded again ---------------------------------------

/** Party slot `slot`'s record address, 1-based as the dashboard's SwitchSlot (the log lines count from 0). */
export const slotAddr = (slot: number): number => PARTY + (slot - 1) * SLOT_SIZE

/** Scenario set-up: logs what, the address and the old and new bytes to steps.log, then writes them through writeBytes. */
export async function writeLogged(what: string, addr: number, bytes: Buffer): Promise<void> {
  const old = await readMemory(addr, bytes.length)
  step(`set-up: ${what} at 0x${hex8(addr)}: ${old.toString('hex')} -> ${bytes.toString('hex')}`)
  await writeBytes(addr, bytes)
}

export const readSlot = async (slot: number): Promise<Mon> => decodeRecord(await readMemory(slotAddr(slot), SLOT_SIZE))

/** Writes `changes` into slot `slot` (1-based) and decodes it back: the checksum must hold and every change must read back. */
export async function writeSlot(slot: number, changes: Changes): Promise<Mon> {
  const raw = await readMemory(slotAddr(slot), SLOT_SIZE)
  const before = decodeRecord(raw)
  expect(before.checksumOk, `slot ${slot} fails its checksum before the write: ${JSON.stringify(before)}`).toBe(true)
  const fields = (Object.keys(changes) as (keyof Changes)[]).map((k) => `${k} ${JSON.stringify(before[k])} -> ${JSON.stringify(changes[k])}`)
  await writeLogged(`slot ${slot} ${fields.join(', ')}`, slotAddr(slot), encodeRecord(raw, changes))
  const after = await readSlot(slot)
  expect(after.checksumOk, `slot ${slot} fails its checksum after the write: ${JSON.stringify(after)}`).toBe(true)
  for (const k of Object.keys(changes) as (keyof Changes)[]) expect(after[k], `slot ${slot}'s ${k} after the write`).toEqual(changes[k])
  return after
}

/** The battler as the research made it: the lead's 100 bytes copied to slot 2, then the party count 2. */
export async function copyLeadToSlot2(): Promise<void> {
  await writeLogged('slot 2 = a copy of the lead', slotAddr(2), await readMemory(slotAddr(1), SLOT_SIZE))
  await writeLogged('party count', PARTY_COUNT, Buffer.from([2]))
}

/** The party count's members, decoded. */
export async function readParty(): Promise<Mon[]> {
  const count = await readU8(PARTY_COUNT)
  const raw = await readMemory(PARTY, count * SLOT_SIZE)
  return Array.from({ length: count }, (_, i) => decodeRecord(raw.subarray(i * SLOT_SIZE, (i + 1) * SLOT_SIZE)))
}

// --- log patterns: each matches one Console.WriteLine; the comment names the PR or the task that added it ------------

// RsBattleEvent.PressConfirmedAsync (T1a, #446): every gated press of the training rules, T2's switch presses included.
// Its `what` is e.g. `BattleConfirm on FIGHT`, `Down on the move list 0 -> 2`, `BattleConfirm on move 1`,
// `level-up box page 1 for slot 0: BattleConfirm`, `B at Delete a move?`, `BattleConfirm (Yes) at Delete a move?`,
// `BattleConfirm (Yes) at Stop learning?`, `Down on the forget-a-move screen 0 -> 1`, `BattleConfirm on forget slot 1`;
// its `by` e.g. `the move list`, `cursor 2`, `page 2`, `the box leaving page 2`, `the summary task closing`.
// Groups: what, ms, by, snapshot, the repair.
export const PRESS = /\[RsBattle\] (.+?), (\d+) ms: confirmed by (.+?) \((GameSnapshot \{.*\})\)(; dropped, sent again)?\s*$/
// T1a (#446): the damage picker's choice, between FIGHT and the move cursor.
export const CHOSEN = /\[RsBattle\] move (\d) (.+?) chosen by the damage picker, lead HP (\d+)\/(\d+) PP (\d+), wild HP (\d+)\s*$/
// T1a (#446): the move prompt, then the answer. Slots count from 0.
export const ASKING = /\[RsBattle\] asking Discord: slot (\d) (.+?) learning (.+) \((\d+)\); nothing is pressed until the answer\s*$/
export const ANSWER = /\[RsBattle\] Delete a move\? slot (\d) (.+?) learning (.+) \((\d+)\): (No|Yes over slot (\d) (.+?))\s*$/
export const FORGET_CLOSED = /\[RsBattle\] forget-a-move screen closed: (.*)$/
export const FLEEING = /\[RsBattle\] fleeing: (.*?)\s*$/
// T1a (#446): one line per member after a training battle. The QA plan's L3 (` exp E` at the end) was not added by T2
// (#459): the line has no experience, which the scenarios read from memory with the bot idle. The optional group stays.
export const PARTY_AFTER = /\[RsBattle\] party after the battle: slot (\d) (.+?) \((\d+)\) Lv (\d+) moves ([\d,]+)(?: exp (\d+))?\s*$/
// T1b (#448): a text wait in the evolution scene; data[0] is the scene's step.
export const SCENE = /\[RsBattle\] BattleAdvance in the evolution scene of slot (\d) at data\[0\] (\d+) at window (\d*), (\d+) ms: (.*)$/
// RsBattleEvent's sending line after BattleCursorToRun: the press it sends, or nothing.
const SENT = /\[RsBattle\] cursor \d* after BattleCursorToRun; sending (.+?)\s*$/

// T2 (#459): the switch. The é of POKéMON is any one or two characters (backend.log is in the console's code page).
export const SWITCH_TRY = /\[RsBattle\] switch try (\d) of 3 to slot (\d): the party list takes keys, (\d+) ms after POK.{1,2}MON: (.*)$/
export const AFTER_SHIFT = /\[RsBattle\] after SHIFT, (\d+) ms: (.*)$/
export const SWITCH_CONFIRMED = /\[RsBattle\] switch confirmed: 0x02024A6A reads (\d) at the action menu, try (\d)/
export const SWITCH_MISSED = /\[RsBattle\] switch try (\d) did not land: 0x02024A6A reads (\d)/
// T2 (#459): the QA plan's L2 (the move prompt's gate) and L10 (a lock that is not a trainer's).
export const MOVE_PROMPT = /\[RsBattle\] move prompt at (.*)$/
export const LOCK_HANDED = /\[RsBattle\] lock handed over: not a trainer \((.*)\)\s*$/
// T3b (#451): the trainer stop's sentence with the map's internal name, then what was read.
export const TRAINER_STOP = /\[RsBattle\] stopped: A trainer battle started on (\w+)\. (.*)$/

// N4a (#449): RsNavigatorEvent's walk. Tiles are `<internal map name> (x, y)`.
export const PLAN = /\[Navigator\] plan to (\S+) \((\d+), (\d+)\) from (\S+) \((\d+), (\d+)\) elevation (\d+): cost (\d+), (\d+) steps, (\d+) segments, (\d+) flags set, live triggers crossed: (.*); script state (\d+) ms, search (\d+) ms\s*$/
export const SEGMENT = /\[Navigator\] segment (\d+)\/(\d+): (\S+) to (\S+) \((\d+), (\d+)\)\s*$/
export const SEGMENT_ON = /\[Navigator\] segment (\d+)\/(\d+): on (\S+) \((\d+), (\d+)\) after (\d+) frames\s*$/
export const NAV_BATTLE = /\[Navigator\] segment (\d+)\/(\d+): battle bit on (\S+) \((\d+), (\d+)\) after (\d+) frames; planning again after the battle\s*$/
export const NAV_HANDOFF = /\[Navigator\] segment (\d+)\/(\d+): field locked (\d+) frames on (\S+) \((\d+), (\d+)\) with no battle; handing the lock to the battle event\s*$/
export const ARRIVED = /\[Navigator\] arrived on (\S+) \((\d+), (\d+)\): (\d+) segments, (\d+) frames\s*$/
export const NAV_STOPPED = /\[Navigator\] stopped: (.*)$/
// N4b (#450): the nurse's presses (FieldAdvance or SpinUp, each read `(f N: …)`), the total, and the party's HP.
export const NURSE_PRESS = /\[Navigator\] nurse: (\w+) at (\(f \d+: .*?\)), confirmed by (\(f \d+: .*?\)) after (\d+) frames\s*$/
export const NURSE_DONE = /\[Navigator\] nurse: (\d+) presses, (\d+) frames\s*$/
export const NURSE_HP = /\[Navigator\] nurse: party HP (.*) \(full\)\s*$/
// NavigatorEvent.ClearNavigation, through FinishReturn (N4a, #449).
export const CLEARING = /\[Navigator\] Clearing navigation path \((arrived back at start|already at start)\) at segment/

// OrchestratorEvent: each state on top, and Resume after a trainer stop that found the field not free (N5, #439).
export const STATE = /\[Orchestrator\] State: (\w+)\s*$/
export const RESUME = /\[Orchestrator\] Resume: (.*)$/
// OrchestratorEvent.SlowTransitionLine (N5, #439); the é as above.
export const SLOW_TRANSITION = /\[Orchestrator\] A first Pok.{1,2}mon at or below the wild level gets the long battle transition\.\s*$/
// T3b (#451): the QA plan's L7, why a heal trip was taken.
export const HEAL_TRIP = /\[Orchestrator\] heal trip: (.+?) HP (\d+)\/(\d+), damaging PP (\d+)\s*$/

// DiscordBotService: the login at start-up, or no token; and a move prompt declined with no login.
export const DISCORD_READY = /Discord client ready\. Logged in as /
export const DISCORD_NO_TOKEN = /Discord bot token is not configured/
export const DISCORD_OUT = /Discord is not logged in; (.+) will not learn (.+)\.\s*$/

// --- the press count -------------------------------------------------------------------------------------------

// BattleCursorToRun's steps, as RsBattleEvent.PressAsync counts a macro.
const CURSOR_TO_RUN_STEPS = fs.readFileSync(path.join(repoDir, 'ShinyBotHunter', 'Macro', 'Configs', 'RS', 'battle_cursor_to_run.yaml'), 'utf8')
  .split(/\r?\n/).filter((l) => /^\s+- /.test(l)).length

/**
 * The presses one battle's lines account for, each as RsBattleEvent counts it: a confirmed press 1 (2 with its repair), a
 * BattleAdvance 1, BattleCursorToRun its macro's steps, the press sent after it its steps, and BattleConfirm on RUN 1.
 */
export function pressCount(lines: string[]): number {
  let n = 0
  for (const l of lines) {
    const press = PRESS.exec(l)
    const sent = SENT.exec(l)
    if (press) n += press[5] ? 2 : 1
    else if (ADVANCE.test(l) || SCENE.test(l) || CONFIRM.test(l)) n += 1
    else if (TO_RUN.test(l)) n += CURSOR_TO_RUN_STEPS
    else if (sent && sent[1] !== 'nothing') n += sent[1].split(';').length
  }
  return n
}

/**
 * Every finished battle (the lines up to its `battle over`, from the last one's): the presses its lines account for equal
 * its `N presses`, so a press without a line fails. The repairs of each kind of press go to steps.log, not failed (rule 16
 * allows that repair).
 */
export function checkPresses(log: string[]): void {
  let from = 0
  let battle = 0
  log.forEach((line, i) => {
    const over = OVER.exec(line)
    if (!over) return
    const lines = log.slice(from, i)
    from = i + 1
    battle++
    const counted = pressCount(lines)
    expect(counted, `"every press has its line": battle ${battle} logs ${over[3]} presses, its lines account for ${counted}:\n` +
      lines.filter((l) => l.includes('[RsBattle]')).join('\n')).toBe(Number(over[3]))
    const repairs: Record<string, number> = {}
    for (const m of lines.map((l) => PRESS.exec(l)).filter((m) => m?.[5])) {
      const kind = m![1].replace(/\d+/g, 'N')
      repairs[kind] = (repairs[kind] ?? 0) + 1
    }
    step(`battle ${battle}: ${counted} presses; dropped and sent again: ${JSON.stringify(repairs)}`)
  })
}

// --- the log extractors the scenarios share ------------------------------------------------------------------------

/** A gated press as `<what> | <confirmed by>`, or null for any other line. */
export const pressOf = (line: string): string | null => {
  const m = PRESS.exec(line)
  return m ? `${m[1]} | ${m[3]}` : null
}

/** The level-up boxes (`box <slot> <page>`) and the move prompts (`ask <slot>`), in log order. */
export const events = (log: string[]): string[] => log.flatMap((l) => {
  const box = /^level-up box page (\d) for slot (\d): BattleConfirm \|/.exec(pressOf(l) ?? '')
  const ask = ASKING.exec(l)
  return box ? [`box ${box[2]} ${box[1]}`] : ask ? [`ask ${ask[1]}`] : []
})

/** The n-th prompt's answer presses, from its asking line to the next prompt or the battle's end. */
export function answerPresses(log: string[], n: number): { presses: string[]; closed: number } {
  const ask = log.flatMap((l, i) => (ASKING.test(l) ? [i] : []))[n - 1]
  const end = log.findIndex((l, i) => i > ask && (MOVE_PROMPT.test(l) || OVER.test(l)))
  const lines = log.slice(ask, end)
  return {
    presses: lines.map(pressOf).filter((p): p is string => p !== null && /Delete a move\?|Stop learning\?|forget/.test(p)),
    closed: lines.filter((l) => FORGET_CLOSED.test(l)).length
  }
}

// The battle's switch: its try and the action menu that confirmed it.
export const switchLines = (b: string[]): string[] => b.flatMap((l) => {
  const t = SWITCH_TRY.exec(l)
  const c = SWITCH_CONFIRMED.exec(l)
  return t ? [`try ${t[1]} to slot ${t[2]}`] : c ? [`confirmed: reads ${c[1]}, try ${c[2]}`] : []
})
// The level-up box presses: page, slot and what confirmed each.
export const boxes = (b: string[]): string[] => b.flatMap((l) => {
  const m = PRESS.exec(l)
  const box = m && /^level-up box page (\d) for slot (\d): BattleConfirm$/.exec(m[1])
  return box ? [`page ${box[1]} for slot ${box[2]}, confirmed by ${m![3]}`] : []
})

/** A [Navigator] line's tile from its groups at, at + 1 and at + 2, as the line prints it: `<internal map name> (x, y)`. */
export const tileOf = (m: RegExpExecArray, at: number): string => `${m[at]} (${m[at + 1]}, ${m[at + 2]})`
export const indexOf = (log: string[], pattern: RegExp, from = 0): number => log.findIndex((l, i) => i >= from && pattern.test(l))

/**
 * "Back on the start tile", after the nurse (`healed`, the line of its party HP): the last arrival on the start tile, then
 * `arrived back at start`; or, when a wild battle starts on the last step onto the start tile, that battle bit on it, a
 * plan from it with 0 segments, and `already at start` (no arrival line). `criterion` begins each failure.
 */
export function checkHome(log: string[], healed: number, start: string, criterion: string): void {
  const cleared = indexOf(log, CLEARING, healed)
  expect(cleared, `${criterion}: no "Clearing navigation path" after the nurse`).toBeGreaterThan(healed)
  const reason = CLEARING.exec(log[cleared])![1]
  const before = log.slice(healed, cleared)
  step(`back on the start tile: ${reason}`)
  if (reason === 'arrived back at start') {
    const home = before.filter((l) => ARRIVED.test(l)).pop()
    expect(home, `${criterion}: no arrival line before the clear`).toBeDefined()
    expect(tileOf(ARRIVED.exec(home!)!, 1), `${criterion}: ${home}`).toBe(start)
    return
  }
  const bit = before.map((l) => NAV_BATTLE.exec(l)).filter((m) => m !== null).pop()
  expect(bit && tileOf(bit, 3), `${criterion}: "already at start" with no battle bit on the start tile before it`).toBe(start)
  const zero = before.slice(before.indexOf(bit!.input)).map((l) => PLAN.exec(l)).find((m) => m !== null)
  expect(zero && [tileOf(zero, 1), tileOf(zero, 4), zero[10]], `${criterion}: no plan from the start tile with 0 segments after ${bit!.input.trim()}`)
    .toEqual([start, start, '0'])
}

// --- waits ------------------------------------------------------------------------------------------------------

/**
 * Fails at once on a stop of the hunt, the battle or the navigator, a crash or a stale-timeout in the log. A stop line
 * `expectedStop` matches is not a failure, before or after it is waited for.
 */
export function failOnStops(log: string[], expectedStop?: RegExp): void {
  const stopped = log.find((l) => (STOPPED.test(l) || NAV_STOPPED.test(l)) && !expectedStop?.test(l))
  if (stopped) throw new Error(`the bot stopped: ${stopped.trim()}`)
  const crashed = log.find((l) => CRASHED.test(l) || l.includes('GameStateTimeoutException'))
  if (crashed) throw new Error(`the event crashed: ${crashed}`)
}

/**
 * rs-wild.ts's wait, which also fails on a [Navigator] stop: polls backend.log every 250 ms until `pattern` has matched
 * `count` lines, and returns the count-th. A timeout names the criterion and quotes the bot's last ten lines.
 */
export async function waitLine(pattern: RegExp, count: number, ms: number, criterion: string, { expectedStop }: { expectedStop?: RegExp } = {}): Promise<string> {
  const deadline = Date.now() + ms
  while (true) {
    const log = backendLog()
    const found = log.filter((l) => pattern.test(l))
    if (found.length >= count) {
      step(`logged: ${found[count - 1].trim()}`)
      return found[count - 1]
    }
    failOnStops(log, expectedStop)
    if (Date.now() > deadline) {
      const last = log.filter((l) => /\[(Spin|Run|RsBattle|Navigator|Orchestrator)\]/.test(l)).slice(-10).join('\n')
      throw new Error(`${criterion}: ${found.length} of ${count} lines matching ${pattern} within ${ms / 1000} s; last lines:\n${last}`)
    }
    await sleep(250)
  }
}

/** getDashboardState's eventState Complete, failing at once on a stop (other than `expectedStop`), a crash or a stale-timeout. */
export async function waitComplete(expectedStop: RegExp | undefined, ms: number): Promise<void> {
  const deadline = Date.now() + ms
  while ((await dashboardState())?.eventState !== 'Complete') {
    failOnStops(backendLog(), expectedStop)
    if (Date.now() > deadline) throw new Error(`"Complete": the event is not Complete within ${ms / MINUTE} minutes`)
    await sleep(1000)
  }
  step('the event is Complete')
}

// --- Discord: the operator answers the new-move prompts, as in the HGSS scenarios -----------------------------------

/** One GET of Discord's REST API with the bot token, read once by the caller and never logged. */
async function discordGet(token: string, route: string): Promise<any> {
  const res = await fetch(`https://discord.com/api/v10/${route}`, { headers: { Authorization: `Bot ${token}` } })
  if (!res.ok) throw new Error(`Discord GET ${route.split('?')[0]}: HTTP ${res.status}`)
  return res.json()
}

// ApplicationSettings:DiscordNewMoveChannelId, from appsettings.json: the user secrets do not set it (read 2026-11-17), and
// settings() does not return it. As text: the id is above Number.MAX_SAFE_INTEGER.
const newMoveChannel = (): string =>
  /"DiscordNewMoveChannelId"\s*:\s*"?(\d+)/.exec(fs.readFileSync(path.join(repoDir, 'ShinyBotHunter', 'appsettings.json'), 'utf8'))![1]

type DiscordMessage = {
  id: string; timestamp: string; content?: string
  embeds?: { title?: string; description?: string }[]
  components?: { components: { label?: string; custom_id?: string }[] }[]
}

/** The backend's Discord login, within 60 s of its start: true when logged in, false with no token. */
export async function discordMode(): Promise<boolean> {
  const line = await waitLine(new RegExp(`${DISCORD_READY.source}|${DISCORD_NO_TOKEN.source}`), 1, MINUTE,
    'the backend logged neither its Discord login nor "no token"')
  return DISCORD_READY.test(line)
}

/** The messages in ShinyLogChannel since `sinceIso` whose text contains `text` (the trainer stop's message). */
export async function discordMessages(sinceIso: string, text: string): Promise<string[]> {
  const { discordToken, channel } = settings()
  const messages = (await discordGet(discordToken, `channels/${channel}/messages?limit=50`)) as DiscordMessage[]
  const found = messages.filter((m) => Date.parse(m.timestamp) > Date.parse(sinceIso)).map((m) => m.content ?? '').filter((c) => c.includes(text))
  step(`Discord: ${found.length} messages since ${sinceIso} containing "${text}": ${JSON.stringify(found)}`)
  return found
}

/**
 * A move prompt: `n`, which prompt of the run it is (from 1, in the order the bot asks); the learner's party slot and the
 * forgotten move's slot as the log counts them, from 0; null for No.
 */
export type Prompt = { n: number; learner: number; move: number; forget: number | null }
const NTH = ['first', 'second', 'third', 'fourth', 'fifth', 'sixth']

/**
 * The run's n-th move prompt, answered: the one place a prompt's answer comes from. It waits for the n-th `asking Discord`
 * line and checks the learner and the move. Logged in, it finds the prompt's message in the new-move channel, newer than
 * the Run click, writes one OPERATOR: line naming the message, its place among this battle's prompts, the button and
 * Confirm Choice, waits 10 minutes for the bot's answer line (a wrong click or no click is a rerun), then reads the edited
 * message back. Not logged in, the bot declines on its own: the "not logged in" line and No. Returns the answer line.
 */
export async function answerPrompt(run: Run, expected: Prompt): Promise<string> {
  const { n } = expected
  const criterion = `"the move prompt answered": prompt ${n}`
  const [, learner, name, move, moveId] = ASKING.exec(await waitLine(ASKING, n, 10 * MINUTE, `${criterion}: no asking line`))!
  expect([Number(learner), Number(moveId)], `${criterion}: the learner slot and the move offered`).toEqual([expected.learner, expected.move])
  if (!run.loggedIn) {
    expect(expected.forget, `${criterion}: a Yes needs Discord logged in and an operator`).toBeNull()
    await waitLine(DISCORD_OUT, n, MINUTE, `${criterion}: no "Discord is not logged in" line`)
    const line = await waitLine(ANSWER, n, MINUTE, `${criterion}: no answer line`)
    expect(ANSWER.exec(line)![5], `${criterion}: not logged in, the answer is not No`).toBe('No')
    return line
  }

  // Its place among this battle's prompts: the asking lines since the last battle over.
  let nth = 0
  let asks = 0
  for (const l of backendLog()) {
    if (OVER.test(l)) nth = 0
    if (ASKING.test(l)) [nth, asks] = [nth + 1, asks + 1]
    if (asks === n) break
  }
  // The prompt's message: newer than the Run click (an earlier run's missed prompt keeps its buttons), with its title and
  // the buttons still on it (an answered one has none). The bot logs the asking line before it posts the message.
  const channel = newMoveChannel()
  const { discordToken } = settings()
  const title = `${name} - New Move`
  let message: DiscordMessage | undefined
  const deadline = Date.now() + MINUTE
  while (!message) {
    if (Date.now() > deadline) throw new Error(`${criterion}: no "${title}" message with its buttons in the new-move channel within 60 s`)
    const messages = (await discordGet(discordToken, `channels/${channel}/messages?limit=10`)) as DiscordMessage[]
    message = messages.find((m) => Date.parse(m.timestamp) > Date.parse(run.runStart) && m.embeds?.[0]?.title === title &&
      (m.components ?? []).some((r) => r.components.some((b) => b.custom_id === 'do_not_learn')))
    if (!message) await sleep(2000)
  }
  const buttons = message.components!.flatMap((r) => r.components)
  const id = expected.forget === null ? 'do_not_learn' : `move_${expected.forget}`
  const label = buttons.find((b) => b.custom_id === id)?.label
  expect(label, `${criterion}: the message has no ${id} button: ${JSON.stringify(buttons.map((b) => b.label))}`).toBeDefined()
  step(`OPERATOR: in Discord's new-move channel, on the ${NTH[nth - 1]} "${title}" message of this battle (learning ${move}), ` +
    `click "${label}", then "Confirm Choice". Within 10 minutes; a missed or wrong click is a rerun.`)
  const line = await waitLine(ANSWER, n, 10 * MINUTE, `${criterion}: no click within 10 minutes; rerun`)
  const answer = ANSWER.exec(line)!
  const clicked = answer[5] === 'No' ? null : Number(answer[6])
  if (clicked !== expected.forget) {
    throw new Error(`${criterion}: the operator clicked ${clicked === null ? 'Do Not Learn' : `slot ${clicked}`}, not "${label}"; rerun`)
  }
  // The bot edits the message after it has its answer, so the read waits up to 60 s for the edit.
  const description = expected.forget === null ? `${name} did not learn ${move}!` : `${name} forgot ${label}, and learned ${move}!`
  let read = ''
  const readDeadline = Date.now() + MINUTE
  while (read !== description && Date.now() < readDeadline) {
    read = ((await discordGet(discordToken, `channels/${channel}/messages/${message.id}`)) as DiscordMessage).embeds?.[0]?.description ?? ''
    if (read !== description) await sleep(2000)
  }
  step(`Discord: the "${title}" message reads "${read}"`)
  expect(read, `${criterion}: the edited message`).toBe(description)
  return line
}

// --- the training form and frame ------------------------------------------------------------------------------------

/**
 * The form after Run, and the frame's expectations: `discord` for a scenario that needs an operator's Yes (it fails at once
 * with no login), `expectedStop` for the one stop it waits for.
 */
export type TrainForm = { trainType: 'Standard' | 'Switch'; targetLevel: number; switchSlot?: number; discord?: boolean; expectedStop?: RegExp }

/**
 * A refused Run: the dashboard shows startAction's 400 text in the card's red feedback box (BotControlTab's
 * renderFeedback) and leaves the form up.
 */
export const refusal = (card: Locator): Locator => card.locator('div[class~="bg-red-900/20"]')

/**
 * startWild with Spin (the player never leaves the start tile, so the walk home at Complete is `already at start`), then
 * NoTarget (with ShinyTarget the milestone never completes), the train type, the target level and the battler's slot,
 * read after the party is reloaded from the game. A refusal fails at once with its sentence, instead of startWild's 30 s
 * wait for the form to go. Returns the ISO time of the Run click.
 */
export async function startTraining(card: Locator, form: TrainForm): Promise<string> {
  // Set once the race below is settled; read only by `refused`'s loop, on the one test thread.
  let settled = false
  const refused = async (): Promise<never> => {
    while (!settled) {
      if ((await refusal(card).count()) > 0) throw new Error(`the Run was refused: "${(await refusal(card).innerText()).trim()}"`)
      await sleep(250)
    }
    return new Promise<never>(() => undefined)
  }
  const start = startWild(card, { mode: 'Spin' }, async () => {
    await card.getByTestId('param-TargetType').locator('select').selectOption('NoTarget')
    await card.getByTestId('param-TrainType').locator('select').selectOption(form.trainType)
    await card.getByTestId('param-TargetLevel').locator('input').fill(String(form.targetLevel))
    if (form.switchSlot !== undefined) {
      const field = card.getByTestId('param-SwitchSlot')
      await field.getByTitle('Reload party from the running game').click()
      await field.locator('select').selectOption(String(form.switchSlot))
    }
    step(`training form: ${JSON.stringify({ ...form, expectedStop: form.expectedStop?.source })}`)
  })
  try {
    return await Promise.race([start, refused()])
  } finally {
    settled = true
  }
}

/** QA_RS_ROM: SA-Wild by default, or RU-Wild. */
export function rsRom(): string {
  const rom = process.env.QA_RS_ROM ?? 'SA-Wild'
  if (!(rom in ROMS)) throw new Error(`QA_RS_ROM must be one of ${Object.keys(ROMS).join(', ')}, not ${rom}`)
  return rom
}

/** QA_RS_VARIANT, one of the spec's own values; the first is the default. */
export function variant<T extends string>(allowed: readonly T[]): T {
  const value = process.env.QA_RS_VARIANT ?? allowed[0]
  if (!allowed.includes(value as T)) throw new Error(`QA_RS_VARIANT must be one of ${allowed.join(', ')}, not ${value}`)
  return value as T
}

export type Run = { rom: string; card: Locator; pages: Record<string, Page>; runStart: string; lead: Mon; loggedIn: boolean }
export type IdleRead = { lock: number; battleBit: number; boxCounter: number; tile: Tile; party: Mon[] }
export type TrainingResult = { runStart: string; lead: Mon; idle: IdleRead; log: string[] }

// The level-up box counter (research, "The level-up box counter"): a stale 3 stays in the free field after the last box.
const BOX_COUNTER = 0x0201609c

/** With the event over and the bot idle: the lock, the battle bit, the box counter, the tile and the party. */
export async function idleRead(): Promise<IdleRead> {
  const read = { lock: await readU8(LOCK), battleBit: await readBattleBit(), boxCounter: await readU8(BOX_COUNTER), tile: await readTile(), party: await readParty() }
  step(`with the bot idle: ${JSON.stringify(read)}`)
  return read
}

/**
 * The training frame. The pristine save and no cheat; the backend, the dashboard and Discord's login; the launch on
 * QA_RS_ROM and the lead decoded (checksum valid, Mudkip), so the codec is proved on real bytes before any write; `setup`
 * (the writes) with the bot idle; startTraining; then `body`. On 'complete' it waits for Complete, reads with the bot
 * idle and removes the bot; on 'cancel' the event is cancelled and the read is made between the cancels. Then the flash
 * unchanged, the shutdown, no stop but `expectedStop`, every battle's press count, and one encounters row per encounter
 * line, none shiny. The teardown runs on every outcome. The result is for the scenario's asserts. Another family changes
 * `save` (installed and compared instead of qa-route101), `start` (instead of startTraining with the form) and `whileIdle`
 * (a read after idleRead, with the bot still idle).
 */
export async function trainingRun(setup: () => Promise<void>, form: TrainForm, body: (run: Run) => Promise<'complete' | 'cancel'>,
  { save = ROUTE101, start = (card: Locator) => startTraining(card, form), whileIdle }:
  { save?: Save; start?: (card: Locator) => Promise<string>; whileIdle?: () => Promise<void> } = {}): Promise<TrainingResult> {
  const rom = rsRom()
  const frame: Frame = { backend: null, app: null }
  const { pg } = settings()
  removeCheats()
  installPristine(rom, save)
  try {
    const admin = await startFrame(frame)
    const loggedIn = await discordMode()
    step(`Discord ${loggedIn ? 'logged in' : 'not logged in: every move prompt is declined at once'}`)
    if (form.discord) expect(loggedIn, 'this scenario needs an operator to click a Yes in Discord, and Discord is not logged in').toBe(true)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, rom, 'emulator')
    const lead = await readSlot(1)
    step(`lead: ${JSON.stringify(lead)}`)
    expect(lead.checksumOk && lead.species === MUDKIP, `the lead does not decode as Mudkip (${MUDKIP}) with a valid checksum`).toBe(true)
    await shotAll('01-launched', pages)
    await setup()
    const runStart = await start(card)
    const end = await body({ rom, card, pages, runStart, lead, loggedIn })
    let idle: IdleRead | null = null
    if (end === 'complete') {
      await waitComplete(form.expectedStop, 10 * MINUTE)
      idle = await idleRead()
      await whileIdle?.()
      await cancelBot(card)
    } else {
      await cancelEventAndBot(card, async () => {
        idle = await idleRead()
        await whileIdle?.()
      })
    }
    await shotAll('02-cancelled', pages)
    expect(flashUnchanged(rom, save), `"the save's first 131,072 bytes unchanged": the ${rom} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    const log = backendLog()
    failOnStops(log, form.expectedStop)
    checkPresses(log)
    const pids = log.filter((l) => ENCOUNTER.test(l)).map((l) => parseEncounter(l).pid)
    const rows = psqlRows(pg, runStart, rom)
    expect(rows.map((r) => hex8(Number(r[0]))), '"one encounters row per encounter line": the rows\' PIDs').toEqual(pids)
    expect(rows.filter((r) => r[3] !== 'f').map((r) => r[0]), '"none shiny": shiny encounters rows').toEqual([])
    return { runStart, lead, idle: idle!, log }
  } finally {
    await teardown(frame, rom)
  }
}

// --- the scenario's own walk: with the bot idle, wild encounters kept off by the Repel variable ------------------------

// gSaveBlock1's variables and flags (research, "Flags, variables and trainers in memory").
const VARS = 0x02026a74
const FLAGS = 0x02026954
// VAR_REPEL_STEP_COUNT, 0x4021 (pret include/constants/vars.h:48): 0x02026AB6. Decomp only: a battle during a walk fails it.
const REPEL_STEPS =VARS + (0x4021 - 0x4000) * 2

/** VAR_REPEL_STEP_COUNT: 250 before a scenario walk, 0 before Run. */
export async function repel(steps: number): Promise<void> {
  const bytes = Buffer.alloc(2)
  bytes.writeUInt16LE(steps)
  await writeLogged(`VAR_REPEL_STEP_COUNT ${steps}`, REPEL_STEPS, bytes)
}

/** Sets or clears event flag `flag` (a trainer's is 0x500 + its id: Calvin's 1598 is bit 6 of 0x02026A1B). */
export async function setTrainerFlag(flag: number, on: boolean): Promise<void> {
  const addr = FLAGS + (flag >> 3)
  const old = await readU8(addr)
  const bit = 1 << (flag & 7)
  await writeLogged(`flag ${flag} ${on ? 'set' : 'cleared'}`, addr, Buffer.from([on ? old | bit : old & ~bit]))
}

/** One segment as the navigator walks it: the macro and the tile it ends on, `RouteSearch`'s segments printed. */
export type Segment = Tile & { macro: string }

// gMain.callback1 (research, the gMain row of "Addresses from pret's symbol tables"): 0 with the lock 0 for 4 to 7 frames
// after every warp, which is not the free field (research W1, W2).
const CALLBACK1 = 0x03001770
// gPlayerAvatar.tileTransitionState (gPlayerAvatar + 3): 1 through a step, 2 for a frame, 0 at rest (research W9).
const TILE_TRANSITION = 0x0202e85b

/**
 * Each segment's macro, then the tile, the lock, callback 1, the tile transition and the battle bit every 250 ms until the
 * player is free on the segment's tile with the step ended (lock 0, callback 1 not 0, tile transition 0) on two reads in a
 * row: the reads are separate requests, so one read can mix frames from either side of a warp's 4 to 7 frames of
 * callback 1 0, and two reads 250 ms apart cannot both fall in them. The game's locked arrival step after a door (36 to 43
 * frames) is waited out the same way. 20 s without fails naming the last read, and a battle bit fails naming it. Then the
 * macro is cleared, as the navigator clears it, which lets go of the last segment's held B.
 */
export async function walkTo(segments: Segment[]): Promise<void> {
  for (const { macro, group, num, x, y } of segments) {
    const target = { group, num, x, y }
    await runMacro(macro)
    const deadline = Date.now() + 20_000
    let freeBefore = false
    while (true) {
      const tile = await readTile()
      const lock = await readU8(LOCK)
      const callback1 = await readU32(CALLBACK1)
      const transition = await readU8(TILE_TRANSITION)
      if ((await readBattleBit()) === 1) throw new Error(`the scenario's walk: battle bit 1 on ${JSON.stringify(tile)} after ${macro}`)
      const free = lock === 0 && callback1 !== 0 && transition === 0 && tile.group === group && tile.num === num && tile.x === x && tile.y === y
      if (free && freeBefore) break
      freeBefore = free
      if (Date.now() > deadline) {
        throw new Error(`the scenario's walk: ${macro} not free on ${JSON.stringify(target)} with the step ended (lock 0, callback 1 not 0, ` +
          `tile transition 0, two reads in a row) in 20 s; last read ${JSON.stringify(tile)}, lock ${lock}, callback 1 0x${hex8(callback1)}, tile transition ${transition}`)
      }
      await sleep(250)
    }
    step(`walk: ${macro} on ${JSON.stringify(target)}`)
  }
  await clearMacro()
  step('walk: macro cleared')
}
