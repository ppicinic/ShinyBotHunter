import React, { useCallback, useEffect, useRef, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { AdminMetadata, BotDashboardState, EmulatorState, EventArgumentOptions, GameRomInfo, PartySlotInfo, PhaseInfo } from '../../types/api'
import { romLabel } from '../../types/api'
import { normalizeGameFlavor } from '../../utils/gameFlavor'
import { buildLegBody, getParamLabel, initialParamsFor, LEG_PARAM_KEYS, legsVisible, requiredParamsFilled, visibleConditionalsOf, visibleParamNames } from './actionSchema'
import { ActionParamFields } from './ActionParamFields'

// Which hunt a mode-carrying action runs. Values are C# GameFlavor names; the label is what the
// games are actually called.
const MODE_OPTIONS: { value: string; label: string }[] = [
  { value: 'HGSS', label: 'HeartGold / SoulSilver' },
  { value: 'RS', label: 'Ruby / Sapphire' }
]

// EventState enum values matching C# EventState in IBotEvent.cs
const ACTIONABLE_STATES = new Set([0, 3, 4]) // NotCreated=0, Waiting=3, Complete=4

export function BotControlTab(): JSX.Element {
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
  // Set by a position's Run or Cancel button while its request is pending; one request per position.
  const [posInFlight, setPosInFlight] = useState<Record<number, 'run' | 'cancel'>>({})
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
      const r = await api.getParty(apiUrl, pos)
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
      const r = await api.getDashboardState(apiUrl)
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

  const handleRunBotAction = async (pos: number, action: EventArgumentOptions): Promise<void> => {
    if (posInFlight[pos]) return
    setPosInFlight((prev) => ({ ...prev, [pos]: 'run' }))
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

      const res = await api.startBotAction(apiUrl, pos, body)
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      setBotState(newState)
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Started "${action.name}" on bot #${pos}`, isError: false, at: pos })
      setTimeout(() => setFeedback(null), 4000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true, at: pos })
    } finally {
      setPosInFlight((prev) => {
        const next = { ...prev }
        delete next[pos]
        return next
      })
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
      setBotState(newState)
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
    if (posInFlight[pos]) return
    setPosInFlight((prev) => ({ ...prev, [pos]: 'cancel' }))
    setConfirmCancelPos(null)
    setFeedback(null)
    try {
      const res = await api.cancelBotAction(apiUrl, pos)
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const newState = await res.json() as BotDashboardState
      setBotState(newState)
      await fetchBotActionsForState(newState)
      setFeedback({ msg: `Cancelled position ${pos}`, isError: false, at: pos })
      setTimeout(() => setFeedback(null), 3000)
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Unknown error', isError: true, at: pos })
    } finally {
      setPosInFlight((prev) => {
        const next = { ...prev }
        delete next[pos]
        return next
      })
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
                          disabled={!!posInFlight[pos]}
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
                        disabled={!!posInFlight[pos]}
                        className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors"
                      >
                        {posInFlight[pos] === 'cancel' ? 'Cancelling...' : 'Cancel'}
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
                                  disabled={!allFilled || !!posInFlight[pos]}
                                  className="px-2 py-0.5 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                                >
                                  {posInFlight[pos] === 'run' ? 'Running...' : 'Run'}
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
                  {renderFeedback(pos)}
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
              // A conditional field can be required too, but only while it is on screen.
              const shownConditionals = visibleConditionalsOf(action, params)
              const flatFilled = (!hasParams || action.requiredParameters!.every((p) => params[p]?.trim()))
                && shownConditionals.every((cp) => !cp.required || params[cp.name]?.trim())
              const actionLegs = topLegs[action.name] ?? (action.legParameters ? [initialParamsFor(action.legParameters)] : [])
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
                      onChange={(e) => handleParamChange(action.name, param, e.target.value)}
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
              )

              return (
                <div
                  key={action.name}
                  className="bg-card-border/20 rounded-lg px-3 py-2.5 space-y-2"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-medium text-white">{action.name}</span>
                    <div className="flex items-center gap-1.5">
                      {showLegs && (
                        <button
                          onClick={() => handleTopAddLeg(action, actionLegs)}
                          className="px-2 py-1 bg-slate-700 hover:bg-slate-600 text-white text-xs rounded transition-colors"
                        >
                          + {legLabel}
                        </button>
                      )}
                      <button
                        onClick={() => handleStartAction(action)}
                        disabled={submitting !== null || !allParamsFilled}
                        className="px-3 py-1 bg-green-700 hover:bg-green-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                      >
                        {isSubmitting ? 'Starting...' : 'Start'}
                      </button>
                    </div>
                  </div>
                  {renderFeedback(action.name)}

                  {showLegs && actionLegs.map((leg, legIndex) => (
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
    </div>
  )
}
