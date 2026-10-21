import React from 'react'
import type { ConditionalParameter, EventArgumentOptions, GameRomInfo, PartySlotInfo, PhaseInfo } from '../../types/api'
import { romLabel } from '../../types/api'
import { getParamLabel, isConditionMet } from './actionSchema'

const ENCOUNTER_TYPE_OPTIONS = ['Spin', 'Run']

const TRAIN_TYPE_OPTIONS = ['None', 'Standard', 'Switch']

const TARGET_TYPE_OPTIONS = ['PhaseTarget', 'ShinyTarget', 'NoTarget']

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
  games
}: {
  schema: EventArgumentOptions
  params: Record<string, string>
  onChange: (param: string, value: string) => void
  availablePhases: PhaseInfo[]
  party: PartySlotInfo[] | undefined
  onReloadParty: () => void
  partyLoading: boolean
  // Detected ROMs, for schemas whose entries pick a game (the starter hunt's legs do). When absent,
  // GameName falls through to a plain text input like any other parameter.
  games?: GameRomInfo[]
}): JSX.Element {
  const visibleConditionals = (schema.conditionalParameters ?? []).filter((cp: ConditionalParameter) => {
    return isConditionMet(cp.condition, params) || !cp.hideIfConditionNotMet
  })
  return (
    <>
      {schema.requiredParameters?.map((param) => (
        <div key={param} className="flex items-center gap-2">
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
            <select
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            >
              <option value="">Select a milestone...</option>
              {availablePhases.map((phase) => (
                <option key={phase.name} value={phase.name}>{phase.name}</option>
              ))}
            </select>
          ) : param === 'TargetType' ? (
            <select
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            >
              <option value="">Select target type...</option>
              {TARGET_TYPE_OPTIONS.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          ) : param === 'EncounterType' ? (
            <select
              value={params[param] ?? ''}
              onChange={(e) => onChange(param, e.target.value)}
              className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            >
              <option value="">Select encounter type...</option>
              {ENCOUNTER_TYPE_OPTIONS.map((t) => (
                <option key={t} value={t}>{t}</option>
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
            <div key={param} className="flex items-center gap-2 mt-0.5">
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
                  {TRAIN_TYPE_OPTIONS.map((t) => (
                    <option key={t} value={t}>{t}</option>
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
              <div key={cp.name} className="flex items-center gap-2 mt-0.5">
                <label className="text-xs text-slate-500 w-20 flex-shrink-0">{getParamLabel(cp.name)}</label>
                {isBattlerPicker ? (
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
                    placeholder="0"
                    className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-40"
                  />
                )}
                {cp.name === 'SwitchSlot' && (
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
