import { app } from 'electron'
import { existsSync, readFileSync, renameSync, writeFileSync } from 'fs'
import { join } from 'path'
import type { WindowBounds } from '../shared/windowContracts'

const layout = new Map<string, WindowBounds>()
let debounceTimer: NodeJS.Timeout | null = null

function layoutFilePath(): string {
  return join(app.getPath('userData'), 'window-layout.json')
}

function isFiniteBounds(b: unknown): b is WindowBounds {
  if (!b || typeof b !== 'object') return false
  const { x, y, width, height } = b as Record<string, unknown>
  return [x, y, width, height].every((v) => typeof v === 'number' && Number.isFinite(v))
}

export function loadLayout(): void {
  layout.clear()
  const path = layoutFilePath()
  if (!existsSync(path)) return

  try {
    const raw = readFileSync(path, 'utf-8')
    const parsed = JSON.parse(raw) as { windows?: Record<string, unknown> }
    const windows = parsed.windows ?? {}
    for (const [name, bounds] of Object.entries(windows)) {
      if (isFiniteBounds(bounds)) {
        layout.set(name, bounds)
      }
    }
  } catch {
    // Corrupt or unreadable file — start with an empty layout rather than crashing.
  }
}

export function getSavedBounds(name: string): WindowBounds | undefined {
  return layout.get(name)
}

function writeLayoutFile(): void {
  const path = layoutFilePath()
  const tmpPath = `${path}.tmp`
  const data = {
    version: 1,
    windows: Object.fromEntries(layout)
  }
  writeFileSync(tmpPath, JSON.stringify(data, null, 2), 'utf-8')
  renameSync(tmpPath, path)
}

export function setSavedBounds(name: string, bounds: WindowBounds): void {
  layout.set(name, bounds)
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    debounceTimer = null
    writeLayoutFile()
  }, 500)
}

export function flushLayout(): void {
  if (debounceTimer) {
    clearTimeout(debounceTimer)
    debounceTimer = null
  }
  writeLayoutFile()
}
