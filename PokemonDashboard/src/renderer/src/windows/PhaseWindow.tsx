import React, { useEffect } from 'react'
import { useAppContext } from '../context/AppContext'
import { PhaseCard } from '../components/PhaseCard'
import { WindowCloseButton } from '../components/WindowChrome'
import { phaseWindowTitle } from '../../../shared/windowContracts'

interface PhaseWindowProps {
  game: string
  // Empty for a follows-the-hunt card, whose URL holds no milestone
  phase: string
  gameName: string | null
}

export function PhaseWindow({ game, phase, gameName }: PhaseWindowProps): JSX.Element {
  const { subscriptions, dashboardConfig } = useAppContext()
  const followsHunt = phase === ''

  useEffect(() => {
    document.title = phaseWindowTitle({ game, phase, gameName, followsHunt })
  }, [game, phase, gameName, followsHunt])

  // A follows-the-hunt card shows the milestone its subscription has now; a new one re-renders this window (FR-39).
  const shown = followsHunt
    ? (dashboardConfig.gamePhases.find((gp) => gp.followsHunt && gp.game === game && (gp.gameName ?? null) === gameName)
        ?.phase ?? '')
    : phase

  const subscription =
    subscriptions.find((s) => s.game === game && s.phase === shown && (s.gameName ?? null) === gameName) ??
    subscriptions.find((s) => s.game === game && s.phase === shown)

  if (!subscription) {
    return (
      <div className="window-root h-screen">
        <div className="dashboard-card h-full">
          <div className="card-header window-drag-region">
            <span className="card-header-title">{shown}</span>
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
      <PhaseCard key={shown} subscription={subscription} windowChrome />
    </div>
  )
}
