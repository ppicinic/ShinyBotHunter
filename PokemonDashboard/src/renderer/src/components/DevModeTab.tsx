import React from 'react'
import { LocationPanel } from './LocationPanel'
import { MacroRecordTab } from './MacroRecordTab'

/**
 * Container for the dev-mode tools. The panels are independent — each owns its emulator selection
 * and its own requests — so they are simply stacked rather than sharing any state.
 */
export function DevModeTab(): JSX.Element {
  return (
    <div>
      <LocationPanel />
      <MacroRecordTab />
    </div>
  )
}
