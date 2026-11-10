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
            <table className="w-full border-collapse">
              <thead>
                <tr className="border-b border-card-border">
                  <th className="text-left text-xs text-slate-400 font-medium pb-1 pr-3">Item</th>
                  <th className="text-right text-xs text-slate-400 font-medium pb-1">Count</th>
                </tr>
              </thead>
              <tbody>
                {tally.items.map((item) => (
                  <tr key={item.id} className="border-b border-card-border/30">
                    <td className="py-1 pr-3 text-white text-sm">{item.name}</td>
                    <td className="py-1 text-right text-white font-medium text-sm">{item.count}</td>
                  </tr>
                ))}
                <tr>
                  <td className="py-1 pr-3 text-slate-400 text-sm font-semibold">Total</td>
                  <td className="py-1 text-right text-white font-semibold text-sm">{tally.total}</td>
                </tr>
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  )
}
