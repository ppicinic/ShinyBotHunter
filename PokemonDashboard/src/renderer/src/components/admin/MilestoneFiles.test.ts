import { describe, expect, it } from 'vitest'
import type { CopyOffer, GameFile } from '../../types/api'
import { assignmentNote, chosenSpecies, linkOf } from './MilestoneFiles'

function file(id: string, assigned: string[] = [], links: GameFile['links'] = []): GameFile {
  return { id, entries: [], hidden: [], assigned, links }
}

function offer(species: number): CopyOffer {
  return { species, phase: [{ species, formCode: null, amount: 1, key: `#${species}` }], goal: [] }
}

describe('linkOf', () => {
  it('returns the link of this milestone on the file', () => {
    const f = file('HG-1', [], [{ phase: '1-4 Route 30', milestoneId: '34-land' }, { phase: '1-2 Route 29', milestoneId: '33-land' }])
    expect(linkOf(f, '1-4 Route 30')).toEqual({ phase: '1-4 Route 30', milestoneId: '34-land' })
  })

  it('is null when only another milestone is linked', () => {
    expect(linkOf(file('HG-1', [], [{ phase: '1-2 Route 29', milestoneId: '33-land' }]), '1-4 Route 30')).toBeNull()
  })
})

describe('assignmentNote', () => {
  it('reads listed on every game file when none is assigned', () => {
    expect(assignmentNote([file('HG-1'), file('HG-2', ['Other'])], 'Training')).toBe('Assigned to no game file: listed on every game file.')
  })

  it('reads listed only on those when some are assigned', () => {
    expect(assignmentNote([file('HG-1', ['Training']), file('HG-2', ['Training']), file('HG-3')], 'Training'))
      .toBe('Assigned to 2 game files: listed only on those.')
  })
})

describe('chosenSpecies', () => {
  it('returns the ticked species in the order of the offer', () => {
    expect(chosenSpecies([offer(19), offer(16), offer(161)], new Set([161, 19]))).toEqual([19, 161])
  })

  it('is empty with nothing ticked', () => {
    expect(chosenSpecies([offer(19)], new Set())).toEqual([])
  })
})
