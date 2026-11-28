import * as fs from 'fs'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import { API, backendLog, MINUTE, readTile } from './static'
import {
  ANCHOR_HUNT, assertLocalStores, cancelBot, cancelEventAndBot, cardWindow, CLEARING, clockConfig, HEAL_TRIP, HEALING, launchHgss, putMilestone,
  rowsFor, RUN_FROM, saveOf, seen, shutdown, stageCopy, startFrame, startWild, subscribe, teardown, WILD, writeLeadExtension, type Frame
} from './milestones'

// #525 Q1c, row hgss-milestone-anchor (FR-23, FR-38; B2a's battle on a walk and heal trip back to the anchor), on the saved
// milestone QA Route 30 at Route 30 · grass (#621 R8b1).
const HANDLE = 'HG-Anchor'
const ROUTE30 = '34-land'
const MILESTONE = 'QA Route 30'
// HG Route 30 by day (milestones.json.gz): Pidgey, Caterpie, Metapod.
const ROUTE30_DAY = ['16', '10', '11']
const GATEHOUSE = '97'
// The navigator's warp wait names every destination id, joined by "/"; the helper's WARP takes one.
const WARP_ANY = /\[Navigator\] Warp to map (\d+(?:\/\d+)*): on map (\d+), (.*)$/
const FOE = /(\S+), isShiny: (True|False)\s*$/
const STALL = /Movement blocked|abandoning the route|Sign seen/
const SEGMENT_PORTAL = /\[Navigator\] Segment \d+: .* portal=True/

const count = (pattern: RegExp, from = 0): number => backendLog().slice(from).filter((l) => pattern.test(l)).length
const linesBetween = (pattern: RegExp, from: number, to: number): string[] => backendLog().slice(from, to).filter((l) => pattern.test(l))

// Every warp wait ends on its destination with the field free (the walk crossed the door, both ways).
function checkWarps(lines: string[], what: string): void {
  const dest = (l: string): string => WARP_ANY.exec(l)![1]
  lines.forEach((line, i) => {
    if (i + 1 === lines.length || dest(lines[i + 1]) !== dest(line)) {
      const on = WARP_ANY.exec(line)![2]
      expect(dest(line).split('/'), `${what}: the warp wait to map ${dest(line)} ended on map ${on}`).toContain(on)
      expect(line, `${what}: the warp wait to map ${dest(line)} did not end with the field free`).toMatch(/Screen = FieldFree/)
    }
  })
}

test('HGSS milestone anchor: Route 30 · grass from Route 32 walks through the gatehouse, heals, returns to (547, 304) and runs up-down', async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    // Day, so the Route 30 species are fixed.
    clockConfig('2026-11-29T12:00:00')
    const admin = await startFrame(frame)
    expect((await putMilestone({ game: 'HGSS', name: MILESTONE, location: ROUTE30, gameFiles: [HANDLE], archived: false })).status, `${MILESTONE} saved`).toBe(200)
    expect((await subscribe({ game: 'HGSS', phase: MILESTONE, gameName: HANDLE })).status, 'the fixed card').toBeLessThan(300)
    const pages: Record<string, Page> = { admin, card: await cardWindow(frame.app!, `HGSS — ${MILESTONE} (${HANDLE})`) }
    let card: Locator = await launchHgss(admin, HANDLE, { clock: true })
    expect(await readTile(), 'not the save\'s start tile').toEqual({ map: 36, x: 474, y: 294, facing: 'up' })
    await shotAll('01-launched', pages)

    // 1. The hunt from the generated anchor, in train mode so the lead at 1 HP takes a heal trip once there.
    const runStart = await startWild(card, { phase: MILESTONE, target: 'ShinyTarget', mode: 'Run', yRange: 1, train: { type: 'Standard', targetLevel: 10 } })
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
    expect(hunt.slice(1, 7), 'the anchor line').toEqual(['Route 30 · grass', '34', '547', '304', 'run up-down ±6', 'generated'])

    // 2. The walk to the anchor through Violet City and the gatehouse; arrival.
    const arrival = await seen(CLEARING, 1, 15 * MINUTE)
    expect(arrival.line, 'the walk to the anchor').toMatch(/arrived back at start/)
    const arrivalAt = backendLog().findIndex((l) => CLEARING.test(l))
    await shotAll('02-at-anchor', pages)

    // 3. The heal trip from the anchor and the walk back to it.
    const heal = HEAL_TRIP.exec((await seen(HEAL_TRIP, 1, 2 * MINUTE)).line)!
    expect([heal[2], heal[3]], `the heal trip's lead HP (${heal[1]})`).toEqual(['1', '30'])
    await seen(HEALING, 1, 10 * MINUTE)
    const back = await seen(CLEARING, 2, 15 * MINUTE)
    expect(back.line, 'the walk back to the anchor').toMatch(/arrived back at start/)
    // Cancelled at once through the REST cancel, the quickest, before a training battle (Metapod evolves at 10).
    expect((await fetch(`${API}/bot/cancelAction`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '0' })).status).toBe(200)
    await expect(card.getByRole('button', { name: WILD, exact: true })).toBeVisible({ timeout: MINUTE })
    step('event cancelled after the walk back')
    const backAt = backendLog().findIndex((l, i) => i > arrivalAt && CLEARING.test(l))
    const tile = await readTile()
    step(`tile after the cancel: ${JSON.stringify(tile)}`)
    if (tile.x === 0 && tile.y === 0) throw new Error('a training battle started before the cancel landed (the position reads (0, 0) in a battle): a rerun, not a bot failure')
    // The run line from (547, 304) may have started before the cancel; the player is on it.
    expect([tile.map, tile.x], 'not on Route 30\'s run line after the walk back').toEqual([34, 547])
    expect(Math.abs(tile.y - 304), 'not on Route 30\'s run line after the walk back').toBeLessThanOrEqual(6)
    await shotAll('03-healed-and-back', pages)

    // 4. The walks: every warp ends on its destination, the gatehouse door crossed going, the doors crossed on the heal trip;
    // the battles on the walks went on with no park and no stall.
    const log = backendLog()
    const healAt = log.findIndex((l) => HEAL_TRIP.test(l))
    const toAnchor = linesBetween(WARP_ANY, 0, arrivalAt)
    const healTrip = linesBetween(WARP_ANY, healAt, backAt)
    checkWarps(toAnchor, 'the walk to the anchor')
    checkWarps(healTrip, 'the heal trip')
    expect(toAnchor.some((l) => WARP_ANY.exec(l)![1].split('/').includes(GATEHOUSE)), 'the walk to the anchor crossed no gatehouse door').toBe(true)
    expect(healTrip.length, 'the heal trip crossed no door').toBeGreaterThan(0)
    for (const l of linesBetween(SEGMENT_PORTAL, 0, backAt)) step(`portal segment: ${l.trim()}`)
    expect(linesBetween(STALL, 0, backAt), 'a stall or a sign on the walks').toEqual([])
    const walkBattles = [...linesBetween(FOE, 0, arrivalAt).map((l) => `to the anchor: ${l.trim()}`), ...linesBetween(FOE, healAt, backAt).map((l) => `heal trip: ${l.trim()}`)]
    step(walkBattles.length === 0 ? 'no battle on the walks: "a battle during the walk" stays in the manual pass for this run' : `battles on the walks: ${walkBattles.join(' ; ')}`)

    // 5. A second Start with no train mode: the up-down Run from the anchor and three encounters there. The cancel may have
    // landed in a battle (Cancel flees nothing), so the bot is closed and launched again on the same save: no scenario saves
    // in-game, so the handle's SaveRAM is still the pristine copy, on the save's tile, and the Start walks to the anchor again.
    await cancelBot(card)
    card = await launchHgss(admin, HANDLE, { clock: true })
    expect(await readTile(), 'not the save\'s start tile on the second launch').toEqual({ map: 36, x: 474, y: 294, facing: 'up' })
    // The save's lead is at 1 HP (QA save files); RAM only, never saved, so the second hunt takes no heal trip.
    await writeLeadExtension({ hp: 30 })
    const runsBefore = count(RUN_FROM)
    const clearsBefore = count(CLEARING)
    await startWild(card, { phase: MILESTONE, target: 'ShinyTarget', mode: 'Run', yRange: 1 })
    const again = await seen(CLEARING, clearsBefore + 1, 15 * MINUTE)
    expect(again.line, 'the second walk to the anchor').toMatch(/arrived back at start/)
    // The walk's battles are filed under the milestone too, so only the encounters from the arrival on are Route 30's.
    const foesFrom = backendLog().flatMap((l, i) => (CLEARING.test(l) ? [i] : []))[clearsBefore]
    const run = RUN_FROM.exec((await seen(RUN_FROM, runsBefore + 1, 5 * MINUTE)).line)!
    expect([run[1], run[2]], 'the Run\'s tile').toEqual(['547', '304'])
    expect(run[3], 'the Run macro').toMatch(/^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/)
    await expect.poll(() => count(FOE, foesFrom), { timeout: 10 * MINUTE, intervals: [2000] }).toBeGreaterThanOrEqual(3)
    await shotAll('04-three-encounters', pages)
    await cancelEventAndBot(card)

    // 6. Nothing was saved in game; the documented shutdown writes the encounters.
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    await shutdown(frame, admin)
    const rows = rowsFor(HANDLE, MILESTONE, new Date(again.at).toISOString())
    expect(rows.length, `fewer than 3 rows under ${MILESTONE} since the second arrival`).toBeGreaterThanOrEqual(3)
    for (const r of rows) expect(ROUTE30_DAY, `species ${r.species} is not in HG Route 30's day table`).toContain(r.species)
    step(`first Start at ${runStart}, ${rowsFor(HANDLE, MILESTONE, runStart).length} rows under ${MILESTONE} in all`)
  } finally {
    await teardown(frame)
  }
})
