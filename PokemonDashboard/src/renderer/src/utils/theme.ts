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

export type ThemeId = (typeof THEMES)[number]['id']

const KEY = 'dashboard-theme'

export function currentTheme(): ThemeId {
  const saved = localStorage.getItem(KEY)
  return THEMES.some((t) => t.id === saved) ? (saved as ThemeId) : 'midnight'
}

function apply(): void {
  document.documentElement.dataset.theme = currentTheme()
}

/** Every dashboard window shares one localStorage, and the storage event reaches the other windows. */
export function initTheme(): void {
  apply()
  window.addEventListener('storage', (e) => {
    if (e.key === KEY) apply()
  })
}

export function setTheme(id: ThemeId): void {
  localStorage.setItem(KEY, id)
  apply()
}
