import React, { useCallback, useEffect, useRef, useState } from 'react'
import { useAppContext } from '../context/AppContext'
import type { BotDashboardState, MacroRecordResult, MacroRecordStatus } from '../types/api'

const STATUS_POLL_MS = 1000

/**
 * Dev-mode macro recorder. Puts an emulator into a mode where ApiHawk stops driving the joypad and
 * captures the human's per-frame input instead, then renders the result as macro YAML to copy into
 * ShinyBotHunter/Macro/Configs.
 *
 * Only mounted when GET /api/metadata reports macroRecordingEnabled; the backend enforces the flag
 * independently, so a stale window cannot reach the endpoints.
 */
export function MacroRecordTab(): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  const [botState, setBotState] = useState<BotDashboardState>({})
  const [selectedPos, setSelectedPos] = useState<number | null>(null)
  const [macroName, setMacroName] = useState('RecordedMacro')

  const [recording, setRecording] = useState(false)
  const [frameCount, setFrameCount] = useState(0)
  const [busy, setBusy] = useState(false)
  const [yaml, setYaml] = useState('')
  const [copied, setCopied] = useState(false)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)

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

  // Default to the first emulator once the list arrives, and drop a selection that went away.
  useEffect(() => {
    setSelectedPos((prev) => (prev !== null && positions.includes(prev) ? prev : positions[0] ?? null))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [botState])

  // Live frame counter while recording. Kept in a ref so the interval can be torn down from
  // anywhere without re-creating it on every count update.
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const stopPolling = useCallback((): void => {
    if (pollRef.current !== null) {
      clearInterval(pollRef.current)
      pollRef.current = null
    }
  }, [])

  useEffect(() => {
    if (!recording || selectedPos === null || !apiUrl) {
      stopPolling()
      return
    }

    pollRef.current = setInterval(() => {
      fetch(`${apiUrl}/api/dev/macroRecord/status/${selectedPos}`)
        .then((r) => (r.ok ? r.json() : null))
        .then((s: MacroRecordStatus | null) => {
          if (s) setFrameCount(s.frameCount)
        })
        .catch(() => {
          /* transient — the next tick retries */
        })
    }, STATUS_POLL_MS)

    return stopPolling
  }, [recording, selectedPos, apiUrl, stopPolling])

  const start = async (): Promise<void> => {
    if (selectedPos === null) return
    setBusy(true)
    setFeedback(null)
    setYaml('')
    setFrameCount(0)
    try {
      const r = await fetch(`${apiUrl}/api/dev/macroRecord/start/${selectedPos}`, { method: 'POST' })
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      setRecording(true)
      setFeedback({ msg: `Recording emulator ${selectedPos}. Play the game, then press Stop.`, isError: false })
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Failed to start recording', isError: true })
    } finally {
      setBusy(false)
    }
  }

  const stop = async (): Promise<void> => {
    if (selectedPos === null) return
    setBusy(true)
    stopPolling()
    try {
      const r = await fetch(`${apiUrl}/api/dev/macroRecord/stop/${selectedPos}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: macroName })
      })
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      const result = (await r.json()) as MacroRecordResult
      setYaml(result.yaml)
      setFrameCount(result.frameCount)
      setFeedback({
        msg: `Captured ${result.frameCount} frames into ${result.stepCount} steps.`,
        isError: false
      })
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Failed to stop recording', isError: true })
    } finally {
      setRecording(false)
      setBusy(false)
    }
  }

  const copy = async (): Promise<void> => {
    try {
      await navigator.clipboard.writeText(yaml)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      setFeedback({ msg: 'Clipboard write was blocked — select the text and copy manually.', isError: true })
    }
  }

  const noBots = positions.length === 0

  return (
    <div className="p-3 space-y-3">
      <div className="text-xs text-slate-400">
        Records the real controller and touch input on a running emulator and turns it into macro
        YAML. The bot stops driving that emulator while recording.
      </div>

      {/* Target + name */}
      <div className="flex gap-2">
        <div className="flex-1">
          <label className="block text-xs text-slate-400 mb-1">Emulator</label>
          <select
            value={selectedPos ?? ''}
            disabled={recording || noBots}
            onChange={(e) => setSelectedPos(Number(e.target.value))}
            className="w-full px-2 py-1.5 bg-slate-800 border border-slate-700 rounded text-sm text-slate-200 disabled:opacity-50"
          >
            {noBots && <option value="">No emulators running</option>}
            {positions.map((pos) => (
              <option key={pos} value={pos}>
                {pos} — {botState[pos]?.name ?? 'unknown'}
              </option>
            ))}
          </select>
        </div>

        <div className="flex-1">
          <label className="block text-xs text-slate-400 mb-1">Macro name</label>
          <input
            type="text"
            value={macroName}
            disabled={recording}
            onChange={(e) => setMacroName(e.target.value)}
            className="w-full px-2 py-1.5 bg-slate-800 border border-slate-700 rounded text-sm text-slate-200 disabled:opacity-50"
          />
        </div>

        <div className="flex items-end">
          <button
            onClick={loadBots}
            disabled={recording}
            title="Refresh emulator list"
            className="px-2 py-1.5 text-slate-400 hover:text-white disabled:opacity-50 text-sm"
          >
            ↻
          </button>
        </div>
      </div>

      {/* Transport */}
      <div className="flex items-center gap-3">
        {!recording ? (
          <button
            onClick={start}
            disabled={busy || noBots || selectedPos === null}
            className="px-3 py-2 bg-red-600 hover:bg-red-500 disabled:opacity-50 text-white text-sm rounded transition-colors font-medium"
          >
            ● Record
          </button>
        ) : (
          <button
            onClick={stop}
            disabled={busy}
            className="px-3 py-2 bg-slate-600 hover:bg-slate-500 disabled:opacity-50 text-white text-sm rounded transition-colors font-medium"
          >
            ■ Stop
          </button>
        )}

        {recording && (
          <span className="text-xs text-red-400 animate-pulse">
            Recording — {frameCount} frames
          </span>
        )}
      </div>

      {/* Result */}
      {yaml && (
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-xs text-slate-400">
              Paste into ShinyBotHunter/Macro/Configs/HGSS/ and restart the backend.
            </span>
            <button
              onClick={copy}
              className="px-2 py-1 bg-indigo-600 hover:bg-indigo-500 text-white text-xs rounded transition-colors"
            >
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
          <textarea
            readOnly
            value={yaml}
            spellCheck={false}
            rows={16}
            onFocus={(e) => e.currentTarget.select()}
            className="w-full px-2 py-2 bg-slate-900 border border-slate-700 rounded text-xs text-slate-200 font-mono resize-y"
          />
        </div>
      )}

      {/* Feedback */}
      {feedback && (
        <div
          className={`text-xs rounded px-3 py-2 ${
            feedback.isError
              ? 'text-red-400 bg-red-900/20 border border-red-700/30'
              : 'text-green-400 bg-green-900/20 border border-green-700/30'
          }`}
        >
          {feedback.msg}
        </div>
      )}
    </div>
  )
}
