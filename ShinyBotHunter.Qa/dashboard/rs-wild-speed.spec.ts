import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ADVANCE, battles, cancelEventAndBot, checkFlee, CONFIRM, ENCOUNTER, fps, installPristine, launchRs, type Mode, OVER,
  parseEncounter, psqlRows, removeCheats, RUN_FROM, seekBound, seekOf, SPIN_LINE, startWild, teardown, TO_RUN, waitForLine
} from './rs-wild'
import { backendLines, backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
// Thirty measured encounters a mode: the first is left out, since its seek has no immune checks after a battle.
const N = 31
const toMs = (text: string): number => Date.parse(text.replace(' ', 'T').replace(/([+-]\d\d)$/, '$1:00'))
const mean = (v: number[]): number => v.reduce((sum, x) => sum + x, 0) / v.length
const PRESS_MS = [ADVANCE, TO_RUN, CONFIRM]

/** Launch Bot + Continue (a new EmuHawk, so no battle is left on screen), the mode, N more fled battles, two cancels. */
async function hunt(admin: Page, mode: Mode, pages: Record<string, Page>): Promise<string> {
  const card = await launchRs(admin, ROM, `emulator-${mode.mode.toLowerCase()}`)
  const before = backendLines(OVER).length
  const runStart = await startWild(card, mode)
  await waitForLine(OVER, before + N, 12 * MINUTE, `"NFR-1 in both modes": ${N} fled battles by ${mode.mode}`)
  await shotAll(`${mode.mode.toLowerCase()}-hunting`, pages)
  await cancelEventAndBot(card)
  return runStart
}

/**
 * One mode's NFR-1 figures over encounters 2 to 31, written to steps.log as a table whatever they are; returns the unmet
 * criteria, each with the mode's summary beside it so a failure shows whether it was the roll's luck.
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

  const hours = (toMs(rows[N - 1][4]) - toMs(rows[1][4])) / 3_600_000
  const rate = (N - 2) / hours
  const { perCheck, falseFailure } = seekBound(per.map((e) => e.toRoll), framesPerMs)
  const observed = per.flatMap((e) => (e.rollToFree === null ? [] : [{ rollToBattle: e.rollToBattle, rollToFree: e.rollToFree }]))
  const rollToFree = observed.map((e) => e.rollToFree)
  const bot = per.map((e) => e.botFrames)
  const summary = `${mode}: ${rate.toFixed(1)} an hour (encounters 2 to ${N}, ${(hours * 3600).toFixed(1)} s); ` +
    `macro to roll mean ${mean(per.map((e) => e.toRoll)).toFixed(0)} ms, shortest ${perCheck.toFixed(1)} frames a check; ` +
    `roll to battle screen mean ${mean(observed.map((e) => e.rollToBattle)).toFixed(0)} ms over ${observed.length} observed ` +
    `(${per.length - observed.length} not observed); battle mean ${mean(per.map((e) => e.frames)).toFixed(0)} frames, ` +
    `max ${Math.max(...per.map((e) => e.frames))}; roll to free field mean ${mean(rollToFree).toFixed(0)} frames, ` +
    `max ${Math.max(...rollToFree).toFixed(0)}; press frames mean ${mean(bot).toFixed(1)}, max ${Math.max(...bot).toFixed(1)} ` +
    `(the poll's lag before each press is in no log); ${(framesPerMs * 1000).toFixed(2)} fps`
  step(summary)

  const failures: string[] = []
  if (!(rate >= 270)) failures.push(`"270 encounters an hour or better over thirty": ${summary}`)
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

test('RS Wild Encounter speed: NFR-1 over thirty encounters by Spin, then thirty by Run with y range 1', async () => {
  test.setTimeout(40 * MINUTE)
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
    const failures: string[] = []
    const fled: [string, string[][]][] = []
    for (const [mode, seekLine, modeRows] of modes) {
      // The rate rests on the rows being these encounters', in order.
      const chunks = battles(log).filter((b) => seekLine.test(b[0])).slice(0, N)
      const pids = chunks.map((b) => parseEncounter(b.find((l) => ENCOUNTER.test(l))!).pid)
      expect(modeRows.slice(0, N).map((r) => hex8(Number(r[0]))), `"270 encounters an hour or better over thirty" (${mode}): ` +
        `the first ${N} rows are not the encounter lines'`).toEqual(pids)
      failures.push(...measure(mode, chunks, modeRows))
      fled.push([mode, chunks])
    }
    for (const [mode, chunks] of fled) {
      for (let k = 1; k < N; k++) {
        try {
          checkFlee(chunks[k], k + 1)
        } catch (e) {
          throw new Error(`${mode}: ${(e as Error).message}`)
        }
      }
      step(`${mode}: encounters 2 to ${N} fled cleanly`)
    }
    expect(failures, `NFR-1 not met:\n${failures.join('\n')}`).toEqual([])
  } finally {
    await teardown(frame, ROM)
  }
})
