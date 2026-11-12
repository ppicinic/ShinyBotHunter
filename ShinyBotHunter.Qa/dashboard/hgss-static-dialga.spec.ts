import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ATTEMPT, backendLines, backendLog, cancelEventAndBot, checkAttempt, DIALGA, hex8, installPristine, launchBot, MACROS, MINUTE,
  parseResult, parseRunLine, PHASE, pristineSha, psql, readPartyCount, readTile, RESET, RESULT, ROM, RUN_LINE, runStatic,
  SAVED, savedSha, settings, shutdown, START_TILE, startFrame, teardown, waitForLine, waitForSummary, type Frame, type Result
} from './static'

test('HGSS Static Encounter Dialga: one save at Run, two attempts each to the summary and a reset, two encounter rows', async () => {
  test.setTimeout(25 * MINUTE)
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
    const runStart = await runStatic(card, DIALGA)

    // 1. The Run line: the encounter and the pathfinder's walk, printed once (FR-4, FR-12); then the save (FR-3).
    const run = parseRunLine(await waitForLine(RUN_LINE, 1, MINUTE))
    expect(run, 'the Run line').toEqual({
      name: DIALGA, species: 483, circle: [12, 15], map: 522, from: [16, 14], facing: 'up', macro: MACROS[DIALGA]
    })
    await waitForLine(SAVED, 1, 90_000)

    // 2. Two attempts: the summary in RAM (and in a screenshot) before the result line, then the reset back to the start tile.
    const attemptAt: number[] = []
    const resultAt: number[] = []
    const results: Result[] = []
    for (const n of [1, 2]) {
      await waitForLine(ATTEMPT, n, 2 * MINUTE)
      attemptAt.push(Date.now())
      const shot = `0${n + 1}-summary`
      await waitForSummary(n - 1, 240_000, shot)
      await shotAll(shot, pages)
      const result = parseResult(await waitForLine(RESULT, n, MINUTE))
      resultAt.push(Date.now())
      expect([result.species, result.shiny], `attempt ${n}'s result`).toEqual([483, false])
      results.push(result)
      await expect.poll(readTile, { message: `attempt ${n}: the reset did not land on the start tile`, timeout: 90_000, intervals: [250] })
        .toEqual(START_TILE)
      step(`attempt ${n}: reset back on the start tile`)
    }

    // 3. The third attempt line shows Run's checks passed after the second reset; then cancel the event and the bot.
    await waitForLine(ATTEMPT, 3, 90_000)
    await cancelEventAndBot(card)
    await shotAll('04-cancelled', pages)

    // 4. The asserts on the log.
    const log = backendLog()
    expect(backendLines(SAVED).length, 'not exactly one save at Run').toBe(1)
    expect(backendLines(RUN_LINE).length, 'the walk macro not printed exactly once').toBe(1)
    for (const line of backendLines(RESET)) step(`logged: ${line.trim()}`)
    checkAttempt(log, 1)
    checkAttempt(log, 2)
    expect(results[0].pid, 'the two personality values are the same').not.toBe(results[1].pid)
    const first = resultAt[0] - attemptAt[0]
    const second = resultAt[1] - resultAt[0]
    step(`attempt 1: ${first} ms from its attempt line to its result; attempt 2: ${second} ms from result 1 to result 2`)
    expect(first, 'attempt 1 took 240 s or more').toBeLessThan(240_000)
    expect(second, 'attempt 2 took 240 s or more').toBeLessThan(240_000)

    // 5. The save at Run reached the SaveRAM, now that EmuHawk has exited (FR-3).
    expect(savedSha(), `${ROM} SaveRAM still equals the pristine copy`).not.toBe(pristineSha())
    step(`${ROM} SaveRAM sha256 ${savedSha()} differs from the pristine copy`)

    await shutdown(frame, admin)

    // 6. Two encounter rows since the Run, the two PIDs in order (FR-11).
    const rows = psql(pg, `SELECT "PID","Species","IsShiny" FROM encounters WHERE "Milestone"='${PHASE}' ` +
      `AND "GameFileName"='${ROM}' AND "Timestamp" >= '${runStart}' ORDER BY "Timestamp"`)
    step(`encounter rows since the Run: ${rows.join(' ; ')}`)
    expect(rows.map((r) => {
      const [pid, species, shiny] = r.split('|')
      return [hex8(Number(pid)), species, shiny]
    }), 'the encounter rows').toEqual(results.map((r) => [hex8(r.pid), '483', 'f']))
  } finally {
    await teardown(frame)
  }
})
