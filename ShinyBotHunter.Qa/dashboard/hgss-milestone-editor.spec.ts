// hgss-milestone-editor (docs/specs/milestone-builder-rework.md, model 1 to 7 and defaults 1, 2, 5; #621 R8a): a milestone
// made from the game data end to end. The Milestone Editor with no emulator: New from game data on Route 30 · grass, the
// version filter and its marks, the When markers, the presets (a typed amount kept), Ruins of Alph's 28 Unown rows, the
// Save and the taken name, the game-file ticks and Archived read back from Mongo; the milestone's fixed card; then the
// launch list of two copies at the title screen. Run line: `npx playwright test hgss-milestone-editor`.
import { expect, test, type Locator, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  assertLocalStores, cancelBot, cardWindow, claimMilestone, launchHgss, milestoneDoc, phaseInfo, picker, seed, shutdown, stageCopy,
  startFrame, subscribe, teardown, WILD, type Frame
} from './milestones'
import { API, MINUTE } from './static'

const HG = 'HG-Edit'
const SS = 'SS-Edit'
// Route 30 · grass and Ruins of Alph (underground hall), in Data/HGSS/milestones.json.gz.
const ROUTE30 = '34-land'
const ROUTE30_NAME = 'Route 30 · grass'
const ALPH = '315-land'
const SAVED = 'QA Edit Route 30'
const ARCHIVED = 'QA Edit Archived'
const ANY = 'QA Edit Any'
const SS_ONLY = 'QA Edit SS'
// Route 30's species in the data file: each version's slots, their conditions and the four swarm-sound slots (all day).
const MARKERS: Record<string, string[]> = {
  Pidgey: ['morning', 'day'], Caterpie: ['morning', 'day'], Metapod: ['morning', 'day'], Weedle: ['morning', 'day'], Kakuna: ['morning', 'day'],
  Rattata: ['night'], Hoothoot: ['night'], Spinarak: ['night'], Ledyba: ['morning'],
  Whismur: ['all day'], Linoone: ['all day'], Buizel: ['all day'], Bidoof: ['all day']
}
const VERSION: Record<string, string> = {
  Caterpie: 'HeartGold', Metapod: 'HeartGold', Spinarak: 'HeartGold', Weedle: 'SoulSilver', Kakuna: 'SoulSilver', Ledyba: 'SoulSilver'
}
const LEDYBA = 165

type EditorRow = { name: string; formCode: string; markers: string[]; version: string; amount: number }
type SeedRow = { speciesId: number; name: string; formCode: string; hasFemaleForm: boolean; amount: number; times: string[] }

const sorted = (xs: string[]): string[] => [...xs].sort()
// The editor's and the card's marker for a row's times (PhaseEditorTab markerLabels; PhaseCard TimePill).
const labels = (times: string[]): string[] => (times.length === 3 ? ['all day'] : times.map((t) => t.toLowerCase()))

/** The editor's Pokémon table, read from the DOM: the name and form inputs, the When pills, the Version cell, the amount. */
function editorRows(admin: Page): Promise<EditorRow[]> {
  return admin.locator('table:has(th:text-is("Version")) tbody tr').evaluateAll((trs) => trs.map((tr) => {
    const td = tr.querySelectorAll('td')
    const texts = td[0].querySelectorAll('input[type="text"]')
    return {
      name: (texts[0] as HTMLInputElement).value, formCode: (texts[1] as HTMLInputElement).value,
      markers: Array.from(td[1].querySelectorAll('span')).map((s) => (s.textContent ?? '').trim()),
      version: (td[2].textContent ?? '').trim(), amount: Number((td[3].querySelector('input') as HTMLInputElement).value)
    }
  }))
}

async function seedRows(location: string, preset: 'LivingDex' | 'FemaleForms' | 'VersionExclusives', version?: 'HeartGold'): Promise<SeedRow[]> {
  const res = await seed('HGSS', location, preset, version)
  expect(res.status, `the seed of ${location} at ${preset}`).toBe(200)
  return res.json.rows as SeedRow[]
}

test('hgss-milestone-editor: New from game data, presets, Save, ticks and archive, the card, the launch lists', async () => {
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  stageCopy(HG, { rom: 'HG-Route32.nds', save: null })
  stageCopy(SS, { rom: 'Pokemon SoulSilver.nds', save: null })
  try {
    const admin = await startFrame(frame)
    const app = frame.app!
    const pages: Record<string, Page> = { admin }
    const table = admin.locator('table:has(th:text-is("Version")) tbody tr')
    const selectors = admin.getByRole('heading', { name: 'Select Milestone', exact: true }).locator('xpath=following-sibling::div[1]')
    const milestoneSelect = selectors.locator('select').nth(1)
    const newFromData = admin.getByRole('button', { name: 'New from game data', exact: true })
    // In a draft the location select is the one select that lists location ids; the preset select the one with Female forms.
    const locationSelect = admin.locator(`select:has(option[value="${ROUTE30}"])`)
    const presetSelect = admin.locator('select:has(option[value="VersionExclusives"])')
    const nameInput = admin.getByPlaceholder('New milestone name')
    const save = admin.getByRole('button', { name: 'Save Milestone', exact: true })
    const saved = (name: string): Locator => admin.getByText(`Saved "${name}" successfully`, { exact: true })
    // A new milestone's Save shows its sentence, then rereads the metadata before it selects the saved milestone and drops
    // the draft; the next click waits for that, so a late reset cannot undo it.
    const savedNew = async (name: string): Promise<void> => {
      await expect(saved(name), `"Saved "${name}" successfully"`).toBeVisible({ timeout: 30_000 })
      await expect(milestoneSelect, `the editor did not select ${name} after its Save`).toHaveValue(name, { timeout: 30_000 })
    }
    const rowsShown = async (n: number, what: string): Promise<EditorRow[]> => {
      await expect.poll(async () => (await editorRows(admin)).length, { message: what, timeout: 30_000 }).toBe(n)
      return editorRows(admin)
    }
    const rowOf = async (name: string): Promise<Locator> => {
      const i = (await editorRows(admin)).findIndex((r) => r.name === name)
      expect(i, `no ${name} row in the editor`).toBeGreaterThan(-1)
      return table.nth(i)
    }
    const openEditor = async (): Promise<void> => {
      await admin.getByRole('button', { name: 'Milestone Editor', exact: true }).click()
      await selectors.locator('select').first().selectOption('HGSS')
      await expect(newFromData, 'the locations did not load').toBeEnabled({ timeout: 30_000 })
    }
    const startDraft = async (location: string): Promise<void> => {
      await newFromData.click()
      await locationSelect.selectOption(location)
    }

    // (a) New from game data on Route 30 · grass: the prefilled name, 13 rows for both versions with their marks and markers.
    await openEditor()
    await startDraft(ROUTE30)
    await expect(nameInput, '"the name is prefilled"').toHaveValue(ROUTE30_NAME)
    const both = await rowsShown(13, '"Both versions: 13 rows"')
    step(`Route 30, both versions: ${JSON.stringify(both)}`)
    expect(sorted(both.map((r) => r.name)), 'Route 30\'s species').toEqual(sorted(Object.keys(MARKERS)))
    for (const r of both) {
      expect(r.version, `"the version marks": ${r.name}`).toBe(VERSION[r.name] ?? 'both')
      expect(r.markers, `"the When markers": ${r.name}`).toEqual(MARKERS[r.name])
    }
    await shotAll('01-route30-both', pages)
    for (const version of ['HeartGold', 'SoulSilver']) {
      await admin.getByRole('button', { name: `${version} only`, exact: true }).click()
      const only = await rowsShown(10, `"${version} only: 10 rows"`)
      expect(only.filter((r) => r.version !== 'both' && r.version !== version), `${version} only: rows of the other version`).toEqual([])
    }
    await admin.getByRole('button', { name: 'Both versions', exact: true }).click()
    await rowsShown(13, 'back to both versions')

    // (b) Presets: a typed amount kept across a change of preset; four presets; Female forms; 28 Unown rows on Ruins of Alph.
    await expect(presetSelect, 'a new draft is Living dex').toHaveValue('LivingDex')
    await (await rowOf('Pidgey')).locator('input[type="number"]').fill('5')
    await presetSelect.selectOption('VersionExclusives')
    const exclusives = await seedRows(ROUTE30, 'VersionExclusives')
    const want = (rows: SeedRow[]): string[] => rows.map((r) => `${r.name}|${r.formCode}|${r.name === 'Pidgey' ? 5 : r.amount}`)
    await expect.poll(async () => (await editorRows(admin)).map((r) => `${r.name}|${r.formCode}|${r.amount}`),
      { message: '"Pidgey stays 5, and every other amount equals the Version exclusives seed"', timeout: 30_000 }).toEqual(want(exclusives))
    expect(await presetSelect.locator('option').evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value)), '"the preset select offers 4 options"')
      .toEqual(['None', 'LivingDex', 'FemaleForms', 'VersionExclusives'])
    await shotAll('02-version-exclusives', pages)
    await presetSelect.selectOption('FemaleForms')
    const female = await seedRows(ROUTE30, 'FemaleForms')
    const femaleRows = await rowsShown(female.length, 'the Female forms rows')
    const withFemale = [...new Set(female.filter((r) => r.hasFemaleForm).map((r) => r.name))]
    step(`Female forms: ${JSON.stringify(femaleRows)}; species with a female form: ${withFemale.join(', ')}`)
    expect(withFemale.length, 'Route 30 has a species with a female form').toBeGreaterThan(0)
    for (const name of withFemale) {
      const forms = femaleRows.filter((r) => r.name === name)
      expect(forms.filter((r) => r.formCode.endsWith(';f')).map((r) => r.amount >= 1), `"${name} has a ;f row at 1 or more"`).toEqual([true])
      expect(forms.filter((r) => !r.formCode.endsWith(';f')).map((r) => r.amount), `"${name}'s male row at 0"`).toEqual([0])
    }
    await shotAll('03-female-forms', pages)
    await presetSelect.selectOption('LivingDex')
    await locationSelect.selectOption(ALPH)
    const unown = await rowsShown(28, '"28 Unown rows"')
    expect(unown.map((r) => `${r.name} ${r.amount}`), '"28 Unown rows at 1"').toEqual(Array(28).fill('Unown 1'))
    expect(new Set(unown.map((r) => r.formCode)).size, '"with distinct form codes"').toBe(28)
    await shotAll('04-alph', pages)

    // (c) Save: Rattata removed, Ditto added, renamed; nothing stored before Save; the taken name refused.
    await locationSelect.selectOption(ROUTE30)
    await rowsShown(13, 'Route 30 again')
    await (await rowOf('Rattata')).getByTitle('Remove', { exact: true }).click()
    await admin.getByRole('button', { name: '+ Add', exact: true }).click()
    await table.last().locator('input[type="text"]').first().fill('Ditto')
    await nameInput.fill(SAVED)
    // Fails unless the PhaseInfos count for the name is 0, so it is the "before Save" check too.
    claimMilestone('HGSS', SAVED)
    await save.click()
    await savedNew(SAVED)
    const doc = milestoneDoc('HGSS', SAVED)
    expect(doc?.Location, `${SAVED}: Location`).toBe(ROUTE30)
    expect(doc.Pokemon.map((p: { Name: string }) => p.Name), `${SAVED}: "Ditto and no Rattata"`).toContain('Ditto')
    expect(doc.Pokemon.map((p: { Name: string }) => p.Name), `${SAVED}: "no Rattata"`).not.toContain('Rattata')
    await shotAll('05-saved', pages)
    await admin.getByRole('button', { name: 'New empty', exact: true }).click()
    await nameInput.fill(SAVED)
    await save.click()
    await expect(admin.getByText(`A milestone named "${SAVED}" already exists; choose another name.`, { exact: true }), '"the taken name"')
      .toBeVisible({ timeout: 30_000 })

    // (d) Ticks and archive, each read back from Mongo.
    await milestoneSelect.selectOption(SAVED)
    await expect(admin.getByText(`Location: ${ROUTE30_NAME}`, { exact: true }), `${SAVED} did not load`).toBeVisible({ timeout: 30_000 })
    const tick = admin.getByLabel(HG, { exact: true })
    await tick.click()
    await expect(tick).toBeChecked()
    await save.click()
    await expect(saved(SAVED)).toBeVisible({ timeout: 30_000 })
    expect(milestoneDoc('HGSS', SAVED).GameFiles, `"${SAVED} ticked for ${HG} only"`).toEqual([HG])
    for (const [name, archived] of [[ARCHIVED, true], [ANY, false]] as const) {
      await admin.getByRole('button', { name: 'New empty', exact: true }).click()
      await nameInput.fill(name)
      await table.first().locator('input[type="text"]').first().fill('Pidgey')
      if (archived) {
        const box = admin.getByLabel('Archived: hide from the launch form', { exact: true })
        await box.click()
        await expect(box).toBeChecked()
      }
      claimMilestone('HGSS', name)
      await save.click()
      await savedNew(name)
      const stored = milestoneDoc('HGSS', name)
      expect([stored.Location, stored.GameFiles, stored.Archived], `${name}: Location, GameFiles, Archived`).toEqual([null, [], archived])
    }
    await startDraft(ROUTE30)
    await presetSelect.selectOption('None')
    await expect.poll(async () => (await editorRows(admin)).map((r) => r.amount), { message: 'None: every amount 0', timeout: 30_000 }).toEqual(Array(13).fill(0))
    await (await rowOf('Ledyba')).locator('input[type="number"]').fill('1')
    await nameInput.fill(SS_ONLY)
    claimMilestone('HGSS', SS_ONLY)
    await save.click()
    await savedNew(SS_ONLY)
    const ssDoc = milestoneDoc('HGSS', SS_ONLY)
    expect([ssDoc.Location, ssDoc.GameFiles], `${SS_ONLY}: Location and no ticks`).toEqual([ROUTE30, []])
    expect(ssDoc.Pokemon.map((p: { Name: string; Amount: number }) => `${p.Name} ${p.Amount}`).sort(), `"${SS_ONLY}: Ledyba 1, every other row 0"`)
      .toEqual(Object.keys(MARKERS).map((n) => `${n} ${n === 'Ledyba' ? 1 : 0}`).sort())
    await shotAll('06-ticks-archive', pages)

    // (e) The fixed card on HG-Edit: the markers in HeartGold's scope, Ledyba with none; not live; no share, no hidden row.
    expect((await subscribe({ game: 'HGSS', phase: SAVED, gameName: HG })).status, `subscribe ${SAVED} for ${HG}`).toBe(200)
    const card = await cardWindow(app, `HGSS — ${SAVED} (${HG})`)
    pages.card = card
    const heartGold = new Map((await seedRows(ROUTE30, 'LivingDex', 'HeartGold')).map((r) => [r.speciesId, labels(r.times)]))
    const info = await phaseInfo('HGSS', SAVED, HG)
    expect(info.status, `GET phaseinfo ${SAVED} ${HG}`).toBe(200)
    const pokemon = info.json.phaseInfo.pokemon as { name: string; speciesId: number }[]
    const expected = pokemon.map((p) => `${p.name}: ${(heartGold.get(p.speciesId) ?? []).join(',')}`)
    const cardRows = (): Promise<string[]> => card.locator('tbody tr:not([data-testid="hidden-summary"])').evaluateAll((trs) => trs.map((tr) =>
      `${(tr.querySelectorAll('td')[1].querySelector('span')?.textContent ?? '').trim()}: ${Array.from(tr.querySelectorAll('[data-testid="time-pill"]')).map((s) => (s.textContent ?? '').trim()).join(',')}`))
    await expect.poll(cardRows, { message: '"each row\'s time-pill texts equal the editor\'s markers in HeartGold scope"', timeout: MINUTE }).toEqual(expected)
    expect(expected, '"Ledyba has none"').toContain(`${pokemon.find((p) => p.speciesId === LEDYBA)!.name}: `)
    await expect(card.locator('.card-header').getByText('not live', { exact: true }), '"the header shows not live"').toBeVisible()
    await expect(card.getByTestId('row-share'), '"no row-share"').toHaveCount(0)
    await expect(card.getByTestId('hidden-summary'), '"no hidden-summary"').toHaveCount(0)
    await shotAll('07-card', pages)

    // (f) The launch list at the title screen, on each file: the saved milestones offered there, and the form's select.
    const metadata = (await (await fetch(`${API}/metadata`)).json()) as { games: Record<string, { phases: { name: string }[] }> }
    const savedNames = metadata.games.HGSS.phases.map((p) => p.name)
    const locationIds = ((await (await fetch(`${API}/milestones/HGSS/locations`)).json()) as { id: string }[]).map((l) => l.id)
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    for (const [handle, listed, unlisted, ssComplete] of [[HG, [SAVED, ANY], [ARCHIVED], true], [SS, [ANY], [SAVED, ARCHIVED], false]] as const) {
      const bot = await launchHgss(admin, handle, { continue: false })
      const pos = Number((await bot.getAttribute('data-testid'))!.replace('emulator-card-', ''))
      const res = await picker(pos)
      expect(res.status, `GET bot/milestones/${pos}`).toBe(200)
      const list = res.json.milestones as { name: string; complete: boolean }[]
      const names = list.map((m) => m.name)
      for (const name of listed) expect(names, `"${handle} lists ${name}"`).toContain(name)
      for (const name of unlisted) expect(names, `"${handle} does not list ${name}"`).not.toContain(name)
      expect(names.filter((n) => !savedNames.includes(n)), `"every entry on ${handle} is a saved milestone of the metadata"`).toEqual([])
      expect(names.filter((n) => locationIds.includes(n)), `"none of the locations is listed" on ${handle}`).toEqual([])
      expect(list.find((m) => m.name === SS_ONLY)?.complete, `"${SS_ONLY} is complete: ${ssComplete} on ${handle}" (default 5)`).toBe(ssComplete)
      await bot.getByRole('button', { name: WILD, exact: true }).click()
      const options = bot.getByTestId('param-PhaseName').locator('select option')
      await expect.poll(async () => (await options.evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value))).filter((v) => v !== ''),
        { message: `"the card's param-PhaseName options equal the picker" on ${handle}`, timeout: 30_000 }).toEqual(names)
      await shotAll(`08-launch-${handle}`, pages)
      await cancelBot(bot)
    }
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
