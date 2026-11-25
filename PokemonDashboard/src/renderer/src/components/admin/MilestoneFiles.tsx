import React, { useEffect, useRef, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { BackfillOffer, CopyOffer, GameFile, GameRomInfo, GoalEntry, MilestoneLink, PhaseInfo } from '../../types/api'

// This milestone's link on this game file, if it has one.
export function linkOf(file: GameFile, phase: string): MilestoneLink | null {
  return file.links.find((l) => l.phase === phase) ?? null
}

// FR-16: a milestone you made, assigned to no game file, is listed on every one.
export function assignmentNote(files: GameFile[], phase: string): string {
  const assigned = files.filter((f) => f.assigned.includes(phase)).length
  return assigned === 0
    ? 'Assigned to no game file: listed on every game file.'
    : `Assigned to ${assigned} game file${assigned === 1 ? '' : 's'}: listed only on ${assigned === 1 ? 'that one' : 'those'}.`
}

// The ticked species, in the offer's order.
export function chosenSpecies(offer: CopyOffer[], ticked: ReadonlySet<number>): number[] {
  return offer.filter((o) => ticked.has(o.species)).map((o) => o.species)
}

function entriesText(entries: GoalEntry[]): string {
  return entries.length === 0 ? 'nothing' : entries.map((e) => `${e.formCode ?? 'any form'} ×${e.amount}`).join(', ')
}

interface FileState {
  file: GameFile
  offer: BackfillOffer
}

const INPUT = 'bg-card-border text-white text-xs rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500 placeholder-slate-500'
const BUTTON = 'text-xs text-slate-400 hover:text-white px-1.5 py-0.5 rounded border border-slate-600 disabled:opacity-40'

// The Milestone Editor's "Game files" section for a saved milestone: per game file of its game, the assignment (FR-16), the link
// to a generated milestone (FR-43) and the copy of its amounts into the goal (FR-45). Opening it only reads; each write is a click.
export function MilestoneFiles({ game, phase }: { game: string; phase: PhaseInfo }): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''
  const [roms, setRoms] = useState<GameRomInfo[] | null>(null)
  const [files, setFiles] = useState<Record<string, FileState>>({})
  const [linkIds, setLinkIds] = useState<Record<string, string>>({})
  const [copyOpen, setCopyOpen] = useState<string | null>(null)
  const [ticked, setTicked] = useState<Set<number>>(new Set())
  const [busy, setBusy] = useState(false)
  const [messages, setMessages] = useState<Record<string, { msg: string; isError: boolean } | null>>({})
  const [loadError, setLoadError] = useState<string | null>(null)
  // The game and milestone shown. A response that arrives after the selection changed is dropped, so one milestone's files are
  // never shown, or written, as another's. Written only by the effect below.
  const shown = useRef('')

  const loadFile = async (handle: string, shownAs: string): Promise<void> => {
    const [goal, offer] = await Promise.all([api.getGoal(apiUrl, handle), api.getBackfillOffer(apiUrl, game, handle, phase.name)])
    if (!goal.ok) throw new Error(`Server returned ${goal.status}`)
    if (!offer.ok) throw new Error(`Server returned ${offer.status}`)
    const state: FileState = { file: await goal.json() as GameFile, offer: await offer.json() as BackfillOffer }
    if (shown.current !== shownAs) return
    setFiles((prev) => ({ ...prev, [handle]: state }))
    setLinkIds((prev) => ({ ...prev, [handle]: state.offer.suggested?.id ?? '' }))
  }

  useEffect(() => {
    if (!apiUrl || !game) return
    const shownAs = `${game}/${phase.name}`
    shown.current = shownAs
    setRoms(null)
    setFiles({})
    setLinkIds({})
    setCopyOpen(null)
    setMessages({})
    setLoadError(null)
    const loadAll = async (): Promise<void> => {
      try {
        const r = await api.getAllGames(apiUrl)
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        const ofGame = (await r.json() as GameRomInfo[]).filter((rom) => rom.flavor === game)
        if (shown.current !== shownAs) return
        setRoms(ofGame)
        await Promise.all(ofGame.map((rom) => loadFile(rom.handle, shownAs)))
      } catch (err) {
        if (shown.current === shownAs) setLoadError(err instanceof Error ? err.message : 'Failed to load the game files')
      }
    }
    loadAll()
  }, [apiUrl, game, phase.name])

  // One write for one file, then that file is read again. A 404 has no body, so notFound says what it means.
  const act = async (handle: string, write: () => Promise<Response>, done: string, notFound?: string): Promise<void> => {
    const shownAs = shown.current
    setBusy(true)
    setMessages((prev) => ({ ...prev, [handle]: null }))
    try {
      const r = await write()
      if (r.status === 404 && notFound) throw new Error(notFound)
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      await loadFile(handle, shownAs)
      if (shown.current === shownAs) setMessages((prev) => ({ ...prev, [handle]: { msg: done, isError: false } }))
    } catch (err) {
      if (shown.current === shownAs) setMessages((prev) => ({ ...prev, [handle]: { msg: err instanceof Error ? err.message : 'Failed to save', isError: true } }))
    } finally {
      setBusy(false)
    }
  }

  const openCopy = (handle: string): void => {
    setCopyOpen(copyOpen === handle ? null : handle)
    setTicked(new Set())
  }

  const tick = (species: number, on: boolean): void => {
    const next = new Set(ticked)
    if (on) next.add(species)
    else next.delete(species)
    setTicked(next)
  }

  const speciesName = (species: number): string => phase.pokemon.find((p) => p.speciesId === species)?.name || `#${species}`
  const loaded = Object.values(files).map((f) => f.file)

  return (
    <div data-testid="milestone-files" className="pt-3 space-y-2">
      <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400">Game files</h3>
      {loadError ? (
        <p className="text-xs text-red-400">{loadError}</p>
      ) : roms === null ? (
        <p className="text-slate-500 text-sm">Loading game files...</p>
      ) : roms.length === 0 ? (
        <p className="text-slate-500 text-sm">No game files of {game} detected.</p>
      ) : (
        <>
          <p className="text-xs text-slate-500">{assignmentNote(loaded, phase.name)}</p>
          {roms.map((rom) => {
            const state = files[rom.handle]
            if (!state) return <p key={rom.handle} className="text-slate-500 text-xs">{rom.handle}: loading...</p>
            const link = linkOf(state.file, phase.name)
            const linkId = (linkIds[rom.handle] ?? '').trim()
            const message = messages[rom.handle]
            return (
              <div key={rom.handle} className="bg-card-border/20 rounded-lg px-3 py-2 space-y-1.5 border border-slate-700/40">
                <div className="flex items-center gap-2">
                  <span className="flex-1 text-xs text-white">{rom.handle}</span>
                  <label className="flex items-center gap-1 text-xs text-slate-400 cursor-pointer select-none">
                    <input
                      type="checkbox"
                      checked={state.file.assigned.includes(phase.name)}
                      onChange={(e) => act(rom.handle, () => api.setMilestoneAssigned(apiUrl, rom.handle, phase.name, e.target.checked), e.target.checked ? 'Assigned' : 'Unassigned')}
                      disabled={busy}
                      className="accent-indigo-500 disabled:opacity-50"
                    />
                    Assigned
                  </label>
                </div>
                {link ? (
                  <div className="flex items-center gap-2">
                    <span className="flex-1 text-xs text-slate-300">Linked to <code>{link.milestoneId}</code></span>
                    <button
                      type="button"
                      onClick={() => act(rom.handle, () => api.setMilestoneLink(apiUrl, game, rom.handle, link, false), 'Link removed')}
                      disabled={busy}
                      className={BUTTON}
                    >
                      Remove link
                    </button>
                  </div>
                ) : (
                  <div className="flex items-center gap-2">
                    <input
                      type="text"
                      value={linkIds[rom.handle] ?? ''}
                      onChange={(e) => setLinkIds({ ...linkIds, [rom.handle]: e.target.value })}
                      placeholder="Generated milestone id, e.g. 34-land"
                      className={`flex-1 ${INPUT}`}
                    />
                    <button
                      type="button"
                      onClick={() => act(rom.handle,
                        () => api.setMilestoneLink(apiUrl, game, rom.handle, { phase: phase.name, milestoneId: linkId }, true),
                        `Linked to ${linkId}`,
                        `No generated milestone "${linkId}" for this game file's version`)}
                      disabled={busy || linkId === ''}
                      className={BUTTON}
                    >
                      Link
                    </button>
                  </div>
                )}
                <button type="button" onClick={() => openCopy(rom.handle)} className="text-xs text-slate-400 hover:text-white">
                  {copyOpen === rom.handle ? '▾' : '▸'} Copy amounts…
                </button>
                {copyOpen === rom.handle && (
                  <div className="space-y-1">
                    {state.offer.copy.length === 0 ? (
                      <p className="text-xs text-slate-500">This milestone has no Pokémon to copy.</p>
                    ) : (
                      <>
                        <div className="grid grid-cols-[auto_1fr_1fr_1fr] gap-x-2 gap-y-0.5 text-xs">
                          <span />
                          <span className="text-slate-500">Pokémon</span>
                          <span className="text-slate-500">This milestone</span>
                          <span className="text-slate-500">The goal now</span>
                          {state.offer.copy.map((o) => (
                            <React.Fragment key={o.species}>
                              <input
                                type="checkbox"
                                checked={ticked.has(o.species)}
                                onChange={(e) => tick(o.species, e.target.checked)}
                                className="accent-indigo-500"
                              />
                              <span className="text-slate-300">{speciesName(o.species)}</span>
                              <span className="text-slate-300">{entriesText(o.phase)}</span>
                              <span className="text-slate-400">{entriesText(o.goal)}</span>
                            </React.Fragment>
                          ))}
                        </div>
                        <button
                          type="button"
                          onClick={() => {
                            const species = chosenSpecies(state.offer.copy, ticked)
                            act(rom.handle, () => api.copyAmounts(apiUrl, game, rom.handle, phase.name, species), `Copied ${species.length} species into the goal`)
                          }}
                          disabled={busy || ticked.size === 0}
                          className={BUTTON}
                        >
                          Copy chosen
                        </button>
                      </>
                    )}
                  </div>
                )}
                {message && (
                  <div className={`text-xs ${message.isError ? 'text-red-400' : 'text-green-400'}`}>{message.msg}</div>
                )}
              </div>
            )
          })}
        </>
      )}
    </div>
  )
}
