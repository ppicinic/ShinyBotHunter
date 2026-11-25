// rs-milestone-anchor (docs/specs/hunt-plans-and-milestone-builder.md, QA rows rs-milestone-anchor and rs-milestone-rows; #525
// Q1d), on SA-Wild with the pristine rs.qa-route101 save and rs-wild.ts's frame. QA_RS_VARIANT:
// - `anchor` (the default): no Hunt Plan field and the RS plan refusal; the Sapphire picker; Route 101 · grass's card at 45, 45
//   and 10 before Run; the walk to the generated anchor (4, 2) and a spin there, five fled encounters, the card live; a Version
//   exclusives goal changes the rows' wanted and keeps the three rows.
// - `no-route`: Route 110 · grass is not reachable on foot from Route 101: the RS navigator's no-route line and Waiting.
// Run lines: `npx playwright test rs-milestone-anchor`; `$env:QA_RS_VARIANT = "no-route"; npx playwright test rs-milestone-anchor`.
import * as fs from 'fs'
import * as path from 'path'
import * as zlib from 'zlib'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { repoDir, shotAll, step, windowByTitle } from './driver'
import {
  ANCHOR_HUNT, applyPreset, assertLocalStores, deleteGameFile, picker, rows, rowsFor, seen, startAction, subscribe, teardown, type Frame
} from './milestones'
import {
  battles, cancelEventAndBot, checkFlee, flashUnchanged, installPristine, launchRs, LEVELS, offered, OVER, readTile, removeCheats, SPIN_LINE,
  dashboardState, spinMacroFor, START, teardown as rsTeardown, WILD, type Tile
} from './rs-wild'
import { CLEARING, variant } from './rs-training'
import { backendLog, MINUTE, shutdown, startFrame } from './static'

const ROM = 'SA-Wild'
const VARIANT = variant(['anchor', 'no-route'] as const)
// Sapphire's generated Route 101 · grass and Route 110 · grass (Data/RS/milestones.json.gz), and the QA database's custom one.
const ROUTE101 = '16-land'
const ROUTE101_NAME = 'Route 101 · grass'
const ROUTE110 = '25-land'
const ROUTE110_NAME = 'Route 110 · grass'
const CUSTOM = 'Route 101'
// Route 101's slot weights: Wurmple 20 + 10 + 10 + 5, Zigzagoon the same, Poochyena 4 + 4 + 1 + 1.
const SHARES = [[265, 0.45], [263, 0.45], [261, 0.1]] as const
const PLAN_REFUSAL = 'Hunt plans need the in-game clock; RS has none.'
// RsNavigatorEvent.WalkToAsync, with no route to the anchor.
const NO_ROUTE = /\[Navigator\] stopped: No route back to the start tile (.+) from (.+)\.(.*)$/
const ENCOUNTERS = 5

// backend.log is written in the console's code page, so a milestone name's middle dot is matched by any one or two characters.
const logged = (name: string): RegExp => new RegExp(`^${name.replace('·', '.{1,2}')}$`)

type MilestoneRow = { species: number; name: string; wanted: number; caught: number; share: number | null; reason: string | null }
type Rows = { rows: MilestoneRow[]; survive: number | null; live: boolean }

/** Sapphire's generated milestone ids, as Milestone.Id makes them. */
function sapphireIds(): string[] {
  const file = path.join(repoDir, 'ShinyBotHunter', 'Data', 'RS', 'milestones.json.gz')
  const all = JSON.parse(zlib.gunzipSync(fs.readFileSync(file)).toString('utf8')) as Record<string, { Maps: number[]; Modality: string }[]>
  return all.Sapphire.map((m) => `${m.Maps[0]}-${m.Modality.toLowerCase()}`)
}

async function route101(): Promise<Rows> {
  const res = await rows('RS', ROUTE101, ROM)
  expect(res.status, `GET milestone/RS/${ROUTE101}/${ROM}`).toBe(200)
  return res.json as Rows
}

function expectShares(r: Rows, what: string): void {
  expect(r.rows.map((x) => x.species), `${what}: "every species is a row", Wurmple, Zigzagoon and Poochyena`).toEqual(SHARES.map(([s]) => s))
  SHARES.forEach(([species, share], i) => expect(r.rows[i].share, `${what}: the share of ${species}`).toBeCloseTo(share, 6))
}

type CardView = { headers: string[]; rows: { name: string; share: string | null; shinies: string }[]; footer: string }
async function readCard(page: Page): Promise<CardView> {
  return page.evaluate(() => {
    const t = (e: Element | null | undefined): string => (e?.textContent ?? '').replace(/\s+/g, ' ').trim()
    const card = document.querySelector('.dashboard-card')
    const headers = Array.from(document.querySelectorAll('thead th')).map(t)
    const share = headers.indexOf('Share')
    const rows = Array.from(document.querySelectorAll('tbody tr')).map((tr) => {
      const td = tr.querySelectorAll('td')
      return { name: t(td[1]).replace('✓', '').trim().toLowerCase(), share: share < 0 ? null : t(td[share]), shinies: t(td[td.length - 1]).replace(/\s+/g, '').replace(/\(\d+missed\)$/, '') }
    })
    return { headers, rows, footer: t(card?.lastElementChild) }
  })
}
// The card's Share cell and last cell for a server row (PhaseCard).
const drawnShares = (r: Rows): string[] => r.rows.map((x) => `${x.name.toLowerCase()} ${x.share === 0 && x.reason !== null ? x.reason : `${((x.share ?? 0) * 100).toFixed(1)} %`}`)
const drawnWanted = (r: Rows): string[] => r.rows.map((x) => `${x.name.toLowerCase()} ${x.wanted === 0 ? `${x.caught}tracked` : `${x.caught}/${x.wanted}`}`)

/** The RS card's one form: the milestone, ShinyTarget and the mode, then Run; returns the click's ISO time. */
async function runRs(card: Locator, milestone: string): Promise<string> {
  const select = card.getByTestId('param-PhaseName').locator('select')
  await expect(select.locator(`option[value="${milestone}"]`), `${milestone} is not offered`).toBeAttached({ timeout: 30_000 })
  await select.selectOption(milestone)
  await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
  // A generated anchor with no range is hunted by Spin whatever the form says; Spin needs no range on the form.
  await card.getByTestId('param-EncounterType').locator('select').selectOption('Spin')
  const runStart = new Date().toISOString()
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  const refusal = card.locator('div[class~="bg-red-900/20"]')
  await expect.poll(async () => (await offered(card).count()) === 0 || (await refusal.count()) > 0, { timeout: 30_000 }).toBe(true)
  if ((await refusal.count()) > 0) throw new Error(`the Run was refused: "${(await refusal.innerText()).trim()}"`)
  step(`Wild Encounter at ${milestone} started at ${runStart}`)
  return runStart
}

test(`rs-milestone-anchor ${VARIANT}: ${VARIANT === 'anchor' ? 'Route 101 · grass walks to its generated anchor and spins there' : 'Route 110 · grass has no route on foot: Waiting'}`, async () => {
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  deleteGameFile(ROM)
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')
    expect((await applyPreset('RS', ROM, 'LivingDex')).status, `a Living dex goal on ${ROM}`).toBe(200)
    await shotAll('01-launched', pages)
    let tile: Tile | null = null
    const readBetween = async (): Promise<void> => {
      tile = await readTile()
      step(`tile between the cancels: ${JSON.stringify(tile)}`)
    }

    if (VARIANT === 'no-route') {
      const runStart = await runRs(card, ROUTE110)
      const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
      expect(hunt[1], `"hunting from the anchor" of ${ROUTE110_NAME}`).toMatch(logged(ROUTE110_NAME))
      expect(hunt.slice(2, 6), `"hunting from the anchor" of ${ROUTE110_NAME}: map, tile and how`).toEqual(['25', '14', '97', 'spin'])
      step(`no-route sentence: ${(await seen(NO_ROUTE, 1, 2 * MINUTE, { waiting: true })).line.trim()}`)
      await expect.poll(async () => (await dashboardState())?.eventState, { message: '"Waiting, no Complete"', timeout: MINUTE, intervals: [1000] })
        .toBe('Waiting')
      await expect(card.getByText('Resume', { exact: true }), '"Waiting": Resume on the card').toBeVisible({ timeout: MINUTE })
      await shotAll('02-waiting', pages)
      await cancelEventAndBot(card, readBetween)
      expect(tile, 'no step taken: the tile read between the cancels').toEqual(START)
      expect(rowsFor(ROM, ROUTE110, runStart), `no encounter under ${ROUTE110}`).toEqual([])
      expect(flashUnchanged(ROM), `"the save's first 131,072 bytes unchanged": the ${ROM} SaveRAM changed`).toBe(true)
      await shutdown(frame, admin)
      return
    }

    // No Hunt Plan field on RS, and a Start that names a plan is refused.
    await expect(offered(card), 'the Wild Encounter form').toBeVisible()
    await expect(card.getByTestId('param-HuntPlan'), '"the form has no Hunt Plan field"').toHaveCount(0)
    const refused = await startAction(0, { EventName: 'Wild Encounter', PhaseName: ROUTE101, TargetType: 'ShinyTarget', EncounterType: 'Spin', HuntPlan: 'QA RS' })
    expect([refused.status, refused.text], '"the RS plan refusal"').toEqual([400, PLAN_REFUSAL])

    // The Sapphire picker: every generated milestone and the custom Route 101.
    const pick = await picker(0)
    expect(pick.status, 'GET bot/milestones/0').toBe(200)
    const listed = pick.json.milestones as { id: string; modality: string | null }[]
    expect(listed.filter((m) => m.modality !== null).map((m) => m.id).sort(), '"picker(0) lists Sapphire\'s generated milestones"').toEqual(sapphireIds().sort())
    expect(listed.filter((m) => m.modality === null).map((m) => m.id), `"and the custom ${CUSTOM}"`).toContain(CUSTOM)

    // Route 101 · grass's card before Run: 45, 45 and 10, not live.
    expect((await subscribe({ game: 'RS', phase: ROUTE101, gameName: ROM })).status, `subscribe ${ROUTE101} for ${ROM}`).toBe(200)
    const cardPage = await windowByTitle(frame.app!, `RS — ${ROUTE101} (${ROM})`, MINUTE)
    pages.route101 = cardPage
    const idle = await route101()
    expectShares(idle, 'before Run')
    expect(idle.live, 'before Run: live').toBe(false)
    await expect.poll(async () => (await readCard(cardPage)).rows.map((r) => `${r.name} ${r.share}`), { message: '"45, 45 and 10" on the card before Run', timeout: MINUTE })
      .toEqual(drawnShares(idle))
    expect((await readCard(cardPage)).footer, '"not live" before Run').toContain('not live')
    await shotAll('02-before-run', pages)

    // The hunt: the walk to (4, 2), a spin there, five fled encounters.
    const runStart = await runRs(card, ROUTE101)
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
    expect(hunt[1], '"hunting from the anchor": the milestone').toMatch(logged(ROUTE101_NAME))
    expect(hunt.slice(2, 7), '"hunting from the anchor map 16 (4, 2)"').toEqual(['16', '4', '2', 'spin', 'generated'])
    await seen(OVER, ENCOUNTERS, 10 * MINUTE)
    const live = await route101()
    expectShares(live, 'hunting')
    expect([live.live, live.survive], '"the card live at 45/45/10 (no Repel)": live and the survive share').toEqual([true, 1])
    await expect.poll(async () => (await readCard(cardPage)).footer, { message: '"the card live": no "not live"', timeout: 2 * MINUTE, intervals: [2000] })
      .not.toContain('not live')
    expect((await readCard(cardPage)).rows.map((r) => `${r.name} ${r.share}`), '"live at 45/45/10" on the card').toEqual(drawnShares(live))
    await shotAll('03-hunting', pages)

    // A Version exclusives goal: the wanted change, the rows stay the three.
    expect((await applyPreset('RS', ROM, 'VersionExclusives')).status, 'Version exclusives').toBe(200)
    const exclusives = await route101()
    expectShares(exclusives, 'with Version exclusives')
    expect(exclusives.rows.map((r) => r.wanted), '"the rows\' wanted change"').not.toEqual(live.rows.map((r) => r.wanted))
    await expect.poll(async () => (await readCard(cardPage)).rows.map((r) => `${r.name} ${r.shinies}`), { message: 'the card\'s wanted with Version exclusives', timeout: 2 * MINUTE, intervals: [2000] })
      .toEqual(drawnWanted(exclusives))
    await shotAll('04-exclusives', pages)

    await cancelEventAndBot(card, readBetween)
    expect(tile, '"hunts there": the tile read between the cancels').toEqual({ ...START, x: 4, y: 2 })
    expect(flashUnchanged(ROM), `"the save's first 131,072 bytes unchanged": the ${ROM} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    const log = backendLog()
    // "the walk ending on (4, 2)": the navigator's arrival before the first spin.
    const arrived = log.findIndex((l) => CLEARING.exec(l)?.[1] === 'arrived back at start')
    const firstSpin = log.findIndex((l) => SPIN_LINE.test(l))
    expect(arrived, '"the walk ending on (4, 2)": no "arrived back at start" line').toBeGreaterThanOrEqual(0)
    expect(arrived, '"the walk ending on (4, 2)" before the first spin').toBeLessThan(firstSpin)
    for (const l of log.filter((x) => SPIN_LINE.test(x))) {
      const [, facing, macro] = SPIN_LINE.exec(l)!
      expect(macro, `"[Spin] facing=" lines: the turns after ${facing}`).toBe(spinMacroFor(facing))
    }
    const finished = battles(log).filter((b) => b.some((l) => OVER.test(l)))
    finished.forEach((b, k) => checkFlee(b, k + 1))
    const found = rowsFor(ROM, ROUTE101, runStart)
    expect(found.length, `"${ENCOUNTERS} encounters under ${ROUTE101}"`).toBeGreaterThanOrEqual(ENCOUNTERS)
    for (const r of found) {
      expect(WILD, '"each Wurmple, Zigzagoon or Poochyena"').toContain(Number(r.species))
      expect(LEVELS, '"at Lv 2 or 3"').toContain(r.level)
      expect(r.shiny, 'not shiny').toBe(false)
    }
  } finally {
    try {
      await rsTeardown(frame, ROM)
    } finally {
      // The subscription and SA-Wild's GameFiles document; the frame is already down.
      await teardown({ backend: null, app: null })
    }
  }
})
