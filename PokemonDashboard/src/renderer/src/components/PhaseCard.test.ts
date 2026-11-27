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
  const phaseInfo = { gameName: 'HGSS', name: 'Route 30', pokemon: rows, isStatic: false, location: null, gameFiles: [], archived: false }
  const subscription = { game: 'HGSS', phase: 'Route 30', gameName: 'HG-QA', encounterData, phaseInfo, location, live, timeOfDay, notFound: false }
  return renderToStaticMarkup(createElement(PhaseCard, { subscription }))
}

function at(key: string, times: PlanTime[], share: number | null, hiddenNow = false): MilestoneLocation['rows'][number] {
  return { key, version: null, times, share, hiddenNow }
}

// The card's header, up to the body below it
function header(html: string): string {
  return html.slice(html.indexOf('card-header'), html.indexOf('flex-1 overflow-hidden'))
}

describe('PhaseCard', () => {
  it("a milestone with no location draws today's rows without a footer", () => {
    const encounterData = {
      gameSpecificResponse: { 'HG-QA': { pokemonEncounters: { '#16': counts(12, 0, 0) }, phaseEncounters: { 'Route 30': { phaseCount: 7, pokemonEncounters: { '#16': counts(5, 0, 0) } } } } }
    } as unknown as GameEncounterResponse

    const html = card([pokemon('Pidgey', 16), pokemon('Rattata', 19)], null, false, null, encounterData)

    expect([html.includes('Pidgey'), html.includes('Rattata'), html.includes('>5<'), html.includes('>12<')]).toEqual([true, true, true, true])
    expect([html.includes('row-share'), html.includes('time-pill'), html.includes('hidden-summary'), html.includes('not live')]).toEqual([false, false, false, false])
    expect(header(html)).toMatch(/Phase: <span[^>]*>7<\/span>/)
    expect(html).not.toContain('border-t')
  })

  it('draws the phase count, the rate and not live in the header', () => {
    const encounterData = { gameSpecificResponse: { 'HG-QA': { phaseEncounters: { 'Route 30': { phaseCount: 42 } } } } } as unknown as GameEncounterResponse
    const location = { rows: [at('#16', ['Morning', 'Day'], null)], hiddenUntil: {} }

    const html = card([pokemon('Pidgey', 16)], location, false, 'Night', encounterData)

    expect(header(html)).toMatch(/Phase: <span[^>]*>42<\/span>/)
    expect(header(html)).toMatch(/Rate: <span[^>]*>—<\/span>/)
    expect(header(html)).toContain('not live (last seen night)')
    expect(html).not.toContain('border-t')
  })

  it('says not live without a last-seen time', () => {
    const location = { rows: [at('#16', ['Morning', 'Day'], null)], hiddenUntil: {} }

    const html = card([pokemon('Pidgey', 16)], location, false, null)

    expect(header(html)).toMatch(/>not live<\/span>/)
  })

  it('draws the share under the name for a milestone with a location', () => {
    const location = { rows: [at('#69', ['Morning', 'Day', 'Night'], 0.308)], hiddenUntil: {} }

    const html = card([pokemon('Bellsprout', 69)], location)

    expect(html).toMatch(/Bellsprout<\/span><div data-testid="row-share"[^>]*>30.8 %<\/div><\/td>/)
    expect(html).not.toContain('>Share<')
  })

  it('marks a morning-and-day row with both pills and an all-day row with all day', () => {
    const location = { rows: [at('#16', ['Morning', 'Day'], 0.4), at('#19', ['Morning', 'Day', 'Night'], 0.25)], hiddenUntil: {} }

    const html = card([pokemon('Pidgey', 16), pokemon('Rattata', 19)], location)

    expect(html).toMatch(/Pidgey<\/span><span data-testid="time-pill"[^>]*>morning<\/span><span data-testid="time-pill"[^>]*>day<\/span><div/)
    expect(html).toMatch(/Rattata<\/span><span data-testid="time-pill"[^>]*>all day<\/span><div/)
  })

  it('draws no pill when no row of the location is timed', () => {
    const location = { rows: [at('#263', ['Morning', 'Day', 'Night'], 0.45), at('#265', ['Morning', 'Day', 'Night'], 0.55)], hiddenUntil: {} }

    const html = card([pokemon('Zigzagoon', 263), pokemon('Wurmple', 265)], location)

    expect([html.includes('row-share'), html.includes('time-pill')]).toEqual([true, false])
  })

  it('draws the rows hidden now as one dim last row', () => {
    const location = {
      rows: [at('#16', ['Morning', 'Day'], 0.6), at('#163', ['Night'], null, true), at('#167', ['Night'], null, true), at('#19', ['Morning', 'Day', 'Night'], 0.4)],
      hiddenUntil: { Night: ['Hoothoot', 'Spinarak'] }
    }

    const html = card([pokemon('Pidgey', 16), pokemon('Hoothoot', 163), pokemon('Spinarak', 167), pokemon('Rattata', 19)], location, true, 'Day')

    const last = html.slice(html.lastIndexOf('<tr'))
    expect(last).toMatch(/^<tr data-testid="hidden-summary"><td colSpan="5" class="[^"]*text-slate-500[^"]*">/)
    expect(last).toMatch(/2 more at<span data-testid="time-pill"[^>]*>night<\/span>: Hoothoot, Spinarak/)
    expect(html.slice(0, html.lastIndexOf('<tr'))).not.toMatch(/Hoothoot|Spinarak/)
  })

  it("draws two hidden groups in the response's order", () => {
    const location = {
      rows: [at('#163', ['Night'], null, true), at('#16', ['Morning'], null, true), at('#21', ['Day'], 1)],
      hiddenUntil: { Night: ['Hoothoot'], Morning: ['Pidgey'] }
    }

    const html = card([pokemon('Hoothoot', 163), pokemon('Pidgey', 16), pokemon('Spearow', 21)], location, true, 'Day')

    expect(html).toMatch(/1 more at<span[^>]*>night<\/span>: Hoothoot<\/span><span> · 1 more at<span[^>]*>morning<\/span>: Pidgey/)
  })

  it('keeps the table with only the summary row when every row is hidden now', () => {
    const location = { rows: [at('#163', ['Night'], null, true)], hiddenUntil: { Night: ['Hoothoot'] } }

    const html = card([pokemon('Hoothoot', 163)], location, true, 'Day')

    expect([html.includes('<table'), html.includes('hidden-summary'), html.includes('No Pokémon')]).toEqual([true, true, false])
    expect(html.match(/<tr/g)).toHaveLength(2)
  })

  it('a row blocked by Repel is neither drawn nor in the summary row', () => {
    const location = { rows: [at('#19', ['Morning', 'Day', 'Night'], 0), at('#16', ['Morning', 'Day'], 1)], hiddenUntil: {} }

    const html = card([pokemon('Rattata', 19), pokemon('Pidgey', 16)], location, true, 'Day')

    expect([html.includes('Rattata'), html.includes('Pidgey'), html.includes('hidden-summary')]).toEqual([false, true, false])
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

    const result = phaseCardRows(pokemon, phaseData, scopedData, null)

    expect(result).toEqual([
      { key: '#167', name: 'Spinarak', sprite: 'spinarak', genderSymbol: '', amount: 2, caught: 1, encountersHere: 4, encountersOnFile: 10, missed: 2, share: null, times: [] },
      { key: 'bidoof;f', name: 'Bidoof', sprite: 'bidoof;f', genderSymbol: '♀', amount: 1, caught: 1, encountersHere: 2, encountersOnFile: 6, missed: 0, share: null, times: [] },
      { key: 'bidoof', name: 'Bidoof', sprite: 'bidoof', genderSymbol: '♂', amount: 0, caught: 0, encountersHere: 0, encountersOnFile: 7, missed: 0, share: null, times: [] }
    ])
  })

  it("takes each row's share and times from the location by position", () => {
    const location = { rows: [at('#16', ['Morning', 'Day'], 0.4), at('#19', [], null)], hiddenUntil: {} }

    const result = phaseCardRows([pokemon('Pidgey', 16), pokemon('Rattata', 19)], undefined, undefined, location)

    expect(result.map((r) => [r.key, r.share, r.times])).toEqual([['#16', 0.4, ['Morning', 'Day']], ['#19', null, []]])
  })

  it('drops a row hidden now and a row at a zero share', () => {
    const location = {
      rows: [at('#16', ['Morning', 'Day'], 0.4), at('#163', ['Night'], null, true), at('#19', ['Morning', 'Day', 'Night'], 0), at('#21', ['Morning', 'Day'], 0.6)],
      hiddenUntil: { Night: ['Hoothoot'] }
    }

    const result = phaseCardRows([pokemon('Pidgey', 16), pokemon('Hoothoot', 163), pokemon('Rattata', 19), pokemon('Spearow', 21)], undefined, undefined, location)

    expect(result.map((r) => r.key)).toEqual(['#16', '#21'])
  })

  it('keeps every row without a location', () => {
    const result = phaseCardRows([pokemon('Pidgey', 16), pokemon('Hoothoot', 163), pokemon('Rattata', 19)], undefined, undefined, null)

    expect(result.map((r) => r.key)).toEqual(['#16', '#163', '#19'])
  })
})
