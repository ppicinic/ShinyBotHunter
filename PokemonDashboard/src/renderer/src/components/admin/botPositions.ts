import type { BotDashboardState, EventArgumentOptions, PartySlotInfo } from '../../types/api'
import { initialParamsFor } from './actionSchema'

// The Bot Control tab's per-position records, held in one reducer. Every record is keyed by
// emulator position.
export type BotPositionsState = {
  botState: BotDashboardState
  // Per-bot available actions: pos -> EventArgumentOptions[]
  botActions: Record<number, EventArgumentOptions[]>
  // Per-bot action parameter values: pos -> { actionName -> { paramName -> value } }
  botActionParams: Record<number, Record<string, Record<string, string>>>
  // Legs of chained actions: pos -> { actionName -> one param record per leg }
  botChainLegs: Record<number, Record<string, Record<string, string>[]>>
  // Which action's form is showing per bot: pos -> actionName
  selectedBotAction: Record<number, string>
  // Live party per bot, loaded on demand so Switch train mode can offer real pokemon: pos -> slots
  partyByPos: Record<number, PartySlotInfo[]>
  // Set by a position's Run or Cancel button while its request is pending; one request per position.
  posInFlight: Record<number, 'run' | 'cancel'>
}

export type BotPositionsAction =
  | { type: 'stateReceived'; state: BotDashboardState }
  | { type: 'actionsLoaded'; updates: Record<number, EventArgumentOptions[]> }
  | { type: 'actionSelected'; pos: number; actionName: string }
  | { type: 'paramChanged'; pos: number; actionName: string; param: string; value: string }
  | { type: 'legParamChanged'; pos: number; actionName: string; legIndex: number; param: string; value: string }
  | { type: 'legAdded'; pos: number; action: EventArgumentOptions }
  | { type: 'legRemoved'; pos: number; actionName: string; legIndex: number }
  | { type: 'partyLoaded'; pos: number; party: PartySlotInfo[] }
  | { type: 'requestStarted'; pos: number; kind: 'run' | 'cancel' }
  | { type: 'requestFinished'; pos: number }

export const initialBotPositions: BotPositionsState = {
  botState: {},
  botActions: {},
  botActionParams: {},
  botChainLegs: {},
  selectedBotAction: {},
  partyByPos: {},
  posInFlight: {}
}

export function botPositionsReducer(state: BotPositionsState, change: BotPositionsAction): BotPositionsState {
  switch (change.type) {
    case 'stateReceived':
      return { ...state, botState: change.state }

    case 'actionsLoaded': {
      const { updates } = change
      // Initialize param values for any new actions (required + optional)
      const nextParams = { ...state.botActionParams }
      for (const [posStr, actionList] of Object.entries(updates)) {
        const pos = Number(posStr)
        if (!nextParams[pos]) nextParams[pos] = {}
        actionList.forEach((action) => {
          if (!nextParams[pos][action.name]) {
            nextParams[pos][action.name] = initialParamsFor(action)
          }
        })
      }
      // Chained actions start with a single leg; the operator adds the rest.
      const nextLegs = { ...state.botChainLegs }
      for (const [posStr, actionList] of Object.entries(updates)) {
        const pos = Number(posStr)
        if (!nextLegs[pos]) nextLegs[pos] = {}
        actionList.forEach((action) => {
          if (action.legParameters && !nextLegs[pos][action.name]) {
            nextLegs[pos][action.name] = [initialParamsFor(action.legParameters)]
          }
        })
      }
      // Only one action's form shows at a time, so fall back to the first whenever the current pick
      // is no longer on offer (the available set changes as the bot's state does).
      const nextSelected = { ...state.selectedBotAction }
      for (const [posStr, actionList] of Object.entries(updates)) {
        const pos = Number(posStr)
        if (actionList.length === 0) {
          delete nextSelected[pos]
        } else if (!actionList.some((action) => action.name === nextSelected[pos])) {
          nextSelected[pos] = actionList[0].name
        }
      }
      return { ...state, botActions: updates, botActionParams: nextParams, botChainLegs: nextLegs, selectedBotAction: nextSelected }
    }

    case 'actionSelected':
      return { ...state, selectedBotAction: { ...state.selectedBotAction, [change.pos]: change.actionName } }

    case 'paramChanged': {
      const { pos, actionName, param, value } = change
      const prev = state.botActionParams
      return {
        ...state,
        botActionParams: {
          ...prev,
          [pos]: {
            ...prev[pos],
            [actionName]: { ...(prev[pos]?.[actionName] ?? {}), [param]: value }
          }
        }
      }
    }

    case 'legParamChanged': {
      const { pos, actionName, legIndex, param, value } = change
      const prev = state.botChainLegs
      const legs = (prev[pos]?.[actionName] ?? []).map((leg, i) => (i === legIndex ? { ...leg, [param]: value } : leg))
      return { ...state, botChainLegs: { ...prev, [pos]: { ...prev[pos], [actionName]: legs } } }
    }

    case 'legAdded': {
      const { pos, action } = change
      if (!action.legParameters) return state
      const prev = state.botChainLegs
      const legs = [...(prev[pos]?.[action.name] ?? []), initialParamsFor(action.legParameters!)]
      return { ...state, botChainLegs: { ...prev, [pos]: { ...prev[pos], [action.name]: legs } } }
    }

    case 'legRemoved': {
      const { pos, actionName, legIndex } = change
      const prev = state.botChainLegs
      const legs = (prev[pos]?.[actionName] ?? []).filter((_, i) => i !== legIndex)
      // A chain always keeps at least one leg -- an empty one is just the single-encounter form.
      if (legs.length === 0) return state
      return { ...state, botChainLegs: { ...prev, [pos]: { ...prev[pos], [actionName]: legs } } }
    }

    case 'partyLoaded':
      return { ...state, partyByPos: { ...state.partyByPos, [change.pos]: change.party } }

    case 'requestStarted':
      return { ...state, posInFlight: { ...state.posInFlight, [change.pos]: change.kind } }

    case 'requestFinished': {
      const next = { ...state.posInFlight }
      delete next[change.pos]
      return { ...state, posInFlight: next }
    }
  }
}
