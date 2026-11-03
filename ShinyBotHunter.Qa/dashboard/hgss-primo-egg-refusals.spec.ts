import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU16, readU32, runDir, runMacro, shotAll,
  startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'

// hgss.qa-primo-full (six Pokémon, facing Primo) and hgss.qa-primo (five, facing Primo), both SoulSilver rev 0.
const FULL_ROM = 'SS-Slugma'
const ROOM_ROM = 'SS-Slugma-Room'
const SAVERAM = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM')
const saveOf = (rom: string): string => path.join(SAVERAM, `${rom}.SaveRAM`)
const ANCHOR_ADDR = 0x21d4158
const MAP_ID_OFFSET = -0x22da4
const X_ADDR = 0x21da6f4
const Y_ADDR = 0x21da6fc
const ACTION = 'HGSS Primo Egg'
const PHASE = 'Primo Slugma'
const FULL_SENTENCE = 'Party is full (6/6)'
const PLACE_SENTENCE = 'Stand facing Primo in the Violet City Pokémon Center: map 158, tile (12, 12), facing up.'

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const backendLines = (pattern: RegExp): string[] =>
  fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/).filter((l) => pattern.test(l))

async function readTile(): Promise<{ map: number; x: number; y: number }> {
  const map = await readU16((await readU32(ANCHOR_ADDR)) + MAP_ID_OFFSET)
  return { map, x: toS16(await readU32(X_ADDR)), y: toS16(await readU32(Y_ADDR)) }
}

test('HGSS Primo Egg through the dashboard: a full party and a player off Primo\'s tile are refused at Run', async () => {
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
    for (const rom of [FULL_ROM, ROOM_ROM]) {
      expect(fs.existsSync(saveOf(rom)), `missing ${saveOf(rom)}`).toBe(true)
      expect(fs.existsSync(saveOf(rom) + '.bak'), `missing ${saveOf(rom)}.bak`).toBe(true)
      step(`${rom}: SaveRAM sha256 ${sha256(saveOf(rom))}, .bak sha256 ${sha256(saveOf(rom) + '.bak')}`)
    }

    // 3. Backend, dashboard, Bot Control.
    backend = await startBackend()
    step('backend up on localhost:5000')
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const pages: Record<string, Page> = { admin }
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    const card = admin.getByTestId('emulator-card-0')
    const picker = card.getByRole('button', { name: ACTION, exact: true })

    const launchBot = async (rom: string, recording: string): Promise<void> => {
      await launch.locator('select').selectOption(rom)
      await launch.getByRole('button', { name: 'Start', exact: true }).click()
      await expect(picker).toBeVisible({ timeout: 5 * 60 * 1000 })
      // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch request returns.
      await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
      startEmulatorRecorder(recording)
      // The position reads -1 until the overworld has faded in.
      await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
      step(`${rom} launched: ${JSON.stringify(await readTile())}`)
    }

    // Run, then the card's red message holds the sentence; the card still offers its actions and no event started.
    const runRefused = async (name: string, sentence: string): Promise<string> => {
      await picker.click()
      await card.getByTestId('param-PhaseName').locator('select').selectOption(PHASE)
      await card.getByRole('button', { name: 'Run', exact: true }).click()
      const message = card.locator('div.text-red-400')
      await expect(message).toContainText(sentence, { timeout: 60_000 })
      const text = (await message.textContent())!.trim()
      step(`${name}: ${text}`)
      await expect(picker).toBeVisible()
      const state = await (await fetch('http://localhost:5000/api/bot/getDashboardState')).json()
      step(`${name}: dashboard state ${JSON.stringify(state)}`)
      expect(state['0']?.eventState, `${name}: an event is running`).toBe('None')
      expect(backendLines(/\[PrimoEgg\]/), `${name}: the Primo event logged`).toEqual([])
      await shotAll(name, pages)
      return text
    }

    // One Cancel removes the idle bot and closes its emulator; the save is untouched.
    const cancelBot = async (rom: string): Promise<void> => {
      await card.getByRole('button', { name: 'Cancel', exact: true }).click()
      await card.getByRole('button', { name: 'Yes', exact: true }).click()
      await expect(card).toHaveCount(0, { timeout: 60_000 })
      await stopRecorder()
      await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
      expect(fs.readFileSync(saveOf(rom)).equals(fs.readFileSync(saveOf(rom) + '.bak')), `${rom} SaveRAM differs from its .bak`).toBe(true)
      step(`${rom}: bot cancelled, no EmuHawk, SaveRAM equals .bak (sha256 ${sha256(saveOf(rom))})`)
    }

    // 4. A full party (FR-2): the save stands facing Primo, so only the party check can refuse.
    await launchBot(FULL_ROM, 'refusal-full')
    await shotAll('01-full-launched', pages)
    await runRefused('02-full-party', FULL_SENTENCE)
    await cancelBot(FULL_ROM)

    // 5. Off Primo's tile (FR-2): one step down, then the exact sentence.
    await launchBot(ROOM_ROM, 'refusal-place')
    await runMacro(`M.Down.13.${Y_ADDR}`)
    const tile = await readTile()
    step(`stepped off Primo's tile: ${JSON.stringify(tile)}`)
    expect(tile, 'the step down did not land on map 158 (12, 13)').toEqual({ map: 158, x: 12, y: 13 })
    await shotAll('03-off-tile', pages)
    expect(await runRefused('04-wrong-place', PLACE_SENTENCE), 'the wrong-place message is not exactly the sentence').toBe(PLACE_SENTENCE)
    await cancelBot(ROOM_ROM)

    // 6. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 7. The documented shutdown.
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
    for (const rom of [FULL_ROM, ROOM_ROM]) {
      if (fs.existsSync(saveOf(rom)) && fs.existsSync(saveOf(rom) + '.bak')) {
        const same = fs.readFileSync(saveOf(rom)).equals(fs.readFileSync(saveOf(rom) + '.bak'))
        step(`after teardown: ${rom} SaveRAM ${same ? 'equals' : 'DIFFERS FROM'} .bak (SaveRAM sha256 ${sha256(saveOf(rom))})`)
      }
    }
  }
})
