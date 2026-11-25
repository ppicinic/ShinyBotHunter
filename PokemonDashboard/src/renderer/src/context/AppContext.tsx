import React, { createContext, useContext, useEffect, useRef, useState } from 'react'
import * as api from '../services/apiClient'
import { IpcSignalRService } from '../services/ipcSignalRService'
import { applyTheme, windowNameFromSearch } from '../utils/theme'
import type {
  ActiveSubscription,
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
  appearance: null,
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

function sameSubscription(a: ActiveSubscription, b: ActiveSubscription): boolean {
  return a.game === b.game && a.phase === b.phase && (a.gameName ?? null) === (b.gameName ?? null)
}

// One subscription per pushed (game, phase, game file); kept ones are the same objects, so their data survives
export function syncSubscriptions(
  current: SubscriptionState[],
  gamePhases: ActiveSubscription[]
): { subscriptions: SubscriptionState[]; added: SubscriptionState[] } {
  const kept = current.filter((sub) => gamePhases.some((gp) => sameSubscription(gp, sub)))
  const added: SubscriptionState[] = []
  for (const { game, phase, gameName } of gamePhases) {
    const sub = { game, phase, gameName: gameName ?? null, encounterData: null, phaseInfo: null }
    if (!kept.some((s) => sameSubscription(s, sub)) && !added.some((s) => sameSubscription(s, sub))) {
      added.push(sub)
    }
  }
  return { subscriptions: [...kept, ...added], added }
}

// A push names the file that encountered in gameSpecificResponse; a no-file card takes the game-wide counts
export function applyEncounters(
  subs: SubscriptionState[],
  response: GameEncounterResponse,
  game: string,
  phase: string
): SubscriptionState[] {
  return subs.map((sub) =>
    sub.game === game &&
    sub.phase === phase &&
    (sub.gameName === null || sub.gameName in response.gameSpecificResponse)
      ? { ...sub, encounterData: response }
      : sub
  )
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
      setSubscriptions((prev) => applyEncounters(prev, response, game, phase))

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

    // Use functional update to get current subscriptions without stale closure
    setSubscriptions((currentSubs) => {
      const { subscriptions: synced, added } = syncSubscriptions(currentSubs, dashboardConfig.gamePhases)

      // Fetch phase info for new subs
      added.forEach(async (fresh) => {
        // Fetch phase info + initial encounter data from REST API
        const result = await fetchWrappedPhaseInfo(cfg.apiUrl, fresh.game, fresh.phase, fresh.gameName)
        if (result) {
          setSubscriptions((prev) =>
            prev.map((sub) =>
              sameSubscription(sub, fresh)
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

      return synced
    })
  }, [dashboardConfig])

  // Follow the pushed appearance; before the first push the window keeps its remembered theme
  useEffect(() => {
    if (dashboardConfig.appearance) {
      applyTheme(dashboardConfig.appearance, windowNameFromSearch(window.location.search))
    }
  }, [dashboardConfig.appearance])

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
