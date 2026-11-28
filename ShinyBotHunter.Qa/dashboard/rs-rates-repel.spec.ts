// rs-rates-repel (docs/specs/hunt-plans-and-milestone-builder.md, QA row rs-rates-repel; FR-26a, FR-29 on RS; #525 Q1d;
// reworked for a saved milestone with a location, #621 R8b2), on SA-Wild (Sapphire rev 2) with the pristine rs.qa-route101 save
// and rs-wild.ts's frame. QA Route 101, Living dex at Route 101 · grass and ticked for SA-Wild. With the bot idle the lead's
// level is written to 4 and the Repel counter to 30: every Route 101 slot (Lv 2 or 3) is cancelled, so the endpoint gives all
// three rows "blocked by Repel" and live, and the card draws none of them. A Run hunt from an anchor set on the start tile (y range 1) closes "REPEL's effect wore off" with
// one A and carries on (PR #592); then a battle, and the card back at 45, 45 and 10. Run line: `npx playwright test rs-rates-repel`.
import { expect, test, type Locator, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ANCHOR_HUNT, assertLocalStores, cardWindow, deleteGameFile, phaseInfo, putMilestone, REPEL_NOT_CLOSED, rowsFor, RS_REPEL_CLOSED, seen, setAnchorHere,
  subscribe, teardown, type Frame
} from './milestones'
import {
  battles, cancelEventAndBot, checkFlee, ENCOUNTER, flashUnchanged, installPristine, launchRs, offered, OVER, removeCheats, teardown as rsTeardown
} from './rs-wild'
import { repel, writeSlot } from './rs-training'
import { backendLog, MINUTE, shutdown, startFrame } from './static'

const ROM = 'SA-Wild'
const ROUTE101 = 'QA Route 101'
const ROUTE101_NAME = 'Route 101 · grass'
// Above every Route 101 slot's Lv 2 or 3, so the game's Repel and RsRates cancel them all.
const LEAD_LEVEL = 4
const REPEL_STEPS = 30
// Route 101's slot weights: Wurmple 20 + 10 + 10 + 5, Zigzagoon the same, Poochyena 4 + 4 + 1 + 1.
const SHARES = [[265, 0.45], [263, 0.45], [261, 0.1]] as const
const BLOCKED = 'blocked by Repel'
// backend.log is written in the console's code page, so the middle dot and the ± are matched by any one or two characters.
const logged = (text: string): RegExp => new RegExp(`^${text.replace('·', '.{1,2}').replace('±', '.{1,2}')}$`)

type MilestoneRow = { species: number; name: string; share: number | null; reason: string | null }
type Rows = { rows: MilestoneRow[]; survive: number | null; live: boolean }

// GET phaseinfo for the file: the saved rows, and at the location each row's share and reason (in the rows' order) and the
// survive share.
async function route101(): Promise<Rows> {
  const res = await phaseInfo('RS', ROUTE101, ROM)
  expect(res.status, `GET phaseinfo/RS/${ROUTE101}/${ROM} (a 500 here is a NaN share in the rate code)`).toBe(200)
  const pokemon = res.json.phaseInfo.pokemon as { speciesId: number; name: string }[]
  const at = res.json.location as { survive: number | null; rows: { share: number | null; reason: string | null }[] }
  return {
    rows: pokemon.map((p, i) => ({ species: p.speciesId, name: p.name, share: at.rows[i].share, reason: at.rows[i].reason })),
    survive: at.survive, live: res.json.live
  }
}
const blocked = (r: Rows): boolean => r.live && r.survive === 0 && r.rows.every((x) => x.share === 0 && x.reason === BLOCKED)
const plain = (r: Rows): boolean => r.live && r.survive === 1 && SHARES.every(([s, share], i) => r.rows[i]?.species === s && Math.abs((r.rows[i].share ?? -1) - share) < 1e-9)

// table: the card has drawn its table (an empty tbody is not the loading line).
type CardView = { table: boolean; rows: string[]; header: string }
async function readCard(page: Page): Promise<CardView> {
  return page.evaluate(() => {
    const t = (e: Element | null | undefined): string => (e?.textContent ?? '').replace(/\s+/g, ' ').trim()
    const rows = Array.from(document.querySelectorAll('tbody tr')).map((tr) => {
      const name = tr.querySelectorAll('td')[1]
      const share = name?.querySelector('[data-testid="row-share"]')
      return `${Array.from(name?.querySelectorAll('span') ?? []).map(t).join('').replace('✓', '').trim().toLowerCase()} ${share ? t(share) : '-'}`
    })
    return { table: document.querySelector('table') !== null, rows, header: t(document.querySelector('.card-header')) }
  })
}
// The card for a server answer (PhaseCard): a row at a zero share is not drawn, the others with their share under the name.
const drawn = (r: Rows): string[] => r.rows.filter((x) => x.share !== 0).map((x) => `${x.name.toLowerCase()} ${((x.share ?? 0) * 100).toFixed(1)} %`)

/** The RS card's one form: Route 101 · grass, ShinyTarget, Run with y range 1, then Run; returns the click's ISO time. */
async function runRs(card: Locator): Promise<string> {
  const select = card.getByTestId('param-PhaseName').locator('select')
  await expect(select.locator(`option[value="${ROUTE101}"]`), `${ROUTE101} is not offered`).toBeAttached({ timeout: 30_000 })
  await select.selectOption(ROUTE101)
  await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
  await card.getByTestId('param-EncounterType').locator('select').selectOption('Run')
  await card.getByTestId('param-yRange').locator('input').fill('1')
  const runStart = new Date().toISOString()
  await card.getByRole('button', { name: 'Run', exact: true }).click()
  const refusal = card.locator('div[class~="bg-red-900/20"]')
  await expect.poll(async () => (await offered(card).count()) === 0 || (await refusal.count()) > 0, { timeout: 30_000 }).toBe(true)
  if ((await refusal.count()) > 0) throw new Error(`the Run was refused: "${(await refusal.innerText()).trim()}"`)
  step(`Wild Encounter at ${ROUTE101} started at ${runStart}`)
  return runStart
}

test('rs-rates-repel: lead Lv 4 and the Repel at 30: every Route 101 row blocked by Repel and live, one close with 1 A, the shares back after', async () => {
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  deleteGameFile(ROM)
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    // Before the launch, so the card's form lists it.
    const saved = await putMilestone({ game: 'RS', name: ROUTE101, location: '16-land', gameFiles: [ROM], archived: false })
    expect(saved.status, `${ROUTE101}, Living dex at 16-land, ticked for ${ROM}`).toBe(200)
    expect((await subscribe({ game: 'RS', phase: ROUTE101, gameName: ROM })).status, `subscribe ${ROUTE101} for ${ROM}`).toBe(200)
    const cardPage = await cardWindow(frame.app!, `RS — ${ROUTE101} (${ROM})`)
    pages.card = cardPage
    const card = await launchRs(admin, ROM, 'emulator')

    // With the bot idle: the lead's level, the Repel counter, and the anchor on the start tile (14, 4) with y range 1.
    await writeSlot(1, { level: LEAD_LEVEL })
    await repel(REPEL_STEPS)
    const anchor = await setAnchorHere(0, ROUTE101, 0, 1)
    expect(anchor.status, 'Set anchor here').toBe(200)
    expect([anchor.json.anchor.map, anchor.json.anchor.x, anchor.json.anchor.y, anchor.json.anchor.yRange], 'the anchor on the start tile').toEqual([16, 14, 4, 1])
    await shotAll('01-set-up', pages)

    const runStart = await runRs(card)
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
    expect(hunt[1], '"the anchor decides Run": the milestone').toMatch(logged(ROUTE101_NAME))
    expect(hunt.slice(2, 5), '"the anchor decides Run": the tile').toEqual(['16', '14', '4'])
    expect(hunt[5], '"the anchor decides Run": the hunt').toMatch(logged('run up-down ±1'))
    expect(hunt[6], 'the anchor is the file\'s').toBe('set on this file')

    // FR-26a: the counter at every decision, the first included, so within one poll of the endpoint every row is blocked.
    await expect.poll(async () => blocked(await route101()), { message: '"within one poll ... all three rows blocked by Repel and live"', timeout: MINUTE, intervals: [2000] })
      .toBe(true)
    const repelled = await route101()
    step(`the rows with the Repel on: ${JSON.stringify(repelled)}`)
    // The card rereads on an encounter's push or the 60 s poll; the decision after the first battle reads the counter at 0.
    await expect.poll(async () => readCard(cardPage), { message: 'the card: no row while every one is "blocked by Repel"', timeout: 3 * MINUTE, intervals: [1000] })
      .toEqual({ table: true, rows: drawn(repelled), header: expect.any(String) })
    expect((await readCard(cardPage)).header, 'the card with the Repel on: live').not.toContain('not live')
    await shotAll('02-blocked', pages)

    // FR-29: the message closed by one A, then the hunt carries on to a battle.
    const closed = await seen(RS_REPEL_CLOSED, 1, 5 * MINUTE)
    expect(RS_REPEL_CLOSED.exec(closed.line)![1], '"closed with 1 A"').toBe('1')
    await seen(OVER, 1, 5 * MINUTE)
    await expect.poll(async () => plain(await route101()), { message: '"the card back at 45/45/10, live": the endpoint', timeout: 2 * MINUTE, intervals: [2000] })
      .toBe(true)
    const back = await route101()
    await expect.poll(async () => readCard(cardPage), { message: '"the card back at 45/45/10, live"', timeout: 3 * MINUTE, intervals: [1000] })
      .toEqual({ table: true, rows: drawn(back), header: expect.not.stringContaining('not live') })
    await shotAll('03-back', pages)

    await cancelEventAndBot(card)
    expect(flashUnchanged(ROM), `"the save's first 131,072 bytes unchanged": the ${ROM} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    const log = backendLog()
    expect(log.filter((l) => RS_REPEL_CLOSED.test(l)).length, '"exactly one" Repel message closed').toBe(1)
    expect(log.filter((l) => REPEL_NOT_CLOSED.test(l)), '"no did not close"').toEqual([])
    const closeAt = log.findIndex((l) => RS_REPEL_CLOSED.test(l))
    const firstEncounter = log.findIndex((l) => ENCOUNTER.test(l))
    expect(firstEncounter, '"no encounter while the Repel lasts": the first encounter line').toBeGreaterThan(closeAt)
    // The rows pair in order with the encounter lines, so each row is a battle logged after the close line.
    const afterClose = log.slice(closeAt).filter((l) => ENCOUNTER.test(l)).map((l) => ENCOUNTER.exec(l)!).map((m) => `${m[2]} Lv ${m[3]}`)
    const recorded = rowsFor(ROM, ROUTE101, runStart).map((r) => `${r.species} Lv ${r.level}`)
    expect(recorded, '"no encounter row while the Repel lasts": the rows against the encounter lines after the close').toEqual(afterClose.slice(0, recorded.length))
    const finished = battles(log).filter((b) => b.some((l) => OVER.test(l)))
    finished.forEach((b, k) => checkFlee(b, k + 1))
  } finally {
    try {
      await rsTeardown(frame, ROM)
    } finally {
      // The subscription and SA-Wild's GameFiles document (the goal and the anchor); the frame is already down.
      await teardown({ backend: null, app: null })
    }
  }
})
