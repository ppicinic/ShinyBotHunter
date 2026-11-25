import React, { useEffect, useMemo, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { GameFile, GameRomInfo, GoalEntry, GoalPreset, SpeciesName } from '../../types/api'

const PRESET_LABELS: Record<GoalPreset, string> = {
  LivingDex: 'Living dex',
  FemaleForms: 'Female forms',
  VersionExclusives: 'Version exclusives'
}

// FR-8: Ruby and Sapphire have no female forms preset (Decided).
export function presetsFor(game: string): GoalPreset[] {
  return game === 'HGSS' ? ['LivingDex', 'FemaleForms', 'VersionExclusives'] : ['LivingDex', 'VersionExclusives']
}

export interface GoalLine {
  // The entry's position in the goal, which edits and removals address
  index: number
  entry: GoalEntry
  name: string
  // An amount of 0: shown and counted, never a target
  tracked: boolean
  // Not a whole number 0 or more, so the goal is not sent
  badAmount: boolean
}

export function isWholeAmount(amount: number): boolean {
  return Number.isInteger(amount) && amount >= 0
}

const BAD_AMOUNT = 'Amount is a whole number, 0 or more.'

// The goal's entries matching the filter (name, dex number or form code), by species, then form with any form first.
export function goalLines(entries: GoalEntry[], names: ReadonlyMap<number, string>, filter: string): GoalLine[] {
  const needle = filter.trim().toLowerCase()
  return entries
    .map((entry, index) => ({ index, entry, name: names.get(entry.species) ?? `#${entry.species}`, tracked: entry.amount === 0, badAmount: !isWholeAmount(entry.amount) }))
    .filter(({ entry, name }) => needle === ''
      || name.toLowerCase().includes(needle)
      || needle.replace(/^#/, '') === String(entry.species)
      || (entry.formCode ?? '').toLowerCase().includes(needle))
    .sort((a, b) => a.entry.species - b.entry.species || (a.entry.formCode ?? '').localeCompare(b.entry.formCode ?? ''))
}

const INPUT = 'bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-50'

export function GoalEditorTab(): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  const [roms, setRoms] = useState<GameRomInfo[]>([])
  const [fileName, setFileName] = useState('')
  const [names, setNames] = useState<SpeciesName[]>([])
  const [entries, setEntries] = useState<GoalEntry[] | null>(null)
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [pendingPreset, setPendingPreset] = useState<GoalPreset | null>(null)
  const [filter, setFilter] = useState('')
  const [addSpecies, setAddSpecies] = useState('')
  const [addForm, setAddForm] = useState('')
  const [addAmount, setAddAmount] = useState(1)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)

  const rom = roms.find((r) => r.handle === fileName)
  const game = rom?.flavor ?? ''
  const nameMap = useMemo(() => new Map(names.map((n) => [n.species, n.name])), [names])

  useEffect(() => {
    if (!apiUrl) return
    api.getAllGames(apiUrl)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: GameRomInfo[]) => {
        const withFlavor = data.filter((r) => r.flavor !== null)
        setRoms(withFlavor)
        setFileName(withFlavor[0]?.handle ?? '')
      })
      .catch((err) => setFeedback({ msg: err instanceof Error ? err.message : 'Failed to load game files', isError: true }))
  }, [apiUrl])

  // A response for a game that is no longer selected is dropped.
  useEffect(() => {
    if (!apiUrl || !game) return
    let current = true
    api.getSpeciesNames(apiUrl, game)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: SpeciesName[]) => { if (current) setNames(data) })
      .catch((err) => { if (current) setFeedback({ msg: err instanceof Error ? err.message : 'Failed to load species names', isError: true }) })
    return () => { current = false }
  }, [apiUrl, game])

  // Reads only: a file with no goal stored shows an empty one, and nothing is written until Save or a preset (FR-42). A response
  // for a file that is no longer selected is dropped, so one file's goal is never shown, or saved, as another's.
  useEffect(() => {
    if (!apiUrl || !fileName) return
    let current = true
    setEntries(null)
    setDirty(false)
    setPendingPreset(null)
    setFeedback(null)
    api.getGoal(apiUrl, fileName)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: GameFile) => { if (current) setEntries(data.entries) })
      .catch((err) => { if (current) setFeedback({ msg: err instanceof Error ? err.message : 'Failed to load the goal', isError: true }) })
    return () => { current = false }
  }, [apiUrl, fileName])

  const edit = (next: GoalEntry[]): void => {
    setEntries(next)
    setDirty(true)
    setFeedback(null)
  }

  const setAmount = (index: number, amount: number): void => {
    if (!entries) return
    edit(entries.map((e, i) => (i === index ? { ...e, amount } : e)))
  }

  const remove = (index: number): void => {
    if (!entries) return
    edit(entries.filter((_, i) => i !== index))
  }

  const add = (): void => {
    if (!entries) return
    const typed = addSpecies.trim()
    const species = /^\d+$/.test(typed)
      ? Number(typed)
      : names.find((n) => n.name.toLowerCase() === typed.toLowerCase())?.species
    if (species === undefined) {
      setFeedback({ msg: `No species named "${typed}"`, isError: true })
      return
    }
    const formCode = addForm.trim() || null
    edit([...entries, { species, formCode, amount: addAmount, key: formCode ?? `#${species}` }])
    setAddSpecies('')
    setAddForm('')
    setAddAmount(1)
  }

  const save = async (): Promise<void> => {
    if (!entries) return
    setBusy(true)
    setFeedback(null)
    try {
      const res = await api.putGoal(apiUrl, fileName, entries.map(({ species, formCode, amount }) => ({ species, formCode, amount })))
      if (!res.ok) throw new Error((await res.text()) || `Server returned ${res.status}`)
      setDirty(false)
      setFeedback({ msg: `Saved the goal of ${fileName}`, isError: false })
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Save failed', isError: true })
    } finally {
      setBusy(false)
    }
  }

  const applyPreset = async (preset: GoalPreset): Promise<void> => {
    setPendingPreset(null)
    setBusy(true)
    setFeedback(null)
    try {
      const res = await api.applyGoalPreset(apiUrl, game, fileName, preset)
      if (res.status === 404) throw new Error('This game file is no longer in the game list: reopen the Goals tab.')
      if (!res.ok) throw new Error((await res.text()) || `Server returned ${res.status}`)
      setEntries(await res.json() as GoalEntry[])
      setFeedback({ msg: `${PRESET_LABELS[preset]} applied. Species already in the goal were kept.`, isError: false })
    } catch (err) {
      setFeedback({ msg: err instanceof Error ? err.message : 'Preset failed', isError: true })
    } finally {
      setBusy(false)
    }
  }

  const lines = entries ? goalLines(entries, nameMap, filter) : []
  // Checked on every entry, not only the filtered lines, so a bad amount out of view still holds the save.
  const badAmounts = entries?.some((e) => !isWholeAmount(e.amount)) ?? false

  return (
    <div className="p-3 space-y-4">
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">Game file</h3>
        <select
          value={fileName}
          onChange={(e) => setFileName(e.target.value)}
          disabled={dirty}
          title={dirty ? 'Save the goal first' : undefined}
          className="w-full bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500 disabled:opacity-50"
        >
          {roms.length === 0 && <option value="">No game files</option>}
          {roms.map((r) => (
            <option key={r.handle} value={r.handle}>{r.handle}</option>
          ))}
        </select>
      </div>

      {entries !== null && (
        <>
          <div>
            <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">Start from a preset</h3>
            <div className="flex gap-2 flex-wrap">
              {presetsFor(game).map((p) => (
                <button
                  key={p}
                  onClick={() => setPendingPreset(p)}
                  disabled={dirty || busy}
                  title={dirty ? 'Save the goal first' : undefined}
                  className="text-xs px-2 py-1 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white rounded transition-colors"
                >
                  {PRESET_LABELS[p]}
                </button>
              ))}
            </div>
            {pendingPreset && (
              <div className="mt-2 text-xs text-amber-400 bg-amber-900/20 border border-amber-700/30 rounded px-2 py-1.5 space-y-1.5">
                <p>
                  {PRESET_LABELS[pendingPreset]} adds an entry for every species the goal does not have yet. Entries already in the
                  goal are kept. This saves the goal of {fileName} now.
                </p>
                <div className="flex gap-2">
                  <button onClick={() => applyPreset(pendingPreset)} className="px-2 py-0.5 bg-indigo-600 hover:bg-indigo-500 text-white rounded">
                    Apply {PRESET_LABELS[pendingPreset]}
                  </button>
                  <button onClick={() => setPendingPreset(null)} className="px-2 py-0.5 text-slate-300 hover:text-white">
                    Cancel
                  </button>
                </div>
              </div>
            )}
          </div>

          <div>
            <div className="flex items-center justify-between mb-2">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400">Entries ({entries.length})</h3>
              <input
                type="text"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
                placeholder="Filter: name, number or form"
                className={`w-48 ${INPUT}`}
              />
            </div>

            {entries.length === 0 ? (
              <p className="text-slate-500 text-sm">No entries. Start from a preset or add one below.</p>
            ) : (
              <div className="space-y-1">
                {lines.map((line) => (
                  <div key={line.index} className="flex gap-2 items-center bg-card-border/20 rounded px-2 py-1 border border-slate-700/40">
                    <span className="flex-1 text-xs text-white">
                      {line.name} <span className="text-slate-500">#{line.entry.species}</span>
                    </span>
                    <span className="w-32 text-xs text-slate-400 truncate">{line.entry.formCode ?? 'any form'}</span>
                    <input
                      type="number"
                      min={0}
                      value={line.entry.amount}
                      onChange={(e) => setAmount(line.index, Number(e.target.value))}
                      className={`w-16 ${INPUT}`}
                    />
                    {line.badAmount
                      ? <span className="w-36 text-xs text-red-400">{BAD_AMOUNT}</span>
                      : <span className="w-36 text-xs text-slate-500">{line.tracked ? 'tracked, never a target' : ''}</span>}
                    <button
                      onClick={() => remove(line.index)}
                      className="text-xs text-red-400 hover:text-red-300 transition-colors px-1"
                      title="Remove"
                    >
                      ✕
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="flex gap-2 items-end">
            <div className="flex-1">
              <label className="block text-xs text-slate-500 mb-0.5">Species (name or number)</label>
              <input
                type="text"
                list="goal-species"
                value={addSpecies}
                onChange={(e) => setAddSpecies(e.target.value)}
                placeholder="e.g. Aipom"
                className={`w-full ${INPUT}`}
              />
              <datalist id="goal-species">
                {names.map((n) => (
                  <option key={n.species} value={n.name} />
                ))}
              </datalist>
            </div>
            <div className="w-32">
              <label className="block text-xs text-slate-500 mb-0.5">Form code (optional)</label>
              <input
                type="text"
                value={addForm}
                onChange={(e) => setAddForm(e.target.value)}
                placeholder="any form"
                className={`w-full ${INPUT}`}
              />
            </div>
            <div className="w-16">
              <label className="block text-xs text-slate-500 mb-0.5">Amount</label>
              <input
                type="number"
                min={0}
                value={addAmount}
                onChange={(e) => setAddAmount(Number(e.target.value))}
                className={`w-full ${INPUT}`}
              />
            </div>
            <button
              onClick={add}
              disabled={!addSpecies.trim() || !isWholeAmount(addAmount)}
              className="text-xs px-2 py-1 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white rounded transition-colors"
            >
              + Add
            </button>
          </div>
          {!isWholeAmount(addAmount) && <p className="text-xs text-red-400">{BAD_AMOUNT}</p>}

          {badAmounts && <p className="text-xs text-red-400">{BAD_AMOUNT} Fix the entries marked so before saving.</p>}
          <button
            onClick={save}
            disabled={busy || !dirty || badAmounts}
            className="w-full px-3 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors font-medium"
          >
            {busy ? 'Saving...' : 'Save goal'}
          </button>
        </>
      )}

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
