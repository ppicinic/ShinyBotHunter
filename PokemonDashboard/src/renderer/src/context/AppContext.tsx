import React, { createContext, useContext, useEffect, useRef, useState } from 'react'
import * as api from '../services/apiClient'
import { IpcSignalRService } from '../services/ipcSignalRService'
import type {
  AppConfig,
  DashboardConfig,
  GameEncounterResponse,
  PhaseInfo,
  PokemonData,
  SubscriptionState,
  WrappedPokemonPhase
} from '../types/api'

interface AppContextValue {
  config: AppConfig | null
  connectionState: 'connected' | 'disconnected' | 'reconnecting'
  dashboardConfig: DashboardConfig
  subscriptions: SubscriptionState[]
  recentEncounters: PokemonData[]
  signalR: IpcSignalRService | null
}

// Empty initial state — the backend sends ReceiveDashboardConfig on connect
// which will populate gamePhases and recentEncounterCount.
const emptyDashboardConfig: DashboardConfig = {
  gamePhases: [],
  recentEncounterCount: 0,
}

const AppContext = createContext<AppContextValue>({
  config: null,
  connectionState: 'disconnected',
  dashboardConfig: emptyDashboardConfig,
  subscriptions: [],
  recentEncounters: [],
  signalR: null
})

// Fetches phase info + initial encounter data from GET /api/phaseinfo/{game}/{phase}[/{gameFileName}]
// The endpoint returns WrappedPokemonPhase: { phaseInfo, data }
async function fetchWrappedPhaseInfo(
  apiUrl: string,
  game: string,
  phase: string,
  gameName?: string | null
): Promise<{ phaseInfo: PhaseInfo; encounterData: GameEncounterResponse } | null> {
  try {
    const response = await api.getSubscriptionPhaseInfo(apiUrl, game, phase, gameName)
    if (!response.ok) return null
    const wrapped = (await response.json()) as WrappedPokemonPhase
    return { phaseInfo: wrapped.phaseInfo, encounterData: wrapped.data }
  } catch {
    return null
  }
}

export function AppProvider({ children }: { children: React.ReactNode }): JSX.Element {
  const [config, setConfig] = useState<AppConfig | null>(null)
  const [connectionState, setConnectionState] = useState<'connected' | 'disconnected' | 'reconnecting'>('disconnected')
  const [dashboardConfig, setDashboardConfig] = useState<DashboardConfig>(emptyDashboardConfig)
  const [subscriptions, setSubscriptions] = useState<SubscriptionState[]>([])
  const [recentEncounters, setRecentEncounters] = useState<PokemonData[]>([])
  const signalRRef = useRef<IpcSignalRService | null>(null)
  // Refs so async callbacks always have the latest values without stale closures
  const configRef = useRef<AppConfig | null>(null)
  const dashboardConfigRef = useRef<DashboardConfig>(emptyDashboardConfig)

  // Load static config on mount
  useEffect(() => {
    const loadConfig = async (): Promise<void> => {
      const cfg = await window.api.loadConfig()
      configRef.current = cfg
      setConfig(cfg)
    }
    loadConfig()
  }, [])

  // Keep dashboardConfigRef in sync
  useEffect(() => {
    dashboardConfigRef.current = dashboardConfig
  }, [dashboardConfig])

  // Start SignalR once config is loaded
  useEffect(() => {
    if (!config) return

    const service = new IpcSignalRService()
    signalRRef.current = service

    const unsubState = service.onConnectionState(setConnectionState)

    // ReceiveDashboardConfig: backend sends this on connect and on any admin change.
    // Payload (camelCased by SignalR): { gamePhases: [{game, phase}], recentEncounterCount: N }
    const unsubConfig = service.onDashboardConfig((newConfig) => {
      setDashboardConfig(newConfig)
    })

    const unsubEncounters = service.onEncounters((response: GameEncounterResponse, game: string, phase: string) => {
      // Update subscription encounter data
      setSubscriptions((prev) =>
        prev.map((sub) =>
          sub.game === game && sub.phase === phase
            ? { ...sub, encounterData: response }
            : sub
        )
      )

      // Extract latest encounters from the payload, trimmed to the configured count.
      // Use the ref to get the current recentEncounterCount without stale closure.
      if (response.latestEncounters && response.latestEncounters.length > 0) {
        const maxCount = dashboardConfigRef.current.recentEncounterCount
        const trimmed = maxCount > 0
          ? response.latestEncounters.slice(0, maxCount)
          : response.latestEncounters
        setRecentEncounters(trimmed)
      }
    })

    service.start().catch((err) => {
      console.error('SignalR connection failed:', err)
    })

    return () => {
      unsubState()
      unsubConfig()
      unsubEncounters()
      service.stop()
    }
  }, [config])

  // Sync SignalR group subscriptions and phase info when dashboardConfig changes.
  // Uses functional setSubscriptions to avoid stale closure issues.
  useEffect(() => {
    const service = signalRRef.current
    const cfg = configRef.current
    if (!service || !cfg) return

    const newSubs = dashboardConfig.gamePhases

    // Use functional update to get current subscriptions without stale closure
    setSubscriptions((currentSubs) => {
      // Determine which subs to add (not already tracked)
      const toAdd = newSubs.filter(
        ({ game, phase }) => !currentSubs.some((s) => s.game === game && s.phase === phase)
      )

      // Fetch phase info for new subs
      toAdd.forEach(async ({ game, phase, gameName }) => {
        // Fetch phase info + initial encounter data from REST API
        const result = await fetchWrappedPhaseInfo(cfg.apiUrl, game, phase, gameName)
        if (result) {
          setSubscriptions((prev) =>
            prev.map((sub) =>
              sub.game === game && sub.phase === phase
                ? { ...sub, phaseInfo: result.phaseInfo, encounterData: result.encounterData }
                : sub
            )
          )

          // Also populate recent encounters from the initial REST load
          if (result.encounterData.latestEncounters && result.encounterData.latestEncounters.length > 0) {
            const maxCount = dashboardConfigRef.current.recentEncounterCount
            const trimmed = maxCount > 0
              ? result.encounterData.latestEncounters.slice(0, maxCount)
              : result.encounterData.latestEncounters
            setRecentEncounters(trimmed)
          }
        }
      })

      // Build new subscriptions list: keep existing ones that are still in config,
      // add placeholders for new ones
      const kept = currentSubs.filter((sub) =>
        newSubs.some((ns) => ns.game === sub.game && ns.phase === sub.phase)
      )
      const added = toAdd.map(({ game, phase, gameName }) => ({
        game,
        phase,
        gameName: gameName ?? null,
        encounterData: null,
        phaseInfo: null
      }))
      return [...kept, ...added]
    })
  }, [dashboardConfig])

  // Trim recentEncounters when recentEncounterCount changes
  useEffect(() => {
    const maxCount = dashboardConfig.recentEncounterCount
    if (maxCount > 0) {
      setRecentEncounters((prev) => (prev.length > maxCount ? prev.slice(0, maxCount) : prev))
    }
  }, [dashboardConfig.recentEncounterCount])

  return (
    <AppContext.Provider
      value={{
        config,
        connectionState,
        dashboardConfig,
        subscriptions,
        recentEncounters,
        signalR: signalRRef.current
      }}
    >
      {children}
    </AppContext.Provider>
  )
}

export function useAppContext(): AppContextValue {
  return useContext(AppContext)
}
