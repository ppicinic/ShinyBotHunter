import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { GameEncounterResponseBase, MilestoneCardRow, MilestoneRowsResponse, PhaseEncounterResponse, PhasePokemonInfo } from '../types/api'
import { milestoneCardRows, PhaseCard, phaseCardRows } from './PhaseCard'

function row(key: string, formCode: string | null, changes: Partial<MilestoneCardRow> = {}): MilestoneCardRow {
  return {
    species: 167,
    key,
    formCode,
    name: 'Spinarak',
    sprite: formCode ?? 'spinarak',
    hasFemaleForm: false,
    wanted: 1,
    caught: 0,
    complete: false,
    hidden: false,
    isTarget: true,
    encountersHere: 0,
    encountersOnFile: 0,
    shinyEncountersOnFile: 0,
    share: null,
    ...changes
  }
}

function counts(encounters: number, shinyEncounters: number, shiniesCaught: number): { encounters: number; shinyEncounters: number; shiniesCaught: number } {
  return { encounters, shinyEncounters, shiniesCaught }
}

describe('milestoneCardRows', () => {
  it('drops a hidden row', () => {
    const result = milestoneCardRows([row('#1', null), row('#2', null, { hidden: true }), row('#3', null)])

    expect(result.map((r) => r.key)).toEqual(['#1', '#3'])
  })

  it('an amount 0 row is shown', () => {
    const result = milestoneCardRows([row('#167', null, { wanted: 0, caught: 2 })])

    expect(result).toHaveLength(1)
    expect(result[0].amount).toBe(0)
  })

  it("Unown's 28 entries are 28 rows", () => {
    const letters = [...'abcdefghijklmnopqrstuvwxyz'].concat(['exclamation', 'question'])
    const rows = letters.map((l) => row(`unown-${l}`, `unown-${l}`, { species: 201, name: 'Unown' }))

    const result = milestoneCardRows(rows)

    expect(new Set(result.map((r) => r.key)).size).toBe(28)
    expect(new Set(result.map((r) => r.sprite)).size).toBe(28)
    expect([result[0].sprite, result[27].sprite]).toEqual(['unown-a', 'unown-question'])
    expect(result.every((r) => r.genderSymbol === '')).toBe(true)
  })

  it('gender follows the form', () => {
    const result = milestoneCardRows([
      row('bidoof;f', 'bidoof;f', { hasFemaleForm: true }),
      row('bidoof', 'bidoof', { hasFemaleForm: true }),
      row('#399', null, { hasFemaleForm: true, sprite: 'bidoof' })
    ])

    expect(result.map((r) => [r.genderSymbol, r.sprite])).toEqual([['♀', 'bidoof;f'], ['♂', 'bidoof'], ['', 'bidoof']])
  })

  it('counts come from the row', () => {
    const result = milestoneCardRows([
      row('#1', null, { encountersHere: 5, encountersOnFile: 9, caught: 1, shinyEncountersOnFile: 3 }),
      row('#2', null, { caught: 2, shinyEncountersOnFile: 1 })
    ])

    expect(result[0]).toMatchObject({ encountersHere: 5, encountersOnFile: 9, caught: 1, missed: 2 })
    expect(result[1].missed).toBe(0)
  })

  it("carries each row's share", () => {
    const result = milestoneCardRows([row('#1', null, { share: 0.25 }), row('#2', null, { share: 0.75 })])

    expect(result.map((r) => r.share)).toEqual([0.25, 0.75])
  })

  it('drops a row at a zero share', () => {
    const result = milestoneCardRows([row('#69', null, { share: 0.308 }), row('#19', null, { share: 0 }), row('#194', null, { share: 0.538 })])

    expect(result.map((r) => r.key)).toEqual(['#69', '#194'])
  })

  it('keeps every row without shares', () => {
    const result = milestoneCardRows([row('#1', null), row('#2', null), row('#3', null)])

    expect(result.map((r) => r.key)).toEqual(['#1', '#2', '#3'])
  })
})

describe('PhaseCard', () => {
  function card(rows: MilestoneCardRow[], survive: number | null): string {
    const milestone = { id: '36-land', name: 'Route 32 · grass', modality: 'Land', rows, survive, live: true, timeOfDay: 'Night' } as unknown as MilestoneRowsResponse
    const subscription = { game: 'HGSS', phase: '36-land', gameName: 'HG-QA', encounterData: null, phaseInfo: null, milestone, notFound: false }
    return renderToStaticMarkup(createElement(PhaseCard, { subscription }))
  }

  it('draws the share under the name, with no Share column', () => {
    const html = card([row('#69', null, { name: 'Bellsprout', share: 0.308 }), row('#41', null, { name: 'Zubat', share: 0 })], 0.65)

    expect(html).toMatch(/Bellsprout<\/span><div data-testid="row-share"[^>]*>30.8 %<\/div><\/td>/)
    expect(html).not.toContain('Zubat')
    expect(html).not.toContain('>Share<')
    expect(html).not.toContain('encounters survive')
  })

  it('draws every row and no share without shares', () => {
    const html = card([row('#69', null, { name: 'Bellsprout' }), row('#41', null, { name: 'Zubat' })], null)

    expect([html.includes('Bellsprout'), html.includes('Zubat'), html.includes('row-share')]).toEqual([true, true, false])
  })

  it('keeps the table when a Repel blocks every species', () => {
    const html = card([row('#19', null, { name: 'Rattata', share: 0 })], 0)

    expect([html.includes('<table'), html.includes('Rattata'), html.includes('No Pokémon')]).toEqual([true, false, false])
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

  it("a stored phase's rows have no share", () => {
    const pokemon: PhasePokemonInfo[] = [
      { name: 'Spinarak', speciesId: 167, amount: 1, formCode: 'spinarak', requireSpecificForm: false, hasFemaleForm: false }
    ]

    const result = phaseCardRows(pokemon, undefined, undefined)

    expect(result[0].share).toBeUndefined()
  })
})
