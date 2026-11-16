import { spawnSync } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Page } from '@playwright/test'
import { readMemory, runMacro, shotAll, step } from './driver'
import { partyCount, partySlot } from './jirachi-seed'
import {
  armCheat, assertUnarmed, BALL_SLOT, BUTTON_WAITS, cancelBot, cancelEventAndBot, CURSOR, dashboardState, disarmCheat, ENCOUNTER,
  FLASH, FOUND, installCheat, installPristine, IN_BATTLE, launchRs, LOCK, OUTCOME, parseEncounter, PARTY, PARTY_COUNT, PHASE,
  pollWhileWaiting, PRISTINE, readU16, readU32, readU8, removeCheats, saveOf, SHINY_PV, SLOT_SIZE, SPIN_LINE, startWild, TARGET,
  teardown, waitForLine, WINDOW, writeBytes
} from './rs-wild'
import { backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
const MONGOSH = path.join(process.env.LOCALAPPDATA!, 'Programs', 'mongosh', 'mongosh.exe')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

function mongo(uri: string, script: string): any {
  const res = spawnSync(MONGOSH, [uri, '--quiet', '--eval', script], { encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`mongosh exited with ${res.status}: ${res.stderr}`)
  return JSON.parse(res.stdout.trim())
}
const caughtFilter = `{ Phase: '${PHASE}', GameFileName: '${ROM}', 'Pokemon.PID': NumberLong('${parseInt(SHINY_PV, 16)}') }`

// When the cheat is armed, as rs-wild-shiny: at encounter 2's action menu, or (QA_RS_ARM_AT=found) at encounter 3's battle line.
const ARM_AT = process.env.QA_RS_ARM_AT ?? 'menu'
const SHINY_AT = ARM_AT === 'menu' ? 3 : 4

// The Pokédex owned flags: gSaveBlock2 + 0x28 (pokeruby's struct SaveBlock2), one bit per national number less one.
const OWNED = 0x02024ea4 + 0x28
const owned = (flags: Buffer, national: number): boolean => ((flags[(national - 1) >> 3] >> ((national - 1) & 7)) & 1) === 1
const MUDKIP = 258
const WURMPLE = 265
const START_MENU = /\[StartMenu\] Start menu: .*; SAVE is at position (\d+)\.\s*$/
const FOUND_POKEMON = /Found pokemon: True\s*$/
const CRASH = /\[BotService\] Event '.*' crashed/

/** Every read the catch makes goes to steps.log, so one failed run is enough to tune the waits. */
const logged = (name: string, read: () => Promise<number>) => async (): Promise<number> => {
  const v = await read()
  step(`${name} read ${v}`)
  return v
}
const battleWindow = logged('battle window state', () => readU16(WINDOW))
const lock = logged('field lock', () => readU8(LOCK))
const battleBit = logged('battle bit', async () => ((await readU8(IN_BATTLE)) >> 1) & 1)

/** Adds the backend's exception text (the lines after a crash line) to a failure after Resume. */
async function withCrashText<T>(criterion: string, wait: () => Promise<T>): Promise<T> {
  try {
    return await wait()
  } catch (e) {
    const log = backendLog()
    const at = log.findIndex((l) => CRASH.test(l))
    const text = at === -1 ? 'no crash line in backend.log' : log.slice(at, at + 8).join('\n')
    throw new Error(`${criterion}: ${(e as Error).message}\nthe backend's exception:\n${text}`)
  }
}

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
    const caughtBefore = mongo(mongoUri, `print(db.CaughtPokemon.countDocuments(${caughtFilter}))`) as number
    step(`${caughtBefore} matching CaughtPokemon documents before the run`)
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')
    await assertUnarmed()

    // 1. With the bot idle: a Master Ball (item 1, u16) ×5 (u16) in the first ball slot (research doc, "A catch"), and
    // whether Wurmple is a new species.
    await writeBytes(BALL_SLOT, Buffer.from([1, 0, 5, 0]))
    const flags = await readMemory(OWNED, 52)
    step(`Pokédex owned flags: ${flags.toString('hex')}`)
    expect(owned(flags, MUDKIP), 'the owned flags are not where the scenario reads them: Mudkip (258) is not owned').toBe(true)
    const newSpecies = !owned(flags, WURMPLE)
    step(`Wurmple is ${newSpecies ? 'new' : 'already owned'}`)
    await shotAll('01-launched', pages)
    await startWild(card, { mode: 'Spin' })

    // 2. Arm as rs-wild-shiny does; the shiny parks the bot at its action menu.
    if (ARM_AT === 'menu') await waitForLine(ENCOUNTER, 2, 4 * MINUTE, '"arm the shiny cheat as rs-wild-shiny does": encounter 2')
    else await waitForLine(FOUND, 3, 5 * MINUTE, '"arm the shiny cheat as rs-wild-shiny does" (fallback): encounter 3\'s battle line')
    step(`arming at ${ARM_AT === 'menu' ? 'encounter 2\'s action menu' : 'encounter 3\'s battle line (the approved fallback)'}`)
    await armCheat()
    const target = '"when the bot is Waiting on the target"'
    const shiny = parseEncounter(await waitForLine(ENCOUNTER, SHINY_AT, 3 * MINUTE, target))
    expect([shiny.species, shiny.pid, shiny.shiny], `${target}: encounter ${SHINY_AT}`).toEqual([WURMPLE, SHINY_PV, true])
    await waitForLine(TARGET, 1, MINUTE, target)
    await expect.poll(async () => (await dashboardState())?.eventState, { message: `${target}: the event is not Waiting`, timeout: MINUTE, intervals: [1000] })
      .toBe('Waiting')
    const resume = card.getByText('Resume', { exact: true })
    await expect(resume, `${target}: no Resume on the card`).toBeVisible({ timeout: MINUTE })
    await disarmCheat()
    await shotAll('02-waiting', pages)

    // 3. The catch, pressed by the scenario while the bot is Waiting (no bot press then).
    await catchIt(newSpecies)
    const outcome = await readU8(OUTCOME)
    const count = await readU8(PARTY_COUNT)
    const pid = await readU32(PARTY + SLOT_SIZE)
    step(`after the catch: outcome ${outcome}, party count ${count}, slot 2 PID ${hex8(pid)}`)
    expect([outcome, count, hex8(pid)], '"the scenario catches it itself": outcome, party count, slot 2 PID').toEqual([7, 2, SHINY_PV])
    await shotAll('03-caught', pages)

    // 4. Resume: the action's Run beside its "Resume" label.
    const seeksBefore = backendLog().filter((l) => SPIN_LINE.test(l)).length
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step('Resume run')
    const saves = '"the bot saves through the start menu"'
    await withCrashText(saves, () => waitForLine(FOUND_POKEMON, 1, 2 * MINUTE, `${saves}: the catch check`))
    await withCrashText(saves, () => waitForLine(START_MENU, 1, 2 * MINUTE, `${saves}: the [StartMenu] line`))

    // 5. "the hunt carries on or completes by the milestone": the next seek line, or Complete.
    const carries = '"the hunt carries on or completes by the milestone"'
    const ended = await withCrashText(carries, async () => {
      const deadline = Date.now() + 3 * MINUTE
      while (Date.now() < deadline) {
        if ((await dashboardState())?.eventState === 'Complete') return 'Complete'
        const log = backendLog()
        if (log.some((l) => CRASH.test(l))) throw new Error('the event crashed')
        if (log.filter((l) => SPIN_LINE.test(l)).length > seeksBefore) return 'a new seek'
        await sleep(2000)
      }
      throw new Error('neither a new [Spin] seek line nor Complete within 180 s')
    })
    step(`after Resume: ${ended}`)
    await shotAll('04-resumed', pages)

    // 6. "one new CaughtPokemon with personality value CD631295". The insert is queued, so poll.
    await expect.poll(() => mongo(mongoUri, `print(db.CaughtPokemon.countDocuments(${caughtFilter}))`) as number,
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

/**
 * From the action menu: Right to BAG (the cursor byte), A; Right to the Poké Balls pocket, A on the ball, A on USE; A at each
 * battle message; A twice at the Pokédex page for a new species; B at the nickname; then the free field. Each failure names
 * the step.
 */
async function catchIt(newSpecies: boolean): Promise<void> {
  const criterion = '"the scenario catches it itself"'
  await runMacro('H.Right.2')
  await pollWhileWaiting(logged('action cursor', () => readU8(CURSOR)), (v) => v === 1, 2000, `${criterion}: Right to BAG (cursor 1)`)
  // The bag is not a battle screen (callback 2 is neither the battle nor the field), and no byte for it is known: fixed
  // waits for the bag to open, the pocket to change and the ball's menu, then USE.
  await runMacro('H.A.2;W.60')
  step('A on BAG, 60 frames (no byte known for the bag)')
  await runMacro('H.Right.2;W.20')
  step('Right to the Poké Balls pocket, 20 frames (no byte known)')
  await runMacro('H.A.2;W.20')
  step('A on the Master Ball, 20 frames (no byte known)')
  await runMacro('H.A.2')
  step('A on USE')

  // "Gotcha! … was caught!", and for a new species "…data was added to the POKéDEX": each a button wait, left after its A.
  const messages = newSpecies ? ['Gotcha!', 'Pokédex data'] : ['Gotcha!']
  for (const message of messages) {
    await pollWhileWaiting(battleWindow, (v) => BUTTON_WAITS.includes(v), 60_000, `${criterion}: the "${message}" message waiting for A`)
    await runMacro('H.A.2')
    await pollWhileWaiting(battleWindow, (v) => !BUTTON_WAITS.includes(v), 5000, `${criterion}: the A at "${message}" read`)
  }
  if (newSpecies) {
    // The Pokédex page is another screen with no byte known: fixed waits for it to open and between its two A.
    await runMacro('W.120;H.A.2;W.60;H.A.2;W.60')
    step('the Pokédex page: 120 frames, A, 60 frames, A, 60 frames (no byte known)')
  }
  // The nickname question is a yes/no, not a button wait: a fixed wait for it, then B.
  await runMacro('W.60;H.B.2')
  step('the nickname question: 60 frames, B (no byte known)')
  await pollWhileWaiting(lock, (v) => v === 0, 30_000, `${criterion}: the free field after B at the nickname (lock 0)`)
  await pollWhileWaiting(battleBit, (v) => v === 0, 5000, `${criterion}: the free field after B at the nickname (battle bit 0)`)
}
