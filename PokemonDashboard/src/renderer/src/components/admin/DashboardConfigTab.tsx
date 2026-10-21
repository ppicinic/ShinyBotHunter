import React, { useEffect, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { AdminMetadata, GameRomInfo } from '../../types/api'
import { romLabel } from '../../types/api'

export function DashboardConfigTab(): JSX.Element {
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
    api.getMetadata(apiUrl)
      .then((r) => r.json())
      .then((data: AdminMetadata) => {
        setMetadata(data)
        const firstGame = Object.keys(data.games ?? {})[0] ?? ''
        setSelectedGame(firstGame)
        setSelectedPhase(data.games[firstGame]?.phases[0]?.name ?? '')
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load metadata'))

    api.getAllGames(apiUrl)
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
      const res = await api.subscribeGamePhase(apiUrl, { game: selectedGame, phase: selectedPhase, gameName: selectedGameName || null })
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
      const res = await api.unsubscribeGamePhase(apiUrl, { game, phase, gameName: gameName ?? null })
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
      const res = await api.updateRecentEncounterCount(apiUrl, recentCount)
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
