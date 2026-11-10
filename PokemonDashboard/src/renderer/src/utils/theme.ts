import {
  ADMIN_WINDOW,
  RECENT_WINDOW,
  phaseWindowName,
  pickupWindowName,
  type Appearance
} from '../../../shared/windowContracts'

// The ids are the data-theme values in assets/themes.css.
export const THEMES = [
  { id: 'midnight', label: 'Midnight' },
  { id: 'hgss-drift', label: 'HGSS Gold and Silver' },
  { id: 'hgss-dusk', label: 'HGSS Dusk' },
  { id: 'rs-drift', label: 'Ruby and Sapphire' },
  { id: 'ruby', label: 'Ruby' },
  { id: 'sapphire', label: 'Sapphire' },
  { id: 'christmas', label: 'Christmas Pine' },
  { id: 'christmas-night', label: 'Christmas Night' }
] as const

// The Night selector lists the night themes first, then the rest (FR-18).
const NIGHT_FIRST = ['hgss-dusk', 'christmas-night']
export const NIGHT_THEME_ORDER = [
  ...NIGHT_FIRST,
  ...THEMES.map((t) => t.id).filter((id) => !NIGHT_FIRST.includes(id))
]

// Rebuilds the window's name from the query windowManager.openCardWindow gives it.
export function windowNameFromSearch(search: string): string {
  const params = new URLSearchParams(search)
  switch (params.get('card')) {
    case 'recent':
      return RECENT_WINDOW
    case 'phase':
      return phaseWindowName({
        game: params.get('game') ?? '',
        phase: params.get('phase') ?? '',
        gameName: params.get('gameName')
      })
    case 'pickup':
      return pickupWindowName(params.get('game') ?? '')
    default:
      return ADMIN_WINDOW
  }
}

// The parts of a name are encoded, so a colon only ever separates them.
export function windowLabel(name: string): string {
  const [kind, ...parts] = name.split(':').map(decodeURIComponent)
  if (kind === 'phase') {
    const [game, phase, gameFile] = parts
    return `${game} / ${phase}${gameFile ? ` (${gameFile})` : ''}`
  }
  if (kind === 'pickup') return `Pickup Items (${parts[0]})`
  return kind === RECENT_WINDOW ? 'Recent Encounters' : 'Admin'
}

function minutes(hhmm: string): number {
  const [h, m] = hhmm.split(':').map(Number)
  return h * 60 + m
}

// Only the time of day counts. A night that starts before the day runs across midnight;
// equal times are night all day.
export function isDay(now: Date, dayStart: string, nightStart: string): boolean {
  const t = now.getHours() * 60 + now.getMinutes()
  const day = minutes(dayStart)
  const night = minutes(nightStart)
  return night < day ? t >= day || t < night : day <= t && t < night
}

export function resolveTheme(appearance: Appearance, windowName: string, now: Date): string {
  const choice = appearance.windows[windowName] ?? appearance.default
  return choice.night && !isDay(now, appearance.dayStart, appearance.nightStart) ? choice.night : choice.theme
}

// Milliseconds to the next day or night boundary strictly after now, or null when the choice never switches.
export function nextSwitchDelay(appearance: Appearance, windowName: string, now: Date): number | null {
  const choice = appearance.windows[windowName] ?? appearance.default
  if (!choice.night) return null
  const delays = [0, 1].flatMap((addDays) =>
    [appearance.dayStart, appearance.nightStart].map((hhmm) => {
      const [h, m] = hhmm.split(':').map(Number)
      const at = new Date(now.getFullYear(), now.getMonth(), now.getDate() + addDays, h, m)
      return at.getTime() - now.getTime()
    })
  )
  return Math.min(...delays.filter((ms) => ms > 0))
}

function storageKey(windowName: string): string {
  return `dashboard-theme:${windowName}`
}

// One key for every window: the switch is the same for all of them.
const ANIMATIONS_KEY = 'dashboard-animations'

// Before first paint: the last theme this window applied. With none saved it stays Midnight.
// With no animations value saved the attribute stays absent, which the CSS reads as on.
export function initTheme(windowName: string): void {
  const saved = localStorage.getItem(storageKey(windowName))
  if (saved !== null) document.documentElement.dataset.theme = saved
  const animations = localStorage.getItem(ANIMATIONS_KEY)
  if (animations !== null) document.documentElement.dataset.animations = animations
}

// Touched only on the renderer's single thread (the AppContext effect and this timer's callback).
let timer: ReturnType<typeof setTimeout> | null = null

export function applyTheme(appearance: Appearance, windowName: string): void {
  if (timer !== null) clearTimeout(timer)
  timer = null
  const now = new Date()
  const theme = resolveTheme(appearance, windowName, now)
  document.documentElement.dataset.theme = theme
  localStorage.setItem(storageKey(windowName), theme)
  const animations = appearance.animations ? 'on' : 'off'
  document.documentElement.dataset.animations = animations
  localStorage.setItem(ANIMATIONS_KEY, animations)
  const delay = nextSwitchDelay(appearance, windowName, now)
  if (delay !== null) timer = setTimeout(() => applyTheme(appearance, windowName), delay)
}
