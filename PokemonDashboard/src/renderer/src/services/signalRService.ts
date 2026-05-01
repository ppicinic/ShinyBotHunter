import * as signalR from '@microsoft/signalr'
import type { BotDashboardState, GameEncounterResponse, DashboardConfig, ActiveSubscription } from '../types/api'
import { normalizeGameFlavor } from '../utils/gameFlavor'

export type EncountersHandler = (response: GameEncounterResponse, game: string, phase: string) => void
export type DashboardConfigHandler = (config: DashboardConfig) => void
export type DashboardStateHandler = (state: BotDashboardState) => void
export type ConnectionStateHandler = (state: 'connected' | 'disconnected' | 'reconnecting') => void

export class SignalRService {
  private connection: signalR.HubConnection | null = null
  private backendUrl: string
  private subscribedGroups: Set<string> = new Set()

  private encountersHandlers: EncountersHandler[] = []
  private dashboardConfigHandlers: DashboardConfigHandler[] = []
  private dashboardStateHandlers: DashboardStateHandler[] = []
  private connectionStateHandlers: ConnectionStateHandler[] = []

  // Track which game+phase the last RecieveEncounters came from
  // The server sends to a group named {game}_{phase}, but the message itself
  // doesn't include game/phase. We track subscriptions to infer it.
  private pendingSubscriptions: Map<string, { game: string; phase: string }> = new Map()

  constructor(backendUrl: string) {
    this.backendUrl = backendUrl
  }

  async start(): Promise<void> {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${this.backendUrl}/socket-api`)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Exponential backoff: 1s, 2s, 4s, 8s, 16s, then 30s forever
          const delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000)
          return delay
        }
      })
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    this.connection.on('RecieveEncounters', (response: GameEncounterResponse) => {
      // Notify all handlers — they receive the raw response.
      // The subscription context (game/phase) is tracked separately.
      this.encountersHandlers.forEach((h) => {
        // Find which subscription this could belong to by checking phaseEncounters keys
        // The server sends to a specific group, so we match by phase name in the response
        const phaseKeys = Object.keys(response.phaseEncounters ?? {})
        for (const [, sub] of this.pendingSubscriptions.entries()) {
          if (phaseKeys.includes(sub.phase)) {
            h(response, sub.game, sub.phase)
            return
          }
        }
        // Fallback: emit with empty context if we can't match
        h(response, '', '')
      })
    })

    // C# interface method is ReceiveDashboardConfig (not RefreshDashboardConfig)
    // Payload shape: { gamePhases: [{game, phase}], recentEncounterCount: number }
    // C# GameFlavor enum serializes as integer by default — normalize to string name.
    this.connection.on('ReceiveDashboardConfig', (rawConfig: { gamePhases: Array<{ game: number | string; phase: string; gameFileName?: string | null }>; recentEncounterCount: number }) => {
      const config: DashboardConfig = {
        gamePhases: (rawConfig.gamePhases ?? []).map((gp): ActiveSubscription => ({
          game: normalizeGameFlavor(gp.game),
          phase: gp.phase,
          gameName: gp.gameFileName ?? null
        })),
        recentEncounterCount: rawConfig.recentEncounterCount ?? 0
      }
      this.dashboardConfigHandlers.forEach((h) => h(config))
    })

    this.connection.on('ReceiveDashboardState', (state: BotDashboardState) => {
      this.dashboardStateHandlers.forEach((h) => h(state))
    })

    this.connection.onreconnecting(() => {
      this.connectionStateHandlers.forEach((h) => h('reconnecting'))
    })

    this.connection.onreconnected(async () => {
      this.connectionStateHandlers.forEach((h) => h('connected'))
      // Re-subscribe to all groups after reconnect
      for (const [, sub] of this.pendingSubscriptions.entries()) {
        await this.subscribeToGamePhase(sub.game, sub.phase)
      }
    })

    this.connection.onclose(() => {
      this.connectionStateHandlers.forEach((h) => h('disconnected'))
    })

    await this.connection.start()
    this.connectionStateHandlers.forEach((h) => h('connected'))
  }

  async subscribeToGamePhase(game: string, phase: string): Promise<void> {
    if (!this.connection) throw new Error('SignalR not connected')
    const groupKey = `${game}_${phase}`
    await this.connection.invoke('SubscribeToGamePhase', game, phase)
    this.subscribedGroups.add(groupKey)
    this.pendingSubscriptions.set(groupKey, { game, phase })
  }

  async unsubscribeFromGamePhase(game: string, phase: string): Promise<void> {
    // SignalR doesn't expose a client-side unsubscribe method in the current hub.
    // We remove from our local tracking so we stop routing messages to that subscription.
    const groupKey = `${game}_${phase}`
    this.subscribedGroups.delete(groupKey)
    this.pendingSubscriptions.delete(groupKey)
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
    return this.connection?.state ?? 'Disconnected'
  }

  async stop(): Promise<void> {
    await this.connection?.stop()
  }
}
