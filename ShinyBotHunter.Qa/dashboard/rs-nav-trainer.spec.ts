// rs-nav-trainer (docs/specs/rs-navigation-and-training.md, QA table; FR-10, FR-19; #433's plan, part Q1f): the first
// Route 102 trainer, Calvin, unbeaten, met by the bot. The bot goes to Waiting on the tile where he sees the player, sends
// one Discord message, presses no A or B once the field locks and nothing after the stop; Resume during his speech parks
// it again; the scenario presses through the speech and wins the battle itself, then Resume, and the bot carries on.
// QA_RS_VARIANT picks how he is met:
// - run (the default): a flee-only Run hunt whose run line crosses his sight, so the seek's 4 s hand-off makes the trainer
//   read. Before it, part 1: a flee-only Spin on Route 101 with the lead at Lv 2 (the long battle transition) shows no
//   trainer read on a normal seek.
// - walk: the navigator's heal trip from Route 102 west of him to the nearest Pokémon Center, Oldale's, whose route
//   crosses his sight.
// The Spin half of the row cannot happen: none of his sight tiles is grass in the committed Data/RS, and a turn in place
// is not a step, so no Spin hunt can stand in his sight (#433's approval, answer 6).
import { expect, test, type Locator, type Page } from '@playwright/test'
import { runMacro, shotAll, step } from './driver'
import {
  ARRIVED, checkPresses, CLEARING, discordMessages, discordMode, failOnStops, HEAL_TRIP, LOCK_HANDED, NAV_HANDOFF, NURSE_DONE,
  NURSE_PRESS, PLAN, PRESS, readSlot, repel, RESUME, rsRom, SEGMENT, SEGMENT_ON, setTrainerFlag, SLOW_TRANSITION, TRAINER_STOP,
  trainingRun, variant, waitLine, walkTo, writeSlot, type Segment
} from './rs-training'
import {
  battles, BUTTON_WAITS, cancelEventAndBot, checkFlee, CONTROLLER, CURSOR, dashboardState, ENCOUNTER, flashUnchanged, installPristine,
  launchRs, LOCK, MENU_FN, OUTCOME, OVER, parseEncounter, pollWhileWaiting, PRESS_LINE, psqlRows, readBattleBit, readTile, readU16,
  readU32, readU8, removeCheats, RUN_FROM, seekOf, SPIN_LINE, startWild, teardown, WINDOW, type Tile
} from './rs-wild'
import { backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const VARIANT = variant(['run', 'walk'] as const)

// Calvin: trainer 318 on Route 102 (0, 17) at (33, 14), facing down, sight 3 (research, "A trainer's intro speech"). In
// the committed Data/RS his tile (33, 14) and (33, 18) are blocked and (33, 15) to (33, 17) free, none grass: his three
// sight tiles span the only corridor between Oldale and the west of the route. The stop's tile must be one of them.
const CALVIN = 318
const FLAG = 0x500 + CALVIN
const ROUTE102 = { group: 0, num: 17 }
const SIGHT: Tile[] = [15, 16, 17].map((y) => ({ ...ROUTE102, x: 33, y }))
const SENTENCE = 'A trainer battle started on Route102.'

// The start, west of him on row 15, and the scenario's walk to it from the save's tile, Route 101 (14, 4): RouteSearch's
// segments on the committed Data/RS, printed by a throwaway local test (not committed). The last crosses his sight at
// (33, 15), so the walk is made with his flag set.
const START: Tile = { ...ROUTE102, x: 31, y: 15 }
const START_TILE = 'Route102 (31, 15)'
const TO_START: Segment[] = [
  { macro: 'M.Left.9.33707826:H.B.1', group: 0, num: 16, x: 9, y: 4 },
  { macro: 'M.Up.0.33707828:H.B.1', group: 0, num: 16, x: 9, y: 0 },
  { macro: 'M.Up.11.33707828:H.B.1', group: 0, num: 10, x: 9, y: 11 },
  { macro: 'M.Left.0.33707826:H.B.1', group: 0, num: 10, x: 0, y: 11 },
  { macro: 'M.Left.36.33707826:H.B.1', ...ROUTE102, x: 36, y: 11 },
  { macro: 'M.Down.15.33707828:H.B.1', ...ROUTE102, x: 36, y: 15 },
  { macro: 'M.Left.31.33707826:H.B.1', ...START }
]
const CENTER = 'OldaleTown_PokemonCenter_1F (7, 4)'

// run: x range 2 from (31, 15), so the line is (29, 15) to (33, 15): grass on 29 to 31, his sight tile at 33. Its macro
// is RunEncounterEvent.RunMacro's, RunHold joined to the first leg.
const X_RANGE = 2
const RUN_MACRO = 'M.Right.33.33707826:H.B.1;M.Left.29.33707826'

// Research, "A trainer's intro speech", Addresses; per game the ROM values.
const OPPONENT = 0x0202ff5e
const FLAG_BYTE = 0x02026a1b // flag 1598: gSaveBlock1 + 0x1220 + (1598 >> 3), bit 6
const FIELD_WINDOW = 0x0202e892
const SCRIPT_STATUS = 0x030005b0
const SCRIPT_MODE = 0x030005b9
const SCRIPT_NATIVE = 0x030005bc
const SCRIPT_PTR = 0x030005c0
const DO_TRAINER_BATTLE: Record<string, number> = { 'SA-Wild': 0x0819f8a2, 'RU-Wild': 0x0819f912 }
const WAIT_FOR_A_OR_B: Record<string, number> = { 'SA-Wild': 0x080670e5, 'RU-Wild': 0x080670e1 }
// RsPointerService: HandleInputChooseMove, the move cursor and the level-up box counter.
const MOVE_FN = 0x0802c68d
const MOVE_CURSOR = 0x02024e64
const BOX_COUNTER = 0x0201609c

const GO_TO_CENTER = /\[Orchestrator\] State: GoToPokemonCenter\s*$/
const STAYED = /^Waited for the free field after the trainer stop; the game state stayed /
const OFF_LINE = /\[Run\] The player is off the run line\./
// Every line the bot logs for a press or a macro it sends: battle presses, the nurse's, a navigator segment, a seek.
const ACTS = [PRESS_LINE, PRESS, NURSE_PRESS, SEGMENT, RUN_FROM, SPIN_LINE]

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
const acts = (lines: string[]): string[] => lines.filter((l) => ACTS.some((p) => p.test(l)))
const tileOf = (m: RegExpExecArray, at: number): string => `${m[at]} (${m[at + 1]}, ${m[at + 2]})`
const named = (t: Tile): string => `Route102 (${t.x}, ${t.y})`
const flagBit = async (): Promise<number> => ((await readU8(FLAG_BYTE)) >> (FLAG & 7)) & 1
const indexOf = (log: string[], pattern: RegExp, from = 0): number => log.findIndex((l, i) => i >= from && pattern.test(l))
const count = (pattern: RegExp): number => backendLog().filter((l) => pattern.test(l)).length

/** Scenario set-up with the bot idle: encounters off and his flag set for the walk to the start, both undone before Run. */
async function walkToStart(): Promise<void> {
  await repel(250)
  await setTrainerFlag(FLAG, true)
  await walkTo(TO_START)
  await setTrainerFlag(FLAG, false)
  await repel(0)
}

/**
 * While Waiting: the speech still at its first wait, so no A or B reached it (lock 1, battle bit 0, opponent 318, his flag
 * clear, the field window at 9, the script at DoTrainerBattle + 4), on one of his sight tiles. Returns the tile.
 */
async function speechRead(rom: string, criterion: string): Promise<Tile> {
  const read = {
    lock: await readU8(LOCK), battleBit: await readBattleBit(), opponent: await readU16(OPPONENT), flag: await flagBit(),
    window: await readU16(FIELD_WINDOW), script: hex8(await readU32(SCRIPT_PTR))
  }
  const tile = await readTile()
  step(`speech read (${criterion}): ${JSON.stringify(read)}, tile ${JSON.stringify(tile)}`)
  expect(read, `${criterion}: the speech waits at its line break with nothing pressed`)
    .toEqual({ lock: 1, battleBit: 0, opponent: CALVIN, flag: 0, window: 9, script: hex8(DO_TRAINER_BATTLE[rom] + 4) })
  expect(SIGHT, `${criterion}: the stop's tile ${JSON.stringify(tile)} is not one of his sight tiles`).toContainEqual(tile)
  step(`the stop's tile: ${named(tile)}, one of his sight tiles`)
  return tile
}

/** Exactly one message with the sentence in ShinyLogChannel since the Run click; the bot sends it just after its stop line. */
async function oneMessage(runStart: string, criterion: string): Promise<void> {
  await expect.poll(async () => (await discordMessages(runStart, SENTENCE)).length,
    { message: `${criterion}: Discord messages "${SENTENCE}" since the Run click`, timeout: 30_000, intervals: [3000] }).toBe(1)
}

/** Nothing pressed or sent by the bot after line `at` of the log. */
function nothingSince(at: number, criterion: string): void {
  expect(acts(backendLog().slice(at + 1)), `"nothing at all after the stop": ${criterion}`).toEqual([])
}

async function waitingState(card: Locator, criterion: string): Promise<void> {
  await expect.poll(async () => (await dashboardState())?.eventState, { message: `${criterion}: the event is not Waiting`, timeout: MINUTE, intervals: [1000] })
    .toBe('Waiting')
  await expect(card.getByText('Resume', { exact: true }), `${criterion}: no Resume on the card`).toBeVisible({ timeout: MINUTE })
}

/**
 * The stop: the sentence with the trainer read's result (`trainer 318`), the bot Waiting with Resume, the speech read, one
 * Discord message. Returns the stop line's index and the tile.
 */
async function waitingOnCalvin(card: Locator, rom: string, runStart: string): Promise<{ at: number; tile: Tile }> {
  const criterion = '"the bot is Waiting on the tile where he sees the player"'
  const line = await waitLine(TRAINER_STOP, 1, 3 * MINUTE, criterion, { expectedStop: TRAINER_STOP })
  const [, map, seen] = TRAINER_STOP.exec(line)!
  expect(map, `${criterion}: the sentence's map`).toBe('Route102')
  expect(seen, `${criterion}: the trainer read's result`).toMatch(new RegExp(`^trainer ${CALVIN}, `))
  await waitingState(card, criterion)
  const tile = await speechRead(rom, criterion)
  await oneMessage(runStart, '"one Discord message"')
  return { at: backendLog().indexOf(line), tile }
}

/**
 * Resume pressed during the speech: within 20 s the orchestrator's "Waited for the free field after the trainer stop" line,
 * Waiting again, nothing pressed, still one message, the speech unchanged.
 */
async function resumeDuringSpeech(card: Locator, rom: string, runStart: string, at: number): Promise<void> {
  const criterion = '"Resume during the speech parks again"'
  const before = count(RESUME)
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  step('Resume during the speech')
  const line = await waitLine(RESUME, before + 1, 20_000, criterion, { expectedStop: TRAINER_STOP })
  expect(RESUME.exec(line)![1], `${criterion}: the Resume line`).toMatch(STAYED)
  await waitingState(card, criterion)
  nothingSince(at, `${criterion}: after the Resume during the speech`)
  await oneMessage(runStart, `${criterion}: no second message`)
  await speechRead(rom, criterion)
}

/**
 * The operator's part, played by the scenario while the bot is Waiting: A at the speech's line break, A at its last button
 * wait, then the fight to the free field (A at a battle window wait 5, 8 or 9 or the level-up box 1 or 2, FIGHT at action
 * cursor 0, the first move at move cursor 0), each press `H.A.2` sent once on its gate and confirmed by the byte it
 * changes, bounded at 3 minutes. Then his flag set and gBattleOutcome 1 (won).
 */
async function winTheBattle(rom: string): Promise<void> {
  const criterion = '"the scenario presses through the speech and wins the battle"'
  let presses = 0
  const press = async (what: string, gate: unknown, read: () => Promise<number>, left: (v: number) => boolean, ms = 5000): Promise<void> => {
    step(`scenario press ${++presses}: A ${what}, gate ${JSON.stringify(gate)}`)
    await runMacro('H.A.2')
    await pollWhileWaiting(read, left, ms, `${criterion}: A ${what}, confirmed`)
  }
  await press('at the speech\'s line break', { window: 9 }, () => readU16(FIELD_WINDOW), (v) => v !== 9)
  // The last line: the script's waitbuttonpress (research, row c2), then A, which starts the battle.
  await pollWhileWaiting(() => readU32(SCRIPT_PTR), (v) => v === DO_TRAINER_BATTLE[rom] + 5, 5000, `${criterion}: the script at DoTrainerBattle + 5`)
  const wait = { status: await readU8(SCRIPT_STATUS), mode: await readU8(SCRIPT_MODE), native: hex8(await readU32(SCRIPT_NATIVE)) }
  expect(wait, `${criterion}: the speech's last button wait`).toEqual({ status: 0, mode: 2, native: hex8(WAIT_FOR_A_OR_B[rom]) })
  await press('at the speech\'s last button wait', wait, readBattleBit, (v) => v === 1, 10_000)

  const deadline = Date.now() + 3 * MINUTE
  while (true) {
    const r = {
      battleBit: await readBattleBit(), lock: await readU8(LOCK), controller: await readU32(CONTROLLER), action: await readU8(CURSOR),
      move: await readU8(MOVE_CURSOR), box: await readU8(BOX_COUNTER), window: await readU16(WINDOW)
    }
    if (r.battleBit === 0 && r.lock === 0) break
    if (Date.now() > deadline) throw new Error(`${criterion}: not in the free field within 3 minutes; last read ${JSON.stringify(r)}`)
    if (r.battleBit === 1 && r.controller === MENU_FN) {
      expect(r.action, `${criterion}: the action cursor at the menu`).toBe(0)
      await press('on FIGHT', r, () => readU32(CONTROLLER), (v) => v !== MENU_FN)
    } else if (r.battleBit === 1 && r.controller === MOVE_FN) {
      expect(r.move, `${criterion}: the move cursor at the move list`).toBe(0)
      await press('on the first move', r, () => readU32(CONTROLLER), (v) => v !== MOVE_FN)
    } else if (r.battleBit === 1 && (r.box === 1 || r.box === 2)) {
      await press(`at the level-up box page ${r.box}`, r, () => readU8(BOX_COUNTER), (v) => v !== r.box)
    } else if (r.battleBit === 1 && BUTTON_WAITS.includes(r.window)) {
      await press(`at window ${r.window}`, r, () => readU16(WINDOW), (v) => v !== r.window)
    } else {
      await sleep(250)
    }
  }
  const after = { flag: await flagBit(), outcome: await readU8(OUTCOME) }
  step(`the scenario's battle: ${presses} presses to the free field; ${JSON.stringify(after)}`)
  expect(after, `${criterion}: flag ${FLAG} and gBattleOutcome in the free field`).toEqual({ flag: 1, outcome: 1 })
}

/** The real Resume: the action's Run beside its "Resume" label. */
async function resume(card: Locator): Promise<void> {
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  step('Resume after the win')
}

/** The stop and everything up to the real Resume, the same for both variants. Returns the stop line's index and the tile. */
async function meetCalvin(card: Locator, pages: Record<string, Page>, rom: string, runStart: string): Promise<{ at: number; tile: Tile }> {
  const stop = await waitingOnCalvin(card, rom, runStart)
  await shotAll('02-stopped', pages)
  nothingSince(stop.at, 'while Waiting at the stop')
  await resumeDuringSpeech(card, rom, runStart, stop.at)
  return stop
}

test(`RS trainer stop (${VARIANT}): Calvin on Route 102, Waiting with one Discord message, the scenario wins, Resume carries on`, async () => {
  test.setTimeout(30 * MINUTE)
  const rom = rsRom()
  step(`variant ${VARIANT} on ${rom}`)
  if (VARIANT === 'walk') await walkVariant(rom)
  else await runVariant(rom)
})

/** walk: the heal trip from (31, 15) to Oldale's Pokémon Center meets him on the way. */
async function walkVariant(rom: string): Promise<void> {
  const HP = 5
  // Set in the body; the cast keeps TypeScript from narrowing it to null.
  let stop = null as { at: number; tile: Tile } | null
  const { log, idle } = await trainingRun(async () => {
    await walkToStart()
    // HP at or below a quarter of the max: the first decision is the heal trip, the nearest Center by route cost.
    const lead = await readSlot(1)
    expect(HP, `HP ${HP} is not at or below the heal line, max HP ${lead.maxHp} / 4`).toBeLessThanOrEqual(Math.floor(lead.maxHp / 4))
    await writeSlot(1, { hp: HP })
  }, { trainType: 'Standard', targetLevel: 101, discord: true, expectedStop: TRAINER_STOP }, async ({ card, pages, runStart }) => {
    await waitLine(GO_TO_CENTER, 1, MINUTE, '"the heal trip": no GoToPokemonCenter', { expectedStop: TRAINER_STOP })
    await waitLine(NAV_HANDOFF, 1, 3 * MINUTE, '"the walk meets him": no hand-off', { expectedStop: TRAINER_STOP })
    stop = await meetCalvin(card, pages, rom, runStart)
    // The battle copies the lead's HP at its start; HP 5 could lose it.
    const lead = await readSlot(1)
    await writeSlot(1, { hp: lead.maxHp })
    await winTheBattle(rom)
    nothingSince(stop.at, 'through the scenario\'s battle')
    await oneMessage(runStart, '"one Discord message": after the battle')
    await shotAll('03-won', pages)
    const plans = count(PLAN)
    await resume(card)
    await waitLine(PLAN, plans + 1, MINUTE, '"the walk carries on": no plan after Resume', { expectedStop: TRAINER_STOP })
    await waitLine(NURSE_DONE, 1, 6 * MINUTE, '"the walk carries on to its goal": the nurse', { expectedStop: TRAINER_STOP })
    await waitLine(CLEARING, 1, 6 * MINUTE, '"the walk carries on to its goal": back on the start tile', { expectedStop: TRAINER_STOP })
    await oneMessage(runStart, '"one Discord message": at the end')
    return 'cancel'
  })
  const { at, tile } = stop!

  // The heal trip, and why: the HP written.
  const trip = HEAL_TRIP.exec(log[indexOf(log, HEAL_TRIP)] ?? '')
  expect(trip && Number(trip[2]), '"the heal trip": the heal trip line\'s HP').toBe(HP)
  const first = PLAN.exec(log[indexOf(log, PLAN)])!
  step(`the trip out: ${first[0].trim()}`)
  expect([tileOf(first, 1), tileOf(first, 4)], '"the walk toward the Center": the first plan').toEqual([CENTER, START_TILE])

  // The hand-off: a lock of 240 frames or more on his sight tile, from a segment that started on another tile (its held B
  // is from its first frame, before the lock); then nothing pressed up to the stop.
  const handOffAt = indexOf(log, NAV_HANDOFF)
  const handOff = NAV_HANDOFF.exec(log[handOffAt])!
  expect(handOffAt, '"the hand-off before the stop"').toBeLessThan(at)
  expect(Number(handOff[3]), `"the 4 s hand-off": ${handOff[0]}`).toBeGreaterThanOrEqual(240)
  expect(tileOf(handOff, 4), `"the hand-off on the stop's tile": ${handOff[0]}`).toBe(named(tile))
  const segmentAt = log.slice(0, handOffAt).map((l) => SEGMENT.exec(l)).filter((m) => m !== null).pop()!
  const startedOn = handOff[1] === '1' ? tileOf(first, 4) : tileOf(SEGMENT_ON.exec(log.slice(0, handOffAt).filter((l) => SEGMENT_ON.test(l)).pop()!)!, 3)
  step(`the segment that met him: ${segmentAt[0].trim()}, started on ${startedOn}`)
  expect(startedOn, '"no A or B once the field locks": the segment started on the lock\'s tile').not.toBe(named(tile))
  expect(acts(log.slice(handOffAt + 1, at)), '"no A or B once the field locks": between the hand-off and the stop').toEqual([])
  expect(log.slice(handOffAt, at).filter((l) => LOCK_HANDED.test(l)), '"the trainer read": a not-a-trainer line').toEqual([])

  // After the real Resume: a new plan from where he stopped the player, the Center, the nurse, and back to the start. No
  // plan is logged while Waiting, so the first plan after the Resume-during-the-speech line is the real Resume's.
  const resumed = log.findIndex((l, i) => i > at && RESUME.test(l)) + 1
  const again = PLAN.exec(log[indexOf(log, PLAN, resumed)])!
  step(`the walk after Resume: ${again[0].trim()}`)
  expect([tileOf(again, 1), tileOf(again, 4)], '"the walk plans again from where the player stands"').toEqual([CENTER, named(tile)])
  const counter = indexOf(log, ARRIVED, resumed)
  expect(tileOf(ARRIVED.exec(log[counter])!, 1), '"the walk carries on to its goal": the first arrival after Resume').toBe(CENTER)
  const nurse = NURSE_DONE.exec(log[indexOf(log, NURSE_DONE, counter)])!
  expect(nurse[1], `"the walk carries on to its goal": ${nurse[0]}`).toBe('6')
  // Back to (31, 15), which is reached from Oldale only through his sight tiles, with no second stop.
  const back = PLAN.exec(log[indexOf(log, PLAN, indexOf(log, NURSE_DONE, counter))])!
  step(`the walk back: ${back[0].trim()}`)
  expect([tileOf(back, 1), tileOf(back, 4)], '"the walk back to the start"').toEqual([START_TILE, CENTER])
  step(`back on the start tile: ${CLEARING.exec(log[indexOf(log, CLEARING)])![1]}`)
  expect(log.filter((l) => TRAINER_STOP.test(l)), '"no second stop": the trainer stop lines').toHaveLength(1)
  expect(idle.tile, '"back on the start tile": read with the bot idle').toEqual(START)
}

/**
 * run: part 1, the long transition with no trainer read, on its own launch; part 2, a fresh launch from the save, the
 * scenario's walk to (31, 15) and a flee-only Run with x range 2 into his sight. The frame is trainingRun's, for two Runs.
 */
async function runVariant(rom: string): Promise<void> {
  const frame: Frame = { backend: null, app: null }
  const { pg } = settings()
  removeCheats()
  installPristine(rom)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    expect(await discordMode(), 'the trainer stop\'s one Discord message needs Discord logged in').toBe(true)

    // Part 1: the lead at Lv 2, at or below every Route 101 wild, so every battle gets the long transition (about 2.5 s).
    let card = await launchRs(admin, rom, 'emulator')
    step(`lead: ${JSON.stringify(await readSlot(1))}`)
    await writeSlot(1, { level: 2 })
    const firstRun = await startWild(card, { mode: 'Spin' })
    await waitLine(OVER, 5, 6 * MINUTE, '"no trainer read with the long transition": five fled battles')
    await cancelEventAndBot(card)
    const part1 = backendLog()
    await shotAll('01-part1', pages)

    // Part 2: a fresh launch, so the level is the save's again.
    card = await launchRs(admin, rom, 'emulator-run')
    await walkToStart()
    const runStart = await startWild(card, { mode: 'Run', xRange: X_RANGE })
    const { at, tile } = await meetCalvin(card, pages, rom, runStart)
    let log = backendLog()
    // The seek that met him: from (31, 15) with the Run macro; its seek line's 4 s or more to the battle screen is the
    // hand-off; nothing pressed from there to the stop, and no not-a-trainer line. RunHold's B is a hold on the first frame
    // of the first leg, in the macro string, not a press line: that leg started on (31, 15), two steps before the lock on
    // his tile, so it fell before the lock. After the lock the repeat macro holds only the d-pad (FR-10).
    const fromAt = log.slice(0, at).map((l, i) => (RUN_FROM.test(l) ? i : -1)).filter((i) => i >= 0).pop()!
    const [, fx, fy, macro] = RUN_FROM.exec(log[fromAt])!
    step(`the seek that met him: ${log[fromAt].trim()}`)
    expect([Number(fx), Number(fy), macro], '"the Run line crosses his sight": the seek\'s tile and macro').toEqual([START.x, START.y, RUN_MACRO])
    expect(named(tile), '"no A or B once the field locks": RunHold\'s B is on the leg\'s first frame, on the seek\'s tile').not.toBe(named(START))
    const handOff = seekOf(log.slice(fromAt, at))
    expect(handOff && handOff.rollToBattle, '"the 4 s hand-off": the seek line\'s ms to the battle screen').toBeGreaterThanOrEqual(4000)
    expect(acts(log.slice(fromAt + 1, at)), '"no A or B once the field locks": between the seek and the stop').toEqual([])
    expect(log.slice(fromAt, at).filter((l) => LOCK_HANDED.test(l)), '"the trainer read": a not-a-trainer line').toEqual([])

    await winTheBattle(rom)
    nothingSince(at, 'through the scenario\'s battle')
    await oneMessage(runStart, '"one Discord message": after the battle')
    await shotAll('03-won', pages)
    const froms = count(RUN_FROM)
    const overs = count(OVER)
    await resume(card)
    // "the hunt carries on": back on its run line where he stopped the player, then a fled encounter.
    const next = RUN_FROM.exec(await waitLine(RUN_FROM, froms + 1, 30_000, '"the hunt carries on": no seek after Resume', { expectedStop: TRAINER_STOP }))!
    expect([Number(next[1]), Number(next[2])], '"back on the run line": the first seek after Resume starts where he stopped the player').toEqual([tile.x, tile.y])
    await waitLine(OVER, overs + 1, 3 * MINUTE, '"the hunt carries on": a fled encounter after Resume', { expectedStop: TRAINER_STOP })
    await oneMessage(runStart, '"one Discord message": at the end')
    await shotAll('04-resumed', pages)
    let idleTile: Tile | null = null
    await cancelEventAndBot(card, async () => {
      idleTile = await readTile()
      step(`tile between the cancels: ${JSON.stringify(idleTile)}`)
    })
    expect(flashUnchanged(rom), `"the save's first 131,072 bytes unchanged": the ${rom} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    log = backendLog()
    failOnStops(log, TRAINER_STOP)
    checkPresses(log)
    expect(log.filter((l) => TRAINER_STOP.test(l)), '"no second stop": the trainer stop lines').toHaveLength(1)
    expect(log.filter((l) => OFF_LINE.test(l)), '"back on the run line": an off-the-run-line stop').toEqual([])
    const t = idleTile as Tile | null
    expect(t && [t.group, t.num, t.y, t.x >= START.x - X_RANGE && t.x <= START.x + X_RANGE], '"back on the run line": the tile read between the cancels')
      .toEqual([ROUTE102.group, ROUTE102.num, START.y, true])

    // Part 1: the long transition's line; every seek under the 4 s hand-off (the bot's trainer read runs only after it);
    // no not-a-trainer line, which every hand-off prints for a lock that is not a trainer's; each battle a clean flee.
    expect(part1.filter((l) => SLOW_TRANSITION.test(l)), '"the long transition": its line').toHaveLength(1)
    const fled1 = battles(part1).filter((b) => b.some((l) => OVER.test(l)))
    const seeks = fled1.map((b) => seekOf(b)?.rollToBattle ?? -1)
    step(`part 1: ms to the battle screen per seek: ${seeks.join(', ')}`)
    for (const ms of seeks) expect(ms > 0 && ms < 4000, `"no trainer read with the long transition": a seek of ${ms} ms to the battle screen`).toBe(true)
    expect(part1.filter((l) => LOCK_HANDED.test(l)), '"no trainer read with the long transition": a hand-off').toEqual([])
    // Every fled battle of both parts, each with its five presses.
    battles(log).filter((b) => b.some((l) => OVER.test(l))).forEach((b, k) => checkFlee(b, k + 1))

    const pids = log.filter((l) => ENCOUNTER.test(l)).map((l) => parseEncounter(l).pid)
    const rows = psqlRows(pg, firstRun, rom)
    expect(rows.map((r) => hex8(Number(r[0]))), '"one encounters row per encounter line": the rows\' PIDs').toEqual(pids)
    expect(rows.filter((r) => r[3] !== 'f').map((r) => r[0]), '"none shiny": shiny encounters rows').toEqual([])
  } finally {
    await teardown(frame, rom)
  }
}
