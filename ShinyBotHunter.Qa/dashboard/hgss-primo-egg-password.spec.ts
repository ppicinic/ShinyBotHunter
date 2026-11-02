import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readMemory, readU32, runDir, shotAll,
  startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import { decodeMon, MON_SIZE } from './egg-pid'

// hgss.qa-primo: five Pokémon, facing Primo; trainer ID 60647 asks for KLUTZ, TEACHER / DARK, STEADFAST.
const ROM = 'SS-Slugma-Room'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const ANCHOR_ADDR = 0x21d4158
const Y_ADDR = 0x21da6fc
// The party, from the anchor: count at -0x23F44, records of 0xEC bytes from -0x23F40 (the spike's egg.py).
const PARTY_COUNT_OFFSET = -0x23f44
const PARTY_OFFSET = -0x23f40
// The script's special variables 0x8000 to 0x800D: [[[anchor] + 0x10] + 0x0C] + 0x8C (read table R33).
const SPECIAL_VARS_CHAIN = [0x10, 0x0c]
const SPECIAL_VARS_OFFSET = 0x8c
const ACTION = 'HGSS Primo Egg'
const PHASE = 'Primo Slugma'
const PASSWORD = [1085, 1337, 981, 1062]
const MINUTE = 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const backendLines = (pattern: RegExp): string[] =>
  fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/).filter((l) => pattern.test(l))
const crashed = (): string[] => backendLines(/\[BotService\] Event '.*' crashed/)
const hex8 = (pid: number): string => (pid >>> 0).toString(16).toUpperCase().padStart(8, '0')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

/** 0x8000 to 0x800D, hop by hop; null while a pointer on the way does not read (the script environment moves). */
async function readSpecialVars(): Promise<number[] | null> {
  try {
    let p = await readU32(ANCHOR_ADDR)
    for (const offset of SPECIAL_VARS_CHAIN) p = await readU32(p + offset)
    const raw = await readMemory(p + SPECIAL_VARS_OFFSET, 28)
    return Array.from({ length: 14 }, (_, i) => raw.readUInt16LE(2 * i))
  } catch {
    return null
  }
}

/** Waits up to timeoutMs for the first line matching pattern, failing at once on a crash line. */
async function waitForLine(pattern: RegExp, timeoutMs: number): Promise<string> {
  const deadline = Date.now() + timeoutMs
  while (true) {
    expect(crashed(), 'the event crashed; see backend.log').toEqual([])
    const line = backendLines(pattern)[0]
    if (line) return line.trim()
    if (Date.now() > deadline) throw new Error(`no ${pattern} line within ${timeoutMs / 1000} s`)
    await sleep(250)
  }
}

test('HGSS Primo Egg through the dashboard: the password entered, the script variables, outcome 5 and the egg', async () => {
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    // 1. Build the dashboard so out/ carries the current test ids.
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)

    // 2. Preconditions.
    expect(fs.existsSync(SAVE), `missing ${SAVE}`).toBe(true)
    expect(fs.existsSync(SAVE + '.bak'), `missing ${SAVE}.bak`).toBe(true)
    step(`SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')}`)

    // 3. Backend, dashboard, Bot Control.
    backend = await startBackend()
    step('backend up on localhost:5000')
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const pages: Record<string, Page> = { admin }
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()

    // 4. Launch Bot + Continue on the save facing Primo.
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const picker = card.getByRole('button', { name: ACTION, exact: true })
    await expect(picker).toBeVisible({ timeout: 5 * 60 * 1000 })
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    await shotAll('01-launched', pages)

    // 5. Run HGSS Primo Egg.
    await picker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption(PHASE)
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    await expect(picker).toHaveCount(0, { timeout: 30_000 })
    step('HGSS Primo Egg started')

    // 6. The password from the trainer ID (FR-3, FR-4).
    const passwordLine = await waitForLine(/\[PrimoEgg\] trainer ID/, 2 * MINUTE)
    step(`logged: ${passwordLine}`)
    expect(passwordLine, 'not the password for trainer ID 60647').toContain('[PrimoEgg] trainer ID 60647, password 1085 1337 / 981 1062')

    // 7. After each confirm, the scenario's own read of the script variables, then the event's line (FR-5).
    for (const prompt of [1, 2]) {
      const first = 2 * (prompt - 1)
      while (backendLines(/\[EasyChat\] confirmed/).length < prompt) {
        expect(crashed(), 'the event crashed; see backend.log').toEqual([])
        await sleep(250)
      }
      step(`prompt ${prompt} confirmed`)
      let vars: number[] | null = null
      const deadline = Date.now() + 10_000
      while (Date.now() < deadline) {
        vars = await readSpecialVars()
        if (vars && vars[first] === PASSWORD[first] && vars[first + 1] === PASSWORD[first + 1]) break
        await sleep(250)
      }
      step(`prompt ${prompt}: special variables 0x8000.. ${JSON.stringify(vars)}`)
      expect(vars?.slice(first, first + 2), `0x${(0x8000 + first).toString(16)} and the next do not read prompt ${prompt}'s words`)
        .toEqual(PASSWORD.slice(first, first + 2))
      await shotAll(`0${prompt + 1}-prompt-${prompt}`, pages)
      const after = await waitForLine(new RegExp(`\\[PrimoEgg\\] after prompt ${prompt}:`), MINUTE)
      step(`logged: ${after}`)
      const [a, b] = [(0x8000 + first).toString(16).toUpperCase(), (0x8001 + first).toString(16).toUpperCase()]
      expect(after, `the event's prompt ${prompt} line`).toContain(`0x${a}=${PASSWORD[first]} 0x${b}=${PASSWORD[first + 1]}`)
    }

    // 8. Outcome 5 (the party count after the egg), then the egg.
    const outcome = await waitForLine(/\[PrimoEgg\] outcome/, MINUTE)
    step(`logged: ${outcome}`)
    expect(outcome, 'not outcome 5').toContain('[PrimoEgg] outcome 5 (party count)')
    const eggLine = await waitForLine(/\[PrimoEgg\] egg in slot/, MINUTE)
    step(`logged: ${eggLine}`)
    const eggPid = eggLine.match(/egg in slot 6, pid=([0-9A-F]{8})/)?.[1]
    expect(eggPid, 'the egg is not in slot 6').toBeDefined()

    // 9. The egg itself, from party RAM: GET getParty may not show eggs.
    const anchor = await readU32(ANCHOR_ADDR)
    const count = (await readMemory(anchor + PARTY_COUNT_OFFSET, 1))[0]
    const egg = decodeMon(await readMemory(anchor + PARTY_OFFSET + 5 * MON_SIZE, MON_SIZE))
    step(`party RAM: count ${count}; slot 6 ${JSON.stringify({ ...egg, pid: hex8(egg.pid) })}`)
    expect(count, 'party count is not 6').toBe(6)
    expect([egg.checksumValid, egg.species, egg.isEgg, hex8(egg.pid)], 'slot 6 is not the logged Slugma egg').toEqual([true, 218, true, eggPid])
    await shotAll('04-egg', pages)

    // 10. The presses and time per prompt (FR-6): logged, not judged.
    const words = backendLines(/\[EasyChat\] slot \d, word \d+:/)
    for (const line of words) step(`backend: ${line.trim()}`)
    expect(words.length, 'not four [EasyChat] word lines').toBe(4)
    expect(backendLines(/\[EasyChat\] confirmed/).length, 'not two [EasyChat] confirmed lines').toBe(2)
    for (const prompt of [1, 2]) {
      let presses = 0
      let seconds = 0
      for (const line of words.slice(2 * (prompt - 1), 2 * prompt)) {
        const m = line.match(/\((\d+) presses, ([\d.]+) s, (built|cached)\)/)!
        presses += Number(m[1])
        seconds += Number(m[2])
      }
      step(`prompt ${prompt}: ${presses} presses, ${seconds.toFixed(1)} s`)
    }
    for (const line of backendLines(/\[EasyChat\] .* not confirmed/)) step(`backend retry: ${line.trim()}`)

    // 11. Cancel the event, then the bot; nothing was saved in game.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(picker).toBeVisible({ timeout: 60_000 })
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('event and bot cancelled; no EmuHawk running')
    await shotAll('05-cancelled', pages)
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 12. Closing Electron finalises the videos, then the documented shutdown.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)
  } finally {
    // Failure path: leave nothing running. A forced kill does not flush SaveRAM.
    await stopRecorder()
    if (app) await app.close().catch(() => undefined)
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    if (emuHawkRunning()) killEmuHawk()
    if (fs.existsSync(SAVE) && fs.existsSync(SAVE + '.bak')) {
      const same = fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak'))
      step(`after teardown: SaveRAM ${same ? 'equals' : 'DIFFERS FROM'} .bak (SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')})`)
    }
  }
})
