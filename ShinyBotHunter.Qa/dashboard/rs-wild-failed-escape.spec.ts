import { expect, test, type Page } from '@playwright/test'
import { readMemory, shotAll, step } from './driver'
import {
  ADVANCE, cancelEventAndBot, CONFIRM, ESCAPE_FAILED, flashUnchanged, installPristine, launchRs, LEAD_SPEED, OVER, PRESS_LINE,
  removeCheats, startWild, teardown, TO_RUN, waitForLine, writeBytes
} from './rs-wild'
import { backendLog, MINUTE, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
const CRITERION = '"a failed escape followed by another A on RUN with the cursor still 3"'
const STOP = /\[RsBattle\] stopped: /

test('RS Wild Encounter, a failed escape: lead Speed 1, A on RUN again with the cursor still on RUN, no stop', async () => {
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')

    // With the bot idle: the lead's Speed to 1 in party RAM. The battle copies it, so the Lv 2 and 3 Pokémon outrun the lead.
    const speed = await readMemory(LEAD_SPEED, 2)
    step(`lead Speed ${speed.readUInt16LE(0)}`)
    await writeBytes(LEAD_SPEED, Buffer.from([1, 0]))
    await shotAll('01-launched', pages)
    await startWild(card, { mode: 'Spin' })

    // The first failed escape, then the end of its battle: the chance rises a try, so it gets away by try 9 of 12.
    await waitForLine(ESCAPE_FAILED, 1, 5 * MINUTE, `${CRITERION}: no failed escape`)
    const seen = backendLog()
    const oversBefore = seen.slice(0, seen.findIndex((l) => ESCAPE_FAILED.test(l))).filter((l) => OVER.test(l)).length
    await waitForLine(OVER, oversBefore + 1, 3 * MINUTE, `${CRITERION}: the battle with the failed escape did not end`)
    await shotAll('02-escaped', pages)
    await cancelEventAndBot(card)
    await shotAll('03-cancelled', pages)
    expect(flashUnchanged(ROM), `"the save's first 131,072 bytes unchanged": the ${ROM} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    const log = backendLog()
    expect(log.filter((l) => STOP.test(l)), '"no stop": a stopped line').toEqual([])
    const failed = log.flatMap((l, i) => (ESCAPE_FAILED.test(l) ? [i] : []))
    expect(failed.length, `${CRITERION}: no escape failed line`).toBeGreaterThan(0)
    for (const i of failed) {
      const [, tryNo, cursor, hp] = ESCAPE_FAILED.exec(log[i])!
      const k = Number(tryNo)
      const at = `${CRITERION}: the failed try ${k}`
      expect([cursor, Number(hp) > 0], `${at}: not with the cursor at 3 and the lead's HP above 0: ${log[i].trim()}`).toEqual(['3', true])

      // Before it: A on RUN (try k), then "Can't escape!" advanced.
      let confirm = i - 1
      while (confirm >= 0 && !CONFIRM.test(log[confirm])) confirm--
      expect(confirm >= 0 && CONFIRM.exec(log[confirm])![1], `${at}: the A on RUN before it is not try ${k}`).toBe(tryNo)
      expect(log.slice(confirm + 1, i).some((l) => ADVANCE.test(l)), `${at}: no BattleAdvance ("Can't escape!") before it`).toBe(true)

      // After it: the next press is A on RUN (try k + 1), with no cursor press, since the cursor is still on RUN.
      const next = log.slice(i + 1).find((l) => PRESS_LINE.test(l)) ?? 'none'
      expect(TO_RUN.test(next), `${at}: a BattleCursorToRun after it: ${next.trim()}`).toBe(false)
      expect(CONFIRM.exec(next)?.[1], `${at}: the next press is not BattleConfirm on RUN (try ${k + 1}): ${next.trim()}`).toBe(String(k + 1))
    }
  } finally {
    await teardown(frame, ROM)
  }
})
