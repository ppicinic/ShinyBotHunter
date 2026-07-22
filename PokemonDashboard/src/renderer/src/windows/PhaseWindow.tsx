import React, { useEffect } from 'react'
import { useAppContext } from '../context/AppContext'
import { PhaseCard } from '../components/PhaseCard'
import { WindowCloseButton } from '../components/WindowChrome'
import { phaseWindowTitle } from '../../../shared/windowContracts'

interface PhaseWindowProps {
  game: string
  phase: string
  gameName: string | null
}

export function PhaseWindow({ game, phase, gameName }: PhaseWindowProps): JSX.Element {
  const { subscriptions } = useAppContext()

  useEffect(() => {
    document.title = phaseWindowTitle({ game, phase, gameName })
  }, [game, phase, gameName])

  const subscription =
    subscriptions.find((s) => s.game === game && s.phase === phase && (s.gameName ?? null) === gameName) ??
    subscriptions.find((s) => s.game === game && s.phase === phase)

  if (!subscription) {
    return (
      <div className="window-root h-screen">
        <div className="dashboard-card h-full">
          <div className="card-header window-drag-region">
            <span className="card-header-title">{phase}</span>
            <WindowCloseButton />
          </div>
          <div className="flex-1 flex items-center justify-center text-slate-500 text-sm">
            Connecting…
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="window-root h-screen">
      <PhaseCard subscription={subscription} windowChrome />
    </div>
  )
}
