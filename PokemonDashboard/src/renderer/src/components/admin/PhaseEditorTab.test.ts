import { describe, expect, it } from 'vitest'
import type { PhaseInfo, RomVersion, SeedRow } from '../../types/api'
import type { Draft } from './PhaseEditorTab'
import { byModality, draftRows, markerLabels, nameTaken, presetsFor, sameMilestone, savePayload, toggleFile, versionsOf } from './PhaseEditorTab'

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

describe('versionsOf', () => {
  it('HGSS is HeartGold and SoulSilver; RS is Ruby and Sapphire', () => {
    expect(versionsOf('HGSS')).toEqual(['HeartGold', 'SoulSilver'])
    expect(versionsOf('RS')).toEqual(['Ruby', 'Sapphire'])
  })
})

function seed(name: string, speciesId: number, amount: number, version: RomVersion | null = null, formCode?: string): SeedRow {
  return {
    name,
    speciesId,
    amount,
    formCode: formCode ?? name.toLowerCase(),
    requireSpecificForm: formCode !== undefined,
    hasFemaleForm: false,
    version,
    times: ['Morning', 'Day']
  }
}

function draft(rows: SeedRow[], patch: Partial<Draft> = {}): Draft {
  return { location: '34-land', version: null, preset: 'LivingDex', seed: rows, edited: {}, removed: [], added: [], ...patch }
}

const amounts = (d: Draft): [string, number][] => draftRows(d).map((r) => [r.formCode, r.amount])

describe('draftRows', () => {
  it('takes the seed\'s amounts', () => {
    expect(amounts(draft([seed('Pidgey', 16, 3), seed('Rattata', 19, 2)]))).toEqual([['pidgey', 3], ['rattata', 2]])
  })

  it('a change of preset replaces only amounts not typed by hand', () => {
    const living = draft([seed('Pidgey', 16, 3), seed('Rattata', 19, 2)], { edited: { '#19': 5 } })
    const none = { ...living, preset: 'None' as const, seed: [seed('Pidgey', 16, 0), seed('Rattata', 19, 0)] }
    expect(amounts(none)).toEqual([['pidgey', 0], ['rattata', 5]])
  })

  it('a typed amount and a removal survive a change of version', () => {
    const both = [seed('Pidgey', 16, 3), seed('Weedle', 13, 3, 'SoulSilver'), seed('Rattata', 19, 2)]
    const typed = draft(both, { edited: { '#13': 2 }, removed: ['#19'] })
    const heartGold = { ...typed, version: 'HeartGold' as const, seed: [seed('Pidgey', 16, 3), seed('Rattata', 19, 2)] }
    expect(amounts(heartGold)).toEqual([['pidgey', 3]])
    expect(amounts({ ...heartGold, version: null, seed: both })).toEqual([['pidgey', 3], ['weedle', 2]])
  })

  it('a form row and the any-form row are edited apart', () => {
    const female = draft([seed('Rattata', 19, 1), seed('Rattata', 19, 1, null, 'rattata;f')], { edited: { 'rattata;f': 4 } })
    expect(amounts(female)).toEqual([['rattata', 1], ['rattata;f', 4]])
  })

  it('rows added by hand come after the seed rows and survive a reseed', () => {
    const added = { name: 'Sentret', speciesId: 161, amount: 1, formCode: 'sentret', requireSpecificForm: false, hasFemaleForm: false }
    const d = draft([seed('Pidgey', 16, 3)], { added: [added] })
    expect(amounts(d)).toEqual([['pidgey', 3], ['sentret', 1]])
    expect(amounts({ ...d, seed: [seed('Pidgey', 16, 0)] })).toEqual([['pidgey', 0], ['sentret', 1]])
  })

  it('drops version and times from each row', () => {
    const [row] = draftRows(draft([seed('Weedle', 13, 3, 'SoulSilver')]))
    expect('version' in row).toBe(false)
    expect('times' in row).toBe(false)
  })
})

describe('savePayload', () => {
  it('a milestone from game data saves the draft\'s rows and its location', () => {
    const payload = savePayload(phase(''), draft([seed('Pidgey', 16, 3)], { location: '35-land' }), 'HGSS', 'Route 31 · grass')
    expect(payload.name).toBe('Route 31 · grass')
    expect(payload.location).toBe('35-land')
    expect(payload.pokemon.map((p) => p.formCode)).toEqual(['pidgey'])
  })

  it('a saved milestone saves its form rows, location, game files and archived', () => {
    const pidgey = { name: 'Pidgey', speciesId: 16, amount: 2, formCode: 'pidgey', requireSpecificForm: false, hasFemaleForm: false }
    const form = { ...phase('1-4 Route 30'), pokemon: [pidgey], location: '34-land', gameFiles: ['HG-1'], archived: true }
    expect(savePayload(form, null, 'HGSS', '1-4 Route 30')).toEqual(form)
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

describe('sameMilestone', () => {
  it('drops a reread for a milestone no longer open', () => {
    expect(sameMilestone(phase('1-4 Route 30'), phase('1-4 Route 30'))).toBe(true)
    expect(sameMilestone(phase('1-5 Route 31'), phase('1-4 Route 30'))).toBe(false)
  })
})

describe('toggleFile', () => {
  it('ticking adds the file, unticking removes it, others are kept', () => {
    expect(toggleFile(['HG-1'], 'HG-2', true)).toEqual(['HG-1', 'HG-2'])
    expect(toggleFile(['HG-1', 'HG-2'], 'HG-1', false)).toEqual(['HG-2'])
  })
})
