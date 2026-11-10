import type { AppConfig } from './api'
import type {
  ConnectionState,
  EncountersPayload,
  PickupTally,
  SharedDashboardConfig,
  SignalRSnapshot,
  WindowBounds,
  WindowInfo
} from '../../../shared/windowContracts'

declare global {
  interface Window {
    api: {
      loadConfig: () => Promise<AppConfig>
      getWindowList: () => Promise<WindowInfo[]>
      setWindowBounds: (name: string, bounds: Partial<WindowBounds>) => Promise<WindowInfo[]>
      getSignalRState: () => Promise<SignalRSnapshot>
      request: (method: string, url: string, body?: string) => Promise<{ status: number; body: string }>
      setPickupWindows: (on: boolean) => Promise<void>
      getPickupItems: (gameFileName: string) => Promise<PickupTally | null>
      onWindowBoundsChanged: (cb: (windows: WindowInfo[]) => void) => () => void
      onSignalRConnectionState: (cb: (state: ConnectionState) => void) => () => void
      onDashboardConfig: (cb: (config: SharedDashboardConfig) => void) => () => void
      onEncounters: (cb: (payload: EncountersPayload) => void) => () => void
      onDashboardState: (cb: (state: unknown) => void) => () => void
      onPickupItems: (cb: (tally: PickupTally) => void) => () => void
    }
  }
}
