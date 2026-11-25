import { describe, expect, it } from 'vitest'
import type { GoalEntry } from '../../types/api'
import { goalLines, presetsFor } from './GoalEditorTab'

function entry(species: number, formCode: string | null, amount = 1): GoalEntry {
  return { species, formCode, amount, key: formCode ?? `#${species}` }
}

const names = new Map([[190, 'Aipom'], [201, 'Unown'], [16, 'Pidgey']])

describe('presetsFor', () => {
  it('gives HeartGold and SoulSilver the three presets', () => {
    expect(presetsFor('HGSS')).toEqual(['LivingDex', 'FemaleForms', 'VersionExclusives'])
  })

  it('gives Ruby and Sapphire no female forms preset', () => {
    expect(presetsFor('RS')).toEqual(['LivingDex', 'VersionExclusives'])
  })
})

describe('goalLines', () => {
  it('sorts by species, then form with any form first', () => {
    const lines = goalLines([entry(201, 'unown-b'), entry(190, null), entry(201, 'unown-a'), entry(16, null), entry(16, 'pidgey;f')], names, '')
    expect(lines.map((l) => l.entry.key)).toEqual(['#16', 'pidgey;f', '#190', 'unown-a', 'unown-b'])
  })

  it('keeps each line at its index in the goal', () => {
    const lines = goalLines([entry(190, null), entry(16, null)], names, '')
    expect(lines.map((l) => l.index)).toEqual([1, 0])
  })

  it('names a line from the names list, else by number', () => {
    const lines = goalLines([entry(190, null), entry(493, null)], names, '')
    expect(lines.map((l) => l.name)).toEqual(['Aipom', '#493'])
  })

  it('filters by name, number and form code, ignoring case', () => {
    const goal = [entry(190, null), entry(201, 'unown-a'), entry(16, null)]
    expect(goalLines(goal, names, 'aIp').map((l) => l.entry.species)).toEqual([190])
    expect(goalLines(goal, names, '16').map((l) => l.entry.species)).toEqual([16])
    expect(goalLines(goal, names, '#190').map((l) => l.entry.species)).toEqual([190])
    expect(goalLines(goal, names, 'UNOWN-A').map((l) => l.entry.key)).toEqual(['unown-a'])
  })

  it('marks a 0 amount line as tracked', () => {
    const lines = goalLines([entry(16, 'pidgey', 0), entry(16, 'pidgey;f', 2)], names, '')
    expect(lines.map((l) => l.tracked)).toEqual([true, false])
  })
})
