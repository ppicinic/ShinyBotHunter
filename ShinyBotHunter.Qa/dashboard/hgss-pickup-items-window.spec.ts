import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
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
// Potion, Super Potion, Hyper Potion, Escape Rope, Rare Candy, in slot order.
const HELD_ITEMS = [17, 26, 25, 78, 50]
const PICKUP_TITLE = `Pokemon Dashboard — Pickup: ${ROM}`
// The first visit's tally in the GET's order: count, then name by ordinal.
const FIRST_TALLY = [
  { id: 78, name: 'Escape Rope', count: 1 },
  { id: 25, name: 'Hyper Potion', count: 1 },
  { id: 17, name: 'Potion', count: 1 },
  { id: 50, name: 'Rare Candy', count: 1 },
  { id: 26, name: 'Super Potion', count: 1 }
]
// With five empty Aipom about 41 % of won battles give an item, so ten with none is about 0.5 %.
const MAX_BATTLES = 10
const MINUTE = 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

type PartySlot = { slot: number; name: string; level: number; hp: number; maxHp: number; isShiny: boolean }
async function readParty(): Promise<PartySlot[]> {
  const res = await fetch('http://localhost:5000/api/bot/getParty/0')
  return res.ok ? ((await res.json()) as PartySlot[]) : []
}

type Tally = { gameFileName: string; total: number; items: { id: number; name: string; count: number }[] }
async function readTally(): Promise<Tally[]> {
  const res = await fetch('http://localhost:5000/api/bot/getPickupItems')
  expect(res.ok, `GET getPickupItems returned ${res.status}`).toBe(true)
  const body = (await res.json()) as Tally[]
  step(`GET getPickupItems: ${JSON.stringify(body)}`)
  return body
}

// The titles of the open Pickup windows; a closing page can throw on title(), so closed ones are skipped.
async function pickupTitles(app: ElectronApplication): Promise<string[]> {
  const titles: string[] = []
  for (const page of app.windows()) {
    if (page.isClosed()) continue
    const title = await page.title()
    if (title.startsWith('Pokemon Dashboard — Pickup:')) titles.push(title)
  }
  return titles
}

// The Pickup window's rows as [name, count], the Total row last.
const windowRows = (pickup: Page): Promise<string[][]> =>
  pickup.locator('tbody tr').evaluateAll((rows) => rows.map((r) => Array.from(r.querySelectorAll('td')).map((td) => (td.textContent ?? '').trim())))
const tallyRows = (tally: Tally): string[][] => [...tally.items.map((i) => [i.name, String(i.count)]), ['Total', String(tally.total)]]

test('HGSS Pickup items window: off at start, on after the first visit shows the five takes, a later take is pushed into it, off closes it', async () => {
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    // 1. Build the dashboard so out/ carries the current markup.
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

    // 5. The switch is off at start and no Pickup window is open.
    expect(await pickupTitles(app), 'a Pickup window at start').toEqual([])
    await admin.getByRole('button', { name: 'Dashboard Config', exact: true }).click()
    const pickupSwitch = admin.getByLabel('Pickup items windows', { exact: true })
    await expect(pickupSwitch).not.toBeChecked()
    await shotAll('01-switch-off', pages)

    // 6. Launch the bot through the top-level action; the bot running opens no window.
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
    await shotAll('02-launched', pages)
    expect(await pickupTitles(app), 'a Pickup window after the launch').toEqual([])

    // 7. Two tiles down from the path into the grass. The position reads -1 until the overworld has faded in.
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    const y = toS16(await readU32(Y_ADDR))
    await runMacro(`M.Down.${y + 2}.${Y_ADDR}`)
    expect(toS16(await readU32(Y_ADDR))).toBe(y + 2)
    step(`player moved from y=${y} to y=${y + 2}`)

    // 8. The party this save was made with: the lead, and five Pickup members holding the five expected items.
    await expect.poll(async () => (await readParty()).length, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(0)
    const party = await readParty()
    step(`party: ${JSON.stringify(party)}`)
    expect(party.length, `${SAVE} does not hold six Pokémon`).toBe(6)
    expect(party[0].name, `${SAVE} does not hold the expected lead`).toBe(LEAD_NAME)
    const ram = await readPartyRam()
    step(`party RAM: ${JSON.stringify(ram)}`)
    expect(ram.filter((m) => m.ability === ABILITY_PICKUP).map((m) => m.slot), `${SAVE}'s Pickup members`).toEqual(PICKUP_SLOTS)
    expect(pickupHolders(ram), `${SAVE}'s Pickup members holding an item`).toEqual(PICKUP_SLOTS)
    expect(PICKUP_SLOTS.map((slot) => ram[slot - 1].heldItem), `${SAVE}'s held items`).toEqual(HELD_ITEMS)

    // 9. Wild Encounter in train mode with Pickup on.
    await wildPicker.click()
    await fillPickupForm(card, { pickup: true })
    // The last element is the empty string after the final newline; the first line after the click replaces it.
    const from = backendLog().length - 1
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step('Wild Encounter started: train Standard to level 101, Pickup on')
    // The event is running; otherwise the waits below could pass on the idle card.
    await expect(wildPicker).toHaveCount(0, { timeout: 30_000 })
    await shotAll('03-started', pages)

    // 10. Watch backend.log until the first visit's trip line; fail at once on a crash, a stop, a stale wait or a park.
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
    const took = backendLog().slice(from, tripAt + 1).map(parseTook).filter((t) => t !== null)
    for (const t of took) step(`took item ${t.item} from slot ${t.slot} (${t.name})`)
    expect(took.map((t) => t.item), 'the first visit\'s takes, in slot order').toEqual(HELD_ITEMS)

    // 11. Switch on: one window for the game, equal to the GET, five items at 1 and a total of 5. The hunt keeps running.
    await admin.getByRole('button', { name: 'Dashboard Config', exact: true }).click()
    await pickupSwitch.check()
    await expect(pickupSwitch).toBeChecked()
    const pickup = await windowByTitle(app, PICKUP_TITLE, 30_000)
    expect(await pickupTitles(app), 'the Pickup windows after the switch on').toEqual([PICKUP_TITLE])
    const first = await readTally()
    expect(first, 'GET getPickupItems after the first visit').toEqual([{ gameFileName: ROM, total: 5, items: FIRST_TALLY }])
    // "No items yet" shows until the window's first load has returned.
    await expect.poll(() => windowRows(pickup), { timeout: 10_000 }).toEqual(tallyRows(first[0]))
    await shotAll('04-switch-on', { admin, recent, pickup })

    // 12. Let the hunt run until a later visit takes an item, for at most MAX_BATTLES won battles; at that visit's trip
    // line, the open window must have risen by its takes through the push alone.
    let scanned = tripAt + 1
    let battles = 0
    let afterBattle = false
    let lastBattleAt = Date.now()
    const newTakes: { item: number; slot: number; name: string }[] = []
    let pushedAt = -1
    while (pushedAt < 0) {
      // The last element is a partial line, or the empty string after the final newline; it is read on a later poll.
      const log = backendLog()
      for (let i = scanned; i < log.length - 1 && pushedAt < 0; i++) {
        const line = log[i]
        if (CRASHED.test(line)) throw new Error(`the event crashed: ${line}`)
        if (PICKUP_STOP.test(line)) throw new Error(`the trip stopped: ${line}`)
        if (line.includes('GameStateTimeoutException')) throw new Error(`a stale wait: ${line}`)
        if (line.includes('[Orchestrator] State: BattleComplete')) {
          afterBattle = true
          lastBattleAt = Date.now()
        }
        if (parseHeld(line) && afterBattle) {
          battles++
          afterBattle = false
          step(`battle ${battles}: ${line}`)
        }
        const t = parseTook(line)
        if (t) {
          newTakes.push(t)
          step(`took item ${t.item} from slot ${t.slot} (${t.name})`)
        }
        if (PICKUP_TRIP.test(line)) {
          if (newTakes.length > 0) pushedAt = i
          else if (battles >= MAX_BATTLES) {
            throw new Error(`no take in ${MAX_BATTLES} won battles (about a 0.5 % chance with five empty Pickup members); rerun the scenario`)
          }
        }
      }
      scanned = Math.max(scanned, log.length - 1)
      if (pushedAt >= 0) break
      if (await resume.isVisible()) throw new Error(`the event parked: ${backendLines(/\[Pickup\]/, from).at(-1) ?? 'no [Pickup] line'}`)
      if (Date.now() - lastBattleAt > 3 * MINUTE) throw new Error(`no BattleComplete for 3 min after ${battles} won battles`)
      await sleep(250)
    }
    const counts = new Map(FIRST_TALLY.map((i) => [i.id, i.count]))
    for (const t of newTakes) counts.set(t.item, (counts.get(t.item) ?? 0) + 1)
    const second = await readTally()
    expect(second.length, 'games in GET getPickupItems').toBe(1)
    expect(second[0].total, 'the total after the later visit').toBe(5 + newTakes.length)
    expect(new Map(second[0].items.map((i) => [i.id, i.count])), 'the counts after the later visit').toEqual(counts)
    const rows = tallyRows(second[0])
    await expect.poll(() => windowRows(pickup), { timeout: 10_000 }).toEqual(rows)
    for (const t of newTakes) {
      const name = second[0].items.find((i) => i.id === t.item)!.name
      expect(rows, `the row for item ${t.item}`).toContainEqual([name, String(counts.get(t.item))])
    }
    expect(await pickupTitles(app), 'the Pickup windows after the push').toEqual([PICKUP_TITLE])
    await shotAll('05-pushed', { admin, recent, pickup })

    // 13. Switch off: the window closes and no Pickup window is left.
    const closed = pickup.waitForEvent('close', { timeout: 30_000 })
    await pickupSwitch.uncheck()
    await expect(pickupSwitch).not.toBeChecked()
    await closed
    await expect.poll(() => pickupTitles(app!), { timeout: 10_000 }).toEqual([])
    await shotAll('06-switch-off', pages)

    // 14. Cancel the event, then one more cancel removes the bot and closes the emulator.
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    step('event cancelled')
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('07-bot-cancelled', pages)

    // 15. Nothing was saved in game.
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 16. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 17. The documented shutdown.
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
