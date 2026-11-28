import React, { useEffect, useState } from 'react'
import type { ConditionalParameter, EventArgumentOptions, GameRomInfo, PartySlotInfo, PickerMilestone } from '../../types/api'
import { romLabel } from '../../types/api'
import { choiceOptions, encounterTypeChoices, getParamLabel, isConditionMet, pickupChoice } from './actionSchema'

const TRAIN_TYPE_OPTIONS = ['None', 'Standard', 'Switch']

const TARGET_TYPE_OPTIONS = ['PhaseTarget', 'ShinyTarget', 'NoTarget']

// The selected milestone stays listed whatever the box says, so the select never shows one milestone while holding another.
export function launchMilestones(milestones: PickerMilestone[], selected: string, incompleteOnly: boolean): PickerMilestone[] {
  return milestones.filter((m) => m.name === selected || !incompleteOnly || !m.complete)
}

/**
 * The inputs for one set of encounter parameters, driven entirely by the schema the server sent.
 * Rendered once for a single action and once per leg of a chained action.
 */
export function ActionParamFields({
  schema,
  params,
  onChange,
  availablePhases,
  party,
  onReloadParty,
  partyLoading,
  games,
  offerIncompleteOnly = true
}: {
  schema: EventArgumentOptions
  params: Record<string, string>
  onChange: (param: string, value: string) => void
  availablePhases: PickerMilestone[]
  party: PartySlotInfo[] | undefined
  // Absent where there is no bot, so no party to reload.
  onReloadParty?: () => void
  partyLoading: boolean
  // Detected ROMs, for schemas whose entries pick a game (the starter hunt's legs do). When absent,
  // GameName falls through to a plain text input like any other parameter.
  games?: GameRomInfo[]
  // False in the Plan Editor, where no milestone reads as complete.
  offerIncompleteOnly?: boolean
}): JSX.Element {
  const [incompleteOnly, setIncompleteOnly] = useState(false)
  // A milestone with a location is a wild one, so only an encounter form (Wild Encounter, a chain's leg) offers it;
  // Togepi, Primo and Static Encounter list milestones with no location only.
  const offered = schema.requiredParameters?.includes('EncounterType')
    ? availablePhases
    : availablePhases.filter((m) => m.modality === null)
  const shownMilestones = launchMilestones(offered, params['PhaseName'] ?? '', incompleteOnly)
  const encounter = encounterTypeChoices(schema, params['EncounterType'])
  useEffect(() => {
    if (params['EncounterType'] && encounter.selected !== params['EncounterType']) onChange('EncounterType', '')
  }, [encounter.selected, params['EncounterType']])
  const pickup = pickupChoice(schema, params['Pickup'])
  useEffect(() => {
    if (params['Pickup'] === 'true' && !pickup.checked) onChange('Pickup', 'false')
  }, [pickup.checked, params['Pickup']])
  const visibleConditionals = (schema.conditionalParameters ?? []).filter((cp: ConditionalParameter) => {
    return isConditionMet(cp.condition, params) || !cp.hideIfConditionNotMet
  })
  return (
    <>
      {schema.requiredParameters?.map((param) => (
        // The milestone picker carries param-PhaseName on the wrapper of its one milestone select, not on the row, so
        // the row's Incomplete only box is not inside it.
        <div key={param} data-testid={param === 'PhaseName' ? undefined : `param-${param}`} className="flex items-center gap-2">
          <label className="text-xs text-slate-500 w-20 flex-shrink-0">{getParamLabel(param)}</label>
          {param === 'GameName' && games ? (
            <select
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            >
              <option value="">Select a game...</option>
              {games.map((game) => (
                <option key={game.handle} value={game.handle}>{romLabel(game)}</option>
              ))}
            </select>
          ) : param === 'PhaseName' ? (
            <div className="flex-1 space-y-1">
              <div data-testid="param-PhaseName" className="space-y-1">
                {/* A "no hunt yet" milestone stays selectable, so Start shows the server's reason. */}
                <select
                  value={params[param] ?? ''}
                  onChange={(e) => onChange(param, e.target.value)}
                  className="w-full bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
                >
                  <option value="">Select a milestone...</option>
                  {shownMilestones.map((m) => (
                    <option key={m.name} value={m.name}>
                      {`${m.name}${m.huntable ? '' : ' (no hunt yet)'}`}
                    </option>
                  ))}
                </select>
              </div>
              {offerIncompleteOnly && (
                <label className="flex items-center gap-1 text-xs text-slate-500">
                  <input type="checkbox" checked={incompleteOnly} onChange={(e) => setIncompleteOnly(e.target.checked)} className="w-3 h-3 accent-indigo-500" />
                  Incomplete only
                </label>
              )}
            </div>
          ) : param === 'TargetType' ? (
            <select
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            >
              <option value="">Select target type...</option>
              {choiceOptions(schema, 'TargetType', TARGET_TYPE_OPTIONS).map((o) => (
                <option key={o.value} value={o.value} disabled={o.disabled}>{o.label}</option>
              ))}
            </select>
          ) : param === 'EncounterType' ? (
            <select
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            >
              <option value="">Select encounter type...</option>
              {encounter.options.map((o) => (
                <option key={o.value} value={o.value} disabled={o.disabled}>{o.label}</option>
              ))}
            </select>
          ) : schema.choices?.[param] ? (
            <select
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            >
              <option value="">Select...</option>
              {schema.choices[param].map((c) => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
          ) : (
            <input
              type="text"
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              placeholder={param}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
            />
          )}
        </div>
      ))}
      {schema.optionalParameters && schema.optionalParameters.length > 0 && (
        <div className="pt-0.5 border-t border-slate-700/30">
          <span className="text-xs text-slate-600">Optional</span>
          {schema.optionalParameters.map((param) => (
            <div key={param} data-testid={`param-${param}`} className="flex items-center gap-2 mt-0.5">
              <label className="text-xs text-slate-500 w-20 flex-shrink-0">{getParamLabel(param)}</label>
              {param.toLowerCase() === 'soundon' || param === 'RequireFemale' ? (
                <input
                  type="checkbox"
                  checked={params[param] === 'true'}
                  onChange={(e) => onChange(param, e.target.checked ? 'true' : 'false')}
                  className="w-4 h-4 accent-indigo-500"
                />
              ) : param === 'TrainType' ? (
                <select
                  value={params[param] ?? 'None'}
                  onChange={(e) => onChange(param, e.target.value)}
                  className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
                >
                  {choiceOptions(schema, 'TrainType', TRAIN_TYPE_OPTIONS).map((o) => (
                    <option key={o.value} value={o.value} disabled={o.disabled}>{o.label}</option>
                  ))}
                </select>
              ) : (
                <input
                  type="text"
                  value={params[param] ?? ''}
                  onChange={(e) => onChange(param, e.target.value)}
                  placeholder={param}
                  className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
                />
              )}
            </div>
          ))}
        </div>
      )}
      {visibleConditionals.length > 0 && (
        <div className="pt-0.5 border-t border-slate-700/30">
          <span className="text-xs text-slate-600">Conditional</span>
          {visibleConditionals.map((cp: ConditionalParameter) => {
            const condMet = isConditionMet(cp.condition, params)
            // The battler picker lists the bot's live party. Slot 1 is the trainee
            // and is never selectable.
            const isBattlerPicker = cp.name === 'SwitchSlot' && party !== undefined && party.length > 0
            return (
              <div key={cp.name} data-testid={`param-${cp.name}`} className="flex items-center gap-2 mt-0.5">
                <label className="text-xs text-slate-500 w-20 flex-shrink-0">
                  {cp.name === 'Pickup' ? pickup.label : getParamLabel(cp.name)}
                </label>
                {cp.name === 'Pickup' ? (
                  <input
                    type="checkbox"
                    checked={pickup.checked}
                    onChange={(e) => onChange(cp.name, e.target.checked ? 'true' : 'false')}
                    disabled={!condMet || pickup.disabled}
                    className="w-4 h-4 accent-indigo-500 disabled:opacity-40"
                  />
                ) : isBattlerPicker ? (
                  <select
                    value={params[cp.name] ?? '2'}
                    onChange={(e) => onChange(cp.name, e.target.value)}
                    disabled={!condMet}
                    className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 disabled:opacity-40"
                  >
                    {party.filter((p) => p.slot > 1).map((p) => (
                      <option key={p.slot} value={String(p.slot)}>
                        {`Slot ${p.slot} — ${p.name} (Lv ${p.level})`}
                      </option>
                    ))}
                  </select>
                ) : (
                  <input
                    type="number"
                    value={params[cp.name] ?? '0'}
                    onChange={(e) => onChange(cp.name, e.target.value)}
                    disabled={!condMet}
                    max={cp.name === 'TargetLevel' ? 101 : undefined}
                    placeholder="0"
                    className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-40"
                  />
                )}
                {cp.name === 'SwitchSlot' && onReloadParty && (
                  <button
                    type="button"
                    onClick={onReloadParty}
                    disabled={partyLoading}
                    title="Reload party from the running game"
                    className="text-xs text-slate-400 hover:text-white px-1.5 py-0.5 rounded border border-slate-600 disabled:opacity-40"
                  >
                    {partyLoading ? '…' : '⟳'}
                  </button>
                )}
              </div>
            )
          })}
        </div>
      )}
    </>
  )
}
