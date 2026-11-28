// rs-milestone-anchor (docs/specs/hunt-plans-and-milestone-builder.md, QA rows rs-milestone-anchor and rs-milestone-rows; #525
// Q1d; reworked for saved milestones with a location, #621 R8b2), on SA-Wild with the pristine rs.qa-route101 save and
// rs-wild.ts's frame. QA_RS_VARIANT:
// - `anchor` (the default): no Plan action on RS and the RS plan refusal; the Sapphire picker; QA Route 101's fixed card at
//   45, 45 and 10 before Run, with no time pill; the walk to the generated anchor (4, 2) and a spin there, five fled
//   encounters, the card live.
// - `no-route`: QA Route 110, at Route 110 · grass, is not reachable on foot from Route 101: the RS navigator's no-route line
//   and Waiting.
// Run lines: `npx playwright test rs-milestone-anchor`; `$env:QA_RS_VARIANT = "no-route"; npx playwright test rs-milestone-anchor`.
import { expect, test, type Locator, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ANCHOR_HUNT, assertLocalStores, cardWindow, deleteGameFile, phaseInfo, picker, putMilestone, rowsFor, seen, startAction, subscribe, teardown,
  type Frame
} from './milestones'
import {
  battles, cancelEventAndBot, checkFlee, flashUnchanged, installPristine, launchRs, LEVELS, offered, OVER, readTile, removeCheats, SPIN_LINE,
  dashboardState, spinMacroFor, START, teardown as rsTeardown, WILD, type Tile
} from './rs-wild'
import { CLEARING, variant } from './rs-training'
import { backendLog, MINUTE, shutdown, startFrame } from './static'

const ROM = 'SA-Wild'
const VARIANT = variant(['anchor', 'no-route'] as const)
// The fixtures, at Sapphire's Route 101 · grass and Route 110 · grass (Data/RS/milestones.json.gz), and the QA database's
// stored Route 101, which has no location.
const QA_ROUTE101 = 'QA Route 101'
const ROUTE101_NAME = 'Route 101 · grass'
const QA_ROUTE110 = 'QA Route 110'
const ROUTE110_NAME = 'Route 110 · grass'
const STORED = 'Route 101'
// Route 101's slot weights: Wurmple 20 + 10 + 10 + 5, Zigzagoon the same, Poochyena 4 + 4 + 1 + 1.
const SHARES = [[265, 0.45], [263, 0.45], [261, 0.1]] as const
const PLAN_REFUSAL = 'Plan needs HasHuntPlans, which RS does not have.'
// RsNavigatorEvent.WalkToAsync, with no route to the anchor.
const NO_ROUTE = /\[Navigator\] stopped: No route back to the start tile (.+) from (.+)\.(.*)$/
const ENCOUNTERS = 5

// backend.log is written in the console's code page, so a milestone name's middle dot is matched by any one or two characters.
const logged = (name: string): RegExp => new RegExp(`^${name.replace('·', '.{1,2}')}$`)

// GET phaseinfo for the file: the saved rows, and at the location each row's share (in the rows' order) and the survive share.
type Rows = { rows: { species: number; name: string; share: number | null }[]; survive: number | null; live: boolean }
async function route101(): Promise<Rows> {
  const res = await phaseInfo('RS', QA_ROUTE101, ROM)
  expect(res.status, `GET phaseinfo/RS/${QA_ROUTE101}/${ROM}`).toBe(200)
  const pokemon = res.json.phaseInfo.pokemon as { speciesId: number; name: string }[]
  const at = res.json.location as { survive: number | null; rows: { share: number | null }[] }
  return { rows: pokemon.map((p, i) => ({ species: p.speciesId, name: p.name, share: at.rows[i].share })), survive: at.survive, live: res.json.live }
}

function expectShares(r: Rows, what: string): void {
  expect(r.rows.map((x) => x.species), `${what}: "every species is a row", Wurmple, Zigzagoon and Poochyena`).toEqual(SHARES.map(([s]) => s))
  SHARES.forEach(([species, share], i) => expect(r.rows[i].share, `${what}: the share of ${species}`).toBeCloseTo(share, 6))
}

type CardView = { rows: { name: string; share: string | null }[]; header: string; pills: number }
async function readCard(page: Page): Promise<CardView> {
  return page.evaluate(() => {
    const t = (e: Element | null | undefined): string => (e?.textContent ?? '').replace(/\s+/g, ' ').trim()
    const rows = Array.from(document.querySelectorAll('tbody tr')).map((tr) => {
      const td = tr.querySelectorAll('td')
      const share = td[1]?.querySelector('[data-testid="row-share"]')
      return {
        name: Array.from(td[1]?.querySelectorAll('span') ?? []).map(t).join('').replace('✓', '').trim().toLowerCase(),
        share: share ? t(share) : null
      }
    })
    return { rows, header: t(document.querySelector('.card-header')), pills: document.querySelectorAll('[data-testid="time-pill"]').length }
  })
}
// The share under each name for a server row (PhaseCard); a row at a zero share is not drawn.
const drawnShares = (r: Rows): string[] => r.rows.filter((x) => x.share !== 0).map((x) => `${x.name.toLowerCase()} ${((x.share ?? 0) * 100).toFixed(1)} %`)

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

test(`rs-milestone-anchor ${VARIANT}: ${VARIANT === 'anchor' ? 'QA Route 101 walks to its generated anchor and spins there' : 'QA Route 110 has no route on foot: Waiting'}`, async () => {
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  deleteGameFile(ROM)
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const [name, location] = VARIANT === 'anchor' ? [QA_ROUTE101, '16-land'] : [QA_ROUTE110, '25-land']
    const saved = await putMilestone({ game: 'RS', name, location, gameFiles: [ROM], archived: false })
    expect(saved.status, `${name}, Living dex at ${location}, ticked for ${ROM}`).toBe(200)
    expect((await subscribe({ game: 'RS', phase: name, gameName: ROM })).status, `subscribe ${name} for ${ROM}`).toBe(200)
    const cardPage = await cardWindow(frame.app!, `RS — ${name} (${ROM})`)
    pages.card = cardPage
    const card = await launchRs(admin, ROM, 'emulator')
    await shotAll('01-launched', pages)
    let tile: Tile | null = null
    const readBetween = async (): Promise<void> => {
      tile = await readTile()
      step(`tile between the cancels: ${JSON.stringify(tile)}`)
    }

    if (VARIANT === 'no-route') {
      const runStart = await runRs(card, QA_ROUTE110)
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
      expect(rowsFor(ROM, QA_ROUTE110, runStart), `no encounter under ${QA_ROUTE110}`).toEqual([])
      expect(flashUnchanged(ROM), `"the save's first 131,072 bytes unchanged": the ${ROM} SaveRAM changed`).toBe(true)
      await shutdown(frame, admin)
      return
    }

    // No Plan action on RS, and a Start of one is refused with the capability it needs.
    await expect(offered(card), 'the Wild Encounter form').toBeVisible()
    await expect(card.getByRole('button', { name: 'Plan', exact: true }), '"no Plan action in Bot Control"').toHaveCount(0)
    await expect(card.getByTestId('param-HuntPlan'), 'no Hunt Plan field').toHaveCount(0)
    const refused = await startAction(0, { EventName: 'Plan', HuntPlan: 'x' })
    expect([refused.status, refused.text], '"the RS plan refusal"').toEqual([400, PLAN_REFUSAL])

    // The Sapphire picker: the saved milestones, the fixture and the stored Route 101 among them.
    const pick = await picker(0)
    expect(pick.status, 'GET bot/milestones/0').toBe(200)
    const listed = (pick.json.milestones as { name: string }[]).map((m) => m.name)
    expect(listed, `"picker(0) lists ${QA_ROUTE101} and the stored ${STORED}"`).toEqual(expect.arrayContaining([QA_ROUTE101, STORED]))

    // QA Route 101's card before Run: 45, 45 and 10, not live, no time pill.
    const idle = await route101()
    expectShares(idle, 'before Run')
    expect(idle.live, 'before Run: live').toBe(false)
    await expect.poll(async () => (await readCard(cardPage)).rows.map((r) => `${r.name} ${r.share}`), { message: '"45, 45 and 10" on the card before Run', timeout: MINUTE })
      .toEqual(drawnShares(idle))
    const before = await readCard(cardPage)
    expect(before.header, '"not live" before Run').toContain('not live')
    expect(before.pills, '"no time-pill" on RS').toBe(0)
    await shotAll('02-before-run', pages)

    // The hunt: the walk to (4, 2), a spin there, five fled encounters.
    const runStart = await runRs(card, QA_ROUTE101)
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
    expect(hunt[1], '"hunting from the anchor": the location').toMatch(logged(ROUTE101_NAME))
    expect(hunt.slice(2, 7), '"hunting from the anchor map 16 (4, 2)"').toEqual(['16', '4', '2', 'spin', 'generated'])
    await seen(OVER, ENCOUNTERS, 10 * MINUTE)
    const live = await route101()
    expectShares(live, 'hunting')
    expect([live.live, live.survive], '"the card live at 45/45/10 (no Repel)": live and the survive share').toEqual([true, 1])
    await expect.poll(async () => (await readCard(cardPage)).header, { message: '"the card live": no "not live"', timeout: 2 * MINUTE, intervals: [2000] })
      .not.toContain('not live')
    const hunting = await readCard(cardPage)
    expect(hunting.rows.map((r) => `${r.name} ${r.share}`), '"live at 45/45/10" on the card').toEqual(drawnShares(live))
    expect(hunting.pills, '"no time-pill" on RS while live').toBe(0)
    await shotAll('03-hunting', pages)

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
    const found = rowsFor(ROM, QA_ROUTE101, runStart)
    expect(found.length, `"${ENCOUNTERS} encounters under ${QA_ROUTE101}"`).toBeGreaterThanOrEqual(ENCOUNTERS)
    for (const r of found) {
      expect(WILD, '"each Wurmple, Zigzagoon or Poochyena"').toContain(Number(r.species))
      expect(LEVELS, '"at Lv 2 or 3"').toContain(r.level)
      expect(r.shiny, 'not shiny').toBe(false)
    }
  } finally {
    try {
      await rsTeardown(frame, ROM)
    } finally {
      // The fixture, the subscription, the card video and SA-Wild's GameFiles document; the frame is already down.
      await teardown({ backend: null, app: null })
    }
  }
})
