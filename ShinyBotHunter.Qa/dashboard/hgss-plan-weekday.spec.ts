// hgss-plan-weekday (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1b, plan scenario 13): C3b's midnight check.
// Plan QA Friday: a default, and a rule for Friday at any time; started at 23:50 on a Thursday. Before midnight the rule does
// not match and the default is hunted; after midnight the weekday and the hour roll together (research R2) and the rule
// matches. One launch shows a weekday rule matching and not matching.
// `hg` (the default): HeartGold from the save's tile on Route 32, (474, 294): default Route 32 · grass, Friday -> Route 30 · grass,
// goal Wooper 1 (Route 32 at night) and Spinarak 1 (Route 30 at night). Route 32's anchor is set on the save's tile, so
// Thursday hunts there with no walk: the walk south to its generated anchor meets a story trigger.
// `ss`: SoulSilver from the qa-ss-route30 save's tile on Route 30, (556, 374): the two milestones swapped, so the default is the
// route the save stands on (default Route 30 · grass, Friday -> Route 32 · grass), goal Hoothoot 1 (SS Route 30 at night, not
// Route 32) and Wooper 1 (SS Route 32 at night, not Route 30). Without the save it fails at once naming the missing file.
// `ss` also checks the trainer stop (#604): on the Thursday walk Youngster Joey, unbeaten on the save, challenges on the step
// up to (552, 332) (docs/research/milestone-builder.md, "HeartGold and SoulSilver: a trainer's challenge during a navigator
// walk", R1). The bot logs `[Navigator] stopped: A trainer battle started on R30.`, sends that sentence once, parks in
// Waiting with only Resume, presses nothing, and the speech stays at its arrow; Resume during the speech parks again with no
// second message. The scenario then presses through the speech and wins, each press on the state it changes (R5), presses
// Resume, and the walk is planned again from the stop's tile to the anchor. `ss` starts at 23:45 to pay for the stop's minutes.
// Before Run every other trainer is marked beaten by a RAM write, so Joey is the one trainer left unbeaten on the save: the
// walks also cross Youngster Mikey's and Bug Catcher Don's sight, and their two-Pokémon battles have no researched gates.
// `ss` ends at midnight's Friday walk line, cancelled once the overworld is ready: on this save the Friday walk passes Lyra's
// one-time gatehouse scene (map script 1 on map 97), which the bot does not handle, so the Friday half has no SoulSilver run.
// Run: `npx playwright test hgss-plan-weekday`; `$env:QA_HGSS_VARIANT = "ss"` for SoulSilver.
import * as fs from 'fs'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { emuHawkRunning, readMemory, readU32, runMacro, shotAll, step } from './driver'
import {
  assertLocalStores, cancelEventAndBot, clockConfig, DROPPED, launchHgss, PLAN_NOW, PLAN_STOP, PLAN_WALK, putGoal, putPlan, readClock,
  rowsFor, RUN_FROM, saveOf, seen, setAnchorHere, shutdown, stageCopy, startFrame, startWild, teardown, variant, writeLeadExtension,
  type Clock, type Frame, type Plan
} from './milestones'
import { DISCORD_READY, discordMessages, RESUME, writeLogged } from './rs-training'
import { dashboardState } from './rs-wild'
import { API, backendLog, hex8, MINUTE, NAV_PRESS, NAV_YES, readTile } from './static'

const which = variant(['hg', 'ss'] as const)
const R32 = { id: '36-land', name: 'Route 32 · grass', at: ['36', '458', '339'], macro: /^M\.Up\.332\.\d+(:\S+)?;M\.Down\.346\.\d+$/ }
const R30 = { id: '34-land', name: 'Route 30 · grass', at: ['34', '547', '304'], macro: /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/ }
// Route 32's anchor as the hg variant sets it on the save's tile.
const R32_HERE = { ...R32, at: ['36', '474', '294'], macro: /^M\.Up\.293\.\d+(:\S+)?;M\.Down\.295\.\d+$/ }
type Stop = typeof R32
// Each version's night tables: HG Route 32 Wooper, Rattata, Bellsprout, Mareep, Zubat; HG Route 30 Spinarak, Rattata,
// Hoothoot; SS Route 30 Hoothoot, Rattata; SS Route 32 Wooper, Ekans, Bellsprout, Mareep, Zubat.
const CASES: Record<typeof which, { handle: string; rom: string; save: string; before: Stop; after: Stop; goal: number[]; beforeNight: string[]; afterNight: string[] }> = {
  hg: {
    handle: 'HG-Weekday', rom: 'HG-Route32.nds', save: 'hgss.qa-route32', before: R32_HERE, after: R30, goal: [194, 167],
    beforeNight: ['194', '19', '69', '179', '41'], afterNight: ['167', '19', '163']
  },
  ss: {
    handle: 'SS-Weekday', rom: 'Pokemon SoulSilver.nds', save: 'hgss.qa-ss-route30', before: R30, after: R32, goal: [163, 194],
    beforeNight: ['163', '19'], afterNight: ['194', '23', '69', '179', '41']
  }
}
const run = CASES[which]
const PLAN: Plan = { name: 'QA Friday', default: run.before.id, rules: [{ times: [], weekdays: ['Friday'], milestone: run.after.id }] }
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const BATTLE = /, isShiny: (True|False)\s*$/
const FIELD_READY = /\[GameState\] the overworld ready: GameSnapshot \{ Screen = FieldFree/

const hhmm = (c: Clock): string => `${['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][c.weekday]} ${c.hour}:${String(c.minute).padStart(2, '0')}`
// The walk line's weekday, hour, milestone, the map and x it walks from (a run line's y varies), and the destination.
function walkOf(line: string): string[] {
  const m = PLAN_WALK.exec(line)!
  return [m[1], m[2], m[3], m[4], m[5], m[7], m[8], m[9]]
}

// --- ss: Youngster Joey on Route 30 (#604; research "HeartGold and SoulSilver: a trainer's challenge during a navigator walk") ---

const SENTENCE = 'A trainer battle started on R30.'
// NavigatorEvent.StopForTrainerAsync, and its failed send; ReturnToStartAsync's plan line.
const TRAINER_STOP = /\[Navigator\] stopped: A trainer battle started on (\w+)\. \((\d+), (\d+)\), (.*)$/
const STOP_NOT_SENT = /\[Navigator\] The trainer stop's message was not sent: /
const RETURNING = /\[Navigator\] Returning to start map=(\d+) \((\d+),(\d+)\) from map=(\d+) \((\d+),(\d+)\)\s*$/
// R1: the challenge starts on the step from (552, 333) up to (552, 332); the tile read at the stop may still be the first.
const STOP_TILES = ['552,332', '552,333']
const JOEY_TILE = { map: 34, x: 552, y: 332 }
// OrchestratorEvent.ResumeAsync's park when the field is not free within 10 s.
const STAYED = /^Waited for the free field after the trainer stop; the game state stayed /
// Every line the HGSS bot logs for a press or a macro it sends: a navigator segment, a script press, a sign, the Repel
// message, a Run or Spin seek, a battle or level-up press, and a battle the orchestrator takes on.
const ACTS = [
  /\[Navigator\] (Segment \d+:|Macro:|Sign:|Healing)/, NAV_PRESS, NAV_YES, /\[Repel\]/, RUN_FROM, /\[Spin\] facing=/, /\[Battle\]/,
  /\[LevelUp\]/, /\[Orchestrator\] State: BattleStart/
]
const acts = (lines: string[]): string[] => lines.filter((l) => ACTS.some((p) => p.test(l)))
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// The presses R5 played: A at a text wait, Fight on the battle menu with no printer, then Quick Attack, the fourth move (Scratch, the first,
// has 0 PP on this save). `W.10` before a touch: the battle menus take input only a few frames after they are drawn.
const A = 'H.A.3;W.20'
const FIGHT = 'W.10;S.70.60.3'
const QUICK_ATTACK = 'W.10;S.170.110.3'

// The game state, from one ApiHawk chain read (cmd 0x05), so every value is from one emulated frame: a port of the parts of
// HgssFieldSystemStateReader.Classify these screens use, with HgssPointerService's offsets, as the research's probe ported it.
const ANCHOR = 0x021d4158
const PRINTERS = 0x021d1f74
const RUN_SCRIPTS = 0x0203ff45
const ENV_MAGIC = 222271
const TRAINER_APPROACH = 3739
const PRINTING = 0x021ef349
const MENU_CHOICE = 0x020477c1
const WAIT_BUTTON = 0x02041075
const BLOCKS: [number, number[], number][] = [
  [ANCHOR, [0], 0x70], // 0 the FieldSystem: the task manager at 0x10, ready at 0x6C
  [ANCHOR, [0, 0], 0x0c], // 1 its first sub-structure: the open app at 4
  [ANCHOR, [0x10, 0], 0x08], // 2 the field task: its function at 4
  [ANCHOR, [0, 4, 0], 0x20], // 3 the app: overlay at 0x0C (12 is the battle), state at 0x14
  [ANCHOR, [0, 4, 0x1c, 0x30, 0], 0x0c], // 4 the battle context: selection at 0, command at 8
  [ANCHOR, [0, 4, 0x1c, 0x2420], 1], // 5 the battle outcome
  [ANCHOR, [0x10, 0x0c, 0], 0x0c], // 6 the script environment: magic at 0, printer slot at 5, script at 0x0A
  [ANCHOR, [0x10, 0x0c, 0x38, 0], 8], // 7 to 9 the script contexts: mode at 1, native at 4
  [ANCHOR, [0x10, 0x0c, 0x3c, 0], 8],
  [ANCHOR, [0x10, 0x0c, 0x40, 0], 8],
  [PRINTERS, [0x10, 0], 0x29] // 10 text printer 0: cursor at 0, state at 0x28
]
type Block = { status: number; bytes: Buffer }
type Read = {
  screen: string; fn?: string; script?: number; native?: string; printer?: number; cursor?: string; app?: number
  command?: number; sel?: number; outcome?: number
}

async function chain(): Promise<Block[]> {
  const parts = [Buffer.from([5, 0, 0, 0, 0, 0, 0, 0, 0, BLOCKS.length])]
  for (const [addr, hops, length] of BLOCKS) {
    const b = Buffer.alloc(4 + 1 + 4 * hops.length + 4)
    b.writeUInt32LE(addr, 0)
    b[4] = hops.length
    hops.forEach((h, i) => b.writeInt32LE(h, 5 + 4 * i))
    b.writeUInt32LE(length, 5 + 4 * hops.length)
    parts.push(b)
  }
  const res = Buffer.from(await (await fetch('http://127.0.0.1:20001/', { method: 'POST', body: Buffer.concat(parts) })).arrayBuffer())
  const out: Block[] = []
  let at = 0
  for (let i = 0; i < BLOCKS.length; i++) {
    const length = res.readInt32LE(at + 2)
    out.push({ status: res[at], bytes: res.subarray(at + 6, at + 6 + length) })
    at += 6 + length
  }
  return out
}

async function snapshot(): Promise<Read> {
  const r = await chain()
  const ok = (i: number): boolean => r[i].status === 0
  const moving: Read = { screen: 'Transitioning' }
  if (!ok(0) || !ok(1)) return moving
  const fsys = r[0].bytes
  const sub0 = r[1].bytes
  if (sub0.readUInt32LE(4) === 0) {
    const taskman = fsys.readUInt32LE(0x10)
    const ready = fsys.readUInt32LE(0x6c) === 1
    if (ready && taskman === 0) return sub0.readUInt32LE(8) === 0 ? { screen: 'FieldFree' } : moving
    if (!ready || !ok(2)) return moving
    const fn = r[2].bytes.readUInt32LE(4)
    if (fn !== RUN_SCRIPTS) return { screen: 'FieldTask', fn: hex8(fn) }
    const env = r[6].bytes
    if (!ok(6) || env.readUInt32LE(0) !== ENV_MAGIC) return moving
    const read: Read = { screen: 'FieldTask', fn: hex8(fn), script: env.readUInt16LE(0x0a) }
    // The innermost running context: its native while it waits in one (mode 2).
    for (const i of [2, 1, 0]) {
      const c = r[7 + i]
      if (c.status === 1) continue
      if (c.status !== 0) return moving
      if (c.bytes[1] === 2) read.native = hex8(c.bytes.readUInt32LE(4))
      break
    }
    if (env[5] === 0 && r[10].status !== 1) {
      if (!ok(10)) return moving
      read.printer = r[10].bytes[0x28]
      read.cursor = hex8(r[10].bytes.readUInt32LE(0))
    }
    return read
  }
  if (!ok(3)) return moving
  const app = r[3].bytes
  if (app.readUInt32LE(0x0c) !== 12) return { screen: 'OtherApp' }
  const state = app.readUInt32LE(0x14)
  if (state < 9) return { screen: 'BattleStarting', app: state }
  const over = (): Read => ({ screen: 'BattleOver', app: state, ...(ok(5) ? { outcome: r[5].bytes[0] } : {}) })
  if (state > 9) return over()
  if (!ok(4)) return moving
  const ctx = r[4].bytes
  const command = ctx.readUInt32LE(8)
  if (command === 45) return over()
  if (command === 22) return { screen: 'BattleScript', command }
  if (command === 5 && ctx[0] === 1) {
    // "What will X do?" is printer 0 while it is up; the menu takes the touch once it is gone (no printer).
    if (r[10].status === 1) return { screen: 'BattleMenu', command, sel: 1 }
    return ok(10) ? { screen: 'BattleMenu', command, sel: 1, printer: r[10].bytes[0x28] } : moving
  }
  if (command === 5 && ctx[0] === 4) return { screen: 'BattleMoveList', command, sel: 4 }
  return { screen: 'BattleTurn', command, sel: ctx[0] }
}

const show = (r: Read): string => JSON.stringify(r)
const inApproach = (r: Read): boolean => r.screen === 'FieldTask' && r.fn === hex8(RUN_SCRIPTS) && r.script === TRAINER_APPROACH
// A box waiting at its arrow (printer state 2); R5 names the native only for the speech's, the printing native.
const atArrow = (r: Read): boolean => inApproach(r) && r.printer === 2

// Every other trainer is marked beaten before Run, so Joey is the one trainer the walks can meet: Youngster Mikey (552, 325)
// and Bug Catcher Don (550, 298) stopped the walks of runs 20261126-080735 and -081249, and their battles (two Pokémon) have
// no researched gates. Trainer flags are FLAG_TRAINER(id) = TRAINER_FLAG_BASE 0x550 + id (pokeheartgold
// include/constants/flags.h), ids from 1 (TRAINER_RIVAL_SILVER, include/constants/trainers.h); the next flag flags.h
// defines is FLAG_UNK_82E = 0x550 + 734, so the range ends at id 733 (ids 734 to 737, Clair 3 and the partner rivals, would
// share its bits). They live in the save's section 4 after its 0x170 u16 vars (research R5).
const JOEY_FLAG = 0x550 + 8
const FIRST_TRAINER_FLAG = 0x550 + 1
const LAST_TRAINER_FLAG = 0x550 + 733

/** The byte holding flag `flag`: [[FieldSystem] + 0x0C] is the save, its section table at + 0x23014. */
async function flagByte(flag: number): Promise<number> {
  const save = await readU32((await readU32(ANCHOR)) + 0x0c)
  const entry = await readMemory(save + 0x23014 + 4 * 16, 16)
  expect(entry.readUInt32LE(0), 'the save\'s section table: entry 4').toBe(4)
  return save + 0x10 + entry.readUInt32LE(8) + 0x170 * 2 + (flag >> 3)
}

/**
 * With the bot idle: every trainer flag in the range set except Joey's, bit by bit, so the range's edge bytes keep their
 * other flags; the whole range read back 3 s later must equal what was written, with Joey's bit clear.
 */
async function beatEveryTrainerButJoey(): Promise<void> {
  const addr = await flagByte(FIRST_TRAINER_FLAG)
  const at = (flag: number): number => (flag >> 3) - (FIRST_TRAINER_FLAG >> 3)
  const joeyBit = 1 << (JOEY_FLAG & 7)
  const old = await readMemory(addr, at(LAST_TRAINER_FLAG) + 1)
  expect(old[at(JOEY_FLAG)] & joeyBit, 'Youngster Joey is beaten on the save').toBe(0)
  const want = Buffer.from(old)
  for (let flag = FIRST_TRAINER_FLAG; flag <= LAST_TRAINER_FLAG; flag++) {
    if (flag !== JOEY_FLAG) want[at(flag)] |= 1 << (flag & 7)
  }
  await writeLogged(`trainer flags 0x${FIRST_TRAINER_FLAG.toString(16)} to 0x${LAST_TRAINER_FLAG.toString(16)} set but Joey's`, addr, want)
  await sleep(3000)
  const after = await readMemory(addr, want.length)
  expect(after.toString('hex'), 'the trainer flag bytes 3 s after the write').toBe(want.toString('hex'))
  expect(after[at(JOEY_FLAG)] & joeyBit, 'Youngster Joey\'s flag after the write').toBe(0)
  step('every trainer but Youngster Joey marked beaten')
}

/** Polls the game state every 100 ms until `done` holds; fails after `ms` naming the last state. */
async function untilState(done: (r: Read) => boolean, ms: number, what: string): Promise<Read> {
  const deadline = Date.now() + ms
  while (true) {
    const r = await snapshot()
    if (done(r)) return r
    if (Date.now() > deadline) throw new Error(`${what}: not within ${ms / 1000} s; the game state stayed ${show(r)}`)
    await sleep(100)
  }
}

/** Exactly one message with the sentence since the Run click: Discord's channel when logged in, else the dropped lines. */
async function stopMessages(sinceIso: string): Promise<string[]> {
  const log = backendLog()
  const notSent = log.find((l) => STOP_NOT_SENT.test(l))
  if (notSent) throw new Error(`the trainer stop's message was not sent: ${notSent}`)
  if (log.some((l) => DISCORD_READY.test(l))) return discordMessages(sinceIso, SENTENCE)
  const dropped = log.flatMap((l) => DROPPED.exec(l)?.[1] ?? []).filter((m) => m.includes(SENTENCE))
  step(`Discord not logged in: ${dropped.length} dropped trainer messages: ${JSON.stringify(dropped)}`)
  return dropped
}
async function oneMessage(sinceIso: string, criterion: string): Promise<void> {
  await expect.poll(() => stopMessages(sinceIso), { message: `${criterion}: messages "${SENTENCE}" since the Run click`, timeout: 30_000, intervals: [3000] })
    .toHaveLength(1)
}

/** Nothing pressed or sent by the bot after line `at` of the log. */
function nothingSince(at: number, criterion: string): void {
  expect(acts(backendLog().slice(at + 1)), `"no press line from the stop on": ${criterion}`).toEqual([])
}

/** Waiting, and the card offers Resume and nothing else: its one form's Run beside Cancel. */
async function waitingWithResume(card: Locator, criterion: string): Promise<void> {
  await expect.poll(async () => (await dashboardState())?.eventState, { message: `${criterion}: the event is not Waiting`, timeout: MINUTE, intervals: [1000] })
    .toBe('Waiting')
  await expect(card.getByText('Resume', { exact: true }), `${criterion}: no Resume on the card`).toBeVisible({ timeout: MINUTE })
  expect((await card.getByRole('button').allInnerTexts()).map((t) => t.trim()), `${criterion}: the card's buttons`).toEqual(['Cancel', 'Run'])
}

/** The speech at its first arrow with the field never free, then the same state on every read for 20 s (R3). */
async function speechHolds(criterion: string): Promise<Read> {
  const arrow = await untilState((r) => {
    if (!inApproach(r)) throw new Error(`${criterion}: the speech left script ${TRAINER_APPROACH} before its arrow: ${show(r)}`)
    return atArrow(r)
  }, 10_000, `${criterion}: the speech's arrow`)
  const tile = await readTile()
  step(`${criterion}: the speech at its arrow ${show(arrow)}, tile ${JSON.stringify(tile)}`)
  expect(arrow.native, `${criterion}: the speech's arrow waits in the printing native (R3)`).toBe(hex8(PRINTING))
  expect({ map: tile.map, x: tile.x, y: tile.y }, `${criterion}: the player's tile at the speech (R1)`).toEqual(JOEY_TILE)
  const until = Date.now() + 20_000
  while (Date.now() < until) {
    await sleep(1000)
    expect(show(await snapshot()), `${criterion}: the speech moved within 20 s`).toBe(show(arrow))
  }
  step(`${criterion}: the speech unchanged for 20 s`)
  return arrow
}

/**
 * The operator's part, played by the scenario while the bot is Waiting (R5): A at each text wait of script 3739 (its arrow,
 * the Yes/No and the button wait after a first win), Fight at the battle menu, Quick Attack at the move list; each press sent
 * once on its gate and confirmed by the game state changing from it within 5 s, never by time. Returns the presses' names.
 */
async function winTheBattle(criterion: string): Promise<string[]> {
  const presses: string[] = []
  const deadline = Date.now() + 3 * MINUTE
  while (true) {
    const r = await snapshot()
    if (r.screen === 'FieldFree') break
    if (Date.now() > deadline) throw new Error(`${criterion}: not in the free field within 3 minutes; the game state ${show(r)}`)
    // Fight as the bot's SelectMove presses it: on the battle menu once its message's printer is gone.
    const gate = r.screen === 'BattleMenu' && r.printer === undefined ? { name: 'Fight', macro: FIGHT }
      : r.screen === 'BattleMoveList' ? { name: 'Quick Attack', macro: QUICK_ATTACK }
      : atArrow(r) ? { name: 'arrow', macro: A }
      : inApproach(r) && r.native === hex8(MENU_CHOICE) ? { name: 'Yes/No', macro: A }
      : inApproach(r) && r.native === hex8(WAIT_BUTTON) ? { name: 'button', macro: A }
      : null
    if (gate === null) {
      await sleep(100)
      continue
    }
    presses.push(gate.name)
    // A touch on a battle menu drawn a few frames ago can be dropped (seen on the first two runs): as the bot's SelectMove
    // does, it is sent again only while the menu still reads unchanged 1 s later, which proves it was dropped; 3 at most.
    const touch = gate.macro !== A
    for (let attempt = 1; ; attempt++) {
      step(`scenario press ${presses.length}${attempt > 1 ? ` again (${attempt})` : ''}: ${gate.macro} (${gate.name}) at ${show(r)}`)
      await runMacro(gate.macro)
      try {
        const after = await untilState((s) => show(s) !== show(r), touch ? 1000 : 5000, `${criterion}: ${gate.name} confirmed`)
        step(`  confirmed by ${show(after)}`)
        break
      } catch (e) {
        if (!touch || attempt === 3) throw e
      }
    }
  }
  const tile = await readTile()
  step(`${criterion}: ${presses.length} presses to the free field (${presses.join(', ')}), tile ${JSON.stringify(tile)}`)
  expect({ map: tile.map, x: tile.x, y: tile.y }, `${criterion}: the player after the win stands on the stop's tile (R5)`).toEqual(JOEY_TILE)
  return presses
}

/**
 * The Thursday walk meets Joey: the stop line, Waiting with only Resume, one message, the speech held with nothing pressed,
 * Resume during the speech parking again, the scenario's win, then the real Resume and the walk planned again from the
 * stop's tile to the anchor.
 */
async function meetJoey(card: Locator, pages: Record<string, Page>, runStart: string): Promise<void> {
  const criterion = '"the walk stops for the trainer"'
  const stopLine = (await seen(TRAINER_STOP, 1, 5 * MINUTE)).line
  const [, map, x, y] = TRAINER_STOP.exec(stopLine)!
  expect(map, `${criterion}: the sentence's map`).toBe('R30')
  expect(STOP_TILES, `${criterion}: the stop's tile (${x}, ${y})`).toContain(`${x},${y}`)
  const at = backendLog().indexOf(stopLine)
  await waitingWithResume(card, criterion)
  await oneMessage(runStart, '"one message"')
  await shotAll('02-stopped', pages)
  const arrow = await speechHolds(criterion)
  nothingSince(at, 'while Waiting at the stop')

  // Resume during the speech: within 20 s ResumeAsync's park, Waiting again, nothing pressed, no second message.
  const resumeCriterion = '"Resume during the speech parks again"'
  const resumes = backendLog().filter((l) => RESUME.test(l)).length
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  step('Resume during the speech')
  const parked = await seen(RESUME, resumes + 1, 20_000, { waiting: true })
  expect(RESUME.exec(parked.line)![1], `${resumeCriterion}: the Resume line`).toMatch(STAYED)
  await waitingWithResume(card, resumeCriterion)
  nothingSince(at, `${resumeCriterion}: after the Resume during the speech`)
  await oneMessage(runStart, `${resumeCriterion}: no second message`)
  expect(show(await snapshot()), `${resumeCriterion}: the speech after the park`).toBe(show(arrow))

  const presses = await winTheBattle('"the scenario presses through the speech and wins"')
  expect(presses.join(', '), '"the presses R5 named": two arrows, Fight and Quick Attack, then the first win\'s Yes/No and two boxes')
    .toMatch(/^arrow, arrow, (Fight, Quick Attack, )+Yes\/No, arrow, button$/)
  nothingSince(at, 'through the scenario\'s battle')
  await oneMessage(runStart, '"one message": after the battle')
  await shotAll('03-won', pages)

  // The real Resume: the leg planned again from where the player stands, to the anchor, with no new park.
  const returns = backendLog().filter((l) => RETURNING.test(l)).length
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  step('Resume after the win')
  const again = RETURNING.exec((await seen(RETURNING, returns + 1, MINUTE, { waiting: true })).line)!
  expect(again.slice(1), '"the walk is planned again from where the player stands"').toEqual([...run.before.at, '34', '552', '332'])
  expect(backendLog().filter((l) => RESUME.test(l)).length, '"Resume after the win": a second park').toBe(resumes + 1)
}

test(`hgss-plan-weekday ${which}: Thursday 23:50 to Friday, a weekday rule not matching, then matching after midnight`, async () => {
  test.setTimeout(40 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(run.handle, { rom: run.rom, save: run.save })
    clockConfig(which === 'ss' ? '2026-12-03T23:45:00' : '2026-12-03T23:50:00')
    const admin = await startFrame(frame)
    const pages = { admin }
    expect((await putGoal(run.handle, run.goal.map((species) => ({ species, amount: 1 })))).status, 'PUT goal').toBe(204)
    expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)

    const card = await launchHgss(admin, run.handle, { clock: true })
    const launchClock = await readClock()
    expect(launchClock.weekday === 4 && launchClock.hour === 23 && launchClock.minute <= 55,
      `${hhmm(launchClock)} at launch is not Thursday 23:55 or earlier; set QA_CLOCK_START earlier on that Thursday`).toBe(true)
    if (which === 'hg') {
      await writeLeadExtension({ hp: 30 })
      const set = await setAnchorHere(0, R32.id, 0, 1)
      expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    } else {
      // No QA record of this save's party: logged, not written.
      step(`SoulSilver party: ${await (await fetch(`${API}/bot/getParty/0`)).text()}`)
      await beatEveryTrainerButJoey()
    }
    await shotAll('01-launched', pages)

    const runStart = await startWild(card, { plan: PLAN.name, mode: 'Run', yRange: 1 })
    await seen(PLAN_STOP, 2, MINUTE)
    // Thursday: the rule does not match, so the default: on HeartGold hunted on its anchor where the bot stands, on SoulSilver
    // walked to from the save's tile, past Youngster Joey.
    let thursdayFrom = runStart
    if (which === 'ss') {
      expect(walkOf((await seen(PLAN_WALK, 1, 2 * MINUTE)).line), 'the Thursday walk')
        .toEqual(['Thursday', '23', run.before.name, '34', '556', ...run.before.at])
      await meetJoey(card, pages, runStart)
      thursdayFrom = new Date((await seen(ARRIVED, 1, 10 * MINUTE)).at).toISOString()
      expect(backendLog().filter((l) => TRAINER_STOP.test(l)), '"no second stop": the trainer stop lines').toHaveLength(1)
    }
    const walksThursday = which === 'ss' ? 1 : 0
    const from1 = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
    expect([from1[1], from1[2]], `the first Run starts on ${run.before.name}'s anchor`).toEqual(run.before.at.slice(1))
    expect(from1[3], `${run.before.name}'s run line`).toMatch(run.before.macro)

    // Midnight: Friday 0h names the rule's milestone, walked to under the default, switched to on arrival.
    const walk2At = await seen(PLAN_WALK, walksThursday + 1, 20 * MINUTE)
    expect(walkOf(walk2At.line), 'the Friday walk').toEqual(['Friday', '0', run.after.name, ...run.before.at.slice(0, 2), ...run.after.at])
    const thursday = rowsFor(run.handle, run.before.id, thursdayFrom).filter((r) => r.at < walk2At.at)
    expect(thursday.length, `no encounter at ${run.before.name}'s anchor before midnight (launched at ${hhmm(launchClock)}); set QA_CLOCK_START earlier`)
      .toBeGreaterThanOrEqual(1)
    for (const row of thursday) expect(run.beforeNight, `a ${row.species} at ${run.before.name}'s anchor on Thursday night`).toContain(row.species)
    const log = backendLog()
    const walk2Index = log.flatMap((l, i) => (PLAN_WALK.test(l) ? [i] : []))[walksThursday]
    expect(log.slice(0, walk2Index).filter((l) => (PLAN_WALK.test(l) || PLAN_NOW.test(l)) && l.includes(run.after.name)),
      `a plan line naming ${run.after.name} before midnight`).toEqual([])

    if (which === 'ss') {
      // The Friday walk would meet Lyra's gatehouse scene (map script 1 on map 97): cancelled at the walk's first overworld
      // ready, before its first segment's macro has gone far.
      await seen(FIELD_READY, log.slice(0, walk2Index).filter((l) => FIELD_READY.test(l)).length + 1, MINUTE)
      await cancelEventAndBot(card)
      expect(backendLog().slice(walk2Index).filter((l) => /\[Navigator\] Warp to map/.test(l)), 'a warp on the Friday walk before the cancel').toEqual([])
    } else {
      const now = await seen(PLAN_NOW, 1, 15 * MINUTE)
      expect(PLAN_NOW.exec(now.line)![1], 'the switch on arrival').toBe(run.after.name)
      const battles = backendLog().filter((l) => BATTLE.test(l)).length
      await seen(BATTLE, battles + 3, 10 * MINUTE)
      const after = backendLog()
      const runs = after.slice(after.findIndex((l) => PLAN_NOW.test(l))).filter((l) => RUN_FROM.test(l)).map((l) => RUN_FROM.exec(l)!)
      expect([runs[0][1], runs[0][2]], `the first Run after the switch starts on ${run.after.name}'s anchor`).toEqual(run.after.at.slice(1))
      for (const r of runs) expect(r[3], `a Run line not on ${run.after.name}'s line`).toMatch(run.after.macro)
      await shotAll('02-friday', pages)
      await cancelEventAndBot(card)
      const friday = rowsFor(run.handle, run.after.id, new Date(now.at).toISOString())
      expect(friday.length, `three rows under ${run.after.id} on Friday`).toBeGreaterThanOrEqual(3)
      for (const row of friday) expect(run.afterNight, `a ${row.species} at ${run.after.name}'s anchor on Friday night`).toContain(row.species)
    }

    expect(fs.readFileSync(saveOf(run.handle)).equals(fs.readFileSync(saveOf(run.handle) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
