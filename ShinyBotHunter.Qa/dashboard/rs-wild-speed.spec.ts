import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ADVANCE, battles, cancelEventAndBot, checkFlee, CONFIRM, ENCOUNTER, failOnStop, fps, installPristine, launchRs, type Mode, OVER,
  parseEncounter, psqlRows, removeCheats, RUN_FROM, seekBound, seekOf, SPIN_LINE, startWild, teardown, TO_RUN
} from './rs-wild'
import { backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
// Each mode hunts this long by the wall clock. The first encounter of a mode is left out, since it follows the launch and
// its seek has no immune checks after a battle.
const MINUTES = Number(process.env.QA_RS_SPEED_MINUTES ?? 20)
const toMs = (text: string): number => Date.parse(text.replace(' ', 'T').replace(/([+-]\d\d)$/, '$1:00'))
const mean = (v: number[]): number => v.reduce((sum, x) => sum + x, 0) / v.length
const PRESS_MS = [ADVANCE, TO_RUN, CONFIRM]
// Owner, 2026-11-15, accepted 266 and 269 an hour measured at full emulator speed: the difference from 270 is the
// encounter roll's luck; the per-battle limits are what the bot controls. The target is reported, the floor asserted.
const TARGET = 270
const FLOOR = 265

/**
 * Launch Bot + Continue (a new EmuHawk, so no battle is left on screen) with no window recorder, the mode, MINUTES of
 * hunting with nothing touching the emulator (only backend.log, a file, is read, to fail early on a stop or a crash),
 * two cancels.
 */
async function hunt(admin: Page, mode: Mode, pages: Record<string, Page>): Promise<string> {
  const card = await launchRs(admin, ROM, 'emulator', false)
  const runStart = await startWild(card, mode)
  const end = Date.now() + MINUTES * MINUTE
  while (Date.now() < end) {
    try {
      failOnStop(backendLog())
    } catch (e) {
      throw new Error(`"NFR-1 in both modes" (${mode.mode}): ${(e as Error).message}`)
    }
    await new Promise((resolve) => setTimeout(resolve, Math.min(5000, end - Date.now())))
  }
  step(`${mode.mode}: hunted ${MINUTES} minutes`)
  await shotAll(`${mode.mode.toLowerCase()}-hunting`, pages)
  await cancelEventAndBot(card)
  return runStart
}

/**
 * One mode's NFR-1 figures over its finished encounters from the second, and the rate over its rows from the second,
 * written to steps.log as a table whatever they are; returns the unmet criteria, each with the mode's summary beside it so
 * a failure shows whether it was the roll's luck.
 */
function measure(mode: string, chunks: string[][], rows: string[][]): string[] {
  const measured = chunks.slice(1)
  const framesPerMs = fps(measured.flat())
  const per = measured.map((b) => {
    const seek = seekOf(b)
    if (!seek) throw new Error(`"NFR-1 in both modes" (${mode}): an encounter with no seek line`)
    const frames = Number(OVER.exec(b.find((l) => OVER.test(l))!)![1])
    const pressMs = b.reduce((sum, l) => sum + PRESS_MS.reduce((s, p) => s + (p.test(l) ? Number(p.exec(l)![2]) : 0), 0), 0)
    // A roll to battle screen of 0 is "not observed": no poll saw the field leave free before the battle screen.
    const rollToFree = seek.rollToBattle > 0 ? seek.rollToBattle * framesPerMs + frames : null
    return { ...seek, frames, pressMs, botFrames: pressMs * framesPerMs, rollToFree }
  })
  step(`${mode}: encounter | macro to roll ms | roll to battle screen ms | battle frames | roll to free field frames | press ms | press frames`)
  per.forEach((e, k) => step(`${mode}: ${k + 2} | ${e.toRoll} | ${e.rollToBattle || 'not observed'} | ${e.frames} | ` +
    `${e.rollToFree === null ? 'not observed' : e.rollToFree.toFixed(0)} | ${e.pressMs} | ${e.botFrames.toFixed(1)}`))

  // The rows from the second, by Postgres time; fewer than two leave the rate NaN, and the PID check after the tables
  // names too few rows.
  const used = rows.slice(1)
  const seconds = used.length >= 2 ? (toMs(used[used.length - 1][4]) - toMs(used[0][4])) / 1000 : NaN
  const rate = ((used.length - 1) * 3600) / seconds
  const { perCheck, falseFailure } = seekBound(per.map((e) => e.toRoll), framesPerMs)
  const observed = per.flatMap((e) => (e.rollToFree === null ? [] : [{ rollToBattle: e.rollToBattle, rollToFree: e.rollToFree }]))
  const rollToFree = observed.map((e) => e.rollToFree)
  const bot = per.map((e) => e.botFrames)
  const summary = `${mode}: ${rate.toFixed(1)} an hour, target ${TARGET} (${used.length} rows after the first, over ${seconds.toFixed(1)} s); ` +
    `${per.length} finished encounters measured; ` +
    `macro to roll mean ${mean(per.map((e) => e.toRoll)).toFixed(0)} ms, shortest ${perCheck.toFixed(1)} frames a check; ` +
    `roll to battle screen mean ${mean(observed.map((e) => e.rollToBattle)).toFixed(0)} ms over ${observed.length} observed ` +
    `(${per.length - observed.length} not observed); battle mean ${mean(per.map((e) => e.frames)).toFixed(0)} frames, ` +
    `max ${Math.max(...per.map((e) => e.frames))}; roll to free field mean ${mean(rollToFree).toFixed(0)} frames, ` +
    `max ${Math.max(...rollToFree).toFixed(0)}; press frames mean ${mean(bot).toFixed(1)}, max ${Math.max(...bot).toFixed(1)} ` +
    `(the poll's lag before each press is in no log); ${(framesPerMs * 1000).toFixed(2)} fps`
  step(summary)

  const failures: string[] = []
  if (!(rate >= FLOOR)) failures.push(`"${TARGET} encounters an hour" (accepted floor ${FLOOR}): ${summary}`)
  else if (rate < TARGET) step(`${mode}: ${rate.toFixed(1)} an hour is under the ${TARGET} target and inside the accepted floor of ${FLOOR}`)
  if (!(observed.length > 0 && mean(rollToFree) <= 700)) {
    failures.push(`"a fled battle, roll to free field, no more than 700 frames" (the mean): ${summary}`)
  }
  if (!(mean(bot) <= 30)) failures.push(`"no more than 30 of those frames between a state the bot waits for and its press landing" (the mean): ${summary}`)
  if (!(perCheck <= 9)) {
    failures.push(`"seeking at no more than 9 frames a check" (fails falsely ${(falseFailure * 100).toFixed(1)}% of the time, ` +
      `when no seek rolled on its first open check): ${summary}`)
  }
  return failures
}

test('RS Wild Encounter speed: NFR-1 over a timed hunt by Spin, then one by Run with y range 1', async () => {
  test.setTimeout((2 * MINUTES + 15) * MINUTE)
  const frame: Frame = { backend: null, app: null }
  const { pg } = settings()
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const spinStart = await hunt(admin, { mode: 'Spin' }, pages)
    const runStart = await hunt(admin, { mode: 'Run', yRange: 1 }, pages)
    await shutdown(frame, admin)

    const log = backendLog()
    const rows = psqlRows(pg, spinStart, ROM)
    const modes: [string, RegExp, string[][]][] = [
      ['Spin', SPIN_LINE, rows.filter((r) => toMs(r[4]) < Date.parse(runStart))],
      ['Run', RUN_FROM, rows.filter((r) => toMs(r[4]) >= Date.parse(runStart))]
    ]
    // Both modes' tables and summaries go to steps.log before any check can throw.
    const failures: string[] = []
    const fled: [string, string[][], string[][]][] = []
    for (const [mode, seekLine, modeRows] of modes) {
      // The cancel may cut the mode's last encounter short; every other one must have finished.
      const all = battles(log).filter((b) => seekLine.test(b[0]))
      const chunks = all.length > 0 && !all[all.length - 1].some((l) => OVER.test(l)) ? all.slice(0, -1) : all
      failures.push(...measure(mode, chunks, modeRows))
      fled.push([mode, chunks, modeRows])
    }
    for (const [mode, chunks, modeRows] of fled) {
      // The rate rests on the rows being these encounters', in order (a cut-short last encounter may add one more row).
      const pids = chunks.map((b) => parseEncounter(b.find((l) => ENCOUNTER.test(l))!).pid)
      expect(modeRows.slice(0, pids.length).map((r) => hex8(Number(r[0]))), `"${TARGET} encounters an hour" (${mode}): ` +
        `the first ${pids.length} rows are not the encounter lines'`).toEqual(pids)
    }
    for (const [mode, chunks] of fled) {
      for (let k = 1; k < chunks.length; k++) {
        try {
          checkFlee(chunks[k], k + 1)
        } catch (e) {
          throw new Error(`${mode}: ${(e as Error).message}`)
        }
      }
      step(`${mode}: encounters 2 to ${chunks.length} fled cleanly`)
    }
    expect(failures, `NFR-1 not met:\n${failures.join('\n')}`).toEqual([])
  } finally {
    await teardown(frame, ROM)
  }
})
