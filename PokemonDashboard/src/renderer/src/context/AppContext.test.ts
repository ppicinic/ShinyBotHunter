import { describe, expect, it } from 'vitest'
import type { GameEncounterResponse, SubscriptionState } from '../types/api'
import { applyEncounters, syncSubscriptions } from './AppContext'

function sub(phase: string, gameName: string | null, withData = false): SubscriptionState {
  return {
    game: 'HGSS',
    phase,
    gameName,
    encounterData: withData ? ({ gameSpecificResponse: {} } as unknown as GameEncounterResponse) : null,
    phaseInfo: null
  }
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
