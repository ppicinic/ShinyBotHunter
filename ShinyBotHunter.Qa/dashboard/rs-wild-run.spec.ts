import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  battles, cancelEventAndBot, checkFlee, ENCOUNTER, flashUnchanged, fps, installPristine, launchRs, LEVELS, OVER, parseEncounter,
  psqlRows, readTile, removeCheats, RUN_FROM, RUN_MACRO, seekBound, seekOf, START, startWild, teardown, waitForLine, WILD,
  type Tile
} from './rs-wild'
import { backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
// y range 1 from (14, 4): the line is rows 3 to 5 of column 14, with its ends on rows 3 and 5.
const ROWS = [3, 4, 5]
const END_ROWS = [3, 5]
const MAX_ENCOUNTERS = 25
const OFF_LINE = /\[Run\] The player is off the run line\./

// A seek that started on an end row, so the battle before it ended there, and whose own battle is over.
const fromEndRow = (b: string[]): boolean => {
  const m = RUN_FROM.exec(b[0])
  return m !== null && END_ROWS.includes(Number(m[2])) && b.some((l) => OVER.test(l))
}

test('RS Wild Encounter by Run: y range 1 from (14, 4), B held, every seek starting in rows 3 to 5 of column 14, ten fled encounters', async () => {
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  const { pg } = settings()
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')
    await shotAll('01-launched', pages)
    const runStart = await startWild(card, { mode: 'Run', yRange: 1 })

    // Ten fled battles, then on until a seek after the first starts on row 3 or 5 and its battle is over; at most 25.
    const endRow = '"the tile stays in rows 3 to 5 of column 14, including after a battle that ended on row 3 or 5"'
    const deadline = Date.now() + 12 * MINUTE
    let fled = 10
    await waitForLine(OVER, fled, deadline - Date.now(), '"ten rows": ten fled battles')
    while (!battles(backendLog()).slice(1).some(fromEndRow)) {
      if (fled === MAX_ENCOUNTERS) throw new Error(`${endRow}: no battle ended on an end row in ${MAX_ENCOUNTERS}`)
      fled++
      await waitForLine(OVER, fled, deadline - Date.now(), `${endRow}: fled battle ${fled} of at most ${MAX_ENCOUNTERS}`)
    }
    step(`a seek started on an end row after ${fled} fled battles`)
    await shotAll('02-hunting', pages)
    let tile = null as Tile | null
    await cancelEventAndBot(card, async () => {
      tile = await readTile()
      step(`tile between the cancels: ${JSON.stringify(tile)}`)
    })
    await shotAll('03-cancelled', pages)
    expect(flashUnchanged(ROM), `"the save's first 131,072 bytes unchanged": the ${ROM} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    // The tile: the [Run] from line of every seek (logged once the bot's own off-the-line check passed), no
    // off-the-line stop, and the read between the cancels. "B held": every seek's macro has RunHold joined to the first move.
    const log = backendLog()
    expect(log.filter((l) => OFF_LINE.test(l)), `${endRow}: an off-the-run-line stop`).toEqual([])
    for (const line of log.filter((l) => RUN_FROM.test(l))) {
      const [, x, y, macro] = RUN_FROM.exec(line)!
      expect([Number(x), ROWS.includes(Number(y))], `${endRow}: a seek from (${x}, ${y})`).toEqual([START.x, true])
      expect(macro, `"steps at 8 frames a tile (B held)": the macro of the seek from (${x}, ${y})`).toBe(RUN_MACRO)
    }
    expect(tile && [tile.group, tile.num, tile.x, ROWS.includes(tile.y)], `${endRow}: the tile read between the cancels`)
      .toEqual([START.group, START.num, START.x, true])

    // "ten rows": one row per encounter line, by PID, each a Route 101 Pokémon at Lv 2 or 3, not shiny; each battle a clean flee.
    const all = battles(log)
    for (let k = 0; k < fled; k++) checkFlee(all[k], k + 1)
    const rowsCriterion = '"ten rows" (Wurmple, Zigzagoon or Poochyena, Lv 2 or 3)'
    const encounters = log.filter((l) => ENCOUNTER.test(l)).map(parseEncounter)
    const rows = psqlRows(pg, runStart, ROM)
    expect(rows.length, `${rowsCriterion}: too few rows`).toBeGreaterThanOrEqual(10)
    expect(rows.map((r) => hex8(Number(r[0]))), `${rowsCriterion}: the rows' PIDs are not the encounter lines'`)
      .toEqual(encounters.map((e) => e.pid))
    for (const [pid, species, level, shiny] of rows) {
      expect(WILD, `${rowsCriterion}: ${pid} is species ${species}`).toContain(Number(species))
      expect(LEVELS, `${rowsCriterion}: ${pid} is Lv ${level}`).toContain(Number(level))
      expect(shiny, `${rowsCriterion}: ${pid} is shiny`).toBe('f')
    }

    // "steps at 8 frames a tile (B held)": G4's bound over the seeks after a battle, reported only. Here it covers 9 to 24
    // seeks, so it would fail falsely up to about 35% of the time; rs-wild-speed asserts it over thirty. Walking is about 16.
    const toRoll = all.slice(1, fled).map((b, k) => {
      const seek = seekOf(b)
      if (!seek) throw new Error(`"steps at 8 frames a tile (B held)": encounter ${k + 2} has no [Run] seek line`)
      return seek.toRoll
    })
    const framesPerMs = fps(log)
    const { perCheck, falseFailure } = seekBound(toRoll, framesPerMs)
    step(`Run seeks after a battle, macro to roll: ${toRoll.join(', ')} ms at ${(framesPerMs * 1000).toFixed(2)} fps; ` +
      `shortest ${perCheck.toFixed(1)} frames a check (bound 9, ${perCheck <= 9 ? 'within' : 'above'} it; not asserted here, ` +
      `a false failure ${(falseFailure * 100).toFixed(0)}% of the time over ${toRoll.length} seeks)`)
  } finally {
    await teardown(frame, ROM)
  }
})
