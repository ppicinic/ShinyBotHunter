// hgss-goal-rows (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1d): no emulator, the backend and the dashboard
// only. Three throwaway HeartGold files with no save, one preset each, seen on one milestone's fixed cards (FR-8, FR-10, FR-12),
// the Goals tab's preset dropdown (#582), the goal editor's refusals, a hidden row, Ruins of Alph's 28 Unown rows, the
// not-found sentence and the species lists. Run line: `npx playwright test hgss-goal-rows` (it needs port 5000).
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import { shotAll, step, windowByTitle } from './driver'
import {
  assertLocalStores, deleteGameFile, gameFileCount, rows, setHidden, shutdown, stageCopy, startFrame, subscribe, teardown, type Frame
} from './milestones'
import { API, MINUTE } from './static'

const ROM = 'HG-Route32.nds'
const DEX = 'HG-QA-Dex'
const FEMALE = 'HG-QA-Female'
const EXCL = 'HG-QA-Excl'
const RS_FILE = 'SA-Wild'
// Route 30 · grass and Ruins of Alph (underground hall), HeartGold's generated ids (Data/HGSS/milestones.json.gz).
const ROUTE30 = '34-land'
const ALPH = '315-land'
const UNKNOWN = '999-land'
const CATERPIE = 10
const PIDGEY = 16
const RATTATA = 19
const UNOWN = 201
const LABELS: Record<string, string> = { None: 'None', LivingDex: 'Living dex', FemaleForms: 'Female forms', VersionExclusives: 'Version exclusives' }
const BAD_AMOUNT = 'Amount is a whole number, 0 or more.'
// PhaseMetadataController.GoalError for an any-form Unown beside the 28 letters.
const MIXED = `Species ${UNOWN} has both an any-form entry and form entries`
// The rows reread on the dashboard's polling interval (config.json, 60 s) when nothing pushes.
const REREAD = 90_000

type Entry = { species: number; formCode: string | null; amount: number }
type GameFile = { id: string; entries: Entry[]; preset: string }
type MilestoneRow = {
  species: number; key: string; formCode: string | null; name: string; hasFemaleForm: boolean; wanted: number; caught: number
  hidden: boolean; shinyEncountersOnFile: number
}

async function goal(handle: string): Promise<GameFile> {
  const file = (await (await fetch(`${API}/goal/${encodeURIComponent(handle)}`)).json()) as GameFile
  step(`GET goal/${handle}: preset ${file.preset}, ${file.entries.length} entries`)
  return file
}

const entryText = (e: Entry): string => `${e.species}/${e.formCode ?? 'any'}/${e.amount}`
const withoutSpecies = (entries: Entry[], species: number): string[] => entries.filter((e) => e.species !== species).map(entryText).sort()

// A row as the card draws it (PhaseCard milestoneCardRows): the name with its gender mark, then caught/wanted or "tracked".
const gender = (r: MilestoneRow): string => (r.formCode === null ? '' : r.formCode.endsWith(';f') ? '♀' : r.hasFemaleForm ? '♂' : '')
function drawn(r: MilestoneRow): string {
  const missed = Math.max(0, r.shinyEncountersOnFile - r.caught)
  return `${r.name}${gender(r)} ${r.wanted === 0 ? `${r.caught}tracked` : `${r.caught}/${r.wanted}`}${missed > 0 ? `(${missed}missed)` : ''}`.toLowerCase()
}

type CardView = { title: string; headers: string[]; rows: { line: string; sprite: number | null }[]; body: string; footer: string }
/** The card's header, table and footer from its window's DOM; whitespace in the last column dropped, as `drawn` writes it. */
async function readCard(page: Page): Promise<CardView> {
  return page.evaluate(() => {
    const t = (e: Element | null | undefined): string => (e?.textContent ?? '').replace(/\s+/g, ' ').trim()
    const card = document.querySelector('.dashboard-card')
    const rows = Array.from(document.querySelectorAll('tbody tr')).map((tr) => {
      const td = tr.querySelectorAll('td')
      const img = td[0]?.querySelector('img')
      return {
        // The name cell's spans only: a card with shares draws the share under the name (PhaseCard).
        line: `${Array.from(td[1]?.querySelectorAll('span') ?? []).map(t).join('').replace('✓', '').trim()} ${t(td[td.length - 1]).replace(/\s+/g, '')}`.toLowerCase(),
        sprite: img ? img.naturalWidth : null
      }
    })
    return {
      title: t(card?.querySelector('.card-header-title')), headers: Array.from(document.querySelectorAll('thead th')).map(t), rows,
      body: t(card?.children[1]), footer: t(card?.lastElementChild)
    }
  })
}

/** The fixed card on `milestone` for `handle`: subscribed, its window found by title, and returned once its table is drawn. */
async function fixedCard(app: ElectronApplication, milestone: string, handle: string): Promise<Page> {
  expect((await subscribe({ game: 'HGSS', phase: milestone, gameName: handle })).status, `subscribe ${milestone} for ${handle}`).toBe(200)
  return windowByTitle(app, `HGSS — ${milestone} (${handle})`, MINUTE)
}

async function shownRows(handle: string, milestone: string): Promise<{ lines: string[]; rows: MilestoneRow[] }> {
  const res = await rows('HGSS', milestone, handle)
  expect(res.status, `GET milestone/HGSS/${milestone}/${handle}`).toBe(200)
  const all = res.json.rows as MilestoneRow[]
  return { lines: all.filter((r) => !r.hidden).map(drawn), rows: all }
}

test('hgss-goal-rows: three presets on three files give three sets of rows; the preset dropdown, refusals, a hidden row, 28 Unown rows', async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  for (const handle of [DEX, FEMALE, EXCL]) {
    deleteGameFile(handle)
    stageCopy(handle, { rom: ROM, save: null })
  }
  try {
    const admin = await startFrame(frame)
    const app = frame.app!
    const pages: Record<string, Page> = { admin }
    const tab = admin.getByRole('button', { name: 'Goals', exact: true })
    const fileSelect = admin.getByRole('heading', { name: 'Game file', exact: true }).locator('xpath=following-sibling::select')
    const presetSelect = admin.getByRole('heading', { name: 'Preset', exact: true }).locator('xpath=following-sibling::select')
    const yellow = admin.getByRole('heading', { name: 'Preset', exact: true }).locator('xpath=following-sibling::div')
    const entriesHeading = admin.getByRole('heading', { name: /^Entries \(\d+\)$/ })
    // The preset's own message, so the one before it on the tab never passes for it.
    const feedback = (preset: string): ReturnType<Page['getByText']> =>
      admin.getByText(new RegExp(`^${LABELS[preset]} chosen: \\d+ species changed, \\d+ hand-edited species kept\\.$`))
    const save = admin.getByRole('button', { name: 'Save goal', exact: true })
    const line = (species: number): ReturnType<Page['locator']> =>
      admin.locator('div.flex.gap-2.items-center').filter({ has: admin.getByText(`#${species}`, { exact: true }) })
    // Waits for the file's own entry count, so the file shown before it is never taken for it.
    const open = async (handle: string): Promise<void> => {
      const count = (await goal(handle)).entries.length
      await fileSelect.selectOption(handle)
      await expect(entriesHeading, `${handle}: the goal did not load`).toHaveText(`Entries (${count})`, { timeout: 30_000 })
    }
    const choose = async (handle: string, preset: string): Promise<{ changed: number; kept: number }> => {
      await presetSelect.selectOption(preset)
      await expect(yellow, `${handle}: no confirmation for ${LABELS[preset]}`).toBeVisible()
      await yellow.getByRole('button', { name: `Apply ${LABELS[preset]}`, exact: true }).click()
      await expect(feedback(preset), `${handle}: no message after ${LABELS[preset]}`).toBeVisible({ timeout: 30_000 })
      const text = (await feedback(preset).innerText()).trim()
      step(`${handle}: "${text}"`)
      const m = /chosen: (\d+) species changed, (\d+) hand-edited species kept\.$/.exec(text)!
      await expect(presetSelect, `${handle}: the select after ${LABELS[preset]}`).toHaveValue(preset)
      return { changed: Number(m[1]), kept: Number(m[2]) }
    }

    // (a) Opening the Goals tab and a file with no document reads only.
    expect(gameFileCount(DEX), `"opening writes nothing": a GameFiles document of ${DEX} before the tab opened`).toBe(0)
    await tab.click()
    await open(DEX)
    await expect(admin.getByText('No entries. Pick a preset or add one below.'), `${DEX}: an empty goal`).toBeVisible()
    await shotAll('01-goals-empty', pages)
    expect(gameFileCount(DEX), `"opening writes nothing": a GameFiles document of ${DEX} after the tab opened`).toBe(0)

    // (b) The three presets through the dropdown, each saved by its Apply.
    const presets: [string, string][] = [[DEX, 'LivingDex'], [FEMALE, 'FemaleForms'], [EXCL, 'VersionExclusives']]
    const saved: Record<string, GameFile> = {}
    for (const [handle, preset] of presets) {
      await open(handle)
      const { kept } = await choose(handle, preset)
      expect(kept, `${handle}: hand-edited species kept from an empty goal`).toBe(0)
      saved[handle] = await goal(handle)
      expect([saved[handle].preset, saved[handle].entries.length > 0], `${handle}: the stored goal after ${LABELS[preset]}`).toEqual([preset, true])
      await shotAll(`02-preset-${preset}`, pages)
    }
    // A quick switch between two files shows only the last one's goal.
    await fileSelect.selectOption(DEX)
    await fileSelect.selectOption(EXCL)
    await expect(entriesHeading, '"a quick switch shows only the last one\'s goal"').toHaveText(`Entries (${saved[EXCL].entries.length})`, { timeout: 30_000 })
    await expect(presetSelect, '"a quick switch shows only the last one\'s goal": the preset').toHaveValue('VersionExclusives')

    // (c) Three fixed cards on Route 30 · grass, one per file: each card's rows are its file's, and the three sets differ.
    const sets: Record<string, string[]> = {}
    for (const [handle] of presets) {
      const page = await fixedCard(app, ROUTE30, handle)
      pages[handle] = page
      const { lines, rows: all } = await shownRows(handle, ROUTE30)
      await expect.poll(async () => (await readCard(page)).rows.map((r) => r.line), { message: `${handle}: the card's rows are not the server's`, timeout: MINUTE })
        .toEqual(lines)
      sets[handle] = all.map((r) => `${r.key}:${r.wanted}`)
      if (handle === FEMALE) {
        const rattata = all.filter((r) => r.species === RATTATA)
        step(`${FEMALE} Rattata rows: ${JSON.stringify(rattata)}`)
        // [female form, wanted], sorted: the male row at 0, the female row wanted.
        expect(rattata.map((r) => [r.formCode?.endsWith(';f') ?? false, r.wanted > 0]).sort(), '"on Female, Rattata is two rows, the female form wanted and the male at 0"')
          .toEqual([[false, false], [true, true]])
      }
    }
    await shotAll('03-route30-cards', pages)
    for (const [a, b] of [[DEX, FEMALE], [DEX, EXCL], [FEMALE, EXCL]]) {
      expect(sets[a], `"the three sets differ pairwise": ${a} and ${b}`).not.toEqual(sets[b])
    }

    // (d) FR-8 on Dex: a saved hand edit survives a change of preset; the select is greyed while an edit is unsaved; Cancel.
    await tab.click()
    await open(DEX)
    const pidgey = line(PIDGEY).locator('input[type="number"]')
    await pidgey.fill('6')
    await expect(presetSelect, '"the select is greyed while an edit is unsaved"').toBeDisabled()
    await shotAll('04-unsaved', pages)
    await save.click()
    await expect(admin.getByText(`Saved the goal of ${DEX}`), `${DEX}: Pidgey 6 not saved`).toBeVisible({ timeout: 30_000 })
    await expect(presetSelect, 'the select after the save').toBeEnabled()
    const toExcl = await choose(DEX, 'VersionExclusives')
    const afterExcl = await goal(DEX)
    expect(toExcl.kept, '"the message gives both counts": hand-edited species kept').toBe(1)
    expect(afterExcl.entries.filter((e) => e.species === PIDGEY).map(entryText), '"the edit is kept": Pidgey after Version exclusives').toEqual([`${PIDGEY}/any/6`])
    expect(withoutSpecies(afterExcl.entries, PIDGEY), '"the rest follow": every other entry is Version exclusives\'')
      .toEqual(withoutSpecies(saved[EXCL].entries, PIDGEY))
    await expect(pidgey, 'Pidgey on the tab after Version exclusives').toHaveValue('6')
    await shotAll('05-kept-on-switch', pages)
    const back = await choose(DEX, 'LivingDex')
    const afterBack = await goal(DEX)
    expect(back.kept, 'switched back: hand-edited species kept').toBe(1)
    expect(afterBack.entries.filter((e) => e.species === PIDGEY).map(entryText), 'switched back: Pidgey').toEqual([`${PIDGEY}/any/6`])
    expect(withoutSpecies(afterBack.entries, PIDGEY), 'switched back: every other entry is Living dex\'').toEqual(withoutSpecies(saved[DEX].entries, PIDGEY))
    await presetSelect.selectOption('VersionExclusives')
    await expect(yellow, 'the confirmation for the Cancel').toBeVisible()
    await shotAll('06-confirm', pages)
    await yellow.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(yellow, '"Cancel in the yellow box": the box').toHaveCount(0)
    await expect(presetSelect, '"Cancel in the yellow box puts the select back"').toHaveValue('LivingDex')
    expect((await goal(DEX)).preset, 'Cancel wrote nothing').toBe('LivingDex')

    // (e) 1.5 shows the red sentence and holds Save; an any-form Unown beside the 28 letters is the server's 400 sentence.
    await pidgey.fill('1.5')
    await expect(line(PIDGEY).getByText(BAD_AMOUNT, { exact: true }), '"1.5 shows the red sentence"').toBeVisible()
    await expect(save, '"1.5 holds Save"').toBeDisabled()
    await shotAll('07-bad-amount', pages)
    await pidgey.fill('6')
    await expect(save, 'Save after the amount is whole again').toBeEnabled()
    await admin.getByPlaceholder('e.g. Aipom').fill('Unown')
    await admin.getByRole('button', { name: '+ Add', exact: true }).click()
    await save.click()
    await expect(admin.getByText(MIXED, { exact: true }), '"a refused save shows the server\'s 400 sentence"').toBeVisible({ timeout: 30_000 })
    await shotAll('08-refused-save', pages)
    await line(UNOWN).filter({ has: admin.getByText('any form', { exact: true }) }).getByTitle('Remove').click()
    await save.click()
    await expect(admin.getByText(`Saved the goal of ${DEX}`), `${DEX}: the goal without the any-form Unown not saved`).toBeVisible({ timeout: 30_000 })
    expect(withoutSpecies((await goal(DEX)).entries, PIDGEY), `${DEX} after the refusal`).toEqual(withoutSpecies(saved[DEX].entries, PIDGEY))

    // (f) Caterpie hidden on Dex: the Dex card drops the row, the Female card keeps it.
    expect((await setHidden(DEX, { milestoneId: ROUTE30, key: `#${CATERPIE}` }, true)).status, 'hide Caterpie on Dex').toBe(204)
    const dex = await shownRows(DEX, ROUTE30)
    expect(dex.rows.find((r) => r.species === CATERPIE)?.hidden, 'the server\'s Caterpie row on Dex').toBe(true)
    await expect.poll(async () => (await readCard(pages[DEX])).rows.map((r) => r.line), { message: '"the Dex card drops the row"', timeout: REREAD, intervals: [2000] })
      .toEqual(dex.lines)
    const female = await shownRows(FEMALE, ROUTE30)
    expect(female.rows.filter((r) => r.species === CATERPIE).map((r) => r.hidden), '"the Female card keeps it": the server').toEqual([false])
    expect((await readCard(pages[FEMALE])).rows.map((r) => r.line), '"the Female card keeps it"').toEqual(female.lines)
    await shotAll('09-hidden', pages)

    // (g) Ruins of Alph (underground hall) on Dex: 28 Unown rows; a missing sprite is logged, not failed (A6).
    const alph = await fixedCard(app, ALPH, DEX)
    pages.alph = alph
    const alphRows = await shownRows(DEX, ALPH)
    await expect.poll(async () => (await readCard(alph)).rows.map((r) => r.line), { message: 'the Ruins of Alph card\'s rows are not the server\'s', timeout: MINUTE })
      .toEqual(alphRows.lines)
    expect(alphRows.rows.map((r) => r.species), '"28 Unown rows"').toEqual(Array(28).fill(UNOWN))
    const sprites = (await readCard(alph)).rows.map((r, i) => `${alphRows.rows[i].key}: ${r.sprite === null ? 'no img' : `naturalWidth ${r.sprite}`}`)
    step(`Ruins of Alph sprites: ${sprites.join(', ')}`)
    await shotAll('10-alph', pages)

    // (h) An unknown id: the not-found sentence.
    const unknown = await fixedCard(app, UNKNOWN, DEX)
    pages.unknown = unknown
    await expect.poll(async () => (await readCard(unknown)).body, { message: 'the unknown milestone\'s card', timeout: MINUTE })
      .toBe(`Milestone "${UNKNOWN}" was not found.`)
    await shotAll('11-not-found', pages)

    // (i) The species lists.
    for (const [game, count] of [['HGSS', 493], ['RS', 386]] as const) {
      const names = (await (await fetch(`${API}/species/${game}`)).json()) as { species: number; name: string }[]
      step(`GET species/${game}: ${names.length} names, last ${JSON.stringify(names.at(-1))}`)
      expect(names.map((n) => n.species), `GET species/${game}`).toEqual(Array.from({ length: count }, (_, i) => i + 1))
    }

    // (j) The RS file, nothing saved: no Female forms.
    const rsDocs = gameFileCount(RS_FILE)
    await tab.click()
    await expect(fileSelect.locator(`option[value="${RS_FILE}"]`), `${RS_FILE} is not in the Goals tab's game files (rs-wild preconditions)`).toBeAttached({ timeout: 30_000 })
    await open(RS_FILE)
    expect(await presetSelect.locator('option').evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value)), '"the dropdown offers no Female forms" on RS')
      .toEqual(['None', 'LivingDex', 'VersionExclusives'])
    await shotAll('12-rs-presets', pages)
    expect(gameFileCount(RS_FILE), `${RS_FILE}: "nothing saved"`).toBe(rsDocs)

    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
