import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ATTEMPT, backendLines, backendLog, cancelEventAndBot, checkAttempt, DIALGA, failOnStop, hex8, installPristine, launchBot, MACROS,
  MINUTE, parseResult, parseRunLine, PHASE, pristineSha, psql, readPartyCount, readTile, RESET, RESULT, ROM, RUN_LINE, runStatic,
  SAVED, savedSha, settings, shotPages, shutdown, START_TILE, startFrame, teardown, waitForLine, waitForSummary, type Frame,
  type Result
} from './static'

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

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

    // 2. Two attempts: the summary drawn (and in a screenshot) before the result line, then the reset back to the start tile.
    const attemptAt: number[] = []
    const resultAt: number[] = []
    const results: Result[] = []
    for (const n of [1, 2]) {
      await waitForLine(ATTEMPT, n, 2 * MINUTE)
      attemptAt.push(Date.now())
      const shot = `0${n + 1}-summary`
      await waitForSummary(n - 1, 240_000, shot)
      await shotPages(shot, pages)
      const result = parseResult(await waitForLine(RESULT, n, MINUTE))
      resultAt.push(Date.now())
      expect([result.species, result.shiny], `attempt ${n}'s result`).toEqual([483, false])
      results.push(result)
      // The tile reads map -1 while the game reboots; a stop (a reset that did not take) fails at once with its line.
      const resetDeadline = Date.now() + 90_000
      let tile = await readTile()
      while (JSON.stringify(tile) !== JSON.stringify(START_TILE)) {
        failOnStop(backendLog())
        if (Date.now() > resetDeadline) throw new Error(`attempt ${n}: the reset did not land on the start tile within 90 s; last ${JSON.stringify(tile)}`)
        await sleep(250)
        tile = await readTile()
      }
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
    checkAttempt(log, 1)
    checkAttempt(log, 2)
    expect(results[0].pid, 'the two personality values are the same').not.toBe(results[1].pid)
    // NFR-1: each attempt line to its result line, as seen by this scenario's 250 ms polls, under 240 s. The reset before
    // attempt 2 is the bot's own `[Static] reset N ms` and is reported beside it, with the sum, not asserted.
    const resetMs = backendLines(RESET).map((l) => Number(RESET.exec(l)![1]))
    for (const n of [1, 2]) {
      const attemptMs = resultAt[n - 1] - attemptAt[n - 1]
      const before = n === 1 ? 'no reset before it' : `reset before it ${resetMs[0]} ms, reset plus attempt ${resetMs[0] + attemptMs} ms`
      step(`attempt ${n}: ${attemptMs} ms from its attempt line to its result line; ${before}`)
      expect(attemptMs, `attempt ${n} took 240 s or more from its attempt line to its result`).toBeLessThan(240_000)
    }
    step(`resets logged: ${resetMs.join(' ms, ')} ms`)

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
