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
  const animations = appearance.animations ? 'on' : 'off'
  document.documentElement.dataset.animations = animations
  localStorage.setItem(ANIMATIONS_KEY, animations)
  const delay = nextSwitchDelay(appearance, windowName, now)
  if (delay !== null) timer = setTimeout(() => applyTheme(appearance, windowName), delay)
  syncMotion()
}

// The theme animations are moved by one timer instead of CSS animations: a running CSS animation, even a
// stepped one, cost about half a core in bursts (docs/specs/dashboard-animation-cost.md). The timer writes
// custom properties on the root, which index.css, themes.css and sprites.css read; stopped, it leaves them
// where they are, so the grounds, glows and snow stay drawn, still.
const REDUCE_MOTION = '(prefers-reduced-motion: reduce)'

// Seconds per cycle of what moves in each theme. The colours and sizes are in assets/themes.css.
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

// The star states, one every 500 ms: the first star's opacity; the second star shows the rest.
const TWINKLE = [0, 0.5, 1, 0.5]

const MOTION_PROPS = ['--header-pos', '--card-fill-pos', '--card-glow-pos', '--card-glow-opacity', '--lights-opacity',
  '--snow-1', '--snow-2', '--snow-3', '--twinkle-1', '--twinkle-2']

// Touched only on the renderer's single thread (applyTheme, initTheme, the media query listener and this timer's callback).
let motion: ReturnType<typeof setInterval> | null = null
let motionTheme: string | undefined
let ticks = 0

// 0 to 1 and back over twice the given seconds, as an alternating CSS animation moves.
function backAndForth(t: number, seconds: number): number {
  const x = (t / seconds) % 2
  return x < 1 ? x : 2 - x
}

// Every 250 ms: the snow 4 times a second, everything else twice.
function tick(): void {
  followTheme()
  const style = document.documentElement.style
  const t = Date.now() / 1000
  const m = MOTION[motionTheme ?? ''] ?? {}
  if (m.snow) SNOW.forEach(([tile, seconds], i) => style.setProperty(`--snow-${i + 1}`, `${(((t / seconds) % 1) * tile).toFixed(1)}px`))
  if (ticks++ % 2 !== 0) return
  if (m.header) style.setProperty('--header-pos', `${(backAndForth(t, m.header) * 100).toFixed(2)}% 50%`)
  if (m.card) style.setProperty('--card-fill-pos', `${(backAndForth(t, m.card) * 100).toFixed(2)}% 50%`)
  if (m.orbit) {
    const f = backAndForth(t, m.orbit / 2)
    style.setProperty('--card-glow-pos', `${(60 * f).toFixed(2)}% ${(40 * f).toFixed(2)}%, ${(100 - 60 * f).toFixed(2)}% ${(100 - 40 * f).toFixed(2)}%`)
  }
  if (m.glow) style.setProperty('--card-glow-opacity', (0.55 + 0.45 * backAndForth(t, m.glow / 2)).toFixed(3))
  if (m.lights) style.setProperty('--lights-opacity', (0.55 + 0.45 * backAndForth(t, m.lights / 2)).toFixed(3))
  if (document.querySelector('.sprite-sparkle')) {
    const v = TWINKLE[Math.floor(t * 2) % TWINKLE.length]
    style.setProperty('--twinkle-1', String(v))
    style.setProperty('--twinkle-2', String(1 - v))
  }
}

// A new theme starts from its own still look, not from where the last theme stopped.
function followTheme(): void {
  const root = document.documentElement
  if (root.dataset.theme === motionTheme) return
  for (const prop of MOTION_PROPS) root.style.removeProperty(prop)
  motionTheme = root.dataset.theme
}

// Runs the timer while the Animations switch is on and Windows does not ask for reduced motion.
function syncMotion(): void {
  followTheme()
  const root = document.documentElement
  const run = root.dataset.animations !== 'off' && !matchMedia(REDUCE_MOTION).matches
  if (run && motion === null) {
    ticks = 0
    motion = setInterval(tick, 250)
  }
  if (!run && motion !== null) {
    clearInterval(motion)
    motion = null
    // Without these the stars fall back to fully shown.
    root.style.removeProperty('--twinkle-1')
    root.style.removeProperty('--twinkle-2')
  }
}
