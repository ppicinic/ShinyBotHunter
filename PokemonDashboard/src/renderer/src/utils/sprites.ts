/**
 * Resolves the sprite:// URL for a Pokemon sprite. The main process serves it from spritesRootPath.
 *
 * Sprite directory structure under spritesRootPath:
 *   Default/         — normal male/genderless sprites
 *   Shiny/           — shiny male/genderless sprites
 *   Female_Default/  — normal female sprites (for species with unique female forms)
 *   Female_Shiny/    — shiny female sprites
 *   Animated/<the same four>/<form>.gif — the moving sprites, in the same folders
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

// The DS games gave each species one of a few canned movements; the form code picks one here so
// the same Pokemon always moves the same way.
const IDLE_MOTIONS = ['sprite-hop', 'sprite-squash', 'sprite-sway', 'sprite-bob']

export function idleMotionFor(formCode: string): string {
  let h = 0
  for (let i = 0; i < formCode.length; i++) h = (h * 31 + formCode.charCodeAt(i)) >>> 0
  return IDLE_MOTIONS[h % IDLE_MOTIONS.length]
}
