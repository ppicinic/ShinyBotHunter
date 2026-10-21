import { app, ipcMain, dialog, net, protocol } from 'electron'
import { isAbsolute, join, relative, resolve } from 'path'
import { pathToFileURL } from 'url'
import { readFileSync, existsSync } from 'fs'
import { flushLayout, loadLayout } from './layoutStore'
import { getSnapshot, startSignalR, stopSignalR } from './signalRClient'
import { bootstrapWindows, listWindows, setWindowBounds, type MainConfig } from './windowManager'
import { IPC, type WindowBounds } from '../shared/windowContracts'

const isDev = process.env.NODE_ENV === 'development'

// Sprites are served as sprite://local/<subdir>/<name>.png; must be registered before app ready.
protocol.registerSchemesAsPrivileged([{ scheme: 'sprite', privileges: { standard: true, secure: true } }])

function deepMerge(base: Record<string, unknown>, override: Record<string, unknown>): Record<string, unknown> {
  const result: Record<string, unknown> = { ...base }
  for (const key of Object.keys(override)) {
    const baseVal = base[key]
    const overrideVal = override[key]
    if (
      overrideVal !== null &&
      typeof overrideVal === 'object' &&
      !Array.isArray(overrideVal) &&
      baseVal !== null &&
      typeof baseVal === 'object' &&
      !Array.isArray(baseVal)
    ) {
      result[key] = deepMerge(
        baseVal as Record<string, unknown>,
        overrideVal as Record<string, unknown>
      )
    } else {
      result[key] = overrideVal
    }
  }
  return result
}

function loadConfig(): MainConfig {
  const configDir = isDev ? app.getAppPath() : process.resourcesPath

  const configPath = join(configDir, 'config.json')
  const raw = readFileSync(configPath, 'utf-8') // throws if missing
  let config = JSON.parse(raw) as Record<string, unknown> // throws if invalid JSON

  const overridePath = join(configDir, 'override-config.json')
  if (existsSync(overridePath)) {
    const overrideRaw = readFileSync(overridePath, 'utf-8')
    const override = JSON.parse(overrideRaw) as Record<string, unknown>
    config = deepMerge(config, override)
  }

  return config as unknown as MainConfig
}

function sanitizeBoundsPartial(input: unknown): Partial<WindowBounds> {
  if (!input || typeof input !== 'object') return {}
  const result: Partial<WindowBounds> = {}
  for (const key of ['x', 'y', 'width', 'height'] as const) {
    const value = (input as Record<string, unknown>)[key]
    if (typeof value === 'number' && Number.isFinite(value)) {
      result[key] = Math.round(value)
    }
  }
  return result
}

app.whenReady().then(() => {
  app.setAppUserModelId('com.pokemon.dashboard')

  const config = loadConfig()

  // Renderer-supplied URLs: only files under spritesRootPath are served.
  const spriteRoot = resolve(config.spritesRootPath)
  protocol.handle('sprite', (request) => {
    const file = resolve(spriteRoot, '.' + decodeURIComponent(new URL(request.url).pathname))
    const rel = relative(spriteRoot, file)
    if (rel.startsWith('..') || isAbsolute(rel)) return new Response(null, { status: 403 })
    return net.fetch(pathToFileURL(file).toString())
  })

  // IPC: Load static config.json — fails the app if config is missing or invalid
  ipcMain.handle(IPC.loadConfig, () => config)

  loadLayout()

  ipcMain.handle(IPC.getWindowList, () => listWindows())

  ipcMain.handle(IPC.setWindowBounds, (_event, name: unknown, bounds: unknown) => {
    if (typeof name !== 'string') throw new Error('Window name must be a string')
    return setWindowBounds(name, sanitizeBoundsPartial(bounds))
  })

  ipcMain.handle(IPC.getSignalRState, () => getSnapshot())

  startSignalR(config.backendUrl)
  bootstrapWindows(config)

  app.on('activate', () => {
    bootstrapWindows(config)
  })
})

app.on('before-quit', () => {
  flushLayout()
  void stopSignalR()
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

// Catch unhandled errors and show a dialog before quitting
process.on('uncaughtException', (err) => {
  dialog.showErrorBox('Fatal Error', `${err.message}\n\n${err.stack ?? ''}`)
  app.quit()
})
