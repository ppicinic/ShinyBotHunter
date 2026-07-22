import React, { useEffect } from 'react'
import { RecentPokemonCard } from '../components/RecentPokemonCard'

export function RecentWindow(): JSX.Element {
  useEffect(() => {
    document.title = 'Pokemon Dashboard — Recent Encounters'
  }, [])

  return (
    <div className="window-root h-screen">
      <RecentPokemonCard windowChrome />
    </div>
  )
}
