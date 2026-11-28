// hgss-anchor-repel-walk (docs/specs/hunt-plans-and-milestone-builder.md, QA; #525 Q1b, plan scenario 10; #621 R8b1): C5
// (#562) and its expected result from the 2026-11-25 addition, on a plain Wild Encounter, since a plan step walks with the
// same Init anchor walk (R6b). The saved milestone QA Route 30 at Route 30 · grass, hunted from the save's tile on Route 32,
// (474, 294), at noon, with the Repel counter written to 8 steps (QA_REPEL_STEPS overrides it) with the bot idle: "Repel's
// effect wore off..." comes up during the walk to the generated anchor, is closed with one A, and the walk carries on to
// Route 30's anchor (547, 304) with no park.
// Run: `npx playwright test hgss-anchor-repel-walk`. A second run with QA_REPEL_STEPS set to segment 1's logged step count
// tries the box on a segment's last tile; it is reported, not required.
import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step } from './driver'
import {
  ANCHOR_HUNT, assertLocalStores, cancelEventAndBot, cardWindow, clockConfig, launchHgss, NAV_REPEL, NO_PATH, putMilestone, REPEL_CLOSED,
  REPEL_NOT_CLOSED, RUN_FROM, saveOf, seen, shutdown, stageCopy, startFrame, startWild, subscribe, teardown, writeLeadExtension, writeRepel,
  type Frame
} from './milestones'
import { backendLog, MINUTE } from './static'

const HANDLE = 'HG-Walk'
const MILESTONE = 'QA Route 30'
const STEPS = Number(process.env.QA_REPEL_STEPS ?? 8)
// NavigatorEvent: each segment as it starts, a warp wait, and FinishReturn after the walk.
const SEGMENT = /\[Navigator\] Segment (\d+): map=(\d+) \((\d+),(\d+)\) -> \((\d+),(\d+)\)/
const WARP_ANY = /\[Navigator\] Warp to map (\d+(?:\/\d+)*): on map (\d+), (.*)$/
const ARRIVED = /\[Navigator\] Clearing navigation path \(arrived back at start\)/
const BATTLE = /, isShiny: (True|False)\s*$/
const R30_MACRO = /^M\.Up\.298\.\d+(:\S+)?;M\.Down\.310\.\d+$/

test('hgss-anchor-repel-walk: the Repel message on the walk to the anchor closed with one A, the walk carries on to the anchor', async () => {
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-29T12:00:00')
    const admin = await startFrame(frame)
    expect((await putMilestone({ game: 'HGSS', name: MILESTONE, location: '34-land', gameFiles: [HANDLE], archived: false })).status, `${MILESTONE} saved`).toBe(200)
    expect((await subscribe({ game: 'HGSS', phase: MILESTONE, gameName: HANDLE })).status, 'the fixed card').toBeLessThan(300)
    const pages: Record<string, Page> = { admin, card: await cardWindow(frame.app!, `HGSS — ${MILESTONE} (${HANDLE})`) }

    const card = await launchHgss(admin, HANDLE, { clock: true })
    await writeLeadExtension({ hp: 30 })
    await writeRepel(STEPS)
    await shotAll('01-launched', pages)

    await startWild(card, { phase: MILESTONE, mode: 'Run', yRange: 1 })
    // The Start walks from the save's tile to the generated anchor.
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
    expect(hunt.slice(1, 7), 'the anchor line').toEqual(['Route 30 · grass', '34', '547', '304', 'run up-down ±6', 'generated'])

    // The box, seen by the navigator at a segment's end or from the stall path, then the one close.
    await seen(NAV_REPEL, 1, 5 * MINUTE)
    await seen(REPEL_CLOSED, 1, MINUTE)
    const arrived = await seen(ARRIVED, 1, 10 * MINUTE)
    const from = RUN_FROM.exec((await seen(RUN_FROM, 1, 2 * MINUTE)).line)!
    await shotAll('02-arrived', pages)

    const log = backendLog()
    expect(log.filter((l) => NAV_REPEL.test(l)).length, '"the Repel message seen" once').toBe(1)
    const closes = log.filter((l) => REPEL_CLOSED.test(l))
    expect(closes.map((l) => REPEL_CLOSED.exec(l)![1]), '"exactly one close, with one A"').toEqual(['1'])
    expect(log.filter((l) => REPEL_NOT_CLOSED.test(l) || NO_PATH.test(l)), 'a failed close or no path to the anchor').toEqual([])
    const seenAt = log.findIndex((l) => NAV_REPEL.test(l))
    const closedAt = log.findIndex((l) => REPEL_CLOSED.test(l))
    const arrivedAt = log.findIndex((l) => ARRIVED.test(l))
    expect(closedAt, 'the close after the box was seen').toBeGreaterThan(seenAt)
    // "The walk goes on": a later segment or warp line between the close and the arrival.
    expect(log.slice(closedAt, arrivedAt).some((l) => SEGMENT.test(l) || WARP_ANY.test(l)), '"the walk goes on" after the close').toBe(true)
    expect([from[1], from[2]], 'the first Run after the walk starts on the anchor').toEqual(['547', '304'])
    expect(from[3], 'Route 30\'s run line').toMatch(R30_MACRO)

    // Where the box came up, for steps.log: the segment it was on, the segment after it, and any battle right after the close.
    const segmentBefore = log.slice(0, seenAt).filter((l) => SEGMENT.test(l)).at(-1)
    const segmentAfter = log.slice(closedAt).find((l) => SEGMENT.test(l))
    step(`the box came up on: ${segmentBefore?.trim() ?? 'no segment line before it'}`)
    step(`the next segment: ${segmentAfter?.trim() ?? 'none (the box came up at arrival)'}`)
    step(`the line that saw it: ${log[seenAt].trim()}`)
    const battleAfterClose = log.slice(closedAt, arrivedAt).find((l) => BATTLE.test(l))
    step(`a battle between the close and the arrival: ${battleAfterClose?.trim() ?? 'none'}`)
    step(`arrival seen at ${new Date(arrived.at).toISOString()}`)

    await cancelEventAndBot(card)
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'the SaveRAM differs from its .bak').toBe(true)
    step('SaveRAM equals .bak')
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
