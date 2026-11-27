import React, { useEffect, useRef, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type {
  AdminMetadata,
  GameRomInfo,
  GoalPreset,
  LocationOption,
  MilestoneLocation,
  Modality,
  PhaseInfo,
  PhaseInfoResponse,
  PhasePokemonInfo,
  PlanTime,
  RomVersion,
  SeedRow
} from '../../types/api'

// Ruby and Sapphire have no female forms preset.
export function presetsFor(game: string): GoalPreset[] {
  return game === 'HGSS' ? ['None', 'LivingDex', 'FemaleForms', 'VersionExclusives'] : ['None', 'LivingDex', 'VersionExclusives']
}

export function versionsOf(game: string): RomVersion[] {
  return game === 'HGSS' ? ['HeartGold', 'SoulSilver'] : ['Ruby', 'Sapphire']
}

// A new milestone's name may not be a saved one's: the save route upserts by name, so it would replace that milestone.
export function nameTaken(phases: PhaseInfo[], name: string): boolean {
  return phases.some((p) => p.name === name.trim())
}

// The reread after Attach or Detach is applied only to the milestone it was asked for, not to one opened meanwhile.
export function sameMilestone(open: PhaseInfo, reread: PhaseInfo): boolean {
  return open.gameName === reread.gameName && open.name === reread.name
}

export function toggleFile(gameFiles: string[], handle: string, on: boolean): string[] {
  const others = gameFiles.filter((f) => f !== handle)
  return on ? [...others, handle] : others
}

// The location dropdown's groups, each kind in the server's order.
export function byModality(locations: LocationOption[]): [Modality, LocationOption[]][] {
  const groups = new Map<Modality, LocationOption[]>()
  for (const l of locations) groups.set(l.modality, [...(groups.get(l.modality) ?? []), l])
  return Array.from(groups)
}

// A row's markers. A milestone with no row met at only one or two times carries none (Ruby and Sapphire, a location met
// all day), nor does a row met at no time here.
export function markerLabels(times: PlanTime[], anyTimed: boolean): string[] {
  if (!anyTimed || times.length === 0) return []
  if (times.length === 3) return ['all day']
  return times.map((t) => t.toLowerCase())
}

// The count key the backend uses for a row.
function rowKey(p: PhasePokemonInfo): string {
  return p.requireSpecificForm ? p.formCode : `#${p.speciesId}`
}

const isTimed = (times: PlanTime[]): boolean => times.length === 1 || times.length === 2

// A milestone made from game data, until Save. Changing the preset or the version replaces only the seed, so typed amounts,
// removals and added rows stay; changing the location starts over.
export interface Draft {
  // The seed's location id
  location: string
  // Null: both versions
  version: RomVersion | null
  preset: GoalPreset
  // The last seed fetched for location, version and preset
  seed: SeedRow[]
  // Amounts typed by hand, by row key
  edited: Record<string, number>
  // Keys of seed rows removed by hand
  removed: string[]
  // Rows added by hand, drawn after the seed rows
  added: PhasePokemonInfo[]
}

// The rows to show and save: the seed rows not removed, at the typed amount if any, then the added rows.
export function draftRows(draft: Draft): PhasePokemonInfo[] {
  const seeded = draft.seed
    .filter((r) => !draft.removed.includes(rowKey(r)))
    .map((r) => ({
      name: r.name,
      speciesId: r.speciesId,
      amount: draft.edited[rowKey(r)] ?? r.amount,
      formCode: r.formCode,
      requireSpecificForm: r.requireSpecificForm,
      hasFemaleForm: r.hasFemaleForm
    }))
  return [...seeded, ...draft.added]
}

export function savePayload(form: PhaseInfo, draft: Draft | null, gameName: string, name: string): PhaseInfo {
  return draft
    ? { ...form, gameName, name, location: draft.location, pokemon: draftRows(draft) }
    : { ...form, gameName, name }
}

const PRESET_LABELS: Record<GoalPreset, string> = {
  None: 'None (all 0)',
  LivingDex: 'Living dex',
  FemaleForms: 'Female forms',
  VersionExclusives: 'Version exclusives'
}

// The card's pill colours, by marker
const PILL_COLORS: Record<string, string> = {
  morning: 'text-amber-300 border-amber-300/40',
  day: 'text-sky-300 border-sky-300/40',
  night: 'text-violet-300 border-violet-300/40',
  'all day': 'text-slate-400 border-card-border'
}

const INPUT = 'bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500 disabled:opacity-50'
const SELECT = 'bg-card-border text-white text-sm rounded px-2 py-1.5 border border-slate-600 focus:outline-none focus:border-indigo-500'
const BUTTON = 'text-xs px-2 py-1 rounded border border-slate-600 text-slate-300 hover:text-white disabled:opacity-50'

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
  isStatic: false,
  location: null,
  gameFiles: [],
  archived: false
})

// One drawn row: a saved milestone's, a draft's seed row (only its amount is typed) or a draft's added row.
interface EditorRow {
  key: string
  pokemon: PhasePokemonInfo
  // Undefined for a row with no location data: drawn with empty When and Version cells
  mark: { version: RomVersion | null; times: PlanTime[] } | undefined
  seeded: boolean
  patch: (patch: Partial<PhasePokemonInfo>) => void
  remove: () => void
}

function LocationSelect({ value, onChange, locations, suggested, placeholder }: {
  value: string
  onChange: (id: string) => void
  locations: LocationOption[]
  suggested: string | null
  placeholder?: string
}): JSX.Element {
  return (
    <select value={value} onChange={(e) => onChange(e.target.value)} className={`flex-1 min-w-0 ${SELECT}`}>
      {placeholder && <option value="">{placeholder}</option>}
      {byModality(locations).map(([modality, group]) => (
        <optgroup key={modality} label={modality}>
          {group.map((l) => (
            <option key={l.id} value={l.id}>{l.id === suggested ? `${l.name} (suggested)` : l.name}</option>
          ))}
        </optgroup>
      ))}
    </select>
  )
}

export function PhaseEditorTab(): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  // ── selection state ──────────────────────────────────────────
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  const [roms, setRoms] = useState<GameRomInfo[]>([])
  const [locations, setLocations] = useState<LocationOption[]>([])
  const [selectedGame, setSelectedGame] = useState<string>('')
  const [selectedPhase, setSelectedPhase] = useState<string>('')
  const [isNewPhase, setIsNewPhase] = useState(false)
  const [newPhaseName, setNewPhaseName] = useState('')

  // ── editor state ─────────────────────────────────────────────
  const [form, setForm] = useState<PhaseInfo | null>(null)
  // The form as last rendered, for the Attach and Detach handler to check after its round trips; written only by render,
  // read only on the UI thread.
  const openForm = useRef<PhaseInfo | null>(null)
  openForm.current = form
  // Set while a new milestone is made from game data; its rows are drawn from it, not from form
  const [draft, setDraft] = useState<Draft | null>(null)
  // A saved milestone's markers and version marks, by row key; null without a location
  const [savedLocation, setSavedLocation] = useState<MilestoneLocation | null>(null)
  const [suggested, setSuggested] = useState<string | null>(null)
  const [attachChoice, setAttachChoice] = useState('')
  const [loadingPhase, setLoadingPhase] = useState(false)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ msg: string; isError: boolean } | null>(null)

  const showFeedback = (msg: string, isError = false): void => {
    setFeedback({ msg, isError })
    if (!isError) setTimeout(() => setFeedback(null), 3000)
  }

  const showLocation = (data: PhaseInfoResponse): void => {
    setSavedLocation(data.location)
    setSuggested(data.suggestedLocation)
    setAttachChoice(data.suggestedLocation ?? '')
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
    api.getAllGames(apiUrl)
      .then((r) => r.json())
      .then((data: GameRomInfo[]) => setRoms(data))
      .catch((err) => showFeedback(err instanceof Error ? err.message : 'Failed to load the game files', true))
  }, [apiUrl])

  // The game's locations, for "New from game data" and for attaching
  useEffect(() => {
    if (!apiUrl || !selectedGame) return
    setLocations([])
    api.getLocations(apiUrl, selectedGame)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status} for the locations`)
        return r.json()
      })
      .then((data: LocationOption[]) => setLocations(data))
      .catch((err) => showFeedback(err instanceof Error ? err.message : 'Failed to load the locations', true))
  }, [apiUrl, selectedGame])

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
      .then((data: PhaseInfoResponse) => {
        setForm(data.phaseInfo)
        showLocation(data)
      })
      .catch((err) => {
        if (err instanceof Error && err.name === 'AbortError') return
        showFeedback(err instanceof Error ? err.message : 'Failed to load milestone', true)
      })
      .finally(() => setLoadingPhase(false))
    return () => controller.abort()
  }, [apiUrl, selectedGame, selectedPhase, isNewPhase])

  // Refetch the seed on each change of location, version or preset. A response is kept only while the draft still asks for
  // it, so a slow one for an earlier choice is dropped.
  const seedLocation = draft?.location
  const seedVersion = draft?.version
  const seedPreset = draft?.preset
  useEffect(() => {
    if (!apiUrl || seedLocation === undefined || seedVersion === undefined || seedPreset === undefined) return
    api.getSeed(apiUrl, selectedGame, seedLocation, seedPreset, seedVersion)
      .then(async (r) => {
        if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
        return r.json()
      })
      .then((data: { name: string; location: string; rows: SeedRow[] }) => setDraft((d) =>
        d && d.location === seedLocation && d.version === seedVersion && d.preset === seedPreset ? { ...d, seed: data.rows } : d))
      .catch((err) => showFeedback(err instanceof Error ? err.message : 'Failed to load the game data', true))
  }, [apiUrl, selectedGame, seedLocation, seedVersion, seedPreset])

  const handleGameChange = (game: string): void => {
    setSelectedGame(game)
    const firstPhase = metadata?.games[game]?.phases[0]?.name ?? ''
    setSelectedPhase(firstPhase)
    setIsNewPhase(false)
    setNewPhaseName('')
    setDraft(null)
  }

  const handleSelectPhase = (phase: string): void => {
    setSelectedPhase(phase)
    setIsNewPhase(false)
    setNewPhaseName('')
    setDraft(null)
    setFeedback(null)
  }

  const handleNewPhase = (): void => {
    setIsNewPhase(true)
    setSelectedPhase('')
    setNewPhaseName('')
    setForm(EMPTY_PHASE(selectedGame, ''))
    setDraft(null)
    setSavedLocation(null)
    setFeedback(null)
  }

  // A new location starts the draft over, its name prefilled; the version and preset stay.
  const startDraft = (location: LocationOption): void => {
    setDraft((d) => ({ location: location.id, version: d?.version ?? null, preset: d?.preset ?? 'LivingDex', seed: [], edited: {}, removed: [], added: [] }))
    setNewPhaseName(location.name)
  }

  const handleNewFromData = (): void => {
    handleNewPhase()
    startDraft(locations[0])
  }

  const handleNewPhaseNameChange = (name: string): void => {
    setNewPhaseName(name)
    setForm((prev) => prev ? { ...prev, name } : null)
  }

  // Attach and detach change only the location: the reread replaces form.location and the markers, so unsaved row edits stay.
  const handleLocation = async (locationId: string | null): Promise<void> => {
    setFeedback(null)
    try {
      const r = locationId
        ? await api.attachLocation(apiUrl, selectedGame, selectedPhase, locationId)
        : await api.detachLocation(apiUrl, selectedGame, selectedPhase)
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      const reread = await api.getPhaseInfo(apiUrl, selectedGame, selectedPhase, new AbortController().signal)
      if (!reread.ok) throw new Error(`Server returned ${reread.status}`)
      const data = await reread.json() as PhaseInfoResponse
      if (!openForm.current || !sameMilestone(openForm.current, data.phaseInfo)) return
      setForm((prev) => prev && { ...prev, location: data.phaseInfo.location })
      showLocation(data)
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Failed to change the location', true)
    }
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
    if (draft) setDraft((d) => d && { ...d, added: [...d.added, EMPTY_POKEMON()] })
    else setForm((prev) => prev ? { ...prev, pokemon: [...prev.pokemon, EMPTY_POKEMON()] } : prev)
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
    if (isNewPhase && nameTaken(availablePhases, phaseName)) {
      showFeedback(`A milestone named "${phaseName}" already exists; choose another name.`, true)
      return
    }
    // An empty milestone is legitimate: with no requirements UpdateMilestoneState treats it as
    // already satisfied, so it never blocks a run. That makes it the catch-all bucket for training,
    // keeping those encounters out of a real milestone's phase counters.

    setSaving(true)
    setFeedback(null)
    try {
      const payload = savePayload(form, draft, selectedGame, phaseName)
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
        setDraft(null)
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
  // A seeded milestone's Pokémon are locked; its game files, Archived and location are not.
  const isReadOnly = !isNewPhase && (selectedPhaseInfo?.isStatic ?? form?.isStatic ?? false)
  const gameRoms = roms.filter((r) => r.flavor === selectedGame)
  const locationName = (id: string): string => locations.find((l) => l.id === id)?.name ?? id

  // Each row's markers and version mark: a draft's from its seed, a saved milestone's looked up by key, since adding or
  // removing rows shifts positions.
  const marks = new Map((savedLocation?.rows ?? []).map((r) => [r.key, r]))
  const anyTimed = draft ? draft.seed.some((r) => isTimed(r.times)) : (savedLocation?.rows ?? []).some((r) => isTimed(r.times))
  const rows: EditorRow[] = draft
    ? [
        ...draft.seed.filter((r) => !draft.removed.includes(rowKey(r))).map((r): EditorRow => {
          const key = rowKey(r)
          return {
            key,
            pokemon: { ...r, amount: draft.edited[key] ?? r.amount },
            mark: r,
            seeded: true,
            patch: (patch) => {
              if (patch.amount !== undefined) setDraft((d) => d && { ...d, edited: { ...d.edited, [key]: patch.amount! } })
            },
            remove: () => setDraft((d) => d && { ...d, removed: [...d.removed, key] })
          }
        }),
        ...draft.added.map((a, i): EditorRow => ({
          key: `added-${i}`,
          pokemon: a,
          mark: undefined,
          seeded: false,
          patch: (patch) => setDraft((d) => d && { ...d, added: d.added.map((x, j) => (j === i ? { ...x, ...patch } : x)) }),
          remove: () => setDraft((d) => d && { ...d, added: d.added.filter((_, j) => j !== i) })
        }))
      ]
    : (form?.pokemon ?? []).map((p, i): EditorRow => ({
        key: `row-${i}`,
        pokemon: p,
        mark: marks.get(rowKey(p)),
        seeded: false,
        patch: (patch) => updatePokemon(i, patch),
        remove: () => removePokemon(i)
      }))

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
            className={SELECT}
          >
            {availableGames.length === 0 && <option value="">Loading...</option>}
            {availableGames.map((g) => (
              <option key={g} value={g}>{g}</option>
            ))}
          </select>

          <select
            value={isNewPhase ? '' : selectedPhase}
            onChange={(e) => handleSelectPhase(e.target.value)}
            className={`flex-1 ${SELECT}`}
            disabled={!selectedGame}
          >
            {isNewPhase && <option value="">New milestone</option>}
            {availablePhases.map((p) => (
              <option key={p.name} value={p.name}>{p.name}</option>
            ))}
          </select>
          <button onClick={handleNewFromData} disabled={locations.length === 0} className={BUTTON}>New from game data</button>
          <button onClick={handleNewPhase} disabled={!selectedGame} className={BUTTON}>New empty</button>
        </div>

        {draft && (
          <div className="mt-2 space-y-2">
            <div className="flex items-center gap-2">
              <span className="text-xs text-slate-500 w-24 flex-shrink-0">Location</span>
              <LocationSelect
                value={draft.location}
                onChange={(id) => startDraft(locations.find((l) => l.id === id)!)}
                locations={locations}
                suggested={null}
              />
            </div>
            <div className="flex items-center gap-1">
              <span className="text-xs text-slate-500 w-24 flex-shrink-0">Load species of</span>
              {[null, ...versionsOf(selectedGame)].map((v) => (
                <button
                  key={v ?? 'both'}
                  onClick={() => setDraft((d) => d && { ...d, version: v })}
                  className={`px-2 py-0.5 text-xs rounded transition-colors ${
                    draft.version === v ? 'bg-indigo-700 text-white' : 'bg-card-border text-slate-400 hover:text-white'
                  }`}
                >
                  {v ? `${v} only` : 'Both versions'}
                </button>
              ))}
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-slate-500 w-24 flex-shrink-0">Preset</span>
              <select
                value={draft.preset}
                onChange={(e) => setDraft((d) => d && { ...d, preset: e.target.value as GoalPreset })}
                className={SELECT}
              >
                {presetsFor(selectedGame).map((p) => (
                  <option key={p} value={p}>{PRESET_LABELS[p]}</option>
                ))}
              </select>
              <span className="text-xs text-slate-500">Fills the amounts. An amount you typed yourself is kept.</span>
            </div>
          </div>
        )}

        {isNewPhase && (
          <div className="mt-2">
            <input
              type="text"
              value={newPhaseName}
              onChange={(e) => handleNewPhaseNameChange(e.target.value)}
              placeholder="New milestone name"
              className={`w-full ${SELECT} placeholder-slate-500`}
            />
          </div>
        )}

        {isReadOnly && (
          <p className="mt-1.5 text-xs text-amber-400 bg-amber-900/20 border border-amber-700/30 rounded px-2 py-1">
            This milestone is loaded from disk: its Pokémon cannot be edited.
          </p>
        )}

        {/* A new empty milestone is attached after its first Save. */}
        {!isNewPhase && form && (
          <div className="mt-2 space-y-1">
            {form.location ? (
              <div className="flex items-center gap-2">
                <span className="flex-1 text-xs text-slate-300">Location: {locationName(form.location)}</span>
                <button onClick={() => handleLocation(null)} className={BUTTON}>Detach</button>
              </div>
            ) : (
              <>
                <p className="text-xs text-slate-400">Location: none. No anchor, no shares, no markers.</p>
                <div className="flex items-center gap-2">
                  <LocationSelect
                    value={attachChoice}
                    onChange={setAttachChoice}
                    locations={locations}
                    suggested={suggested}
                    placeholder={suggested ? undefined : 'Pick a location...'}
                  />
                  <button onClick={() => handleLocation(attachChoice)} disabled={!attachChoice} className={BUTTON}>
                    Attach to a location
                  </button>
                </div>
              </>
            )}
          </div>
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

          {rows.length === 0 ? (
            <p className="text-slate-500 text-sm">No Pokémon defined.</p>
          ) : (
            <table className="w-full text-xs">
              <thead>
                <tr className="text-left text-slate-500">
                  <th className="font-normal pb-1">Pokémon</th>
                  <th className="font-normal pb-1">When</th>
                  <th className="font-normal pb-1">Version</th>
                  {/* 0 is meaningful: the pokemon is tracked in the phase stats but not required,
                      so it never blocks the milestone and the bot never stops for it. */}
                  <th className="font-normal pb-1 w-16">Amount</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.key} className="border-t border-slate-700/40 align-top">
                    <td className="py-1 pr-2 space-y-1">
                      <input
                        type="text"
                        value={row.pokemon.name}
                        onChange={(e) => row.patch({ name: e.target.value })}
                        placeholder="e.g. Rattata"
                        disabled={isReadOnly || row.seeded}
                        className={`w-full ${INPUT}`}
                      />
                      <div className="flex gap-2 items-center">
                        <input
                          type="text"
                          value={row.pokemon.formCode}
                          onChange={(e) => row.patch({ formCode: e.target.value })}
                          placeholder="form code, e.g. rattata (optional)"
                          disabled={isReadOnly || row.seeded}
                          className={`flex-1 min-w-0 ${INPUT}`}
                        />
                        <label className="flex items-center gap-1 text-slate-400 select-none">
                          <input
                            type="checkbox"
                            checked={row.pokemon.requireSpecificForm}
                            onChange={(e) => row.patch({ requireSpecificForm: e.target.checked })}
                            disabled={isReadOnly || row.seeded}
                            className="accent-indigo-500 disabled:opacity-50"
                          />
                          Require form
                        </label>
                      </div>
                    </td>
                    <td className="py-1 pr-2">
                      {markerLabels(row.mark?.times ?? [], anyTimed).map((m) => (
                        <span key={m} className={`mr-1 px-1 rounded border ${PILL_COLORS[m]}`}>{m}</span>
                      ))}
                    </td>
                    <td className="py-1 pr-2">{row.mark && (row.mark.version ?? <span className="text-slate-500">both</span>)}</td>
                    <td className="py-1 pr-2">
                      <input
                        type="number"
                        min={0}
                        value={row.pokemon.amount}
                        onChange={(e) => row.patch({ amount: Number(e.target.value) })}
                        disabled={isReadOnly}
                        className={`w-full ${INPUT}`}
                      />
                    </td>
                    <td className="py-1">
                      {!isReadOnly && (
                        <button
                          onClick={row.remove}
                          className="text-red-400 hover:text-red-300 transition-colors px-1"
                          title="Remove"
                        >
                          ✕
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="mt-1 text-xs text-slate-500">Amount 0: shown and counted, never a target.</p>

          <div className="pt-3 space-y-1">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400">Game files</h3>
            <div className="flex flex-wrap gap-x-3 gap-y-1">
              {gameRoms.map((rom) => (
                <label key={rom.handle} className="flex items-center gap-1.5 text-xs text-slate-300">
                  <input
                    type="checkbox"
                    checked={form.gameFiles.includes(rom.handle)}
                    onChange={(e) => setForm((prev) => prev && { ...prev, gameFiles: toggleFile(prev.gameFiles, rom.handle, e.target.checked) })}
                    className="accent-indigo-500"
                  />
                  {rom.handle}
                </label>
              ))}
            </div>
            <p className="text-xs text-slate-500">None ticked: offered on every game file.</p>
            <label className="flex items-center gap-1.5 text-xs text-slate-300">
              <input
                type="checkbox"
                checked={form.archived}
                onChange={(e) => setForm((prev) => prev && { ...prev, archived: e.target.checked })}
                className="accent-indigo-500"
              />
              Archived: hide from the launch form
            </label>
          </div>

          {/* Save button */}
          <div className="pt-2">
            <button
              onClick={handleSave}
              disabled={saving}
              className="w-full px-3 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors font-medium"
            >
              {saving ? 'Saving...' : 'Save Milestone'}
            </button>
            {draft && <p className="mt-1 text-xs text-slate-500">Nothing is stored before Save.</p>}
          </div>
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
