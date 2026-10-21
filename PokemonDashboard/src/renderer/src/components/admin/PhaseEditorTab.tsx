import React, { useEffect, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { AdminMetadata, PhaseInfo, PhasePokemonInfo } from '../../types/api'

const EMPTY_POKEMON = (): PhasePokemonInfo => ({
  name: '',
  speciesId: 0,
  amount: 1,
  formCode: '',
  hasFemaleForm: false,
  requireSpecificForm: false
})

const EMPTY_PHASE = (gameName: string, phaseName: string): PhaseInfo => ({
  gameName,
  name: phaseName,
  pokemon: [EMPTY_POKEMON()],
  isStatic: false
})

export function PhaseEditorTab(): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  // ── selection state ──────────────────────────────────────────
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  const [selectedGame, setSelectedGame] = useState<string>('')
  const [selectedPhase, setSelectedPhase] = useState<string>('')
  const [isNewPhase, setIsNewPhase] = useState(false)
  const [newPhaseName, setNewPhaseName] = useState('')

  // ── editor state ─────────────────────────────────────────────
  const [form, setForm] = useState<PhaseInfo | null>(null)
  const [loadingPhase, setLoadingPhase] = useState(false)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)

  const showFeedback = (msg: string, isError = false): void => {
    setFeedback({ msg, isError })
    if (!isError) setTimeout(() => setFeedback(null), 3000)
  }

  // Load metadata on mount
  useEffect(() => {
    if (!apiUrl) return
    api.getMetadata(apiUrl)
      .then((r) => r.json())
      .then((data: AdminMetadata) => {
        setMetadata(data)
        const firstGame = Object.keys(data.games ?? {})[0] ?? ''
        setSelectedGame(firstGame)
        const firstPhase = data.games[firstGame]?.phases[0]?.name ?? ''
        setSelectedPhase(firstPhase)
      })
      .catch((err) => showFeedback(err instanceof Error ? err.message : 'Failed to load metadata', true))
  }, [apiUrl])

  // Load phase info when selection changes (and not creating new)
  useEffect(() => {
    if (!apiUrl || !selectedGame || !selectedPhase || isNewPhase) return
    const controller = new AbortController()
    setForm(null)
    setLoadingPhase(true)
    api.getPhaseInfo(apiUrl, selectedGame, selectedPhase, controller.signal)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((data: { phaseInfo: PhaseInfo }) => setForm(data.phaseInfo))
      .catch((err) => {
        if (err instanceof Error && err.name === 'AbortError') return
        showFeedback(err instanceof Error ? err.message : 'Failed to load milestone', true)
      })
      .finally(() => setLoadingPhase(false))
    return () => controller.abort()
  }, [apiUrl, selectedGame, selectedPhase, isNewPhase])

  const handleGameChange = (game: string): void => {
    setSelectedGame(game)
    const firstPhase = metadata?.games[game]?.phases[0]?.name ?? ''
    setSelectedPhase(firstPhase)
    setIsNewPhase(false)
    setNewPhaseName('')
  }

  const handleSelectPhase = (phase: string): void => {
    setSelectedPhase(phase)
    setIsNewPhase(false)
    setNewPhaseName('')
    setFeedback(null)
  }

  const handleNewPhase = (): void => {
    setIsNewPhase(true)
    setSelectedPhase('')
    setNewPhaseName('')
    setForm(EMPTY_PHASE(selectedGame, ''))
    setFeedback(null)
  }

  const handleNewPhaseNameChange = (name: string): void => {
    setNewPhaseName(name)
    setForm((prev) => prev ? { ...prev, name } : null)
  }

  // ── pokemon list helpers ──────────────────────────────────────
  const updatePokemon = (idx: number, patch: Partial<PhasePokemonInfo>): void => {
    setForm((prev) => {
      if (!prev) return prev
      const pokemon = prev.pokemon.map((p, i) => i === idx ? { ...p, ...patch } : p)
      return { ...prev, pokemon }
    })
  }

  const addPokemon = (): void => {
    setForm((prev) => prev ? { ...prev, pokemon: [...prev.pokemon, EMPTY_POKEMON()] } : prev)
  }

  const removePokemon = (idx: number): void => {
    setForm((prev) => {
      if (!prev) return prev
      return { ...prev, pokemon: prev.pokemon.filter((_, i) => i !== idx) }
    })
  }

  // ── save ─────────────────────────────────────────────────────
  const handleSave = async (): Promise<void> => {
    if (!form) return
    const phaseName = isNewPhase ? newPhaseName.trim() : selectedPhase
    if (!phaseName) { showFeedback('Milestone name is required', true); return }
    // An empty milestone is legitimate: with no requirements UpdateMilestoneState treats it as
    // already satisfied, so it never blocks a run. That makes it the catch-all bucket for training,
    // keeping those encounters out of a real milestone's phase counters.

    setSaving(true)
    setFeedback(null)
    try {
      const payload: PhaseInfo = { ...form, gameName: selectedGame, name: phaseName }
      const res = await api.updatePhaseInfo(apiUrl, selectedGame, phaseName, payload)
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `Server returned ${res.status}`)
      }
      const saved = await res.json() as PhaseInfo
      setForm(saved)
      showFeedback(`Saved "${phaseName}" successfully`)
      // Refresh metadata so new phase appears in dropdown
      if (isNewPhase) {
        const metaRes = await api.getMetadata(apiUrl)
        if (metaRes.ok) {
          const data = await metaRes.json() as AdminMetadata
          setMetadata(data)
        }
        setIsNewPhase(false)
        setSelectedPhase(phaseName)
        setNewPhaseName('')
      }
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Save failed', true)
    } finally {
      setSaving(false)
    }
  }

  const availableGames = Object.keys(metadata?.games ?? {})
  const availablePhases = selectedGame ? (metadata?.games[selectedGame]?.phases ?? []) : []
  const selectedPhaseInfo = availablePhases.find((p) => p.name === selectedPhase)
  const isReadOnly = !isNewPhase && (selectedPhaseInfo?.isStatic ?? form?.isStatic ?? false)

  return (
    <div className="p-3 space-y-4">
      {/* Game + Phase selector */}
      <div>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">
          Select Milestone
        </h3>
        <div className="flex gap-2 flex-wrap">
          <select
            value={selectedGame}
            onChange={(e) => handleGameChange(e.target.value)}
            className="bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
          >
            {availableGames.length === 0 && <option value="">Loading...</option>}
            {availableGames.map((g) => (
              <option key={g} value={g}>{g}</option>
            ))}
          </select>

          <select
            value={isNewPhase ? '__new__' : selectedPhase}
            onChange={(e) => {
              if (e.target.value === '__new__') handleNewPhase()
              else handleSelectPhase(e.target.value)
            }}
            className="flex-1 bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500"
            disabled={!selectedGame}
          >
            {availablePhases.map((p) => (
              <option key={p.name} value={p.name}>{p.name}</option>
            ))}
            <option value="__new__">+ New milestone…</option>
          </select>
        </div>

        {isNewPhase && (
          <div className="mt-2">
            <input
              type="text"
              value={newPhaseName}
              onChange={(e) => handleNewPhaseNameChange(e.target.value)}
              placeholder="New milestone name"
              className="w-full bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500"
            />
          </div>
        )}

        {isReadOnly && (
          <p className="mt-1.5 text-xs text-amber-400 bg-amber-900/20 border border-amber-700/30 rounded px-2 py-1">
            This milestone is static (loaded from disk) and cannot be edited.
          </p>
        )}
      </div>

      {/* Pokemon list editor */}
      {loadingPhase ? (
        <p className="text-slate-500 text-sm">Loading milestone...</p>
      ) : form !== null ? (
        <div>
          <div className="flex items-center justify-between mb-2">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400">
              Pokémon
            </h3>
            {!isReadOnly && (
              <button
                onClick={addPokemon}
                className="text-xs px-2 py-1 bg-indigo-700 hover:bg-indigo-600 text-white rounded transition-colors"
              >
                + Add
              </button>
            )}
          </div>

          {form.pokemon.length === 0 ? (
            <p className="text-slate-500 text-sm">No Pokémon defined.</p>
          ) : (
            <div className="space-y-2">
              {form.pokemon.map((pkmn, idx) => (
                <div
                  key={idx}
                  className="bg-card-border/20 rounded-lg px-3 py-2 space-y-2 border border-slate-700/40"
                >
                  <div className="flex gap-2 items-center">
                    {/* Name */}
                    <div className="flex-1">
                      <label className="block text-xs text-slate-500 mb-0.5">Name / Species</label>
                      <input
                        type="text"
                        value={pkmn.name}
                        onChange={(e) => updatePokemon(idx, { name: e.target.value })}
                        placeholder="e.g. Rattata"
                        disabled={isReadOnly}
                        className="w-full bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-50"
                      />
                    </div>
                    {/* Amount */}
                    <div className="w-20">
                      <label className="block text-xs text-slate-500 mb-0.5">Amount</label>
                      {/* 0 is meaningful: the pokemon is tracked in the phase stats but not required,
                          so it never blocks the milestone and the bot never stops for it. */}
                      <input
                        type="number"
                        min={0}
                        value={pkmn.amount}
                        onChange={(e) => updatePokemon(idx, { amount: Number(e.target.value) })}
                        disabled={isReadOnly}
                        className="w-full bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 disabled:opacity-50"
                      />
                    </div>
                    {/* Remove */}
                    {!isReadOnly && (
                      <button
                        onClick={() => removePokemon(idx)}
                        className="self-end mb-0.5 text-xs text-red-400 hover:text-red-300 transition-colors px-1"
                        title="Remove"
                      >
                        ✕
                      </button>
                    )}
                  </div>

                  <div className="flex gap-2 items-center">
                    {/* Form code */}
                    <div className="flex-1">
                      <label className="block text-xs text-slate-500 mb-0.5">Form Code</label>
                      <input
                        type="text"
                        value={pkmn.formCode}
                        onChange={(e) => updatePokemon(idx, { formCode: e.target.value })}
                        placeholder="e.g. rattata (optional)"
                        disabled={isReadOnly}
                        className="w-full bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-50"
                      />
                    </div>
                    {/* Require specific form */}
                    <div className="flex items-center gap-1.5 self-end mb-1">
                      <input
                        type="checkbox"
                        id={`rsf-${idx}`}
                        checked={pkmn.requireSpecificForm}
                        onChange={(e) => updatePokemon(idx, { requireSpecificForm: e.target.checked })}
                        disabled={isReadOnly}
                        className="accent-indigo-500 disabled:opacity-50"
                      />
                      <label htmlFor={`rsf-${idx}`} className="text-xs text-slate-400 cursor-pointer select-none">
                        Require form
                      </label>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}

          {/* Save button */}
          {!isReadOnly && (
            <div className="pt-2">
              <button
                onClick={handleSave}
                disabled={saving}
                className="w-full px-3 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors font-medium"
              >
                {saving ? 'Saving...' : 'Save Milestone'}
              </button>
            </div>
          )}
        </div>
      ) : null}

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
