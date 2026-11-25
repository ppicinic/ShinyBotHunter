import { describe, expect, it } from 'vitest'
import type { MilestoneCardRow } from '../../types/api'
import { hiddenCount, rowRequest } from './MilestoneRowsPanel'

function row(species: number, formCode: string | null, hidden: boolean): MilestoneCardRow {
  return {
    species,
    key: formCode ?? `#${species}`,
    formCode,
    name: `#${species}`,
    sprite: formCode ?? `${species}`,
    hasFemaleForm: false,
    wanted: 1,
    caught: 0,
    complete: false,
    hidden,
    isTarget: !hidden,
    encountersHere: 0,
    encountersOnFile: 0,
    shinyEncountersOnFile: 0
  }
}

describe('hiddenCount', () => {
  it('counts only the hidden rows', () => {
    expect(hiddenCount([row(16, null, true), row(19, null, false), row(201, 'unown-a', true)])).toBe(2)
  })

  it('is 0 with nothing hidden', () => {
    expect(hiddenCount([row(16, null, false)])).toBe(0)
  })
})

describe('rowRequest', () => {
  it('sends an any-form row by its species key', () => {
    expect(rowRequest('34-land', row(16, null, false))).toEqual({ milestoneId: '34-land', key: '#16' })
  })

  it('sends a form row by its form code', () => {
    expect(rowRequest('315-land', row(201, 'unown-a', false))).toEqual({ milestoneId: '315-land', key: 'unown-a' })
  })
})
