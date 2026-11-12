import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import { decodeMon } from './egg-pid'
import {
  backendLog, cancelEventAndBot, checkAttempt, GIRATINA, hex8, installPristine, launchBot, MACROS, MINUTE, parseResult,
  parseRunLine, PHASE, pristineSha, psql, readPartyCount, readSlot, readTile, RESULT, ROM, RUN_LINE, runStatic, SAVED, savedSha,
  settings, shotPages, shutdown, START_TILE, startFrame, teardown, WALK, waitForLine, waitForSummary, type Frame
} from './static'

// ITEM_GRISEOUS_ORB: the gift Giratina holds it and is in its Origin form (1).
const GRISEOUS_ORB = 112

test('HGSS Static Encounter Giratina: the six-step walk, slot 2 read with the summary on screen as Origin Giratina, logged as giratina-origin', async () => {
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  const { pg } = settings()
  installPristine()
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchBot(admin, ROM, 'emulator')
    expect(await readTile(), 'not the save\'s start tile').toEqual(START_TILE)
    expect(await readPartyCount(), 'the save\'s party is not one').toBe(1)
    await shotAll('01-launched', pages)
    const runStart = await runStatic(card, GIRATINA)

    // 1. The pathfinder's walk is six steps up, from the start tile to the circle.
    const run = parseRunLine(await waitForLine(RUN_LINE, 1, MINUTE))
    expect(run, 'the Run line').toEqual({
      name: GIRATINA, species: 487, circle: [16, 8], map: 522, from: [16, 14], facing: 'up', macro: MACROS[GIRATINA]
    })
    await waitForLine(SAVED, 1, 90_000)

    // 2. At the walk line the player stands on the circle; it stays there until the Yes.
    await waitForLine(WALK, 1, 2 * MINUTE)
    const onCircle = await readTile()
    step(`at the walk line: ${JSON.stringify(onCircle)}`)
    expect([onCircle.map, onCircle.x, onCircle.y], 'the walk did not end on the circle').toEqual([522, 16, 8])

    // 3. QA's own read of slot 2, through the driver, with the summary on screen.
    await waitForSummary(0, 240_000, '02-summary')
    const mon = decodeMon(await readSlot(1))
    await shotPages('02-summary', pages)
    step(`slot 2 at the summary: species ${mon.species}, form ${mon.form}, item ${mon.heldItem}, pid ${hex8(mon.pid)}, checksum ${mon.checksumValid}`)
    expect([mon.species, mon.form, mon.heldItem, mon.checksumValid], 'slot 2 is not Origin Giratina holding the Griseous Orb')
      .toEqual([487, 1, GRISEOUS_ORB, true])

    // 4. The result line is the same Pokémon; then cancel the event and the bot.
    const result = parseResult(await waitForLine(RESULT, 1, MINUTE))
    expect([result.species, hex8(result.pid)], 'the result line').toEqual([487, hex8(mon.pid)])
    await cancelEventAndBot(card)
    await shotAll('03-cancelled', pages)
    checkAttempt(backendLog(), 1)

    // 5. The save at Run reached the SaveRAM, now that EmuHawk has exited (FR-3).
    expect(savedSha(), `${ROM} SaveRAM still equals the pristine copy`).not.toBe(pristineSha())
    step(`${ROM} SaveRAM sha256 ${savedSha()} differs from the pristine copy`)

    await shutdown(frame, admin)

    // 6. One encounter row since the Run: QA's PID, logged as the Origin form (FR-11).
    const rows = psql(pg, `SELECT "PID","Species","FormCode" FROM encounters WHERE "Milestone"='${PHASE}' ` +
      `AND "GameFileName"='${ROM}' AND "Timestamp" >= '${runStart}' ORDER BY "Timestamp"`)
    step(`encounter rows since the Run: ${rows.join(' ; ')}`)
    expect(rows.map((r) => {
      const [pid, species, formCode] = r.split('|')
      return [hex8(Number(pid)), species, formCode]
    }), 'the encounter row').toEqual([[hex8(mon.pid), '487', 'giratina-origin']])
  } finally {
    await teardown(frame)
  }
})
