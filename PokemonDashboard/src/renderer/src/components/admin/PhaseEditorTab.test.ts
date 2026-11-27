import { describe, expect, it } from 'vitest'
import type { PhaseInfo } from '../../types/api'
import { nameTaken, presetsFor, toggleFile } from './PhaseEditorTab'

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
