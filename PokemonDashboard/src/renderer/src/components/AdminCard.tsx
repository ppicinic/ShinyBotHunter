import React, { useCallback, useEffect, useState } from 'react'
import { useAppContext } from '../context/AppContext'
import type { AdminMetadata, BotDashboardState, EmulatorState, EventArgumentOptions, PhaseInfo, PhasePokemonInfo } from '../types/api'
import { normalizeGameFlavor } from '../utils/gameFlavor'

type TabId = 'config' | 'botControl' | 'phaseEditor'

const TABS: { id: TabId; label: string }[] = [
  { id: 'config', label: 'Dashboard Config' },
  { id: 'botControl', label: 'Bot Control' },
  { id: 'phaseEditor', label: 'Phase Editor' }
]

// ============================================================
// Dashboard Config Tab
// ============================================================

function DashboardConfigTab(): JSX.Element {
  const { dashboardConfig, config } = useAppContext()
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  const [selectedGame, setSelectedGame] = useState<string>('')
  const [selectedPhase, setSelectedPhase] = useState<string>('')
  const [selectedGameName, setSelectedGameName] = useState<string>('')
  const [allGames, setAllGames] = useState<string[]>([])
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
      .then((data: string[]) => setAllGames(data))
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
            {availablePhases.length === 0 && <option value="">No phases</option>}
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
              <option key={g} value={g}>{g}</option>
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
  const [submitting, setSubmitting] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState<number | null>(null)
  const [confirmCancelPos, setConfirmCancelPos] = useState<number | null>(null)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)
  const [allGames, setAllGames] = useState<string[]>([])
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  // Per-bot available actions: pos -> EventArgumentOptions[]
  const [botActions, setBotActions] = useState<Record<number, EventArgumentOptions[]>>({})
  // Per-bot action parameter values: pos -> { actionName -> { paramName -> value } }
  const [botActionParams, setBotActionParams] = useState<Record<number, Record<string, Record<string, string>>>>({})

  const apiUrl = config?.apiUrl ?? ''

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
    // Initialize param values for any new actions
    setBotActionParams((prev) => {
      const next = { ...prev }
      for (const [posStr, actionList] of Object.entries(updates)) {
        const pos = Number(posStr)
        if (!next[pos]) next[pos] = {}
        actionList.forEach((action) => {
          if (!next[pos][action.name]) {
            next[pos][action.name] = {}
            action.requiredParameters?.forEach((param) => {
              next[pos][action.name][param] = ''
            })
          }
        })
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
          initial[action.name] = {}
          action.requiredParameters?.forEach((param) => {
            initial[action.name][param] = ''
          })
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
      .then((data: string[]) => setAllGames(data))
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

  // Expose refresh to parent
  useEffect(() => {
    onRefresh // just reference it so the parent can call loadDashboardState
  }, [onRefresh])

  const TARGET_TYPE_OPTIONS = ['PhaseTarget', 'ShinyTarget']

  const handleRunBotAction = async (pos: number, action: EventArgumentOptions): Promise<void> => {
    setFeedback(null)
    try {
      const params = botActionParams[pos]?.[action.name] ?? {}
      const body: Record<string, string | undefined> = { eventName: action.name }
      if (params['PhaseName'] !== undefined) {
        body['phaseName'] = params['PhaseName']
      }
      if (params['TargetType'] !== undefined) {
        body['targetType'] = params['TargetType']
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
      const body: Record<string, string> = { eventName: action.name }
      if (params['GameName'] !== undefined) {
        body['gameName'] = params['GameName']
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
                      {availableActions.map((action) => {
                        const params = posParams[action.name] ?? {}
                        const hasParams = (action.requiredParameters?.length ?? 0) > 0
                        const allFilled = !hasParams || action.requiredParameters!.every((p) => params[p]?.trim())
                        return (
                          <div key={action.name} className="space-y-1">
                            <div className="flex items-center justify-between">
                              <span className="text-xs text-slate-300">{action.name}</span>
                              <button
                                onClick={() => handleRunBotAction(pos, action)}
                                disabled={!allFilled}
                                className="px-2 py-0.5 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                              >
                                Run
                              </button>
                            </div>
                            {action.requiredParameters?.map((param) => (
                              <div key={param} className="flex items-center gap-2">
                                <label className="text-xs text-slate-500 w-20 flex-shrink-0">{param}</label>
                                {param === 'PhaseName' ? (
                                  <select
                                    value={params[param] ?? ''}
                                    onChange={(e) => handleBotParamChange(pos, action.name, param, e.target.value)}
                                    className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
                                  >
                                    <option value="">Select a phase...</option>
                                    {availablePhases.map((phase) => (
                                      <option key={phase.name} value={phase.name}>{phase.name}</option>
                                    ))}
                                  </select>
                                ) : param === 'TargetType' ? (
                                  <select
                                    value={params[param] ?? ''}
                                    onChange={(e) => handleBotParamChange(pos, action.name, param, e.target.value)}
                                    className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
                                  >
                                    <option value="">Select target type...</option>
                                    {TARGET_TYPE_OPTIONS.map((t) => (
                                      <option key={t} value={t}>{t}</option>
                                    ))}
                                  </select>
                                ) : (
                                  <input
                                    type="text"
                                    value={params[param] ?? ''}
                                    onChange={(e) => handleBotParamChange(pos, action.name, param, e.target.value)}
                                    placeholder={param}
                                    className="flex-1 bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
                                  />
                                )}
                              </div>
                            ))}
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
              const allParamsFilled = !hasParams || action.requiredParameters!.every((p) => params[p]?.trim())
              const isSubmitting = submitting === action.name

              return (
                <div
                  key={action.name}
                  className="bg-card-border/20 rounded-lg px-3 py-2.5 space-y-2"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-medium text-white">{action.name}</span>
                    <button
                      onClick={() => handleStartAction(action)}
                      disabled={isSubmitting || !allParamsFilled}
                      className="px-3 py-1 bg-green-700 hover:bg-green-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                    >
                      {isSubmitting ? 'Starting...' : 'Start'}
                    </button>
                  </div>

                  {action.requiredParameters?.map((param) => (
                    <div key={param} className="flex items-center gap-2">
                      <label className="text-xs text-slate-400 w-24 flex-shrink-0">{param}</label>
                      {param === 'GameName' ? (
                        <select
                          value={params[param] ?? ''}
                          onChange={(e) => handleParamChange(action.name, param, e.target.value)}
                          className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500"
                        >
                          <option value="">Select a game...</option>
                          {allGames.map((game) => (
                            <option key={game} value={game}>{game}</option>
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
    setForm(null)
    setLoadingPhase(true)
    const encodedPhase = encodeURIComponent(selectedPhase)
    fetch(`${apiUrl}/api/phaseinfo/${selectedGame}/${encodedPhase}`)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: { phaseInfo: PhaseInfo }) => setForm(data.phaseInfo))
      .catch((err) => showFeedback(err instanceof Error ? err.message : 'Failed to load phase', true))
      .finally(() => setLoadingPhase(false))
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
    if (!phaseName) { showFeedback('Phase name is required', true); return }
    if (form.pokemon.length === 0) { showFeedback('At least one Pokémon is required', true); return }

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
          Select Phase
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
            <option value="__new__">+ New phase…</option>
          </select>
        </div>

        {isNewPhase && (
          <div className="mt-2">
            <input
              type="text"
              value={newPhaseName}
              onChange={(e) => handleNewPhaseNameChange(e.target.value)}
              placeholder="New phase name"
              className="w-full bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
            />
          </div>
        )}

        {isReadOnly && (
          <p className="mt-1.5 text-xs text-amber-400 bg-amber-900/20 border border-amber-700/30 rounded px-2 py-1">
            This phase is static (loaded from disk) and cannot be edited.
          </p>
        )}
      </div>

      {/* Pokemon list editor */}
      {loadingPhase ? (
        <p className="text-slate-500 text-sm">Loading phase...</p>
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
                      <input
                        type="number"
                        min={1}
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
                {saving ? 'Saving...' : 'Save Phase'}
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

  const handleRefresh = (): void => {
    setRefreshKey((k) => k + 1)
  }

  return (
    <div className="dashboard-card h-full">
      <div className="card-header">
        <span className="card-header-title">Admin</span>
        <button
          onClick={handleRefresh}
          title="Refresh"
          className="text-slate-400 hover:text-white transition-colors text-sm px-1"
        >
          ↻
        </button>
      </div>

      {/* Tab bar */}
      <div className="tab-bar px-2">
        {TABS.map((tab) => (
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
      </div>
    </div>
  )
}
