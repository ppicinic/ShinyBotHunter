import * as signalR from '@microsoft/signalr'
import { normalizeGameFlavor } from '../shared/gameFlavor'
import {
  IPC,
  type ConnectionState,
  type PhaseWindowRequest,
  type SharedDashboardConfig,
  type SignalRSnapshot
} from '../shared/windowContracts'
import { broadcast, syncPhaseWindows } from './windowManager'

const emptyDashboardConfig: SharedDashboardConfig = {
  gamePhases: [],
  recentEncounterCount: 0
}

let connection: signalR.HubConnection | null = null
let connectionState: ConnectionState = 'disconnected'
let lastDashboardConfig: SharedDashboardConfig = emptyDashboardConfig
let stopped = false
let initialConnectTimer: NodeJS.Timeout | null = null

// Server sends to a group named {game}_{phase}; the message itself doesn't include
// game/phase, so we track subscriptions to infer which one a message belongs to.
const pendingSubscriptions: Map<string, { game: string; phase: string }> = new Map()

function setConnectionState(state: ConnectionState): void {
  connectionState = state
  broadcast(IPC.signalRConnectionState, state)
}

async function resubscribeAll(): Promise<void> {
  if (!connection) return
  for (const { game, phase } of pendingSubscriptions.values()) {
    try {
      await connection.invoke('SubscribeToGamePhase', game, phase)
    } catch (err) {
      console.error('Failed to resubscribe to group', game, phase, err)
    }
  }
}

async function syncGroupSubscriptions(requests: PhaseWindowRequest[]): Promise<void> {
  if (!connection) return

  const desiredKeys = new Set(requests.map((r) => `${r.game}_${r.phase}`))

  const toAdd = requests.filter((r) => !pendingSubscriptions.has(`${r.game}_${r.phase}`))
  for (const { game, phase } of toAdd) {
    const key = `${game}_${phase}`
    try {
      await connection.invoke('SubscribeToGamePhase', game, phase)
      pendingSubscriptions.set(key, { game, phase })
    } catch (err) {
      console.error('Failed to subscribe to group', game, phase, err)
    }
  }

  for (const key of pendingSubscriptions.keys()) {
    if (!desiredKeys.has(key)) {
      pendingSubscriptions.delete(key)
    }
  }
}

function buildConnection(backendUrl: string): signalR.HubConnection {
  // In a Node/Electron main-process environment, @microsoft/signalr automatically
  // `require()`s the `ws` and `eventsource` packages for its WebSocket/SSE transports —
  // no explicit WebSocket constructor wiring needed, as long as those packages are installed.
  const hub = new signalR.HubConnectionBuilder()
    .withUrl(`${backendUrl}/socket-api`)
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retryContext) => {
        const delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000)
        return delay
      }
    })
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  hub.on('RecieveEncounters', (response: unknown) => {
    const phaseKeys = Object.keys((response as { phaseEncounters?: Record<string, unknown> })?.phaseEncounters ?? {})
    let matched: { game: string; phase: string } | null = null
    for (const sub of pendingSubscriptions.values()) {
      if (phaseKeys.includes(sub.phase)) {
        matched = sub
        break
      }
    }
    broadcast(IPC.signalREncounters, {
      response,
      game: matched?.game ?? '',
      phase: matched?.phase ?? ''
    })
  })

  hub.on(
    'ReceiveDashboardConfig',
    (rawConfig: {
      gamePhases: Array<{ game: number | string; phase: string; gameFileName?: string | null }>
      recentEncounterCount: number
    }) => {
      const config: SharedDashboardConfig = {
        gamePhases: (rawConfig.gamePhases ?? []).map((gp) => ({
          game: normalizeGameFlavor(gp.game),
          phase: gp.phase,
          gameName: gp.gameFileName ?? null
        })),
        recentEncounterCount: rawConfig.recentEncounterCount ?? 0
      }
      lastDashboardConfig = config

      syncGroupSubscriptions(config.gamePhases).catch((err) =>
        console.error('Failed to sync group subscriptions', err)
      )

      broadcast(IPC.signalRDashboardConfig, config)
      syncPhaseWindows(config.gamePhases)
    }
  )

  hub.on('ReceiveDashboardState', (state: unknown) => {
    broadcast(IPC.signalRDashboardState, state)
  })

  hub.onreconnecting(() => {
    setConnectionState('reconnecting')
  })

  hub.onreconnected(() => {
    setConnectionState('connected')
    resubscribeAll().catch((err) => console.error('Failed to resubscribe after reconnect', err))
  })

  hub.onclose(() => {
    setConnectionState('disconnected')
  })

  return hub
}

async function connectWithRetry(): Promise<void> {
  while (!stopped) {
    try {
      await connection?.start()
      setConnectionState('connected')
      return
    } catch (err) {
      console.error('SignalR initial connection failed, retrying in 5s', err)
      await new Promise((resolve) => {
        initialConnectTimer = setTimeout(resolve, 5000)
      })
    }
  }
}

export function startSignalR(backendUrl: string): void {
  stopped = false
  connection = buildConnection(backendUrl)
  connectWithRetry().catch((err) => console.error('SignalR connect loop failed', err))
}

export async function stopSignalR(): Promise<void> {
  stopped = true
  if (initialConnectTimer) {
    clearTimeout(initialConnectTimer)
    initialConnectTimer = null
  }
  await connection?.stop()
  connection = null
}

export function getSnapshot(): SignalRSnapshot {
  return { connectionState, dashboardConfig: lastDashboardConfig }
}
