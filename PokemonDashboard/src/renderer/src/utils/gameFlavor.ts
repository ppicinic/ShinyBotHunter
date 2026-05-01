// C# GameFlavor enum — must stay in sync with ShinyBotHunter/Macro/GameFlavor.cs
export const GAME_FLAVOR_NAMES: Record<number, string> = {
  0: 'HGSS'
}

/**
 * Normalizes a GameFlavor value that may be a C# enum integer or already a string name.
 */
export function normalizeGameFlavor(gameFlavor: number | string): string {
  if (typeof gameFlavor === 'number') {
    return GAME_FLAVOR_NAMES[gameFlavor] ?? String(gameFlavor)
  }
  return gameFlavor
}
