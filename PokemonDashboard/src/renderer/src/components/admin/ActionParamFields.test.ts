import { describe, expect, it } from 'vitest'
import type { PickerMilestone } from '../../types/api'
import { launchMilestones } from './ActionParamFields'

function milestone(name: string, complete = false): PickerMilestone {
  return { name, modality: null, huntable: true, complete, anchor: null, anchorSet: false }
}

const milestones = [milestone('1-4 Route 30', true), milestone('1-5 Route 31'), milestone('2-1 Route 32', true)]

describe('launchMilestones', () => {
  it('lists every milestone in the server\'s order', () => {
    expect(launchMilestones(milestones, '', false).map((m) => m.name)).toEqual(['1-4 Route 30', '1-5 Route 31', '2-1 Route 32'])
  })

  it('incomplete only leaves out the complete ones', () => {
    expect(launchMilestones(milestones, '', true).map((m) => m.name)).toEqual(['1-5 Route 31'])
  })

  it('keeps the selected milestone listed when it is complete', () => {
    expect(launchMilestones(milestones, '2-1 Route 32', true).map((m) => m.name)).toEqual(['1-5 Route 31', '2-1 Route 32'])
  })
})
