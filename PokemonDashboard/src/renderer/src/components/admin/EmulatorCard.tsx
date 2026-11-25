import React, { useState } from 'react'
import type { Dispatch } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { Anchor, BotMilestones, EventArgumentOptions, GameFlavor, Modality, PartySlotInfo } from '../../types/api'
import { requiredParamsFilled } from './actionSchema'
import { ActionParamFields } from './ActionParamFields'
import { MilestoneRowsPanel } from './MilestoneRowsPanel'
import type { BotPositionsAction } from './botPositions'

// Matches BotService.HowHunted's wording, so the card and the backend log read the same: a headbutt milestone is headbutted
// from its anchor whatever the range.
function anchorHunt(anchor: Anchor, modality: Modality | null): string {
  if (modality === 'Headbutt') return 'headbutt'
  if (anchor.xRange > 0) return `run left-right ±${anchor.xRange}`
  if (anchor.yRange > 0) return `run up-down ±${anchor.yRange}`
  return 'spin'
}

const PLAN_UNSAVED = 'Save or discard the plan changes first'

export function EmulatorCard({
  pos,
  name,
  game,
  milestones,
  onMilestonesChanged,
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
  game: GameFlavor
  milestones: BotMilestones | undefined
  onMilestonesChanged: () => void
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
  const { config } = useAppContext()
  const [anchorError, setAnchorError] = useState<string | null>(null)
  // The plan editor holds unsaved changes: Run would start the saved plan, and another action would drop the editor.
  const [planDirty, setPlanDirty] = useState(false)
  const selectedActionName = selected ?? availableActions[0]?.name
  const availablePhases = milestones?.milestones ?? []
  const saveAnchor = async (save: () => Promise<Response>): Promise<void> => {
    setAnchorError(null)
    try {
      const r = await save()
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      onMilestonesChanged()
    } catch (err) {
      setAnchorError(err instanceof Error ? err.message : 'Failed to save the anchor')
    }
  }
  return (
    <li
      data-testid={`emulator-card-${pos}`}
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
                  disabled={planDirty}
                  title={planDirty ? PLAN_UNSAVED : undefined}
                  className={`px-2 py-0.5 text-xs rounded transition-colors disabled:opacity-50 ${
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
            // FR-22: only the Wild Encounter form hunts from an anchor; a chain's legs hunt where the bot stands.
            // With a plan the form's milestone is not hunted, so it has no anchor line.
            const anchorMilestone = action.name === 'Wild Encounter' && !params['HuntPlan']
              ? availablePhases.find((m) => m.id === params['PhaseName'])
              : undefined
            const encounterType = params['EncounterType'] ?? ''
            const xRange = parseInt(params['xRange'], 10) || 0
            const yRange = parseInt(params['yRange'], 10) || 0
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
                      disabled={!allFilled || !!inFlight || planDirty}
                      title={planDirty ? PLAN_UNSAVED : undefined}
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
                        gameFileName={milestones?.gameFileName}
                        onMilestonesChanged={onMilestonesChanged}
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
                    onChange={(param, value) => {
                      if (param === 'PhaseName') setAnchorError(null)
                      dispatch({ type: 'paramChanged', pos, actionName: action.name, param, value })
                    }}
                    availablePhases={availablePhases}
                    gameFileName={milestones?.gameFileName}
                    onMilestonesChanged={onMilestonesChanged}
                    onPlanDirtyChange={setPlanDirty}
                    party={party}
                    onReloadParty={onReloadParty}
                    partyLoading={partyLoading}
                  />
                )}
                {anchorMilestone && (
                  <div data-testid="milestone-anchor" className="space-y-0.5">
                    <div className="flex items-center gap-2">
                      <span className="flex-1 text-xs text-slate-400">
                        {anchorMilestone.anchor
                          ? `Anchor: map ${anchorMilestone.anchor.map} (${anchorMilestone.anchor.x}, ${anchorMilestone.anchor.y}), ${anchorHunt(anchorMilestone.anchor, anchorMilestone.modality)}, ${anchorMilestone.anchorSet ? 'set on this file' : 'generated'}`
                          : 'No anchor: hunted where the bot stands'}
                      </span>
                      {/* The form's Run or Spin and range become the anchor's; Headbutt keeps no range. A Run with no range would
                          store a spin, so it waits for a range. */}
                      <button
                        type="button"
                        onClick={() => saveAnchor(() => encounterType === 'Run'
                          ? api.setAnchor(config?.apiUrl ?? '', pos, anchorMilestone.id, xRange, yRange)
                          : api.setAnchor(config?.apiUrl ?? '', pos, anchorMilestone.id, 0, 0))}
                        disabled={encounterType === '' || (encounterType === 'Run' && xRange <= 0 && yRange <= 0)}
                        className="text-xs text-slate-400 hover:text-white px-1.5 py-0.5 rounded border border-slate-600 disabled:opacity-40"
                      >
                        Set anchor here
                      </button>
                      {anchorMilestone.anchorSet && (
                        <button
                          type="button"
                          onClick={() => saveAnchor(() => api.clearAnchor(config?.apiUrl ?? '', pos, anchorMilestone.id))}
                          className="text-xs text-slate-400 hover:text-white px-1.5 py-0.5 rounded border border-slate-600"
                        >
                          Clear
                        </button>
                      )}
                    </div>
                    {/* The server still checks the form's range, then the anchor's replaces it (B2a). */}
                    {anchorMilestone.anchor && anchorMilestone.modality !== 'Headbutt' && encounterType !== 'Headbutt' && (
                      <div className="text-xs text-amber-400">
                        The anchor decides Run or Spin and the range: this hunt will {anchorHunt(anchorMilestone.anchor, anchorMilestone.modality)}, whatever the form says.
                      </div>
                    )}
                    {anchorError && <div className="text-xs text-red-400">{anchorError}</div>}
                  </div>
                )}
                {/* FR-12: only a generated milestone has rows to hide on this file. */}
                {anchorMilestone && anchorMilestone.modality !== null && milestones && (
                  <MilestoneRowsPanel game={game} gameFileName={milestones.gameFileName} milestoneId={anchorMilestone.id} />
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
