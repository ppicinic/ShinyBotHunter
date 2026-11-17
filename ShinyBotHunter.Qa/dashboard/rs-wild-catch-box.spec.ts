import * as fs from 'fs'
import { expect, test, type Page } from '@playwright/test'
import { readMemory, runMacro, shotAll, step } from './driver'
import {
  ARM_AT, armAndWaitOnShiny, assertUnarmed, battleWindow, BUTTON_WAITS, cancelEventAndBot, caughtCount, dashboardState, failOnStop,
  fieldBattleBit, fieldLock, FLASH, installCheat, installPristine, launchRs, masterBallAndDex, OUTCOME, PARTY, PARTY_COUNT,
  pollWhileWaiting, PRISTINE, readU8, removeCheats, resumeAndSave, saveOf, SHINY_PV, SLOT_SIZE, SPIN_LINE, startWild, teardown,
  throwBall, withCrashText, writeBytes
} from './rs-wild'
import { backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// gPokemonStorage.boxes (RsPointerService.BoxLayout; research doc, "Memory read by the spike"): 14 boxes of 30 slots of
// 80 bytes, the personality value the first u32 of each slot.
const BOXES = 0x020300a4
const BOX_COUNT = 14
const BOX_SLOTS = 30
const BOX_SLOT_SIZE = 80
const BOX_SIZE = BOX_SLOTS * BOX_SLOT_SIZE

test('RS Wild Encounter catch with a full party: the shiny sent to a PC box, found there on Resume, saved, recorded, and the hunt carries on', async () => {
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

    // 1. With the bot idle: six in the party (slot 1 copied into slots 2 to 6, since the party read rejects an empty
    // slot, then the count), the Master Ball, and whether Wurmple is a new species.
    const slot1 = await readMemory(PARTY, SLOT_SIZE)
    expect(await readU8(PARTY_COUNT), 'the save\'s party is not one').toBe(1)
    await writeBytes(PARTY + SLOT_SIZE, Buffer.concat([slot1, slot1, slot1, slot1, slot1]))
    await writeBytes(PARTY_COUNT, Buffer.from([6]))
    const newSpecies = await masterBallAndDex()
    await shotAll('01-launched', pages)
    try {
      await startWild(card, { mode: 'Spin' })
    } catch (e) {
      throw new Error(`"Run starts with six in the party": ${(e as Error).message}`)
    }

    // 2. Arm as rs-wild-shiny does; the shiny parks the bot at its action menu.
    await armAndWaitOnShiny(card)
    await shotAll('02-waiting', pages)

    // 3. The catch, pressed by the scenario while the bot is Waiting (no bot press then), then the PC.
    await throwBall(newSpecies)
    await toTheFreeField()
    const outcome = await readU8(OUTCOME)
    const count = await readU8(PARTY_COUNT)
    step(`after the catch: outcome ${outcome}, party count ${count}`)
    expect([outcome, count], '"the scenario catches it itself": outcome and party count').toEqual([7, 6])
    await findInBoxes()
    await shotAll('03-caught', pages)

    // 4. Resume: the action's Run beside its "Resume" label; the catch check finds it in the box and the bot saves.
    const seeksBefore = await resumeAndSave(card)

    // 5. "the hunt carries on with a new seek, though the party is full": no stop, no Complete.
    const carries = '"the hunt carries on with a new seek, though the party is full"'
    await withCrashText(carries, async () => {
      const deadline = Date.now() + 3 * MINUTE
      while (Date.now() < deadline) {
        if ((await dashboardState())?.eventState === 'Complete') throw new Error('the hunt went Complete')
        const log = backendLog()
        failOnStop(log)
        if (log.filter((l) => SPIN_LINE.test(l)).length > seeksBefore) return
        await sleep(2000)
      }
      throw new Error('no new [Spin] seek line within 180 s')
    })
    step('after Resume: a new seek')
    await shotAll('04-resumed', pages)

    // 6. "one new CaughtPokemon with personality value CD631295". The insert is queued, so poll.
    await expect.poll(() => caughtCount(mongoUri, ROM),
      { message: `"one new CaughtPokemon with personality value ${SHINY_PV}"`, timeout: MINUTE, intervals: [2000] }).toBe(caughtBefore + 1)
    step('one new CaughtPokemon document')

    // 7. The cancels close the emulator, which leaves the flushed SaveRAM; "the saved SaveRAM differs from the pristine copy".
    await cancelEventAndBot(card)
    const save = fs.readFileSync(saveOf(ROM))
    expect(save.subarray(0, FLASH).equals(fs.readFileSync(PRISTINE).subarray(0, FLASH)),
      '"the saved SaveRAM differs from the pristine copy": the flash equals it').toBe(false)
    step('saved SaveRAM flash differs from the pristine copy')
    await shutdown(frame, admin)
  } finally {
    // Keeps the end-state save in the run folder and restores SA-Wild.SaveRAM and .bak from the pristine copy once EmuHawk has exited.
    await teardown(frame, ROM)
  }
})

/**
 * After the Pokédex page, the nickname question and "… was sent to …'s PC." come in an order not yet seen on the emulator,
 * so this is driven by memory: until the field is free (lock 0 and battle bit 0, 60 s), A at each button wait, confirmed
 * by the window leaving it; when nothing has changed for 2 s and the nickname question is not yet answered, B once.
 */
async function toTheFreeField(): Promise<void> {
  const criterion = '"the free field after the catch"'
  const deadline = Date.now() + 60_000
  let nicknameAnswered = false
  let last = ''
  let changedAt = Date.now()
  while (true) {
    const lock = await fieldLock()
    const bit = await fieldBattleBit()
    if (lock === 0 && bit === 0) {
      step(`${criterion}: lock 0, battle bit 0`)
      return
    }
    const window = await battleWindow()
    if (Date.now() > deadline) throw new Error(`${criterion} within 60 s; last read lock ${lock}, battle bit ${bit}, window ${window}`)
    const seen = `${lock} ${bit} ${window}`
    if (seen !== last) {
      last = seen
      changedAt = Date.now()
    }
    if (BUTTON_WAITS.includes(window)) {
      await runMacro('H.A.2')
      step(`the battle message at window ${window}: A`)
      await pollWhileWaiting(battleWindow, (v) => !BUTTON_WAITS.includes(v), 5000, `${criterion}: the A at window ${window} read`)
      last = ''
    } else if (!nicknameAnswered && Date.now() - changedAt >= 2000) {
      await runMacro('H.B.2')
      step('the nickname question: B (nothing changed for 2 s)')
      nicknameAnswered = true
      last = ''
    }
    await sleep(500)
  }
}

/** The shiny's personality value in box storage: one read per box until found; fails naming the address otherwise. */
async function findInBoxes(): Promise<void> {
  for (let box = 0; box < BOX_COUNT; box++) {
    const bytes = await readMemory(BOXES + box * BOX_SIZE, BOX_SIZE)
    for (let slot = 0; slot < BOX_SLOTS; slot++) {
      if (hex8(bytes.readUInt32LE(slot * BOX_SLOT_SIZE)) === SHINY_PV) {
        step(`PID ${SHINY_PV} in box ${box + 1} slot ${slot + 1} (0x${(BOXES + box * BOX_SIZE + slot * BOX_SLOT_SIZE).toString(16).toUpperCase()})`)
        return
      }
    }
  }
  throw new Error(`"the Pokémon is in box storage": PID ${SHINY_PV} in none of the ${BOX_COUNT} boxes read from 0x${BOXES.toString(16).toUpperCase()}`)
}
