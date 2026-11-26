// hgss-existing-backfill (docs/specs/hunt-plans-and-milestone-builder.md, QA rows hgss-existing-unchanged and hgss-backfill;
// #525 Q1d): production's phases, imported into the local QA PhaseInfos by id and removed after, behave as before (FR-42,
// FR-42a, FR-39c): the picker, a stored card and a hunt where the bot stands. Then "1-4 Route 30" is linked to Route 30 · grass
// on one file (FR-43 to FR-46, FR-44a): the counts with and without the link, the link's refusals, the copy of amounts, and the
// picker of a second file at the title screen. Run line: `npx playwright test hgss-existing-backfill`.
import * as fs from 'fs'
import * as path from 'path'
import * as zlib from 'zlib'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { repoDir, shotAll, step, windowByTitle } from './driver'
import {
  ANCHOR_HUNT, assertLocalStores, cancelBot, deleteGameFile, importPhases, launchHgss, PHASES_FIXTURE, picker, PLAN_NOW, PLAN_STOP, PLAN_WALK,
  rows, rowsFor, saveOf, seen, shutdown, stageCopy, startAction, startFrame, startWild, subscribe, teardown, WILD, writeLeadExtension,
  type Frame
} from './milestones'
import { dashboardState } from './rs-wild'
import { API, backendLog, MINUTE, readTile, sha256 } from './static'

const ROM = 'HG-Route32.nds'
const LEGACY = 'HG-Legacy'
const LEGACY2 = 'HG-Legacy2'
const PHASE = '1-4 Route 30'
// Route 30 · grass, Backfill.Suggestion's id for the phase; Route 32 · grass for the second link.
const ROUTE30 = '34-land'
const ROUTE30_NAME = 'Route 30 · grass'
const OTHER = '36-land'
const MISTYPED = '34-lnad'
const EPOCH = '1970-01-01T00:00:00Z'
// BotService.LinkedRefusal, PhaseMetadataController.SetLinkAsync and MilestoneFiles' 404 sentence.
const LINKED = `${PHASE} is linked to ${ROUTE30_NAME} on ${LEGACY}; start ${ROUTE30_NAME}, or remove the link.`
const LINKED_ELSEWHERE = `${PHASE} is already linked to ${ROUTE30} on ${LEGACY}; remove that link first`
const NOT_FOUND = `No generated milestone "${MISTYPED}" for this game file's version`
// The place checks that come before the link check on Primo and Static (HgssPrimoEggEvent.StandFacingPrimo, BotService.OffTheStage).
const PRIMO_PLACE = /^Stand facing Primo in the Violet City Pok.{1,2}mon Center: /
const STATIC_PLACE = 'Stand on the Mystri Stage in the Sinjoh Ruins.'
// The stored rows of the fixture's "1-4 Route 30": name with its gender mark (a pinned form of a species with a female form is
// male), then the amount.
const STORED_ROWS = ['weedle 1', 'ledyba♂ 2', 'pidgey 1', 'hoothoot 1', 'rattata♂ 1']
const WEEDLE = 13
const PIDGEY = 16
const LEDYBA = 165
// BattleOrchestratorEvent, at every wild battle's start; OrchestratorEvent at Init.
const ENCOUNTER = /, isShiny: (True|False)\s*$/
const START_TILE = /\[Orchestrator\] Start tile captured: map=(\d+) \((-?\d+),(-?\d+)\) facing=(\w+)/
const REREAD = 90_000

type Picked = { id: string; name: string; modality: string | null }
type Entry = { species: number; formCode: string | null; amount: number }
type MilestoneRow = { species: number; key: string; encountersHere: number; share: number | null }

/** HeartGold's generated milestone ids, as Milestone.Id makes them from Data/HGSS/milestones.json.gz. */
function heartGoldIds(): string[] {
  const file = path.join(repoDir, 'ShinyBotHunter', 'Data', 'HGSS', 'milestones.json.gz')
  const all = JSON.parse(zlib.gunzipSync(fs.readFileSync(file)).toString('utf8')) as Record<string, { Maps: number[]; Modality: string }[]>
  return all.HeartGold.map((m) => `${m.Maps[0]}-${m.Modality.toLowerCase()}`)
}

async function pickerOf(pos: number): Promise<Picked[]> {
  const res = await picker(pos)
  expect(res.status, `GET bot/milestones/${pos}`).toBe(200)
  return res.json.milestones as Picked[]
}

async function link(handle: string, milestoneId: string, linked: boolean): Promise<{ status: number; text: string }> {
  const res = await fetch(`${API}/goal/HGSS/${encodeURIComponent(handle)}/link?linked=${linked}`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ phase: PHASE, milestoneId })
  })
  const text = await res.text()
  step(`PUT goal/HGSS/${handle}/link?linked=${linked} ${milestoneId} -> ${res.status} ${text}`)
  return { status: res.status, text }
}

type CardView = { headers: string[]; rows: { name: string; here: number; amount: string }[]; shares: number; count: number; footer: string }
/** The card's table, header count and footer from its window's DOM. */
async function readCard(page: Page): Promise<CardView> {
  return page.evaluate(() => {
    const t = (e: Element | null | undefined): string => (e?.textContent ?? '').replace(/\s+/g, ' ').trim()
    const card = document.querySelector('.dashboard-card')
    const headers = Array.from(document.querySelectorAll('thead th')).map(t)
    const here = headers.indexOf('Milestone Encounters')
    const rows = Array.from(document.querySelectorAll('tbody tr')).map((tr) => {
      const td = tr.querySelectorAll('td')
      const shinies = t(td[td.length - 1]).replace(/\s+/g, '').replace(/\(.*$/, '')
      return { name: t(td[1]).replace('✓', '').trim().toLowerCase(), here: Number(t(td[here]).replace(/,/g, '')), amount: shinies.includes('/') ? shinies.split('/')[1] : shinies }
    })
    const count = Number(t(card?.querySelector('[data-testid="phase-encounter-count"]')).replace(/,/g, ''))
    const shares = document.querySelectorAll('[data-testid="row-share"]').length
    return { headers, rows, shares, count, footer: t(card?.lastElementChild) }
  })
}

/** Every row's encounters here as the counts the server should give: this file's rows under the counted milestones, by species. */
function expectedHere(rowsOf: MilestoneRow[], handle: string, milestones: string[]): number[] {
  const species = milestones.flatMap((m) => rowsFor(handle, m, EPOCH)).map((r) => Number(r.species))
  return rowsOf.map((r) => species.filter((s) => s === r.species).length)
}

/** The counts of the rows the card draws: a row at a zero share (a species not met now on a file seen hunting) is not drawn. */
const drawnHere = (rowsOf: MilestoneRow[], here: number[]): number[] => here.filter((_, i) => rowsOf[i].share !== 0)

async function milestoneRows(handle: string): Promise<MilestoneRow[]> {
  const res = await rows('HGSS', ROUTE30, handle)
  expect(res.status, `GET milestone/HGSS/${ROUTE30}/${handle}`).toBe(200)
  return res.json.rows as MilestoneRow[]
}

async function cancelEvent(card: Locator): Promise<void> {
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name: 'Cancel', exact: true }).click()
  await card.getByRole('button', { name: 'Yes', exact: true }).click()
  await expect(card.getByRole('button', { name: WILD, exact: true }), 'the event did not end').toBeVisible({ timeout: MINUTE })
  step('event cancelled')
}

test('hgss-existing-backfill: stored phases behave as before; 1-4 Route 30 linked to Route 30 · grass on one file, then unlinked', async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(fs.existsSync(PHASES_FIXTURE), `missing ${PHASES_FIXTURE}: the orchestrator stages the production-phases fixture (qa-automation.md, preconditions)`).toBe(true)
  const fixture = JSON.parse(fs.readFileSync(PHASES_FIXTURE, 'utf8')) as { GameName: string; Name: string }[]
  const fixtureNames = fixture.filter((p) => p.GameName === 'HGSS').map((p) => p.Name)
  step(`fixture: ${fixture.length} phases, ${fixtureNames.length} HGSS: ${fixtureNames.join(', ')}`)
  for (const handle of [LEGACY, LEGACY2]) deleteGameFile(handle)
  stageCopy(LEGACY, { rom: ROM, save: 'hgss.qa-route32' })
  stageCopy(LEGACY2, { rom: ROM, save: null })
  try {
    // Before the backend starts: it reads the phases once at start-up.
    const imported = importPhases(PHASES_FIXTURE)
    step(`${imported.length} of the fixture's phases were missing from the QA PhaseInfos and were imported`)
    const admin = await startFrame(frame)
    const app = frame.app!
    const pages: Record<string, Page> = { admin }
    const card = await launchHgss(admin, LEGACY)
    const launchTile = await readTile()
    await writeLeadExtension({ hp: 30 })
    await shotAll('01-launched', pages)

    // (a) The picker: every stored HGSS phase beside every generated milestone, and the form's select in the server's order.
    const before = await pickerOf(0)
    const ids = before.map((m) => m.id)
    for (const name of fixtureNames) expect(ids, `"the picker lists every stored phase": ${name}`).toContain(name)
    expect(before.filter((m) => m.modality !== null).map((m) => m.id).sort(), '"beside the generated ones": HeartGold\'s generated milestones')
      .toEqual(heartGoldIds().sort())
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    await card.getByRole('button', { name: WILD, exact: true }).click()
    const select = card.getByTestId('param-PhaseName').locator('select')
    await expect(select.locator(`option[value="${PHASE}"]`), `${PHASE} is not offered`).toBeAttached({ timeout: 30_000 })
    const options = (await select.locator('option').evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value))).filter((v) => v !== '')
    expect(options, '"in the server\'s order": the form\'s milestone select').toEqual(ids)
    await shotAll('02-picker', pages)

    // (b) A fixed card on the stored phase with no file: today's card, its stored rows, no share under a name and no new footer line.
    expect((await subscribe({ game: 'HGSS', phase: PHASE, gameName: null })).status, `subscribe ${PHASE}`).toBe(200)
    const stored = await windowByTitle(app, `HGSS — ${PHASE}`, MINUTE)
    pages.stored = stored
    await expect.poll(async () => (await readCard(stored)).rows.map((r) => `${r.name} ${r.amount}`), { message: `"exactly the stored rows" of ${PHASE}`, timeout: MINUTE })
      .toEqual(STORED_ROWS)
    const storedView = await readCard(stored)
    step(`stored card: ${JSON.stringify(storedView)}`)
    expect(storedView.shares, '"no share" on the stored card').toBe(0)
    expect(storedView.footer, '"no new footer line" on the stored card').not.toMatch(/not live|Shares: not seen/)
    const countBefore = storedView.count

    // (c) A hunt at the stored phase where the bot stands: no anchor and no plan, three encounters, counted under its name.
    const runStart = await startWild(card, { phase: PHASE, target: 'ShinyTarget', mode: 'Run', xRange: 1 })
    const captured = START_TILE.exec((await seen(START_TILE, 1, MINUTE)).line)!
    expect([Number(captured[1]), Number(captured[2]), Number(captured[3])], '"the Start tile captured is the launch tile"')
      .toEqual([launchTile.map, launchTile.x, launchTile.y])
    await seen(ENCOUNTER, 3, 10 * MINUTE)
    await shotAll('03-hunting', pages)
    await cancelEvent(card)
    const log = backendLog()
    for (const [what, pattern] of [['hunting from the anchor', ANCHOR_HUNT], ['Plan stop', PLAN_STOP], ['Plan walk', PLAN_WALK], ['Plan now', PLAN_NOW]] as const) {
      expect(log.filter((l) => pattern.test(l)), `"no ${what} line" for a stored phase`).toEqual([])
    }
    const encounters = log.filter((l) => ENCOUNTER.test(l)).length
    await expect.poll(() => rowsFor(LEGACY, PHASE, runStart).length, { message: `"${encounters} rows under ${PHASE} for ${LEGACY}"`, timeout: 10_000, intervals: [1000] })
      .toBe(encounters)
    await expect.poll(async () => (await readCard(stored)).count, { message: `"the card's count up by ${encounters}"`, timeout: MINUTE })
      .toBe(countBefore + encounters)
    await shotAll('04-counted', pages)

    // (d) The Milestone Editor's Game files section: the suggestion, a mistyped id (404), the link, and a second link (400).
    await admin.getByRole('button', { name: 'Milestone Editor', exact: true }).click()
    const editorSelects = admin.getByRole('heading', { name: 'Select Milestone', exact: true }).locator('xpath=following-sibling::div[1]').locator('select')
    await editorSelects.first().selectOption('HGSS')
    await editorSelects.nth(1).selectOption(PHASE)
    const files = admin.getByTestId('milestone-files')
    const block = (handle: string): Locator => files.locator('div.rounded-lg').filter({ has: admin.getByText(handle, { exact: true }) })
    await expect(block(LEGACY).locator('input[type="text"]'), '"the suggestion reads 34-land"').toHaveValue(ROUTE30, { timeout: 30_000 })
    await block(LEGACY2).locator('input[type="text"]').fill(MISTYPED)
    await block(LEGACY2).getByRole('button', { name: 'Link', exact: true }).click()
    await expect(block(LEGACY2).getByText(NOT_FOUND, { exact: true }), '"a mistyped id gives the 404 sentence"').toBeVisible({ timeout: 30_000 })
    await block(LEGACY).getByRole('button', { name: 'Link', exact: true }).click()
    await expect(block(LEGACY).locator('code'), '"Link stores it"').toHaveText(ROUTE30, { timeout: 30_000 })
    // The status line reads the same as the feedback message under the block; the message is the green one.
    await expect(block(LEGACY).locator('div.text-green-400'), '"Linked to 34-land"').toHaveText(`Linked to ${ROUTE30}`)
    await shotAll('05-linked', pages)
    const again = await link(LEGACY, OTHER, true)
    expect([again.status, again.text], '"a second link of the phase to another id gives the server\'s 400"').toEqual([400, LINKED_ELSEWHERE])

    // (e) The generated cards: HG-Legacy counts the linked phase's encounters by species, HG-Legacy2 its own (none).
    const linked = await milestoneRows(LEGACY)
    const withLink = expectedHere(linked, LEGACY, [ROUTE30, PHASE])
    expect(linked.map((r) => r.encountersHere), `"${ROUTE30_NAME} on ${LEGACY} counts the linked encounters" (by species)`).toEqual(withLink)
    step(`with the link, ${LEGACY}'s rows count ${withLink.reduce((a, b) => a + b, 0)} encounters of their species`)
    expect((await subscribe({ game: 'HGSS', phase: ROUTE30, gameName: LEGACY })).status, `subscribe ${ROUTE30} for ${LEGACY}`).toBe(200)
    const generated = await windowByTitle(app, `HGSS — ${ROUTE30} (${LEGACY})`, MINUTE)
    pages.generated = generated
    await expect.poll(async () => (await readCard(generated)).rows.map((r) => r.here), { message: `${LEGACY}'s generated card with the link`, timeout: MINUTE })
      .toEqual(drawnHere(linked, withLink))
    const second = await milestoneRows(LEGACY2)
    const own = expectedHere(second, LEGACY2, [ROUTE30])
    expect(second.map((r) => r.encountersHere), `"a second card for ${LEGACY2} shows its own counts"`).toEqual(own)
    expect(own.reduce((a, b) => a + b, 0), `${LEGACY2} never hunts`).toBe(0)
    expect((await subscribe({ game: 'HGSS', phase: ROUTE30, gameName: LEGACY2 })).status, `subscribe ${ROUTE30} for ${LEGACY2}`).toBe(200)
    const generated2 = await windowByTitle(app, `HGSS — ${ROUTE30} (${LEGACY2})`, MINUTE)
    pages.generated2 = generated2
    await expect.poll(async () => (await readCard(generated2)).rows.map((r) => r.here), { message: `"${LEGACY2}'s card shows 0"`, timeout: MINUTE })
      .toEqual(drawnHere(second, own))
    await shotAll('06-generated-cards', pages)

    // (f) The linked name leaves HG-Legacy's picker, and every hunt type refuses it.
    expect((await pickerOf(0)).map((m) => m.id), `"${PHASE} is gone from ${LEGACY}'s picker"`).not.toContain(PHASE)
    const idleState = (await dashboardState())?.eventState
    const leg = { PhaseName: PHASE, TargetType: 'ShinyTarget', EncounterType: 'Run', xRange: 1 }
    const starts: [string, Record<string, unknown>, RegExp | string | null][] = [
      ['Wild Encounter', { EventName: 'Wild Encounter', ...leg }, null],
      ['a Chained Encounter leg', { EventName: 'Chained Encounter', Legs: [leg] }, null],
      ['HGSS Togepi Egg', { EventName: 'HGSS Togepi Egg', PhaseName: PHASE }, null],
      ['HGSS Primo Egg', { EventName: 'HGSS Primo Egg', PhaseName: PHASE }, PRIMO_PLACE],
      ['HGSS Static Encounter', { EventName: 'HGSS Static Encounter', PhaseName: PHASE, StaticEncounter: 'Dialga (Sinjoh Ruins)' }, STATIC_PLACE]
    ]
    for (const [what, args, place] of starts) {
      const res = await startAction(0, args)
      expect(res.status, `"the linked name refused on ${what}": the status`).toBe(400)
      const placeFirst = place !== null && (typeof place === 'string' ? res.text === place : place.test(res.text))
      if (placeFirst) step(`${what}: refused by its place check before the link check (the save stands on Route 32): "${res.text}"`)
      else expect(res.text, `"the linked name refused on ${what}"`).toBe(LINKED)
      expect((await dashboardState())?.eventState, `${what}: an event started`).toBe(idleState)
    }

    // (g) The copy: the offer beside the goal, then Weedle and Pidgey copied; Ledyba untouched.
    const offer = (await (await fetch(`${API}/goal/HGSS/${LEGACY}/backfill/${encodeURIComponent(PHASE)}`)).json()) as {
      suggested: { id: string; name: string } | null; copy: { species: number; phase: Entry[]; goal: Entry[] }[]
    }
    step(`GET backfill: ${JSON.stringify(offer)}`)
    expect(offer.suggested, 'the backfill offer\'s suggestion').toEqual({ id: ROUTE30, name: ROUTE30_NAME })
    expect(offer.copy.map((o) => [o.species, o.phase.length > 0, o.goal.length]), '"GET backfill shows the phase beside the goal" (an empty goal)')
      .toEqual([[WEEDLE, true, 0], [LEDYBA, true, 0], [PIDGEY, true, 0], [163, true, 0], [19, true, 0]])
    await block(LEGACY).getByRole('button', { name: /Copy amounts/ }).click()
    for (const name of ['Weedle', 'Pidgey']) await block(LEGACY).getByText(name, { exact: true }).locator('xpath=preceding-sibling::input[1]').check()
    await block(LEGACY).getByRole('button', { name: 'Copy chosen', exact: true }).click()
    await expect(block(LEGACY).getByText('Copied 2 species into the goal', { exact: true }), 'the copy').toBeVisible({ timeout: 30_000 })
    const copied = ((await (await fetch(`${API}/goal/${LEGACY}`)).json()) as { entries: Entry[] }).entries
    step(`${LEGACY} goal after the copy: ${JSON.stringify(copied)}`)
    expect(copied.map((e) => `${e.species}/${e.formCode ?? 'any'}/${e.amount}`).sort(), '"the goal holds those two and Ledyba is untouched"')
      .toEqual([`${WEEDLE}/any/1`, `${PIDGEY}/any/1`])
    await shotAll('07-copied', pages)

    // (h) The link removed: the name is back in the picker and the generated card drops the linked encounters.
    await block(LEGACY).getByRole('button', { name: 'Remove link', exact: true }).click()
    await expect(block(LEGACY).getByText('Link removed', { exact: true }), 'the link removed').toBeVisible({ timeout: 30_000 })
    expect((await pickerOf(0)).map((m) => m.id), `"${PHASE} is back in ${LEGACY}'s picker"`).toContain(PHASE)
    const unlinked = await milestoneRows(LEGACY)
    const withoutLink = expectedHere(unlinked, LEGACY, [ROUTE30])
    expect(unlinked.map((r) => r.encountersHere), `"without the link" ${LEGACY}'s rows`).toEqual(withoutLink)
    await expect.poll(async () => (await readCard(generated)).rows.map((r) => r.here), { message: `"the generated card drops the ${PHASE} encounters"`, timeout: REREAD, intervals: [2000] })
      .toEqual(drawnHere(unlinked, withoutLink))
    await shotAll('08-unlinked', pages)

    // (i) The link is per file: with HG-Legacy linked again, HG-Legacy2 at the title screen still lists the phase, and every
    // generated milestone of the version.
    await cancelBot(card)
    expect(sha256(saveOf(LEGACY)), `${LEGACY}: the SaveRAM differs from its .bak (nothing is saved)`).toBe(sha256(saveOf(LEGACY) + '.bak'))
    expect((await link(LEGACY, ROUTE30, true)).status, `${LEGACY} linked again`).toBe(204)
    const titleCard = await launchHgss(admin, LEGACY2, { continue: false })
    const pos = Number((await titleCard.getAttribute('data-testid'))!.replace('emulator-card-', ''))
    const title = await pickerOf(pos)
    expect(title.map((m) => m.id), `"${LEGACY2}'s picker lists ${PHASE}" while ${LEGACY} has the link`).toContain(PHASE)
    expect(title.filter((m) => m.modality !== null).map((m) => m.id).sort(), '"every milestone of the version" at the title screen')
      .toEqual(heartGoldIds().sort())
    await shotAll('09-title-screen', pages)
    await cancelBot(titleCard)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
