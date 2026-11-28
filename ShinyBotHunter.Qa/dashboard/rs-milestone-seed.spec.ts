// rs-milestone-seed (docs/specs/milestone-builder-rework.md, model 1 to 3, 7 and default 12; #621 R8a): no emulator, the
// backend and the dashboard only. The Milestone Editor on Ruby and Sapphire: no Female forms preset (in the select and from
// the seed route), Route 102's rows for both versions and each, their version marks, the Version exclusives amounts, no When
// marker on any row, and the Save read back from Mongo. Run line: `npx playwright test rs-milestone-seed`.
import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import { assertLocalStores, claimMilestone, milestoneDoc, seed, shutdown, startFrame, teardown, type Frame } from './milestones'
import { MINUTE } from './static'

// Route 102 · grass, in Data/RS/milestones.json.gz: Seedot only on Ruby, Lotad only on Sapphire.
const ROUTE102 = '17-land'
const NAME = 'QA RS Route 102'
const SHARED = ['Zigzagoon', 'Wurmple', 'Poochyena', 'Ralts', 'Surskit']
const VERSION: Record<string, string> = { Seedot: 'Ruby', Lotad: 'Sapphire' }

type EditorRow = { name: string; markers: string[]; version: string; amount: number }

/** The editor's Pokémon table, read from the DOM: the name input, the When pills, the Version cell, the amount. */
function editorRows(admin: Page): Promise<EditorRow[]> {
  return admin.locator('table:has(th:text-is("Version")) tbody tr').evaluateAll((trs) => trs.map((tr) => {
    const td = tr.querySelectorAll('td')
    return {
      name: (td[0].querySelector('input[type="text"]') as HTMLInputElement).value,
      markers: Array.from(td[1].querySelectorAll('span')).map((s) => (s.textContent ?? '').trim()),
      version: (td[2].textContent ?? '').trim(), amount: Number((td[3].querySelector('input') as HTMLInputElement).value)
    }
  }))
}

test('rs-milestone-seed: no Female forms on RS, Route 102 for both versions and each, Version exclusives, no markers, Save', async () => {
  test.setTimeout(10 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const newFromData = admin.getByRole('button', { name: 'New from game data', exact: true })
    const presetSelect = admin.locator('select:has(option[value="VersionExclusives"])')
    const rowsShown = async (n: number, what: string): Promise<EditorRow[]> => {
      await expect.poll(async () => (await editorRows(admin)).length, { message: what, timeout: 30_000 }).toBe(n)
      const rows = await editorRows(admin)
      expect(rows.filter((r) => r.markers.length > 0), `"no RS row has a When marker" (default 12): ${what}`).toEqual([])
      return rows
    }

    // The seed route refuses the female forms preset on RS.
    const refused = await seed('RS', ROUTE102, 'FemaleForms')
    expect(refused.status, 'seed RS FemaleForms: the status').toBe(400)
    expect(refused.text, 'seed RS FemaleForms: the sentence').toMatch(/^(Ruby|Sapphire) has no female forms preset$/)

    await admin.getByRole('button', { name: 'Milestone Editor', exact: true }).click()
    await admin.getByRole('heading', { name: 'Select Milestone', exact: true }).locator('xpath=following-sibling::div[1]').locator('select').first().selectOption('RS')
    await expect(newFromData, 'the RS locations did not load').toBeEnabled({ timeout: 30_000 })
    await newFromData.click()
    await admin.locator(`select:has(option[value="${ROUTE102}"])`).selectOption(ROUTE102)
    expect(await presetSelect.locator('option').evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value)), '"None, Living dex and Version exclusives: no Female forms"')
      .toEqual(['None', 'LivingDex', 'VersionExclusives'])

    // Route 102: seven rows for both versions (five shared, Seedot and Lotad marked), six for each version.
    const both = await rowsShown(7, '"Both versions gives 7 rows"')
    step(`Route 102, both versions: ${JSON.stringify(both)}`)
    expect(both.map((r) => r.name).sort(), 'Route 102\'s species').toEqual([...SHARED, 'Seedot', 'Lotad'].sort())
    for (const r of both) expect(r.version, `the version mark of ${r.name}`).toBe(VERSION[r.name] ?? 'both')
    await shotAll('01-route102-both', pages)
    for (const [version, other] of [['Ruby', 'Lotad'], ['Sapphire', 'Seedot']]) {
      await admin.getByRole('button', { name: `${version} only`, exact: true }).click()
      const only = await rowsShown(6, `"${version} only gives 6"`)
      expect(only.map((r) => r.name), `${version} only: no ${other}`).not.toContain(other)
    }
    await admin.getByRole('button', { name: 'Both versions', exact: true }).click()
    await rowsShown(7, 'back to both versions')

    // Version exclusives: Seedot and Lotad at 1, the rest at 0.
    await presetSelect.selectOption('VersionExclusives')
    const amounts = (rows: { name: string; amount: number }[]): string[] => rows.map((r) => `${r.name} ${r.amount}`).sort()
    const exclusives = [...SHARED, 'Seedot', 'Lotad'].map((name) => ({ name, amount: name in VERSION ? 1 : 0 }))
    await expect.poll(async () => amounts(await editorRows(admin)), { message: '"Version exclusives puts Seedot and Lotad at 1 and the rest at 0"', timeout: 30_000 })
      .toEqual(amounts(exclusives))
    await rowsShown(7, 'Version exclusives')
    await shotAll('02-version-exclusives', pages)

    // Saved, and read back from Mongo.
    await admin.getByPlaceholder('New milestone name').fill(NAME)
    claimMilestone('RS', NAME)
    await admin.getByRole('button', { name: 'Save Milestone', exact: true }).click()
    await expect(admin.getByText(`Saved "${NAME}" successfully`, { exact: true }), `"Saved "${NAME}" successfully"`).toBeVisible({ timeout: 30_000 })
    const doc = milestoneDoc('RS', NAME)
    expect(doc?.Location, `${NAME}: Location`).toBe(ROUTE102)
    expect(amounts(doc.Pokemon.map((p: { Name: string; Amount: number }) => ({ name: p.Name, amount: p.Amount }))), `${NAME}: the stored rows`).toEqual(amounts(exclusives))
    await shotAll('03-saved', pages)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
