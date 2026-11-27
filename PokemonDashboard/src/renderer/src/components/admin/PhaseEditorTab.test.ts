import { describe, expect, it } from 'vitest'
import { presetsFor } from './PhaseEditorTab'

describe('presetsFor', () => {
  it('gives HeartGold and SoulSilver none and the three presets', () => {
    expect(presetsFor('HGSS')).toEqual(['None', 'LivingDex', 'FemaleForms', 'VersionExclusives'])
  })

  it('gives Ruby and Sapphire no female forms preset', () => {
    expect(presetsFor('RS')).toEqual(['None', 'LivingDex', 'VersionExclusives'])
  })
})
