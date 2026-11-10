import React, { useState } from 'react'
import { getAnimatedSpriteUrl, getSpriteUrl, idleMotionFor } from '../utils/sprites'
import '../assets/sprites.css'

interface PokemonSpriteProps {
  formCode: string
  isShiny: boolean
  name: string
  size: number
}

/** The moving sprite if the folder has one, else the still one with a DS-style idle movement, else nothing. */
export function PokemonSprite({ formCode, isShiny, name, size }: PokemonSpriteProps): JSX.Element | null {
  const [stage, setStage] = useState<'animated' | 'still' | 'none'>('animated')
  if (stage === 'none') return null

  const src = stage === 'animated' ? getAnimatedSpriteUrl(formCode, isShiny) : getSpriteUrl(formCode, isShiny)
  // The entry plays on the stage; the idle movement goes on the img so the two transforms add up.
  const idle = stage === 'still' ? ` ${idleMotionFor(formCode)}` : ''

  return (
    <div className="sprite-stage" style={{ width: size, height: size }}>
      <img
        src={src}
        alt={name}
        className={`pokemon-sprite${idle}`}
        onError={() => setStage(stage === 'animated' ? 'still' : 'none')}
      />
      {isShiny && <span className="sprite-sparkle" aria-hidden />}
    </div>
  )
}
