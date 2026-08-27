import React, { useCallback, useEffect, useRef, useState } from 'react'
import { useAppContext } from '../context/AppContext'
import type { AdminMetadata, BotDashboardState, ConditionalParameter, EmulatorState, EventArgumentOptions, GameRomInfo, NewEventLegArguments, PartySlotInfo, PhaseInfo, PhasePokemonInfo } from '../types/api'
import { romLabel } from '../types/api'
import { normalizeGameFlavor } from '../utils/gameFlavor'
import { ConnectionStatus } from './ConnectionStatus'
import { DevModeTab } from './DevModeTab'
import { WindowLayoutTab } from './WindowLayoutTab'

type TabId = 'config' | 'botControl' | 'phaseEditor' | 'windows' | 'macroRecord'

const BASE_TABS: { id: TabId; label: string }[] = [
  { id: 'config', label: 'Dashboard Config' },
  { id: 'botControl', label: 'Bot Control' },
  { id: 'phaseEditor', label: 'Milestone Editor' },
  { id: 'windows', label: 'Window Layout' }
]

// Only shown when the backend reports EnableMacroRecording. The endpoints 404 regardless, so this
// is presentation only.
const DEV_MODE_TAB: { id: TabId; label: string } = { id: 'macroRecord', label: 'Dev Mode' }

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
}
const getParamLabel = (param: string): string => PARAM_DISPLAY_NAMES[param] ?? param

const ENCOUNTER_TYPE_OPTIONS = ['Spin', 'Run']
// Maps EncounterType string names to C# enum integer values (Spin=0, Run=1)
const ENCOUNTER_TYPE_VALUES: Record<string, number> = { Spin: 0, Run: 1 }

const TRAIN_TYPE_OPTIONS = ['None', 'Standard', 'Switch']
// Maps TrainType string names to C# enum integer values (None=0, Standard=1, Switch=2)
const TRAIN_TYPE_VALUES: Record<string, number> = { None: 0, Standard: 1, Switch: 2 }

const TARGET_TYPE_OPTIONS = ['PhaseTarget', 'ShinyTarget', 'NoTarget']

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
function isConditionMet(condition: { conditionType: string | number; name: string; value: string }, params: Record<string, string>): boolean {
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
function initialParamsFor(schema: EventArgumentOptions): Record<string, string> {
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
function requiredParamsFilled(schema: EventArgumentOptions, params: Record<string, string>): boolean {
  const required = schema.requiredParameters ?? []
  return required.every((p) => params[p]?.trim())
}

/** Maps one form's parameter values onto the JSON body of an encounter leg. */
/**
 * Parameter names buildLegBody knows how to place. Anything outside this set is passed through
 * verbatim, so an action with a new parameter reaches the server without a dashboard change --
 * previously such a parameter rendered an input and was then silently dropped on submit.
 */
const LEG_PARAM_KEYS = new Set([
  'PhaseName', 'TargetType', 'EncounterType', 'xRange', 'yRange', 'TrainType', 'TargetLevel', 'SwitchSlot'
])

function buildLegBody(params: Record<string, string>): NewEventLegArguments {
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
  for (const [key, value] of Object.entries(params)) {
    if (LEG_PARAM_KEYS.has(key)) continue
    if (value === '') continue
    ;(leg as Record<string, unknown>)[key] = value
  }
  return leg
}

/**
 * The inputs for one set of encounter parameters, driven entirely by the schema the server sent.
 * Rendered once for a single action and once per leg of a chained action.
 */
function ActionParamFields({
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
              {param.toLowerCase() === 'soundon' ? (
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

// ============================================================
// Dashboard Config Tab
// ============================================================

function DashboardConfigTab(): JSX.Element {
  const { dashboardConfig, config } = useAppContext()
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  const [selectedGame, setSelectedGame] = useState<string>('')
  const [selectedPhase, setSelectedPhase] = useState<string>('')
  const [selectedGameName, setSelectedGameName] = useState<string>('')
  const [allGames, setAllGames] = useState<GameRomInfo[]>([])
  const [recentCount, setRecentCount] = useState<number>(dashboardConfig.recentEncounterCount)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const apiUrl = config?.apiUrl ?? ''

  // Load metadata (available games + phases) on mount
  useEffect(() => {
    if (!apiUrl) return
    fetch(`${apiUrl}/api/metadata`)
      .then((r) => r.json())
      .then((data: AdminMetadata) => {
        setMetadata(data)
        const firstGame = Object.keys(data.games ?? {})[0] ?? ''
        setSelectedGame(firstGame)
        setSelectedPhase(data.games[firstGame]?.phases[0]?.name ?? '')
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load metadata'))

    fetch(`${apiUrl}/api/getAllGames`)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: GameRomInfo[]) => setAllGames(data))
      .catch((err) => console.error('Failed to load games:', err))
  }, [apiUrl])

  // Keep recentCount in sync when dashboardConfig updates from SignalR
  useEffect(() => {
    setRecentCount(dashboardConfig.recentEncounterCount)
  }, [dashboardConfig.recentEncounterCount])

  // Update phase dropdown when game changes
  const handleGameChange = (game: string): void => {
    setSelectedGame(game)
    setSelectedPhase(metadata?.games[game]?.phases[0]?.name ?? '')
  }

  const showFeedback = (msg: string, isError = false): void => {
    if (isError) {
      setError(msg)
      setSuccess(null)
    } else {
      setSuccess(msg)
      setError(null)
      setTimeout(() => setSuccess(null), 3000)
    }
  }

  const handleAddSubscription = async (): Promise<void> => {
    if (!selectedGame || !selectedPhase) return
    setLoading(true)
    try {
      const res = await fetch(`${apiUrl}/api/subcribeGamePhase`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ game: selectedGame, phase: selectedPhase, gameName: selectedGameName || null })
      })
      if (!res.ok) throw new Error(`Server returned ${res.status}`)
      showFeedback(`Subscribed to ${selectedGame} / ${selectedPhase}${selectedGameName ? ` (${selectedGameName})` : ''}`)
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Unknown error', true)
    } finally {
      setLoading(false)
    }
  }

  const handleRemoveSubscription = async (game: string, phase: string, gameName?: string | null): Promise<void> => {
    setLoading(true)
    try {
      const res = await fetch(`${apiUrl}/api/unsubcribeGamePhase`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ game, phase, gameName: gameName ?? null })
      })
      if (!res.ok) throw new Error(`Server returned ${res.status}`)
      showFeedback(`Unsubscribed from ${game} / ${phase}`)
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Unknown error', true)
    } finally {
      setLoading(false)
    }
  }

  const handleUpdateRecentCount = async (): Promise<void> => {
    setLoading(true)
    try {
      const res = await fetch(`${apiUrl}/api/updateRecentEncounterConfig`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(recentCount)
      })
      if (!res.ok) throw new Error(`Server returned ${res.status}`)
      showFeedback(`Recent encounter count updated to ${recentCount}`)
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Unknown error', true)
    } finally {
      setLoading(false)
    }
  }

  const availableGames = Object.keys(metadata?.games ?? {})
  const availablePhases = selectedGame ? (metadata?.games[selectedGame]?.phases ?? []) : []

  return (
    <div className="p-3 space-y-4">
      {/* Active subscriptions */}
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Active Subscriptions
        </h3>
        {dashboardConfig.gamePhases.length === 0 ? (
          <p className="text-slate-500 text-sm">No active subscriptions.</p>
        ) : (
          <ul className="space-y-1">
            {dashboardConfig.gamePhases.map(({ game, phase, gameName }) => (
              <li
                key={`${game}_${phase}_${gameName ?? ''}`}
                className="flex items-center justify-between bg-card-border/30 rounded px-3 py-1.5"
              >
                <span className="text-sm text-white">
                  <span className="text-slate-400 text-xs mr-2">{game}</span>
                  {phase}
                  {gameName && (
                    <span className="text-slate-400 text-xs ml-2">({gameName})</span>
                  )}
                </span>
                <button
                  onClick={() => handleRemoveSubscription(game, phase, gameName)}
                  disabled={loading}
                  className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors"
                >
                  Remove
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {/* Add subscription */}
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Add Subscription
        </h3>
        <div className="flex gap-2 flex-wrap">
          <select
            value={selectedGame}
            onChange={(e) => handleGameChange(e.target.value)}
            className="bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
          >
            {availableGames.length === 0 && <option value="">Loading...</option>}
            {availableGames.map((g) => (
              <option key={g} value={g}>{g}</option>
            ))}
          </select>
          <select
            value={selectedPhase}
            onChange={(e) => setSelectedPhase(e.target.value)}
            className="flex-1 bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
          >
            {availablePhases.length === 0 && <option value="">No milestones</option>}
            {availablePhases.map((p) => (
              <option key={p.name} value={p.name}>{p.name}</option>
            ))}
          </select>
          <select
            value={selectedGameName}
            onChange={(e) => setSelectedGameName(e.target.value)}
            className="bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
          >
            <option value="">No game file</option>
            {allGames.map((g) => (
              <option key={g.handle} value={g.handle}>{romLabel(g)}</option>
            ))}
          </select>
          <button
            onClick={handleAddSubscription}
            disabled={loading || !selectedGame || !selectedPhase}
            className="px-3 py-1.5 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors"
          >
            Add
          </button>
        </div>
      </div>

      {/* Recent encounter count */}
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Recent Encounter Count
        </h3>
        <div className="flex gap-2 items-center">
          <input
            type="number"
            min={1}
            max={100}
            value={recentCount}
            onChange={(e) => setRecentCount(Number(e.target.value))}
            className="w-20 bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
          />
          <button
            onClick={handleUpdateRecentCount}
            disabled={loading}
            className="px-3 py-1.5 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors"
          >
            Update
          </button>
          <span className="text-xs text-slate-500">
            Current: {dashboardConfig.recentEncounterCount}
          </span>
        </div>
      </div>

      {/* Feedback */}
      {error && (
        <div className="text-xs text-red-400 bg-red-900/20 border border-red-700/30 rounded px-3 py-2">
          {error}
        </div>
      )}
      {success && (
        <div className="text-xs text-green-400 bg-green-900/20 border border-green-700/30 rounded px-3 py-2">
          {success}
        </div>
      )}

      <p className="text-xs text-slate-500">
        Changes are broadcast to all connected dashboards via SignalR.
      </p>
    </div>
  )
}

// ============================================================
// Bot Control Tab
// ============================================================

// EventState enum values matching C# EventState in IBotEvent.cs
const ACTIONABLE_STATES = new Set([0, 3, 4]) // NotCreated=0, Waiting=3, Complete=4

function BotControlTab({ onRefresh }: { onRefresh: () => void }): JSX.Element {
  const { config, signalR } = useAppContext()
  const [actions, setActions] = useState<EventArgumentOptions[]>([])
  const [loadingActions, setLoadingActions] = useState(true)
  const [actionsError, setActionsError] = useState<string | null>(null)
  const [botState, setBotState] = useState<BotDashboardState>({})
  const [loadingState, setLoadingState] = useState(true)
  // Per-action parameter values: actionName -> { paramName -> value }
  const [paramValues, setParamValues] = useState<Record<string, Record<string, string>>>({})
  // Entries for top-level actions that take a list (the starter hunt's queue): actionName -> one
  // param record per entry, mirroring how per-bot chained legs are held.
  const [topLegs, setTopLegs] = useState<Record<string, Record<string, string>[]>>({})

  const handleTopLegChange = (actionName: string, index: number, param: string, value: string): void => {
    setTopLegs((prev) => {
      const legs = [...(prev[actionName] ?? [])]
      legs[index] = { ...legs[index], [param]: value }
      return { ...prev, [actionName]: legs }
    })
  }

  const handleTopAddLeg = (action: EventArgumentOptions, current: Record<string, string>[]): void => {
    if (!action.legParameters) return
    setTopLegs((prev) => ({
      ...prev,
      [action.name]: [...(prev[action.name] ?? current), initialParamsFor(action.legParameters!)]
    }))
  }

  const handleTopRemoveLeg = (actionName: string, index: number): void => {
    setTopLegs((prev) => ({
      ...prev,
      [actionName]: (prev[actionName] ?? []).filter((_, i) => i !== index)
    }))
  }
  const [submitting, setSubmitting] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState<number | null>(null)
  const [confirmCancelPos, setConfirmCancelPos] = useState<number | null>(null)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)
  const [allGames, setAllGames] = useState<GameRomInfo[]>([])
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)

  /**
   * Milestones belonging to whichever ROM the action has selected. Top-level actions pick a game
   * before a milestone, so the list follows from that choice rather than being global -- offering
   * an HGSS milestone for a Ruby hunt would file encounters under a phase nothing reads.
   */
  const phasesForSelectedGame = (params: Record<string, string>, legs?: Record<string, string>[]): PhaseInfo[] => {
    const handle = params['GameName'] ?? legs?.find((leg) => leg['GameName'])?.['GameName']
    const rom = allGames.find((g) => g.handle === handle)
    return rom?.flavor ? (metadata?.games[rom.flavor]?.phases ?? []) : []
  }
  // Per-bot available actions: pos -> EventArgumentOptions[]
  const [botActions, setBotActions] = useState<Record<number, EventArgumentOptions[]>>({})
  // Per-bot action parameter values: pos -> { actionName -> { paramName -> value } }
  const [botActionParams, setBotActionParams] = useState<Record<number, Record<string, Record<string, string>>>>({})
  // Legs of chained actions: pos -> { actionName -> one param record per leg }
  const [botChainLegs, setBotChainLegs] = useState<Record<number, Record<string, Record<string, string>[]>>>({})
  // Which action's form is showing per bot: pos -> actionName
  const [selectedBotAction, setSelectedBotAction] = useState<Record<number, string>>({})
  // Live party per bot, loaded on demand so Switch train mode can offer real pokemon: pos -> slots
  const [partyByPos, setPartyByPos] = useState<Record<number, PartySlotInfo[]>>({})
  const [loadingParty, setLoadingParty] = useState<number | null>(null)

  const apiUrl = config?.apiUrl ?? ''

  const loadParty = useCallback(async (pos: number): Promise<void> => {
    if (!apiUrl) return
    setLoadingParty(pos)
    try {
      const r = await fetch(`${apiUrl}/api/bot/getParty/${pos}`)
      if (!r.ok) throw new Error(`Server returned ${r.status}`)
      const party = await r.json() as PartySlotInfo[]
      setPartyByPos((prev) => ({ ...prev, [pos]: party }))
    } catch (err) {
      console.error(`Failed to load party for pos ${pos}:`, err)
      setPartyByPos((prev) => ({ ...prev, [pos]: [] }))
    } finally {
      setLoadingParty(null)
    }
  }, [apiUrl])

  const fetchBotActionsForState = useCallback(async (state: BotDashboardState): Promise<void> => {
    if (!apiUrl) return
    const entries = Object.entries(state)
    const updates: Record<number, EventArgumentOptions[]> = {}
    await Promise.all(
      entries.map(async ([posStr, emulatorState]) => {
        const pos = Number(posStr)
        const es = emulatorState as EmulatorState
        if (ACTIONABLE_STATES.has(es.eventState)) {
          try {
            const r = await fetch(`${apiUrl}/api/bot/getBotActions/${pos}`)
            if (r.ok) {
              updates[pos] = await r.json() as EventArgumentOptions[]
            }
          } catch (err) {
            console.error(`Failed to fetch bot actions for pos ${pos}:`, err)
          }
        } else {
          updates[pos] = []
        }
      })
    )
    setBotActions(updates)
    // Initialize param values for any new actions (required + optional)
    setBotActionParams((prev) => {
      const next = { ...prev }
      for (const [posStr, actionList] of Object.entries(updates)) {
        const pos = Number(posStr)
        if (!next[pos]) next[pos] = {}
        actionList.forEach((action) => {
          if (!next[pos][action.name]) {
            next[pos][action.name] = initialParamsFor(action)
          }
        })
      }
      return next
    })
    // Chained actions start with a single leg; the operator adds the rest.
    setBotChainLegs((prev) => {
      const next = { ...prev }
      for (const [posStr, actionList] of Object.entries(updates)) {
        const pos = Number(posStr)
        if (!next[pos]) next[pos] = {}
        actionList.forEach((action) => {
          if (action.legParameters && !next[pos][action.name]) {
            next[pos][action.name] = [initialParamsFor(action.legParameters)]
          }
        })
      }
      return next
    })
    // Only one action's form shows at a time, so fall back to the first whenever the current pick
    // is no longer on offer (the available set changes as the bot's state does).
    setSelectedBotAction((prev) => {
      const next = { ...prev }
      for (const [posStr, actionList] of Object.entries(updates)) {
        const pos = Number(posStr)
        if (actionList.length === 0) {
          delete next[pos]
        } else if (!actionList.some((action) => action.name === next[pos])) {
          next[pos] = actionList[0].name
        }
      }
      return next
    })
  }, [apiUrl])

  const loadDashboardState = useCallback(async (): Promise<void> => {
    if (!apiUrl) return
    setLoadingState(true)
    try {
      const r = await fetch(`${apiUrl}/api/bot/getDashboardState`)
      if (!r.ok) throw new Error(`Server returned ${r.status}`)
      const state = await r.json() as BotDashboardState
      setBotState(state)
      await fetchBotActionsForState(state)
    } catch (err) {
      console.error('Failed to load bot dashboard state:', err)
    } finally {
      setLoadingState(false)
    }
  }, [apiUrl, fetchBotActionsForState])

  // Load available actions and initial dashboard state on mount
  useEffect(() => {
    if (!apiUrl) return

    setLoadingActions(true)
    fetch(`${apiUrl}/api/getTopLevelActions`)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: EventArgumentOptions[]) => {
        setActions(data)
        const initial: Record<string, Record<string, string>> = {}
        data.forEach((action) => {
          initial[action.name] = initialParamsFor(action)
        })
        setParamValues(initial)
      })
      .catch((err) => setActionsError(err instanceof Error ? err.message : 'Failed to load actions'))
      .finally(() => setLoadingActions(false))

    fetch(`${apiUrl}/api/getAllGames`)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: GameRomInfo[]) => setAllGames(data))
      .catch((err) => console.error('Failed to load games:', err))

    fetch(`${apiUrl}/api/metadata`)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: AdminMetadata) => setMetadata(data))
      .catch((err) => console.error('Failed to load metadata:', err))

    loadDashboardState()
  }, [apiUrl, loadDashboardState])

  // Subscribe to SignalR ReceiveDashboardState push updates
  useEffect(() => {
    if (!signalR) return
    const unsub = signalR.onDashboardState(async (state: BotDashboardState) => {
      setBotState(state)
      setLoadingState(false)
      await fetchBotActionsForState(state)
    })
    return unsub
  }, [signalR, fetchBotActionsForState])

  // Positions whose encounter form -- single or any leg of a chain -- has Switch train mode picked.
  const switchPositions = new Set<string>()
  Object.entries(botActionParams).forEach(([posStr, actionsForPos]) => {
    if (Object.values(actionsForPos).some((p) => p['TrainType'] === 'Switch')) switchPositions.add(posStr)
  })
  Object.entries(botChainLegs).forEach(([posStr, actionsForPos]) => {
    if (Object.values(actionsForPos).some((legs) => legs.some((p) => p['TrainType'] === 'Switch'))) switchPositions.add(posStr)
  })
  const switchPositionsKey = Array.from(switchPositions).sort().join(',')
  const loadedSwitchPositions = useRef<Set<string>>(new Set())

  // Pull the live party whenever Switch train mode is newly selected, so the battler dropdown is
  // populated with the pokemon actually in the game right now.
  useEffect(() => {
    if (!apiUrl) return
    const current = new Set(switchPositionsKey ? switchPositionsKey.split(',') : [])
    current.forEach((posStr) => {
      if (!loadedSwitchPositions.current.has(posStr)) void loadParty(Number(posStr))
    })
    loadedSwitchPositions.current = current
  }, [switchPositionsKey, apiUrl, loadParty])

  // Expose refresh to parent
  useEffect(() => {
    onRefresh // just reference it so the parent can call loadDashboardState
  }, [onRefresh])

  const handleRunBotAction = async (pos: number, action: EventArgumentOptions): Promise<void> => {
    setFeedback(null)
    try {
      const params = botActionParams[pos]?.[action.name] ?? {}
      let body: Record<string, unknown>
      if (action.legParameters) {
        body = { eventName: action.name, legs: (botChainLegs[pos]?.[action.name] ?? []).map(buildLegBody) }
      } else {
        body = { eventName: action.name, ...buildLegBody(params) }
        // Find soundOn key case-insensitively
        const soundOnKey = Object.keys(params).find((k) => k.toLowerCase() === 'soundon')
        if (soundOnKey !== undefined) {
          body['soundOn'] = params[soundOnKey] === 'true'
        }
        // Pass through parameters buildLegBody does not model. Server-side binding is
        // case-insensitive, so the schema's own name is sent unchanged.
        for (const [key, value] of Object.entries(params)) {
          if (LEG_PARAM_KEYS.has(key)) continue
          if (key.toLowerCase() === 'soundon') continue
          if (value === '') continue
          body[key] = value
        }
      }

      const res = await fetch(`${apiUrl}/api/bot/startAction/${pos}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      setBotState(newState)
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Started "${action.name}" on bot #${pos}`, isError: false })
      setTimeout(() => setFeedback(null), 4000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true })
    }
  }

  const handleParamChange = (actionName: string, param: string, value: string): void => {
    setParamValues((prev) => ({
      ...prev,
      [actionName]: { ...prev[actionName], [param]: value }
    }))
  }

  const handleStartAction = async (action: EventArgumentOptions): Promise<void> => {
    setSubmitting(action.name)
    setFeedback(null)
    try {
      const params = paramValues[action.name] ?? {}
      const body: Record<string, unknown> = { eventName: action.name }
      if (action.legParameters) {
        const legs = topLegs[action.name] ?? [initialParamsFor(action.legParameters)]
        body['legs'] = legs.map(buildLegBody)
      }
      if (params['GameName'] !== undefined) {
        body['gameName'] = params['GameName']
      }
      // Find soundOn key case-insensitively
      const soundOnKey = Object.keys(params).find((k) => k.toLowerCase() === 'soundon')
      if (soundOnKey !== undefined) {
        body['soundOn'] = params[soundOnKey] === 'true'
      }
      // Everything else goes under its own name, which server-side binding matches
      // case-insensitively. Without this an action's parameters render, get filled in, and are
      // then dropped on submit -- the form looks configurable while the server sees nothing.
      for (const [key, value] of Object.entries(params)) {
        if (key === 'GameName') continue
        if (key.toLowerCase() === 'soundon') continue
        if (value === '') continue
        // Numeric parameters are sent as numbers rather than relying on the server coercing them.
        const asNumber = Number(value)
        body[key] = value.trim() !== '' && !Number.isNaN(asNumber) && /^\d+$/.test(value.trim())
          ? asNumber
          : value
      }

      const res = await fetch(`${apiUrl}/api/startTopLevelAction`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      setBotState(newState)
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Started "${action.name}"`, isError: false })
      setTimeout(() => setFeedback(null), 4000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true })
    } finally {
      setSubmitting(null)
    }
  }

  const handleCancelAction = async (pos: number): Promise<void> => {
    setConfirmCancelPos(null)
    setCancelling(pos)
    setFeedback(null)
    try {
      const res = await fetch(`${apiUrl}/api/bot/cancelAction`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(pos)
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      setBotState(newState)
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Cancelled position ${pos}`, isError: false })
      setTimeout(() => setFeedback(null), 3000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true })
    } finally {
      setCancelling(null)
    }
  }

  const activePositions = Object.entries(botState).map(([pos, state]) => ({
    pos: Number(pos),
    name: (state as EmulatorState).name,
    eventState: (state as EmulatorState).eventState
  }))

  const handleBotParamChange = (pos: number, actionName: string, param: string, value: string): void => {
    setBotActionParams((prev) => ({
      ...prev,
      [pos]: {
        ...prev[pos],
        [actionName]: { ...(prev[pos]?.[actionName] ?? {}), [param]: value }
      }
    }))
  }

  const handleLegParamChange = (pos: number, actionName: string, legIndex: number, param: string, value: string): void => {
    setBotChainLegs((prev) => {
      const legs = (prev[pos]?.[actionName] ?? []).map((leg, i) => (i === legIndex ? { ...leg, [param]: value } : leg))
      return { ...prev, [pos]: { ...prev[pos], [actionName]: legs } }
    })
  }

  const handleAddLeg = (pos: number, action: EventArgumentOptions): void => {
    if (!action.legParameters) return
    setBotChainLegs((prev) => {
      const legs = [...(prev[pos]?.[action.name] ?? []), initialParamsFor(action.legParameters!)]
      return { ...prev, [pos]: { ...prev[pos], [action.name]: legs } }
    })
  }

  const handleRemoveLeg = (pos: number, actionName: string, legIndex: number): void => {
    setBotChainLegs((prev) => {
      const legs = (prev[pos]?.[actionName] ?? []).filter((_, i) => i !== legIndex)
      // A chain always keeps at least one leg -- an empty one is just the single-encounter form.
      if (legs.length === 0) return prev
      return { ...prev, [pos]: { ...prev[pos], [actionName]: legs } }
    })
  }

  return (
    <div className="p-3 space-y-4">
      {/* Active Emulators */}
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Active Emulators
        </h3>
        {loadingState ? (
          <p className="text-slate-500 text-sm">Loading...</p>
        ) : activePositions.length === 0 ? (
          <p className="text-slate-500 text-sm">No active emulators.</p>
        ) : (
          <ul className="space-y-2">
            {activePositions.map(({ pos, name }) => {
              const availableActions = botActions[pos] ?? []
              const posParams = botActionParams[pos] ?? {}
              const posLegs = botChainLegs[pos] ?? {}
              const selectedActionName = selectedBotAction[pos] ?? availableActions[0]?.name
              const emState = botState[pos] as EmulatorState | undefined
              const gameName = emState ? normalizeGameFlavor(emState.gameFlavor) : ''
              const availablePhases = gameName ? (metadata?.games[gameName]?.phases ?? []) : []
              return (
                <li
                  key={pos}
                  className="bg-card-border/30 rounded px-3 py-2 space-y-1.5"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm text-white">
                      <span className="text-slate-400 text-xs mr-2">#{pos}</span>
                      {name}
                    </span>
                    {confirmCancelPos === pos ? (
                      <span className="flex items-center gap-1.5">
                        <span className="text-xs text-slate-300">Confirm?</span>
                        <button
                          onClick={() => handleCancelAction(pos)}
                          disabled={cancelling === pos}
                          className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors font-semibold"
                        >
                          Yes
                        </button>
                        <button
                          onClick={() => setConfirmCancelPos(null)}
                          className="text-xs text-slate-400 hover:text-slate-300 transition-colors"
                        >
                          No
                        </button>
                      </span>
                    ) : (
                      <button
                        onClick={() => setConfirmCancelPos(pos)}
                        disabled={cancelling === pos}
                        className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors"
                      >
                        {cancelling === pos ? 'Cancelling...' : 'Cancel'}
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
                              onClick={() => setSelectedBotAction((prev) => ({ ...prev, [pos]: action.name }))}
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
                                    onClick={() => handleAddLeg(pos, action)}
                                    className="px-2 py-0.5 bg-slate-700 hover:bg-slate-600 text-white text-xs rounded transition-colors"
                                  >
                                    + Leg
                                  </button>
                                )}
                                <button
                                  onClick={() => handleRunBotAction(pos, action)}
                                  disabled={!allFilled}
                                  className="px-2 py-0.5 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                                >
                                  Run
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
                                        onClick={() => handleRemoveLeg(pos, action.name, legIndex)}
                                        className="text-xs text-red-400 hover:text-red-300 transition-colors"
                                      >
                                        Remove
                                      </button>
                                    )}
                                  </div>
                                  <ActionParamFields
                                    schema={action.legParameters!}
                                    params={leg}
                                    onChange={(param, value) => handleLegParamChange(pos, action.name, legIndex, param, value)}
                                    availablePhases={availablePhases}
                                    party={partyByPos[pos]}
                                    onReloadParty={() => loadParty(pos)}
                                    partyLoading={loadingParty === pos}
                                  />
                                </div>
                              ))
                            ) : (
                              <ActionParamFields
                                schema={action}
                                params={params}
                                onChange={(param, value) => handleBotParamChange(pos, action.name, param, value)}
                                availablePhases={availablePhases}
                                party={partyByPos[pos]}
                                onReloadParty={() => loadParty(pos)}
                                partyLoading={loadingParty === pos}
                              />
                            )}
                          </div>
                        )
                      })}
                    </div>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </div>

      {/* Available Actions */}
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Start Action
        </h3>

        {loadingActions ? (
          <p className="text-slate-500 text-sm">Loading actions...</p>
        ) : actionsError ? (
          <div className="text-xs text-red-400 bg-red-900/20 border border-red-700/30 rounded px-3 py-2">
            {actionsError}
          </div>
        ) : actions.length === 0 ? (
          <p className="text-slate-500 text-sm">No actions available.</p>
        ) : (
          <div className="space-y-2">
            {actions.map((action) => {
              const params = paramValues[action.name] ?? {}
              const hasParams = (action.requiredParameters?.length ?? 0) > 0
              const flatFilled = !hasParams || action.requiredParameters!.every((p) => params[p]?.trim())
              const actionLegs = topLegs[action.name] ?? (action.legParameters ? [initialParamsFor(action.legParameters)] : [])
              const legsFilled = !action.legParameters
                || (actionLegs.length > 0 && actionLegs.every((leg) => requiredParamsFilled(action.legParameters!, leg)))
              const allParamsFilled = flatFilled && legsFilled
              const isSubmitting = submitting === action.name
              const legLabel = action.legParameters?.name ?? 'Entry'

              return (
                <div
                  key={action.name}
                  className="bg-card-border/20 rounded-lg px-3 py-2.5 space-y-2"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-medium text-white">{action.name}</span>
                    <div className="flex items-center gap-1.5">
                      {action.legParameters && (
                        <button
                          onClick={() => handleTopAddLeg(action, actionLegs)}
                          className="px-2 py-1 bg-slate-700 hover:bg-slate-600 text-white text-xs rounded transition-colors"
                        >
                          + {legLabel}
                        </button>
                      )}
                      <button
                        onClick={() => handleStartAction(action)}
                        disabled={isSubmitting || !allParamsFilled}
                        className="px-3 py-1 bg-green-700 hover:bg-green-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                      >
                        {isSubmitting ? 'Starting...' : 'Start'}
                      </button>
                    </div>
                  </div>

                  {action.legParameters && actionLegs.map((leg, legIndex) => (
                    <div key={legIndex} className="border border-slate-700/60 rounded px-2 py-1.5 space-y-1">
                      <div className="flex items-center justify-between">
                        <span className="text-xs text-slate-400">{legLabel} {legIndex + 1}</span>
                        {actionLegs.length > 1 && (
                          <button
                            onClick={() => handleTopRemoveLeg(action.name, legIndex)}
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
                          if (!topLegs[action.name]) setTopLegs((prev) => ({ ...prev, [action.name]: actionLegs }))
                          handleTopLegChange(action.name, legIndex, param, value)
                        }}
                        availablePhases={[]}
                        party={undefined}
                        onReloadParty={() => undefined}
                        partyLoading={false}
                        games={allGames}
                      />
                    </div>
                  ))}

                  {action.requiredParameters?.map((param) => (
                    <div key={param} className="flex items-center gap-2">
                      <label className="text-xs text-slate-400 w-24 flex-shrink-0">{getParamLabel(param)}</label>
                      {param === 'GameName' ? (
                        <select
                          value={params[param] ?? ''}
                          onChange={(e) => handleParamChange(action.name, param, e.target.value)}
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
                          onChange={(e) => handleParamChange(action.name, param, e.target.value)}
                          disabled={!(params['GameName'] ?? actionLegs.find((leg) => leg['GameName'])?.['GameName'])}
                          className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 disabled:opacity-50"
                        >
                          <option value="">
                            {(params['GameName'] ?? actionLegs.find((leg) => leg['GameName'])?.['GameName'])
                              ? 'Select a milestone...' : 'Pick a game first'}
                          </option>
                          {phasesForSelectedGame(params, actionLegs).map((phase) => (
                            <option key={phase.name} value={phase.name}>{phase.name}</option>
                          ))}
                        </select>
                      ) : (
                        <input
                          type="text"
                          value={params[param] ?? ''}
                          onChange={(e) => handleParamChange(action.name, param, e.target.value)}
                          placeholder={param}
                          className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
                        />
                      )}
                    </div>
                  ))}
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
                              onChange={(e) => handleParamChange(action.name, param, e.target.checked ? 'true' : 'false')}
                              className="w-4 h-4 accent-indigo-500"
                            />
                          ) : (
                            <input
                              type="text"
                              value={params[param] ?? ''}
                              onChange={(e) => handleParamChange(action.name, param, e.target.value)}
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
            })}
          </div>
        )}
      </div>

      {/* Feedback */}
      {feedback && (
        <div
          className={`text-xs rounded px-3 py-2 ${
            feedback.isError
              ? 'text-red-400 bg-red-900/20 border border-red-700/30'
              : 'text-green-400 bg-green-900/20 border border-green-700/30'
          }`}
        >
          {feedback.msg}
        </div>
      )}
    </div>
  )
}

// ============================================================
// Phase Editor Tab
// ============================================================

const EMPTY_POKEMON = (): PhasePokemonInfo => ({
  name: '',
  speciesId: 0,
  amount: 1,
  formCode: '',
  hasFemaleForm: false,
  requireSpecificForm: false
})

const EMPTY_PHASE = (gameName: string, phaseName: string): PhaseInfo => ({
  gameName,
  name: phaseName,
  pokemon: [EMPTY_POKEMON()],
  isStatic: false
})

function PhaseEditorTab(): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  // ── selection state ──────────────────────────────────────────
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  const [selectedGame, setSelectedGame] = useState<string>('')
  const [selectedPhase, setSelectedPhase] = useState<string>('')
  const [isNewPhase, setIsNewPhase] = useState(false)
  const [newPhaseName, setNewPhaseName] = useState('')

  // ── editor state ─────────────────────────────────────────────
  const [form, setForm] = useState<PhaseInfo | null>(null)
  const [loadingPhase, setLoadingPhase] = useState(false)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)

  const showFeedback = (msg: string, isError = false): void => {
    setFeedback({ msg, isError })
    if (!isError) setTimeout(() => setFeedback(null), 3000)
  }

  // Load metadata on mount
  useEffect(() => {
    if (!apiUrl) return
    fetch(`${apiUrl}/api/metadata`)
      .then((r) => r.json())
      .then((data: AdminMetadata) => {
        setMetadata(data)
        const firstGame = Object.keys(data.games ?? {})[0] ?? ''
        setSelectedGame(firstGame)
        const firstPhase = data.games[firstGame]?.phases[0]?.name ?? ''
        setSelectedPhase(firstPhase)
      })
      .catch((err) => showFeedback(err instanceof Error ? err.message : 'Failed to load metadata', true))
  }, [apiUrl])

  // Load phase info when selection changes (and not creating new)
  useEffect(() => {
    if (!apiUrl || !selectedGame || !selectedPhase || isNewPhase) return
    const controller = new AbortController()
    setForm(null)
    setLoadingPhase(true)
    const encodedPhase = encodeURIComponent(selectedPhase)
    fetch(`${apiUrl}/api/phaseinfo/${selectedGame}/${encodedPhase}`, { signal: controller.signal })
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: { phaseInfo: PhaseInfo }) => setForm(data.phaseInfo))
      .catch((err) => {
        if (err instanceof Error && err.name === 'AbortError') return
        showFeedback(err instanceof Error ? err.message : 'Failed to load milestone', true)
      })
      .finally(() => setLoadingPhase(false))
    return () => controller.abort()
  }, [apiUrl, selectedGame, selectedPhase, isNewPhase])

  const handleGameChange = (game: string): void => {
    setSelectedGame(game)
    const firstPhase = metadata?.games[game]?.phases[0]?.name ?? ''
    setSelectedPhase(firstPhase)
    setIsNewPhase(false)
    setNewPhaseName('')
  }

  const handleSelectPhase = (phase: string): void => {
    setSelectedPhase(phase)
    setIsNewPhase(false)
    setNewPhaseName('')
    setFeedback(null)
  }

  const handleNewPhase = (): void => {
    setIsNewPhase(true)
    setSelectedPhase('')
    setNewPhaseName('')
    setForm(EMPTY_PHASE(selectedGame, ''))
    setFeedback(null)
  }

  const handleNewPhaseNameChange = (name: string): void => {
    setNewPhaseName(name)
    setForm((prev) => prev ? { ...prev, name } : null)
  }

  // ── pokemon list helpers ──────────────────────────────────────
  const updatePokemon = (idx: number, patch: Partial<PhasePokemonInfo>): void => {
    setForm((prev) => {
      if (!prev) return prev
      const pokemon = prev.pokemon.map((p, i) => i === idx ? { ...p, ...patch } : p)
      return { ...prev, pokemon }
    })
  }

  const addPokemon = (): void => {
    setForm((prev) => prev ? { ...prev, pokemon: [...prev.pokemon, EMPTY_POKEMON()] } : prev)
  }

  const removePokemon = (idx: number): void => {
    setForm((prev) => {
      if (!prev) return prev
      return { ...prev, pokemon: prev.pokemon.filter((_, i) => i !== idx) }
    })
  }

  // ── save ─────────────────────────────────────────────────────
  const handleSave = async (): Promise<void> => {
    if (!form) return
    const phaseName = isNewPhase ? newPhaseName.trim() : selectedPhase
    if (!phaseName) { showFeedback('Milestone name is required', true); return }
    // An empty milestone is legitimate: with no requirements UpdateMilestoneState treats it as
    // already satisfied, so it never blocks a run. That makes it the catch-all bucket for training,
    // keeping those encounters out of a real milestone's phase counters.

    setSaving(true)
    setFeedback(null)
    try {
      const payload: PhaseInfo = { ...form, gameName: selectedGame, name: phaseName }
      const encodedPhase = encodeURIComponent(phaseName)
      const res = await fetch(`${apiUrl}/api/updatePhaseInfo/${selectedGame}/${encodedPhase}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const saved = await res.json() as PhaseInfo
      setForm(saved)
      showFeedback(`Saved "${phaseName}" successfully`)
      // Refresh metadata so new phase appears in dropdown
      if (isNewPhase) {
        const metaRes = await fetch(`${apiUrl}/api/metadata`)
        if (metaRes.ok) {
          const data = await metaRes.json() as AdminMetadata
          setMetadata(data)
        }
        setIsNewPhase(false)
        setSelectedPhase(phaseName)
        setNewPhaseName('')
      }
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Save failed', true)
    } finally {
      setSaving(false)
    }
  }

  const availableGames = Object.keys(metadata?.games ?? {})
  const availablePhases = selectedGame ? (metadata?.games[selectedGame]?.phases ?? []) : []
  const selectedPhaseInfo = availablePhases.find((p) => p.name === selectedPhase)
  const isReadOnly = !isNewPhase && (selectedPhaseInfo?.isStatic ?? form?.isStatic ?? false)

  return (
    <div className="p-3 space-y-4">
      {/* Game + Phase selector */}
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Select Milestone
        </h3>
        <div className="flex gap-2 flex-wrap">
          <select
            value={selectedGame}
            onChange={(e) => handleGameChange(e.target.value)}
            className="bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
          >
            {availableGames.length === 0 && <option value="">Loading...</option>}
            {availableGames.map((g) => (
              <option key={g} value={g}>{g}</option>
            ))}
          </select>

          <select
            value={isNewPhase ? '__new__' : selectedPhase}
            onChange={(e) => {
              if (e.target.value === '__new__') handleNewPhase()
              else handleSelectPhase(e.target.value)
            }}
            className="flex-1 bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            disabled={!selectedGame}
          >
            {availablePhases.map((p) => (
              <option key={p.name} value={p.name}>{p.name}</option>
            ))}
            <option value="__new__">+ New milestone…</option>
          </select>
        </div>

        {isNewPhase && (
          <div className="mt-2">
            <input
              type="text"
              value={newPhaseName}
              onChange={(e) => handleNewPhaseNameChange(e.target.value)}
              placeholder="New milestone name"
              className="w-full bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
            />
          </div>
        )}

        {isReadOnly && (
          <p className="mt-1.5 text-xs text-amber-400 bg-amber-900/20 border border-amber-700/30 rounded px-2 py-1">
            This milestone is static (loaded from disk) and cannot be edited.
          </p>
        )}
      </div>

      {/* Pokemon list editor */}
      {loadingPhase ? (
        <p className="text-slate-500 text-sm">Loading milestone...</p>
      ) : form !== null ? (
        <div>
          <div className="flex items-center justify-between mb-2">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400">
              Pokémon
            </h3>
            {!isReadOnly && (
              <button
                onClick={addPokemon}
                className="text-xs px-2 py-1 bg-indigo-700 hover:bg-indigo-600 text-white rounded transition-colors"
              >
                + Add
              </button>
            )}
          </div>

          {form.pokemon.length === 0 ? (
            <p className="text-slate-500 text-sm">No Pokémon defined.</p>
          ) : (
            <div className="space-y-2">
              {form.pokemon.map((pkmn, idx) => (
                <div
                  key={idx}
                  className="bg-card-border/20 rounded-lg px-3 py-2 space-y-2 border border-slate-700/40"
                >
                  <div className="flex gap-2 items-center">
                    {/* Name */}
                    <div className="flex-1">
                      <label className="block text-xs text-slate-500 mb-0.5">Name / Species</label>
                      <input
                        type="text"
                        value={pkmn.name}
                        onChange={(e) => updatePokemon(idx, { name: e.target.value })}
                        placeholder="e.g. Rattata"
                        disabled={isReadOnly}
                        className="w-full bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-50"
                      />
                    </div>
                    {/* Amount */}
                    <div className="w-20">
                      <label className="block text-xs text-slate-500 mb-0.5">Amount</label>
                      {/* 0 is meaningful: the pokemon is tracked in the phase stats but not required,
                          so it never blocks the milestone and the bot never stops for it. */}
                      <input
                        type="number"
                        min={0}
                        value={pkmn.amount}
                        onChange={(e) => updatePokemon(idx, { amount: Number(e.target.value) })}
                        disabled={isReadOnly}
                        className="w-full bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 disabled:opacity-50"
                      />
                    </div>
                    {/* Remove */}
                    {!isReadOnly && (
                      <button
                        onClick={() => removePokemon(idx)}
                        className="self-end mb-0.5 text-xs text-red-400 hover:text-red-300 transition-colors px-1"
                        title="Remove"
                      >
                        ✕
                      </button>
                    )}
                  </div>

                  <div className="flex gap-2 items-center">
                    {/* Form code */}
                    <div className="flex-1">
                      <label className="block text-xs text-slate-500 mb-0.5">Form Code</label>
                      <input
                        type="text"
                        value={pkmn.formCode}
                        onChange={(e) => updatePokemon(idx, { formCode: e.target.value })}
                        placeholder="e.g. rattata (optional)"
                        disabled={isReadOnly}
                        className="w-full bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-50"
                      />
                    </div>
                    {/* Require specific form */}
                    <div className="flex items-center gap-1.5 self-end mb-1">
                      <input
                        type="checkbox"
                        id={`rsf-${idx}`}
                        checked={pkmn.requireSpecificForm}
                        onChange={(e) => updatePokemon(idx, { requireSpecificForm: e.target.checked })}
                        disabled={isReadOnly}
                        className="accent-indigo-500 disabled:opacity-50"
                      />
                      <label htmlFor={`rsf-${idx}`} className="text-xs text-slate-400 cursor-pointer select-none">
                        Require form
                      </label>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}

          {/* Save button */}
          {!isReadOnly && (
            <div className="pt-2">
              <button
                onClick={handleSave}
                disabled={saving}
                className="w-full px-3 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors font-medium"
              >
                {saving ? 'Saving...' : 'Save Milestone'}
              </button>
            </div>
          )}
        </div>
      ) : null}

      {/* Feedback */}
      {feedback && (
        <div
          className={`text-xs rounded px-3 py-2 ${
            feedback.isError
              ? 'text-red-400 bg-red-900/20 border border-red-700/30'
              : 'text-green-400 bg-green-900/20 border border-green-700/30'
          }`}
        >
          {feedback.msg}
        </div>
      )}
    </div>
  )
}

// ============================================================
// Admin Card
// ============================================================

export function AdminCard(): JSX.Element {
  const [activeTab, setActiveTab] = useState<TabId>('config')
  const [refreshKey, setRefreshKey] = useState(0)
  const [macroRecordingEnabled, setMacroRecordingEnabled] = useState(false)

  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  useEffect(() => {
    if (!apiUrl) return
    fetch(`${apiUrl}/api/metadata`)
      .then((r) => (r.ok ? r.json() : null))
      .then((data: AdminMetadata | null) => setMacroRecordingEnabled(data?.macroRecordingEnabled ?? false))
      .catch(() => setMacroRecordingEnabled(false))
  }, [apiUrl])

  const tabs = macroRecordingEnabled ? [...BASE_TABS, DEV_MODE_TAB] : BASE_TABS

  // If the flag is turned off while the dev tab is open, fall back rather than render nothing.
  useEffect(() => {
    if (!macroRecordingEnabled && activeTab === 'macroRecord') setActiveTab('config')
  }, [macroRecordingEnabled, activeTab])

  const handleRefresh = (): void => {
    setRefreshKey((k) => k + 1)
  }

  return (
    <div className="dashboard-card h-full">
      <div className="card-header">
        <span className="card-header-title">Admin</span>
        <div className="flex items-center gap-3">
          <ConnectionStatus />
          <button
            onClick={handleRefresh}
            title="Refresh"
            className="text-slate-400 hover:text-white transition-colors text-sm px-1"
          >
            ↻
          </button>
        </div>
      </div>

      {/* Tab bar */}
      <div className="tab-bar px-2">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            className={`tab-btn ${activeTab === tab.id ? 'active' : ''}`}
            onClick={() => setActiveTab(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-auto">
        {activeTab === 'config' && <DashboardConfigTab />}
        {activeTab === 'botControl' && (
          <BotControlTab key={refreshKey} onRefresh={handleRefresh} />
        )}
        {activeTab === 'phaseEditor' && <PhaseEditorTab />}
        {activeTab === 'windows' && <WindowLayoutTab />}
        {activeTab === 'macroRecord' && macroRecordingEnabled && <DevModeTab />}
      </div>
    </div>
  )
}
