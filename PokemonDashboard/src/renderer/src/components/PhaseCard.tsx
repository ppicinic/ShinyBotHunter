import React, { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { GameEncounterResponseBase, PhasePokemonInfo, SubscriptionState } from '../types/api'
import { useAppContext } from '../context/AppContext'
import * as api from '../services/apiClient'
import { getSpriteUrl } from '../utils/sprites'
import { WindowCloseButton } from './WindowChrome'

const GAME_DISPLAY_NAMES: Record<string, string> = {
  HGSS: 'HeartGold & SoulSilver'
}

function getGameDisplayName(game: string): string {
  return GAME_DISPLAY_NAMES[game] ?? game
}

interface PhaseCardProps {
  subscription: SubscriptionState
  windowChrome?: boolean
}

export function PhaseCard({ subscription, windowChrome }: PhaseCardProps): JSX.Element {
  const { config, connectionState } = useAppContext()
  const { game, phase, gameName, encounterData, phaseInfo } = subscription

  // When gameName is set, use the game-file-specific response; otherwise use the game-level response
  const scopedData: GameEncounterResponseBase | null | undefined = gameName
    ? encounterData?.gameSpecificResponse?.[gameName]
    : encounterData

  const phaseData = scopedData?.phaseEncounters?.[phase]
  const totalPhaseEncounters = phaseData?.allPokemonCounts?.encounters ?? 0
  const totalPhaseShiny = phaseData?.allPokemonCounts?.shinyEncounters ?? 0
  const phaseCount = phaseData?.phaseCount ?? null

  const pokemonList: PhasePokemonInfo[] = phaseInfo?.pokemon ?? []

  // The phase card sizes its rows to the space available rather than scrolling: the whole point is
  // to see every milestone pokemon at a glance. Sprites shrink with the rows but only within
  // SPRITE_MIN..SPRITE_MAX, so a short milestone doesn't get comically large art and a long one
  // stays legible. Recent encounters and the admin card still scroll -- their content is unbounded.
  const ROW_MIN = 28
  const SPRITE_MIN = 24
  const SPRITE_MAX = 96
  const bodyRef = useRef<HTMLDivElement>(null)
  const headRef = useRef<HTMLTableSectionElement>(null)
  const [rowHeight, setRowHeight] = useState<number | null>(null)

  useLayoutEffect(() => {
    const body = bodyRef.current
    if (!body || pokemonList.length === 0) {
      setRowHeight(null)
      return
    }
    const measure = (): void => {
      const available = body.clientHeight - (headRef.current?.offsetHeight ?? 0)
      setRowHeight(Math.max(ROW_MIN, Math.floor(available / pokemonList.length)))
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(body)
    return () => observer.disconnect()
  }, [pokemonList.length])

  // Leave room for the row's own vertical padding so sprites never force a row taller than its slot.
  const spriteSize = rowHeight === null
    ? SPRITE_MAX
    : Math.min(SPRITE_MAX, Math.max(SPRITE_MIN, rowHeight - 4))

  // Encounter rate polling
  const [encounterRate, setEncounterRate] = useState<number | null>(null)
  const pollingInterval = config?.encounterRatePollingIntervalMs ?? 60000

  useEffect(() => {
    if (!config?.apiUrl) return

    const fetchRate = async (): Promise<void> => {
      try {
        const res = await api.getEncounterRate(config.apiUrl, game, phase, gameName)
        if (!res.ok) return
        const rate = await res.json() as number
        setEncounterRate(rate)
      } catch {
        // silently ignore polling errors
      }
    }

    // Fetch immediately on mount, then on interval
    fetchRate()
    const timer = setInterval(fetchRate, pollingInterval)
    return () => clearInterval(timer)
  }, [config?.apiUrl, game, phase, gameName, pollingInterval])

  return (
    <div className="dashboard-card h-full flex flex-col">
      <div className={`card-header ${windowChrome ? 'window-drag-region' : ''}`}>
        <div>
          <span className="text-xs text-slate-500 mr-2">{gameName ?? getGameDisplayName(game)}</span>
          <span className="card-header-title">{phase}</span>
        </div>
        <div className="flex items-center gap-3 text-xs text-slate-400">
          <span>
            Encounters: <span className="text-white font-semibold">{totalPhaseEncounters.toLocaleString()}</span>
          </span>
          {totalPhaseShiny > 0 && (
            <span className="shiny-glow font-semibold">✨ {totalPhaseShiny}</span>
          )}
          {windowChrome && (
            <>
              <span className={`status-dot ${connectionState}`} />
              <WindowCloseButton />
            </>
          )}
        </div>
      </div>

      <div ref={bodyRef} className="flex-1 overflow-hidden px-2 py-1">
        {pokemonList.length === 0 ? (
          <div className="flex items-center justify-center h-full text-slate-500 text-sm">
            {phaseInfo === null ? 'Loading milestone requirements...' : 'No Pokémon defined for this milestone.'}
          </div>
        ) : (
          <table className="w-full border-collapse">
            <thead ref={headRef}>
              <tr className="border-b border-card-border">
                <th className="text-center text-xs text-slate-400 font-medium pb-1 pr-3 w-24"></th>
                <th className="text-center text-xs text-slate-400 font-medium pb-1 pr-3">Name</th>
                <th className="text-center text-xs text-slate-400 font-medium pb-1 pr-3">Milestone Encounters</th>
                <th className="text-center text-xs text-slate-400 font-medium pb-1 pr-3">Game Encounters</th>
                <th className="text-center text-xs text-slate-400 font-medium pb-1">Shinies Caught</th>
              </tr>
            </thead>
            <tbody>
              {pokemonList.map((pkmn) => {
                // A row that pins a form counts only that form (male and female, or Unown's 28,
                // stay separate); a row that accepts any form counts the whole species. Must match
                // CacheLoadRequest.SpeciesKey on the server, which decides what got cached.
                const countKey = pkmn.requireSpecificForm ? pkmn.formCode : `#${pkmn.speciesId}`
                const phaseCounts = phaseData?.pokemonEncounters?.[countKey]
                const totalCounts = scopedData?.pokemonEncounters?.[countKey]
                const shiniesCaught = totalCounts?.shiniesCaught ?? 0
                const shiniesEncountered = totalCounts?.shinyEncounters ?? 0
                // Missed = shiny encounters that were not caught
                const shiniesMissed = Math.max(0, shiniesEncountered - shiniesCaught)
                const amountRequired = pkmn.amount
                // Amount 0 means "track this pokemon's encounters but don't require it". Without the
                // guard, 0 >= 0 marks the row complete and shows a meaningless "0/0".
                const isTrackedOnly = amountRequired === 0
                const isComplete = !isTrackedOnly && shiniesCaught >= amountRequired
                // Phase card shows target (shiny) sprites since these are the pokemon being hunted
                // Only shown when the row actually pins a form -- otherwise it accepts either
                // gender and marking it would be a lie. Within a pinned row, ";f" is the only thing
                // ever encoded, so a plain code means male when the species has a female form.
                const genderSymbol = !pkmn.requireSpecificForm
                  ? ''
                  : pkmn.formCode.endsWith(';f')
                    ? '♀'
                    : pkmn.hasFemaleForm
                      ? '♂'
                      : ''
                const spritePath = getSpriteUrl(pkmn.formCode, true)

                return (
                  <tr
                    key={pkmn.speciesId}
                    className={`border-b border-card-border/30 ${isComplete ? 'bg-green-900/20' : ''}`}
                    style={rowHeight === null ? undefined : { height: rowHeight }}
                  >
                    {/* Sprite */}
                    <td className="py-0 pr-3 text-center">
                      <img
                        src={spritePath}
                        alt={pkmn.name}
                        style={{ width: spriteSize, height: spriteSize }}
                        className="object-contain mx-auto"
                        onError={(e) => {
                          (e.target as HTMLImageElement).style.display = 'none'
                        }}
                      />
                    </td>

                    {/* Name */}
                    <td className="py-0 pr-3 text-center">
                      <span className="text-white font-semibold text-sm capitalize">{pkmn.name}</span>
                      {genderSymbol && (
                        <span className={`ml-1 text-sm ${genderSymbol === '♀' ? 'text-pink-400' : 'text-sky-400'}`}>{genderSymbol}</span>
                      )}
                      {isComplete && (
                        <span className="ml-2 text-xs px-1.5 py-0.5 rounded bg-green-700/50 text-green-300">✓</span>
                      )}
                    </td>

                    {/* Phase encounters */}
                    <td className="py-0 pr-3 text-center text-white font-medium text-sm">
                      {(phaseCounts?.encounters ?? 0).toLocaleString()}
                    </td>

                    {/* Game encounters */}
                    <td className="py-0 pr-3 text-center text-white font-medium text-sm">
                      {(totalCounts?.encounters ?? 0).toLocaleString()}
                    </td>

                    {/* Shinies caught / needed */}
                    <td className={`py-0 text-center font-medium text-sm ${isComplete ? 'text-green-400' : 'text-white'}`}>
                      {isTrackedOnly ? (
                        <div>
                          {shiniesCaught}
                          <span className="ml-1 text-xs text-slate-500 font-normal">tracked</span>
                        </div>
                      ) : (
                        <div>{shiniesCaught}/{amountRequired}</div>
                      )}
                      {shiniesMissed > 0 && (
                        <div className="text-xs text-red-400 font-normal">({shiniesMissed} missed)</div>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Footer: phase count + encounter rate */}
      <div className="flex items-center justify-end gap-4 px-3 py-1.5 border-t border-card-border bg-card-header/50 flex-shrink-0">
        <span className="text-xs text-slate-400">
          Phase:{' '}
          <span className="text-white font-semibold">
            {phaseCount !== null ? phaseCount.toLocaleString() : '—'}
          </span>
        </span>
        <span className="text-xs text-slate-400">
          Rate:{' '}
          <span className="text-white font-semibold">
            {encounterRate !== null ? `${encounterRate.toFixed(2)}/hr` : '—'}
          </span>
        </span>
      </div>
    </div>
  )
}
