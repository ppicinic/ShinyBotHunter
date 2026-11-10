/**
 * Resolves the sprite:// URLs for a Pokemon sprite. The main process serves them from spritesRootPath.
 *
 * Sprite directory structure under spritesRootPath:
 *   Default/         — normal male/genderless sprites
 *   Shiny/           — shiny male/genderless sprites
 *   Female_Default/  — normal female sprites (for species with unique female forms)
 *   Female_Shiny/    — shiny female sprites
 *   Animated/<the same four>/<name>.gif — the moving version, where one exists
 *
 * The formCode from the backend is the PokeAPI slug (e.g. "chikorita").
 * For female-form Pokemon the formCode is appended with ";f" (e.g. "meganium;f").
 * We strip the ";f" suffix to get the filename and route to the Female_* directory.
 */
function spritePath(formCode: string, isShiny: boolean): string {
  const isFemaleForm = formCode.endsWith(';f')
  const filename = isFemaleForm ? formCode.slice(0, -2) : formCode

  let subdir: string
  if (isFemaleForm) {
    subdir = isShiny ? 'Female_Shiny' : 'Female_Default'
  } else {
    subdir = isShiny ? 'Shiny' : 'Default'
  }
  return `${subdir}/${filename}`
}

export function getSpriteUrl(formCode: string, isShiny: boolean): string {
  return `sprite://local/${spritePath(formCode, isShiny)}.png`
}

export function getAnimatedSpriteUrl(formCode: string, isShiny: boolean): string {
  return `sprite://local/Animated/${spritePath(formCode, isShiny)}.gif`
}
