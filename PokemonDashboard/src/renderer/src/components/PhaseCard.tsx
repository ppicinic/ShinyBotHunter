import React, { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { GameEncounterResponseBase, MilestoneLocation, PhaseEncounterResponse, PhasePokemonInfo, PlanTime, SubscriptionState } from '../types/api'
import { useAppContext } from '../context/AppContext'
import * as api from '../services/apiClient'
import { PokemonSprite } from './PokemonSprite'
import { WindowCloseButton } from './WindowChrome'

const GAME_DISPLAY_NAMES: Record<string, string> = {
  HGSS: 'HeartGold & SoulSilver'
}

function getGameDisplayName(game: string): string {
  return GAME_DISPLAY_NAMES[game] ?? game
}

// The one row shape the table draws
interface CardRow {
  // The count key, unique per row: a female and a male row, or Unown's 28, share a species
  key: string
  name: string
  sprite: string
  genderSymbol: string
  amount: number
  caught: number
  encountersHere: number
  encountersOnFile: number
  missed: number
  // From the location's row at the same position; null and empty without a location
  share: number | null
  times: PlanTime[]
}

// The time-of-day pills; "all day" follows the theme's neutrals and border
const PILL_COLORS: Record<PlanTime | 'All', string> = {
  Morning: 'text-amber-300 border-amber-300/40',
  Day: 'text-sky-300 border-sky-300/40',
  Night: 'text-violet-300 border-violet-300/40',
  All: 'text-slate-400 border-card-border'
}

function TimePill({ time }: { time: PlanTime | 'All' }): JSX.Element {
  return (
    <span data-testid="time-pill" className={`ml-1 text-xs px-1 rounded border ${PILL_COLORS[time]}`}>
      {time === 'All' ? 'all day' : time.toLowerCase()}
    </span>
  )
}

// A row hidden now is not drawn, nor one at a zero share (Repel blocks it): only what can be met now shows
export function phaseCardRows(
  pokemon: PhasePokemonInfo[],
  phaseData: PhaseEncounterResponse | undefined,
  scopedData: GameEncounterResponseBase | null | undefined,
  location: MilestoneLocation | null
): CardRow[] {
  return pokemon.flatMap((pkmn, i) => {
    const at = location?.rows[i]
    if (at && (at.hiddenNow || at.share === 0)) return []
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
    return [{
      key: countKey,
      name: pkmn.name,
      sprite: pkmn.formCode,
      genderSymbol,
      amount: pkmn.amount,
      caught: shiniesCaught,
      encountersHere: phaseCounts?.encounters ?? 0,
      encountersOnFile: totalCounts?.encounters ?? 0,
      missed: shiniesMissed,
      share: at?.share ?? null,
      times: at?.times ?? []
    }]
  })
}

interface PhaseCardProps {
  subscription: SubscriptionState
  windowChrome?: boolean
}

export function PhaseCard({ subscription, windowChrome }: PhaseCardProps): JSX.Element {
  const { config, connectionState } = useAppContext()
  const { game, phase, gameName, encounterData, phaseInfo, location, notFound } = subscription

  // When gameName is set, use the game-file-specific response; otherwise use the game-level response
  const scopedData: GameEncounterResponseBase | null | undefined = gameName
    ? encounterData?.gameSpecificResponse?.[gameName]
    : encounterData

  const phaseData = scopedData?.phaseEncounters?.[phase]
  const totalPhaseEncounters = phaseData?.allPokemonCounts?.encounters ?? 0
  const totalPhaseShiny = phaseData?.allPokemonCounts?.shinyEncounters ?? 0
  const phaseCount = phaseData?.phaseCount ?? null

  const rows: CardRow[] = phaseCardRows(phaseInfo?.pokemon ?? [], phaseData, scopedData, location)
  // Ruby and Sapphire, and a location met all day, carry no pills: only a location with a timed row has them
  const timed = location?.rows.some((r) => r.times.length === 1 || r.times.length === 2) ?? false

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
    if (!body || rows.length === 0) {
      setRowHeight(null)
      return
    }
    const measure = (): void => {
      const available = body.clientHeight - (headRef.current?.offsetHeight ?? 0)
      setRowHeight(Math.max(ROW_MIN, Math.floor(available / rows.length)))
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(body)
    return () => observer.disconnect()
  }, [rows.length])

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
            Encounters: <span data-testid="phase-encounter-count" className="text-white font-semibold">{totalPhaseEncounters.toLocaleString()}</span>
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
        {(phaseInfo?.pokemon.length ?? 0) === 0 ? (
          <div className="flex items-center justify-center h-full text-slate-500 text-sm">
            {notFound
              ? `Milestone "${phase}" was not found.`
              : phaseInfo === null
                ? 'Loading milestone requirements...'
                : 'No Pokémon defined for this milestone.'}
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
              {rows.map(({ key, name, sprite, genderSymbol, amount: amountRequired, caught: shiniesCaught, encountersHere, encountersOnFile, missed: shiniesMissed, share, times }) => {
                // Amount 0 means "track this pokemon's encounters but don't require it". Without the
                // guard, 0 >= 0 marks the row complete and shows a meaningless "0/0".
                const isTrackedOnly = amountRequired === 0
                const isComplete = !isTrackedOnly && shiniesCaught >= amountRequired

                return (
                  <tr
                    key={key}
                    className={`border-b border-card-border/30 ${isComplete ? 'bg-green-900/20' : ''}`}
                    style={rowHeight === null ? undefined : { height: rowHeight }}
                  >
                    {/* Sprite */}
                    <td className="py-0 pr-3 text-center">
                      {/* Phase card shows target (shiny) sprites since these are the pokemon being hunted */}
                      <PokemonSprite formCode={sprite} isShiny name={name} size={spriteSize} />
                    </td>

                    {/* Name */}
                    <td className="py-0 pr-3 text-center">
                      <span className="text-white font-semibold text-sm capitalize">{name}</span>
                      {genderSymbol && (
                        <span className={`ml-1 text-sm ${genderSymbol === '♀' ? 'text-pink-400' : 'text-sky-400'}`}>{genderSymbol}</span>
                      )}
                      {isComplete && (
                        <span className="ml-2 text-xs px-1.5 py-0.5 rounded bg-green-700/50 text-green-300">✓</span>
                      )}
                      {timed && (times.length === 3 ? <TimePill time="All" /> : times.map((t) => <TimePill key={t} time={t} />))}
                      {share !== null && (
                        <div data-testid="row-share" className="text-xs text-slate-400">{(share * 100).toFixed(1)} %</div>
                      )}
                    </td>

                    {/* Phase encounters */}
                    <td className="py-0 pr-3 text-center text-white font-medium text-sm">
                      {encountersHere.toLocaleString()}
                    </td>

                    {/* Game encounters */}
                    <td className="py-0 pr-3 text-center text-white font-medium text-sm">
                      {encountersOnFile.toLocaleString()}
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
