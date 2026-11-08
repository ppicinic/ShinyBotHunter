import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU32, runDir, runMacro,
  shotAll, startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import { backendLines, backendLog, CRASHED, Y_ADDR } from './headbutt'
import {
  ABILITY_PICKUP, fillPickupForm, parseHeld, parseTook, parseTrip, pickupHolders, PICKUP_STOP, PICKUP_TRIP, readPartyRam
} from './pickup'

// hgss.qa-pickup-held: Typhlosion Lv 37 leading, five Aipom with Pickup in slots 2 to 6, each holding an item.
const ROM = 'HG-Pickup-Held'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const LEAD_NAME = 'Typhlosion'
const PICKUP_SLOTS = [2, 3, 4, 5, 6]
const MINUTE = 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

type PartySlot = { slot: number; name: string; level: number; hp: number; maxHp: number; isShiny: boolean }
async function readParty(): Promise<PartySlot[]> {
  const res = await fetch('http://localhost:5000/api/bot/getParty/0')
  return res.ok ? ((await res.json()) as PartySlot[]) : []
}
const namesAndLevels = (party: PartySlot[]): string[] => party.map((p) => `${p.slot} ${p.name} Lv ${p.level}`)

test('HGSS Pickup with five items in hand: the first decision is a trip that takes all five in slot order, cancel', async () => {
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

    // 3. Backend.
    backend = await startBackend()
    step('backend up on localhost:5000')

    // 4. Dashboard and its windows.
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const recent = await windowByTitle(app, 'Pokemon Dashboard — Recent Encounters', 60_000)
    const pages = { admin, recent }
    step('windows attached')

    // 5. Launch the bot through the top-level action.
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const wildPicker = card.getByRole('button', { name: 'Wild Encounter', exact: true })
    const resume = card.getByText('Resume', { exact: true })
    await expect(wildPicker).toBeVisible({ timeout: 5 * 60 * 1000 })
    // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch request returns.
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await shotAll('01-launched', pages)

    // 6. Two tiles down from the path into the grass. The position reads -1 until the overworld has faded in.
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    const y = toS16(await readU32(Y_ADDR))
    await runMacro(`M.Down.${y + 2}.${Y_ADDR}`)
    expect(toS16(await readU32(Y_ADDR))).toBe(y + 2)
    step(`player moved from y=${y} to y=${y + 2}`)
    await shotAll('02-in-grass', pages)

    // 7. The party this save was made with: the lead, and five Pickup members each holding an item. Those slots
    // and item ids are the takes expected.
    await expect.poll(async () => (await readParty()).length, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(0)
    const party = await readParty()
    step(`party: ${JSON.stringify(party)}`)
    expect(party.length, `${SAVE} does not hold six Pokémon`).toBe(6)
    expect(party[0].name, `${SAVE} does not hold the expected lead`).toBe(LEAD_NAME)
    const ram = await readPartyRam()
    step(`party RAM: ${JSON.stringify(ram)}`)
    expect(ram.filter((m) => m.ability === ABILITY_PICKUP).map((m) => m.slot), `${SAVE}'s Pickup members`).toEqual(PICKUP_SLOTS)
    expect(pickupHolders(ram), `${SAVE}'s Pickup members holding an item`).toEqual(PICKUP_SLOTS)
    const expected = PICKUP_SLOTS.map((slot) => ({ slot, item: ram[slot - 1].heldItem }))

    // 8. Wild Encounter in train mode with Pickup on.
    await wildPicker.click()
    const { label, disabled } = await fillPickupForm(card, { pickup: true })
    step(`Pickup checkbox: "${label}", disabled ${disabled}`)
    expect(label, 'the Pickup label carries a reason').toBe('Pickup')
    // The last element is the empty string after the final newline; the first line after the click replaces it.
    const from = backendLog().length - 1
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step('Wild Encounter started: train Standard to level 101, Pickup on')
    // The event is running; otherwise the waits below could pass on the idle card.
    await expect(wildPicker).toHaveCount(0, { timeout: 30_000 })
    await shotAll('03-started', pages)

    // 9. Watch backend.log until the first trip line; fail at once on a crash, a stop, a stale wait or a park.
    let tripAt = -1
    const deadline = Date.now() + 3 * MINUTE
    while (tripAt < 0) {
      const log = backendLog()
      const lines = log.slice(from, log.length - 1)
      for (const line of lines) {
        if (CRASHED.test(line)) throw new Error(`the event crashed: ${line}`)
        if (PICKUP_STOP.test(line)) throw new Error(`the trip stopped: ${line}`)
        if (line.includes('GameStateTimeoutException')) throw new Error(`a stale wait: ${line}`)
      }
      const i = lines.findIndex((l) => PICKUP_TRIP.test(l))
      if (i >= 0) tripAt = from + i
      else if (await resume.isVisible()) throw new Error(`the event parked: ${backendLines(/\[Pickup\]/, from).at(-1) ?? 'no [Pickup] line'}`)
      else if (Date.now() > deadline) throw new Error(`no "[Pickup] trip took" line within 3 min: ${backendLines(/\[Pickup\]/, from).join(' | ')}`)
      else await sleep(250)
    }
    // At once, before a battle can end: every Pickup member empty-handed in RAM.
    const afterTrip = await readPartyRam()
    step(`party RAM after the trip: ${JSON.stringify(afterTrip)}`)
    await shotAll('04-trip-done', pages)

    // 10. The first decision is a trip before any encounter, its five takes in slot order with the save's items.
    const lines = backendLog().slice(from, tripAt + 1)
    const pickupLines = lines.filter((l) => l.includes('[Pickup]'))
    for (const line of pickupLines) step(`backend: ${line}`)
    expect(parseHeld(pickupLines[0]), `the first [Pickup] line: ${pickupLines[0]}`).toEqual({ held: 5, members: 5, trip: true })
    expect(lines.filter((l) => /\[Orchestrator\] State: (Encountering|BattleStart)/.test(l)), 'an encounter before the trip').toEqual([])
    const took = lines.map(parseTook).filter((t) => t !== null)
    expect(took.map((t) => ({ slot: t.slot, item: t.item })), 'the takes, in order').toEqual(expected)
    const ms = parseTrip(lines.at(-1)!)!
    step(`trip took ${ms} ms`)
    expect(ms, 'the trip took 15 s or more (NFR-1)').toBeLessThan(15_000)
    expect(pickupHolders(afterTrip), 'Pickup members still holding an item after the trip').toEqual([])

    // 11. The event's own post-trip read passed: the decision goes on to Encountering with no stop.
    await expect.poll(() => backendLines(/\[Orchestrator\] State: Encountering|\[Pickup\] Pickup: /, tripAt).length,
      { timeout: 30_000, intervals: [250] }).toBeGreaterThan(0)
    expect(backendLines(PICKUP_STOP, tripAt), 'a stop after the trip').toEqual([])

    // 12. Cancel the event; no slot moved and nothing is held.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    step('event cancelled')
    const partyAfter = await readParty()
    step(`party after: ${JSON.stringify(partyAfter)}`)
    expect(namesAndLevels(partyAfter), 'the party changed').toEqual(namesAndLevels(party))
    const ramAfter = await readPartyRam()
    step(`party RAM after the cancel: ${JSON.stringify(ramAfter)}`)
    expect(pickupHolders(ramAfter), 'Pickup members holding an item after the cancel').toEqual([])
    await shotAll('05-event-cancelled', pages)

    // 13. One more cancel removes the bot and closes the emulator.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('06-bot-cancelled', pages)

    // 14. Nothing was saved in game.
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 15. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 16. The documented shutdown.
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
    // On every outcome: a forced kill must not have changed the save either.
    if (fs.existsSync(SAVE) && fs.existsSync(SAVE + '.bak')) {
      const same = fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak'))
      step(`after teardown: SaveRAM ${same ? 'equals' : 'DIFFERS FROM'} .bak (SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')})`)
    }
  }
})
