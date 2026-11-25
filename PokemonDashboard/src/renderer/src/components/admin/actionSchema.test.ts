import { describe, expect, it } from 'vitest'
import {
  buildLegBody,
  choiceOptions,
  encounterTypeChoices,
  gamesFor,
  getParamLabel,
  initialParamsFor,
  isConditionMet,
  LEG_PARAM_KEYS,
  legsVisible,
  pickupChoice,
  planPick,
  planPreview,
  planTime,
  requiredParamsFilled,
  visibleConditionalsOf,
  visibleParamNames
} from './actionSchema'
import type { EventArgumentOptions, GameRomInfo, PlanRule } from '../../types/api'

describe('getParamLabel', () => {
  it('maps a known parameter to its display label', () => {
    expect(getParamLabel('PhaseName')).toBe('Milestone')
    expect(getParamLabel('SwitchSlot')).toBe('Battler')
    expect(getParamLabel('TargetLevel')).toBe('Target Level (101 = no end)')
  })

  it('returns an unknown parameter unchanged', () => {
    expect(getParamLabel('Foo')).toBe('Foo')
  })
})

describe('isConditionMet', () => {
  it('Eq holds only when the value matches', () => {
    const condition = { conditionType: 'Eq' as const, name: 'TrainType', value: 'Switch' }

    expect(isConditionMet(condition, { TrainType: 'Switch' })).toBe(true)
    expect(isConditionMet(condition, { TrainType: 'None' })).toBe(false)
  })

  it('Neq holds only when the value differs, and a missing param counts as differing', () => {
    const condition = { conditionType: 'Neq' as const, name: 'TrainType', value: 'None' }

    expect(isConditionMet(condition, { TrainType: 'Standard' })).toBe(true)
    expect(isConditionMet(condition, { TrainType: 'None' })).toBe(false)
    expect(isConditionMet(condition, {})).toBe(true)
  })
})

describe('initialParamsFor', () => {
  it('seeds required params empty and optional and conditional params with their defaults', () => {
    const schema = {
      name: 'Hunt',
      requiredParameters: ['PhaseName'],
      optionalParameters: ['SoundOn', 'xRange', 'TrainType', 'TargetType'],
      conditionalParameters: [
        {
          condition: { conditionType: 'Eq' as const, name: 'TrainType', value: 'Switch' },
          name: 'SwitchSlot',
          hideIfConditionNotMet: true
        }
      ]
    }

    expect(initialParamsFor(schema)).toEqual({
      PhaseName: '',
      SoundOn: 'false',
      xRange: '0',
      TrainType: 'None',
      TargetType: '',
      SwitchSlot: '2'
    })
  })

  it('returns an empty object for a schema with no parameter lists', () => {
    expect(initialParamsFor({ name: 'Stop' })).toEqual({})
  })
})

describe('requiredParamsFilled', () => {
  it('is false while a required param is blank or whitespace and true once all are set', () => {
    const schema = { name: 'Hunt', requiredParameters: ['PhaseName', 'TargetType'] }

    expect(requiredParamsFilled(schema, { PhaseName: 'Route 1', TargetType: '' })).toBe(false)
    expect(requiredParamsFilled(schema, { PhaseName: 'Route 1', TargetType: '   ' })).toBe(false)
    expect(requiredParamsFilled(schema, { PhaseName: 'Route 1', TargetType: 'Pidgey' })).toBe(true)
  })

  it('is true when the schema has no required parameters', () => {
    expect(requiredParamsFilled({ name: 'Stop' }, {})).toBe(true)
  })
})

describe('visibleConditionalsOf', () => {
  it('returns only conditionals whose condition holds', () => {
    const targetLevel = {
      condition: { conditionType: 'Neq' as const, name: 'TrainType', value: 'None' },
      name: 'TargetLevel',
      hideIfConditionNotMet: true
    }
    const schema = { name: 'Hunt', conditionalParameters: [targetLevel] }

    expect(visibleConditionalsOf(schema, { TrainType: 'None' })).toEqual([])
    expect(visibleConditionalsOf(schema, { TrainType: 'Standard' })).toEqual([targetLevel])
  })
})

describe('legsVisible', () => {
  it('is true without a legCondition', () => {
    expect(legsVisible({ name: 'Chain' }, {})).toBe(true)
  })

  it('follows the legCondition when present', () => {
    const schema = {
      name: 'Chain',
      legCondition: { conditionType: 'Eq' as const, name: 'Mode', value: 'Legs' }
    }

    expect(legsVisible(schema, { Mode: 'Legs' })).toBe(true)
    expect(legsVisible(schema, { Mode: 'Single' })).toBe(false)
  })
})

describe('visibleParamNames', () => {
  it('is required plus optional plus the visible conditionals', () => {
    const schema = {
      name: 'Hunt',
      requiredParameters: ['PhaseName'],
      optionalParameters: ['TrainType'],
      conditionalParameters: [
        {
          condition: { conditionType: 'Neq' as const, name: 'TrainType', value: 'None' },
          name: 'TargetLevel',
          hideIfConditionNotMet: true
        },
        {
          condition: { conditionType: 'Eq' as const, name: 'TrainType', value: 'Switch' },
          name: 'SwitchSlot',
          hideIfConditionNotMet: true
        }
      ]
    }

    expect(visibleParamNames(schema, { TrainType: 'Standard' })).toEqual(
      new Set(['PhaseName', 'TrainType', 'TargetLevel'])
    )
  })
})

describe('gamesFor', () => {
  const rom = (handle: string, flavor: string | null, revisionSupport: string): GameRomInfo => ({
    handle,
    console: 'GBA',
    flavor,
    gameName: handle,
    gameCode: '',
    language: 'English',
    revision: 0,
    displayName: handle,
    revisionSupport
  })
  const games = [
    rom('Sapphire', 'RS', 'Tested'),
    rom('Ruby', 'RS', 'SupportedUntested'),
    rom('HeartGold', 'HGSS', 'Tested')
  ]

  it('keeps only tested RS when the schema has an RS tested-only gameFilter', () => {
    const schema = { name: 'Wishmaker Jirachi Hunt', gameFilter: { flavor: 'RS', testedOnly: true } }

    expect(gamesFor(schema, games).map((g) => g.handle)).toEqual(['Sapphire'])
  })

  it('returns every game when gameFilter is null or absent', () => {
    expect(gamesFor({ name: 'Launch Bot', gameFilter: null }, games)).toEqual(games)
    expect(gamesFor({ name: 'Launch Bot' }, games)).toEqual(games)
  })
})

describe('buildLegBody', () => {
  it('maps known params onto the leg fields', () => {
    const leg = buildLegBody({
      PhaseName: 'Route 1',
      TargetType: 'Pidgey',
      EncounterType: 'Run',
      xRange: '3',
      yRange: 'x',
      TrainType: 'Standard',
      TargetLevel: '50'
    })

    expect(leg).toEqual({
      phaseName: 'Route 1',
      targetType: 'Pidgey',
      encounterType: 'Run',
      xRange: 3,
      yRange: 0,
      trainType: 'Standard',
      targetLevel: 50
    })
  })

  it('omits encounterType and targetLevel when blank, and targetLevel when 0', () => {
    expect(buildLegBody({ EncounterType: '', TargetLevel: '' })).toEqual({})
    expect(buildLegBody({ TargetLevel: '0' })).toEqual({})
  })

  it('sets switchSlot only in Switch train mode', () => {
    expect(buildLegBody({ TrainType: 'Switch', SwitchSlot: '3' })).toEqual({ trainType: 'Switch', switchSlot: 3 })
    expect(buildLegBody({ TrainType: 'Standard', SwitchSlot: '3' })).toEqual({ trainType: 'Standard' })
  })

  it('sends pickup as a boolean and omits it when unset', () => {
    expect(buildLegBody({ TrainType: 'Standard', Pickup: 'true' })).toEqual({ trainType: 'Standard', pickup: true })
    expect(buildLegBody({ TrainType: 'Standard', Pickup: 'false' })).toEqual({ trainType: 'Standard', pickup: false })
    expect(buildLegBody({ TrainType: 'Standard', Pickup: '' })).toEqual({ trainType: 'Standard' })
    expect(buildLegBody({ TrainType: 'Standard' })).toEqual({ trainType: 'Standard' })
    expect(buildLegBody({ TrainType: 'None', Pickup: 'true' })).toEqual({ trainType: 'None' })
  })

  it('LEG_PARAM_KEYS has Pickup', () => {
    expect(LEG_PARAM_KEYS.has('Pickup')).toBe(true)
  })

  it("passes params outside LEG_PARAM_KEYS through, skipping blanks and turning 'true'/'false' into booleans", () => {
    expect(buildLegBody({ Extra: 'abc', Flag: 'true', Off: 'false', Empty: '' })).toEqual({
      Extra: 'abc',
      Flag: true,
      Off: false
    })
  })
})

describe('encounterTypeChoices', () => {
  const disabled: EventArgumentOptions = {
    name: 'Wild Encounter',
    disabledChoices: { EncounterType: { Headbutt: 'No Pokémon in your party knows Headbutt.' } }
  }

  it('offers Spin, Run and Headbutt in that order, all enabled without disabledChoices', () => {
    for (const schema of [{ name: 'Wild Encounter' }, { name: 'Wild Encounter', disabledChoices: null }]) {
      const { options } = encounterTypeChoices(schema, undefined)

      expect(options.map((o) => o.value)).toEqual(['Spin', 'Run', 'Headbutt'])
      expect(options.map((o) => o.label)).toEqual(['Spin', 'Run', 'Headbutt'])
      expect(options.some((o) => o.disabled)).toBe(false)
    }
  })

  it('greys a choice named in disabledChoices.EncounterType with its reason in the label', () => {
    const { options } = encounterTypeChoices(disabled, undefined)

    expect(options).toEqual([
      { value: 'Spin', label: 'Spin', disabled: false },
      { value: 'Run', label: 'Run', disabled: false },
      { value: 'Headbutt', label: 'Headbutt (No Pokémon in your party knows Headbutt.)', disabled: true }
    ])
  })

  it('uses the disabledChoices of the leg schema', () => {
    const action: EventArgumentOptions = { name: 'Chained Encounter', legParameters: { ...disabled, name: 'Leg' } }

    const leg = encounterTypeChoices(action.legParameters!, undefined)
    const parent = encounterTypeChoices(action, undefined)

    expect(leg.options.find((o) => o.value === 'Headbutt')!.disabled).toBe(true)
    expect(parent.options.find((o) => o.value === 'Headbutt')!.disabled).toBe(false)
  })

  it('clears a selected value that is now disabled and keeps an enabled one', () => {
    expect(encounterTypeChoices(disabled, 'Headbutt').selected).toBe('')
    expect(encounterTypeChoices(disabled, 'Run').selected).toBe('Run')
    expect(encounterTypeChoices(disabled, undefined).selected).toBe('')
  })
})

describe('choiceOptions', () => {
  const noTarget = 'No Target needs a train mode, which Ruby and Sapphire do not have yet.'
  const training = 'Training is not available on Ruby and Sapphire yet.'
  const rs: EventArgumentOptions = {
    name: 'Wild Encounter',
    disabledChoices: {
      TargetType: { NoTarget: noTarget },
      TrainType: { Standard: training, Switch: training }
    }
  }

  it('greys TargetType NoTarget with its reason', () => {
    expect(choiceOptions(rs, 'TargetType', ['PhaseTarget', 'ShinyTarget', 'NoTarget'])).toEqual([
      { value: 'PhaseTarget', label: 'PhaseTarget', disabled: false },
      { value: 'ShinyTarget', label: 'ShinyTarget', disabled: false },
      { value: 'NoTarget', label: `NoTarget (${noTarget})`, disabled: true }
    ])
  })

  it('greys TrainType Standard and Switch with their reason', () => {
    expect(choiceOptions(rs, 'TrainType', ['None', 'Standard', 'Switch'])).toEqual([
      { value: 'None', label: 'None', disabled: false },
      { value: 'Standard', label: `Standard (${training})`, disabled: true },
      { value: 'Switch', label: `Switch (${training})`, disabled: true }
    ])
  })

  it('enables everything without disabledChoices', () => {
    for (const schema of [{ name: 'Wild Encounter' }, { name: 'Wild Encounter', disabledChoices: null }]) {
      for (const param of ['TargetType', 'TrainType']) {
        const options = choiceOptions(schema, param, ['None', 'Standard', 'NoTarget'])

        expect(options.every((o) => !o.disabled && o.label === o.value)).toBe(true)
      }
    }
  })
})

describe('pickupChoice', () => {
  const reason = 'No Pokémon in your party has the Pickup ability.'
  const disabled: EventArgumentOptions = { name: 'Wild Encounter', disabledChoices: { Pickup: { true: reason } } }

  it('is enabled and labelled Pickup without disabledChoices', () => {
    for (const schema of [{ name: 'Wild Encounter' }, { name: 'Wild Encounter', disabledChoices: null }]) {
      expect(pickupChoice(schema, undefined)).toEqual({ label: 'Pickup', disabled: false, checked: false })
    }
  })

  it('is disabled with the reason in the label', () => {
    const choice = pickupChoice(disabled, undefined)

    expect(choice.label).toBe(`Pickup (${reason})`)
    expect(choice.disabled).toBe(true)
  })

  it('uses the disabledChoices of the leg schema', () => {
    const action: EventArgumentOptions = { name: 'Chained Encounter', legParameters: { ...disabled, name: 'Leg' } }

    expect(pickupChoice(action.legParameters!, undefined).disabled).toBe(true)
    expect(pickupChoice(action, undefined).disabled).toBe(false)
  })

  it('clears a checked box that is now disabled', () => {
    expect(pickupChoice(disabled, 'true').checked).toBe(false)
    expect(pickupChoice({ name: 'Wild Encounter' }, 'true').checked).toBe(true)
    expect(pickupChoice({ name: 'Wild Encounter' }, undefined).checked).toBe(false)
    expect(pickupChoice({ name: 'Wild Encounter' }, 'false').checked).toBe(false)
  })
})

// The same cases as HuntPlanTests.cs, so the preview and the hunt pick alike.
describe('hunt plans', () => {
  const plan = (...rules: PlanRule[]): { default: string; rules: PlanRule[] } => ({ default: '36-land', rules })

  it('planTime has HuntPlan.TimeOfDay boundaries', () => {
    expect([3, 4, 9, 10, 19, 20, 23, 0].map(planTime)).toEqual(['Night', 'Morning', 'Morning', 'Day', 'Day', 'Night', 'Night', 'Night'])
  })

  it('planPick: the first matching rule wins', () => {
    const p = plan({ times: ['Night'], weekdays: [], milestone: '34-land' }, { times: ['Night'], weekdays: [], milestone: '33-land' })

    expect(planPick(p, 'Monday', 21)).toEqual({ milestone: '34-land', rule: 1 })
  })

  it('planPick: no rule matching is the default', () => {
    expect(planPick(plan({ times: ['Night'], weekdays: [], milestone: '34-land' }), 'Monday', 12)).toEqual({ milestone: '36-land', rule: null })
  })

  it('planPick: a weekday matches only its day', () => {
    const p = plan({ times: [], weekdays: ['Friday'], milestone: 'Union Cave' })

    expect(planPick(p, 'Friday', 12)).toEqual({ milestone: 'Union Cave', rule: 1 })
    expect(planPick(p, 'Thursday', 12)).toEqual({ milestone: '36-land', rule: null })
  })

  it('planPick: empty sets match any', () => {
    const p = plan({ times: [], weekdays: [], milestone: '34-land' })

    expect(planPick(p, 'Sunday', 0).milestone).toBe('34-land')
    expect(planPick(p, 'Wednesday', 12).milestone).toBe('34-land')
  })

  it('planPreview follows the rule order for a weekday', () => {
    const night: PlanRule = { times: ['Night'], weekdays: [], milestone: '34-land' }
    const friday: PlanRule = { times: [], weekdays: ['Friday'], milestone: '99-land' }

    expect(planPreview(plan(night, friday), 'Sunday')).toEqual([
      { from: 0, to: 4, milestone: '34-land', rule: 1 },
      { from: 4, to: 20, milestone: '36-land', rule: null },
      { from: 20, to: 24, milestone: '34-land', rule: 1 }
    ])
    expect(planPreview(plan(night, friday), 'Friday')).toEqual([
      { from: 0, to: 4, milestone: '34-land', rule: 1 },
      { from: 4, to: 20, milestone: '99-land', rule: 2 },
      { from: 20, to: 24, milestone: '34-land', rule: 1 }
    ])
    expect(planPreview(plan(friday, night), 'Friday')).toEqual([{ from: 0, to: 24, milestone: '99-land', rule: 1 }])
  })

  it('a chosen plan stands in for the milestone; with none the milestone is required, as before (FR-42)', () => {
    const schema = { name: 'Wild Encounter', requiredParameters: ['PhaseName', 'TargetType'] }

    expect(requiredParamsFilled(schema, { PhaseName: '', TargetType: 'ShinyTarget', HuntPlan: 'Johto' })).toBe(true)
    expect(requiredParamsFilled(schema, { PhaseName: '', TargetType: 'ShinyTarget', HuntPlan: '' })).toBe(false)
    expect(requiredParamsFilled(schema, { PhaseName: '', TargetType: '', HuntPlan: 'Johto' })).toBe(false)
  })

  it('with no plan chosen the Start body is what it was without the field (FR-42)', () => {
    const today = { PhaseName: '34-land', TargetType: 'ShinyTarget', EncounterType: 'Spin', TrainType: 'None' }

    expect(buildLegBody({ ...today, HuntPlan: '' })).toStrictEqual(buildLegBody(today))
    expect(buildLegBody({ ...today, HuntPlan: 'Johto' })).toStrictEqual({ ...buildLegBody(today), HuntPlan: 'Johto' })
  })

  it('labels the field Hunt Plan', () => {
    expect(getParamLabel('HuntPlan')).toBe('Hunt Plan')
  })
})
