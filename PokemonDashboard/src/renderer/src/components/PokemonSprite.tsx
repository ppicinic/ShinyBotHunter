import React, { useState } from 'react'
import { getAnimatedSpriteUrl, getSpriteUrl } from '../utils/sprites'

// The DS games gave each species one of a few canned movements; the species name picks one here
// so the same Pokemon always moves the same way.
const MOTIONS = ['sprite-hop', 'sprite-squash', 'sprite-sway', 'sprite-bob']

function motionFor(formCode: string): string {
  let h = 0
  for (let i = 0; i < formCode.length; i++) h = (h * 31 + formCode.charCodeAt(i)) >>> 0
  return MOTIONS[h % MOTIONS.length]
}

interface PokemonSpriteProps {
  formCode: string
  isShiny: boolean
  name: string
  size: number
}

/** The moving sprite if the folder has one, else the still one with a DS-style movement, else nothing. */
export function PokemonSprite({ formCode, isShiny, name, size }: PokemonSpriteProps): JSX.Element | null {
  const [stage, setStage] = useState<'animated' | 'still' | 'none'>('animated')
  if (stage === 'none') return null

  const src = stage === 'animated' ? getAnimatedSpriteUrl(formCode, isShiny) : getSpriteUrl(formCode, isShiny)
  // The entry movement plays once when the sprite mounts; a still sprite then keeps its idle movement.
  const motion = stage === 'animated' ? 'sprite-enter' : `sprite-enter ${motionFor(formCode)}`

  return (
    <div className="sprite-stage" style={{ width: size, height: size }}>
      <img
        src={src}
        alt={name}
        className={`pokemon-sprite ${motion} ${isShiny ? 'sprite-shiny' : ''}`}
        onError={() => setStage(stage === 'animated' ? 'still' : 'none')}
      />
      {isShiny && <span className="sprite-sparkle" aria-hidden />}
    </div>
  )
}
