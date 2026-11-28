import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { runDir, shotAll, startBackend, step, stopBackend, windowByTitle } from './driver'
import { backendLog, MINUTE } from './static'
import {
  assertLocalStores, cancelBot, cancelEventAndBot, cardWindow, clockConfig, launchHgss, phaseInfo, putMilestone, readClock, saveOf, setAnchorHere,
  shutdown, stageCopy, startFrame, startWild, subscribe, teardown, variant, WILD, writeLeadExtension, type Frame
} from './milestones'

// #525 Q1c, row hgss-rates-clock (FR-27, FR-28, FR-30, FR-32, FR-41; B4b and B4c), on the saved milestone QA Route 32 at
// Route 32 · grass (#621 R8b1). The card's rows change at the first decision past a time-of-day boundary, with no move;
// `20` also checks the not-live card, the night rod card (QA Good Rod at 67-goodrod, a card only) and a restart.
const VARIANT = variant(['20', '4', '10'] as const)
const HANDLE = 'HG-Clock'
const MILESTONE = 'QA Route 32'
const ROD = 'QA Good Rod'
const CARD = `HGSS — ${MILESTONE} (${HANDLE})`
const ROD_CARD = `HGSS — ${ROD} (${HANDLE})`
// HeartGold Route 32's land shares by time of day (milestones.json.gz); every other HeartGold species has 0.
const TABLE: Record<string, Record<number, number>> = {
  Morning: { 69: 0.3, 19: 0.35, 179: 0.2, 187: 0.1, 41: 0.05 },
  Day: { 69: 0.3, 19: 0.4, 179: 0.2, 187: 0.1 },
  Night: { 194: 0.35, 19: 0.3, 69: 0.2, 179: 0.1, 41: 0.05 }
}
// Every species of HeartGold Route 32's slots; the Living dex rows of both versions also hold SoulSilver's Ekans (23), which
// has no slot on a HeartGold file, so no share and no times there.
const HG_SPECIES = [19, 41, 69, 179, 187, 194, 264, 293, 399, 418]
// MilestoneRows.Reason for a species with no slot at that time: Wooper 194 (night), Hoppip 187 (morning, day), Zubat 41
// (morning, night); the radio's species never have a share (FR-30).
const REASON: Record<number, string> = { 194: 'night only', 187: 'morning and day only', 41: 'morning and night only' }
const SOUND: Record<number, string> = { 293: 'needs Hoenn Sound', 264: 'needs Hoenn Sound', 418: 'needs Sinnoh Sound', 399: 'needs Sinnoh Sound' }
// PhaseInfoService.Times on HeartGold: the rows not met now (hiddenNow), and the card's summary of them: the next time any of
// them is met, and their names.
const HIDDEN: Record<string, { species: number[]; next: string }> = {
  Morning: { species: [194], next: 'night' },
  Day: { species: [41, 194], next: 'night' },
  Night: { species: [187], next: 'morning' }
}
const NAMES: Record<number, string> = { 194: 'wooper', 187: 'hoppip', 41: 'zubat', 69: 'bellsprout' }
const CASES = {
  '20': { start: '2026-11-29T19:54:00', boundary: 20, before: 'Day', after: 'Night', live: [194], gone: [187] },
  '4': { start: '2026-11-30T03:54:00', boundary: 4, before: 'Night', after: 'Morning', live: [187], gone: [194] },
  '10': { start: '2026-11-30T09:54:00', boundary: 10, before: 'Morning', after: 'Day', live: [69], gone: [41] }
} as const
const DECIDE = /\[Orchestrator\] State: DecideOverworldAction\s*$/
const FOE = /(\S+), isShiny: (True|False)\s*$/
const CARD_POLL = 150_000
// A species' row on the card; the hidden rows' summary row also names species.
const SPECIES_ROW = 'tbody tr:not([data-testid="hidden-summary"])'

const count = (pattern: RegExp): number => backendLog().filter((l) => pattern.test(l)).length

type Seen = { live: boolean; timeOfDay: string | null; survive: number | null; rows: { species: number; share: number | null; reason: string | null; hiddenNow: boolean }[] }
// The card's GET phaseinfo on the handle: the location's rows pair by position with the milestone's rows.
async function read(name: string): Promise<Seen> {
  const { json } = await phaseInfo('HGSS', name, HANDLE)
  expect(json.location, `${name} on ${HANDLE}: no location rows (the copy's version not scanned?)`).not.toBeNull()
  const pokemon = json.phaseInfo.pokemon as { speciesId: number }[]
  const rows = (json.location.rows as { share: number | null; reason: string | null; hiddenNow: boolean }[])
    .map((r, i) => ({ species: pokemon[i].speciesId, share: r.share, reason: r.reason, hiddenNow: r.hiddenNow }))
  return { live: json.live, timeOfDay: json.timeOfDay, survive: json.location.survive, rows }
}

function expectTime(s: Seen, time: string, what: string): void {
  expect(s.timeOfDay, `${what}: the time of day`).toBe(time)
  for (const r of s.rows) {
    if (!HG_SPECIES.includes(r.species)) {
      expect([r.share, r.hiddenNow], `${what}: species ${r.species}, not in HeartGold's slots: its share and hiddenNow`).toEqual([null, false])
      continue
    }
    expect(r.share, `${what}: species ${r.species}'s share`).toBeCloseTo(TABLE[time][r.species] ?? 0, 3)
    if (SOUND[r.species]) expect(r.reason, `${what}: species ${r.species}'s reason`).toBe(SOUND[r.species])
    else if (TABLE[time][r.species] === undefined && REASON[r.species]) expect(r.reason, `${what}: species ${r.species}'s reason`).toBe(REASON[r.species])
    expect(r.hiddenNow, `${what}: species ${r.species}'s hiddenNow`).toBe(HIDDEN[time].species.includes(r.species))
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
    expect((await putMilestone({ game: 'HGSS', name: MILESTONE, location: '36-land', gameFiles: [HANDLE], archived: false })).status, `${MILESTONE} saved`).toBe(200)
    expect((await subscribe({ game: 'HGSS', phase: MILESTONE, gameName: HANDLE })).status, 'the fixed card').toBeLessThan(300)
    const fixed: Page = await cardWindow(frame.app!, CARD)
    const pages: Record<string, Page> = { admin, card: fixed }
    const card = await launchHgss(admin, HANDLE, { clock: true })
    await writeLeadExtension({ hp: 30 })

    // 1. At least 3 minutes before the boundary, or the launch took too long for this InitialTime.
    const clock = await readClock()
    const minutes = (c.boundary * 60 - (clock.hour * 60 + clock.minute) + 1440) % 1440
    step(`clock after the launch ${JSON.stringify(clock)}: ${minutes} min to ${c.boundary}:00`)
    if (minutes < 3 || minutes > 60) throw new Error(`the launch left ${minutes} min before ${c.boundary}:00 (clock ${clock.hour}:${clock.minute}); set QA_CLOCK_START earlier`)

    // 2. The hunt from where the bot stands (anchor set here on grass), so the first decision comes before the boundary.
    const set = await setAnchorHere(0, MILESTONE, 0, 1)
    expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    const runStart = await startWild(card, { phase: MILESTONE, target: 'ShinyTarget', mode: 'Run', yRange: 1 })
    await expect.poll(async () => { const s = await read(MILESTONE); return s.live && s.timeOfDay }, { timeout: 2 * MINUTE, intervals: [2000] }).toBe(c.before)
    expectTime(await read(MILESTONE), c.before, 'before the boundary')
    await shotAll('01-before', pages)

    // 3. The first decision past the boundary: the endpoint's TimeOfDay and the rows change (B4b), live.
    await expect.poll(async () => (await read(MILESTONE)).timeOfDay, { timeout: (minutes + 10) * MINUTE, intervals: [2000] }).toBe(c.after)
    const after = await read(MILESTONE)
    step(`${c.after} seen with ${count(FOE)} encounters and ${count(DECIDE)} decisions logged since the launch`)
    expect(after.live, 'not live after the boundary').toBe(true)
    expectTime(after, c.after, 'after the boundary')
    // The card draws only the species met now, each with its share under its name (PhaseCard).
    for (const species of c.live) {
      const share = `${(TABLE[c.after][species] * 100).toFixed(1)} %`
      await expect(fixed.locator(SPECIES_ROW, { hasText: new RegExp(NAMES[species], 'i') }).getByTestId('row-share'), `${NAMES[species]} on the card at ${share}`)
        .toHaveText(share, { timeout: CARD_POLL })
    }
    for (const species of c.gone) {
      await expect(fixed.locator(SPECIES_ROW, { hasText: new RegExp(NAMES[species], 'i') }), `${NAMES[species]} (${REASON[species]}) still on the card`)
        .toHaveCount(0, { timeout: CARD_POLL })
    }
    // The card has re-read the rows past the boundary (the shares above): the hidden rows' summary, and no "not live" while hunting.
    const summary = (await fixed.getByTestId('hidden-summary').innerText()).trim()
    step(`hidden summary: ${summary}`)
    const hidden = /^(\d+) more at\s*(morning|day|night): (.+)$/i.exec(summary)
    expect(hidden, `the hidden summary "${summary}"`).not.toBeNull()
    const names = HIDDEN[c.after].species.map((s) => NAMES[s]).sort()
    expect([Number(hidden![1]), hidden![2].toLowerCase(), hidden![3].toLowerCase().split(', ').sort()], 'the hidden rows: their count, next time and names')
      .toEqual([names.length, HIDDEN[c.after].next, names])
    await expect(fixed.getByText(/^not live/), '"not live" on the card while hunting').toHaveCount(0)
    await shotAll('02-after', pages)

    if (VARIANT !== '20') {
      await cancelEventAndBot(card, async () => {
        await expect(fixed.getByText(`not live (last seen ${c.after.toLowerCase()})`, { exact: true }), 'not live after the event').toBeVisible({ timeout: CARD_POLL })
      })
    } else {
      // 4. The event ended: not live, last seen at night, the plain night shares (FR-28).
      await cancelEvent(card)
      await expect.poll(async () => (await read(MILESTONE)).live, { timeout: 10_000, intervals: [1000] }).toBe(false)
      expectTime(await read(MILESTONE), 'Night', 'not live')
      await expect(fixed.getByText('not live (last seen night)', { exact: true })).toBeVisible({ timeout: CARD_POLL })

      // 5. The night rod card: Cherrygrove City's Good Rod with Staryu (NightFishing at night), not live (FR-32).
      expect((await putMilestone({ game: 'HGSS', name: ROD, location: '67-goodrod', gameFiles: [HANDLE], archived: false })).status, `${ROD} saved`).toBe(200)
      expect((await subscribe({ game: 'HGSS', phase: ROD, gameName: HANDLE })).status, 'the rod card').toBeLessThan(300)
      const rod = await read(ROD)
      expect([rod.live, rod.timeOfDay], 'the rod card\'s live and time of day').toEqual([false, 'Night'])
      expect(rod.rows.find((r) => r.species === 120)?.share, 'Staryu\'s share at night').toBeCloseTo(0.1, 3)
      const rodWindow = await windowByTitle(frame.app!, ROD_CARD, MINUTE)
      pages.rod = rodWindow
      await expect(rodWindow.locator('tbody tr', { hasText: /staryu/i }).getByTestId('row-share')).toHaveText('10.0 %', { timeout: CARD_POLL })
      await expect(rodWindow.getByText('not live (last seen night)', { exact: true })).toBeVisible({ timeout: CARD_POLL })
      await shotAll('03-not-live', pages)

      // 6. The backend alone, shut down the documented way and started again with the dashboard open: nothing seen (FR-28).
      await cancelBot(card)
      fs.copyFileSync(path.join(runDir, 'backend.log'), path.join(runDir, 'backend-1.log'))
      expect(await stopBackend(frame.backend!), 'backend did not exit within 60 s of "end"').not.toBeNull()
      frame.backend = await startBackend()
      step('backend restarted; its first log is backend-1.log')
      const fresh = await read(MILESTONE)
      expect([fresh.live, fresh.timeOfDay, fresh.survive], 'the rows after the restart').toEqual([false, null, null])
      expect(fresh.rows.every((r) => r.share === null), 'a share after the restart').toBe(true)
      await expect(fixed.getByText('not live', { exact: true }), 'the card not live with no time seen').toBeVisible({ timeout: CARD_POLL })
      await expect(fixed.getByTestId('row-share'), 'a share on the card after the restart').toHaveCount(0)
      await shotAll('04-restarted', pages)
    }
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`hunt started at ${runStart}`)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
