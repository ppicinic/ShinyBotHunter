// hgss-plan-editor (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1b, plan scenario 8): FR-37, FR-37a, FR-37b and the
// C3a, C4a and C4b editor items, on the idle HeartGold bot (HG-Editor, position 0) and a SoulSilver bot at its title screen
// (SS-Title2, position 1). No hunt runs: the one Start is refused before anything is read from the emulator.
// With the HuntPlans collection dropped first (it must be empty; nothing else is deleted): the backend starts, lists no plans
// and does not create it. Then, through the Wild Encounter form's Hunt Plan field: create, replace, the preview for Sunday and
// Friday, everything greyed while an edit is unsaved, Discard, the refused saves, rename (and its refusals), delete; with a plan
// picked, the milestone row, Run enabled with no milestone, no anchor line or Rows panel, and a plan naming Sprout Tower (no
// anchor) marked and refused at Start; the plan on the SoulSilver bot; a draft dropped by a REST cancel of its bot.
// Run: `npx playwright test hgss-plan-editor`.
import { spawnSync } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step, stopRecorder } from './driver'
import {
  assertLocalStores, cancelBot, deletePlan, launchHgss, putPlan, renamePlan, saveOf, shutdown, stageCopy, startFrame,
  teardown, WILD, type Frame, type Plan, type PlanRule
} from './milestones'
import { API, MINUTE, settings } from './static'

const HG = 'HG-Editor'
const SS = 'SS-Title2'
const R32 = { id: '36-land', name: 'Route 32 · grass' }
const R30 = { id: '34-land', name: 'Route 30 · grass' }
const UNION = { id: '99-land', name: 'Union Cave 1F · cave' }
// One of the 19 land milestones with no generated anchor (FR-19).
const SPROUT = { id: '155-land', name: 'Sprout Tower · cave' }
const JOHTO = 'Johto'
const JOHTO_RULES: PlanRule[] = [
  { times: ['Night'], weekdays: [], milestone: R30.id },
  { times: [], weekdays: ['Friday'], milestone: UNION.id }
]
const OTHER: Plan = { name: 'QA Other', default: R32.id, rules: [] }
// The plans this scenario saves through the editor, which milestones.ts's teardown does not know of.
const UI_PLANS = [JOHTO, 'Johto 2']
const MONGOSH = path.join(process.env.LOCALAPPDATA!, 'Programs', 'mongosh', 'mongosh.exe')

test('hgss-plan-editor: create, replace, preview, greyed while unsaved, refusals, rename, delete, the plan on SoulSilver', async () => {
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  // milestones.ts keeps its mongosh call to itself; this scenario's reads, the drop and its own plans' removal go to the same
  // URI, checked local again here.
  const { mongoUri } = settings()
  expect(['localhost', '127.0.0.1'], 'the backend\'s Mongo is not local').toContain(new URL(mongoUri).hostname)
  const mongo = (script: string): any => {
    const res = spawnSync(MONGOSH, [mongoUri, '--quiet', '--eval', `print(EJSON.stringify(${script}))`], { encoding: 'utf8' })
    if (res.status !== 0) throw new Error(`mongosh exited with ${res.status}: ${res.stderr}`)
    const value = JSON.parse(res.stdout.trim().split(/\r?\n/).at(-1)!)
    step(`mongosh ${script} -> ${JSON.stringify(value)}`)
    return value
  }
  const hasCollection = (): boolean => mongo('db.getCollectionNames().includes("HuntPlans")')
  const plansOf = async (game: string): Promise<Plan[]> => (await (await fetch(`${API}/plans/${game}`)).json()) as Plan[]
  try {
    stageCopy(HG, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    stageCopy(SS, { rom: 'Pokemon SoulSilver.nds', save: null })
    // C4b: no HuntPlans collection. Only an empty one is dropped, so no plan is ever deleted by this.
    const stored = mongo('db.HuntPlans.countDocuments({})')
    expect(stored, `HuntPlans holds ${stored} plans; this scenario drops the collection: move them aside by hand first`).toBe(0)
    mongo('db.HuntPlans.drop()')
    expect(hasCollection(), 'HuntPlans still exists after the drop').toBe(false)

    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    expect(await plansOf('HGSS'), 'GET plans/HGSS with no collection').toEqual([])
    expect(hasCollection(), 'the backend created HuntPlans at start-up or on a read').toBe(false)

    const hg = await launchHgss(admin, HG)
    const ss = await launchHgss(admin, SS, { continue: false })
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    await hg.getByRole('button', { name: WILD, exact: true }).click()
    const editor = hg.getByTestId('param-HuntPlan')
    const planSelect = editor.locator('select').first()
    const button = (name: string, scope: Locator = editor): Locator => scope.getByRole('button', { name, exact: true })
    const nameBox = editor.getByPlaceholder('Plan name')
    const defaultSelect = editor.getByText('Otherwise hunt', { exact: true }).locator('xpath=..').locator('select')
    const rule = (n: number): Locator => editor.getByRole('button', { name: `Remove rule ${n}`, exact: true }).locator('xpath=..')
    const error = editor.locator('div.text-red-400')
    const preview = editor.getByText('Preview', { exact: true }).locator('xpath=../..')
    const fillRule = async (n: number, r: PlanRule): Promise<void> => {
      for (const time of r.times) await button(time, rule(n)).click()
      for (const day of r.weekdays) await rule(n).getByTitle(day, { exact: true }).click()
      await rule(n).locator('select').selectOption(r.milestone)
    }
    const fillJohto = async (extra: PlanRule[] = []): Promise<void> => {
      await nameBox.fill(JOHTO)
      await defaultSelect.selectOption(R32.id)
      const all = [...JOHTO_RULES, ...extra]
      for (let n = 1; n <= all.length; n++) {
        await button('Add rule').click()
        await fillRule(n, all[n - 1])
      }
    }

    // 1. Create: Johto, default Route 32, Night -> Route 30, Friday -> Union Cave 1F. A new plan opens on Edit plans.
    await button('Edit plans').click()
    await fillJohto()
    await button('Save').click()
    await expect(planSelect.locator(`option[value="${JOHTO}"]`), 'Johto in the Hunt Plan select').toBeAttached({ timeout: 30_000 })
    const saved = (await plansOf('HGSS')).filter((p) => p.name === JOHTO)
    expect(saved.map(({ name, default: d, rules }) => ({ name, default: d, rules })), 'GET plans/HGSS holds Johto once')
      .toEqual([{ name: JOHTO, default: R32.id, rules: JOHTO_RULES }])
    expect((await plansOf('RS')).filter((p) => p.name === JOHTO), 'GET plans/RS').toEqual([])
    await shotAll('01-created', pages)

    // 2. Replace: two more saves of the name (Morning added to rule 1, then taken off) leave one document.
    for (const _ of [1, 2]) {
      await button('Morning', rule(1)).click()
      await button('Save').click()
      await expect(button('Save'), 'the save did not land').toBeDisabled({ timeout: 30_000 })
      expect(mongo(`db.HuntPlans.countDocuments({ Game: 0, Name: "${JOHTO}" })`), 'documents named Johto').toBe(1)
    }
    expect((await plansOf('HGSS')).find((p) => p.name === JOHTO)?.rules, 'Johto after the two replaces').toEqual(JOHTO_RULES)

    // 3. The preview follows the rule order, the first match winning, read from the bars' titles and the legend.
    const daySelect = preview.locator('select')
    const bars = async (): Promise<(string | null)[]> => preview.locator('div.relative.flex > div').evaluateAll((els) => els.map((e) => e.getAttribute('title')))
    const legend = async (): Promise<string[]> => (await preview.locator('div.flex-wrap > span').allInnerTexts()).map((t) => t.trim())
    await daySelect.selectOption('Sunday')
    expect(await bars(), 'the Sunday preview').toEqual([
      `00:00-04:00 ${R30.name} (rule 1)`, `04:00-20:00 ${R32.name} (otherwise)`, `20:00-24:00 ${R30.name} (rule 1)`
    ])
    expect(await legend(), 'the Sunday legend').toEqual([R30.name, R32.name])
    await shotAll('02-preview-sunday', pages)
    await daySelect.selectOption('Friday')
    expect(await bars(), 'the Friday preview').toEqual([
      `00:00-04:00 ${R30.name} (rule 1)`, `04:00-20:00 ${UNION.name} (rule 2)`, `20:00-24:00 ${R30.name} (rule 1)`
    ])
    expect(await legend(), 'the Friday legend').toEqual([R30.name, UNION.name])
    await shotAll('03-preview-friday', pages)

    // 4. An unsaved edit greys whatever would drop it; Discard restores the saved rules.
    await button('Day', rule(1)).click()
    const tabs = hg.getByRole('button', { name: WILD, exact: true }).locator('xpath=..').getByRole('button')
    for (const greyed of [button(JOHTO), button('+ New plan'), button('Close plans'), button('Run', hg), ...await tabs.all()]) {
      await expect(greyed, `greyed while unsaved: ${await greyed.innerText()}`).toBeDisabled()
    }
    await shotAll('04-unsaved', pages)
    await button('Discard').click()
    await expect(button('Day', rule(1)), 'Discard: Day off again').toHaveAttribute('aria-pressed', 'false')
    await expect(button('Night', rule(1)), 'Discard: Night still on').toHaveAttribute('aria-pressed', 'true')
    await expect(button('+ New plan'), 'nothing unsaved after Discard').toBeEnabled()

    // 5. Refused saves show the server's sentence: no default, then a rule with no milestone.
    await button('+ New plan').click()
    await nameBox.fill('QA Bad')
    await button('Save').click()
    await expect(error, 'a plan with no default').toContainText('The plan has no default milestone')
    await defaultSelect.selectOption(R32.id)
    await button('Add rule').click()
    await button('Save').click()
    await expect(error, 'a rule with no milestone').toContainText('Rule 1 names no milestone')
    await shotAll('05-refused-save', pages)
    await button('Discard').click()
    expect((await plansOf('HGSS')).map((p) => p.name), 'a refused plan was saved').not.toContain('QA Bad')

    // 6. Rename to Johto 2; onto an existing name refused; a blank name refused (the button is greyed, so through REST).
    await button(JOHTO).click()
    await nameBox.fill('Johto 2')
    await button('Rename').click()
    await expect(button('Johto 2'), 'the renamed plan\'s button').toBeVisible({ timeout: 30_000 })
    expect((await plansOf('HGSS')).map((p) => p.name).filter((n) => n.startsWith(JOHTO)), 'after the rename').toEqual(['Johto 2'])
    expect((await putPlan('HGSS', OTHER)).status, 'PUT QA Other').toBe(204)
    await nameBox.fill(OTHER.name)
    await button('Rename').click()
    await expect(error, 'a rename onto an existing name').toContainText(`A plan named ${OTHER.name} already exists.`)
    await nameBox.fill('')
    await expect(button('Rename'), 'Rename with a blank name').toBeDisabled()
    const blank = await renamePlan('HGSS', 'Johto 2', ' ')
    expect([blank.status, blank.text], 'a blank rename through REST').toEqual([400, 'The plan needs a name.'])
    await nameBox.fill('Johto 2')
    await shotAll('06-rename', pages)

    // 7. Delete.
    await button('Delete').click()
    await button('Yes').click()
    await expect(button('Johto 2'), 'the deleted plan\'s button').toHaveCount(0, { timeout: 30_000 })
    expect((await plansOf('HGSS')).map((p) => p.name), 'after the delete').not.toContain('Johto 2')

    // 8. Johto again, with a third rule to Sprout Tower, which has no anchor: marked in the editor, refused at Start.
    await button('+ New plan').click()
    await fillJohto([{ times: [], weekdays: [], milestone: SPROUT.id }])
    await expect(rule(3), 'Sprout Tower marked in the editor').toContainText('no anchor on this file: Start refuses the plan')
    await button('Save').click()
    await expect(planSelect.locator(`option[value="${JOHTO}"]`), 'Johto in the Hunt Plan select').toBeAttached({ timeout: 30_000 })
    await planSelect.selectOption(JOHTO)
    await expect(hg.getByTestId('param-PhaseName'), 'the milestone row with a plan')
      .toContainText(`Set by the plan "${JOHTO}": it starts on its default and follows the in-game clock.`)
    await expect(hg.getByTestId('milestone-anchor'), 'an anchor line with a plan').toHaveCount(0)
    await expect(hg.getByTestId('milestone-rows'), 'a Rows panel with a plan').toHaveCount(0)
    // Target and mode are required (EventRegistry); the plan stands in for the milestone only.
    await hg.getByTestId('param-TargetType').locator('select').selectOption('PhaseTarget')
    await hg.getByTestId('param-EncounterType').locator('select').selectOption('Run')
    await hg.getByTestId('param-yRange').locator('input').fill('1')
    await expect(button('Run', hg), 'Run with a plan and no milestone').toBeEnabled()
    await shotAll('07-plan-picked', pages)
    // The form is filled, so Run is clicked here rather than through startWild, which would fill it again.
    await button('Run', hg).click()
    await expect(hg.locator('div[class~="bg-red-900/20"]'), 'Start with Sprout Tower in the plan')
      .toContainText(`${JOHTO}: ${SPROUT.name} has no anchor.`, { timeout: 30_000 })
    await shotAll('08-refused-start', pages)

    // 9. The same plan on the SoulSilver bot at its title screen.
    await ss.getByRole('button', { name: WILD, exact: true }).click()
    await expect(ss.getByTestId('param-HuntPlan').locator('select').first().locator(`option[value="${JOHTO}"]`), 'Johto on the SoulSilver bot')
      .toBeAttached({ timeout: 30_000 })
    await shotAll('09-soulsilver', pages)

    // 10. A draft open while its bot is cancelled through REST is dropped with no error (no uncaught error in the page).
    const errors: string[] = []
    admin.on('pageerror', (e) => errors.push(e.message))
    if (await button('Edit plans').isVisible()) await button('Edit plans').click()
    await button('+ New plan').click()
    await nameBox.fill('QA Draft')
    await expect(button('Discard'), 'the draft is unsaved').toBeVisible()
    const cancel = await fetch(`${API}/bot/cancelAction`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '0' })
    expect(cancel.status, 'POST bot/cancelAction 0').toBe(200)
    await expect(hg, 'the HeartGold card after the REST cancel').toHaveCount(0, { timeout: MINUTE })
    await stopRecorder()
    await admin.waitForTimeout(3000)
    expect(errors, 'a page error after the REST cancel').toEqual([])
    await expect(admin.getByTestId('emulator-card-1'), 'the SoulSilver card after the HeartGold cancel').toBeVisible()
    await shotAll('10-draft-dropped', pages)
    await cancelBot(ss)
    expect(fs.readFileSync(saveOf(HG)).equals(fs.readFileSync(saveOf(HG) + '.bak')), 'the HG-Editor SaveRAM differs from its .bak').toBe(true)

    expect((await deletePlan('HGSS', JOHTO)).status, 'DELETE Johto').toBe(204)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame, () => {
      if (hasCollection()) mongo(`db.HuntPlans.deleteMany({ Game: 0, Name: { $in: ${JSON.stringify(UI_PLANS)} } }).deletedCount`)
    })
  }
})
