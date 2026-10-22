import React from 'react'
import type { EventArgumentOptions, GameRomInfo, PhaseInfo } from '../../types/api'
import { romLabel } from '../../types/api'
import { getParamLabel, initialParamsFor, legsVisible, requiredParamsFilled, visibleConditionalsOf } from './actionSchema'
import { ActionParamFields } from './ActionParamFields'

// Which hunt a mode-carrying action runs. Values are C# GameFlavor names; the label is what the
// games are actually called.
const MODE_OPTIONS: { value: string; label: string }[] = [
  { value: 'HGSS', label: 'HeartGold / SoulSilver' },
  { value: 'RS', label: 'Ruby / Sapphire' }
]

export function TopLevelActionForm({
  action,
  params,
  storedLegs,
  allGames,
  phasesFor,
  submitting,
  onParamChange,
  onSeedLegs,
  onLegChange,
  onAddLeg,
  onRemoveLeg,
  onStart,
  feedback
}: {
  action: EventArgumentOptions
  params: Record<string, string>
  storedLegs: Record<string, string>[] | undefined
  allGames: GameRomInfo[]
  phasesFor: (params: Record<string, string>, legs?: Record<string, string>[]) => PhaseInfo[]
  submitting: string | null
  onParamChange: (param: string, value: string) => void
  onSeedLegs: (legs: Record<string, string>[]) => void
  onLegChange: (index: number, param: string, value: string) => void
  onAddLeg: (current: Record<string, string>[]) => void
  onRemoveLeg: (index: number) => void
  onStart: () => void
  feedback: JSX.Element | null
}): JSX.Element {
  const hasParams = (action.requiredParameters?.length ?? 0) > 0
  // A conditional field can be required too, but only while it is on screen.
  const shownConditionals = visibleConditionalsOf(action, params)
  const flatFilled = (!hasParams || action.requiredParameters!.every((p) => params[p]?.trim()))
    && shownConditionals.every((cp) => !cp.required || params[cp.name]?.trim())
  const actionLegs = storedLegs ?? (action.legParameters ? [initialParamsFor(action.legParameters)] : [])
  // Truthiness, not a check against undefined: the server sends legParameters as null for
  // an action that has no legs, and null is not undefined. Testing for undefined gave the
  // launch actions a "+ Entry" button and then disabled their Start button, waiting on a
  // leg that could never be added.
  const showLegs = !!action.legParameters && legsVisible(action, params)
  const legsFilled = !showLegs
    || (actionLegs.length > 0 && actionLegs.every((leg) => requiredParamsFilled(action.legParameters!, leg)))
  const allParamsFilled = flatFilled && legsFilled
  const isSubmitting = submitting === action.name
  const legLabel = action.legParameters?.name ?? 'Entry'

  // One field, picked by name. Shared by the always-present parameters and the
  // conditional ones, which are ordinary fields that happen to come and go with a mode.
  const renderTopField = (param: string): JSX.Element => (
    <div key={param} className="flex items-center gap-2">
      <label className="text-xs text-slate-400 w-24 flex-shrink-0">{getParamLabel(param)}</label>
      {param === 'Mode' ? (
        <select
          value={params[param] ?? ''}
          onChange={(e) => onParamChange(param, e.target.value)}
          className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500"
        >
          <option value="">Select a game...</option>
          {MODE_OPTIONS.map((mode) => (
            <option key={mode.value} value={mode.value}>{mode.label}</option>
          ))}
        </select>
      ) : param === 'GameName' ? (
        <select
          value={params[param] ?? ''}
          onChange={(e) => onParamChange(param, e.target.value)}
          className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500"
        >
          <option value="">Select a game...</option>
          {allGames.map((game) => (
            <option key={game.handle} value={game.handle}>{romLabel(game)}</option>
          ))}
        </select>
      ) : param === 'PhaseName' ? (
        <select
          value={params[param] ?? ''}
          onChange={(e) => onParamChange(param, e.target.value)}
          disabled={!(params['GameName'] ?? actionLegs.find((leg) => leg['GameName'])?.['GameName'])}
          className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 disabled:opacity-50"
        >
          <option value="">
            {(params['GameName'] ?? actionLegs.find((leg) => leg['GameName'])?.['GameName'])
              ? 'Select a milestone...' : 'Pick a game first'}
          </option>
          {phasesFor(params, actionLegs).map((phase) => (
            <option key={phase.name} value={phase.name}>{phase.name}</option>
          ))}
        </select>
      ) : (
        <input
          type="text"
          value={params[param] ?? ''}
          onChange={(e) => onParamChange(param, e.target.value)}
          placeholder={param}
          className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
        />
      )}
    </div>
  )

  return (
    <div
      data-testid={`top-level-action-${action.name}`}
      className="bg-card-border/20 rounded-lg px-3 py-2.5 space-y-2"
    >
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-white">{action.name}</span>
        <div className="flex items-center gap-1.5">
          {showLegs && (
            <button
              onClick={() => onAddLeg(actionLegs)}
              className="px-2 py-1 bg-slate-700 hover:bg-slate-600 text-white text-xs rounded transition-colors"
            >
              + {legLabel}
            </button>
          )}
          <button
            onClick={() => onStart()}
            disabled={submitting !== null || !allParamsFilled}
            className="px-3 py-1 bg-green-700 hover:bg-green-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
          >
            {isSubmitting ? 'Starting...' : 'Start'}
          </button>
        </div>
      </div>
      {feedback}

      {showLegs && actionLegs.map((leg, legIndex) => (
        <div key={legIndex} className="border border-slate-700/60 rounded px-2 py-1.5 space-y-1">
          <div className="flex items-center justify-between">
            <span className="text-xs text-slate-400">{legLabel} {legIndex + 1}</span>
            {actionLegs.length > 1 && (
              <button
                onClick={() => onRemoveLeg(legIndex)}
                className="text-xs text-red-400 hover:text-red-300 transition-colors"
              >
                Remove
              </button>
            )}
          </div>
          <ActionParamFields
            schema={action.legParameters!}
            params={leg}
            onChange={(param, value) => {
              if (!storedLegs) onSeedLegs(actionLegs)
              onLegChange(legIndex, param, value)
            }}
            availablePhases={[]}
            party={undefined}
            onReloadParty={() => undefined}
            partyLoading={false}
            games={allGames}
          />
        </div>
      ))}

      {action.requiredParameters?.map((param) => renderTopField(param))}
      {shownConditionals.map((cp) => renderTopField(cp.name))}
      {action.optionalParameters && action.optionalParameters.length > 0 && (
        <div className="pt-1 border-t border-slate-700/30">
          <span className="text-xs text-slate-600">Optional</span>
          {action.optionalParameters.map((param) => (
            <div key={param} className="flex items-center gap-2 mt-1">
              <label className="text-xs text-slate-400 w-24 flex-shrink-0">{getParamLabel(param)}</label>
              {param.toLowerCase() === 'soundon' ? (
                <input
                  type="checkbox"
                  checked={params[param] === 'true'}
                  onChange={(e) => onParamChange(param, e.target.checked ? 'true' : 'false')}
                  className="w-4 h-4 accent-indigo-500"
                />
              ) : (
                <input
                  type="text"
                  value={params[param] ?? ''}
                  onChange={(e) => onParamChange(param, e.target.value)}
                  placeholder={param}
                  className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
                />
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
