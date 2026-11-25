import React, { useEffect, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { ConditionalParameter, EventArgumentOptions, GameRomInfo, PartySlotInfo, PickerMilestone } from '../../types/api'
import { romLabel } from '../../types/api'
import { choiceOptions, encounterTypeChoices, getParamLabel, isConditionMet, pickupChoice } from './actionSchema'
import { HuntPlanEditor } from './HuntPlanEditor'

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
  games,
  gameFileName,
  onMilestonesChanged,
  onPlanDirtyChange
}: {
  schema: EventArgumentOptions
  params: Record<string, string>
  onChange: (param: string, value: string) => void
  availablePhases: PickerMilestone[]
  party: PartySlotInfo[] | undefined
  onReloadParty: () => void
  partyLoading: boolean
  // Detected ROMs, for schemas whose entries pick a game (the starter hunt's legs do). When absent,
  // GameName falls through to a plain text input like any other parameter.
  games?: GameRomInfo[]
  // The bot's game file, for archiving and labelling the picked milestone. Without it the picker has no Archive or Order.
  gameFileName?: string
  onMilestonesChanged?: () => void
  // Told whether the plan editor holds unsaved changes, so the card can grey what would drop them.
  onPlanDirtyChange?: (dirty: boolean) => void
}): JSX.Element {
  const { config } = useAppContext()
  const [milestoneText, setMilestoneText] = useState('')
  const [milestoneModality, setMilestoneModality] = useState('')
  const [incompleteOnly, setIncompleteOnly] = useState(false)
  const [showArchived, setShowArchived] = useState(false)
  const [milestoneError, setMilestoneError] = useState<string | null>(null)
  // Generated milestones are wild locations, so only an encounter form (Wild Encounter, a chain's leg) offers them;
  // Togepi, Primo and Static Encounter list custom milestones only.
  const offered = schema.requiredParameters?.includes('EncounterType')
    ? availablePhases
    : availablePhases.filter((m) => m.modality === null)
  const modalities = Array.from(new Set(offered.flatMap((m) => (m.modality ? [m.modality] : []))))
  const selectedId = params['PhaseName'] ?? ''
  const selectedMilestone = offered.find((m) => m.id === selectedId)
  const filterText = milestoneText.trim().toLowerCase()
  // The picked milestone stays listed whatever the filters say, so the select never shows one milestone while holding another.
  const shownMilestones = offered.filter((m) => m.id === selectedId || (
    (showArchived || !m.archived) &&
    (!incompleteOnly || !m.complete) &&
    (milestoneModality === '' || (milestoneModality === 'Custom' ? m.modality === null : m.modality === milestoneModality)) &&
    (filterText === '' || [m.name, m.id, m.order ?? ''].some((s) => s.toLowerCase().includes(filterText)))
  ))
  const saveMilestone = async (save: () => Promise<Response>): Promise<void> => {
    setMilestoneError(null)
    try {
      const r = await save()
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      onMilestonesChanged?.()
    } catch (err) {
      setMilestoneError(err instanceof Error ? err.message : 'Failed to save the milestone')
    }
  }
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
        <div key={param} data-testid={`param-${param}`} className="flex items-center gap-2">
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
          ) : param === 'PhaseName' && params['HuntPlan'] ? (
            // The server ignores the form's milestone when a plan is given (C3b); the value is kept for No plan.
            <span className="flex-1 text-xs text-slate-400">
              Set by the plan &quot;{params['HuntPlan']}&quot;: it starts on its default and follows the in-game clock.
            </span>
          ) : param === 'PhaseName' ? (
            <div className="flex-1 space-y-1">
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={milestoneText}
                  onChange={(e) => setMilestoneText(e.target.value)}
                  placeholder="Filter"
                  className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
                />
                {modalities.length > 0 && (
                  <select
                    value={milestoneModality}
                    onChange={(e) => setMilestoneModality(e.target.value)}
                    className="bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
                  >
                    <option value="">All</option>
                    <option value="Custom">Custom</option>
                    {modalities.map((m) => (
                      <option key={m} value={m}>{m}</option>
                    ))}
                  </select>
                )}
                <label className="flex items-center gap-1 text-xs text-slate-500">
                  <input type="checkbox" checked={incompleteOnly} onChange={(e) => setIncompleteOnly(e.target.checked)} className="w-3 h-3 accent-indigo-500" />
                  Incomplete only
                </label>
                <label className="flex items-center gap-1 text-xs text-slate-500">
                  <input type="checkbox" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} className="w-3 h-3 accent-indigo-500" />
                  Show archived
                </label>
              </div>
              {/* A "no hunt yet" milestone stays selectable, so Start shows the server's reason. */}
              <select
                value={params[param] ?? ''}
                onChange={(e) => onChange(param, e.target.value)}
                className="w-full bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
              >
                <option value="">Select a milestone...</option>
                {shownMilestones.map((m) => (
                  <option key={m.id} value={m.id}>
                    {`${m.order ? `${m.order} ` : ''}${m.name}${m.huntable ? '' : ' (no hunt yet)'}${m.archived ? ' (archived)' : ''}`}
                  </option>
                ))}
              </select>
              {gameFileName && selectedMilestone && (
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => saveMilestone(() => api.setMilestoneArchived(config?.apiUrl ?? '', gameFileName, selectedMilestone.id, !selectedMilestone.archived))}
                    className="text-xs text-slate-400 hover:text-white px-1.5 py-0.5 rounded border border-slate-600"
                  >
                    {selectedMilestone.archived ? 'Unarchive' : 'Archive'}
                  </button>
                  <input
                    key={selectedMilestone.id}
                    type="text"
                    defaultValue={selectedMilestone.order ?? ''}
                    onBlur={(e) => {
                      if (e.target.value !== (selectedMilestone.order ?? '')) {
                        void saveMilestone(() => api.setMilestoneOrder(config?.apiUrl ?? '', gameFileName, selectedMilestone.id, e.target.value))
                      }
                    }}
                    placeholder="Order"
                    className="w-20 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
                  />
                  {milestoneError && <span className="text-xs text-red-400">{milestoneError}</span>}
                </div>
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
              ) : param === 'HuntPlan' ? (
                <HuntPlanEditor
                  value={params[param] ?? ''}
                  onChange={(v) => onChange(param, v)}
                  milestones={availablePhases}
                  onDirtyChange={(dirty) => onPlanDirtyChange?.(dirty)}
                />
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
          {params['HuntPlan'] && (
            <div className="text-xs text-slate-400 mt-0.5">
              The plan picks each milestone and hunts it from its anchor. Encounter Type must suit all of them: Run or Spin for grass and caves, Headbutt for trees. The dashboard window follows the bot.
            </div>
          )}
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
