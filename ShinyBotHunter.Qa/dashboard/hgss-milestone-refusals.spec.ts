import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { runDir, runMacro, shotAll, step } from './driver'
import { X_ADDR, Y_ADDR } from './headbutt'
import { backendLog, MINUTE, readTile } from './static'
import {
  applyPreset, assertLocalStores, cancelBot, launchHgss, milestoneMessages, NO_PATH, PLAN_WALK, picker, putGoal, putPlan, rows, seen, shutdown,
  stageCopy, startAction, startFrame, startWild, teardown, WILD, writeRepel, type Frame
} from './milestones'

// #525 Q1c, rows hgss-plan-refused and hgss-milestone-bad-anchor: FR-15, FR-15a, FR-17, FR-18, FR-22, FR-24, FR-35, the HGSS
// no-path case of B2a, the A3a and A3b picker items, the all-complete ending, and the headbutt picker read.
const HANDLE = 'HG-Refuse'
const TITLE = 'SS-Title'
const ROUTE32 = '36-land'
const SURF = '36-surf'
const HEADBUTT = '36-headbutt'
const ROUTE47 = '151-land'
const ROUTE30 = '34-land'
const AZALEA = '74-headbutt'
const NO_PATH_BACK = /\[Navigator\] No path back to start from /
const GENERATED_ID = /^\d+-[a-z]+$/

type Entry = { id: string; name: string; modality: string | null; complete: boolean; archived: boolean; order: string | null; anchor: { map: number; x: number; y: number } | null; anchorSet: boolean }
const entries = async (): Promise<Entry[]> => (await picker(0)).json.milestones
const entry = async (id: string): Promise<Entry> => (await entries()).find((e) => e.id === id)!
const state = async (): Promise<string> => (await (await fetch('http://localhost:5000/api/bot/getDashboardState')).json())['0']?.eventState
const idleStates = ['None', 'NotStarted', 'Complete']

async function openAction(card: Locator, name: string): Promise<void> {
  await card.page().getByRole('button', { name: 'Bot Control', exact: true }).click()
  await card.getByRole('button', { name, exact: true }).click()
}
const milestoneSelect = (card: Locator): Locator => card.getByTestId('param-PhaseName').first().locator('select')
const optionValues = async (select: Locator): Promise<string[]> =>
  (await select.locator('option').evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value))).filter((v) => v !== '')
const optionTexts = async (select: Locator): Promise<string[]> =>
  (await select.locator('option').evaluateAll((os) => os.map((o) => [(o as HTMLOptionElement).value, o.textContent ?? '']))).filter(([v]) => v !== '').map(([, t]) => t)

// A Start refused with exactly `sentence`, and no event started.
async function refused(args: Record<string, unknown>, sentence: string): Promise<void> {
  const res = await startAction(0, { EventName: WILD, TargetType: 'PhaseTarget', ...args })
  expect([res.status, res.text], `the Start ${JSON.stringify(args)}`).toEqual([400, sentence])
  expect(idleStates, 'an event started after a refusal').toContain(await state())
}

// The backend's log and the Admin video of the first session, before the restart overwrites them.
function keepFirstSession(): void {
  fs.copyFileSync(path.join(runDir, 'backend.log'), path.join(runDir, 'backend-1.log'))
  fs.renameSync(path.join(runDir, 'admin.webm'), path.join(runDir, 'admin-1.webm'))
}

test('HGSS milestone refusals: the picker, no hunt yet, the wrong type, a bad anchor, an unreachable plan, no path, all complete', async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    stageCopy(TITLE, { rom: 'Pokemon SoulSilver.nds', save: null })
    let admin: Page = await startFrame(frame)
    expect((await applyPreset('HGSS', HANDLE, 'LivingDex')).status, 'the Living dex preset').toBeLessThan(300)
    let card = await launchHgss(admin, HANDLE)

    // (a) The picker's filters on the Wild Encounter form.
    await openAction(card, WILD)
    const select = milestoneSelect(card)
    const form = card.getByTestId('param-PhaseName').first().locator('xpath=../..')
    const all = await optionValues(select)
    step(`${all.length} milestones offered`)
    await form.getByPlaceholder('Filter').fill('Route 32')
    for (const text of await optionTexts(select)) expect(text.toLowerCase(), 'the text filter').toContain('route 32')
    await form.getByPlaceholder('Filter').fill('')
    const modality = card.getByTestId('param-PhaseName-filter')
    await modality.selectOption('Surf')
    for (const id of await optionValues(select)) expect(id, 'the modality filter').toMatch(/-surf$/)
    await modality.selectOption('Custom')
    for (const id of await optionValues(select)) expect(id, 'the Custom filter').not.toMatch(GENERATED_ID)
    await modality.selectOption('')
    await form.getByText('Incomplete only').click()
    const complete = new Set((await entries()).filter((e) => e.complete).map((e) => e.id))
    for (const id of await optionValues(select)) expect(complete.has(id), `${id} is complete but offered with Incomplete only`).toBe(false)
    await form.getByText('Incomplete only').click()
    // Archive Route 32 · surf and label Route 32 · grass 1-8.
    await select.selectOption(SURF)
    await card.getByRole('button', { name: 'Archive', exact: true }).click()
    await expect.poll(async () => (await entry(SURF)).archived, { timeout: 10_000 }).toBe(true)
    await select.selectOption(ROUTE32)
    await card.getByPlaceholder('Order').fill('1-8')
    await card.getByPlaceholder('Order').press('Tab')
    await expect.poll(async () => (await entry(ROUTE32)).order, { timeout: 10_000 }).toBe('1-8')
    await expect(select.locator(`option[value="${SURF}"]`), 'an archived milestone offered').toHaveCount(0, { timeout: 30_000 })
    await form.getByText('Show archived').click()
    await expect(select.locator(`option[value="${SURF}"]`), 'Show archived').toHaveText(/\(archived\)$/, { timeout: 30_000 })
    await form.getByText('Show archived').click()
    await expect(select.locator(`option[value="${ROUTE32}"]`)).toHaveText(/^1-8 Route 32 · grass/)
    await shotAll('01-picker', { admin })

    // The Togepi, Primo and Static forms list custom milestones only; a Chained Encounter leg lists both kinds.
    for (const action of ['HGSS Togepi Egg', 'HGSS Primo Egg', 'HGSS Static Encounter']) {
      await openAction(card, action)
      const ids = await optionValues(milestoneSelect(card))
      step(`${action} offers ${ids.length}: ${ids.join(', ')}`)
      for (const id of ids) expect(id, `${action} offers a generated milestone`).not.toMatch(GENERATED_ID)
    }
    await openAction(card, 'Chained Encounter')
    await card.getByRole('button', { name: '+ Leg', exact: true }).click()
    const legIds = await optionValues(milestoneSelect(card))
    expect(legIds.some((id) => GENERATED_ID.test(id)) && legIds.some((id) => !GENERATED_ID.test(id)), 'a Chained Encounter leg does not list both kinds').toBe(true)

    // A dashboard reload and a backend restart keep both (Electron and the backend started again).
    await cancelBot(card)
    await shutdown(frame, admin)
    keepFirstSession()
    admin = await startFrame(frame)
    card = await launchHgss(admin, HANDLE)
    expect([(await entry(SURF)).archived, (await entry(ROUTE32)).order], 'after the restart').toEqual([true, '1-8'])
    await openAction(card, WILD)
    await milestoneSelect(card).selectOption(ROUTE32)
    await expect(milestoneSelect(card).locator(`option[value="${ROUTE32}"]`)).toHaveText(/^1-8 Route 32 · grass/)
    // Emptying the Order box clears the label.
    await card.getByPlaceholder('Order').fill('')
    await card.getByPlaceholder('Order').press('Tab')
    await expect.poll(async () => (await entry(ROUTE32)).order, { timeout: 10_000 }).toBeNull()
    // Route 32 · surf unarchived again, so (c) can pick it.
    await card.getByTestId('param-PhaseName').first().locator('xpath=../..').getByText('Show archived').click()
    await milestoneSelect(card).selectOption(SURF)
    await card.getByRole('button', { name: 'Unarchive', exact: true }).click()
    await expect.poll(async () => (await entry(SURF)).archived, { timeout: 10_000 }).toBe(false)

    // (b) The headbutt stand Azalea Town gets from this file's 32-bit trainer ID (2318286077 % 10 = 7: tree 3, the cheapest
    // of the trees whose kind holds a wanted species; it faces Up, which the picker does not carry).
    const azalea = await entry(AZALEA)
    expect(azalea.anchor, 'Azalea Town · headbutt\'s stand').toMatchObject({ map: 74, x: 407, y: 456 })

    // (c) FR-15a and the type rule.
    await expect(startWild(card, { phase: SURF, mode: 'Run', yRange: 1 })).rejects.toThrow('Route 32 · surf: no hunt yet.')
    expect(idleStates, 'an event started after no hunt yet').toContain(await state())
    await refused({ PhaseName: ROUTE32, EncounterType: 'Headbutt' }, 'Route 32 · grass is not hunted with Headbutt.')
    await refused({ PhaseName: HEADBUTT, EncounterType: 'Run', yRange: 1 }, 'Route 32 · headbutt is not hunted with Run.')

    // (d) FR-22 and FR-24: Repel so these steps start no battle; up two tiles of grass, left three onto bare ground.
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
    const returning = backendLog().filter((l) => /\[Navigator\] Returning to start/.test(l)).length
    await expect(startWild(card, { phase: ROUTE32, mode: 'Run', yRange: 1 })).rejects.toThrow('Route 32 · grass: the anchor (471, 292) is not grass.')
    expect(idleStates, 'an event started after the bad anchor').toContain(await state())
    expect(backendLog().filter((l) => /\[Navigator\] Returning to start/.test(l)).length, 'a walk after the bad anchor').toBe(returning)
    await anchorLine.getByRole('button', { name: 'Clear', exact: true }).click()
    await expect(anchorLine).toContainText('Anchor: map 36 (458, 339), run up-down ±7, generated', { timeout: 10_000 })
    await expect(anchorLine.getByRole('button', { name: 'Clear', exact: true })).toHaveCount(0)
    // A Set with the start menu open is refused with the field sentence.
    await runMacro('H.X.3;W.60')
    await anchorLine.getByRole('button', { name: 'Set anchor here', exact: true }).click()
    await expect(anchorLine.locator('.text-red-400')).toHaveText('Close any menu or message and stand in the field.', { timeout: 10_000 })
    await runMacro('H.B.3;W.60')
    expect((await entry(ROUTE32)).anchorSet, 'the Set with the menu open stored an anchor').toBe(false)
    await writeRepel(0)
    await shotAll('02-anchor-refusals', { admin })

    // (e) FR-35: a plan whose rule names Route 47, reached only by Surf from Cianwood.
    expect((await putPlan('HGSS', { name: 'QA Refused', default: ROUTE32, rules: [{ times: ['Night'], weekdays: [], milestone: ROUTE47 }] })).status).toBeLessThan(300)
    await refused({ HuntPlan: 'QA Refused', EncounterType: 'Run', yRange: 1 },
      'QA Refused: Route 47 · grass\'s anchor (131, 367) cannot be reached from here without an HM.')

    // (f) B2a: with no plan the hunt walks, the navigator finds no path, and the hunt parks (never Complete).
    await startWild(card, { phase: ROUTE47, mode: 'Run', yRange: 1 })
    await seen(NO_PATH_BACK, 1, MINUTE, { waiting: true })
    const noPath = NO_PATH.exec((await seen(NO_PATH, 1, MINUTE, { waiting: true })).line)!
    expect(noPath.slice(1, 4), 'the no-path line').toEqual(['Route 47 · grass', '131', '367'])
    await expect.poll(state, { timeout: 30_000 }).toBe('Waiting')
    await expect(card.getByText('Resume', { exact: true })).toBeVisible({ timeout: 30_000 })
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card.getByRole('button', { name: WILD, exact: true })).toBeVisible({ timeout: MINUTE })

    // (g) FR-34, all complete: an empty goal and a plan; the hunt ends at its first decision with no walk and no message.
    expect((await putGoal(HANDLE, [])).status, 'the empty goal').toBeLessThan(300)
    expect((await putPlan('HGSS', { name: 'QA Done', default: ROUTE32, rules: [{ times: ['Night'], weekdays: [], milestone: ROUTE30 }] })).status).toBeLessThan(300)
    const doneStart = new Date().toISOString()
    const walks = backendLog().filter((l) => PLAN_WALK.test(l)).length
    const res = await startAction(0, { EventName: WILD, TargetType: 'PhaseTarget', HuntPlan: 'QA Done', EncounterType: 'Run', yRange: 1 })
    expect(res.status, `the Start of QA Done: ${res.text}`).toBe(200)
    await expect.poll(state, { timeout: 2 * MINUTE, intervals: [1000] }).toBe('Complete')
    expect(backendLog().filter((l) => PLAN_WALK.test(l)).length, 'a plan walk with every stop complete').toBe(walks)
    expect(await milestoneMessages(doneStart), 'a milestone message for milestones complete at Start').toEqual([])
    await shotAll('03-all-complete', { admin })
    await cancelBot(card)

    // (h) SoulSilver at the title screen: the picker lists its generated milestones (the version's own slots), and no
    // headbutt stand, since the trainer ID does not read there.
    card = await launchHgss(admin, TITLE, { continue: false })
    const title = (await picker(0)).json
    expect(title.gameFileName, 'the picker\'s file').toBe(TITLE)
    const generated = (title.milestones as Entry[]).filter((e) => e.modality !== null)
    step(`${generated.length} generated milestones on ${TITLE}`)
    expect(generated.length, 'no generated milestone at the title screen').toBeGreaterThan(0)
    expect(generated.find((e) => e.id === AZALEA)?.anchor, 'a headbutt stand with no trainer ID').toBeNull()
    // SoulSilver's Route 30: Ledyba (165) and Weedle (13) in the morning, no Spinarak (167), unlike HeartGold's.
    const species = ((await rows('HGSS', ROUTE30, TITLE)).json.rows as { species: number }[]).map((r) => r.species)
    expect([species.includes(165), species.includes(13), species.includes(167)], 'Route 30 is not SoulSilver\'s').toEqual([true, true, false])
    await shotAll('04-soulsilver-title', { admin })
    await cancelBot(card)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
