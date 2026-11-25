import type { Appearance } from '../../../shared/windowContracts'
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

export function setMilestoneArchived(apiUrl: string, gameFileName: string, milestone: string, archived: boolean): Promise<Response> {
  return request('PUT', `${apiUrl}/api/goal/${encodeURIComponent(gameFileName)}/archived?milestone=${encodeURIComponent(milestone)}&archived=${archived}`)
}

// An empty label clears the milestone's label.
export function setMilestoneOrder(apiUrl: string, gameFileName: string, milestone: string, label: string): Promise<Response> {
  return request('PUT', `${apiUrl}/api/goal/${encodeURIComponent(gameFileName)}/order`, { milestone, label })
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

// A generated milestone's rows seen through the game file's goal, with the encounter data; 404 when the file's version has no such milestone.
export function getMilestoneRows(apiUrl: string, game: string, milestoneId: string, gameFileName: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/milestone/${encodeURIComponent(game)}/${encodeURIComponent(milestoneId)}/${encodeURIComponent(gameFileName)}`)
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

// A game file with no goal stored reads as an empty one.
export function getGoal(apiUrl: string, gameFileName: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/goal/${encodeURIComponent(gameFileName)}`)
}

// Replaces the goal's entries; 400 with the reason the server refuses them.
export function putGoal(apiUrl: string, gameFileName: string, entries: { species: number; formCode: string | null; amount: number }[]): Promise<Response> {
  return request('PUT', `${apiUrl}/api/goal/${encodeURIComponent(gameFileName)}`, entries)
}

// Adds the preset's entries for species the goal has none for, saves, and returns the goal; 404 when the file's version has no milestones.
export function applyGoalPreset(apiUrl: string, game: string, gameFileName: string, preset: import('../types/api').GoalPreset): Promise<Response> {
  return request('POST', `${apiUrl}/api/goal/${encodeURIComponent(game)}/${encodeURIComponent(gameFileName)}/preset/${preset}`)
}

export function getSpeciesNames(apiUrl: string, game: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/species/${encodeURIComponent(game)}`)
}

// Always sends the flag: the server reads a missing one as false.
export function setRowHidden(apiUrl: string, gameFileName: string, row: import('../types/api').HiddenRow, hidden: boolean): Promise<Response> {
  return request('PUT', `${apiUrl}/api/goal/${encodeURIComponent(gameFileName)}/hidden?hidden=${hidden}`, row)
}

// A milestone you made, assigned to no file, is listed on every file.
export function setMilestoneAssigned(apiUrl: string, gameFileName: string, phase: string, assigned: boolean): Promise<Response> {
  return request('PUT', `${apiUrl}/api/goal/${encodeURIComponent(gameFileName)}/assigned?phase=${encodeURIComponent(phase)}&assigned=${assigned}`)
}

// 404 when the file's version has no such milestone; 400 with the reason when the phase is linked to another one.
export function setMilestoneLink(apiUrl: string, game: string, gameFileName: string, link: import('../types/api').MilestoneLink, linked: boolean): Promise<Response> {
  return request('PUT', `${apiUrl}/api/goal/${encodeURIComponent(game)}/${encodeURIComponent(gameFileName)}/link?linked=${linked}`, link)
}

export function getBackfillOffer(apiUrl: string, game: string, gameFileName: string, phase: string): Promise<Response> {
  return request('GET', `${apiUrl}/api/goal/${encodeURIComponent(game)}/${encodeURIComponent(gameFileName)}/backfill/${encodeURIComponent(phase)}`)
}

// Replaces the goal's entries for the chosen species with the phase's and returns the goal; 400 with the reason it is refused.
export function copyAmounts(apiUrl: string, game: string, gameFileName: string, phase: string, species: number[]): Promise<Response> {
  return request('POST', `${apiUrl}/api/goal/${encodeURIComponent(game)}/${encodeURIComponent(gameFileName)}/copy/${encodeURIComponent(phase)}`, species)
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
