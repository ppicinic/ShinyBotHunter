import { describe, expect, it } from 'vitest'
import type { PhaseInfo } from '../../types/api'
import { byModality, markerLabels, nameTaken, presetsFor, toggleFile } from './PhaseEditorTab'

function phase(name: string): PhaseInfo {
  return { gameName: 'HGSS', name, pokemon: [], isStatic: false, location: null, gameFiles: [], archived: false }
}

describe('presetsFor', () => {
  it('gives HeartGold and SoulSilver none and the three presets', () => {
    expect(presetsFor('HGSS')).toEqual(['None', 'LivingDex', 'FemaleForms', 'VersionExclusives'])
  })

  it('gives Ruby and Sapphire no female forms preset', () => {
    expect(presetsFor('RS')).toEqual(['None', 'LivingDex', 'VersionExclusives'])
  })
})

describe('markerLabels', () => {
  it('lists one or two times in Morning, Day, Night order', () => {
    expect(markerLabels(['Morning', 'Day'], true)).toEqual(['morning', 'day'])
    expect(markerLabels(['Night'], true)).toEqual(['night'])
  })

  it('reads all day for all three when another row is timed', () => {
    expect(markerLabels(['Morning', 'Day', 'Night'], true)).toEqual(['all day'])
  })

  it('gives nothing when no row is timed', () => {
    expect(markerLabels(['Morning', 'Day', 'Night'], false)).toEqual([])
  })

  it('gives nothing for a row outside the slots', () => {
    expect(markerLabels([], true)).toEqual([])
  })
})

describe('byModality', () => {
  it('groups the locations by modality in the server\'s order', () => {
    const route29 = { id: '33-land', name: 'Route 29 · grass', modality: 'Land' as const }
    const route30 = { id: '34-land', name: 'Route 30 · grass', modality: 'Land' as const }
    const tree = { id: '34-headbutt', name: 'Route 30 · headbutt', modality: 'Headbutt' as const }
    expect(byModality([route29, route30, tree])).toEqual([['Land', [route29, route30]], ['Headbutt', [tree]]])
  })
})

describe('nameTaken', () => {
  it('is true for a saved milestone\'s name, trimmed', () => {
    expect(nameTaken([phase('1-4 Route 30')], '  1-4 Route 30 ')).toBe(true)
  })

  it('is false for a free name', () => {
    expect(nameTaken([phase('1-4 Route 30')], 'Route 30 · grass')).toBe(false)
  })
})

describe('toggleFile', () => {
  it('ticking adds the file, unticking removes it, others are kept', () => {
    expect(toggleFile(['HG-1'], 'HG-2', true)).toEqual(['HG-1', 'HG-2'])
    expect(toggleFile(['HG-1', 'HG-2'], 'HG-1', false)).toEqual(['HG-2'])
  })
})
