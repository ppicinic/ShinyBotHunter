import type { BotDashboardState, DashboardConfig, GameEncounterResponse } from '../types/api'

export type EncountersHandler = (response: GameEncounterResponse, game: string, phase: string) => void
export type DashboardConfigHandler = (config: DashboardConfig) => void
export type DashboardStateHandler = (state: BotDashboardState) => void
export type ConnectionStateHandler = (state: 'connected' | 'disconnected' | 'reconnecting') => void

// Drop-in replacement for the old direct-SignalR service: the actual SignalR connection
// now lives in the Electron main process (one connection per app instance, shared across
// all card windows). This class just relays main-process broadcasts to context consumers.
export class IpcSignalRService {
  private encountersHandlers: EncountersHandler[] = []
  private dashboardConfigHandlers: DashboardConfigHandler[] = []
  private dashboardStateHandlers: DashboardStateHandler[] = []
  private connectionStateHandlers: ConnectionStateHandler[] = []
  private connectionState: 'connected' | 'disconnected' | 'reconnecting' = 'disconnected'
  private unsubscribers: Array<() => void> = []

  constructor() {
    this.unsubscribers.push(
      window.api.onSignalRConnectionState((state) => {
        this.connectionState = state
        this.connectionStateHandlers.forEach((h) => h(state))
      })
    )
    this.unsubscribers.push(
      window.api.onDashboardConfig((config) => {
        this.dashboardConfigHandlers.forEach((h) => h(config))
      })
    )
    this.unsubscribers.push(
      window.api.onEncounters(({ response, game, phase }) => {
        this.encountersHandlers.forEach((h) => h(response as GameEncounterResponse, game, phase))
      })
    )
    this.unsubscribers.push(
      window.api.onDashboardState((state) => {
        this.dashboardStateHandlers.forEach((h) => h(state as BotDashboardState))
      })
    )
  }

  async start(): Promise<void> {
    const snapshot = await window.api.getSignalRState()
    this.connectionState = snapshot.connectionState
    this.connectionStateHandlers.forEach((h) => h(snapshot.connectionState))
    this.dashboardConfigHandlers.forEach((h) => h(snapshot.dashboardConfig))
  }

  async stop(): Promise<void> {
    this.unsubscribers.forEach((unsub) => unsub())
    this.unsubscribers = []
  }

  onEncounters(handler: EncountersHandler): () => void {
    this.encountersHandlers.push(handler)
    return () => {
      this.encountersHandlers = this.encountersHandlers.filter((h) => h !== handler)
    }
  }

  onDashboardConfig(handler: DashboardConfigHandler): () => void {
    this.dashboardConfigHandlers.push(handler)
    return () => {
      this.dashboardConfigHandlers = this.dashboardConfigHandlers.filter((h) => h !== handler)
    }
  }

  onDashboardState(handler: DashboardStateHandler): () => void {
    this.dashboardStateHandlers.push(handler)
    return () => {
      this.dashboardStateHandlers = this.dashboardStateHandlers.filter((h) => h !== handler)
    }
  }

  onConnectionState(handler: ConnectionStateHandler): () => void {
    this.connectionStateHandlers.push(handler)
    return () => {
      this.connectionStateHandlers = this.connectionStateHandlers.filter((h) => h !== handler)
    }
  }

  getConnectionState(): string {
    return this.connectionState
  }
}
