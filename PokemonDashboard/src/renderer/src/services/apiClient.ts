import type { PhaseInfo } from '../types/api'

// Every request the dashboard makes to the backend, one function per endpoint. Each goes through
// the main process and returns a Response; the call sites keep their own status and error handling.

async function request(method: string, url: string, body?: unknown): Promise<Response> {
  const res = await window.api.request(method, url, body === undefined ? undefined : JSON.stringify(body))
  return new Response(res.body || null, { status: res.status })
}

export function getMetadata(apiUrl: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/metadata`)
}

export function getAllGames(apiUrl: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/getAllGames`)
}

export function subscribeGamePhase(apiUrl: string, body: { game: string; phase: string; gameName: string | null }): Promise<Response> {
  return request('POST', `${apiUrl}/api/subcribeGamePhase`, body)
}

export function unsubscribeGamePhase(apiUrl: string, body: { game: string; phase: string; gameName: string | null }): Promise<Response> {
  return request('POST', `${apiUrl}/api/unsubcribeGamePhase`, body)
}

export function updateRecentEncounterCount(apiUrl: string, count: number): Promise<Response> {
  return request('POST', `${apiUrl}/api/updateRecentEncounterConfig`, count)
}

export function getTopLevelActions(apiUrl: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/getTopLevelActions`)
}

export function getDashboardState(apiUrl: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/bot/getDashboardState`)
}

export function getBotActions(apiUrl: string, pos: number): Promise<Response> {
  return request('GET', `${apiUrl}/api/bot/getBotActions/${pos}`)
}

export function getParty(apiUrl: string, pos: number): Promise<Response> {
  return request('GET', `${apiUrl}/api/bot/getParty/${pos}`)
}

export function startBotAction(apiUrl: string, pos: number, body: Record<string, unknown>): Promise<Response> {
  return request('POST', `${apiUrl}/api/bot/startAction/${pos}`, body)
}

export function startTopLevelAction(apiUrl: string, body: Record<string, unknown>): Promise<Response> {
  return request('POST', `${apiUrl}/api/startTopLevelAction`, body)
}

export function cancelBotAction(apiUrl: string, pos: number): Promise<Response> {
  return request('POST', `${apiUrl}/api/bot/cancelAction`, pos)
}

// The signal cannot cross IPC, so the request runs to completion and its result is dropped on abort.
export async function getPhaseInfo(apiUrl: string, game: string, phase: string, signal: AbortSignal): Promise<Response> {
  const res = await request('GET', `${apiUrl}/api/phaseinfo/${game}/${encodeURIComponent(phase)}`)
  signal.throwIfAborted()
  return res
}

export function updatePhaseInfo(apiUrl: string, game: string, phase: string, payload: PhaseInfo): Promise<Response> {
  return request('POST', `${apiUrl}/api/updatePhaseInfo/${game}/${encodeURIComponent(phase)}`, payload)
}
