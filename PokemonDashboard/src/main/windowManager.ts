import { BrowserWindow, screen, shell } from 'electron'
import { join } from 'path'
import { flushLayout, getSavedBounds, setSavedBounds } from './layoutStore'
import {
  ADMIN_WINDOW,
  IPC,
  RECENT_WINDOW,
  phaseWindowName,
  phaseWindowTitle,
  pickupWindowName,
  pickupWindowTitle,
  type CardKind,
  type PhaseWindowRequest,
  type PickupTally,
  type WindowBounds,
  type WindowInfo
} from '../shared/windowContracts'

const isDev = process.env.NODE_ENV === 'development'

export interface MainConfig {
  backendUrl: string
  apiUrl: string
  spritesRootPath: string
  cards: {
    phaseCard: { enabled: boolean }
    recentPokemonCard: { enabled: boolean }
    adminCard: { enabled: boolean }
  }
}

interface OpenWindow {
  win: BrowserWindow
  kind: CardKind
  title: string
}

const open = new Map<string, OpenWindow>()
const closedByUser = new Set<string>()
const managerClosing = new Set<string>()
const resizeDebounceTimers = new Map<string, NodeJS.Timeout>()
let mainConfig: MainConfig | null = null

// The Pickup items state below is touched only on the main-process event loop (IPC handlers
// and SignalR callbacks), so it needs no lock.
let pickupWindowsOn = false
const pickupTallies = new Map<string, PickupTally>()
const closedPickupByUser = new Set<string>()

const DEFAULTS: Record<CardKind, { width: number; height: number; minWidth: number; minHeight: number }> = {
  admin: { width: 820, height: 900, minWidth: 520, minHeight: 480 },
  recent: { width: 480, height: 700, minWidth: 360, minHeight: 280 },
  phase: { width: 640, height: 520, minWidth: 360, minHeight: 280 },
  pickup: { width: 360, height: 420, minWidth: 280, minHeight: 200 }
}

export function broadcast(channel: string, payload: unknown): void {
  for (const w of BrowserWindow.getAllWindows()) {
    if (!w.isDestroyed()) {
      w.webContents.send(channel, payload)
    }
  }
}

function ensureVisible(bounds: WindowBounds, kind: CardKind): WindowBounds {
  const defaults = DEFAULTS[kind]
  const display = screen.getDisplayMatching(bounds)
  const wa = display.workArea

  const width = Math.min(Math.max(bounds.width, defaults.minWidth), wa.width)
  const height = Math.min(Math.max(bounds.height, defaults.minHeight), wa.height)

  const minX = wa.x - width + 120
  const maxX = wa.x + wa.width - 120
  const minY = wa.y
  const maxY = wa.y + wa.height - 40

  const x = Math.min(Math.max(bounds.x, minX), maxX)
  const y = Math.min(Math.max(bounds.y, minY), maxY)

  return { x, y, width, height }
}

function defaultBounds(kind: CardKind): WindowBounds {
  const defaults = DEFAULTS[kind]
  const wa = screen.getPrimaryDisplay().workArea
  const cascade = open.size * 32
  return {
    x: wa.x + 40 + cascade,
    y: wa.y + 40 + cascade,
    width: defaults.width,
    height: defaults.height
  }
}

export function listWindows(): WindowInfo[] {
  const result: WindowInfo[] = []
  for (const [name, { win, kind, title }] of open) {
    if (win.isDestroyed()) continue
    result.push({ name, kind, title, bounds: win.getBounds() })
  }
  return result
}

function scheduleSaveBounds(name: string, win: BrowserWindow): void {
  const existing = resizeDebounceTimers.get(name)
  if (existing) clearTimeout(existing)
  resizeDebounceTimers.set(
    name,
    setTimeout(() => {
      resizeDebounceTimers.delete(name)
      if (win.isDestroyed()) return
      setSavedBounds(name, win.getBounds())
      broadcast(IPC.windowBoundsChanged, listWindows())
    }, 500)
  )
}

interface OpenCardWindowOptions {
  name: string
  kind: CardKind
  title: string
  query: Record<string, string>
}

function openCardWindow({ name, kind, title, query }: OpenCardWindowOptions): void {
  const existing = open.get(name)
  if (existing && !existing.win.isDestroyed()) {
    existing.win.focus()
    return
  }

  const defaults = DEFAULTS[kind]
  const bounds = ensureVisible(getSavedBounds(name) ?? defaultBounds(kind), kind)

  const win = new BrowserWindow({
    ...bounds,
    minWidth: defaults.minWidth,
    minHeight: defaults.minHeight,
    show: false,
    frame: kind === 'admin',
    autoHideMenuBar: true,
    title,
    backgroundColor: '#0d0d1a',
    webPreferences: {
      preload: join(__dirname, '../preload/index.js')
    }
  })

  open.set(name, { win, kind, title })
  broadcast(IPC.windowBoundsChanged, listWindows())

  win.on('ready-to-show', () => {
    win.show()
  })

  win.webContents.setWindowOpenHandler((details) => {
    shell.openExternal(details.url)
    return { action: 'deny' }
  })

  win.on('resize', () => scheduleSaveBounds(name, win))
  win.on('move', () => scheduleSaveBounds(name, win))

  win.on('closed', () => {
    open.delete(name)
    const timer = resizeDebounceTimers.get(name)
    if (timer) {
      clearTimeout(timer)
      resizeDebounceTimers.delete(name)
    }
    if (kind === 'phase' && !managerClosing.has(name)) {
      closedByUser.add(name)
    }
    if (kind === 'pickup' && !managerClosing.has(name)) {
      closedPickupByUser.add(name)
    }
    managerClosing.delete(name)
    broadcast(IPC.windowBoundsChanged, listWindows())
  })

  const qs = new URLSearchParams(query).toString()
  if (isDev && process.env['ELECTRON_RENDERER_URL']) {
    win.loadURL(`${process.env['ELECTRON_RENDERER_URL']}?${qs}`)
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'), { query })
  }
}

export function bootstrapWindows(config: MainConfig): void {
  mainConfig = config

  if (config.cards.adminCard.enabled) {
    openCardWindow({
      name: ADMIN_WINDOW,
      kind: 'admin',
      title: 'Pokemon Dashboard — Admin',
      query: { card: 'admin' }
    })
  }

  if (config.cards.recentPokemonCard.enabled) {
    openCardWindow({
      name: RECENT_WINDOW,
      kind: 'recent',
      title: 'Pokemon Dashboard — Recent Encounters',
      query: { card: 'recent' }
    })
  }
}

export function syncPhaseWindows(requests: PhaseWindowRequest[]): void {
  if (!mainConfig?.cards.phaseCard.enabled) return

  const desired = new Map(requests.map((r) => [phaseWindowName(r), r]))

  for (const [name, { win, kind }] of open) {
    if (kind === 'phase' && !desired.has(name)) {
      managerClosing.add(name)
      if (!win.isDestroyed()) win.close()
    }
  }

  for (const name of closedByUser) {
    if (!desired.has(name)) closedByUser.delete(name)
  }

  for (const [name, request] of desired) {
    if (open.has(name) || closedByUser.has(name)) continue
    openCardWindow({
      name,
      kind: 'phase',
      title: phaseWindowTitle(request),
      query: {
        card: 'phase',
        game: request.game,
        phase: request.phase,
        ...(request.gameName ? { gameName: request.gameName } : {})
      }
    })
  }
}

export function setPickupWindowsOn(on: boolean, tallies: PickupTally[] = []): void {
  pickupWindowsOn = on
  for (const tally of tallies) pickupTallies.set(tally.gameFileName, tally)
  syncPickupWindows()
}

export function getPickupWindowsOn(): boolean {
  return pickupWindowsOn
}

export function receivePickupTally(tally: PickupTally): void {
  pickupTallies.set(tally.gameFileName, tally)
  broadcast(IPC.pickupItems, tally)
  syncPickupWindows()
}

export function getPickupTally(gameFileName: string): PickupTally | null {
  return pickupTallies.get(gameFileName) ?? null
}

function syncPickupWindows(): void {
  const desired = new Set(pickupWindowsOn ? [...pickupTallies.keys()].map(pickupWindowName) : [])

  for (const [name, { win, kind }] of open) {
    if (kind === 'pickup' && !desired.has(name)) {
      managerClosing.add(name)
      if (!win.isDestroyed()) win.close()
    }
  }

  if (!pickupWindowsOn) {
    closedPickupByUser.clear()
    return
  }

  for (const gameFileName of pickupTallies.keys()) {
    const name = pickupWindowName(gameFileName)
    if (open.has(name) || closedPickupByUser.has(name)) continue
    openCardWindow({
      name,
      kind: 'pickup',
      title: pickupWindowTitle(gameFileName),
      query: { card: 'pickup', game: gameFileName }
    })
  }
}

export function setWindowBounds(name: string, partial: Partial<WindowBounds>): WindowInfo[] {
  const entry = open.get(name)
  if (!entry || entry.win.isDestroyed()) {
    throw new Error(`No open window named "${name}"`)
  }

  const merged = ensureVisible({ ...entry.win.getBounds(), ...partial }, entry.kind)
  entry.win.setBounds(merged)
  setSavedBounds(name, entry.win.getBounds())
  flushLayout()
  broadcast(IPC.windowBoundsChanged, listWindows())
  return listWindows()
}
