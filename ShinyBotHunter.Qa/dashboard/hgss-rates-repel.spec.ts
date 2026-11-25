import * as fs from 'fs'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { shotAll, step, windowByTitle } from './driver'
import { FOUND, SPIN_LINE } from './rs-wild'
import { backendLog, CRASHED, MINUTE, readTile } from './static'
import {
  ANCHOR_HUNT, applyPreset, assertLocalStores, cancelBot, clockConfig, launchHgss, readClock, readRepel, REPEL_CLOSED, REPEL_NOT_CLOSED, rows,
  rowsFor, saveOf, seen, setAnchorHere, shutdown, stageCopy, startFrame, startWild, subscribe, teardown, variant, WILD, writeLeadExtension,
  writeRepel, type Frame
} from './milestones'

// #525 Q1c, row hgss-rates-repel (FR-26, FR-27, FR-29; B4a to B4c). `run`: the Repel counter through a Run hunt and its
// close; `spin`: the counter unchanged across 90 s of turning.
const VARIANT = variant(['run', 'spin'] as const)
const HANDLE = 'HG-Repel'
const ROUTE32 = '36-land'
const CARD = `HGSS — ${ROUTE32} (${HANDLE})`
// HeartGold Route 32 at night (milestones.json.gz): Wooper L6 35 %, Rattata L4 30 %, Bellsprout L6 20 %, Mareep L6 10 %,
// Zubat L4 5 %. A Repel level of 5 cancels Rattata and Zubat (equal levels are kept): 65 % survive.
const NIGHT: Record<number, number> = { 194: 0.35, 19: 0.3, 69: 0.2, 179: 0.1, 41: 0.05 }
const BLOCKED: Record<number, number> = { 194: 0.35 / 0.65, 69: 0.2 / 0.65, 179: 0.1 / 0.65, 19: 0, 41: 0 }
const LEVEL6 = ['194', '69', '179']
const DECIDE = /\[Orchestrator\] State: DecideOverworldAction\s*$/
const FOE = /(\S+), isShiny: (True|False)\s*$/
// The card re-reads its rows on the rate poll's interval (60 s unless the dashboard config says less).
const CARD_POLL = 150_000

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
const count = (pattern: RegExp, from = 0): number => backendLog().slice(from).filter((l) => pattern.test(l)).length
const indexOf = (pattern: RegExp, from = 0): number => backendLog().findIndex((l, i) => i >= from && pattern.test(l))

type Shares = { live: boolean; timeOfDay: string | null; survive: number | null; share: Record<number, number | null>; reason: Record<number, string | null> }
async function shares(): Promise<Shares> {
  const json = (await rows('HGSS', ROUTE32, HANDLE)).json
  const share: Record<number, number | null> = {}
  const reason: Record<number, string | null> = {}
  for (const r of json.rows as { species: number; share: number | null; reason: string | null }[]) {
    share[r.species] = r.share
    reason[r.species] = r.reason
  }
  return { live: json.live, timeOfDay: json.timeOfDay, survive: json.survive, share, reason }
}

function expectShares(s: Shares, expected: Record<number, number>, survive: number, what: string): void {
  expect(s.timeOfDay, `${what}: the time of day`).toBe('Night')
  expect(s.survive, `${what}: the survive share`).toBeCloseTo(survive, 3)
  for (const [species, share] of Object.entries(expected)) expect(s.share[Number(species)], `${what}: species ${species}'s share`).toBeCloseTo(share, 3)
}

async function cancelEvent(card: Locator): Promise<void> {
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name: 'Cancel', exact: true }).click()
  await card.getByRole('button', { name: 'Yes', exact: true }).click()
  await expect(card.getByRole('button', { name: WILD, exact: true })).toBeVisible({ timeout: MINUTE })
  step('event cancelled')
}

test(`HGSS rates and Repel (${VARIANT}): the card follows the counter, the message closes with one A, the counter holds while turning`, async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-29T21:00:00')
    const admin = await startFrame(frame)
    expect((await applyPreset('HGSS', HANDLE, 'LivingDex')).status, 'the Living dex preset').toBeLessThan(300)
    expect((await subscribe({ game: 'HGSS', phase: ROUTE32, gameName: HANDLE })).status, 'the fixed card').toBeLessThan(300)
    const cardWindow: Page = await windowByTitle(frame.app!, CARD, MINUTE)
    const pages = { admin, card: cardWindow }
    const card = await launchHgss(admin, HANDLE, { clock: true })
    expect(await readTile(), 'not the save\'s start tile').toEqual({ map: 36, x: 474, y: 294, facing: 'up' })
    const clock = await readClock()
    expect(clock.hour >= 20 || clock.hour < 4, `the clock is not at night: ${JSON.stringify(clock)}`).toBe(true)

    if (VARIANT === 'run') {
      // 1. Set-up with the bot idle: the lead at Lv 5 (so the Repel level is 5) and full HP; the anchor here on grass, so no
      // walk spends Repel steps; then the counter.
      await writeLeadExtension({ hp: 30, level: 5 })
      const set = await setAnchorHere(0, ROUTE32, 0, 1)
      expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
      expect(set.json.anchor, 'the anchor set').toMatchObject({ map: 36, x: 474, y: 294, xRange: 0, yRange: 1 })
      const steps = Number(process.env.QA_REPEL_STEPS ?? 120)
      await writeRepel(steps)
      const decided = count(DECIDE)
      const runStart = await startWild(card, { phase: ROUTE32, mode: 'Run', yRange: 1 })
      const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
      expect(hunt.slice(2, 7), 'the anchor line').toEqual(['36', '474', '294', 'run up-down ±1', 'set on this file'])

      // 2. B4a: the first decision after a launch reads no Repel (the save's section offset is not known yet), so the
      // rows are the plain night shares, live, until the next decision.
      await seen(DECIDE, decided + 1, MINUTE)
      let first: Shares | null = null
      const firstDeadline = Date.now() + MINUTE
      while (!first) {
        const before = count(DECIDE)
        const s = await shares()
        if (count(DECIDE) !== decided + 1 || before !== decided + 1) {
          throw new Error('the second decision came before the rows of the first were read: B4a not observed this run (a rerun)')
        }
        if (s.live) first = s
        else if (Date.now() > firstDeadline) throw new Error('the rows never went live after the first decision')
        else await sleep(500)
      }
      expectShares(first, NIGHT, 1, 'the first decision')

      // 3. From the second decision (after encounter 1) the counter is read: Rattata and Zubat blocked by Repel, the rest
      // rescaled, 65 % survive; on the endpoint within a poll, on the card within its interval.
      await seen(DECIDE, decided + 2, 10 * MINUTE)
      await expect.poll(async () => (await shares()).survive, { timeout: 5000, intervals: [1000] }).toBeCloseTo(0.65, 3)
      const blocked = await shares()
      expectShares(blocked, BLOCKED, 0.65, 'the second decision')
      expect([blocked.reason[19], blocked.reason[41]], 'Rattata\'s and Zubat\'s reasons').toEqual(['blocked by Repel', 'blocked by Repel'])
      expect(blocked.live, 'not live while hunting').toBe(true)
      // On the card the blocked species are not drawn and the rest show their rescaled share under the name (PhaseCard).
      await expect(cardWindow.locator('tbody tr', { hasText: /wooper/i }).getByTestId('row-share'), 'Wooper rescaled on the card')
        .toHaveText(`${(BLOCKED[194] * 100).toFixed(1)} %`, { timeout: CARD_POLL })
      await expect(cardWindow.locator('tbody tr', { hasText: /rattata|zubat/i }), 'Rattata or Zubat on the card while blocked by Repel').toHaveCount(0)
      await shotAll('01-blocked-by-repel', pages)

      // 4. The counter runs out on the run line: one close with one A, then the hunt goes on to a battle.
      const closed = await seen(REPEL_CLOSED, 1, 20 * MINUTE)
      expect(REPEL_CLOSED.exec(closed.line)![1], 'the A presses that closed the Repel message').toBe('1')
      const closedAt = indexOf(REPEL_CLOSED)
      await expect.poll(() => count(FOE, closedAt), { timeout: 10 * MINUTE, intervals: [2000] }).toBeGreaterThanOrEqual(1)
      const battleAt = indexOf(FOE, closedAt)
      // The decision after that battle reads the counter at 0: Rattata live again at the plain night share.
      await expect.poll(() => count(DECIDE, battleAt), { timeout: 3 * MINUTE, intervals: [1000] }).toBeGreaterThanOrEqual(1)
      await expect.poll(async () => (await shares()).share[19], { timeout: 5000, intervals: [1000] }).toBeCloseTo(NIGHT[19], 3)
      const after = await shares()
      expectShares(after, NIGHT, 1, 'after the close')
      expect(after.reason[19], 'Rattata\'s reason after the close').toBeNull()
      await shotAll('02-after-the-close', pages)

      // 5. The counter is 0 with the bot idle; the logs.
      await cancelEvent(card)
      expect(await readRepel(), 'the Repel counter after the hunt').toBe(0)
      expect(count(REPEL_CLOSED), 'not exactly one Repel close').toBe(1)
      expect(count(REPEL_NOT_CLOSED), 'a Repel message did not close').toBe(0)
      await cancelBot(card)
      expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
      await shutdown(frame, admin)

      // 6. Every encounter before the close is Lv 6 (Wooper, Bellsprout, Mareep): the game's Repel was on at level 5.
      const all = rowsFor(HANDLE, ROUTE32, runStart)
      const before = all.filter((r) => r.at < closed.at)
      step(`${before.length} encounters while ${steps} Repel steps lasted (QA_REPEL_STEPS tunes it); ${all.length} in all`)
      expect(before.length, 'no encounter while the Repel lasted').toBeGreaterThanOrEqual(1)
      for (const r of before) {
        expect(LEVEL6, `species ${r.species} while the Repel lasted`).toContain(r.species)
        expect(r.level, `a Lv ${r.level} encounter while the Repel lasted`).toBe(6)
      }
    } else {
      // 1. No level or HP write: every Route 32 night slot (Lv 4 to 6) is below the Lv 9 lead, so no battle starts. The
      // anchor here with both ranges 0 is a spin.
      const set = await setAnchorHere(0, ROUTE32, 0, 0)
      expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
      expect(set.json.anchor, 'the anchor set').toMatchObject({ map: 36, x: 474, y: 294, xRange: 0, yRange: 0 })
      await writeRepel(50)
      const spins = count(SPIN_LINE)
      await startWild(card, { phase: ROUTE32, mode: 'Spin' })
      const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
      expect(hunt.slice(2, 7), 'the anchor line').toEqual(['36', '474', '294', 'spin', 'set on this file'])
      const spin = SPIN_LINE.exec((await seen(SPIN_LINE, spins + 1, 2 * MINUTE)).line)!
      const spinAt = backendLog().flatMap((l, i) => (SPIN_LINE.test(l) ? [i] : []))[spins]

      // 2. 90 s of the seek with no battle: the seek in the free field is unbounded.
      const deadline = Date.now() + 90_000
      while (Date.now() < deadline) {
        const log = backendLog().slice(spinAt)
        const bad = log.find((l) => FOUND.test(l) || CRASHED.test(l) || l.includes('GameStateTimeoutException'))
        if (bad) throw new Error(`within the 90 s of turning: ${bad}`)
        await sleep(1000)
      }
      await shotAll('01-turning', pages)
      await cancelEvent(card)

      // 3. The counter and the tile are unchanged; the turns, from the macro's frames over the 90 s (the recorder slows the
      // emulator about 5 %; the reviewer counts them on emulator.mp4).
      expect(await readRepel(), 'the Repel counter after 90 s of turning').toBe(50)
      const tile = await readTile()
      expect([tile.map, tile.x, tile.y], 'the player moved while turning').toEqual([36, 474, 294])
      const parts = spin[2].split(';')
      const frames = parts.reduce((sum, p) => sum + (p.startsWith('T.') ? 3 : p.startsWith('W.') ? Number(p.slice(2)) : 0), 0)
      const turns = Math.floor((90 * 60 * 0.95) / (frames / parts.filter((p) => p.startsWith('T.')).length))
      step(`spin macro ${spin[2]}: ${frames} frames a cycle, about ${turns} turns in 90 s`)
      expect(turns, 'fewer than 30 turns in 90 s').toBeGreaterThanOrEqual(30)
      expect(count(SPIN_LINE), 'the spin seek restarted (a battle or a park)').toBe(spins + 1)
      await cancelBot(card)
      expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
      await shutdown(frame, admin)
    }
  } finally {
    await teardown(frame)
  }
})
