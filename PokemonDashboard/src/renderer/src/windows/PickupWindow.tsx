import React, { useEffect, useState } from 'react'
import { WindowCloseButton } from '../components/WindowChrome'
import { pickupWindowTitle, type PickupTally } from '../../../shared/windowContracts'

interface PickupWindowProps {
  gameFileName: string
}

export function PickupWindow({ gameFileName }: PickupWindowProps): JSX.Element {
  const [tally, setTally] = useState<PickupTally | null>(null)

  useEffect(() => {
    document.title = pickupWindowTitle(gameFileName)
    // Listen before the first load so a push between the two is not missed.
    const unsubscribe = window.api.onPickupItems((pushed) => {
      if (pushed.gameFileName === gameFileName) setTally(pushed)
    })
    window.api.getPickupItems(gameFileName).then(setTally)
    return unsubscribe
  }, [gameFileName])

  return (
    <div className="window-root h-screen">
      <div className="dashboard-card h-full flex flex-col">
        <div className="card-header window-drag-region">
          <span className="card-header-title">Pickup: {gameFileName}</span>
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
