import * as fs from 'fs'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { emuHawkRunning, killEmuHawk, killTree, readU32, runMacro, shotAll, step, toS16 } from './driver'
import { failOnStop, SEEK } from './rs-wild'
import { API, backendLog, launchBot, MINUTE, psql, ROUTE33_ROM, saveOf, settings, sha256, shutdown, startFrame, type Frame } from './static'

// hgss.qa-route33, as hgss-route33 uses it: two tiles down from the path into the grass, then Run with x range 3, or Spin.
const SAVE = saveOf(ROUTE33_ROM)
const Y_ADDR = 0x21da6fc
const PHASE = 'Route 33'
const ACTION = 'Wild Encounter'
// Each mode hunts this long by the wall clock. The first encounter of a mode is left out, since it follows the launch.
const MINUTES = Number(process.env.QA_HGSS_SPEED_MINUTES ?? 20)
const toMs = (text: string): number => Date.parse(text.replace(' ', 'T').replace(/([+-]\d\d)$/, '$1:00'))
const mean = (v: number[]): number => v.reduce((sum, x) => sum + x, 0) / v.length
// Owner, 2026-11-16: "for a grass route run should be 189~ encounters per hour with a shiny lead. within margin is ok."
// The floor is 189 less about 5%. Run is asserted; Spin is reported only.
const TARGET = 189
const FLOOR = 180

type Mode = { mode: 'Run'; xRange: number } | { mode: 'Spin' }
type PartySlot = { slot: number; name: string; level: number; isShiny: boolean }

async function cancelOnce(card: Locator): Promise<void> {
  // The operator may have switched the Admin window to another tab during the run; the card is only on Bot Control.
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name: 'Cancel', exact: true }).click()
  await card.getByRole('button', { name: 'Yes', exact: true }).click()
}

/**
 * Launch Bot + Continue (a new EmuHawk, so no battle is left on screen) with no window recorder, two tiles down into the
 * grass, the lead from the party endpoint with the bot idle (before Run only), Wild Encounter, MINUTES of hunting with
 * nothing touching the emulator (only backend.log, a file, is read, to fail early on a stop or a crash), two cancels, and
 * the save against its .bak once EmuHawk has closed. Returns the ISO time of the Run click.
 */
async function hunt(admin: Page, mode: Mode, pages: Record<string, Page>): Promise<string> {
  const card = await launchBot(admin, ROUTE33_ROM, 'emulator', false)
  const y = toS16(await readU32(Y_ADDR))
  await runMacro(`M.Down.${y + 2}.${Y_ADDR}`)
  expect(toS16(await readU32(Y_ADDR)), 'the player did not move two tiles down into the grass').toBe(y + 2)
  step(`player moved from y=${y} to y=${y + 2}`)
  if (mode.mode === 'Run') {
    const party = (await (await fetch(`${API}/bot/getParty/0`)).json()) as PartySlot[]
    step(`lead ${party[0].name} Lv ${party[0].level}, shiny ${party[0].isShiny} (the owner's ${TARGET} an hour is for a shiny lead)`)
  }

  const picker = card.getByRole('button', { name: ACTION, exact: true })
  await picker.click()
  const phase = card.getByTestId('param-PhaseName').locator('select')
  try {
    await expect(phase.locator(`option[value="${PHASE}"]`)).toHaveCount(1, { timeout: 30_000 })
  } catch {
    throw new Error(`no HGSS \`${PHASE}\` milestone is offered; write it to Mongo (qa-automation.md, Phases)`)
  }
  await phase.selectOption(PHASE)
  await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
  await card.getByTestId('param-EncounterType').locator('select').selectOption(mode.mode)
  if (mode.mode === 'Run') await card.getByTestId('param-xRange').locator('input').fill(String(mode.xRange))
  await card.getByTestId('param-TrainType').locator('select').selectOption('None')
  const runStart = new Date().toISOString()
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  await expect(picker).toHaveCount(0, { timeout: 30_000 })
  step(`${ACTION} ${mode.mode} started at ${runStart}`)

  const end = Date.now() + MINUTES * MINUTE
  const check = (): void => {
    try {
      failOnStop(backendLog())
    } catch (e) {
      throw new Error(`"no stop or crash line" (${mode.mode}): ${(e as Error).message}`)
    }
  }
  while (Date.now() < end) {
    check()
    await new Promise((resolve) => setTimeout(resolve, Math.min(5000, end - Date.now())))
  }
  check()
  step(`${mode.mode}: hunted ${MINUTES} minutes`)
  await shotAll(`${mode.mode.toLowerCase()}-hunting`, pages)

  await cancelOnce(card)
  await expect(picker).toBeVisible({ timeout: MINUTE })
  step('event cancelled')
  await cancelOnce(card)
  await expect(card).toHaveCount(0, { timeout: MINUTE })
  await expect.poll(emuHawkRunning, { timeout: MINUTE }).toBe(false)
  step('bot cancelled; no EmuHawk running')
  const same = fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak'))
  step(`${mode.mode}: SaveRAM ${same ? 'equals' : 'differs from'} .bak (SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')})`)
  expect(same, `"the save equals its .bak afterwards" (${mode.mode}): SaveRAM differs from its .bak`).toBe(true)
  return runStart
}

/**
 * One mode's rate over its rows from the second, by Postgres time, and its mean seek from the `seek:` lines from the
 * second, written to steps.log whatever they are. HGSS logs no frame count, so nothing is in frames.
 */
function measure(mode: string, rows: string[][], log: string[]): number {
  const used = rows.slice(1)
  // Fewer than two rows leave the rate NaN, which the Run check names as unmet.
  const seconds = used.length >= 2 ? (toMs(used[used.length - 1][4]) - toMs(used[0][4])) / 1000 : NaN
  const rate = ((used.length - 1) * 3600) / seconds
  const seeks = log.flatMap((l) => {
    const m = SEEK.exec(l)
    return m && m[1] === mode ? [{ toRoll: Number(m[2]), rollToBattle: Number(m[3]) }] : []
  }).slice(1)
  // A roll to battle screen of 0 is "not observed": no poll saw the field leave free before the battle screen.
  const observed = seeks.filter((s) => s.rollToBattle > 0)
  step(`${mode}: ${rate.toFixed(1)} an hour, target ${TARGET} (${used.length} rows after the first, over ${seconds.toFixed(1)} s, ` +
    `${used[0]?.[4]} to ${used[used.length - 1]?.[4]}); seek mean over ${seeks.length} seeks after the first: macro to roll ` +
    `${mean(seeks.map((s) => s.toRoll)).toFixed(0)} ms, roll to battle screen ${mean(observed.map((s) => s.rollToBattle)).toFixed(0)} ms ` +
    `over ${observed.length} observed (${seeks.length - observed.length} not observed)`)
  return rate
}

test('HGSS Wild Encounter speed: a timed hunt on Route 33 by Run with x range 3, then one by Spin, against 189 an hour', async () => {
  test.setTimeout((2 * MINUTES + 15) * MINUTE)
  const frame: Frame = { backend: null, app: null }
  const { pg } = settings()
  // Before the try, so the finally block never kills an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(fs.existsSync(SAVE), `missing ${SAVE}`).toBe(true)
  expect(fs.existsSync(SAVE + '.bak'), `missing ${SAVE}.bak`).toBe(true)
  step(`SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')}`)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const runStart = await hunt(admin, { mode: 'Run', xRange: 3 }, pages)
    const spinStart = await hunt(admin, { mode: 'Spin' }, pages)
    await shutdown(frame, admin)

    const rows = psql(pg, `SELECT "PID","Species","Level","IsShiny","Timestamp" FROM encounters WHERE "GameFileName"='${ROUTE33_ROM}' ` +
      `AND "Milestone"='${PHASE}' AND "Timestamp" >= '${runStart}' ORDER BY "Timestamp"`).map((r) => r.split('|'))
    step(`encounter rows since the Run: ${rows.length}`)
    const log = backendLog()
    // Both summaries go to steps.log before the check can throw.
    const runRate = measure('Run', rows.filter((r) => toMs(r[4]) < Date.parse(spinStart)), log)
    const spinRate = measure('Spin', rows.filter((r) => toMs(r[4]) >= Date.parse(spinStart)), log)
    if (runRate < TARGET && runRate >= FLOOR) step(`Run: ${runRate.toFixed(1)} an hour is under ${TARGET} and inside the margin of ${FLOOR}`)
    step(`Spin: ${spinRate.toFixed(1)} an hour, reported only`)
    expect(runRate >= FLOOR, `"${TARGET} encounters an hour by Run, within margin" (floor ${FLOOR}): Run made ${runRate.toFixed(1)}`).toBe(true)
  } finally {
    // Failure path: leave nothing running. A forced kill does not flush SaveRAM.
    if (frame.app) await frame.app.close().catch(() => undefined)
    if (frame.backend && frame.backend.exitCode === null) killTree(frame.backend.pid!)
    if (emuHawkRunning()) killEmuHawk()
  }
})
