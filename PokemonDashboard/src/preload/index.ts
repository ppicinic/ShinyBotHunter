import { contextBridge, ipcRenderer, type IpcRendererEvent } from 'electron'
import { IPC } from '../shared/windowContracts'
import type {
  ConnectionState,
  EncountersPayload,
  SharedDashboardConfig,
  SignalRSnapshot,
  WindowBounds,
  WindowInfo
} from '../shared/windowContracts'

function on<T>(channel: string) {
  return (cb: (payload: T) => void): (() => void) => {
    const listener = (_event: IpcRendererEvent, payload: T): void => cb(payload)
    ipcRenderer.on(channel, listener)
    return () => ipcRenderer.removeListener(channel, listener)
  }
}

contextBridge.exposeInMainWorld('api', {
  loadConfig: () => ipcRenderer.invoke(IPC.loadConfig),
  getWindowList: (): Promise<WindowInfo[]> => ipcRenderer.invoke(IPC.getWindowList),
  setWindowBounds: (name: string, bounds: Partial<WindowBounds>): Promise<WindowInfo[]> =>
    ipcRenderer.invoke(IPC.setWindowBounds, name, bounds),
  getSignalRState: (): Promise<SignalRSnapshot> => ipcRenderer.invoke(IPC.getSignalRState),
  onWindowBoundsChanged: on<WindowInfo[]>(IPC.windowBoundsChanged),
  onSignalRConnectionState: on<ConnectionState>(IPC.signalRConnectionState),
  onDashboardConfig: on<SharedDashboardConfig>(IPC.signalRDashboardConfig),
  onEncounters: on<EncountersPayload>(IPC.signalREncounters),
  onDashboardState: on<unknown>(IPC.signalRDashboardState)
})
