import { describe, expect, it } from 'vitest'
import {
  buildLegBody,
  getParamLabel,
  initialParamsFor,
  isConditionMet,
  legsVisible,
  requiredParamsFilled,
  visibleConditionalsOf,
  visibleParamNames
} from './actionSchema'

describe('getParamLabel', () => {
  it('maps a known parameter to its display label', () => {
    expect(getParamLabel('PhaseName')).toBe('Milestone')
    expect(getParamLabel('SwitchSlot')).toBe('Battler')
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

  it("passes params outside LEG_PARAM_KEYS through, skipping blanks and turning 'true'/'false' into booleans", () => {
    expect(buildLegBody({ Extra: 'abc', Flag: 'true', Off: 'false', Empty: '' })).toEqual({
      Extra: 'abc',
      Flag: true,
      Off: false
    })
  })
})
