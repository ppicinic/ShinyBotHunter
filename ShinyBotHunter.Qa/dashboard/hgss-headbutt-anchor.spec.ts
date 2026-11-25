import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { shotAll, step, windowByTitle } from './driver'
import { parseFr11 } from './headbutt'
import { API, backendLog, MINUTE, readTile } from './static'
import {
  ANCHOR_HUNT, applyPreset, assertLocalStores, cancelEventAndBot, CLEARING, launchHgss, rows, rowsFor, saveOf, seen, shutdown, stageCopy,
  startFrame, startWild, subscribe, teardown, teachMove, TRAINER_ID, writeLeadExtension, type Frame
} from './milestones'

// #525 Q1c: FR-21 in a hunt (B1h-b checks 1, 3 and 6; B1h-c) and a headbutt card with no share.
const HANDLE = 'HG-Headbutt2'
const HEADBUTT = '36-headbutt'
const CARD = `HGSS — ${HEADBUTT} (${HANDLE})`
// The full trainer ID of hgss.qa-route32 (TID 15613, SID 35374); 2318286077 % 10 = 7 picks Route 32's tree 23 at (471, 421),
// stood on from (470, 421) facing Right, the cheapest of the trees whose kind holds a wanted species (milestones.json.gz).
const TRAINER = ['2318286077', '15613']
const STAND = ['36', '470', '421']
// Route 32's headbutt species: Hoothoot, Pineco, Exeggcute.
const SPECIES = ['163', '204', '102']
const FR11 = /\[Headbutt\] start=script/
const HEADBUTT_SCRIPT = 10014
const MOVE_HEADBUTT = 29

const count = (pattern: RegExp, from = 0): number => backendLog().slice(from).filter((l) => pattern.test(l)).length

test('HGSS headbutt anchor: the trainer ID picks Route 32\'s stand, the walk there with no Rock Smash, three headbutt battles', async () => {
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    const admin = await startFrame(frame)
    expect((await applyPreset('HGSS', HANDLE, 'LivingDex')).status, 'the Living dex preset').toBeLessThan(300)
    expect((await subscribe({ game: 'HGSS', phase: HEADBUTT, gameName: HANDLE })).status, 'the fixed card').toBeLessThan(300)
    const cardWindow: Page = await windowByTitle(frame.app!, CARD, MINUTE)
    const pages = { admin, card: cardWindow }
    const card = await launchHgss(admin, HANDLE)
    expect(await readTile(), 'not the save\'s start tile').toEqual({ map: 36, x: 474, y: 294, facing: 'up' })

    // 1. RAM-only set-up with the bot idle: full HP, and Headbutt in the lead Metapod's empty fourth move slot (no QA save on
    // this PC has a Headbutt user). The cached party read is dropped and the page reloaded, so the form offers Headbutt.
    await writeLeadExtension({ hp: 30 })
    await teachMove(1, 3, MOVE_HEADBUTT, 15)
    expect((await fetch(`${API}/bot/getParty/0`)).ok, 'the party reload').toBe(true)
    await admin.reload()
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()

    // 2. Start: the trainer ID line, then the hunt from the generated stand.
    const fr11Before = count(FR11)
    const runStart = await startWild(card, { phase: HEADBUTT, mode: 'Headbutt' })
    const trainer = TRAINER_ID.exec((await seen(TRAINER_ID, 1, MINUTE)).line)!
    expect([trainer[1], trainer[2]], 'the trainer ID line').toEqual(TRAINER)
    step(`trainer ID ${trainer[3]}`)
    const hunt = ANCHOR_HUNT.exec((await seen(ANCHOR_HUNT, 1, MINUTE)).line)!
    expect(hunt.slice(1, 7), 'the anchor line').toEqual(['Route 32 · headbutt', ...STAND, 'headbutt', 'generated'])

    // 3. The walk ends on the stand (Route 32 has no Rock Smash rock on the way); three cycles with a battle each. A fourth
    // FR-11 line means the third battle's foe was logged (a cycle's line comes before its battle), as hgss-headbutt waits.
    const arrival = await seen(CLEARING, 1, 15 * MINUTE)
    expect(arrival.line, 'the walk to the stand').toMatch(/arrived back at start/)
    await seen(FR11, fr11Before + 4, 8 * MINUTE)
    const cycles = backendLog().filter((l) => FR11.test(l)).slice(fr11Before, fr11Before + 3).map((l) => parseFr11(l)!)
    for (const [i, c] of cycles.entries()) {
      step(`cycle ${i + 1}: start=script ${c.script} presses=${c.presses} end=${c.end} return=${c.ret}`)
      expect(c.script, `cycle ${i + 1} ran another script`).toBe(HEADBUTT_SCRIPT)
      expect(c.end, `cycle ${i + 1}: the tree at (471, 421) gave no Pokémon`).toBe('battle')
    }
    // The first return is made on the stand itself: none when the walk ended facing the tree, otherwise a turn, no step.
    expect(cycles[0].ret, 'the first return took a step off the stand').toMatch(/^(none|(re)?built from \(470,421,\w+\))$/)
    await shotAll('01-three-cycles', pages)
    await cancelEventAndBot(card)

    // 4. The headbutt card: no share under a name, no shares footer; the endpoint gives no shares for a headbutt milestone.
    const json = (await rows('HGSS', HEADBUTT, HANDLE)).json
    expect([json.survive, json.rows.every((r: { share: number | null }) => r.share === null)], 'shares on a headbutt milestone').toEqual([null, true])
    await expect(cardWindow.getByTestId('row-share'), 'a share on the headbutt card').toHaveCount(0)
    await expect(cardWindow.getByText(/Shares: not seen|not live/), 'a shares footer on the headbutt card').toHaveCount(0)
    await shotAll('02-card', pages)

    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    await shutdown(frame, admin)
    // The walk's battles are filed under the milestone too, so only the rows from the arrival on are the tree's.
    step(`${rowsFor(HANDLE, HEADBUTT, runStart).length} rows under ${HEADBUTT} since the Start, the walk's included`)
    const found = rowsFor(HANDLE, HEADBUTT, new Date(arrival.at).toISOString())
    expect(found.length, 'fewer than 3 headbutt rows').toBeGreaterThanOrEqual(3)
    for (const r of found) expect(SPECIES, `species ${r.species} is not a Route 32 headbutt species`).toContain(r.species)
  } finally {
    await teardown(frame)
  }
})
