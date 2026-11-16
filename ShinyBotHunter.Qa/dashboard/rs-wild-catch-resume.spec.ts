import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { runMacro, shotAll, step } from './driver'
import { partyCount, partySlot } from './jirachi-seed'
import {
  ARM_AT, armAndWaitOnShiny, assertUnarmed, cancelBot, cancelEventAndBot, caughtCount, dashboardState, failOnStop, fieldBattleBit,
  fieldLock, FLASH, installCheat, installPristine, launchRs, masterBallAndDex, OUTCOME, PARTY, PARTY_COUNT, pollWhileWaiting,
  PRISTINE, readU32, readU8, removeCheats, resumeAndSave, saveOf, SHINY_PV, SLOT_SIZE, SPIN_LINE, startWild, teardown, throwBall,
  withCrashText
} from './rs-wild'
import { backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

test('RS Wild Encounter catch and Resume: the shiny caught with a Master Ball, saved through the start menu, recorded, in party slot 2', async () => {
  expect(['menu', 'found'], 'QA_RS_ARM_AT must be menu or found').toContain(ARM_AT)
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  const { mongoUri } = settings()
  removeCheats()
  installCheat(ROM)
  installPristine(ROM)
  step('SA-Wild.SaveRAM and .bak restored from the pristine copy before the run')
  try {
    const caughtBefore = caughtCount(mongoUri, ROM)
    step(`${caughtBefore} matching CaughtPokemon documents before the run`)
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')
    await assertUnarmed()

    // 1. With the bot idle: the Master Ball, and whether Wurmple is a new species.
    const newSpecies = await masterBallAndDex()
    await shotAll('01-launched', pages)
    await startWild(card, { mode: 'Spin' })

    // 2. Arm as rs-wild-shiny does; the shiny parks the bot at its action menu.
    await armAndWaitOnShiny(card)
    await shotAll('02-waiting', pages)

    // 3. The catch, pressed by the scenario while the bot is Waiting (no bot press then).
    await throwBall(newSpecies)
    // The nickname question is a yes/no, not a button wait: a fixed wait for it, then B.
    const criterion = '"the scenario catches it itself"'
    await runMacro('W.60;H.B.2')
    step('the nickname question: 60 frames, B (no byte known)')
    await pollWhileWaiting(fieldLock, (v) => v === 0, 30_000, `${criterion}: the free field after B at the nickname (lock 0)`)
    await pollWhileWaiting(fieldBattleBit, (v) => v === 0, 5000, `${criterion}: the free field after B at the nickname (battle bit 0)`)
    const outcome = await readU8(OUTCOME)
    const count = await readU8(PARTY_COUNT)
    const pid = await readU32(PARTY + SLOT_SIZE)
    step(`after the catch: outcome ${outcome}, party count ${count}, slot 2 PID ${hex8(pid)}`)
    expect([outcome, count, hex8(pid)], '"the scenario catches it itself": outcome, party count, slot 2 PID').toEqual([7, 2, SHINY_PV])
    await shotAll('03-caught', pages)

    // 4. Resume: the action's Run beside its "Resume" label.
    const seeksBefore = await resumeAndSave(card)

    // 5. "the hunt carries on or completes by the milestone": the next seek line, or Complete.
    const carries = '"the hunt carries on or completes by the milestone"'
    const ended = await withCrashText(carries, async () => {
      const deadline = Date.now() + 3 * MINUTE
      while (Date.now() < deadline) {
        if ((await dashboardState())?.eventState === 'Complete') return 'Complete'
        const log = backendLog()
        failOnStop(log)
        if (log.filter((l) => SPIN_LINE.test(l)).length > seeksBefore) return 'a new seek'
        await sleep(2000)
      }
      throw new Error('neither a new [Spin] seek line nor Complete within 180 s')
    })
    step(`after Resume: ${ended}`)
    await shotAll('04-resumed', pages)

    // 6. "one new CaughtPokemon with personality value CD631295". The insert is queued, so poll.
    await expect.poll(() => caughtCount(mongoUri, ROM),
      { message: `"one new CaughtPokemon with personality value ${SHINY_PV}"`, timeout: MINUTE, intervals: [2000] }).toBe(caughtBefore + 1)
    step('one new CaughtPokemon document')

    // 7. The cancels close the emulator, which leaves the flushed SaveRAM; then "the saved SaveRAM holds it in party slot 2".
    if (ended === 'Complete') await cancelBot(card)
    else await cancelEventAndBot(card)
    const saved = '"the saved SaveRAM holds it in party slot 2"'
    const save = fs.readFileSync(saveOf(ROM))
    const slot2 = partySlot(save, 1)
    step(`saved SaveRAM: party count ${partyCount(save)}, slot 2 PID ${hex8(slot2.pid)}`)
    expect(save.subarray(0, FLASH).equals(fs.readFileSync(PRISTINE).subarray(0, FLASH)), `${saved}: the flash equals the pristine copy`).toBe(false)
    expect([partyCount(save), hex8(slot2.pid)], `${saved}: party count and slot 2 PID`).toEqual([2, SHINY_PV])
    await shutdown(frame, admin)
  } finally {
    // Keeps the end-state save in the run folder and restores SA-Wild.SaveRAM and .bak from the pristine copy once EmuHawk has exited.
    await teardown(frame, ROM)
  }
})
