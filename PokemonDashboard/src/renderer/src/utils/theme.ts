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

// Each kind of theme animation follows its own root attribute: off, low, medium or high.
const KINDS = ['gradients', 'snow', 'stars'] as const
type Kind = (typeof KINDS)[number]
const ATTRIBUTE = { gradients: 'animGradients', snow: 'animSnow', stars: 'animStars' } as const

// Until the saved setting holds a value per kind (C6), one saved value sets all three.
function setAnimations(value: string): void {
  for (const kind of KINDS) document.documentElement.dataset[ATTRIBUTE[kind]] = value
}

// Before first paint: the last theme this window applied. With none saved it stays Midnight.
// With no animations value saved the attributes stay absent: nothing moves until the pushed appearance sets them.
// From here on the motion timers follow the root's attributes and Windows' reduced motion.
export function initTheme(windowName: string): void {
  const saved = localStorage.getItem(storageKey(windowName))
  if (saved !== null) document.documentElement.dataset.theme = saved
  const animations = localStorage.getItem(ANIMATIONS_KEY)
  if (animations !== null) setAnimations(animations)
  new MutationObserver(syncMotion).observe(document.documentElement, {
    attributeFilter: ['data-theme', 'data-anim-gradients', 'data-anim-snow', 'data-anim-stars']
  })
  matchMedia(REDUCE_MOTION).addEventListener('change', syncMotion)
  syncMotion()
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
  // The saved switch is on or off for now; on is the timer at low for every kind.
  const animations = appearance.animations ? 'low' : 'off'
  setAnimations(animations)
  localStorage.setItem(ANIMATIONS_KEY, animations)
  const delay = nextSwitchDelay(appearance, windowName, now)
  if (delay !== null) timer = setTimeout(() => applyTheme(appearance, windowName), delay)
}

// In low and medium a kind of theme animation is moved by a timer instead of a CSS animation: a running CSS
// animation, even a stepped one, cost about half a core in bursts (docs/specs/dashboard-animation-cost.md). The
// timer writes custom properties on the root, which index.css, themes.css and sprites.css read. In high the CSS
// animation runs instead and the timer is stopped; in off, and under reduced motion, the snow and stars are not
// drawn and the gradients are still (the CSS does both).
const REDUCE_MOTION = '(prefers-reduced-motion: reduce)'

// Milliseconds between moves, per kind and mode. Off and high have none.
const RATES: Record<Kind, Record<string, number>> = {
  gradients: { low: 500, medium: 100 },
  snow: { low: 250, medium: 100 },
  stars: { low: 600, medium: 300 }
}

// Seconds per cycle of the gradients in each theme, and which themes snow. The colours and sizes are in assets/themes.css.
const MOTION: Record<string, { header?: number; card?: number; orbit?: number; glow?: number; lights?: number; snow?: boolean }> = {
  'hgss-drift': { header: 24, card: 36 },
  'hgss-dusk': { orbit: 30 },
  'rs-drift': { header: 24, card: 36 },
  ruby: { header: 20, card: 34 },
  sapphire: { header: 20, card: 34 },
  christmas: { card: 30, snow: true },
  'christmas-night': { glow: 8, lights: 2.4, snow: true }
}

// Each snow layer: its tile height in px (its inset and background-size in themes.css) and the seconds to fall one tile.
const SNOW = [
  [457, 14],
  [613, 20],
  [797, 27]
]

const PROPS: Record<Kind, string[]> = {
  gradients: ['--header-pos', '--card-fill-pos', '--card-glow-pos', '--card-glow-opacity', '--lights-opacity'],
  snow: ['--snow-1', '--snow-2', '--snow-3'],
  stars: ['--twinkle-1', '--twinkle-2']
}

// 0 to 1 and back over twice the given seconds, as an alternating CSS animation moves.
function backAndForth(t: number, seconds: number): number {
  const x = (t / seconds) % 2
  return x < 1 ? x : 2 - x
}

// One move of a kind at time t (seconds), for a timer every ms milliseconds.
function move(kind: Kind, t: number, ms: number): void {
  const style = document.documentElement.style
  const m = MOTION[motionTheme ?? ''] ?? {}
  if (kind === 'snow') {
    SNOW.forEach(([tile, seconds], i) => style.setProperty(`--snow-${i + 1}`, `${(((t / seconds) % 1) * tile).toFixed(1)}px`))
  } else if (kind === 'gradients') {
    if (m.header) style.setProperty('--header-pos', `${(backAndForth(t, m.header) * 100).toFixed(2)}% 50%`)
    if (m.card) style.setProperty('--card-fill-pos', `${(backAndForth(t, m.card) * 100).toFixed(2)}% 50%`)
    if (m.orbit) {
      const f = backAndForth(t, m.orbit / 2)
      style.setProperty('--card-glow-pos', `${(60 * f).toFixed(2)}% ${(40 * f).toFixed(2)}%, ${(100 - 60 * f).toFixed(2)}% ${(100 - 40 * f).toFixed(2)}%`)
    }
    if (m.glow) style.setProperty('--card-glow-opacity', (0.55 + 0.45 * backAndForth(t, m.glow / 2)).toFixed(3))
    if (m.lights) style.setProperty('--lights-opacity', (0.55 + 0.45 * backAndForth(t, m.lights / 2)).toFixed(3))
  } else if (document.querySelector('.sprite-sparkle')) {
    // A twinkle of at least four moves and at least the CSS's 1.8 s; the second star is half a twinkle behind.
    const step = ms / 1000
    const v = backAndForth(Math.floor(t / step) * step, Math.max(1.8, 4 * step) / 2)
    style.setProperty('--twinkle-1', v.toFixed(2))
    style.setProperty('--twinkle-2', (1 - v).toFixed(2))
  }
}

// Touched only on the renderer's single thread (initTheme, the observer, the media query listener and the timers' callbacks).
const running: Partial<Record<Kind, { id: ReturnType<typeof setInterval>; ms: number }>> = {}
let motionTheme: string | undefined

// Starts, changes or stops each kind's timer to match the root's attributes, the theme and reduced motion.
// A kind that stops or changes theme loses its properties, so it starts again from the still look.
function syncMotion(): void {
  const root = document.documentElement
  const themeChanged = root.dataset.theme !== motionTheme
  motionTheme = root.dataset.theme
  const reduced = matchMedia(REDUCE_MOTION).matches
  const m = MOTION[motionTheme ?? ''] ?? {}
  for (const kind of KINDS) {
    // Every theme and window has stars: a shiny sprite can appear at any moment. A tick with none shown writes nothing.
    const has = kind === 'stars' || (kind === 'snow' ? m.snow === true : Object.keys(m).some((k) => k !== 'snow'))
    const ms = reduced || !has ? undefined : RATES[kind][root.dataset[ATTRIBUTE[kind]] ?? '']
    const now = running[kind]
    if (now?.ms === ms && !themeChanged) continue
    if (now) clearInterval(now.id)
    for (const prop of PROPS[kind]) root.style.removeProperty(prop)
    running[kind] = ms === undefined ? undefined : { id: setInterval(() => move(kind, Date.now() / 1000, ms), ms), ms }
  }
}
