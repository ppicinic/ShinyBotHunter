import React, { useEffect, useRef, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { HiddenRow, MilestoneCardRow, MilestoneRowsResponse } from '../../types/api'

export function hiddenCount(rows: MilestoneCardRow[]): number {
  return rows.filter((r) => r.hidden).length
}

// GameFile.Hidden stores a row by the milestone and the row's count key: "#<species>" for any form, else the form code.
export function rowRequest(milestoneId: string, row: MilestoneCardRow): HiddenRow {
  return { milestoneId, key: row.key }
}

// FR-12: a generated milestone's rows on the bot's game file, each with a Hide checkbox. A hidden row stays listed here, greyed,
// so it can be unhidden; the counts window leaves it out.
export function MilestoneRowsPanel({ game, gameFileName, milestoneId }: { game: string; gameFileName: string; milestoneId: string }): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''
  const [rows, setRows] = useState<MilestoneCardRow[] | null>(null)
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // The file and milestone shown. A response for any other, after the pick changed, is dropped. Written only by the load below.
  const shown = useRef('')

  const load = async (shownAs: string): Promise<void> => {
    try {
      const r = await api.getMilestoneRows(apiUrl, game, milestoneId, gameFileName)
      if (!r.ok) throw new Error(`Server returned ${r.status}`)
      const data = await r.json() as MilestoneRowsResponse
      if (shown.current === shownAs) setRows(data.rows)
    } catch (err) {
      if (shown.current === shownAs) setError(err instanceof Error ? err.message : 'Failed to load the rows')
    }
  }

  useEffect(() => {
    if (!apiUrl) return
    const shownAs = `${gameFileName}/${milestoneId}`
    shown.current = shownAs
    setRows(null)
    setError(null)
    load(shownAs)
  }, [apiUrl, game, gameFileName, milestoneId])

  const setHidden = async (row: MilestoneCardRow, hidden: boolean): Promise<void> => {
    const shownAs = shown.current
    setBusy(true)
    setError(null)
    try {
      const r = await api.setRowHidden(apiUrl, gameFileName, rowRequest(milestoneId, row), hidden)
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      await load(shownAs)
    } catch (err) {
      if (shown.current === shownAs) setError(err instanceof Error ? err.message : 'Failed to save the row')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div data-testid="milestone-rows" className="space-y-0.5">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        disabled={rows === null}
        className="text-xs text-slate-400 hover:text-white disabled:opacity-40"
      >
        {open ? '▾' : '▸'} Rows ({rows === null ? '…' : hiddenCount(rows)} hidden)
      </button>
      {open && rows !== null && (
        <ul className="space-y-0.5">
          {rows.map((row) => (
            <li key={row.key} className={`flex items-center gap-2 text-xs ${row.hidden ? 'text-slate-500 opacity-60' : 'text-slate-300'}`}>
              <span className="flex-1">
                {row.name} · {row.formCode ?? 'any form'}: wanted {row.wanted}, caught {row.caught}, {row.encountersHere} encounters here
              </span>
              <label className="flex items-center gap-1 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={row.hidden}
                  onChange={(e) => setHidden(row, e.target.checked)}
                  disabled={busy}
                  className="accent-indigo-500 disabled:opacity-50"
                />
                Hide
              </label>
            </li>
          ))}
        </ul>
      )}
      {error && <div className="text-xs text-red-400">{error}</div>}
    </div>
  )
}
