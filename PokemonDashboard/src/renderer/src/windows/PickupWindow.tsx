import React, { useEffect, useState } from 'react'
import { WindowCloseButton } from '../components/WindowChrome'
import { useAppContext } from '../context/AppContext'
import * as api from '../services/apiClient'
import { pickupWindowTitle, type PickupTally } from '../../../shared/windowContracts'

interface PickupWindowProps {
  gameFileName: string
}

export function PickupWindow({ gameFileName }: PickupWindowProps): JSX.Element {
  const { config } = useAppContext()
  const [tally, setTally] = useState<PickupTally | null>(null)

  useEffect(() => {
    document.title = pickupWindowTitle(gameFileName)
  }, [gameFileName])

  useEffect(() => {
    if (!config?.apiUrl) return
    // Listen before the first load so a push between the two is not missed.
    const unsubscribe = window.api.onPickupItems((pushed) => {
      if (pushed.gameFileName === gameFileName) setTally(pushed)
    })
    api.getPickupItems(config.apiUrl)
      .then((r) => {
        if (!r.ok) throw new Error(`Server returned ${r.status}`)
        return r.json()
      })
      .then((tallies: PickupTally[]) => {
        const loaded = tallies.find((t) => t.gameFileName === gameFileName) ?? null
        // The tally only grows between bot restarts, so a pushed one is never older than the load.
        setTally((prev) => prev ?? loaded)
      })
      .catch((err) => console.error('Failed to load Pickup items:', err))
    return unsubscribe
  }, [config?.apiUrl, gameFileName])

  return (
    <div className="window-root h-screen">
      <div className="dashboard-card h-full flex flex-col">
        <div className="card-header window-drag-region">
          <div>
            <span className="text-xs text-slate-500 mr-2">{gameFileName}</span>
            {tally && <span className="card-header-title">{tally.milestone}</span>}
          </div>
          <WindowCloseButton />
        </div>
        <div className="flex-1 overflow-auto px-2 py-1">
          {!tally ? (
            <div className="flex items-center justify-center h-full text-slate-500 text-sm">No items yet</div>
          ) : (
            <>
              <div className="grid gap-2 grid-cols-[repeat(auto-fill,minmax(5rem,1fr))]">
                {tally.items.map((item) => (
                  <div key={item.id} data-testid="pickup-item" className="flex flex-col items-center text-center">
                    <div className="w-12 h-12 flex items-center justify-center">
                      <img
                        src={`sprite://local/Items/${item.id}.png`}
                        alt={item.name}
                        className="pokemon-sprite w-12 h-12 object-contain"
                        onError={(e) => {
                          (e.target as HTMLImageElement).style.display = 'none'
                        }}
                      />
                    </div>
                    <span data-testid="pickup-item-name" className="text-white text-xs">{item.name}</span>
                    <span data-testid="pickup-item-count" className="text-white font-medium text-sm">{item.count}</span>
                  </div>
                ))}
              </div>
              <div data-testid="pickup-total" className="flex justify-between border-t border-card-border mt-2 py-1">
                <span className="text-slate-400 text-sm font-semibold">Total</span>
                <span className="text-white font-semibold text-sm">{tally.total}</span>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
