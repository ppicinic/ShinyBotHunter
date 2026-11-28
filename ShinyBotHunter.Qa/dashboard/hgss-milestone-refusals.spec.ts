import { expect, test, type Locator } from '@playwright/test'
import { runMacro, shotAll, step } from './driver'
import { X_ADDR, Y_ADDR } from './headbutt'
import { backendLog, MINUTE, readTile } from './static'
import {
  ANCHOR_HUNT, assertLocalStores, cancelBot, cardWindow, launchHgss, milestoneMessages, NO_PATH, picker, putMilestone, seen, setAnchorHere,
  shutdown, stageCopy, startAction, startFrame, startWild, subscribe, teardown, WILD, writeRepel, type Frame
} from './milestones'

// #525 Q1c and #621 R8b2, rows hgss-plan-refused and hgss-milestone-bad-anchor as reworked: saved milestones with a location
// by fixture; FR-15 (not ticked for the file), FR-15a, FR-17, FR-18, FR-22, FR-24, the HGSS no-path case of B2a, the
// launch forms' milestone lists, the all-complete ending, and the headbutt picker read.
const HANDLE = 'HG-Refuse'
// A file name no scenario stages: the unticked fixture is ticked for it only.
const ELSEWHERE = 'HG-Elsewhere'
const ROUTE32 = 'QA Route 32'
const SURF = 'QA Surf'
const HEADBUTT = 'QA R32 Headbutt'
const ROUTE47 = 'QA Route 47'
const AZALEA = 'QA Azalea'
const DONE = 'QA Done'
const UNTICKED = 'QA Unticked'
// The save's tile (qa-automation.md, QA save files: hgss.qa-route32), on Route 32's grass.
const SAVE_TILE = [36, 474, 294]
const NO_PATH_BACK = /\[Navigator\] No path back to start from /
const RETURNING = /\[Navigator\] Returning to start/

type Entry = { name: string; modality: string | null; huntable: boolean; complete: boolean; anchor: { map: number; x: number; y: number } | null; anchorSet: boolean }
const entries = async (): Promise<Entry[]> => (await picker(0)).json.milestones
const entry = async (name: string): Promise<Entry> => (await entries()).find((e) => e.name === name)!
const state = async (): Promise<string> => (await (await fetch('http://localhost:5000/api/bot/getDashboardState')).json())['0']?.eventState
const idleStates = ['None', 'NotStarted', 'Complete']
const count = (pattern: RegExp): number => backendLog().filter((l) => pattern.test(l)).length

async function openAction(card: Locator, name: string): Promise<void> {
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name, exact: true }).click()
}
const milestoneSelect = (card: Locator): Locator => card.getByTestId('param-PhaseName').first().locator('select')
const optionValues = async (select: Locator): Promise<string[]> =>
  (await select.locator('option').evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value))).filter((v) => v !== '')

// A Start refused with exactly `sentence`, and no event started.
async function refused(args: Record<string, unknown>, sentence: string): Promise<void> {
  const res = await startAction(0, { EventName: WILD, TargetType: 'PhaseTarget', ...args })
  expect([res.status, res.text], `the Start ${JSON.stringify(args)}`).toEqual([400, sentence])
  expect(idleStates, 'an event started after a refusal').toContain(await state())
}

test('HGSS milestone refusals: not ticked, no hunt yet, the wrong type, a bad anchor, no path, all complete, the forms\' lists', async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    const admin = await startFrame(frame)
    const pages = { admin }
    // The fixtures, Living dex at their locations, ticked for this file; QA Done at None (every amount 0); QA Unticked
    // ticked for another file only.
    for (const [name, location, gameFiles, preset] of [
      [ROUTE32, '36-land', [HANDLE], 'LivingDex'], [SURF, '36-surf', [HANDLE], 'LivingDex'], [HEADBUTT, '36-headbutt', [HANDLE], 'LivingDex'],
      [ROUTE47, '151-land', [HANDLE], 'LivingDex'], [AZALEA, '74-headbutt', [HANDLE], 'LivingDex'], [DONE, '36-land', [HANDLE], 'None'],
      [UNTICKED, '36-land', [ELSEWHERE], 'LivingDex']
    ] as const) {
      const res = await putMilestone({ game: 'HGSS', name, location, gameFiles: [...gameFiles], archived: false, preset })
      expect(res.status, `the fixture ${name}`).toBe(200)
    }
    const card = await launchHgss(admin, HANDLE)

    // (a) FR-15 at Start: a milestone ticked only for another file is not listed and is refused on this one.
    expect((await entries()).map((e) => e.name), `${UNTICKED} is listed on ${HANDLE}`).not.toContain(UNTICKED)
    await refused({ PhaseName: UNTICKED, EncounterType: 'Run', yRange: 1 }, `${UNTICKED} is not ticked for ${HANDLE}.`)

    // (b) The headbutt stand Azalea Town gets from this file's 32-bit trainer ID (2318286077 % 10 = 7: tree 3, the cheapest
    // tree of all, a common one; it faces Up, which the picker does not carry).
    expect((await entry(AZALEA)).anchor, 'Azalea Town · headbutt\'s stand').toMatchObject({ map: 74, x: 407, y: 456 })

    // (c) The Togepi, Primo and Static forms list milestones with no location only; a Chained Encounter leg lists both kinds.
    const all = await entries()
    const noLocation = all.filter((e) => e.modality === null).map((e) => e.name)
    step(`${all.length} milestones on ${HANDLE}, ${noLocation.length} with no location: ${noLocation.join(', ')}`)
    expect(noLocation.length > 0 && noLocation.length < all.length, 'the picker has milestones of both kinds').toBe(true)
    for (const action of ['HGSS Togepi Egg', 'HGSS Primo Egg', 'HGSS Static Encounter']) {
      await openAction(card, action)
      await expect.poll(() => optionValues(milestoneSelect(card)), { message: `${action} offers only the milestones with no location`, timeout: 30_000 })
        .toEqual(noLocation)
    }
    await openAction(card, 'Chained Encounter')
    await card.getByRole('button', { name: '+ Leg', exact: true }).click()
    await expect.poll(() => optionValues(milestoneSelect(card)), { message: 'a Chained Encounter leg offers both kinds', timeout: 30_000 })
      .toEqual(all.map((e) => e.name))
    await shotAll('01-forms', pages)

    // (d) FR-15a and the type rule.
    await expect(startWild(card, { phase: SURF, mode: 'Run', yRange: 1 })).rejects.toThrow('Route 32 · surf: no hunt yet.')
    expect(idleStates, 'an event started after no hunt yet').toContain(await state())
    await refused({ PhaseName: ROUTE32, EncounterType: 'Headbutt' }, 'Route 32 · grass is not hunted with Headbutt.')
    await refused({ PhaseName: HEADBUTT, EncounterType: 'Run', yRange: 1 }, 'Route 32 · headbutt is not hunted with Run.')

    // (e) FR-22 and FR-24: Repel so these steps start no battle; up two tiles of grass, left three onto bare ground.
    await writeRepel(250)
    await runMacro(`M.Up.292.${Y_ADDR};M.Left.471.${X_ADDR}`)
    const bare = await readTile()
    expect([bare.map, bare.x, bare.y], 'the walk onto bare ground').toEqual([36, 471, 292])
    await openAction(card, WILD)
    await milestoneSelect(card).selectOption(ROUTE32)
    await card.getByTestId('param-EncounterType').locator('select').selectOption('Run')
    await card.getByTestId('param-yRange').locator('input').fill('1')
    const anchorLine = card.getByTestId('milestone-anchor')
    await expect(anchorLine).toContainText('Anchor: map 36 (458, 339), run up-down ±7, generated')
    await anchorLine.getByRole('button', { name: 'Set anchor here', exact: true }).click()
    await expect(anchorLine).toContainText('Anchor: map 36 (471, 292), run up-down ±1, set on this file', { timeout: 10_000 })
    const returning = count(RETURNING)
    await expect(startWild(card, { phase: ROUTE32, mode: 'Run', yRange: 1 })).rejects.toThrow('Route 32 · grass: the anchor (471, 292) is not grass.')
    expect(idleStates, 'an event started after the bad anchor').toContain(await state())
    expect(count(RETURNING), 'a walk after the bad anchor').toBe(returning)
    await anchorLine.getByRole('button', { name: 'Clear', exact: true }).click()
    await expect(anchorLine).toContainText('Anchor: map 36 (458, 339), run up-down ±7, generated', { timeout: 10_000 })
    await expect(anchorLine.getByRole('button', { name: 'Clear', exact: true })).toHaveCount(0)
    // A Set with the start menu open is refused with the field sentence.
    await runMacro('H.X.3;W.60')
    await anchorLine.getByRole('button', { name: 'Set anchor here', exact: true }).click()
    await expect(anchorLine.locator('.text-red-400')).toHaveText('Close any menu or message and stand in the field.', { timeout: 10_000 })
    await runMacro('H.B.3;W.60')
    expect((await entry(ROUTE32)).anchorSet, 'the Set with the menu open stored an anchor').toBe(false)
    // Back on the save's tile, which is grass, for QA Done's anchor: the walk south to its generated one meets a story trigger.
    await runMacro(`M.Right.474.${X_ADDR};M.Down.294.${Y_ADDR}`)
    const home = await readTile()
    expect([home.map, home.x, home.y], 'the walk back to the save\'s tile').toEqual(SAVE_TILE)
    const doneAnchor = await setAnchorHere(0, DONE, 0, 1)
    expect(doneAnchor.status, `Set anchor here for ${DONE}: ${doneAnchor.text}`).toBe(200)
    expect([doneAnchor.json.anchor.map, doneAnchor.json.anchor.x, doneAnchor.json.anchor.y], `${DONE}'s anchor on the save's tile`).toEqual(SAVE_TILE)
    await writeRepel(0)
    await shotAll('02-anchor-refusals', pages)

    // (f) B2a: the hunt walks to Route 47's generated anchor, reached only by Surf from Cianwood; the navigator finds no path,
    // and the hunt parks (never Complete). Its card is the one this run records.
    expect((await subscribe({ game: 'HGSS', phase: ROUTE47, gameName: HANDLE })).status, `subscribe ${ROUTE47} for ${HANDLE}`).toBe(200)
    const route47Card = await cardWindow(frame.app!, `HGSS — ${ROUTE47} (${HANDLE})`)
    await startWild(card, { phase: ROUTE47, mode: 'Run', yRange: 1 })
    await seen(NO_PATH_BACK, 1, MINUTE, { waiting: true })
    const noPath = NO_PATH.exec((await seen(NO_PATH, 1, MINUTE, { waiting: true })).line)!
    expect(noPath.slice(1, 4), 'the no-path line').toEqual([ROUTE47, '131', '367'])
    await expect.poll(state, { timeout: 30_000 }).toBe('Waiting')
    await expect(card.getByText('Resume', { exact: true })).toBeVisible({ timeout: 30_000 })
    await shotAll('03-no-path', { ...pages, card: route47Card })
    // No battle ran (the walk never started), so the player stands free on the save's tile after the cancel.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card.getByRole('button', { name: WILD, exact: true })).toBeVisible({ timeout: MINUTE })

    // (g) FR-34, all complete: every amount 0; the hunt from its anchor, where the bot stands, ends at its first decision with
    // no walk and no message.
    const doneStart = new Date().toISOString()
    const walks = count(RETURNING)
    const anchorLines = count(ANCHOR_HUNT)
    const res = await startAction(0, { EventName: WILD, TargetType: 'PhaseTarget', PhaseName: DONE, EncounterType: 'Run', yRange: 1 })
    expect(res.status, `the Start of ${DONE}: ${res.text}`).toBe(200)
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, anchorLines + 1, MINUTE)).line)!
    expect([...hunt.slice(2, 5).map(Number), hunt[6]], `${DONE}: hunting from its anchor`).toEqual([...SAVE_TILE, 'set on this file'])
    await expect.poll(state, { timeout: 2 * MINUTE, intervals: [1000] }).toBe('Complete')
    expect(count(RETURNING), 'a walk with the milestone complete').toBe(walks)
    expect(await milestoneMessages(doneStart), 'a milestone message for a milestone complete at Start').toEqual([])
    await shotAll('04-all-complete', pages)
    await cancelBot(card)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
