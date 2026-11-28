import { describe, expect, it } from 'vitest'
import { phaseCardRows } from '../components/PhaseCard'
import type { GameEncounterResponse, PhaseInfoResponse, PhasePokemonInfo, SubscriptionState } from '../types/api'
import { applyEncounters, applyReread, milestonesToReread, syncSubscriptions } from './AppContext'

function sub(phase: string, gameName: string | null, withData = false): SubscriptionState {
  return {
    game: 'HGSS',
    phase,
    gameName,
    encounterData: withData ? ({ gameSpecificResponse: {} } as unknown as GameEncounterResponse) : null,
    phaseInfo: null,
    rowNames: [],
    location: null,
    live: false,
    timeOfDay: null,
    notFound: false
  }
}

function located(phase: string, gameName: string): SubscriptionState {
  return { ...sub(phase, gameName), location: { rows: [], hiddenUntil: {} } }
}

describe('syncSubscriptions', () => {
  it('gives two files on one milestone a subscription each', () => {
    const result = syncSubscriptions([], [
      { game: 'HGSS', phase: 'Route 30 Night', gameName: 'A' },
      { game: 'HGSS', phase: 'Route 30 Night', gameName: 'B' }
    ])

    expect(result.subscriptions.map((s) => s.gameName)).toEqual(['A', 'B'])
    expect(result.added).toEqual(result.subscriptions)
  })

  it("adds the moved follows-the-hunt card's own subscription", () => {
    const m1B = sub('M1', 'B', true)
    const m2A = sub('M2', 'A', true)

    const result = syncSubscriptions([m1B, m2A], [
      { game: 'HGSS', phase: 'M2', gameName: 'A' },
      { game: 'HGSS', phase: 'M2', gameName: 'B', followsHunt: true }
    ])

    expect(result.subscriptions).toHaveLength(2)
    expect(result.subscriptions[0]).toBe(m2A)
    expect(result.added).toEqual([sub('M2', 'B')])
    expect(result.subscriptions[1]).toBe(result.added[0])
  })

  it('keeps a card with no file as its own subscription', () => {
    const gamePhases = [
      { game: 'HGSS', phase: 'P' },
      { game: 'HGSS', phase: 'P', gameName: 'A' }
    ]

    const first = syncSubscriptions([], gamePhases)
    const second = syncSubscriptions(first.subscriptions, gamePhases)

    expect(first.subscriptions.map((s) => s.gameName)).toEqual([null, 'A'])
    expect(second.added).toEqual([])
    expect(second.subscriptions).toEqual(first.subscriptions)
  })

  it('shares one subscription between a fixed and a follows-the-hunt card on one file and milestone', () => {
    const result = syncSubscriptions([], [
      { game: 'HGSS', phase: 'P', gameName: 'A' },
      { game: 'HGSS', phase: 'P', gameName: 'A', followsHunt: true }
    ])

    expect(result.subscriptions).toEqual([sub('P', 'A')])
  })
})

describe('applyEncounters', () => {
  it('updates only the file that encountered', () => {
    const pA = sub('P', 'A')
    const pB = sub('P', 'B')
    const pNone = sub('P', null)
    const qA = sub('Q', 'A')
    const response = { gameSpecificResponse: { A: {} } } as unknown as GameEncounterResponse

    const result = applyEncounters([pA, pB, pNone, qA], response, 'HGSS', 'P')

    expect(result[0].encounterData).toBe(response)
    expect(result[1]).toBe(pB)
    expect(result[2].encounterData).toBe(response)
    expect(result[3]).toBe(qA)
  })
})

describe('milestonesToReread', () => {
  it('re-reads the location of a milestone on a push to its file', () => {
    const mA = located('M', 'A')
    const response = { gameSpecificResponse: { A: {} } } as unknown as GameEncounterResponse

    const result = milestonesToReread([mA, sub('P', 'A'), located('M', 'B')], response, 'HGSS', 'M')

    expect(result).toEqual([mA])
  })

  it('does not re-read a milestone with no location', () => {
    const response = { gameSpecificResponse: { A: {} } } as unknown as GameEncounterResponse

    const result = milestonesToReread([sub('P', 'A'), sub('P', null), located('M', 'A')], response, 'HGSS', 'P')

    expect(result).toEqual([])
  })
})

describe('applyReread', () => {
  function pokemon(name: string, speciesId: number): PhasePokemonInfo {
    return { name, speciesId, amount: 1, formCode: name.toLowerCase(), requireSpecificForm: false, hasFemaleForm: false }
  }

  it('leaves every share on its own species after a row is removed and the rest reordered', () => {
    const counts = { gameSpecificResponse: { A: {} } } as unknown as GameEncounterResponse
    const before: SubscriptionState = {
      ...sub('M', 'A'),
      encounterData: counts,
      phaseInfo: { gameName: 'HGSS', name: 'M', pokemon: [pokemon('Pidgey', 16), pokemon('Rattata', 19), pokemon('Sentret', 161)], isStatic: false, location: null, gameFiles: [], archived: false },
      rowNames: ['Pidgey', 'Rattata', 'Sentret'],
      location: {
        rows: [
          { key: '#16', version: null, times: ['Morning', 'Day'], share: 0.5, hiddenNow: false },
          { key: '#19', version: null, times: ['Morning', 'Day', 'Night'], share: 0.3, hiddenNow: false },
          { key: '#161', version: null, times: ['Morning', 'Day'], share: 0.2, hiddenNow: false }
        ],
        hiddenUntil: {}
      }
    }
    const response = {
      phaseInfo: { gameName: 'HGSS', name: 'M', pokemon: [pokemon('Sentret', 161), pokemon('Pidgey', 16)], isStatic: false },
      rowNames: ['Sentret', 'Pidgey'],
      data: { gameSpecificResponse: {} },
      live: true,
      timeOfDay: 'Day',
      location: {
        rows: [
          { key: '#161', times: ['Morning', 'Day'], share: 0.25, hiddenNow: false },
          { key: '#16', times: ['Morning', 'Day'], share: 0.75, hiddenNow: false }
        ],
        hiddenUntil: {}
      }
    } as unknown as PhaseInfoResponse

    const [after] = applyReread([before], sub('M', 'A'), response)
    const rows = phaseCardRows(after.phaseInfo?.pokemon ?? [], after.rowNames, undefined, undefined, after.location)

    expect(rows.map((r) => [r.name, r.share])).toEqual([['Sentret', 0.25], ['Pidgey', 0.75]])
    expect(after.encounterData).toBe(counts)
  })

  it('names the rows from the same response after a letter row is removed', () => {
    const unown = (letter: string): PhasePokemonInfo => ({ ...pokemon('Unown', 201), formCode: `unown-${letter}`, requireSpecificForm: true })
    const before: SubscriptionState = {
      ...sub('M', 'A'),
      phaseInfo: { gameName: 'HGSS', name: 'M', pokemon: [unown('a'), unown('b')], isStatic: false, location: null, gameFiles: [], archived: false },
      rowNames: ['Unown (A)', 'Unown (B)']
    }
    const response = {
      phaseInfo: { gameName: 'HGSS', name: 'M', pokemon: [unown('b')], isStatic: false },
      rowNames: ['Unown (B)'],
      data: { gameSpecificResponse: {} },
      live: true,
      timeOfDay: 'Day',
      location: null
    } as unknown as PhaseInfoResponse

    const [after] = applyReread([before], sub('M', 'A'), response)
    const rows = phaseCardRows(after.phaseInfo?.pokemon ?? [], after.rowNames, undefined, undefined, after.location)

    expect(rows.map((r) => [r.key, r.name])).toEqual([['unown-b', 'Unown (B)']])
  })
})
