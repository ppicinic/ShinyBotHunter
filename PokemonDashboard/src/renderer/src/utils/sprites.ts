/**
 * Resolves the local file:// URL for a Pokemon sprite.
 *
 * Sprite directory structure under spritesRootPath:
 *   Default/         — normal male/genderless sprites
 *   Shiny/           — shiny male/genderless sprites
 *   Female_Default/  — normal female sprites (for species with unique female forms)
 *   Female_Shiny/    — shiny female sprites
 *
 * The formCode from the backend is the PokeAPI slug (e.g. "chikorita").
 * For female-form Pokemon the formCode is appended with ";f" (e.g. "meganium;f").
 * We strip the ";f" suffix to get the filename and route to the Female_* directory.
 */
export function getSpriteUrl(spritesRootPath: string, formCode: string, isShiny: boolean): string {
  const root = spritesRootPath.replace(/\\/g, '/').replace(/\/$/, '')

  const isFemaleForm = formCode.endsWith(';f')
  const filename = isFemaleForm ? formCode.slice(0, -2) : formCode

  let subdir: string
  if (isFemaleForm) {
    subdir = isShiny ? 'Female_Shiny' : 'Female_Default'
  } else {
    subdir = isShiny ? 'Shiny' : 'Default'
  }

  return `file:///${root}/${subdir}/${filename}.png`
}
