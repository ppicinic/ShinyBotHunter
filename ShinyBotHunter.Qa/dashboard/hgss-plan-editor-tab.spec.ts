// hgss-plan-editor-tab (docs/specs/milestone-builder-rework.md, "Plans, as the owner redefined them"; #621 R8c1): the
// Admin > Plan Editor tab with no emulator. A new HGSS plan with a Night rule and a default, each a whole Wild Encounter
// hunt; its stored body read back; the form as saved on reselect; a Wait rule; Up and Down; the taken name; rename and
// delete; the server's refusal sentences in the tab; and what a step's milestone and action selects offer.
// Run line: `npx playwright test hgss-plan-editor-tab`.
import { expect, test, type Locator, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import { assertLocalStores, putMilestone, putPlan, shutdown, startFrame, teardown, type Frame, type Game } from './milestones'
import { API, MINUTE } from './static'

const PLAN = 'QA Nights'
const RENAMED = 'QA Nights 2'
const BAD = 'QA Bad Step'
const RS_PLAN = 'QA RS Plan'
const R30 = 'QA Route 30'
const R32 = 'QA Route 32'
const ARCHIVED = 'QA Plan Archived'
// A file no bot runs: QA Route 32 is ticked for it only, and the editor offers it anyway.
const OTHER_FILE = 'HG-PlanTabOther'

type Stored = { status: number; json: any }
async function stored(game: Game, name: string): Promise<Stored> {
  const res = await fetch(`${API}/plans/${game}/${encodeURIComponent(name)}`)
  const text = await res.text()
  step(`GET plans/${game}/${name} -> ${res.status} ${text.slice(0, 600)}`)
  return { status: res.status, json: res.status === 200 ? JSON.parse(text) : null }
}

// The stored step's fields other than the action, set or not (the API writes an unset field as null).
const setFields = (args: Record<string, unknown>): Record<string, unknown> =>
  Object.fromEntries(Object.entries(args).filter(([k, v]) => k !== 'eventName' && v !== null))

const values = (select: Locator): Promise<string[]> =>
  select.locator('option').evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value).filter((v) => v !== ''))

test('hgss-plan-editor-tab: a new plan of whole hunts, stored, reselected, Wait, order, rename, delete, refusals, the selects', async () => {
  test.setTimeout(15 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    expect((await putMilestone({ game: 'HGSS', name: R30, location: '34-land', gameFiles: [], archived: false })).status, `save ${R30}`).toBe(200)
    expect((await putMilestone({ game: 'HGSS', name: R32, location: '36-land', gameFiles: [OTHER_FILE], archived: false })).status, `save ${R32}`).toBe(200)
    expect((await putMilestone({ game: 'HGSS', name: ARCHIVED, location: '34-land', gameFiles: [], archived: true })).status, `save ${ARCHIVED}`).toBe(200)
    // The tab saves these names itself. putPlan claims a name for the teardown before its PUT, and fails on a name this run did
    // not make; this body (a default with no milestone) is refused, so nothing is stored and the tab's new plan is new.
    for (const name of [PLAN, RENAMED, BAD]) {
      const res = await putPlan('HGSS', { name, rules: [], default: { eventName: 'Wild Encounter' }, endWhenComplete: false })
      expect([res.status, res.text], `the claim of ${name}`).toEqual([400, 'The default: Wild Encounter needs PhaseName.'])
    }
    // RS has no plans at all: the REST refusal, which claims the name the tab tries below.
    const rs = await putPlan('RS', { name: RS_PLAN, rules: [], default: { eventName: 'Wait' }, endWhenComplete: false })
    expect([rs.status, rs.text], 'PUT an RS plan').toEqual([400, 'RS has no in-game clock, so it has no plans.'])

    // The tab mounts on its click and reads the metadata then, so after the fixtures.
    const metadata = (await (await fetch(`${API}/metadata`)).json()) as { games: Record<string, { phases: { name: string; archived: boolean }[] }> }
    const offered = metadata.games.HGSS.phases.filter((p) => !p.archived).map((p) => p.name).sort()
    await admin.getByRole('button', { name: 'Plan Editor', exact: true }).click()
    const tab = (id: string): Locator => admin.getByTestId(id)
    // getByTestId matches the whole id, so plan-rule-1 is the first rule's container and never plan-rule-up.
    const rule = (n: number): Locator => tab(`plan-rule-${n}`)
    const deflt = tab('plan-default')
    const field = (scope: Locator, param: string): Locator => scope.getByTestId(`param-${param}`)
    const milestone = (scope: Locator): Locator => field(scope, 'PhaseName').locator('select')
    const select = tab('plan-select')
    const save = tab('plan-save')
    await tab('plan-game').selectOption('HGSS')
    await expect(select, 'a new plan is open').toHaveValue('', { timeout: 30_000 })

    // A step's selects: every non-archived HGSS milestone whatever its ticks, no Incomplete only, Wild Encounter and Wait.
    expect((await values(milestone(deflt))).sort(), '"every non-archived HGSS milestone whatever its ticks"').toEqual(offered)
    expect(offered, `${R32}, ticked only for ${OTHER_FILE}, is offered`).toContain(R32)
    expect(offered, `"not an archived one": ${ARCHIVED}`).not.toContain(ARCHIVED)
    await expect(admin.getByText('Incomplete only'), '"shows no Incomplete only"').toHaveCount(0)
    expect(await values(deflt.getByTestId('plan-step-action')), '"the action select offers Wild Encounter and Wait only"').toEqual(['Wild Encounter', 'Wait'])
    await shotAll('01-new-plan', pages)

    // A new plan: rule 1 Night, Wild Encounter QA Route 30, PhaseTarget, Run, y 1; the default QA Route 32, Spin, Standard 30, Pickup.
    await tab('plan-name').fill(PLAN)
    await tab('plan-add-rule').click()
    await rule(1).getByTestId('plan-time-Night').click()
    await expect(rule(1).getByTestId('plan-time-Night')).toHaveAttribute('aria-pressed', 'true')
    await milestone(rule(1)).selectOption(R30)
    await field(rule(1), 'TargetType').locator('select').selectOption('PhaseTarget')
    await field(rule(1), 'EncounterType').locator('select').selectOption('Run')
    await field(rule(1), 'yRange').locator('input').fill('1')
    await milestone(deflt).selectOption(R32)
    await field(deflt, 'TargetType').locator('select').selectOption('PhaseTarget')
    await field(deflt, 'EncounterType').locator('select').selectOption('Spin')
    await field(deflt, 'TrainType').locator('select').selectOption('Standard')
    await field(deflt, 'TargetLevel').locator('input').fill('30')
    const pickup = field(deflt, 'Pickup').locator('input[type="checkbox"]')
    await pickup.click()
    await expect(pickup).toBeChecked()
    await shotAll('02-filled', pages)
    await save.click()
    // The select shows the saved name once the list read after the Save holds it.
    await expect(select, `the plan select after the Save of ${PLAN}`).toHaveValue(PLAN, { timeout: 30_000 })

    const first = await stored('HGSS', PLAN)
    expect(first.status, `GET plans/HGSS/${PLAN}`).toBe(200)
    expect([first.json.rules.length, first.json.endWhenComplete], 'one rule; Plan ends Never').toEqual([1, false])
    const [r1] = first.json.rules
    expect([r1.times, r1.weekdays], '"the rule is { times: [Night], weekdays: [] }"').toEqual([['Night'], []])
    expect([r1.arguments.eventName, setFields(r1.arguments)], 'the rule\'s step').toEqual(['Wild Encounter',
      { phaseName: R30, targetType: 'PhaseTarget', encounterType: 'Run', xRange: 0, yRange: 1, trainType: 'None' }])
    expect([first.json.default.eventName, setFields(first.json.default)], '"the default has no xRange or yRange"').toEqual(['Wild Encounter',
      { phaseName: R32, targetType: 'PhaseTarget', encounterType: 'Spin', trainType: 'Standard', targetLevel: 30, pickup: true }])

    // The taken name: a new plan named as a saved one cannot be saved.
    await select.selectOption('')
    await tab('plan-name').fill(PLAN)
    await expect(save, 'Save for a new plan under a saved name').toBeDisabled()
    await expect(admin.getByText('already a plan', { exact: true }), 'the "already a plan" note').toBeVisible()

    // Reselect: the fields are as saved.
    await select.selectOption(PLAN)
    await expect(tab('plan-name')).toHaveValue(PLAN)
    for (const [time, on] of [['Morning', 'false'], ['Day', 'false'], ['Night', 'true']]) {
      await expect(rule(1).getByTestId(`plan-time-${time}`), `rule 1 ${time} as saved`).toHaveAttribute('aria-pressed', on)
    }
    await expect(milestone(rule(1)), 'rule 1\'s milestone as saved').toHaveValue(R30)
    await expect(field(rule(1), 'EncounterType').locator('select')).toHaveValue('Run')
    await expect(field(rule(1), 'yRange').locator('input')).toHaveValue('1')
    await expect(milestone(deflt), 'the default\'s milestone as saved').toHaveValue(R32)
    await expect(field(deflt, 'EncounterType').locator('select')).toHaveValue('Spin')
    await expect(field(deflt, 'TrainType').locator('select')).toHaveValue('Standard')
    await expect(field(deflt, 'TargetLevel').locator('input')).toHaveValue('30')
    await expect(pickup, 'Pickup as saved').toBeChecked()
    await expect(tab('plan-ends')).toHaveValue('never')
    await shotAll('03-reselected', pages)

    // A second rule, Friday Morning with Wait, stored as { eventName: 'Wait' } and nothing else.
    await tab('plan-add-rule').click()
    await rule(2).getByTestId('plan-step-action').selectOption('Wait')
    await expect(rule(2).getByText('(this step has no options)', { exact: true })).toBeVisible()
    await rule(2).getByTestId('plan-time-Morning').click()
    await expect(rule(2).getByTestId('plan-time-Morning')).toHaveAttribute('aria-pressed', 'true')
    await rule(2).getByTestId('plan-day-Friday').click()
    await expect(rule(2).getByTestId('plan-day-Friday')).toHaveAttribute('aria-pressed', 'true')
    const order = async (): Promise<string[]> => ((await stored('HGSS', PLAN)).json?.rules ?? []).map((r: any) => r.arguments.eventName)
    await save.click()
    await expect.poll(order, { message: 'the Save of the Wait rule', timeout: 30_000 }).toEqual(['Wild Encounter', 'Wait'])
    const wait = (await stored('HGSS', PLAN)).json.rules[1]
    expect([wait.times, wait.weekdays, wait.arguments.eventName, setFields(wait.arguments)], '"saved as { eventName: Wait }"')
      .toEqual([['Morning'], ['Friday'], 'Wait', {}])

    // Up and Down change the stored order; Up is disabled on the first rule (and Down on the last).
    await expect(rule(1).getByTestId('plan-rule-up'), '"Up is disabled on the first rule"').toBeDisabled()
    await expect(rule(2).getByTestId('plan-rule-down')).toBeDisabled()
    await rule(2).getByTestId('plan-rule-up').click()
    await expect(rule(1).getByTestId('plan-step-action'), 'rule 1 after Up').toHaveValue('Wait')
    await save.click()
    await expect.poll(order, { message: 'the stored order after Up', timeout: 30_000 }).toEqual(['Wait', 'Wild Encounter'])
    await rule(1).getByTestId('plan-rule-down').click()
    await expect(rule(1).getByTestId('plan-step-action'), 'rule 1 after Down').toHaveValue('Wild Encounter')
    await save.click()
    await expect.poll(order, { message: 'the stored order after Down', timeout: 30_000 }).toEqual(['Wild Encounter', 'Wait'])
    await shotAll('04-order', pages)

    // Rename, then Delete with Yes.
    await tab('plan-name').fill(RENAMED)
    await tab('plan-rename').click()
    await expect(select, `the plan select after the rename to ${RENAMED}`).toHaveValue(RENAMED, { timeout: 30_000 })
    expect((await stored('HGSS', PLAN)).status, '"the old name is 404"').toBe(404)
    expect((await stored('HGSS', RENAMED)).status, '"the new 200"').toBe(200)
    await tab('plan-delete').click()
    await tab('plan-delete-yes').click()
    await expect(select, 'a new plan after the delete').toHaveValue('', { timeout: 30_000 })
    await expect.poll(async () => (await stored('HGSS', RENAMED)).status, { message: '"Delete with Yes: gone"', timeout: 30_000 }).toBe(404)
    await shotAll('05-deleted', pages)

    // The red refusals: a rule with no encounter type, and any plan on RS.
    await tab('plan-name').fill(BAD)
    await tab('plan-add-rule').click()
    await milestone(rule(1)).selectOption(R30)
    await field(rule(1), 'TargetType').locator('select').selectOption('PhaseTarget')
    await save.click()
    await expect(tab('plan-error'), 'the refusal of a rule with no EncounterType').toHaveText('Rule 1: Wild Encounter needs EncounterType.', { timeout: 30_000 })
    expect((await stored('HGSS', BAD)).status, `${BAD} was stored`).toBe(404)
    await shotAll('06-refused-step', pages)
    await tab('plan-game').selectOption('RS')
    await tab('plan-name').fill(RS_PLAN)
    await save.click()
    await expect(tab('plan-error'), 'the refusal on RS').toHaveText('RS has no in-game clock, so it has no plans.', { timeout: 30_000 })
    expect((await stored('RS', RS_PLAN)).status, `${RS_PLAN} was stored`).toBe(404)
    await shotAll('07-refused-rs', pages)

    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
