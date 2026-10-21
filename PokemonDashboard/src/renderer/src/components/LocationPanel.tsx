import React, { useCallback, useEffect, useState } from 'react'
import { useAppContext } from '../context/AppContext'
import type { BotDashboardState, LocationInfo } from '../types/api'

/**
 * On-demand player-position readout for authoring navigation steps.
 *
 * Reads over the backend's memory path, which is independent of the command server, so it works
 * while a macro recording or a bot event is in progress. Self-contained: its own emulator
 * selection, sharing no state with the macro recorder.
 */
export function LocationPanel(): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  const [botState, setBotState] = useState<BotDashboardState>({})
  const [selectedPos, setSelectedPos] = useState<number | null>(null)
  const [location, setLocation] = useState<LocationInfo | null>(null)
  const [reading, setReading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const positions = Object.keys(botState)
    .map(Number)
    .sort((a, b) => a - b)

  const loadBots = useCallback(async (): Promise<void> => {
    if (!apiUrl) return
    try {
      const r = await fetch(`${apiUrl}/api/bot/getDashboardState`)
      if (!r.ok) throw new Error(`Server returned ${r.status}`)
      setBotState((await r.json()) as BotDashboardState)
    } catch (err) {
      console.error('Failed to load emulator list:', err)
    }
  }, [apiUrl])

  useEffect(() => {
    loadBots()
  }, [loadBots])

  useEffect(() => {
    setSelectedPos((prev) => (prev !== null && positions.includes(prev) ? prev : positions[0] ?? null))
  }, [botState])

  // Values belong to the emulator they were read from — drop them on switch so a stale reading
  // can't be mistaken for the newly selected instance.
  useEffect(() => {
    setLocation(null)
    setError(null)
  }, [selectedPos])

  const read = async (): Promise<void> => {
    if (selectedPos === null) return
    setReading(true)
    try {
      const r = await fetch(`${apiUrl}/api/dev/location/${selectedPos}`)
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      setLocation((await r.json()) as LocationInfo)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to read location')
    } finally {
      setReading(false)
    }
  }

  const copy = async (): Promise<void> => {
    if (!location) return
    const text =
      `map=${location.mapId} botMap=${location.botMapId ?? '?'} ` +
      `x=${location.x} y=${location.y} facing=${location.facingName}`
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      /* clipboard blocked — the values are on screen anyway */
    }
  }

  const noBots = positions.length === 0

  return (
    <div className="p-3 space-y-3 border-b border-slate-700/50">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-slate-200">Location</span>
        <button
          onClick={loadBots}
          title="Refresh emulator list"
          className="px-1 text-slate-400 hover:text-white text-sm"
        >
          ↻
        </button>
      </div>

      <div className="text-xs text-slate-400">
        Game map ID is what pathfinding and the <code>M.</code> warp guard use. Bot map ID is the
        shared layout key — several locations can report the same one.
      </div>

      <div className="flex gap-2">
        <select
          value={selectedPos ?? ''}
          disabled={noBots}
          onChange={(e) => setSelectedPos(Number(e.target.value))}
          className="flex-1 px-2 py-1.5 bg-slate-800 border border-slate-700 rounded text-sm text-slate-200 disabled:opacity-50"
        >
          {noBots && <option value="">No emulators running</option>}
          {positions.map((pos) => (
            <option key={pos} value={pos}>
              {pos} — {botState[pos]?.name ?? 'unknown'}
            </option>
          ))}
        </select>

        <button
          onClick={read}
          disabled={reading || noBots || selectedPos === null}
          className="px-3 py-1.5 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors font-medium"
        >
          {reading ? 'Reading…' : 'Read'}
        </button>
      </div>

      {location && (
        <div className="space-y-2">
          <div className="grid grid-cols-3 gap-2">
            <Stat label="Game Map ID" value={location.mapId} />
            <Stat label="Bot Map ID" value={location.botMapId ?? '—'} />
            <Stat label="Internal Name" value={location.internalName ?? '—'} />
            <Stat label="X" value={location.x} />
            <Stat label="Y" value={location.y} />
            <Stat label="Facing" value={location.facingName} />
          </div>

          <div className="flex items-center justify-between">
            <span className="text-xs text-slate-500">
              {location.botMapId === null ? 'Game map ID is not in the lookup table' : ''}
            </span>
            <button
              onClick={copy}
              className="px-2 py-1 bg-slate-700 hover:bg-slate-600 text-white text-xs rounded transition-colors"
            >
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
        </div>
      )}

      {error && (
        <div className="text-xs rounded px-3 py-2 text-red-400 bg-red-900/20 border border-red-700/30">
          {error}
        </div>
      )}
    </div>
  )
}

function Stat({ label, value }: { label: string; value: number | string }): JSX.Element {
  return (
    <div className="bg-slate-800 border border-slate-700 rounded px-2 py-1.5">
      <div className="text-[10px] uppercase tracking-wide text-slate-500">{label}</div>
      <div className="text-sm text-slate-100 font-mono truncate">{value}</div>
    </div>
  )
}
