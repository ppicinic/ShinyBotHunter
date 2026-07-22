# Execution Plan: One OS Window Per Card (Multi-Window Dashboard)

**Status:** Draft for approval — no implementation yet
**Date:** 2026-07-15
**Scope:** `PokemonDashboard/` only. Zero C# backend changes.

---

## 1. Context

The dashboard is an Electron 28 + React 18 + TypeScript app (electron-vite, Tailwind 3). Today it opens **one** 1600x900 `BrowserWindow` and renders all cards — Phase cards (one per subscription), Recent Encounters, Admin — inside a `react-grid-layout` snapping grid ([App.tsx](../src/renderer/src/App.tsx)). Problems:

- A streaming setup (OBS) wants to capture each card as its **own OS window**.
- Card layout is **never persisted** — `onLayoutChange` only updates React state.
- No way to set card dimensions in **exact pixels**.

### Target behavior (user-approved)

1. The grid is **removed entirely**; each card is its own OS window.
2. All enabled card windows **auto-open at launch**; phase windows open/close automatically as subscriptions change.
3. Every window has a **stable name**. Its pixel `x/y/width/height` auto-saves (debounced) to a per-machine JSON file. A window created with a previously-seen name **restores its saved bounds**.
4. The Admin card gains a **"Window Layout" tab**: edit any window's x/y/width/height in pixels; Apply resizes the real OS window immediately and persists.
5. Phase + Recent windows are **frameless** (card header = drag region + close button). Admin keeps the native frame.
6. **One SignalR connection per app instance**, owned by the Electron **main process**, fanned out to windows via IPC. (10 emulators viewed from 2 PCs = 2 backend connections, not 22.) Each PC keeps its own layout file in `userData` — independent layouts per viewing machine, which is what a streaming setup wants.

### Architecture after the change

```
┌────────────────────────── Electron main process ──────────────────────────┐
│ index.ts ── loadConfig() (unchanged read-only config.json)                 │
│ signalRClient.ts ── THE SignalR connection (@microsoft/signalr + ws)       │
│      caches: connectionState, last DashboardConfig                        │
│      → broadcasts events to all windows via webContents.send              │
│      → drives windowManager.syncPhaseWindows(gamePhases)                  │
│ windowManager.ts ── Map<name, BrowserWindow>; open/close/focus;           │
│      bounds restore + ensureVisible clamp; debounced auto-save            │
│ layoutStore.ts ── window-layout.json in app.getPath('userData')           │
└───────────────┬────────────────────────────────────────────────────────────┘
                │ IPC (preload contextBridge)
   ┌────────────┴──────┬───────────────────┬─────────────────────┐
   │ admin (framed)    │ recent (frameless)│ phase:* (frameless) │
   │ AdminWindow.tsx   │ RecentWindow.tsx  │ PhaseWindow.tsx     │
   │ AppProvider + IpcSignalRService (same observer API as before)│
   └───────────────────┴───────────────────┴─────────────────────┘
```

REST calls (`fetch` to `config.apiUrl`) stay in the renderer, unchanged.

---

## 2. Facts about the current code an implementer must know

| Fact | Where |
|---|---|
| Main process creates one window, loads `config.json` (+`override-config.json` deep-merge), IPC `load-config` | [src/main/index.ts](../src/main/index.ts) — keep `deepMerge`/`loadConfig` verbatim |
| Renderer SignalR client: reconnect backoff, `RecieveEncounters` (sic) group inference via `pendingSubscriptions`, `ReceiveDashboardConfig` normalization with `normalizeGameFlavor`, `ReceiveDashboardState` | [src/renderer/src/services/signalRService.ts](../src/renderer/src/services/signalRService.ts) — this logic **moves to main** |
| `AppContext` loads config → builds `SignalRService` → exposes `{ config, connectionState, dashboardConfig, subscriptions, recentEncounters, signalR }`; REST `fetchWrappedPhaseInfo` seeds new subscriptions | [src/renderer/src/context/AppContext.tsx](../src/renderer/src/context/AppContext.tsx) |
| `AdminCard` (line 295, 435-442) uses `signalR.onDashboardState(...)` from context — the replacement service must keep this method | [src/renderer/src/components/AdminCard.tsx](../src/renderer/src/components/AdminCard.tsx) |
| Admin tabs: `TabId`/`TABS` at lines 6-12; tab render at 1280-1287; header + refresh button at 1254-1265 | same file |
| `PhaseCard` root `div.dashboard-card`, header at lines 63-77, footer at 158-172; already calls `useAppContext()` | [src/renderer/src/components/PhaseCard.tsx](../src/renderer/src/components/PhaseCard.tsx) |
| `RecentPokemonCard` header at lines 98-102 | [src/renderer/src/components/RecentPokemonCard.tsx](../src/renderer/src/components/RecentPokemonCard.tsx) |
| `.dashboard-card` uses `@apply rounded-lg` and is declared **after** `@tailwind utilities` → a `rounded-none` utility class will NOT override it (source order). Use a descendant rule instead (see §5.6) | [src/renderer/src/assets/index.css](../src/renderer/src/assets/index.css) lines 52-57 |
| react-grid-layout CSS override block to delete: lines 144-182 | same file |
| tsconfigs are `composite` project references (`tsconfig.json` → node + web). Typecheck with `npx tsc --build --force` from `PokemonDashboard/` (there is **no** npm typecheck script; `--noEmit` conflicts with `composite`) | tsconfig.json / tsconfig.node.json / tsconfig.web.json |
| `tsconfig.node.json` includes `src/main`, `src/preload`; `tsconfig.web.json` includes `src/renderer/src`. **Neither includes `src/shared` yet** | same |
| `window.api` type lives in [src/renderer/src/types/global.d.ts](../src/renderer/src/types/global.d.ts) (currently only `loadConfig`) | |
| `normalizeGameFlavor` lives in renderer utils; also imported by `AdminCard.tsx` line 4 | [src/renderer/src/utils/gameFlavor.ts](../src/renderer/src/utils/gameFlavor.ts) |
| Renderer types `ActiveSubscription { game, phase, gameName?: string \| null }`, `DashboardConfig { gamePhases, recentEncounterCount }` | [src/renderer/src/types/api.ts](../src/renderer/src/types/api.ts) lines 23-33 |
| electron-vite `externalizeDepsPlugin()` for main/preload → runtime `dependencies` stay external and must be installed; electron-builder auto-packages production deps | [electron.vite.config.ts](../electron.vite.config.ts), [package.json](../package.json) |

### Critical gotcha: SignalR in the main process

Electron 28 ships Node 18 — **no global `WebSocket`**. `@microsoft/signalr` in Node needs a WebSocket implementation or it silently falls back to long-polling. Therefore:

- Add `ws` to `dependencies` and `@types/ws` to `devDependencies`.
- Build the connection with an injected constructor:
  ```ts
  import WebSocket from 'ws'
  new signalR.HubConnectionBuilder()
    .withUrl(`${backendUrl}/socket-api`, { WebSocket: WebSocket as unknown as typeof globalThis.WebSocket })
  ```
  (the cast is expected; signalr's `WebSocketConstructor` type doesn't line up with `ws` types exactly).

Other main-process gotchas:
- Import Electron's `screen` module lazily/only use it **after** `app.whenReady()`.
- `fs.renameSync` overwrites existing files on Windows in modern Node — the temp-file-then-rename save is safe.
- The HTML `<title>Pokemon Dashboard</title>` overrides `BrowserWindow`'s `title` option on load — each window shell must set `document.title` so OBS/taskbar can tell windows apart.

---

## 3. Contracts (exact — implement verbatim)

### 3.1 `src/shared/windowContracts.ts` (NEW)

Shared by main, preload, renderer. Must not import from `src/main` or `src/renderer`.

```ts
export interface WindowBounds { x: number; y: number; width: number; height: number }

export type CardKind = 'admin' | 'recent' | 'phase'

export interface WindowInfo {
  name: string        // stable identity, also the persistence key
  kind: CardKind
  title: string       // human label, e.g. "HGSS — Route 32"
  bounds: WindowBounds // live OS bounds
}

// Mirrors renderer ActiveSubscription (types/api.ts) — duplicated so main never imports renderer code
export interface PhaseWindowRequest { game: string; phase: string; gameName?: string | null }

export interface SharedDashboardConfig {
  gamePhases: PhaseWindowRequest[]
  recentEncounterCount: number
}

export type ConnectionState = 'connected' | 'disconnected' | 'reconnecting'

export interface SignalRSnapshot {
  connectionState: ConnectionState
  dashboardConfig: SharedDashboardConfig
}

// Payload for the encounters broadcast; `response` is the raw GameEncounterResponse (opaque to main)
export interface EncountersPayload { response: unknown; game: string; phase: string }

export const ADMIN_WINDOW = 'admin'
export const RECENT_WINDOW = 'recent'

export const IPC = {
  loadConfig: 'load-config',                       // existing channel, unchanged
  getWindowList: 'get-window-list',                // invoke → WindowInfo[]
  setWindowBounds: 'set-window-bounds',            // invoke (name, Partial<WindowBounds>) → WindowInfo[]
  getSignalRState: 'get-signalr-state',            // invoke → SignalRSnapshot
  windowBoundsChanged: 'window-bounds-changed',    // main → renderers: WindowInfo[]
  signalRConnectionState: 'signalr-connection-state', // main → renderers: ConnectionState
  signalRDashboardConfig: 'signalr-dashboard-config', // main → renderers: SharedDashboardConfig
  signalREncounters: 'signalr-encounters',            // main → renderers: EncountersPayload
  signalRDashboardState: 'signalr-dashboard-state'    // main → renderers: unknown (BotDashboardState)
} as const

export function phaseWindowName(r: PhaseWindowRequest): string {
  const enc = encodeURIComponent
  return `phase:${enc(r.game)}:${enc(r.phase)}:${enc(r.gameName ?? '')}`
}

export function phaseWindowTitle(r: PhaseWindowRequest): string {
  return `${r.game} — ${r.phase}${r.gameName ? ` (${r.gameName})` : ''}`
}
```

### 3.2 `src/shared/gameFlavor.ts` (NEW — moved, not new logic)

Move the entire contents of `src/renderer/src/utils/gameFlavor.ts` here unchanged (main's SignalR client needs `normalizeGameFlavor`). Then replace `src/renderer/src/utils/gameFlavor.ts` with a re-export so `AdminCard.tsx` line 4 keeps working untouched:

```ts
export * from '../../../shared/gameFlavor'
```

### 3.3 `window-layout.json` (persistence file)

Path: `join(app.getPath('userData'), 'window-layout.json')` — per-machine, survives app updates; the bundled `config.json` stays read-only.

```json
{
  "version": 1,
  "windows": {
    "admin":                    { "x": 100, "y": 80,  "width": 820, "height": 900 },
    "recent":                   { "x": 940, "y": 80,  "width": 480, "height": 700 },
    "phase:HGSS:Route%2032:":   { "x": 60,  "y": 400, "width": 640, "height": 520 }
  }
}
```

Rules: unknown/extra keys are preserved forever (that is the "same name → previous settings" requirement); missing or corrupt file → empty map, never a crash; writes go to `window-layout.json.tmp` then `renameSync` over the real file.

### 3.4 Default bounds per kind (when a name has no saved entry)

| kind | width×height | minWidth×minHeight |
|---|---|---|
| admin | 820×900 | 520×480 |
| recent | 480×700 | 360×280 |
| phase | 640×520 | 360×280 |

Position: primary display `workArea` origin + a 32px cascade offset per already-open window.

---

## 4. Main process — file by file

### 4.1 `src/main/layoutStore.ts` (NEW)

```ts
export function loadLayout(): void                    // read+parse file into module memory; call once at startup
export function getSavedBounds(name: string): WindowBounds | undefined
export function setSavedBounds(name: string, b: WindowBounds): void // update memory + debounced (500ms) write
export function flushLayout(): void                   // cancel pending debounce, write synchronously now
```

Implementation notes: module-level `Map<string, WindowBounds>` + one shared debounce timer; validate parsed JSON minimally (each entry has 4 finite numbers, else drop that entry); `version: 1` written but not enforced on read.

### 4.2 `src/main/signalRClient.ts` (NEW — port of renderer `signalRService.ts`)

Port these behaviors 1:1 from the current renderer service (line refs into `signalRService.ts`):

- Builder with exponential backoff `withAutomaticReconnect` (lines 30-40) **plus** the `ws` WebSocket injection from §2.
- `RecieveEncounters` group inference via `pendingSubscriptions` map (lines 42-58) — but instead of calling handler arrays, broadcast `IPC.signalREncounters` with `{ response, game, phase }`.
- `ReceiveDashboardConfig` normalization with `normalizeGameFlavor` + `gameFileName → gameName` mapping (lines 63-73). Then, in order:
  1. cache as `lastDashboardConfig`
  2. subscribe to any new game+phase groups (`SubscribeToGamePhase` invoke, lines 99-105) and drop removed ones from local tracking (lines 107-113)
  3. broadcast `IPC.signalRDashboardConfig`
  4. call `windowManager.syncPhaseWindows(config.gamePhases)`
- `ReceiveDashboardState` → broadcast `IPC.signalRDashboardState` (payload passes through untyped).
- `onreconnecting/onreconnected/onclose` → cache `connectionState` + broadcast `IPC.signalRConnectionState`; on reconnected, re-invoke `SubscribeToGamePhase` for all tracked groups (lines 83-89).

Public surface:

```ts
export function startSignalR(backendUrl: string): void   // fire-and-forget with retry-on-initial-failure (see note)
export function stopSignalR(): Promise<void>
export function getSnapshot(): SignalRSnapshot           // cached connectionState + lastDashboardConfig (empty default)
```

Note on initial connect: the current renderer code only retries after a successful first connect (`withAutomaticReconnect` doesn't cover initial failure). In main, wrap `connection.start()` in a retry loop (5s delay, forever) so launching the dashboard before the backend still converges. Broadcast helper: `BrowserWindow.getAllWindows().forEach(w => w.webContents.send(channel, payload))`.

### 4.3 `src/main/windowManager.ts` (NEW)

Owns `const open = new Map<string, BrowserWindow>()`, `const closedByUser = new Set<string>()`, and a `managerClosing = new Set<string>()` marker used to distinguish user-closes from sync-closes.

```ts
export function bootstrapWindows(config: MainConfig): void
export function syncPhaseWindows(requests: PhaseWindowRequest[]): void
export function listWindows(): WindowInfo[]
export function setWindowBounds(name: string, partial: Partial<WindowBounds>): WindowInfo[]
export function broadcast(channel: string, payload: unknown): void
```

`MainConfig` (define locally in main): `{ backendUrl: string; cards: { phaseCard: {enabled: boolean}; recentPokemonCard: {enabled: boolean}; adminCard: {enabled: boolean} } }` — cast from `loadConfig()`.

**`openCardWindow({ name, kind, title, query })`** (internal):
- If `open.has(name)`: `focus()` existing, return (duplicate-name guard).
- Bounds = `ensureVisible(getSavedBounds(name) ?? defaults(kind, open.size))`.
- `new BrowserWindow({ ...bounds, frame: kind === 'admin', show: false, autoHideMenuBar: true, title, backgroundColor: '#0d0d1a', minWidth, minHeight, webPreferences: { preload: join(__dirname, '../preload/index.js'), sandbox: false, webSecurity: false } })` — preload/sandbox/webSecurity copied from current `createWindow()` (webSecurity off is required for `file://` sprites).
- `ready-to-show` → `show()`. Keep the existing `setWindowOpenHandler` → `shell.openExternal` block.
- Load with query (always include `card`; phase adds `game`, `phase`, and `gameName` only when non-null):
  ```ts
  if (isDev && process.env['ELECTRON_RENDERER_URL']) {
    win.loadURL(`${process.env['ELECTRON_RENDERER_URL']}?${new URLSearchParams(query)}`)
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'), { query })
  }
  ```
- `on('resize')` + `on('move')` → one shared 500ms debounce per window → `setSavedBounds(name, win.getBounds())` + `broadcast(IPC.windowBoundsChanged, listWindows())`.
- `on('closed')` → delete from `open`; if kind is `phase` and name not in `managerClosing`, add to `closedByUser`; remove from `managerClosing`.

**`syncPhaseWindows(requests)`**:
- Early-return unless `config.cards.phaseCard.enabled`.
- `desired = new Map(requests.map(r => [phaseWindowName(r), r]))` (Map dedupes duplicates).
- Close: every open window whose name starts with `phase:` and isn't in `desired` → add name to `managerClosing`, `win.close()`. (Saved bounds stay in the layout file for the future.)
- Prune: `closedByUser` entries not in `desired` are deleted (unsubscribe→resubscribe reopens the window).
- Open: every desired entry that is neither open nor in `closedByUser` → `openCardWindow` with `kind: 'phase'`, `title: phaseWindowTitle(r)`, query `{ card: 'phase', game, phase, ...(gameName ? { gameName } : {}) }`.

**`bootstrapWindows(config)`**: store config module-level; open `admin` (framed, query `{card:'admin'}`, title `Pokemon Dashboard — Admin`) if `cards.adminCard.enabled`; open `recent` (query `{card:'recent'}`, title `Pokemon Dashboard — Recent Encounters`) if `cards.recentPokemonCard.enabled`. Phase windows come later via `syncPhaseWindows` when the main SignalR client receives config. Note: if **neither** admin nor recent is enabled, phase windows will still open — main's own SignalR connection drives them; no hidden helper window is needed. (Edge: if *nothing* is enabled or config leaves zero windows, quit via `window-all-closed` may fire immediately with zero windows open — acceptable, matches a useless config.)

**`setWindowBounds(name, partial)`**: look up window (throw if unknown/destroyed); merged = `ensureVisible({ ...win.getBounds(), ...partial })`; `win.setBounds(merged)`; `setSavedBounds(name, win.getBounds())` + `flushLayout()`; `broadcast(IPC.windowBoundsChanged, listWindows())`; return `listWindows()`.

**`ensureVisible(b)`**: `const wa = screen.getDisplayMatching(b).workArea`; clamp `width/height` to `wa` size (respect per-kind minimums); clamp `x` to `[wa.x - b.width + 120, wa.x + wa.width - 120]` and `y` to `[wa.y, wa.y + wa.height - 40]` so the header strip always remains grabbable.

### 4.4 `src/main/index.ts` (REWRITE)

Keep verbatim: `deepMerge`, `loadConfig`, the `load-config` IPC handler, `app.setAppUserModelId`, the `uncaughtException` dialog, `window-all-closed` → quit.

New `whenReady` body (order matters):
```ts
const config = loadConfig() as MainConfig   // throws → uncaughtException dialog, as today
ipcMain.handle(IPC.loadConfig, () => config)
loadLayout()
ipcMain.handle(IPC.getWindowList, () => listWindows())
ipcMain.handle(IPC.setWindowBounds, (_e, name: unknown, bounds: unknown) => {
  // validate: name is string + open window; bounds object; each present field a finite integer → else throw
  return setWindowBounds(name, sanitizedPartial)
})
ipcMain.handle(IPC.getSignalRState, () => getSnapshot())
startSignalR(config.backendUrl)
bootstrapWindows(config)
```
`before-quit` → `flushLayout()` + `void stopSignalR()`. Delete `createWindow()` and the `activate` re-create handler (replace with `activate` → `bootstrapWindows(config)`; harmless on Windows).

Quit policy result: closing admin alone leaves other windows fully live (main owns SignalR); closing the **last** window quits the app.

---

## 5. Preload + renderer — file by file

### 5.1 `src/preload/index.ts` (REWRITE — exact)

```ts
import { contextBridge, ipcRenderer } from 'electron'
import { IPC } from '../shared/windowContracts'
import type { PhaseWindowRequest, SignalRSnapshot, WindowBounds, WindowInfo } from '../shared/windowContracts'

function on<T>(channel: string) {
  return (cb: (payload: T) => void): (() => void) => {
    const listener = (_e: Electron.IpcRendererEvent, payload: T): void => cb(payload)
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
  onSignalRConnectionState: on<'connected' | 'disconnected' | 'reconnecting'>(IPC.signalRConnectionState),
  onDashboardConfig: on<SharedDashboardConfig>(IPC.signalRDashboardConfig),
  onEncounters: on<EncountersPayload>(IPC.signalREncounters),
  onDashboardState: on<unknown>(IPC.signalRDashboardState)
})
```

### 5.2 `src/renderer/src/types/global.d.ts` (REWRITE)

Type `window.api` with exactly the preload surface above (`AppConfig` for `loadConfig`; import the rest from `../../../../shared/windowContracts` — adjust relative depth: file sits in `src/renderer/src/types/`, so `../../../shared/windowContracts`).

### 5.3 `src/renderer/src/services/ipcSignalRService.ts` (NEW) + delete `signalRService.ts`

Same public API as the old `SignalRService` so `AppContext` and `AdminCard` keep working:

```ts
export class IpcSignalRService {
  constructor()                       // registers the four window.api.on* listeners; they dispatch to handler arrays
  async start(): Promise<void>        // snapshot = await window.api.getSignalRState(); cache + dispatch
                                      // connectionState and dashboardConfig to handlers
  async stop(): Promise<void>         // calls every unsubscribe fn captured in the constructor
  async subscribeToGamePhase(_g: string, _p: string): Promise<void>  // no-op: main owns group membership
  async unsubscribeFromGamePhase(_g: string, _p: string): Promise<void> // no-op
  onEncounters(h: EncountersHandler): () => void        // same handler types as the old service
  onDashboardConfig(h: DashboardConfigHandler): () => void
  onDashboardState(h: DashboardStateHandler): () => void
  onConnectionState(h: ConnectionStateHandler): () => void
  getConnectionState(): string
}
```

Details: the encounters IPC payload `{ response, game, phase }` is dispatched as `h(response as GameEncounterResponse, game, phase)`; `SharedDashboardConfig` is structurally identical to renderer `DashboardConfig` — assign directly. Registration order in `AppContext` (handlers first, then `start()`) plus full-state config pushes means the snapshot/event race is benign.

### 5.4 `src/renderer/src/context/AppContext.tsx` (EDIT — minimal)

- Line 2: import `IpcSignalRService` instead of `SignalRService`; line 19/29-36/66: type becomes `IpcSignalRService | null`.
- Line 90: `const service = new IpcSignalRService()` (constructor no longer takes `backendUrl`).
- Everything else — subscriptions diffing, REST seeding, recentEncounters trimming — unchanged. The existing `service.subscribeToGamePhase(...)` call at line 152 stays (now a harmless no-op).

### 5.5 `src/renderer/src/App.tsx` (REWRITE — exact)

```tsx
import React from 'react'
import './assets/index.css'
import { AppProvider } from './context/AppContext'
import { AdminWindow } from './windows/AdminWindow'
import { RecentWindow } from './windows/RecentWindow'
import { PhaseWindow } from './windows/PhaseWindow'

export default function App(): JSX.Element {
  const params = new URLSearchParams(window.location.search)
  const card = params.get('card') ?? 'admin'
  return (
    <AppProvider>
      {card === 'admin' && <AdminWindow />}
      {card === 'recent' && <RecentWindow />}
      {card === 'phase' && (
        <PhaseWindow
          game={params.get('game') ?? ''}
          phase={params.get('phase') ?? ''}
          gameName={params.get('gameName') || null}
        />
      )}
    </AppProvider>
  )
}
```

Deleted with this rewrite: `GridLayout` import + both react-grid-layout/react-resizable CSS imports, `buildDefaultLayout`, the top bar, the `windowWidth` resize listener.

### 5.6 Window shells — `src/renderer/src/windows/` (NEW ×3) + chrome helpers

`AdminWindow.tsx`: `useEffect(() => { document.title = 'Pokemon Dashboard — Admin' }, [])`; render `<div className="window-root h-screen"><AdminCard /></div>`.

`RecentWindow.tsx`: sets `document.title = 'Pokemon Dashboard — Recent Encounters'`; renders `<div className="window-root h-screen"><RecentPokemonCard windowChrome /></div>`.

`PhaseWindow.tsx` (`props: { game, phase, gameName }`): sets `document.title = phaseWindowTitle(props)` (import from shared). Find the subscription: exact triple match on `(game, phase, gameName)` first, else fall back to `(game, phase)`. If found → `<PhaseCard subscription={sub} windowChrome />`; else render a placeholder that is still draggable/closable:

```tsx
<div className="dashboard-card h-full">
  <div className="card-header window-drag-region">
    <span className="card-header-title">{props.phase}</span>
    <WindowCloseButton />
  </div>
  <div className="flex-1 flex items-center justify-center text-slate-500 text-sm">Connecting…</div>
</div>
```

`src/renderer/src/components/WindowChrome.tsx` (NEW): `export function WindowCloseButton()` → `<button className="window-no-drag text-slate-500 hover:text-white text-sm px-1" title="Close window" onClick={() => window.close()}>✕</button>`.

**CSS** ([index.css](../src/renderer/src/assets/index.css)):
- Delete the react-grid-layout block (lines 144-182).
- Add:
  ```css
  .window-drag-region { -webkit-app-region: drag; }
  .window-drag-region button, .window-drag-region a,
  .window-drag-region input, .window-drag-region select,
  .window-no-drag { -webkit-app-region: no-drag; }

  /* Frameless windows: card fills the window, no rounded corners/shadow.
     NB: must be a descendant rule — .dashboard-card is declared after
     @tailwind utilities, so a `rounded-none` utility would lose. */
  .window-root .dashboard-card { border-radius: 0; box-shadow: none; height: 100%; }
  ```

**`PhaseCard.tsx`** (EDIT): add `windowChrome?: boolean` to props. When set: header div (line 64) gains `window-drag-region`; the right-side stats div (lines 69-76) additionally renders a status dot (`<span className={`status-dot ${connectionState}`} />` — pull `connectionState` from the existing `useAppContext()` call) and `<WindowCloseButton />`.

**`RecentPokemonCard.tsx`** (EDIT): add `windowChrome?: boolean`. Header (lines 99-102) gains `window-drag-region`; right side becomes a flex group with the existing count, a status dot, and `<WindowCloseButton />`.

### 5.7 `src/renderer/src/components/WindowLayoutTab.tsx` (NEW) + `AdminCard.tsx` (EDIT)

`WindowLayoutTab` — the pixel-config UI:

- State: `windows: WindowInfo[]`; `drafts: Record<string /*name*/, Partial<Record<'x'|'y'|'width'|'height', string>>>` (strings, so inputs stay controlled while typing).
- Mount: `window.api.getWindowList().then(setWindows)`; subscribe `window.api.onWindowBoundsChanged(setWindows)`; return the unsubscribe from the effect.
- Render a row per window (admin included): `title` + `name` (small/dim), four `<input type="number">`. Input value = `drafts[name]?.[field] ?? String(bounds[field])` — untouched fields track live drags; touched fields hold the draft (no clobbering mid-typing).
- Buttons per row: **Apply** — parse drafted fields to integers (skip NaN/empty), `await window.api.setWindowBounds(name, parsed)`, then `setWindows(result)` and clear that row's draft; **Reset** — clear the row's draft. Show a row error string on IPC rejection.
- Footer hint: "Positions and sizes auto-save when you move or resize windows. Closed phase windows aren't listed."

`AdminCard.tsx` edits:
1. Line 6: `type TabId = 'config' | 'botControl' | 'phaseEditor' | 'windows'`.
2. Lines 8-12: append `{ id: 'windows', label: 'Window Layout' }`.
3. Lines 1280-1287: add `{activeTab === 'windows' && <WindowLayoutTab />}` (+ import).
4. Header (lines 1256-1265): add `<ConnectionStatus />` next to the refresh button inside a flex group — the top bar that used to host it is gone.

---

## 6. Dependency + config changes

- `package.json`: **remove** `react-grid-layout` (deps) and `@types/react-grid-layout` (devDeps); **add** `ws` (deps) and `@types/ws` (devDeps). Then `npm install`.
- `tsconfig.node.json` include → `["electron.vite.config.*", "src/main/**/*", "src/preload/**/*", "src/shared/**/*"]`.
- `tsconfig.web.json` include → `["src/renderer/src/**/*", "src/shared/**/*"]`.
- `react-flip-move` stays (RecentPokemonCard uses it). `config.json` format is untouched; `cards.*.enabled` flags now gate window creation.

---

## 7. Milestones (execute in order; checkpoint after each)

Checkpoint command (from `PokemonDashboard/`): `npx tsc --build --force` — must exit clean.

1. **M1 — Shared layer**: §3.1, §3.2, tsconfig includes. *(Checkpoint.)*
2. **M2 — Main process**: `npm i` with §6 dep changes first; then `layoutStore.ts` → `signalRClient.ts` → `windowManager.ts` → rewrite `index.ts`. *(Checkpoint; `npm run dev` should open admin+recent windows — each still renders the old full grid; that's expected mid-migration.)*
3. **M3 — Preload + types**: §5.1, §5.2. *(Checkpoint.)*
4. **M4 — Renderer transport**: §5.3 new service, §5.4 AppContext swap, delete `services/signalRService.ts`. *(Checkpoint; dev app now shows live data again, still grid-rendered.)*
5. **M5 — Routing + shells**: §5.5, §5.6 shells + WindowChrome. *(Checkpoint; each window now renders exactly one card.)*
6. **M6 — Chrome polish**: CSS additions/deletions, `windowChrome` props in both cards. *(Checkpoint.)*
7. **M7 — Admin tab**: §5.7. *(Checkpoint.)*
8. **M8 — Cleanup + full verification**: confirm no `react-grid-layout` references remain (`grep`), run §8.

---

## 8. Verification (manual, `npm run dev` with the C# backend running)

1. Launch → admin (native frame) + recent (frameless) open. Backend logs show **one** SignalR connection for the whole app.
2. Once config arrives, one frameless window per subscription appears, titled `"{game} — {phase}"`.
3. Drag/resize any window → after ~0.5s `%APPDATA%/pokemon-dashboard/window-layout.json` updates → restart the app → every window reopens at its exact saved x/y/width/height.
4. Admin → Dashboard Config: add a subscription → its window opens; remove it → the window closes; its entry stays in the layout file; re-adding reopens it at the same spot (the "same name → previous settings" requirement).
5. Admin → Window Layout: all open windows listed; dragging another window live-updates its row; a field being typed in is not clobbered; Apply with width `800` snaps the OS window instantly and survives restart.
6. Frameless cards: header drags the window; ✕ closes it; a phase window closed by hand stays closed on subsequent config pushes; unsubscribe + resubscribe reopens it.
7. Close admin → other windows keep streaming encounters (proves the main-process connection). Close all windows → app exits (no zombie process in Task Manager).
8. Hand-edit the layout file to `x: 20000` for one window → relaunch → window is clamped onto a visible display.
9. Kill the backend → status dots turn red/amber; restart backend → auto-reconnect, config re-push, **no duplicate windows**.
10. `npm run build && npm run dist` → run the installer output: query routing works from `loadFile`, sprites render (webSecurity off preserved), `ws`/`@microsoft/signalr` present in packaged resources, layout file lands in the installed app's userData.

## 9. Out of scope (recorded, not built)

- Persisting dashboard subscriptions across backend restarts (backend `AdminService` state is in-memory — separate work).
- Per-window zoom/scale factor for stream text size.
- "Reopen closed phase window" button in the admin card (workaround: unsubscribe/resubscribe or restart).
- Always-on-top / OBS-specific window flags.
