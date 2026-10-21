import React, { useCallback, useEffect, useReducer, useRef, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { AdminMetadata, BotDashboardState, EmulatorState, EventArgumentOptions, GameRomInfo, PartySlotInfo, PhaseInfo } from '../../types/api'
import { buildLegBody, initialParamsFor, LEG_PARAM_KEYS, legsVisible, visibleParamNames } from './actionSchema'
import { botPositionsReducer, initialBotPositions } from './botPositions'
import { EmulatorCard } from './EmulatorCard'
import { TopLevelActionForm } from './TopLevelActionForm'

// EventState enum values matching C# EventState in IBotEvent.cs
const ACTIONABLE_STATES = new Set([0, 3, 4]) // NotCreated=0, Waiting=3, Complete=4

export function BotControlTab(): JSX.Element {
  const { config, signalR } = useAppContext()
  const [actions, setActions] = useState<EventArgumentOptions[]>([])
  const [loadingActions, setLoadingActions] = useState(true)
  const [actionsError, setActionsError] = useState<string | null>(null)
  const [positions, dispatch] = useReducer(botPositionsReducer, initialBotPositions)
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
  const [confirmCancelPos, setConfirmCancelPos] = useState<number | null>(null)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean; at: number | string } | null>(null)
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
  const [loadingParty, setLoadingParty] = useState<number | null>(null)

  const apiUrl = config?.apiUrl ?? ''

  const loadParty = useCallback(async (pos: number): Promise<void> => {
    if (!apiUrl) return
    setLoadingParty(pos)
    try {
      const r = await api.getParty(apiUrl, pos)
      if (!r.ok) throw new Error(`Server returned ${r.status}`)
      const party = await r.json() as PartySlotInfo[]
      dispatch({ type: 'partyLoaded', pos, party })
    } catch (err) {
      console.error(`Failed to load party for pos ${pos}:`, err)
      dispatch({ type: 'partyLoaded', pos, party: [] })
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
            const r = await api.getBotActions(apiUrl, pos)
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
    dispatch({ type: 'actionsLoaded', updates })
  }, [apiUrl])

  const loadDashboardState = useCallback(async (): Promise<void> => {
    if (!apiUrl) return
    setLoadingState(true)
    try {
      const r = await api.getDashboardState(apiUrl)
      if (!r.ok) throw new Error(`Server returned ${r.status}`)
      const state = await r.json() as BotDashboardState
      dispatch({ type: 'stateReceived', state })
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
    api.getTopLevelActions(apiUrl)
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

    api.getAllGames(apiUrl)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: GameRomInfo[]) => setAllGames(data))
      .catch((err) => console.error('Failed to load games:', err))

    api.getMetadata(apiUrl)
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
      dispatch({ type: 'stateReceived', state })
      setLoadingState(false)
      await fetchBotActionsForState(state)
    })
    return unsub
  }, [signalR, fetchBotActionsForState])

  // Positions whose encounter form -- single or any leg of a chain -- has Switch train mode picked.
  const switchPositions = new Set<string>()
  Object.entries(positions.botActionParams).forEach(([posStr, actionsForPos]) => {
    if (Object.values(actionsForPos).some((p) => p['TrainType'] === 'Switch')) switchPositions.add(posStr)
  })
  Object.entries(positions.botChainLegs).forEach(([posStr, actionsForPos]) => {
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

  const handleRunBotAction = async (pos: number, action: EventArgumentOptions): Promise<void> => {
    if (positions.posInFlight[pos]) return
    dispatch({ type: 'requestStarted', pos, kind: 'run' })
    setFeedback(null)
    try {
      const params = positions.botActionParams[pos]?.[action.name] ?? {}
      let body: Record<string, unknown>
      if (action.legParameters) {
        body = { eventName: action.name, legs: (positions.botChainLegs[pos]?.[action.name] ?? []).map(buildLegBody) }
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

      const res = await api.startBotAction(apiUrl, pos, body)
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      dispatch({ type: 'stateReceived', state: newState })
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Started "${action.name}" on bot #${pos}`, isError: false, at: pos })
      setTimeout(() => setFeedback(null), 4000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true, at: pos })
    } finally {
      dispatch({ type: 'requestFinished', pos })
    }
  }

  const handleParamChange = (actionName: string, param: string, value: string): void => {
    setParamValues((prev) => ({
      ...prev,
      [actionName]: { ...prev[actionName], [param]: value }
    }))
  }

  const handleStartAction = async (action: EventArgumentOptions): Promise<void> => {
    if (submitting !== null) return
    setSubmitting(action.name)
    setFeedback(null)
    try {
      const params = paramValues[action.name] ?? {}
      const body: Record<string, unknown> = { eventName: action.name }
      if (action.legParameters && legsVisible(action, params)) {
        const legs = topLegs[action.name] ?? [initialParamsFor(action.legParameters)]
        body['legs'] = legs.map(buildLegBody)
      }
      if (params['GameName'] !== undefined) {
        body['gameName'] = params['GameName']
      }
      // A field the current mode hides is not part of this submission, even if an earlier mode left
      // a value in it.
      const visible = visibleParamNames(action, params)
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
        if (!visible.has(key)) continue
        // Numeric parameters are sent as numbers rather than relying on the server coercing them.
        const asNumber = Number(value)
        body[key] = value.trim() !== '' && !Number.isNaN(asNumber) && /^\d+$/.test(value.trim())
          ? asNumber
          : value
      }

      const res = await api.startTopLevelAction(apiUrl, body)
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      dispatch({ type: 'stateReceived', state: newState })
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Started "${action.name}"`, isError: false, at: action.name })
      setTimeout(() => setFeedback(null), 4000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true, at: action.name })
    } finally {
      setSubmitting(null)
    }
  }

  const handleCancelAction = async (pos: number): Promise<void> => {
    if (positions.posInFlight[pos]) return
    dispatch({ type: 'requestStarted', pos, kind: 'cancel' })
    setConfirmCancelPos(null)
    setFeedback(null)
    try {
      const res = await api.cancelBotAction(apiUrl, pos)
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      dispatch({ type: 'stateReceived', state: newState })
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Cancelled position ${pos}`, isError: false, at: pos })
      setTimeout(() => setFeedback(null), 3000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true, at: pos })
    } finally {
      dispatch({ type: 'requestFinished', pos })
    }
  }

  const activePositions = Object.entries(positions.botState).map(([pos, state]) => ({
    pos: Number(pos),
    name: (state as EmulatorState).name,
    eventState: (state as EmulatorState).eventState
  }))

  // The last message shows next to the position or top-level action it came from.
  const renderFeedback = (at: number | string): JSX.Element | null => feedback?.at === at ? (
    <div
      className={`text-xs rounded px-3 py-2 ${
        feedback.isError
          ? 'text-red-400 bg-red-900/20 border border-red-700/30'
          : 'text-green-400 bg-green-900/20 border border-green-700/30'
      }`}
    >
      {feedback.msg}
    </div>
  ) : null

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
            {activePositions.map(({ pos, name }) => (
              <EmulatorCard
                key={pos}
                pos={pos}
                name={name}
                emState={positions.botState[pos] as EmulatorState | undefined}
                metadata={metadata}
                actions={positions.botActions[pos] ?? []}
                params={positions.botActionParams[pos] ?? {}}
                legs={positions.botChainLegs[pos] ?? {}}
                selected={positions.selectedBotAction[pos]}
                party={positions.partyByPos[pos]}
                partyLoading={loadingParty === pos}
                inFlight={positions.posInFlight[pos]}
                confirmingCancel={confirmCancelPos === pos}
                onConfirmCancel={setConfirmCancelPos}
                onCancel={() => handleCancelAction(pos)}
                onRun={(action) => handleRunBotAction(pos, action)}
                onReloadParty={() => loadParty(pos)}
                dispatch={dispatch}
                feedback={renderFeedback(pos)}
              />
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
            {actions.map((action) => (
              <TopLevelActionForm
                key={action.name}
                action={action}
                params={paramValues[action.name] ?? {}}
                storedLegs={topLegs[action.name]}
                allGames={allGames}
                phasesFor={phasesForSelectedGame}
                submitting={submitting}
                onParamChange={(param, value) => handleParamChange(action.name, param, value)}
                onSeedLegs={(legs) => setTopLegs((prev) => ({ ...prev, [action.name]: legs }))}
                onLegChange={(index, param, value) => handleTopLegChange(action.name, index, param, value)}
                onAddLeg={(current) => handleTopAddLeg(action, current)}
                onRemoveLeg={(index) => handleTopRemoveLeg(action.name, index)}
                onStart={() => handleStartAction(action)}
                feedback={renderFeedback(action.name)}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
