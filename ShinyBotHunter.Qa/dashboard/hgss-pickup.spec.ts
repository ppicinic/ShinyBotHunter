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
  ABILITY_PICKUP, checkDecisions, fillPickupForm, parseHeld, parseTrip, pickupHolders, PICKUP_HELD, PICKUP_STOP, readPartyRam,
  type Held
} from './pickup'

// hgss.qa-pickup: Typhlosion Lv 37 leading, five Aipom with Pickup in slots 2 to 6, nothing held.
const ROM = 'HG-Pickup'
const SAVE = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const LEAD_NAME = 'Typhlosion'
const PICKUP_SLOTS = [2, 3, 4, 5, 6]
const PHASE_TITLE = 'HGSS — Route 33'
const MINUTE = 60 * 1000
// The epic's stop rule: three trips or 30 won battles, whichever comes first.
const TRIPS = 3
const BATTLES = 30

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

type PartySlot = { slot: number; name: string; level: number; hp: number; maxHp: number; isShiny: boolean }
async function readParty(): Promise<PartySlot[]> {
  const res = await fetch('http://localhost:5000/api/bot/getParty/0')
  return res.ok ? ((await res.json()) as PartySlot[]) : []
}

test('HGSS Pickup farm on Route 33: a count line after every won battle, every trip takes every item, cancel', async () => {
  test.setTimeout(45 * MINUTE)
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
    await admin.getByRole('button', { name: 'Dashboard Config', exact: true }).click()
    const subscriptions = admin.getByRole('heading', { name: 'Active Subscriptions' }).locator('..').locator('li')
    await admin.waitForTimeout(2000) // the subscription list arrives over SignalR after load
    if (!(await subscriptions.allTextContents()).some((t) => t.trim() === 'HGSSRoute 33Remove')) {
      const add = admin.getByRole('heading', { name: 'Add Subscription' }).locator('..')
      await add.locator('select').nth(0).selectOption('HGSS')
      await add.locator('select').nth(1).selectOption('Route 33')
      await add.locator('select').nth(2).selectOption('')
      await add.getByRole('button', { name: 'Add', exact: true }).click()
      step('subscribed HGSS / Route 33 through Dashboard Config')
    }
    let phase: Page
    try {
      phase = await windowByTitle(app, PHASE_TITLE, 60_000)
    } catch {
      throw new Error(`no "${PHASE_TITLE}" window; check that HGSS / Route 33 is subscribed in Dashboard Config`)
    }
    const pages = { admin, recent, phase }
    const countEl = phase.getByTestId('phase-encounter-count')
    const encounterCount = async (): Promise<number> => Number((await countEl.textContent())!.replace(/\D/g, ''))
    // The count reads 0 until the phase info and encounters arrive together; the loading line goes when they do.
    await expect(countEl).toBeVisible({ timeout: 60_000 })
    await expect(phase.getByText('Loading milestone requirements...')).toHaveCount(0, { timeout: 60_000 })
    const before = await encounterCount()
    step(`windows attached; Route 33 encounters before: ${before}`)

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

    // 7. The party this save was made with: the lead, and five Pickup members holding nothing.
    await expect.poll(async () => (await readParty()).length, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(0)
    const party = await readParty()
    step(`party: ${JSON.stringify(party)}`)
    expect(party.length, `${SAVE} does not hold six Pokémon`).toBe(6)
    expect(party[0].name, `${SAVE} does not hold the expected lead`).toBe(LEAD_NAME)
    const ram = await readPartyRam()
    step(`party RAM: ${JSON.stringify(ram)}`)
    expect(ram.filter((m) => m.ability === ABILITY_PICKUP).map((m) => m.slot), `${SAVE}'s Pickup members`).toEqual(PICKUP_SLOTS)
    expect(pickupHolders(ram), `${SAVE}'s Pickup members holding an item`).toEqual([])

    // 8. Wild Encounter in train mode to level 101 (no end) with Pickup on.
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
    await shotAll('03-farming', pages)

    // 9. Watch backend.log every 250 ms until three trips or 30 won battles. A 30th decision that is a trip runs on
    // to its trip line, so the cancel never lands inside the menu. Fails at once on a crash, a stop, a stale wait, a
    // park, or 3 minutes without a BattleComplete (a stall).
    let scanned = from
    let battles = 0
    let trips = 0
    let afterBattle = false
    let lastBattleAt = Date.now()
    let last: Held | null = null
    let endAt = -1
    while (endAt < 0) {
      // The last element is a partial line, or the empty string after the final newline; it is read on a later poll.
      const log = backendLog()
      for (let i = scanned; i < log.length - 1 && endAt < 0; i++) {
        const line = log[i]
        if (CRASHED.test(line)) throw new Error(`the event crashed: ${line}`)
        if (PICKUP_STOP.test(line)) throw new Error(`the trip stopped: ${line}`)
        if (line.includes('GameStateTimeoutException')) throw new Error(`a stale wait: ${line}`)
        if (line.includes('[Orchestrator] State: BattleComplete')) {
          afterBattle = true
          lastBattleAt = Date.now()
        }
        const held = parseHeld(line)
        if (held) {
          last = held
          if (afterBattle) {
            battles++
            afterBattle = false
            step(`battle ${battles}: ${line}`)
          }
          if (battles >= BATTLES && !held.trip) endAt = i
        }
        const ms = parseTrip(line)
        if (ms !== null) {
          trips++
          const now = await readPartyRam()
          step(`trip ${trips}: ${ms} ms; party RAM ${JSON.stringify(now)}`)
          expect(ms, `trip ${trips} took 15 s or more (NFR-1)`).toBeLessThan(15_000)
          expect(pickupHolders(now), `Pickup members still holding an item after trip ${trips}`).toEqual([])
          if (trips >= TRIPS || battles >= BATTLES) endAt = i
        }
      }
      scanned = Math.max(scanned, log.length - 1)
      if (endAt >= 0) break
      if (await resume.isVisible()) throw new Error(`the event parked: ${backendLines(/\[Pickup\]/, from).at(-1) ?? 'no [Pickup] line'}`)
      if (Date.now() - lastBattleAt > 3 * MINUTE) {
        for (const line of backendLines(/\[Orchestrator\] State:|\[Pickup\]|\[LevelUp\]/, from).slice(-20)) step(`backend: ${line}`)
        await shotAll('04-stuck', pages)
        throw new Error(`no BattleComplete for 3 min after ${battles} won battles`)
      }
      await sleep(250)
    }
    const endedOnTrip = parseTrip(backendLog()[endAt]) !== null

    // 10. Cancel the event at once: a battle takes about 30 s, so no new roll lands before the memory check.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    step(`event cancelled; ${trips} trips in ${battles} won battles, ended on ${endedOnTrip ? 'a trip' : `${BATTLES} battles`}`)
    await shotAll('05-event-cancelled', pages)

    // 11. Every decision from the Run click to the end line: one count line after every battle; a trip takes exactly
    // the count it logged, in ascending Pickup slots, and logs its time before any battle starts; a no-trip has
    // fewer held than members (PickupTakeAt 0).
    const log = backendLog().slice(0, endAt + 1)
    const first = parseHeld(log.slice(from).find((l) => PICKUP_HELD.test(l))!)
    expect(first, 'the first decision').toEqual({ held: 0, members: 5, trip: false })
    const decisions = checkDecisions(log, from)
    expect(decisions.length, 'decisions after a battle').toBe(battles)
    decisions.forEach((d, i) => {
      const name = `decision ${i + 1} (${d.lines.join(' | ')})`
      expect(d.held.members, `${name}: Pickup members`).toBe(5)
      if (d.held.trip) {
        const slots = d.took.map((t) => t.slot)
        expect(slots.length, `${name}: takes`).toBe(d.held.held)
        expect(slots, `${name}: slots not ascending`).toEqual([...slots].sort((a, b) => a - b))
        expect(slots.every((s) => PICKUP_SLOTS.includes(s)), `${name}: a take from a slot without Pickup`).toBe(true)
        expect(d.tripMs, `${name}: no trip line`).not.toBeNull()
        const afterHeld = d.lines.slice(d.lines.findIndex((l) => PICKUP_HELD.test(l)))
        expect(afterHeld.filter((l) => l.includes('[Orchestrator] State: BattleStart')), `${name}: a battle in the trip`).toEqual([])
      } else {
        expect(d.held.held, `${name}: no trip with every member holding`).toBeLessThan(d.held.members)
        expect(d.took, `${name}: a take without a trip`).toEqual([])
      }
    })
    expect(backendLines(PICKUP_STOP, from), 'a [Pickup] stop').toEqual([])
    expect(backendLines(CRASHED, from), 'a crash').toEqual([])

    // 12. The party after the cancel: the same members, and memory agrees with the last decision.
    const partyAfter = await readParty()
    step(`party after: ${JSON.stringify(partyAfter)}`)
    expect(partyAfter.map((p) => p.name), 'the party changed').toEqual(party.map((p) => p.name))
    const ramAfter = await readPartyRam()
    step(`party RAM after the cancel: ${JSON.stringify(ramAfter)}`)
    const heldNow = pickupHolders(ramAfter).length
    expect(heldNow, `Pickup members holding an item, against the last decision ${JSON.stringify(last)}`).toBe(endedOnTrip ? 0 : last!.held)
    await expect.poll(encounterCount, { timeout: 60_000, intervals: [1000] }).toBeGreaterThan(before)
    step(`Route 33 encounters now: ${await encounterCount()}`)
    step(`trip count: ${trips}`)

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
