import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { runDir, shotAll, startBackend, step, stopBackend, windowByTitle } from './driver'
import { backendLog, MINUTE } from './static'
import {
  applyPreset, assertLocalStores, cancelBot, cancelEventAndBot, clockConfig, launchHgss, readClock, rows, saveOf, setAnchorHere, shutdown,
  stageCopy, startFrame, startWild, subscribe, teardown, variant, WILD, writeLeadExtension, type Frame
} from './milestones'

// #525 Q1c, row hgss-rates-clock (FR-27, FR-28, FR-30, FR-32, FR-41; B4b and B4c). The card's rows change at the first
// decision past a time-of-day boundary, with no move; `20` also checks the not-live card, the night rod card and a restart.
const VARIANT = variant(['20', '4', '10'] as const)
const HANDLE = 'HG-Clock'
const ROUTE32 = '36-land'
const GOOD_ROD = '67-goodrod'
const CARD = `HGSS — ${ROUTE32} (${HANDLE})`
const ROD_CARD = `HGSS — ${GOOD_ROD} (${HANDLE})`
// HeartGold Route 32's land shares by time of day (milestones.json.gz); every other species has 0.
const TABLE: Record<string, Record<number, number>> = {
  Morning: { 69: 0.3, 19: 0.35, 179: 0.2, 187: 0.1, 41: 0.05 },
  Day: { 69: 0.3, 19: 0.4, 179: 0.2, 187: 0.1 },
  Night: { 194: 0.35, 19: 0.3, 69: 0.2, 179: 0.1, 41: 0.05 }
}
// MilestoneRows.Reason for a species with no slot at that time: Wooper 194 (night), Hoppip 187 (morning, day), Zubat 41
// (morning, night); the radio's species never have a share (FR-30).
const REASON: Record<number, string> = { 194: 'night only', 187: 'morning and day only', 41: 'morning and night only' }
const SOUND: Record<number, string> = { 293: 'needs Hoenn Sound', 264: 'needs Hoenn Sound', 418: 'needs Sinnoh Sound', 399: 'needs Sinnoh Sound' }
const NAMES: Record<number, string> = { 194: 'wooper', 187: 'hoppip', 41: 'zubat', 69: 'bellsprout' }
const CASES = {
  '20': { start: '2026-11-29T19:54:00', boundary: 20, before: 'Day', after: 'Night', live: [194], gone: [187] },
  '4': { start: '2026-11-30T03:54:00', boundary: 4, before: 'Night', after: 'Morning', live: [187], gone: [194] },
  '10': { start: '2026-11-30T09:54:00', boundary: 10, before: 'Morning', after: 'Day', live: [69], gone: [41] }
} as const
const DECIDE = /\[Orchestrator\] State: DecideOverworldAction\s*$/
const FOE = /(\S+), isShiny: (True|False)\s*$/
const CARD_POLL = 150_000

const count = (pattern: RegExp): number => backendLog().filter((l) => pattern.test(l)).length

type Seen = { live: boolean; timeOfDay: string | null; survive: number | null; rows: { species: number; share: number | null; reason: string | null }[] }
const read = async (id: string): Promise<Seen> => (await rows('HGSS', id, HANDLE)).json

function expectTime(s: Seen, time: string, what: string): void {
  expect(s.timeOfDay, `${what}: the time of day`).toBe(time)
  for (const r of s.rows) {
    expect(r.share, `${what}: species ${r.species}'s share`).toBeCloseTo(TABLE[time][r.species] ?? 0, 3)
    if (SOUND[r.species]) expect(r.reason, `${what}: species ${r.species}'s reason`).toBe(SOUND[r.species])
    else if (TABLE[time][r.species] === undefined && REASON[r.species]) expect(r.reason, `${what}: species ${r.species}'s reason`).toBe(REASON[r.species])
  }
}

async function cancelEvent(card: Locator): Promise<void> {
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name: 'Cancel', exact: true }).click()
  await card.getByRole('button', { name: 'Yes', exact: true }).click()
  await expect(card.getByRole('button', { name: WILD, exact: true })).toBeVisible({ timeout: MINUTE })
  step('event cancelled')
}

test(`HGSS rates and the clock (${VARIANT}): the rows change at the first decision past ${CASES[VARIANT].boundary}:00 with no move`, async () => {
  test.setTimeout(30 * MINUTE)
  const c = CASES[VARIANT]
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig(c.start)
    const admin = await startFrame(frame)
    expect((await applyPreset('HGSS', HANDLE, 'LivingDex')).status, 'the Living dex preset').toBeLessThan(300)
    expect((await subscribe({ game: 'HGSS', phase: ROUTE32, gameName: HANDLE })).status, 'the fixed card').toBeLessThan(300)
    const cardWindow: Page = await windowByTitle(frame.app!, CARD, MINUTE)
    const pages: Record<string, Page> = { admin, card: cardWindow }
    const card = await launchHgss(admin, HANDLE, { clock: true })
    await writeLeadExtension({ hp: 30 })

    // 1. At least 3 minutes before the boundary, or the launch took too long for this InitialTime.
    const clock = await readClock()
    const minutes = (c.boundary * 60 - (clock.hour * 60 + clock.minute) + 1440) % 1440
    step(`clock after the launch ${JSON.stringify(clock)}: ${minutes} min to ${c.boundary}:00`)
    if (minutes < 3 || minutes > 60) throw new Error(`the launch left ${minutes} min before ${c.boundary}:00 (clock ${clock.hour}:${clock.minute}); set QA_CLOCK_START earlier`)

    // 2. The hunt from where the bot stands (anchor set here on grass), so the first decision comes before the boundary.
    const set = await setAnchorHere(0, ROUTE32, 0, 1)
    expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    const runStart = await startWild(card, { phase: ROUTE32, target: 'ShinyTarget', mode: 'Run', yRange: 1 })
    await expect.poll(async () => { const s = await read(ROUTE32); return s.live && s.timeOfDay }, { timeout: 2 * MINUTE, intervals: [2000] }).toBe(c.before)
    expectTime(await read(ROUTE32), c.before, 'before the boundary')
    await shotAll('01-before', pages)

    // 3. The first decision past the boundary: the endpoint's TimeOfDay and the rows change (B4b), live.
    await expect.poll(async () => (await read(ROUTE32)).timeOfDay, { timeout: (minutes + 10) * MINUTE, intervals: [2000] }).toBe(c.after)
    const after = await read(ROUTE32)
    step(`${c.after} seen with ${count(FOE)} encounters and ${count(DECIDE)} decisions logged since the launch`)
    expect(after.live, 'not live after the boundary').toBe(true)
    expectTime(after, c.after, 'after the boundary')
    // The card draws only the species met now, each with its share under its name (PhaseCard).
    for (const species of c.live) {
      const share = `${(TABLE[c.after][species] * 100).toFixed(1)} %`
      await expect(cardWindow.locator('tbody tr', { hasText: new RegExp(NAMES[species], 'i') }).getByTestId('row-share'), `${NAMES[species]} on the card at ${share}`)
        .toHaveText(share, { timeout: CARD_POLL })
    }
    for (const species of c.gone) {
      await expect(cardWindow.locator('tbody tr', { hasText: new RegExp(NAMES[species], 'i') }), `${NAMES[species]} (${REASON[species]}) still on the card`)
        .toHaveCount(0, { timeout: CARD_POLL })
    }
    await shotAll('02-after', pages)

    if (VARIANT !== '20') {
      await cancelEventAndBot(card)
    } else {
      // 4. The event ended: not live, last seen at night, the plain night shares (FR-28).
      await cancelEvent(card)
      await expect.poll(async () => (await read(ROUTE32)).live, { timeout: 10_000, intervals: [1000] }).toBe(false)
      expectTime(await read(ROUTE32), 'Night', 'not live')
      await expect(cardWindow.getByText('not live (last seen night)')).toBeVisible({ timeout: CARD_POLL })

      // 5. The night rod card: Cherrygrove City's Good Rod with Staryu (NightFishing at night), not live (FR-32).
      expect((await subscribe({ game: 'HGSS', phase: GOOD_ROD, gameName: HANDLE })).status, 'the rod card').toBeLessThan(300)
      const rod = await read(GOOD_ROD)
      expect([rod.live, rod.timeOfDay], 'the rod card\'s live and time of day').toEqual([false, 'Night'])
      expect(rod.rows.find((r) => r.species === 120)?.share, 'Staryu\'s share at night').toBeCloseTo(0.1, 3)
      const rodWindow = await windowByTitle(frame.app!, ROD_CARD, MINUTE)
      pages.rod = rodWindow
      await expect(rodWindow.locator('tbody tr', { hasText: /staryu/i }).getByTestId('row-share')).toHaveText('10.0 %', { timeout: CARD_POLL })
      await expect(rodWindow.getByText('not live (last seen night)')).toBeVisible({ timeout: CARD_POLL })
      await shotAll('03-not-live', pages)

      // 6. The backend alone, shut down the documented way and started again with the dashboard open: nothing seen (FR-28).
      await cancelBot(card)
      fs.copyFileSync(path.join(runDir, 'backend.log'), path.join(runDir, 'backend-1.log'))
      expect(await stopBackend(frame.backend!), 'backend did not exit within 60 s of "end"').not.toBeNull()
      frame.backend = await startBackend()
      step('backend restarted; its first log is backend-1.log')
      const fresh = await read(ROUTE32)
      expect([fresh.live, fresh.timeOfDay, fresh.survive], 'the rows after the restart').toEqual([false, null, null])
      expect(fresh.rows.every((r) => r.share === null), 'a share after the restart').toBe(true)
      await expect(cardWindow.getByText('Shares: not seen since the backend started')).toBeVisible({ timeout: CARD_POLL })
      await shotAll('04-restarted', pages)
    }
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`hunt started at ${runStart}`)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
