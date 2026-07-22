import React from 'react'

export function WindowCloseButton(): JSX.Element {
  return (
    <button
      className="window-no-drag text-slate-500 hover:text-white text-sm px-1"
      title="Close window"
      onClick={() => window.close()}
    >
      ✕
    </button>
  )
}
