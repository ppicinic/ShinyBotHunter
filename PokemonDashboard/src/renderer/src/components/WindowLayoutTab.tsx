import React, { useEffect, useState } from 'react'
import type { WindowBounds, WindowInfo } from '../../../shared/windowContracts'

type BoundsField = 'x' | 'y' | 'width' | 'height'
const FIELDS: { key: BoundsField; label: string }[] = [
  { key: 'x', label: 'X' },
  { key: 'y', label: 'Y' },
  { key: 'width', label: 'Width' },
  { key: 'height', label: 'Height' }
]

type Draft = Partial<Record<BoundsField, string>>

export function WindowLayoutTab(): JSX.Element {
  const [windows, setWindows] = useState<WindowInfo[]>([])
  const [drafts, setDrafts] = useState<Record<string, Draft>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})

  useEffect(() => {
    window.api.getWindowList().then(setWindows).catch(console.error)
    const unsubscribe = window.api.onWindowBoundsChanged(setWindows)
    return unsubscribe
  }, [])

  const setField = (name: string, field: BoundsField, value: string): void => {
    setDrafts((prev) => ({ ...prev, [name]: { ...prev[name], [field]: value } }))
  }

  const resetRow = (name: string): void => {
    setDrafts((prev) => {
      const next = { ...prev }
      delete next[name]
      return next
    })
    setErrors((prev) => {
      const next = { ...prev }
      delete next[name]
      return next
    })
  }

  const applyRow = async (name: string): Promise<void> => {
    const draft = drafts[name]
    if (!draft) return

    const parsed: Partial<WindowBounds> = {}
    for (const { key } of FIELDS) {
      const raw = draft[key]
      if (raw === undefined || raw === '') continue
      const num = Number(raw)
      if (Number.isFinite(num)) parsed[key] = Math.round(num)
    }

    try {
      const updated = await window.api.setWindowBounds(name, parsed)
      setWindows(updated)
      resetRow(name)
    } catch (err) {
      setErrors((prev) => ({ ...prev, [name]: err instanceof Error ? err.message : 'Failed to apply' }))
    }
  }

  return (
    <div className="p-3 space-y-4">
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Window Layout
        </h3>
        {windows.length === 0 ? (
          <p className="text-slate-500 text-sm">No windows open.</p>
        ) : (
          <div className="space-y-2">
            {windows.map((w) => {
              const draft = drafts[w.name] ?? {}
              const isDirty = Object.keys(draft).length > 0
              return (
                <div key={w.name} className="bg-card-border/30 rounded px-3 py-2">
                  <div className="flex items-center justify-between mb-2">
                    <div>
                      <span className="text-sm text-white font-medium">{w.title}</span>
                      <span className="text-xs text-slate-500 ml-2">{w.name}</span>
                    </div>
                    <div className="flex gap-2">
                      <button
                        onClick={() => resetRow(w.name)}
                        disabled={!isDirty}
                        className="text-xs text-slate-400 hover:text-white disabled:opacity-30 transition-colors"
                      >
                        Reset
                      </button>
                      <button
                        onClick={() => applyRow(w.name)}
                        disabled={!isDirty}
                        className="px-2 py-1 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-30 text-white text-xs rounded transition-colors"
                      >
                        Apply
                      </button>
                    </div>
                  </div>
                  <div className="flex gap-2">
                    {FIELDS.map(({ key, label }) => (
                      <div key={key} className="flex-1">
                        <label className="block text-xs text-slate-500 mb-0.5">{label}</label>
                        <input
                          type="number"
                          value={draft[key] ?? String(w.bounds[key])}
                          onChange={(e) => setField(w.name, key, e.target.value)}
                          className="w-full bg-card-border text-white text-sm rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500"
                        />
                      </div>
                    ))}
                  </div>
                  {errors[w.name] && (
                    <div className="text-xs text-red-400 mt-1">{errors[w.name]}</div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>

      <p className="text-xs text-slate-500">
        Positions and sizes auto-save whenever you move or resize a window. Closed phase
        windows aren&apos;t listed — unsubscribe/resubscribe or restart the app to reopen one.
      </p>
    </div>
  )
}
