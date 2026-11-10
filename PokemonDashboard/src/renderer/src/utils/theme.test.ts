import { afterEach, describe, expect, it, vi } from 'vitest'
import { phaseWindowName, pickupWindowName, type Appearance } from '../../../shared/windowContracts'
import {
  NIGHT_THEME_ORDER,
  THEMES,
  applyTheme,
  initTheme,
  isDay,
  nextSwitchDelay,
  resolveTheme,
  windowLabel,
  windowNameFromSearch
} from './theme'

function at(hhmm: string, month = 10, date = 10): Date {
  const [h, m] = hhmm.split(':').map(Number)
  return new Date(2026, month, date, h, m)
}

function appearance(partial: Partial<Appearance>): Appearance {
  return { dayStart: '07:00', nightStart: '19:00', default: { theme: 'midnight' }, windows: {}, ...partial }
}

describe('windowNameFromSearch', () => {
  // Built the way windowManager.openCardWindow builds the query.
  const search = (query: Record<string, string>): string => `?${new URLSearchParams(query).toString()}`

  it('gives admin for the admin card and for no card', () => {
    expect(windowNameFromSearch(search({ card: 'admin' }))).toBe('admin')
    expect(windowNameFromSearch('')).toBe('admin')
  })

  it('gives recent for the recent card', () => {
    expect(windowNameFromSearch(search({ card: 'recent' }))).toBe('recent')
  })

  it('gives the phase window name main uses', () => {
    const withFile = { game: 'RS', phase: 'Hoenn Starters', gameName: 'Ruby' }
    expect(windowNameFromSearch(search({ card: 'phase', ...withFile }))).toBe('phase:RS:Hoenn%20Starters:Ruby')
    expect(windowNameFromSearch(search({ card: 'phase', ...withFile }))).toBe(phaseWindowName(withFile))

    const noFile = { game: 'RS', phase: 'Hoenn Starters' }
    expect(windowNameFromSearch(search({ card: 'phase', ...noFile }))).toBe('phase:RS:Hoenn%20Starters:')
    expect(windowNameFromSearch(search({ card: 'phase', ...noFile }))).toBe(phaseWindowName(noFile))
  })

  it('gives the pickup window name main uses', () => {
    expect(windowNameFromSearch(search({ card: 'pickup', game: 'Ruby' }))).toBe('pickup:Ruby')
    expect(windowNameFromSearch(search({ card: 'pickup', game: 'Ruby' }))).toBe(pickupWindowName('Ruby'))
  })
})

describe('windowLabel', () => {
  it('labels every kind of window from its name', () => {
    expect(windowLabel('phase:RS:Hoenn%20Starters:Ruby')).toBe('RS / Hoenn Starters (Ruby)')
    expect(windowLabel('phase:RS:Hoenn%20Starters:')).toBe('RS / Hoenn Starters')
    expect(windowLabel('pickup:Ruby')).toBe('Pickup Items (Ruby)')
    expect(windowLabel('admin')).toBe('Admin')
    expect(windowLabel('recent')).toBe('Recent Encounters')
  })
})

describe('resolveTheme lookup', () => {
  const config = appearance({
    default: { theme: 'hgss-drift' },
    windows: { 'phase:RS:Hoenn%20Starters:Ruby': { theme: 'ruby' }, admin: { theme: 'sapphire' } }
  })

  it('uses the window entry when there is one, else the default', () => {
    expect(resolveTheme(config, 'phase:RS:Hoenn%20Starters:Ruby', at('12:00'))).toBe('ruby')
    expect(resolveTheme(config, 'phase:RS:Hoenn%20Starters:', at('12:00'))).toBe('hgss-drift')
  })

  it('overrides admin only', () => {
    expect(resolveTheme(config, 'admin', at('12:00'))).toBe('sapphire')
    expect(resolveTheme(config, 'recent', at('12:00'))).toBe('hgss-drift')
  })
})

describe('day and night', () => {
  it('switches at 07:00 and 19:00', () => {
    const config = appearance({ default: { theme: 'christmas', night: 'christmas-night' } })
    const expected: Array<[string, string]> = [
      ['06:59', 'christmas-night'],
      ['07:00', 'christmas'],
      ['18:59', 'christmas'],
      ['19:00', 'christmas-night'],
      ['23:59', 'christmas-night'],
      ['00:00', 'christmas-night']
    ]
    for (const [time, theme] of expected) {
      expect(resolveTheme(config, 'admin', at(time)), time).toBe(theme)
    }
  })

  it('keeps a choice without night at every time', () => {
    const config = appearance({ default: { theme: 'ruby' } })
    for (const time of ['06:59', '07:00', '18:59', '19:00', '23:59', '00:00']) {
      expect(resolveTheme(config, 'admin', at(time)), time).toBe('ruby')
    }
  })

  it('handles a night that starts before the day, across midnight', () => {
    const expected: Array<[string, boolean]> = [
      ['05:59', true],
      ['06:00', false],
      ['19:59', false],
      ['20:00', true],
      ['00:00', true]
    ]
    for (const [time, day] of expected) {
      expect(isDay(at(time), '20:00', '06:00'), time).toBe(day)
    }
  })

  it('is night all day when the times are equal', () => {
    for (const time of ['06:59', '07:00', '12:00', '00:00']) {
      expect(isDay(at(time), '07:00', '07:00'), time).toBe(false)
    }
  })
})

describe('nextSwitchDelay', () => {
  const config = appearance({ default: { theme: 'christmas', night: 'christmas-night' } })

  it('is null for a choice without night', () => {
    expect(nextSwitchDelay(appearance({}), 'admin', at('06:59'))).toBeNull()
  })

  it('runs to the next boundary strictly after now', () => {
    expect(nextSwitchDelay(config, 'admin', at('06:59'))).toBe(60_000)
    expect(nextSwitchDelay(config, 'admin', at('18:00'))).toBe(60 * 60_000)
    expect(nextSwitchDelay(config, 'admin', at('23:00'))).toBe(8 * 60 * 60_000)
    expect(nextSwitchDelay(config, 'admin', at('07:00'))).toBe(12 * 60 * 60_000)
  })
})

describe('theme lists', () => {
  it('lists the night themes first in the Night selector', () => {
    expect(NIGHT_THEME_ORDER).toEqual([
      'hgss-dusk',
      'christmas-night',
      'midnight',
      'hgss-drift',
      'rs-drift',
      'ruby',
      'sapphire',
      'christmas'
    ])
    expect([...NIGHT_THEME_ORDER].sort()).toEqual(THEMES.map((t) => t.id).sort())
  })

  it('never picks Christmas by the date', () => {
    expect(resolveTheme(appearance({}), 'admin', at('12:00', 11, 25))).toBe('midnight')
  })
})

describe('applyTheme and initTheme', () => {
  let dataset: Record<string, string>
  let store: Map<string, string>

  function stubDom(): void {
    dataset = {}
    store = new Map()
    vi.stubGlobal('document', { documentElement: { dataset } })
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => store.set(key, value)
    })
  }

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('arms no timer for a choice without night', () => {
    stubDom()
    vi.useFakeTimers()
    applyTheme(appearance({ default: { theme: 'ruby' } }), 'admin')
    expect(vi.getTimerCount()).toBe(0)
    expect(dataset.theme).toBe('ruby')
    expect(store.get('dashboard-theme:admin')).toBe('ruby')
  })

  it('switches at the boundary, re-arms, and clears on a config change', () => {
    stubDom()
    vi.useFakeTimers()
    vi.setSystemTime(at('18:59'))
    applyTheme(appearance({ default: { theme: 'christmas', night: 'christmas-night' } }), 'admin')
    expect(dataset.theme).toBe('christmas')
    expect(vi.getTimerCount()).toBe(1)

    vi.advanceTimersByTime(60_000)
    expect(dataset.theme).toBe('christmas-night')
    expect(store.get('dashboard-theme:admin')).toBe('christmas-night')
    expect(vi.getTimerCount()).toBe(1)

    applyTheme(appearance({ default: { theme: 'ruby' } }), 'admin')
    expect(vi.getTimerCount()).toBe(0)
  })

  it('applies the saved theme before first paint, and nothing when none is saved', () => {
    stubDom()
    initTheme('recent')
    expect(dataset.theme).toBeUndefined()

    store.set('dashboard-theme:recent', 'sapphire')
    initTheme('recent')
    expect(dataset.theme).toBe('sapphire')
  })
})
