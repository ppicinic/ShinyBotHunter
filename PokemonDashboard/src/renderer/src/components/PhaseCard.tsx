import React, { useEffect, useState } from 'react'
import type { GameEncounterResponseBase, PhasePokemonInfo, SubscriptionState } from '../types/api'
import { useAppContext } from '../context/AppContext'
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

  // Encounter rate polling
  const [encounterRate, setEncounterRate] = useState<number | null>(null)
  const pollingInterval = config?.encounterRatePollingIntervalMs ?? 60000

  useEffect(() => {
    if (!config?.apiUrl) return

    const fetchRate = async (): Promise<void> => {
      try {
        const encodedPhase = encodeURIComponent(phase)
        const url = gameName
          ? `${config.apiUrl}/api/phaseinfo/encounterRate/${game}/${encodedPhase}/${encodeURIComponent(gameName)}`
          : `${config.apiUrl}/api/phaseinfo/encounterRate/${game}/${encodedPhase}`
        const res = await fetch(url)
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

      <div className="card-body overflow-auto flex-1">
        {pokemonList.length === 0 ? (
          <div className="flex items-center justify-center h-full text-slate-500 text-sm">
            {phaseInfo === null ? 'Loading milestone requirements...' : 'No Pokémon defined for this milestone.'}
          </div>
        ) : (
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-card-border">
                <th className="text-center text-xs text-slate-400 font-medium pb-2 pr-3 w-24"></th>
                <th className="text-center text-xs text-slate-400 font-medium pb-2 pr-3">Name</th>
                <th className="text-center text-xs text-slate-400 font-medium pb-2 pr-3">Milestone Encounters</th>
                <th className="text-center text-xs text-slate-400 font-medium pb-2 pr-3">Game Encounters</th>
                <th className="text-center text-xs text-slate-400 font-medium pb-2">Shinies Caught</th>
              </tr>
            </thead>
            <tbody>
              {pokemonList.map((pkmn) => {
                const phaseCounts = phaseData?.pokemonEncounters?.[pkmn.speciesId]
                const totalCounts = scopedData?.pokemonEncounters?.[pkmn.speciesId]
                const shiniesCaught = totalCounts?.shiniesCaught ?? 0
                const shiniesEncountered = totalCounts?.shinyEncounters ?? 0
                // Missed = shiny encounters that were not caught
                const shiniesMissed = Math.max(0, shiniesEncountered - shiniesCaught)
                const amountRequired = pkmn.amount
                const isComplete = shiniesCaught >= amountRequired
                // Phase card shows target (shiny) sprites since these are the pokemon being hunted
                const spritePath = getSpriteUrl(config?.spritesRootPath ?? '', pkmn.formCode, true)

                return (
                  <tr
                    key={pkmn.speciesId}
                    className={`border-b border-card-border/30 ${isComplete ? 'bg-green-900/20' : ''}`}
                  >
                    {/* Sprite */}
                    <td className="py-2 pr-3 text-center">
                      <img
                        src={spritePath}
                        alt={pkmn.name}
                        className="w-24 h-24 object-contain mx-auto"
                        onError={(e) => {
                          (e.target as HTMLImageElement).style.display = 'none'
                        }}
                      />
                    </td>

                    {/* Name */}
                    <td className="py-2 pr-3 text-center">
                      <span className="text-white font-semibold text-sm capitalize">{pkmn.name}</span>
                      {isComplete && (
                        <span className="ml-2 text-xs px-1.5 py-0.5 rounded bg-green-700/50 text-green-300">✓</span>
                      )}
                    </td>

                    {/* Phase encounters */}
                    <td className="py-2 pr-3 text-center text-white font-medium text-sm">
                      {(phaseCounts?.encounters ?? 0).toLocaleString()}
                    </td>

                    {/* Game encounters */}
                    <td className="py-2 pr-3 text-center text-white font-medium text-sm">
                      {(totalCounts?.encounters ?? 0).toLocaleString()}
                    </td>

                    {/* Shinies caught / needed */}
                    <td className={`py-2 text-center font-medium text-sm ${isComplete ? 'text-green-400' : 'text-white'}`}>
                      <div>{shiniesCaught}/{amountRequired}</div>
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
