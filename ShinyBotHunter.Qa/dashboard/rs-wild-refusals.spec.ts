import { expect, test, type Locator, type Page } from '@playwright/test'
import { readMemory, runMacro, shotAll, step } from './driver'
import {
  ACTION, cancelBot, dashboardState, FAINTED_SENTENCE, FIELD_SENTENCE, flashUnchanged, HEADBUTT_SENTENCE, installPristine,
  launchRs, LEAD_HP, LOCK, offered, PHASE, pollWhileWaiting, readU8, removeCheats, teardown, writeBytes
} from './rs-wild'
import { API, backendLines, MINUTE, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'

test('RS Wild Encounter refusals: a fainted lead, Headbutt and a field message are each a 400, no event', async () => {
  test.setTimeout(15 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')
    await shotAll('01-launched', pages)

    // "each a 400 with its sentence, no event": exactly the sentence, no event state, Wild Encounter still offered, and
    // the hunt logged nothing.
    const refused = async (c: Locator, name: string, encounterType: string, sentence: string): Promise<void> => {
      const res = await fetch(`${API}/bot/startAction/0`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ EventName: ACTION, PhaseName: PHASE, TargetType: 'ShinyTarget', EncounterType: encounterType, TrainType: 'None' })
      })
      const body = await res.text()
      step(`${name}: startAction ${res.status} "${body}"`)
      const criterion = `"each a 400 with its sentence, no event": ${name}`
      expect(res.status, `${criterion}: the Start was not refused`).toBe(400)
      expect(body, `${criterion}: not the sentence`).toBe(sentence)
      const state = await dashboardState()
      step(`${name}: dashboard state ${JSON.stringify(state)}`)
      expect(state?.eventState, `${criterion}: an event started`).toBe('None')
      await expect(offered(c), `${criterion}: Wild Encounter is no longer offered`).toBeVisible()
      expect(backendLines(/\[(Spin|Run|RsBattle)\]/), `${criterion}: the hunt logged a line`).toEqual([])
      await shotAll(name, pages)
    }

    // 1. The lead's HP at 0, then back.
    const hp = await readMemory(LEAD_HP, 2)
    step(`lead HP ${hp.readUInt16LE(0)}`)
    await writeBytes(LEAD_HP, Buffer.from([0, 0]))
    await refused(card, '03-fainted', 'Spin', FAINTED_SENTENCE)
    await writeBytes(LEAD_HP, hp)

    // 2. Headbutt: refused before any read.
    await refused(card, '04-headbutt', 'Headbutt', HEADBUTT_SENTENCE)

    // 3. Select in the free field shows "An item in the BAG can be registered on SELECT", which locks the field until A.
    await runMacro('H.Select.2')
    await pollWhileWaiting(() => readU8(LOCK), (v) => v === 1, 5000, 'the field locked by the Select message')
    await refused(card, '05-message', 'Spin', FIELD_SENTENCE)
    // The message is still printing when the refusal returns, and A while it prints only hurries it: A is pressed again
    // until the field unlocks. No event is running, so these presses are the scenario's own tidying.
    let lock = 1
    for (let press = 0; press < 5 && lock === 1; press++) {
      await new Promise((r) => setTimeout(r, 1500))
      await runMacro('H.A.2')
      await new Promise((r) => setTimeout(r, 1000))
      lock = await readU8(LOCK)
    }
    expect(lock, 'the field free again after A at the Select message').toBe(0)

    await cancelBot(card)
    await shotAll('06-cancelled', pages)
    expect(flashUnchanged(ROM), `"no event": the ${ROM} SaveRAM's first 131,072 bytes changed`).toBe(true)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame, ROM)
  }
})
