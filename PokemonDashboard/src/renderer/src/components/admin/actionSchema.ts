import type { ConditionalParameter, EventArgumentOptions, NewEventLegArguments } from '../../types/api'

// Maps internal parameter names to user-friendly display labels
const PARAM_DISPLAY_NAMES: Record<string, string> = {
  PhaseName: 'Milestone',
  GameName: 'Game',
  TargetType: 'Target Type',
  SoundOn: 'Sound On',
  EncounterType: 'Encounter Type',
  xRange: 'X Range',
  yRange: 'Y Range',
  TrainType: 'Train Type',
  TargetLevel: 'Target Level',
  SwitchSlot: 'Battler',
  Mode: 'Game',
}
export const getParamLabel = (param: string): string => PARAM_DISPLAY_NAMES[param] ?? param

// Maps EncounterType string names to C# enum integer values (Spin=0, Run=1)
const ENCOUNTER_TYPE_VALUES: Record<string, number> = { Spin: 0, Run: 1 }

// Maps TrainType string names to C# enum integer values (None=0, Standard=1, Switch=2)
const TRAIN_TYPE_VALUES: Record<string, number> = { None: 0, Standard: 1, Switch: 2 }

/** Seed value for a freshly-discovered parameter. TrainType must default to 'None' rather than ''
 *  so the Neq condition guarding TargetLevel starts out unmet. */
function defaultParamValue(param: string): string {
  switch (param.toLowerCase()) {
    case 'soundon':
      return 'false'
    case 'xrange':
    case 'yrange':
      return '0'
    case 'traintype':
      return 'None'
    case 'switchslot':
      return '2'
    default:
      return ''
  }
}

/** Evaluate whether a condition is met given the current param values.
 *  conditionType may be the string 'Eq'/'Neq' or the integer 0/1 (C# enum default serialization). */
export function isConditionMet(condition: { conditionType: string | number; name: string; value: string }, params: Record<string, string>): boolean {
  const isEq = condition.conditionType === 'Eq' || condition.conditionType === 0
  if (isEq) {
    return params[condition.name] === condition.value
  }
  const isNeq = condition.conditionType === 'Neq' || condition.conditionType === 1
  if (isNeq) {
    return params[condition.name] !== condition.value
  }
  return false
}

/** Seed values for every parameter a schema declares. */
export function initialParamsFor(schema: EventArgumentOptions): Record<string, string> {
  const params: Record<string, string> = {}
  schema.requiredParameters?.forEach((param) => {
    params[param] = ''
  })
  schema.optionalParameters?.forEach((param) => {
    params[param] = defaultParamValue(param)
  })
  schema.conditionalParameters?.forEach((cp) => {
    params[cp.name] = defaultParamValue(cp.name)
  })
  return params
}

/** True once every required parameter of the schema has a value. */
export function requiredParamsFilled(schema: EventArgumentOptions, params: Record<string, string>): boolean {
  const required = schema.requiredParameters ?? []
  return required.every((p) => params[p]?.trim())
}

/** The conditional parameters currently on screen. */
export function visibleConditionalsOf(schema: EventArgumentOptions, params: Record<string, string>): ConditionalParameter[] {
  return (schema.conditionalParameters ?? []).filter((cp) => isConditionMet(cp.condition, params))
}

/** Whether this schema is offering its list of legs. An action without a leg condition always is. */
export function legsVisible(schema: EventArgumentOptions, params: Record<string, string>): boolean {
  return !schema.legCondition || isConditionMet(schema.legCondition, params)
}

/** Every parameter the form is showing, which is the set the submission is built from. */
export function visibleParamNames(schema: EventArgumentOptions, params: Record<string, string>): Set<string> {
  return new Set([
    ...(schema.requiredParameters ?? []),
    ...(schema.optionalParameters ?? []),
    ...visibleConditionalsOf(schema, params).map((cp) => cp.name)
  ])
}

/** Maps one form's parameter values onto the JSON body of an encounter leg. */
/**
 * Parameter names buildLegBody knows how to place. Anything outside this set is passed through
 * verbatim, so an action with a new parameter reaches the server without a dashboard change --
 * previously such a parameter rendered an input and was then silently dropped on submit.
 */
export const LEG_PARAM_KEYS = new Set([
  'PhaseName', 'TargetType', 'EncounterType', 'xRange', 'yRange', 'TrainType', 'TargetLevel', 'SwitchSlot'
])

export function buildLegBody(params: Record<string, string>): NewEventLegArguments {
  const leg: NewEventLegArguments = {}
  if (params['PhaseName'] !== undefined) {
    leg.phaseName = params['PhaseName']
  }
  if (params['TargetType'] !== undefined) {
    leg.targetType = params['TargetType']
  }
  if (params['EncounterType'] !== undefined && params['EncounterType'] !== '') {
    leg.encounterType = ENCOUNTER_TYPE_VALUES[params['EncounterType']]
  }
  if (params['xRange'] !== undefined) {
    leg.xRange = parseInt(params['xRange'], 10) || 0
  }
  if (params['yRange'] !== undefined) {
    leg.yRange = parseInt(params['yRange'], 10) || 0
  }
  if (params['TrainType'] !== undefined) {
    leg.trainType = TRAIN_TYPE_VALUES[params['TrainType']] ?? 0
  }
  if (params['TargetLevel'] !== undefined && params['TargetLevel'] !== '' && params['TargetLevel'] !== '0') {
    leg.targetLevel = parseInt(params['TargetLevel'], 10)
  }
  if (params['TrainType'] === 'Switch' && params['SwitchSlot'] !== undefined && params['SwitchSlot'] !== '') {
    leg.switchSlot = parseInt(params['SwitchSlot'], 10)
  }
  // Same rule as the flat forms: anything the allowlist does not model goes through under its own
  // name, so a schema with new leg parameters works without another silent-drop bug here.
  // Checkbox values travel as booleans, since a server-side bool? cannot bind the string "true".
  for (const [key, value] of Object.entries(params)) {
    if (LEG_PARAM_KEYS.has(key)) continue
    if (value === '') continue
    ;(leg as Record<string, unknown>)[key] = value === 'true' ? true : value === 'false' ? false : value
  }
  return leg
}
