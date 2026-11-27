import type { Appearance } from '../../../shared/windowContracts'
import type { GoalPreset, PhaseInfo, RomVersion } from '../types/api'

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

export function subscribeGamePhase(apiUrl: string, body: { game: string; phase: string; gameName: string | null; followsHunt: boolean }): Promise<Response> {
  return request('POST', `${apiUrl}/api/subcribeGamePhase`, body)
}

export function unsubscribeGamePhase(apiUrl: string, body: { game: string; phase: string; gameName: string | null; followsHunt: boolean }): Promise<Response> {
  return request('POST', `${apiUrl}/api/unsubcribeGamePhase`, body)
}

export function updateRecentEncounterCount(apiUrl: string, count: number): Promise<Response> {
  return request('POST', `${apiUrl}/api/updateRecentEncounterConfig`, count)
}

export function setAppearance(apiUrl: string, appearance: Appearance): Promise<Response> {
  return request('POST', `${apiUrl}/api/admin/appearance`, appearance)
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

// 404 when no bot is at the position.
export function getBotMilestones(apiUrl: string, pos: number): Promise<Response> {
  return request('GET', `${apiUrl}/api/bot/milestones/${pos}`)
}

// The milestone's anchor on the bot's game file becomes where the bot stands, hunted with this range (both 0: spin). 400 with the reason.
export function setAnchor(apiUrl: string, pos: number, milestone: string, xRange: number, yRange: number): Promise<Response> {
  return request('PUT', `${apiUrl}/api/bot/anchor/${pos}?milestone=${encodeURIComponent(milestone)}&xRange=${xRange}&yRange=${yRange}`)
}

export function clearAnchor(apiUrl: string, pos: number, milestone: string): Promise<Response> {
  return request('DELETE', `${apiUrl}/api/bot/anchor/${pos}?milestone=${encodeURIComponent(milestone)}`)
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

// The phase window's form: phase info plus initial encounters, optionally for one game file.
export function getSubscriptionPhaseInfo(apiUrl: string, game: string, phase: string, gameName?: string | null): Promise<Response> {
  const base = `${apiUrl}/api/phaseinfo/${game}/${encodeURIComponent(phase)}`
  return request('GET', gameName ? `${base}/${encodeURIComponent(gameName)}` : base)
}

// The editor's location dropdown; 404 for a game with no generated locations.
export function getLocations(apiUrl: string, game: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/milestones/${encodeURIComponent(game)}/locations`)
}

// "New from game data": the location's rows at the preset's amounts, for one version or both (null). Stores nothing.
export function getSeed(apiUrl: string, game: string, locationId: string, preset: GoalPreset, version: RomVersion | null): Promise<Response> {
  const query = version ? `preset=${preset}&version=${version}` : `preset=${preset}`
  return request('GET', `${apiUrl}/api/milestones/${encodeURIComponent(game)}/locations/${encodeURIComponent(locationId)}/seed?${query}`)
}

// Writes only the milestone's location, never a row or an amount; 404 when either is unknown.
export function attachLocation(apiUrl: string, game: string, name: string, locationId: string): Promise<Response> {
  return request('PUT', `${apiUrl}/api/milestones/${encodeURIComponent(game)}/${encodeURIComponent(name)}/location/${encodeURIComponent(locationId)}`)
}

export function detachLocation(apiUrl: string, game: string, name: string): Promise<Response> {
  return request('DELETE', `${apiUrl}/api/milestones/${encodeURIComponent(game)}/${encodeURIComponent(name)}/location`)
}

export function getEncounterRate(apiUrl: string, game: string, phase: string, gameName?: string | null): Promise<Response> {
  const base = `${apiUrl}/api/phaseinfo/encounterRate/${game}/${encodeURIComponent(phase)}`
  return request('GET', gameName ? `${base}/${encodeURIComponent(gameName)}` : base)
}

export function getLocation(apiUrl: string, pos: number): Promise<Response> {
  return request('GET', `${apiUrl}/api/dev/location/${pos}`)
}

export function getMacroRecordStatus(apiUrl: string, pos: number): Promise<Response> {
  return request('GET', `${apiUrl}/api/dev/macroRecord/status/${pos}`)
}

export function startMacroRecord(apiUrl: string, pos: number): Promise<Response> {
  return request('POST', `${apiUrl}/api/dev/macroRecord/start/${pos}`)
}

export function stopMacroRecord(apiUrl: string, pos: number, name: string): Promise<Response> {
  return request('POST', `${apiUrl}/api/dev/macroRecord/stop/${pos}`, { name })
}

// Each action a plan step may be, with its schema, in the plan's own order.
export function getPlanSteps(apiUrl: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/plans/steps`)
}

export function getPlans(apiUrl: string, game: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/plans/${encodeURIComponent(game)}`)
}

// Saved under the route's game and name whatever the body says, but the body must carry a name; 400 with the reason.
export function savePlan(apiUrl: string, game: string, name: string, plan: import('../types/api').HuntPlan): Promise<Response> {
  return request('PUT', `${apiUrl}/api/plans/${encodeURIComponent(game)}/${encodeURIComponent(name)}`, plan)
}

// 400 with the reason when the plan is in use or there is no such plan.
export function deletePlan(apiUrl: string, game: string, name: string): Promise<Response> {
  return request('DELETE', `${apiUrl}/api/plans/${encodeURIComponent(game)}/${encodeURIComponent(name)}`)
}

// 400 with the reason when the plan is in use, the new name is blank or already taken.
export function renamePlan(apiUrl: string, game: string, name: string, to: string): Promise<Response> {
  return request('POST', `${apiUrl}/api/plans/${encodeURIComponent(game)}/${encodeURIComponent(name)}/rename?to=${encodeURIComponent(to)}`)
}
