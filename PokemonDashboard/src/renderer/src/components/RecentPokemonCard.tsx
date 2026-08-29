import React from 'react'
import FlipMove from 'react-flip-move'
import type { PokemonData } from '../types/api'
import { useAppContext } from '../context/AppContext'
import { getSpriteUrl } from '../utils/sprites'
import { WindowCloseButton } from './WindowChrome'

const NATURE_NAMES: Record<number, string> = {
  0: 'Hardy', 1: 'Lonely', 2: 'Brave', 3: 'Adamant', 4: 'Naughty',
  5: 'Bold', 6: 'Docile', 7: 'Relaxed', 8: 'Impish', 9: 'Lax',
  10: 'Timid', 11: 'Hasty', 12: 'Serious', 13: 'Jolly', 14: 'Naive',
  15: 'Modest', 16: 'Mild', 17: 'Quiet', 18: 'Bashful', 19: 'Rash',
  20: 'Calm', 21: 'Gentle', 22: 'Sassy', 23: 'Careful', 24: 'Quirky'
}

const GENDER_SYMBOLS: Record<number, string> = {
  0: '⚲',
  1: '♂',
  2: '♀'
}

interface PokemonEntryRowProps {
  pokemon: PokemonData
  spritesRootPath: string
}

// FlipMove requires forwardRef on child components
const PokemonEntryRow = React.forwardRef<HTMLDivElement, PokemonEntryRowProps>(
  function PokemonEntryRow({ pokemon, spritesRootPath }, ref) {
    const spriteUrl = getSpriteUrl(spritesRootPath, pokemon.formCode, pokemon.isShiny)

    const ivValues = [
      { label: 'HP', value: pokemon.ivHp },
      { label: 'Atk', value: pokemon.ivAtk },
      { label: 'Def', value: pokemon.ivDef },
      { label: 'SpAtk', value: pokemon.ivSpAtk },
      { label: 'SpDef', value: pokemon.ivSpDef },
      { label: 'Spe', value: pokemon.ivSpe }
    ]

    return (
      <div
        ref={ref}
        className={`p-2 rounded-lg mb-2 ${pokemon.isShiny ? 'shiny-row' : 'bg-card-border/20'}`}
      >
        <div className="flex items-start gap-3">
          {/* Sprite */}
          <div className="flex-shrink-0 w-24 h-24 flex items-center justify-center">
            <img
              src={spriteUrl}
              alt={pokemon.name}
              className="pokemon-sprite w-24 h-24 object-contain"
              onError={(e) => {
                (e.target as HTMLImageElement).style.display = 'none'
              }}
            />
          </div>

          {/* Main info */}
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-1">
              {pokemon.isShiny && <span className="shiny-glow text-sm">✨</span>}
              <span className={`font-semibold text-sm capitalize ${pokemon.isShiny ? 'shiny-glow' : 'text-white'}`}>
                {pokemon.name}
              </span>
              <span className="text-slate-400 text-xs">{GENDER_SYMBOLS[pokemon.gender] ?? '?'}</span>
              <span className="text-slate-400 text-xs">Lv.{pokemon.level}</span>
              <span className="text-slate-400 text-xs ml-auto">{NATURE_NAMES[pokemon.nature] ?? '?'}</span>
            </div>

            {/* ShinyValue */}
            <div className="flex items-center gap-2 mb-1.5">
              <span className="text-xs text-slate-400">SV:</span>
              <span className={`text-xs font-bold ${pokemon.shinyValue < 8 ? 'shiny-glow' : 'text-slate-300'}`}>
                {pokemon.shinyValue}
              </span>
            </div>

            {/* IVs */}
            <div className="stat-grid">
              {ivValues.map(({ label, value }) => (
                <div key={label} className="stat-cell">
                  <span className="stat-label">{label}</span>
                  <span className={`stat-value ${value === 31 ? 'max' : ''}`}>{value}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    )
  }
)

/**
 * Pairs each encounter with a key that is unique within the list.
 *
 * A PID is not unique. Ruby/Sapphire reach a limited set of RNG outcomes, so the same PID -- and
 * with it the same nature and IVs, a genuinely identical pokemon -- turns up repeatedly across
 * emulators; a ten-row window has held four repeats. Keying rows by PID alone therefore handed
 * FlipMove duplicate keys, which broke reconciliation and left stale rows pinned to the top of the
 * list while newer encounters arrived beneath them.
 *
 * The first occurrence keeps the bare PID so a row's identity survives the list shifting down,
 * which is what lets FlipMove animate it into its new position instead of remounting it.
 */
function encounterKeys(encounters: PokemonData[]): { pokemon: PokemonData; key: string }[] {
  const seen = new Map<string, number>()
  return encounters.map((pokemon) => {
    const pid = String(pokemon.pid)
    const occurrence = seen.get(pid) ?? 0
    seen.set(pid, occurrence + 1)
    return { pokemon, key: occurrence === 0 ? pid : `${pid}#${occurrence}` }
  })
}

interface RecentPokemonCardProps {
  windowChrome?: boolean
}

export function RecentPokemonCard({ windowChrome }: RecentPokemonCardProps = {}): JSX.Element {
  const { recentEncounters, config, connectionState } = useAppContext()

  return (
    <div className="dashboard-card h-full">
      <div className={`card-header ${windowChrome ? 'window-drag-region' : ''}`}>
        <span className="card-header-title">Recent Encounters</span>
        <div className="flex items-center gap-3">
          <span className="text-xs text-slate-500">{recentEncounters.length} shown</span>
          {windowChrome && (
            <>
              <span className={`status-dot ${connectionState}`} />
              <WindowCloseButton />
            </>
          )}
        </div>
      </div>

      <div className="card-body">
        {recentEncounters.length === 0 ? (
          <div className="flex items-center justify-center h-full text-slate-500 text-sm">
            No encounters yet...
          </div>
        ) : (
          <FlipMove
            duration={600}
            easing="ease-out"
            enterAnimation="elevator"
            leaveAnimation="fade"
            typeName={null}
          >
            {encounterKeys(recentEncounters).map(({ pokemon, key }) => (
              <PokemonEntryRow
                key={key}
                pokemon={pokemon}
                spritesRootPath={config?.spritesRootPath ?? ''}
              />
            ))}
          </FlipMove>
        )}
      </div>
    </div>
  )
}
