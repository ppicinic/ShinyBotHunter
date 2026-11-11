import React, { useEffect, useRef, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import {
  ADMIN_WINDOW,
  RECENT_WINDOW,
  phaseWindowName,
  type AnimationLevel,
  type Appearance,
  type ThemeChoice,
  type WindowInfo
} from '../../../../shared/windowContracts'
import { NIGHT_THEME_ORDER, THEMES, windowLabel } from '../../utils/theme'

// The Default row's data-window; no window is named this.
const DEFAULT_ROW = 'default'

const ANIMATION_KINDS = [
  { kind: 'gradients', label: 'Gradients' },
  { kind: 'snow', label: 'Snow' },
  { kind: 'stars', label: 'Stars' }
] as const

const ANIMATION_LEVELS: { level: AnimationLevel; name: string }[] = [
  { level: 'off', name: 'Off' },
  { level: 'low', name: 'Low' },
  { level: 'medium', name: 'Medium' },
  { level: 'high', name: 'High' }
]

const selectClass =
  'bg-card-border text-white text-sm rounded px-2 py-1 border border-slate-600 focus:outline-none focus:border-indigo-500'

function themeLabel(id: string): string {
  return THEMES.find((t) => t.id === id)?.label ?? id
}

interface Props {
  showFeedback: (msg: string, isError?: boolean) => void
}

export function AppearanceSection({ showFeedback }: Props): JSX.Element {
  const { dashboardConfig, config } = useAppContext()
  const appearance = dashboardConfig.appearance
  const [pickupNames, setPickupNames] = useState<string[]>([])
  // Rows ticked for "Switch at night" whose night theme is not picked yet; nothing is posted for them.
  const [pendingNight, setPendingNight] = useState<Set<string>>(new Set())
  // The newest of the pushed appearance and the last posted one, so a second change made
  // before the first one's push returns builds on it instead of undoing it.
  const latest = useRef<Appearance | null>(appearance)

  useEffect(() => {
    latest.current = appearance
  }, [appearance])

  // Main broadcasts the window list whenever a window opens, closes, moves or resizes.
  useEffect(() => {
    const read = (list: WindowInfo[]): void =>
      setPickupNames(list.filter((w) => w.kind === 'pickup').map((w) => w.name))
    window.api.getWindowList().then(read).catch(console.error)
    return window.api.onWindowBoundsChanged(read)
  }, [])

  const heading = (
    <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">Appearance</h3>
  )

  if (!appearance) {
    return (
      <div>
        {heading}
        <p className="text-slate-500 text-sm">Waiting for the backend</p>
      </div>
    )
  }

  const post = async (next: Appearance): Promise<void> => {
    latest.current = next
    try {
      const res = await api.setAppearance(config?.apiUrl ?? '', next)
      if (!res.ok) throw new Error(`Server returned ${res.status}`)
    } catch (err) {
      showFeedback(err instanceof Error ? err.message : 'Unknown error', true)
    }
  }

  const currentChoice = (row: string): ThemeChoice | undefined => {
    const current = latest.current as Appearance
    return row === DEFAULT_ROW ? current.default : current.windows[row]
  }

  const postChoice = (row: string, choice: ThemeChoice): Promise<void> => {
    const current = latest.current as Appearance
    return row === DEFAULT_ROW
      ? post({ ...current, default: choice })
      : post({ ...current, windows: { ...current.windows, [row]: choice } })
  }

  const removeWindow = (row: string): Promise<void> => {
    const current = latest.current as Appearance
    const windows = { ...current.windows }
    delete windows[row]
    return post({ ...current, windows })
  }

  const setPending = (row: string, on: boolean): void => {
    setPendingNight((prev) => {
      const next = new Set(prev)
      if (on) next.add(row)
      else next.delete(row)
      return next
    })
  }

  const handleTheme = (row: string, theme: string): void => {
    if (theme === '') {
      setPending(row, false)
      removeWindow(row)
      return
    }
    postChoice(row, { ...currentChoice(row), theme })
  }

  const handleSwitch = (row: string, on: boolean): void => {
    setPending(row, on)
    const choice = currentChoice(row)
    if (!on && choice?.night) postChoice(row, { theme: choice.theme })
  }

  const handleNight = (row: string, night: string): void => {
    setPending(row, false)
    postChoice(row, { ...(currentChoice(row) as ThemeChoice), night })
  }

  const handleTime = (field: 'dayStart' | 'nightStart', value: string): void => {
    // The backend applies no fallback on the POST body, so a cleared field is not posted
    if (value === '') return
    post({ ...(latest.current as Appearance), [field]: value })
  }

  const openRows = [
    ADMIN_WINDOW,
    RECENT_WINDOW,
    ...dashboardConfig.gamePhases.map(phaseWindowName),
    ...pickupNames
  ]
  const goneRows = Object.keys(appearance.windows).filter((name) => !openRows.includes(name))
  const showTimes =
    pendingNight.size > 0 ||
    !!appearance.default.night ||
    Object.values(appearance.windows).some((choice) => !!choice.night)

  const renderRow = (row: string): JSX.Element => {
    const isDefault = row === DEFAULT_ROW
    const choice = isDefault ? appearance.default : appearance.windows[row]
    const switchOn = !!choice?.night || pendingNight.has(row)
    return (
      <div
        key={row}
        data-window={row}
        className="flex items-center gap-2 flex-wrap bg-card-border/30 rounded px-3 py-1.5"
      >
        <span className="text-sm text-white flex-1 min-w-[10rem]">{isDefault ? 'Default' : windowLabel(row)}</span>
        <select
          aria-label="Theme"
          value={choice?.theme ?? ''}
          onChange={(e) => handleTheme(row, e.target.value)}
          className={selectClass}
        >
          {!isDefault && <option value="">Default</option>}
          {THEMES.map((t) => (
            <option key={t.id} value={t.id}>{t.label}</option>
          ))}
        </select>
        <label className="flex items-center gap-1 text-xs text-slate-300">
          <input
            type="checkbox"
            aria-label="Switch at night"
            checked={switchOn}
            disabled={!choice}
            onChange={(e) => handleSwitch(row, e.target.checked)}
          />
          Switch at night
        </label>
        {switchOn && (
          <select
            aria-label="Night theme"
            value={choice?.night ?? ''}
            onChange={(e) => handleNight(row, e.target.value)}
            className={selectClass}
          >
            <option value="" disabled>Choose a night theme</option>
            {NIGHT_THEME_ORDER.map((id) => (
              <option key={id} value={id}>{themeLabel(id)}</option>
            ))}
          </select>
        )}
      </div>
    )
  }

  return (
    <div>
      {heading}
      <div className="flex gap-4 items-center mb-2 text-xs text-slate-300">
        Animations
        {ANIMATION_KINDS.map(({ kind, label }) => (
          <label key={kind} className="flex items-center gap-2">
            {label}
            <select
              aria-label={label}
              value={appearance.animations[kind]}
              onChange={(e) => {
                const current = latest.current as Appearance
                post({ ...current, animations: { ...current.animations, [kind]: e.target.value as AnimationLevel } })
              }}
              className={selectClass}
            >
              {ANIMATION_LEVELS.map(({ level, name }) => (
                <option key={level} value={level}>{name}</option>
              ))}
            </select>
          </label>
        ))}
      </div>
      {showTimes && (
        <div className="flex gap-4 items-center mb-2 text-xs text-slate-300">
          <label className="flex items-center gap-2">
            Day starts
            <input
              type="time"
              aria-label="Day starts"
              value={appearance.dayStart}
              onChange={(e) => handleTime('dayStart', e.target.value)}
              className={selectClass}
            />
          </label>
          <label className="flex items-center gap-2">
            Night starts
            <input
              type="time"
              aria-label="Night starts"
              value={appearance.nightStart}
              onChange={(e) => handleTime('nightStart', e.target.value)}
              className={selectClass}
            />
          </label>
        </div>
      )}
      <div className="space-y-1">
        {[DEFAULT_ROW, ...openRows].map(renderRow)}
        {goneRows.map((row) => (
          <div
            key={row}
            data-window={row}
            className="flex items-center gap-2 bg-card-border/30 rounded px-3 py-1.5"
          >
            <span className="text-sm text-white flex-1 opacity-50">{windowLabel(row)}</span>
            <span className="text-xs text-slate-400 opacity-50">{themeLabel(appearance.windows[row].theme)}</span>
            <button
              onClick={() => removeWindow(row)}
              className="text-xs text-red-400 hover:text-red-300 transition-colors"
            >
              Remove
            </button>
          </div>
        ))}
      </div>
    </div>
  )
}
