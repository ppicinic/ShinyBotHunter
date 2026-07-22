import React from 'react'
import './assets/index.css'

import { AppProvider } from './context/AppContext'
import { AdminWindow } from './windows/AdminWindow'
import { RecentWindow } from './windows/RecentWindow'
import { PhaseWindow } from './windows/PhaseWindow'

export default function App(): JSX.Element {
  const params = new URLSearchParams(window.location.search)
  const card = params.get('card') ?? 'admin'

  return (
    <AppProvider>
      {card === 'admin' && <AdminWindow />}
      {card === 'recent' && <RecentWindow />}
      {card === 'phase' && (
        <PhaseWindow
          game={params.get('game') ?? ''}
          phase={params.get('phase') ?? ''}
          gameName={params.get('gameName') || null}
        />
      )}
    </AppProvider>
  )
}
