import React, { useCallback, useEffect, useState } from 'react'
import { useAppContext } from '../context/AppContext'
import type { AdminMetadata, BotDashboardState, EventArgumentOptions } from '../types/api'

type TabId = 'config' | 'botControl'

const TABS: { id: TabId; label: string }[] = [
  { id: 'config', label: 'Dashboard Config' },
  { id: 'botControl', label: 'Bot Control' }
]

// ============================================================
// Dashboard Config Tab
// ============================================================

function DashboardConfigTab(): JSX.Element {
  const { dashboardConfig, config } = useAppContext()
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  const [selectedGame, setSelectedGame] = useState<string>('')
  const [selectedPhase, setSelectedPhase] = useState<string>('')
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
        setSelectedPhase(data.games[firstGame]?.phases[0] ?? '')
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load metadata'))
  }, [apiUrl])

  // Keep recentCount in sync when dashboardConfig updates from SignalR
  useEffect(() => {
    setRecentCount(dashboardConfig.recentEncounterCount)
  }, [dashboardConfig.recentEncounterCount])

  // Update phase dropdown when game changes
  const handleGameChange = (game: string): void => {
    setSelectedGame(game)
    setSelectedPhase(metadata?.games[game]?.phases[0] ?? '')
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
        body: JSON.stringify({ game: selectedGame, phase: selectedPhase })
      })
      if (!res.ok) throw new Error(`Server returned ${res.status}`)
      showFeedback(`Subscribed to ${selectedGame} / ${selectedPhase}`)
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Unknown error', true)
    } finally {
      setLoading(false)
    }
  }

  const handleRemoveSubscription = async (game: string, phase: string): Promise<void> => {
    setLoading(true)
    try {
      const res = await fetch(`${apiUrl}/api/unsubcribeGamePhase`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ game, phase })
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
            {dashboardConfig.gamePhases.map(({ game, phase }) => (
              <li
                key={`${game}_${phase}`}
                className="flex items-center justify-between bg-card-border/30 rounded px-3 py-1.5"
              >
                <span className="text-sm text-white">
                  <span className="text-slate-400 text-xs mr-2">{game}</span>
                  {phase}
                </span>
                <button
                  onClick={() => handleRemoveSubscription(game, phase)}
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
        <div className="flex gap-2">
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
              <option key={p} value={p}>{p}</option>
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

function BotControlTab({ onRefresh }: { onRefresh: () => void }): JSX.Element {
  const { config } = useAppContext()
  const [actions, setActions] = useState<EventArgumentOptions[]>([])
  const [loadingActions, setLoadingActions] = useState(true)
  const [actionsError, setActionsError] = useState<string | null>(null)
  const [botState, setBotState] = useState<BotDashboardState>({})
  const [loadingState, setLoadingState] = useState(true)
  // Per-action parameter values: actionName -> { paramName -> value }
  const [paramValues, setParamValues] = useState<Record<string, Record<string, string>>>({})
  const [submitting, setSubmitting] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState<number | null>(null)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)

  const apiUrl = config?.apiUrl ?? ''

  const loadDashboardState = useCallback(async (): Promise<void> => {
    if (!apiUrl) return
    setLoadingState(true)
    try {
      const r = await fetch(`${apiUrl}/api/bot/getDashboardState`)
      if (!r.ok) throw new Error(`Server returned ${r.status}`)
      const state = await r.json() as BotDashboardState
      setBotState(state)
    } catch (err) {
      console.error('Failed to load bot dashboard state:', err)
    } finally {
      setLoadingState(false)
    }
  }, [apiUrl])

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

    loadDashboardState()
  }, [apiUrl, loadDashboardState])

  // Expose refresh to parent
  useEffect(() => {
    onRefresh // just reference it so the parent can call loadDashboardState
  }, [onRefresh])

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
      setFeedback({ msg: `Started "${action.name}"`, isError: false })
      setTimeout(() => setFeedback(null), 4000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true })
    } finally {
      setSubmitting(null)
    }
  }

  const handleCancelAction = async (pos: number): Promise<void> => {
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
      setFeedback({ msg: `Cancelled position ${pos}`, isError: false })
      setTimeout(() => setFeedback(null), 3000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true })
    } finally {
      setCancelling(null)
    }
  }

  const activePositions = Object.entries(botState).map(([pos, name]) => ({
    pos: Number(pos),
    name
  }))

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
          <ul className="space-y-1">
            {activePositions.map(({ pos, name }) => (
              <li
                key={pos}
                className="flex items-center justify-between bg-card-border/30 rounded px-3 py-1.5"
              >
                <span className="text-sm text-white">
                  <span className="text-slate-400 text-xs mr-2">#{pos}</span>
                  {name}
                </span>
                <button
                  onClick={() => handleCancelAction(pos)}
                  disabled={cancelling === pos}
                  className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors"
                >
                  {cancelling === pos ? 'Cancelling...' : 'Cancel'}
                </button>
              </li>
            ))}
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
                      <input
                        type="text"
                        value={params[param] ?? ''}
                        onChange={(e) => handleParamChange(action.name, param, e.target.value)}
                        placeholder={param}
                        className="flex-1 bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
                      />
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
      </div>
    </div>
  )
}
