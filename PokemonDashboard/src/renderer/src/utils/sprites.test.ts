import { describe, expect, it } from 'vitest'
import { getAnimatedSpriteUrl, getSpriteUrl, idleMotionFor } from './sprites'

describe('getSpriteUrl', () => {
  it('routes a plain form to Default or Shiny', () => {
    expect(getSpriteUrl('chikorita', false)).toBe('sprite://local/Default/chikorita.png')
    expect(getSpriteUrl('chikorita', true)).toBe('sprite://local/Shiny/chikorita.png')
  })

  it('routes a female form to the Female folders without the ;f', () => {
    expect(getSpriteUrl('meganium;f', false)).toBe('sprite://local/Female_Default/meganium.png')
    expect(getSpriteUrl('meganium;f', true)).toBe('sprite://local/Female_Shiny/meganium.png')
  })
})

describe('getAnimatedSpriteUrl', () => {
  it('routes a plain form to Animated/Default or Animated/Shiny', () => {
    expect(getAnimatedSpriteUrl('pikachu', false)).toBe('sprite://local/Animated/Default/pikachu.gif')
    expect(getAnimatedSpriteUrl('pikachu', true)).toBe('sprite://local/Animated/Shiny/pikachu.gif')
  })

  it('routes a female form to the animated Female folders', () => {
    expect(getAnimatedSpriteUrl('pikachu;f', false)).toBe('sprite://local/Animated/Female_Default/pikachu.gif')
    expect(getAnimatedSpriteUrl('pikachu;f', true)).toBe('sprite://local/Animated/Female_Shiny/pikachu.gif')
  })
})

describe('idleMotionFor', () => {
  it('gives the same motion for the same form every time', () => {
    expect(idleMotionFor('eevee')).toBe(idleMotionFor('eevee'))
  })

  it('keeps the pinned motions', () => {
    expect(idleMotionFor('eevee')).toBe('sprite-sway')
    expect(idleMotionFor('bulbasaur')).toBe('sprite-bob')
    expect(idleMotionFor('wailord')).toBe('sprite-hop')
  })
})
