export interface WindowBounds {
  x: number
  y: number
  width: number
  height: number
}

export type CardKind = 'admin' | 'recent' | 'phase' | 'pickup'

export interface WindowInfo {
  name: string
  kind: CardKind
  title: string
  bounds: WindowBounds
}

// Mirrors renderer ActiveSubscription (types/api.ts) — duplicated so main never imports renderer code
export interface PhaseWindowRequest {
  game: string
  phase: string
  gameName?: string | null
}

export interface SharedDashboardConfig {
  gamePhases: PhaseWindowRequest[]
  recentEncounterCount: number
}

export type ConnectionState = 'connected' | 'disconnected' | 'reconnecting'

export interface SignalRSnapshot {
  connectionState: ConnectionState
  dashboardConfig: SharedDashboardConfig
}

export interface EncountersPayload {
  response: unknown
  game: string
  phase: string
}

// The shape of GET /api/bot/getPickupItems (one entry per game) and of the ReceivePickupItems push (one game).
export interface PickupItem {
  id: number
  name: string
  count: number
}

export interface PickupTally {
  gameFileName: string
  total: number
  items: PickupItem[]
}

export const ADMIN_WINDOW = 'admin'
export const RECENT_WINDOW = 'recent'

export const IPC = {
  loadConfig: 'load-config',
  getWindowList: 'get-window-list',
  setWindowBounds: 'set-window-bounds',
  getSignalRState: 'get-signalr-state',
  windowBoundsChanged: 'window-bounds-changed',
  signalRConnectionState: 'signalr-connection-state',
  signalRDashboardConfig: 'signalr-dashboard-config',
  signalREncounters: 'signalr-encounters',
  signalRDashboardState: 'signalr-dashboard-state',
  apiRequest: 'api-request',
  setPickupWindows: 'set-pickup-windows',
  getPickupItems: 'get-pickup-items',
  pickupItems: 'pickup-items'
} as const

export function phaseWindowName(r: PhaseWindowRequest): string {
  const enc = encodeURIComponent
  return `phase:${enc(r.game)}:${enc(r.phase)}:${enc(r.gameName ?? '')}`
}

export function phaseWindowTitle(r: PhaseWindowRequest): string {
  return `${r.game} — ${r.phase}${r.gameName ? ` (${r.gameName})` : ''}`
}

export function pickupWindowName(gameFileName: string): string {
  return `pickup:${encodeURIComponent(gameFileName)}`
}

export function pickupWindowTitle(gameFileName: string): string {
  return `Pokemon Dashboard — Pickup: ${gameFileName}`
}
