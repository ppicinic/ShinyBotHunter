import React, { useState, useCallback, useEffect } from 'react'
import GridLayout, { Layout } from 'react-grid-layout'
import 'react-grid-layout/css/styles.css'
import 'react-resizable/css/styles.css'
import './assets/index.css'

import { AppProvider, useAppContext } from './context/AppContext'
import { PhaseCard } from './components/PhaseCard'
import { RecentPokemonCard } from './components/RecentPokemonCard'
import { AdminCard } from './components/AdminCard'
import { ConnectionStatus } from './components/ConnectionStatus'

// ============================================================
// Dashboard layout
// ============================================================

const COLS = 24
const ROW_HEIGHT = 30
const MARGIN: [number, number] = [8, 8]

function buildDefaultLayout(
  hasPhaseCards: number,
  showRecent: boolean,
  showAdmin: boolean
): Layout[] {
  const layouts: Layout[] = []
  let x = 0

  // Phase cards: each gets 8 columns
  for (let i = 0; i < hasPhaseCards; i++) {
    layouts.push({
      i: `phase-${i}`,
      x: (i * 8) % COLS,
      y: 0,
      w: 8,
      h: 14,
      minW: 6,
      minH: 8
    })
    x = ((i + 1) * 8) % COLS
  }

  if (showRecent) {
    layouts.push({
      i: 'recent',
      x: x % COLS,
      y: 0,
      w: 8,
      h: 20,
      minW: 6,
      minH: 10
    })
    x = (x + 8) % COLS
  }

  if (showAdmin) {
    layouts.push({
      i: 'admin',
      x: x % COLS,
      y: 0,
      w: 8,
      h: 16,
      minW: 6,
      minH: 10
    })
  }

  return layouts
}

// ============================================================
// Inner dashboard (needs context)
// ============================================================

function Dashboard(): JSX.Element {
  const { config, subscriptions } = useAppContext()

  const showRecent = config?.cards.recentPokemonCard.enabled ?? false
  const showAdmin = config?.cards.adminCard.enabled ?? false
  const showPhase = config?.cards.phaseCard.enabled ?? false

  const phaseCount = showPhase ? subscriptions.length : 0

  const [layout, setLayout] = useState<Layout[]>([])

  // Rebuild layout whenever config or subscription count changes
  useEffect(() => {
    setLayout(buildDefaultLayout(phaseCount, showRecent, showAdmin))
  }, [phaseCount, showRecent, showAdmin])

  const onLayoutChange = useCallback((newLayout: Layout[]) => {
    setLayout(newLayout)
  }, [])

  const [windowWidth, setWindowWidth] = useState(window.innerWidth)
  useEffect(() => {
    const handler = (): void => setWindowWidth(window.innerWidth)
    window.addEventListener('resize', handler)
    return () => window.removeEventListener('resize', handler)
  }, [])

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      {/* Top bar */}
      <div className="flex items-center justify-between px-4 py-2 border-b border-card-border bg-card-header flex-shrink-0">
        <h1 className="text-sm font-bold text-white tracking-wide">🎮 Pokemon Dashboard</h1>
        <ConnectionStatus />
      </div>

      {/* Grid area */}
      <div className="flex-1 overflow-auto">
        <GridLayout
          className="layout"
          layout={layout}
          cols={COLS}
          rowHeight={ROW_HEIGHT}
          width={windowWidth}
          margin={MARGIN}
          onLayoutChange={onLayoutChange}
          draggableHandle=".card-header"
          compactType={null}
          preventCollision={false}
          isResizable={true}
          isDraggable={true}
        >
          {/* Phase cards — one per active subscription */}
          {showPhase && subscriptions.map((sub, idx) => (
            <div key={`phase-${idx}`}>
              <PhaseCard subscription={sub} />
            </div>
          ))}

          {/* Recent Pokemon card */}
          {showRecent && (
            <div key="recent">
              <RecentPokemonCard />
            </div>
          )}

          {/* Admin card */}
          {showAdmin && (
            <div key="admin">
              <AdminCard />
            </div>
          )}
        </GridLayout>
      </div>
    </div>
  )
}

// ============================================================
// Root App
// ============================================================

export default function App(): JSX.Element {
  return (
    <AppProvider>
      <Dashboard />
    </AppProvider>
  )
}
