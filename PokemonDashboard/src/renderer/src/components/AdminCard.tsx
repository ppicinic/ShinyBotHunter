import React, { useEffect, useState } from 'react'
import { useAppContext } from '../context/AppContext'
import * as api from '../services/apiClient'
import type { AdminMetadata } from '../types/api'
import { BotControlTab } from './admin/BotControlTab'
import { DashboardConfigTab } from './admin/DashboardConfigTab'
import { PhaseEditorTab } from './admin/PhaseEditorTab'
import { ConnectionStatus } from './ConnectionStatus'
import { DevModeTab } from './DevModeTab'
import { WindowLayoutTab } from './WindowLayoutTab'

type TabId = 'config' | 'botControl' | 'phaseEditor' | 'windows' | 'macroRecord'

const BASE_TABS: { id: TabId; label: string }[] = [
  { id: 'config', label: 'Dashboard Config' },
  { id: 'botControl', label: 'Bot Control' },
  { id: 'phaseEditor', label: 'Milestone Editor' },
  { id: 'windows', label: 'Window Layout' }
]

// Only shown when the backend reports EnableMacroRecording. The endpoints 404 regardless, so this
// is presentation only.
const DEV_MODE_TAB: { id: TabId; label: string } = { id: 'macroRecord', label: 'Dev Mode' }

export function AdminCard(): JSX.Element {
  const [activeTab, setActiveTab] = useState<TabId>('config')
  const [refreshKey, setRefreshKey] = useState(0)
  const [macroRecordingEnabled, setMacroRecordingEnabled] = useState(false)

  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''

  useEffect(() => {
    if (!apiUrl) return
    api.getMetadata(apiUrl)
      .then((r) => (r.ok ? r.json() : null))
      .then((data: AdminMetadata | null) => setMacroRecordingEnabled(data?.macroRecordingEnabled ?? false))
      .catch(() => setMacroRecordingEnabled(false))
  }, [apiUrl])

  const tabs = macroRecordingEnabled ? [...BASE_TABS, DEV_MODE_TAB] : BASE_TABS

  // If the flag is turned off while the dev tab is open, fall back rather than render nothing.
  useEffect(() => {
    if (!macroRecordingEnabled && activeTab === 'macroRecord') setActiveTab('config')
  }, [macroRecordingEnabled, activeTab])

  const handleRefresh = (): void => {
    setRefreshKey((k) => k + 1)
  }

  return (
    <div className="dashboard-card h-full">
      <div className="card-header">
        <span className="card-header-title">Admin</span>
        <div className="flex items-center gap-3">
          <ConnectionStatus />
          <button
            onClick={handleRefresh}
            title="Refresh"
            className="text-slate-400 hover:text-white transition-colors text-sm px-1"
          >
            ↻
          </button>
        </div>
      </div>

      {/* Tab bar */}
      <div className="tab-bar px-2">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            className={`tab-btn ${activeTab === tab.id ? 'active' : ''}`}
            onClick={() => setActiveTab(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-auto">
        {activeTab === 'config' && <DashboardConfigTab />}
        {activeTab === 'botControl' && (
          <BotControlTab key={refreshKey} />
        )}
        {activeTab === 'phaseEditor' && <PhaseEditorTab />}
        {activeTab === 'windows' && <WindowLayoutTab />}
        {activeTab === 'macroRecord' && macroRecordingEnabled && <DevModeTab />}
      </div>
    </div>
  )
}
