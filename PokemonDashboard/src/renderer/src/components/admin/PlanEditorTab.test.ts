import { describe, expect, it } from 'vitest'
import type { EventArgumentOptions, PhaseInfo, Plan } from '../../types/api'
import { initialParamsFor } from './actionSchema'
import { editorMilestones, formOf, moveRule, planBody, toggled, withAction } from './PlanEditorTab'
import type { PlanForm, StepForm } from './PlanEditorTab'

// GET api/plans/steps as R6a serves it.
const wild: EventArgumentOptions = {
  name: 'Wild Encounter',
  requiredParameters: ['PhaseName', 'TargetType', 'EncounterType'],
  optionalParameters: ['TrainType'],
  conditionalParameters: [
    { condition: { conditionType: 'Eq', name: 'EncounterType', value: 'Run' }, name: 'xRange', hideIfConditionNotMet: true, required: false },
    { condition: { conditionType: 'Eq', name: 'EncounterType', value: 'Run' }, name: 'yRange', hideIfConditionNotMet: true, required: false },
    { condition: { conditionType: 'Neq', name: 'TrainType', value: 'None' }, name: 'TargetLevel', hideIfConditionNotMet: true, required: false },
    { condition: { conditionType: 'Eq', name: 'TrainType', value: 'Switch' }, name: 'SwitchSlot', hideIfConditionNotMet: true, required: false },
    { condition: { conditionType: 'Neq', name: 'TrainType', value: 'None' }, name: 'Pickup', hideIfConditionNotMet: true, required: false }
  ],
  disabledChoices: null,
  choices: null
}
const wait: EventArgumentOptions = { name: 'Wait', conditionalParameters: [] }
const steps = [wild, wait]

const nulls = { phaseName: null, targetType: null, encounterType: null, xRange: null, yRange: null, trainType: null, targetLevel: null, switchSlot: null, pickup: null, huntPlan: null }

// R6a's example body.
const saved: Plan = {
  game: 'HGSS',
  name: 'Johto nights',
  rules: [
    { times: ['Night'], weekdays: [], arguments: { ...nulls, eventName: 'Wild Encounter', phaseName: '1-4 Route 30', targetType: 'PhaseTarget', encounterType: 'Run', xRange: 2, yRange: 2, trainType: 'None' } },
    { times: ['Morning'], weekdays: ['Friday'], arguments: { ...nulls, eventName: 'Wait' } }
  ],
  default: { ...nulls, eventName: 'Wild Encounter', phaseName: '2-1 Route 32', targetType: 'ShinyTarget', encounterType: 'Spin', trainType: 'Standard', targetLevel: 101, pickup: true },
  endWhenComplete: false
}

const withoutNulls = (body: Record<string, unknown>): Record<string, unknown> => Object.fromEntries(Object.entries(body).filter(([, v]) => v !== null))

const step = (action: string, params: Record<string, string>, times: StepForm['times'] = [], weekdays: StepForm['weekdays'] = []): StepForm => ({ times, weekdays, action, params })

describe('formOf', () => {
  it("loads each rule's times, days, action and params, and the default", () => {
    const form = formOf(saved, steps)

    expect(form.name).toBe('Johto nights')
    expect(form.endWhenComplete).toBe(false)
    expect(form.rules).toEqual([
      step('Wild Encounter', { PhaseName: '1-4 Route 30', TargetType: 'PhaseTarget', EncounterType: 'Run', TrainType: 'None', xRange: '2', yRange: '2', TargetLevel: '', SwitchSlot: '2', Pickup: '' }, ['Night']),
      step('Wait', {}, ['Morning'], ['Friday'])
    ])
    expect(form.default).toEqual(
      step('Wild Encounter', { PhaseName: '2-1 Route 32', TargetType: 'ShinyTarget', EncounterType: 'Spin', TrainType: 'Standard', xRange: '0', yRange: '0', TargetLevel: '101', SwitchSlot: '2', Pickup: 'true' })
    )
  })
})

describe('planBody', () => {
  const form: PlanForm = {
    name: 'Pickup days',
    rules: [
      step('Wait', {}, ['Night'], ['Sunday']),
      step('Wild Encounter', { ...initialParamsFor(wild), PhaseName: '1-4 Route 30', TargetType: 'PhaseTarget', EncounterType: 'Spin' }, ['Day'])
    ],
    default: step('Wild Encounter', { ...initialParamsFor(wild), PhaseName: '2-1 Route 32', TargetType: 'ShinyTarget', EncounterType: 'Run', xRange: '3' }),
    endWhenComplete: false
  }

  it("builds R6a's Plan shape, rules in order, default without times", () => {
    expect(planBody(form, 'HGSS', steps)).toStrictEqual({
      game: 'HGSS',
      name: 'Pickup days',
      rules: [
        { times: ['Night'], weekdays: ['Sunday'], arguments: { eventName: 'Wait' } },
        { times: ['Day'], weekdays: [], arguments: { eventName: 'Wild Encounter', phaseName: '1-4 Route 30', targetType: 'PhaseTarget', encounterType: 'Spin', trainType: 'None' } }
      ],
      default: { eventName: 'Wild Encounter', phaseName: '2-1 Route 32', targetType: 'ShinyTarget', encounterType: 'Run', xRange: 3, yRange: 0, trainType: 'None' },
      endWhenComplete: false
    })
  })

  it('carries endWhenComplete', () => {
    expect(planBody({ ...form, endWhenComplete: true }, 'HGSS', steps).endWhenComplete).toBe(true)
  })

  it('a loaded plan saves back to the body it came from, minus nulls', () => {
    const body = planBody(formOf(saved, steps), 'HGSS', steps)

    expect(body.rules.map((r) => r.arguments)).toStrictEqual(saved.rules.map((r) => withoutNulls(r.arguments)))
    expect(body.rules.map((r) => [r.times, r.weekdays])).toEqual(saved.rules.map((r) => [r.times, r.weekdays]))
    expect(body.default).toStrictEqual(withoutNulls(saved.default))
  })
})

describe('withAction', () => {
  it("changing a rule's action resets its params and keeps its times and days", () => {
    const rule = step('Wild Encounter', { ...initialParamsFor(wild), PhaseName: '1-4 Route 30' }, ['Night'], ['Friday'])

    const toWait = withAction(rule, wait)
    const back = withAction(toWait, wild)

    expect(toWait).toEqual(step('Wait', {}, ['Night'], ['Friday']))
    expect(back).toEqual(step('Wild Encounter', initialParamsFor(wild), ['Night'], ['Friday']))
  })
})

describe('moveRule', () => {
  const a = step('Wait', {}, ['Morning'])
  const b = step('Wait', {}, ['Day'])
  const c = step('Wait', {}, ['Night'])

  it('up swaps with the rule above', () => {
    expect(moveRule([a, b, c], 1, -1)).toEqual([b, a, c])
  })

  it('down swaps with the rule below', () => {
    expect(moveRule([a, b, c], 1, 1)).toEqual([a, c, b])
  })

  it('the first cannot go up and the last cannot go down', () => {
    const rules = [a, b, c]

    expect(moveRule(rules, 0, -1)).toBe(rules)
    expect(moveRule(rules, 2, 1)).toBe(rules)
  })
})

describe('toggled', () => {
  const times = ['Morning', 'Day', 'Night']

  it('adds and removes a chip in Morning, Day, Night order', () => {
    expect(toggled(['Night'], 'Morning', times)).toEqual(['Morning', 'Night'])
    expect(toggled(['Morning', 'Night'], 'Night', times)).toEqual(['Morning'])
  })

  it('keeps weekdays in Sunday-first order', () => {
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

    expect(toggled(['Saturday', 'Monday'], 'Sunday', days)).toEqual(['Sunday', 'Monday', 'Saturday'])
  })
})

describe('editorMilestones', () => {
  const phase = (name: string, archived = false, gameFiles: string[] = []): PhaseInfo => ({
    gameName: 'HGSS', name, pokemon: [], isStatic: false, location: null, gameFiles, archived
  })

  it("lists the game's milestones in natural order", () => {
    const names = editorMilestones([phase('1-10 Route 46'), phase('2-1 Route 32'), phase('1-4 Route 30')], []).map((m) => m.name)

    expect(names).toEqual(['1-4 Route 30', '1-10 Route 46', '2-1 Route 32'])
  })

  it('leaves out archived ones unless a step uses one', () => {
    const phases = [phase('1-4 Route 30', true), phase('2-1 Route 32')]

    expect(editorMilestones(phases, []).map((m) => m.name)).toEqual(['2-1 Route 32'])
    expect(editorMilestones(phases, ['1-4 Route 30']).map((m) => m.name)).toEqual(['1-4 Route 30', '2-1 Route 32'])
  })

  it('ignores game-file ticks', () => {
    const milestones = editorMilestones([phase('1-4 Route 30', false, ['HeartGold.nds']), phase('2-1 Route 32', false, [])], [])

    expect(milestones).toEqual([
      { name: '1-4 Route 30', modality: null, huntable: true, complete: false, anchor: null, anchorSet: false },
      { name: '2-1 Route 32', modality: null, huntable: true, complete: false, anchor: null, anchorSet: false }
    ])
  })
})
