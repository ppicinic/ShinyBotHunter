import React from 'react'
import type { Dispatch } from 'react'
import type { AdminMetadata, EmulatorState, EventArgumentOptions, PartySlotInfo } from '../../types/api'
import { normalizeGameFlavor } from '../../utils/gameFlavor'
import { requiredParamsFilled } from './actionSchema'
import { ActionParamFields } from './ActionParamFields'
import type { BotPositionsAction } from './botPositions'

export function EmulatorCard({
  pos,
  name,
  emState,
  metadata,
  actions: availableActions,
  params: posParams,
  legs: posLegs,
  selected,
  party,
  partyLoading,
  inFlight,
  confirmingCancel,
  onConfirmCancel,
  onCancel,
  onRun,
  onReloadParty,
  dispatch,
  feedback
}: {
  pos: number
  name: string
  emState: EmulatorState | undefined
  metadata: AdminMetadata | null
  actions: EventArgumentOptions[]
  params: Record<string, Record<string, string>>
  legs: Record<string, Record<string, string>[]>
  selected: string | undefined
  party: PartySlotInfo[] | undefined
  partyLoading: boolean
  inFlight: 'run' | 'cancel' | undefined
  confirmingCancel: boolean
  onConfirmCancel: (pos: number | null) => void
  onCancel: () => void
  onRun: (action: EventArgumentOptions) => void
  onReloadParty: () => void
  dispatch: Dispatch<BotPositionsAction>
  feedback: JSX.Element | null
}): JSX.Element {
  const selectedActionName = selected ?? availableActions[0]?.name
  const gameName = emState ? normalizeGameFlavor(emState.gameFlavor) : ''
  const availablePhases = gameName ? (metadata?.games[gameName]?.phases ?? []) : []
  return (
    <li
      className="bg-card-border/30 rounded px-3 py-2 space-y-1.5"
    >
      <div className="flex items-center justify-between">
        <span className="text-sm text-white">
          <span className="text-slate-400 text-xs mr-2">#{pos}</span>
          {name}
        </span>
        {confirmingCancel ? (
          <span className="flex items-center gap-1.5">
            <span className="text-xs text-slate-300">Confirm?</span>
            <button
              onClick={() => onCancel()}
              disabled={!!inFlight}
              className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors font-semibold"
            >
              Yes
            </button>
            <button
              onClick={() => onConfirmCancel(null)}
              className="text-xs text-slate-400 hover:text-slate-300 transition-colors"
            >
              No
            </button>
          </span>
        ) : (
          <button
            onClick={() => onConfirmCancel(pos)}
            disabled={!!inFlight}
            className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors"
          >
            {inFlight === 'cancel' ? 'Cancelling...' : 'Cancel'}
          </button>
        )}
      </div>
      {availableActions.length > 0 && (
        <div className="space-y-1 pt-1 border-t border-slate-700/50">
          {availableActions.length > 1 && (
            <div className="flex items-center gap-1">
              {availableActions.map((action) => (
                <button
                  key={action.name}
                  onClick={() => dispatch({ type: 'actionSelected', pos, actionName: action.name })}
                  className={`px-2 py-0.5 text-xs rounded transition-colors ${
                    selectedActionName === action.name
                      ? 'bg-indigo-700 text-white'
                      : 'bg-card-border text-slate-400 hover:text-white'
                  }`}
                >
                  {action.name}
                </button>
              ))}
            </div>
          )}
          {/* Only the selected action's form renders, so its Run button is unambiguous. */}
          {availableActions.filter((action) => action.name === selectedActionName).map((action) => {
            const params = posParams[action.name] ?? {}
            const legs = posLegs[action.name] ?? []
            const allFilled = action.legParameters
              ? legs.length > 0 && legs.every((leg) => requiredParamsFilled(action.legParameters!, leg))
              : requiredParamsFilled(action, params)
            return (
              <div key={action.name} className="space-y-1">
                <div className="flex items-center justify-between">
                  <span className="text-xs text-slate-300">{action.name}</span>
                  <div className="flex items-center gap-1.5">
                    {action.legParameters && (
                      <button
                        onClick={() => dispatch({ type: 'legAdded', pos, action })}
                        className="px-2 py-0.5 bg-slate-700 hover:bg-slate-600 text-white text-xs rounded transition-colors"
                      >
                        + Leg
                      </button>
                    )}
                    <button
                      onClick={() => onRun(action)}
                      disabled={!allFilled || !!inFlight}
                      className="px-2 py-0.5 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                    >
                      {inFlight === 'run' ? 'Running...' : 'Run'}
                    </button>
                  </div>
                </div>
                {action.legParameters ? (
                  legs.map((leg, legIndex) => (
                    <div key={legIndex} className="border border-slate-700/60 rounded px-2 py-1 space-y-1">
                      <div className="flex items-center justify-between">
                        <span className="text-xs text-slate-400">Leg {legIndex + 1}</span>
                        {legs.length > 1 && (
                          <button
                            onClick={() => dispatch({ type: 'legRemoved', pos, actionName: action.name, legIndex })}
                            className="text-xs text-red-400 hover:text-red-300 transition-colors"
                          >
                            Remove
                          </button>
                        )}
                      </div>
                      <ActionParamFields
                        schema={action.legParameters!}
                        params={leg}
                        onChange={(param, value) => dispatch({ type: 'legParamChanged', pos, actionName: action.name, legIndex, param, value })}
                        availablePhases={availablePhases}
                        party={party}
                        onReloadParty={onReloadParty}
                        partyLoading={partyLoading}
                      />
                    </div>
                  ))
                ) : (
                  <ActionParamFields
                    schema={action}
                    params={params}
                    onChange={(param, value) => dispatch({ type: 'paramChanged', pos, actionName: action.name, param, value })}
                    availablePhases={availablePhases}
                    party={party}
                    onReloadParty={onReloadParty}
                    partyLoading={partyLoading}
                  />
                )}
              </div>
            )
          })}
        </div>
      )}
      {feedback}
    </li>
  )
}
