import React from 'react'
import { useAppContext } from '../context/AppContext'

export function ConnectionStatus(): JSX.Element {
  const { connectionState, config } = useAppContext()

  const labels: Record<string, string> = {
    connected: 'Connected',
    disconnected: 'Disconnected',
    reconnecting: 'Reconnecting...'
  }

  return (
    <div className="flex items-center gap-2 px-3 py-1.5 rounded bg-card-header border border-card-border text-xs">
      <span className={`status-dot ${connectionState}`} />
      <span className="text-slate-400">{labels[connectionState] ?? connectionState}</span>
      {config && (
        <span className="text-slate-600 ml-1">{config.backendUrl}</span>
      )}
    </div>
  )
}
