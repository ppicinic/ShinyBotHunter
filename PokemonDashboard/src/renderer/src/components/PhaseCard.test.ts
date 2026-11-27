import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { GameEncounterResponse, GameEncounterResponseBase, MilestoneLocation, PhaseEncounterResponse, PhasePokemonInfo, PlanTime } from '../types/api'
import { PhaseCard, phaseCardRows } from './PhaseCard'

function counts(encounters: number, shinyEncounters: number, shiniesCaught: number): { encounters: number; shinyEncounters: number; shiniesCaught: number } {
  return { encounters, shinyEncounters, shiniesCaught }
}

function pokemon(name: string, speciesId: number): PhasePokemonInfo {
  return { name, speciesId, amount: 1, formCode: name.toLowerCase(), requireSpecificForm: false, hasFemaleForm: false }
}

function card(
  rows: PhasePokemonInfo[],
  location: MilestoneLocation | null,
  live = true,
  timeOfDay: PlanTime | null = null,
  encounterData: GameEncounterResponse | null = null
): string {
  const phaseInfo = { gameName: 'HGSS', name: 'Route 30', pokemon: rows, isStatic: false }
  const subscription = { game: 'HGSS', phase: 'Route 30', gameName: 'HG-QA', encounterData, phaseInfo, location, live, timeOfDay, notFound: false }
  return renderToStaticMarkup(createElement(PhaseCard, { subscription }))
}

describe('PhaseCard', () => {
  it("a milestone with no location draws today's rows", () => {
    const encounterData = {
      gameSpecificResponse: { 'HG-QA': { pokemonEncounters: { '#16': counts(12, 0, 0) }, phaseEncounters: { 'Route 30': { phaseCount: 7, pokemonEncounters: { '#16': counts(5, 0, 0) } } } } }
    } as unknown as GameEncounterResponse

    const html = card([pokemon('Pidgey', 16), pokemon('Rattata', 19)], null, false, null, encounterData)

    expect([html.includes('Pidgey'), html.includes('Rattata'), html.includes('>5<'), html.includes('>12<')]).toEqual([true, true, true, true])
    expect([html.includes('row-share'), html.includes('not live')]).toEqual([false, false])
  })
})

describe('phaseCardRows', () => {
  it("a stored phase's rows are today's", () => {
    const pokemon: PhasePokemonInfo[] = [
      { name: 'Spinarak', speciesId: 167, amount: 2, formCode: 'spinarak', requireSpecificForm: false, hasFemaleForm: false },
      { name: 'Bidoof', speciesId: 399, amount: 1, formCode: 'bidoof;f', requireSpecificForm: true, hasFemaleForm: true },
      { name: 'Bidoof', speciesId: 399, amount: 0, formCode: 'bidoof', requireSpecificForm: true, hasFemaleForm: true }
    ]
    const phaseData = { pokemonEncounters: { '#167': counts(4, 0, 0), 'bidoof;f': counts(2, 0, 0) } } as unknown as PhaseEncounterResponse
    const scopedData = {
      pokemonEncounters: { '#167': counts(10, 3, 1), 'bidoof;f': counts(6, 1, 1), bidoof: counts(7, 0, 0) }
    } as unknown as GameEncounterResponseBase

    const result = phaseCardRows(pokemon, phaseData, scopedData)

    expect(result).toEqual([
      { key: '#167', name: 'Spinarak', sprite: 'spinarak', genderSymbol: '', amount: 2, caught: 1, encountersHere: 4, encountersOnFile: 10, missed: 2 },
      { key: 'bidoof;f', name: 'Bidoof', sprite: 'bidoof;f', genderSymbol: '♀', amount: 1, caught: 1, encountersHere: 2, encountersOnFile: 6, missed: 0 },
      { key: 'bidoof', name: 'Bidoof', sprite: 'bidoof', genderSymbol: '♂', amount: 0, caught: 0, encountersHere: 0, encountersOnFile: 7, missed: 0 }
    ])
  })
})
