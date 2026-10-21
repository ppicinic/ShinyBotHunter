import type { PhaseInfo } from '../types/api'

// Every request the admin window makes, one function per endpoint. Each returns the raw fetch
// response; the call sites keep their own status and error handling.

function postJson(url: string, body: unknown): Promise<Response> {
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  })
}

export function getMetadata(apiUrl: string): Promise<Response> {
  return fetch(`${apiUrl}/api/metadata`)
}

export function getAllGames(apiUrl: string): Promise<Response> {
  return fetch(`${apiUrl}/api/getAllGames`)
}

export function subscribeGamePhase(apiUrl: string, body: { game: string; phase: string; gameName: string | null }): Promise<Response> {
  return postJson(`${apiUrl}/api/subcribeGamePhase`, body)
}

export function unsubscribeGamePhase(apiUrl: string, body: { game: string; phase: string; gameName: string | null }): Promise<Response> {
  return postJson(`${apiUrl}/api/unsubcribeGamePhase`, body)
}

export function updateRecentEncounterCount(apiUrl: string, count: number): Promise<Response> {
  return postJson(`${apiUrl}/api/updateRecentEncounterConfig`, count)
}

export function getTopLevelActions(apiUrl: string): Promise<Response> {
  return fetch(`${apiUrl}/api/getTopLevelActions`)
}

export function getDashboardState(apiUrl: string): Promise<Response> {
  return fetch(`${apiUrl}/api/bot/getDashboardState`)
}

export function getBotActions(apiUrl: string, pos: number): Promise<Response> {
  return fetch(`${apiUrl}/api/bot/getBotActions/${pos}`)
}

export function getParty(apiUrl: string, pos: number): Promise<Response> {
  return fetch(`${apiUrl}/api/bot/getParty/${pos}`)
}

export function startBotAction(apiUrl: string, pos: number, body: Record<string, unknown>): Promise<Response> {
  return postJson(`${apiUrl}/api/bot/startAction/${pos}`, body)
}

export function startTopLevelAction(apiUrl: string, body: Record<string, unknown>): Promise<Response> {
  return postJson(`${apiUrl}/api/startTopLevelAction`, body)
}

export function cancelBotAction(apiUrl: string, pos: number): Promise<Response> {
  return postJson(`${apiUrl}/api/bot/cancelAction`, pos)
}

export function getPhaseInfo(apiUrl: string, game: string, phase: string, signal: AbortSignal): Promise<Response> {
  return fetch(`${apiUrl}/api/phaseinfo/${game}/${encodeURIComponent(phase)}`, { signal })
}

export function updatePhaseInfo(apiUrl: string, game: string, phase: string, payload: PhaseInfo): Promise<Response> {
  return postJson(`${apiUrl}/api/updatePhaseInfo/${game}/${encodeURIComponent(phase)}`, payload)
}
