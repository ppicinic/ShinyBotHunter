// hgss-existing-milestones (docs/specs/milestone-builder-rework.md, owner's answer 5 and defaults 1, 11; #621 R8a): the 20
// existing milestones of production's export, imported into the local QA PhaseInfos and removed after, read and hunt as
// before with no location; attach and detach change only the location; a hunt from a set anchor once attached. Then the
// seeded 1-1 Starters ticked, archived and attached. Run line: `npx playwright test hgss-existing-milestones`.
import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ANCHOR_HUNT, assertLocalStores, attach, cancelEventAndBot, cardWindow, clearAnchor, clockConfig, detach, importPhases, keepExisting, launchHgss,
  milestoneDoc, phaseInfo, PHASES_FIXTURE, picker, rowsFor, seen, setAnchorHere, shutdown, stageCopy, START_TILE, startFrame, startWild,
  subscribe, teardown, writeLeadExtension, type Frame
} from './milestones'
import { backendLog, MINUTE } from './static'

const HANDLE = 'HG-Exist'
const ROUTE30_PHASE = '1-4 Route 30'
const ROUTE32_PHASE = '1-8 Route 32'
const STARTERS = '1-1 Starters'
const UNKNOWN = 'QA No Such Milestone'
// Backfill.Suggestions (ShinyBotHunter/Milestones/Backfill.cs): the suggested location of each existing phase that has one.
const SUGGESTED: Record<string, string> = {
  '1-2 Route 29': '33-land', '1-2c Route 29': '33-land', '1-3 Route 46': '48-land', '1-3b Route 46': '48-land',
  '1-4 Route 30': '34-land', '1-4b Route 30': '34-land', '1-4c Route 30': '34-land', '1-6 Dark Cave': '176-land',
  '1-6b Dark Cave': '176-land', '1-8 Route 32': '36-land', '1-8b Route 32': '36-land', '1-9 Sprout Tower': '155-land',
  '1-10 Ruins of Alph': '315-land', '2-2c Slowpoke Well': '177-land', '2-3c Azalea Town Headbutt': '74-headbutt'
}
const NO_LOCATION = 'Location: none. No anchor, no shares, no markers.'
// The save's tile (qa-automation.md, QA save files: hgss.qa-route32).
const SAVE_TILE = [36, 474, 294]
// BattleOrchestratorEvent, at every wild battle's start.
const ENCOUNTER = /, isShiny: (True|False)\s*$/

type FixtureRow = { Name: string; Amount: unknown; FormCode: string; RequireSpecificForm: boolean }
type Fixture = { GameName: 'HGSS' | 'RS'; Name: string; Pokemon: FixtureRow[] }
// The export is extended JSON: a number is { "$numberInt": "1" }.
const num = (v: unknown): number => (typeof v === 'object' && v !== null ? Number(Object.values(v)[0]) : Number(v))

test('hgss-existing-milestones: the 20 read as before; attach and detach; a hunt with no location, then from a set anchor; Starters', async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(fs.existsSync(PHASES_FIXTURE), `missing ${PHASES_FIXTURE}: the orchestrator stages the production-phases fixture (qa-automation.md, preconditions)`).toBe(true)
  const fixture = JSON.parse(fs.readFileSync(PHASES_FIXTURE, 'utf8')) as Fixture[]
  const hgssNames = fixture.filter((p) => p.GameName === 'HGSS').map((p) => p.Name)
  step(`fixture: ${fixture.length} phases, ${hgssNames.length} HGSS: ${hgssNames.join(', ')}`)
  stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
  clockConfig('2026-11-30T12:00:00')
  try {
    // Before the backend starts: it reads the phases once at start-up.
    importPhases(PHASES_FIXTURE)
    const admin = await startFrame(frame)
    const app = frame.app!
    const pages: Record<string, Page> = { admin }

    // (a) No emulator. Each of the 20: the fixture's rows and amounts, no location, the suggestion as Backfill's table.
    for (const p of fixture) {
      const res = await phaseInfo(p.GameName, p.Name)
      expect(res.status, `GET phaseinfo ${p.GameName} ${p.Name}`).toBe(200)
      const rows = res.json.phaseInfo.pokemon as { name: string; amount: number; formCode: string; requireSpecificForm: boolean }[]
      expect(rows.map((r) => [r.name, r.amount, r.requireSpecificForm]), `"${p.Name}: the fixture's rows and amounts"`)
        .toEqual(p.Pokemon.map((r) => [r.Name, num(r.Amount), r.RequireSpecificForm]))
      p.Pokemon.forEach((r, i) => {
        if (r.FormCode !== '') expect(rows[i].formCode, `${p.Name} row ${i + 1}: the form code`).toBe(r.FormCode)
      })
      expect(res.json.location, `"${p.Name}: location null"`).toBeNull()
      expect(res.json.suggestedLocation, `"${p.Name}: suggestedLocation as in Backfill's table"`).toBe(SUGGESTED[p.Name] ?? null)
    }

    // The editor on 1-4 Route 30: no location, the suggestion; Attach and Detach change only the location.
    await admin.getByRole('button', { name: 'Milestone Editor', exact: true }).click()
    const selects = admin.getByRole('heading', { name: 'Select Milestone', exact: true }).locator('xpath=following-sibling::div[1]').locator('select')
    await selects.first().selectOption('HGSS')
    await selects.nth(1).selectOption(ROUTE30_PHASE)
    await expect(admin.getByText(NO_LOCATION, { exact: true }), `"${ROUTE30_PHASE} reads ${NO_LOCATION}"`).toBeVisible({ timeout: 30_000 })
    await expect(admin.locator('select:has(option[value="34-land"]) option:checked'), '"the attach select reads Route 30 · grass (suggested)"')
      .toHaveText('Route 30 · grass (suggested)', { timeout: 30_000 })
    keepExisting('HGSS', ROUTE30_PHASE)
    const before = JSON.stringify(milestoneDoc('HGSS', ROUTE30_PHASE).Pokemon)
    const markers = (): Promise<number> => admin.locator('table:has(th:text-is("Version")) tbody td:nth-child(2) span').count()
    expect(await markers(), 'no markers before the attach').toBe(0)
    await admin.getByRole('button', { name: 'Attach to a location', exact: true }).click()
    await expect(admin.getByText('Location: Route 30 · grass', { exact: true }), '"Location: Route 30 · grass"').toBeVisible({ timeout: 30_000 })
    await expect.poll(markers, { message: '"the markers appear"', timeout: 30_000 }).toBeGreaterThan(0)
    const attached = milestoneDoc('HGSS', ROUTE30_PHASE)
    expect([attached.Location, JSON.stringify(attached.Pokemon)], '"the Mongo rows are equal to before"').toEqual(['34-land', before])
    await shotAll('01-attached', pages)
    await admin.getByRole('button', { name: 'Detach', exact: true }).click()
    await expect(admin.getByText(NO_LOCATION, { exact: true }), '"Detach: back to none"').toBeVisible({ timeout: 30_000 })
    const detached = milestoneDoc('HGSS', ROUTE30_PHASE)
    expect([detached.Location, JSON.stringify(detached.Pokemon)], '"Detach: the rows equal"').toEqual([null, before])
    expect((await attach('HGSS', ROUTE30_PHASE, '999-land')).status, '"attach to 999-land gives 404"').toBe(404)
    expect((await attach('HGSS', UNKNOWN, '34-land')).status, '"attach on an unknown name gives 404"').toBe(404)
    expect((await detach('HGSS', UNKNOWN)).status, '"detach on an unknown name gives 404"').toBe(404)

    // (b) The emulator: the picker, a hunt with no location, then attached with a set anchor.
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    let card = await launchHgss(admin, HANDLE, { clock: true })
    // The save's lead is at 1 HP (QA save files); RAM only, never saved.
    await writeLeadExtension({ hp: 30 })
    const res = await picker(0)
    expect(res.status, 'GET bot/milestones/0').toBe(200)
    const list = res.json.milestones as { name: string; modality: string | null; anchor: unknown }[]
    for (const name of hgssNames) {
      expect(list.find((m) => m.name === name), `"the picker lists ${name}, with modality and anchor null"`)
        .toMatchObject({ name, modality: null, anchor: null })
    }
    expect((await subscribe({ game: 'HGSS', phase: ROUTE32_PHASE, gameName: HANDLE })).status, `subscribe ${ROUTE32_PHASE} for ${HANDLE}`).toBe(200)
    const fixed = await cardWindow(app, `HGSS — ${ROUTE32_PHASE} (${HANDLE})`)
    pages.card = fixed
    await shotAll('02-launched', pages)

    const runStart = await startWild(card, { phase: ROUTE32_PHASE, target: 'ShinyTarget', mode: 'Run', yRange: 1 })
    const tile = START_TILE.exec((await seen(START_TILE, 1, MINUTE)).line)!
    expect([Number(tile[1]), Number(tile[2]), Number(tile[3])], '"START_TILE map=36 (474,294)"').toEqual(SAVE_TILE)
    await seen(ENCOUNTER, 3, 10 * MINUTE)
    await shotAll('03-hunting', pages)
    // The encounter line is logged at a battle's start and Cancel flees nothing, so the bot may stop inside a battle; Set
    // anchor here and the next Start need the free field, so the bot is closed and launched again on the same save.
    await cancelEventAndBot(card)
    const log = backendLog()
    expect(log.filter((l) => ANCHOR_HUNT.test(l)), '"no ANCHOR_HUNT" with no location').toEqual([])
    const encounters = log.filter((l) => ENCOUNTER.test(l)).length
    await expect.poll(() => rowsFor(HANDLE, ROUTE32_PHASE, runStart).length, { message: `"${encounters} rows under ${ROUTE32_PHASE}"`, timeout: 10_000, intervals: [1000] })
      .toBe(encounters)
    await expect.poll(async () => Number((await fixed.getByTestId('phase-encounter-count').innerText()).replace(/,/g, '')),
      { message: '"which its fixed card counts"', timeout: 2 * MINUTE, intervals: [2000] }).toBe(encounters)
    await expect(fixed.getByTestId('time-pill'), '"no time-pill" with no location').toHaveCount(0)
    await expect(fixed.getByTestId('row-share'), '"no row-share" with no location').toHaveCount(0)
    await shotAll('04-counted', pages)

    // The second launch: no scenario saves in-game, so the handle's SaveRAM is still the pristine copy, on the save's tile.
    card = await launchHgss(admin, HANDLE, { clock: true })
    await writeLeadExtension({ hp: 30 })
    keepExisting('HGSS', ROUTE32_PHASE)
    expect((await attach('HGSS', ROUTE32_PHASE, '36-land')).status, `attach ${ROUTE32_PHASE} to 36-land`).toBe(204)
    const set = await setAnchorHere(0, ROUTE32_PHASE, 0, 1)
    expect(set.status, 'Set anchor here (0, 1)').toBe(200)
    expect([set.json.anchor.map, set.json.anchor.x, set.json.anchor.y], '"the anchor (474, 294)", the save\'s tile').toEqual(SAVE_TILE)
    await startWild(card, { phase: ROUTE32_PHASE, target: 'ShinyTarget', mode: 'Run', yRange: 1 })
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
    expect(hunt.slice(1).map(String), '"ANCHOR_HUNT Route 32 · grass: hunting from the anchor ..., run up-down ±1 (set on this file)"')
      .toEqual(['Route 32 · grass', ...SAVE_TILE.map(String), 'run up-down ±1', 'set on this file'])
    await expect.poll(async () => [await fixed.getByTestId('time-pill').count() > 0, await fixed.getByTestId('row-share').count() > 0],
      { message: '"the card now shows time-pill and row-share"', timeout: 3 * MINUTE, intervals: [2000] }).toEqual([true, true])
    await shotAll('05-anchored', pages)
    // Clear and Detach need no free field.
    await cancelEventAndBot(card, async () => {
      expect((await clearAnchor(0, ROUTE32_PHASE)).status, 'Clear').toBe(204)
      expect((await detach('HGSS', ROUTE32_PHASE)).status, 'Detach').toBe(204)
    })

    // Default 11 on the seeded 1-1 Starters: ticked and archived through the editor, attached and detached.
    keepExisting('HGSS', STARTERS)
    await admin.getByRole('button', { name: 'Milestone Editor', exact: true }).click()
    await selects.first().selectOption('HGSS')
    await selects.nth(1).selectOption(STARTERS)
    await expect(admin.getByText('This milestone is loaded from disk: its Pokémon cannot be edited.', { exact: true }), `${STARTERS} did not load`)
      .toBeVisible({ timeout: 30_000 })
    for (const label of [HANDLE, 'Archived: hide from the launch form']) {
      const box = admin.getByLabel(label, { exact: true })
      await box.click()
      await expect(box, label).toBeChecked()
    }
    await admin.getByRole('button', { name: 'Save Milestone', exact: true }).click()
    await expect(admin.getByText(`Saved "${STARTERS}" successfully`, { exact: true }), `${STARTERS} saved`).toBeVisible({ timeout: 30_000 })
    const starters = milestoneDoc('HGSS', STARTERS)
    expect([starters.GameFiles, starters.Archived], `"${STARTERS}: the tick and archive are stored"`).toEqual([[HANDLE], true])
    expect((await attach('HGSS', STARTERS, '34-land')).status, `"${STARTERS}: attach gives 204"`).toBe(204)
    expect((await detach('HGSS', STARTERS)).status, `"${STARTERS}: detach gives 204"`).toBe(204)
    await shotAll('06-starters', pages)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
