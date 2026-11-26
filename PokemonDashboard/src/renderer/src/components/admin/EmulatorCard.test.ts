import { describe, expect, it } from 'vitest'
import type { BotMilestones, PickerMilestone } from '../../types/api'
import { cardHeading } from './EmulatorCard'

function milestone(id: string, name: string): PickerMilestone {
  return { id, name, modality: id === name ? null : 'Land', huntable: true, complete: false, archived: false, order: null, anchor: null, anchorSet: false }
}

const picker: BotMilestones = { gameFileName: 'HG-1', milestones: [milestone('34-land', 'Route 30 · grass'), milestone('Route 33', 'Route 33')] }

describe('cardHeading', () => {
  it('reads a generated milestone\'s id as its name', () => {
    expect(cardHeading('34-land', picker)).toBe('Route 30 · grass')
  })

  it('keeps a plan hunt\'s and a custom phase\'s name', () => {
    expect(cardHeading('Plan Johto', picker)).toBe('Plan Johto')
    expect(cardHeading('Route 33', picker)).toBe('Route 33')
  })

  it('keeps the id while the picker is not loaded', () => {
    expect(cardHeading('34-land', undefined)).toBe('34-land')
  })
})
