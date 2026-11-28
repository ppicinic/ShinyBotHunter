// hgss-plan-refusals (docs/specs/milestone-builder-rework.md, "Plans, as the owner redefined them"; #621 R8c1): the Bot
// Control "Plan" action and its refusals on HG-PlanRefuse (hgss.qa-route33: no party member has the Pickup ability). Nothing
// hunts: each Start is refused before an event starts, with the refused step's label in front of its own Start's sentence
// (a Pickup step with no Pickup member, a location with no hunt, the wrong encounter type, an archived and an unticked
// milestone), and an unknown plan; two PUTs are refused at the save (a step that is not a plan step yet, a range a Spin does
// not take); and the card's Run shows the same sentence in its red box. Run line: `npx playwright test hgss-plan-refusals`.
import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import { backendLines, backendLog } from './headbutt'
import {
  assertLocalStores, cancelBot, launchHgss, putMilestone, putPlan, saveOf, shutdown, stageCopy, startAction, startFrame, startPlan,
  teardown, WILD, type Frame
} from './milestones'
import { API, MINUTE } from './static'

const HANDLE = 'HG-PlanRefuse'
const ELSEWHERE = 'HG-PlanElsewhere'
const R33 = 'QA R33'
const SURF = 'QA Surf'
const ARCHIVED = 'QA R33 Archived'
const UNTICKED = 'QA R33 Other'
const NOPE = 'QA Nope'
const WAIT = { eventName: 'Wait' }
const step33 = (extra: Record<string, unknown>): Record<string, unknown> =>
  ({ eventName: WILD, phaseName: R33, targetType: 'PhaseTarget', encounterType: 'Run', yRange: 1, ...extra })
const ruleOf = (args: Record<string, unknown>): { times: ['Night']; weekdays: []; arguments: Record<string, unknown> } =>
  ({ times: ['Night'], weekdays: [], arguments: args })

// Each saved plan and the sentence its Start is refused with; the first is also run through the card.
const STARTS: { name: string; rules: ReturnType<typeof ruleOf>[]; default: Record<string, unknown>; sentence: string }[] = [
  {
    name: 'QA Refuse Pickup', rules: [], default: step33({ trainType: 'Standard', targetLevel: 101, pickup: true }),
    sentence: 'The default: No Pokémon in your party has the Pickup ability.'
  },
  {
    name: 'QA Refuse Surf', rules: [ruleOf(step33({ phaseName: SURF }))], default: WAIT,
    sentence: 'Rule 1: Route 32 · surf: no hunt yet.'
  },
  {
    name: 'QA Refuse Headbutt', rules: [ruleOf({ eventName: WILD, phaseName: R33, targetType: 'PhaseTarget', encounterType: 'Headbutt' })], default: WAIT,
    sentence: 'Rule 1: Route 33 · grass is not hunted with Headbutt.'
  },
  { name: 'QA Refuse Archived', rules: [ruleOf(step33({ phaseName: ARCHIVED }))], default: WAIT, sentence: `Rule 1: ${ARCHIVED} is archived.` },
  { name: 'QA Refuse Unticked', rules: [ruleOf(step33({ phaseName: UNTICKED }))], default: WAIT, sentence: `Rule 1: ${UNTICKED} is not ticked for ${HANDLE}.` }
]

// Refused at the save: nothing stored.
const PUTS: { name: string; rule: Record<string, unknown>; sentence: string }[] = [
  { name: 'QA Refuse Static', rule: { eventName: 'HGSS Static Encounter' }, sentence: 'Rule 1: HGSS Static Encounter cannot be a plan step yet; a step is Wild Encounter or Wait.' },
  { name: 'QA Refuse Range', rule: step33({ encounterType: 'Spin', yRange: undefined, xRange: 1 }), sentence: 'Rule 1: xRange applies only when EncounterType is Run.' }
]

const ORCHESTRATOR_STATE = /\[Orchestrator\] State:/
const eventState = async (): Promise<string> => {
  const state = (await (await fetch(`${API}/bot/getDashboardState`)).json())['0']?.eventState
  step(`position 0 eventState ${state}`)
  return state
}
const planStatus = async (name: string): Promise<number> => (await fetch(`${API}/plans/HGSS/${encodeURIComponent(name)}`)).status

test('hgss-plan-refusals: the Plan action lists the saved plans; each step refusal, an unknown plan and two refused saves', async () => {
  test.setTimeout(15 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  stageCopy(HANDLE, { rom: 'Pokemon HeartGold.nds', save: 'hgss.qa-route33' })
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const fixtures = [
      { name: R33, location: '37-land', gameFiles: [HANDLE], archived: false },
      { name: SURF, location: '36-surf', gameFiles: [HANDLE], archived: false },
      { name: ARCHIVED, location: '37-land', gameFiles: [HANDLE], archived: true },
      { name: UNTICKED, location: '37-land', gameFiles: [ELSEWHERE], archived: false }
    ]
    for (const m of fixtures) expect((await putMilestone({ game: 'HGSS', ...m })).status, `save ${m.name}`).toBe(200)

    // The two refused saves: a 400 with the sentence, and nothing stored.
    for (const p of PUTS) {
      const res = await putPlan('HGSS', { name: p.name, rules: [ruleOf(p.rule)], default: WAIT, endWhenComplete: false })
      expect([res.status, res.text], `PUT ${p.name}`).toEqual([400, p.sentence])
      expect(await planStatus(p.name), `${p.name} was stored`).toBe(404)
    }
    // The plans are saved before the launch, so the card's first read of its actions lists them.
    for (const p of STARTS) {
      expect((await putPlan('HGSS', { name: p.name, rules: p.rules, default: p.default, endWhenComplete: false })).status, `PUT ${p.name}`).toBe(204)
    }
    expect(await planStatus(NOPE), `a plan named ${NOPE} exists`).toBe(404)

    const card = await launchHgss(admin, HANDLE)
    // Bot Control lists Plan with the saved names.
    await card.getByRole('button', { name: 'Plan', exact: true }).click()
    const saved = ((await (await fetch(`${API}/plans/HGSS`)).json()) as { name: string }[]).map((p) => p.name).sort()
    expect(saved, 'GET plans/HGSS').toEqual(expect.arrayContaining(STARTS.map((p) => p.name)))
    const listed = async (): Promise<string[]> => (await card.getByTestId('param-HuntPlan').locator('select option')
      .evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value))).filter((v) => v !== '').sort()
    await expect.poll(listed, { message: '"Bot Control lists Plan with the saved names"', timeout: 30_000 }).toEqual(saved)
    await shotAll('01-plan-action', pages)

    // Each Start: a 400 with its sentence, the state still None, no [Orchestrator] State: line.
    for (const [name, sentence] of [...STARTS.map((p) => [p.name, p.sentence]), [NOPE, `No hunt plan named ${NOPE}.`]]) {
      // The last element is the empty string after the final newline; the first line after the POST replaces it.
      const from = backendLog().length - 1
      const res = await startAction(0, { EventName: 'Plan', HuntPlan: name })
      expect([res.status, res.text], `the Start of ${name}`).toEqual([400, sentence])
      expect(await eventState(), `an event started after the refusal of ${name}`).toBe('None')
      expect(backendLines(ORCHESTRATOR_STATE, from), `an [Orchestrator] State: line after the refusal of ${name}`).toEqual([])
    }

    // The card's Run: the same sentence in its red box.
    const from = backendLog().length - 1
    await expect(startPlan(card, STARTS[0].name), 'the card\'s Run of the Pickup plan').rejects.toThrow(`the Run was refused: "${STARTS[0].sentence}"`)
    expect(await eventState(), 'an event started after the card\'s refused Run').toBe('None')
    expect(backendLines(ORCHESTRATOR_STATE, from), 'an [Orchestrator] State: line after the card\'s refused Run').toEqual([])
    await expect(card.getByRole('button', { name: WILD, exact: true }), 'the idle bot\'s actions').toBeVisible()
    await shotAll('02-refused-run', pages)

    await cancelBot(card)
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), `the ${HANDLE} SaveRAM differs from its .bak`).toBe(true)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
