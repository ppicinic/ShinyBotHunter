import * as fs from 'fs'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { emuHawkRunning, shotAll, step, writeU8 } from './driver'
import { decodeMon } from './egg-pid'
import {
  ACTION, API, backendLines, cancelBot, countAddr, DIALGA, installPristine, launchBot, MAP_SENTENCE, PARTY_SENTENCE, PHASE,
  pristineSha, readPartyCount, readSlot, readTile, ROM, ROUTE33_ROM, savedSha, saveOf, sha256, shutdown, SID, slotAddr,
  START_TILE, startFrame, teardown, TID, type Frame
} from './static'

test('HGSS Static Encounter: a second Pokémon and a player off the stage are each refused at Start, nothing saved', async () => {
  const frame: Frame = { backend: null, app: null }
  // Before the try, so the finally block never closes an emulator or restores over a save this run did not touch.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  const route33 = saveOf(ROUTE33_ROM)
  expect(fs.readFileSync(route33).equals(fs.readFileSync(route33 + '.bak')), `${route33} differs from its .bak`).toBe(true)
  installPristine()
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }

    // A Start through the API is a 400 with exactly the sentence; no event starts and the hunt logs nothing.
    const refused = async (card: Locator, name: string, sentence: string): Promise<void> => {
      const res = await fetch(`${API}/bot/startAction/0`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ EventName: ACTION, PhaseName: PHASE, StaticEncounter: DIALGA })
      })
      const body = await res.text()
      step(`${name}: startAction ${res.status} "${body}"`)
      expect(res.status, `${name}: the Start was not refused`).toBe(400)
      expect(body, `${name}: not the sentence`).toBe(sentence)
      const state = await (await fetch(`${API}/bot/getDashboardState`)).json()
      step(`${name}: dashboard state ${JSON.stringify(state)}`)
      expect(state['0']?.eventState, `${name}: an event started`).toBe('None')
      await expect(card.getByRole('button', { name: ACTION, exact: true })).toBeVisible()
      expect(backendLines(/\[Static\]/), `${name}: a [Static] line`).toEqual([])
      await shotAll(name, pages)
    }

    // 1. The party refusal (FR-2): the staged save on the stage, with a second Arceus written into party RAM.
    let card = await launchBot(admin, ROM, 'refusal-party')
    expect(await readTile(), 'not the save\'s start tile').toEqual(START_TILE)
    expect(await readPartyCount(), 'the save\'s party is not one').toBe(1)
    const slot1 = await readSlot(0)
    const arceus = decodeMon(slot1)
    expect([arceus.species, arceus.checksumValid, arceus.tid, arceus.sid], 'slot 1 is not the save\'s Arceus').toEqual([493, true, TID, SID])
    await shotAll('01-launched', pages)

    // Slot 1 copied into slot 2 and the count to 2: only the differing bytes, all at once, as ApiHawk drains its queue in one frame.
    const slot2 = await readSlot(1)
    const slot2Addr = await slotAddr(1)
    const differing = [...slot1.keys()].filter((i) => slot1[i] !== slot2[i])
    await Promise.all([...differing.map((i) => writeU8(slot2Addr + i, slot1[i])), writeU8(await countAddr(), 2)])
    expect(await readPartyCount(), 'the count does not read 2').toBe(2)
    const second = decodeMon(await readSlot(1))
    step(`second Pokémon written: ${differing.length} bytes; slot 2 species ${second.species}, checksum ${second.checksumValid}`)
    expect([second.species, second.checksumValid], 'slot 2 is not an Arceus').toEqual([493, true])
    await refused(card, '02-party', PARTY_SENTENCE)
    await cancelBot(card)
    expect(savedSha(), `${ROM} SaveRAM is not the pristine copy`).toBe(pristineSha())
    step(`${ROM} SaveRAM equals the pristine copy`)

    // 2. The map refusal (FR-2): hgss.qa-route33 is not on the stage.
    card = await launchBot(admin, ROUTE33_ROM, 'refusal-map')
    await refused(card, '03-map', MAP_SENTENCE)
    await cancelBot(card)
    expect(fs.readFileSync(route33).equals(fs.readFileSync(route33 + '.bak')), `${route33} differs from its .bak`).toBe(true)
    step(`${ROUTE33_ROM} SaveRAM equals .bak (sha256 ${sha256(route33)})`)

    await shutdown(frame, admin)
  } finally {
    await teardown(frame)
  }
})
