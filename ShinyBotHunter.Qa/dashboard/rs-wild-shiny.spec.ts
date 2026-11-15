import { expect, test, type Page } from '@playwright/test'
import { runMacro, shotAll, step } from './driver'
import {
  armCheat, assertUnarmed, battles, BUTTON_WAITS, cancelEventAndBot, checkFlee, CONTROLLER, CURSOR, dashboardState, disarmCheat,
  ENCOUNTER, FOUND, flashUnchanged, installCheat, installPristine, launchRs, LOCK, MENU_FN, parseEncounter, pollWhileWaiting,
  pressesAfterTarget, psqlRows, readBattleBit, readU16, readU32, readU8, removeCheats, SHINY_PV, startWild, TARGET, teardown,
  waitForLine, WINDOW
} from './rs-wild'
import { backendLog, hex8, MINUTE, settings, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// When the cheat is armed. `menu` (the spec's moment): once encounter 2's action menu is logged, so encounter 3 is the
// shiny. `found` (the approved fallback, for a hotkey that lands after the battle): at encounter 3's battle line, so
// encounter 4 is the shiny.
const ARM_AT = process.env.QA_RS_ARM_AT ?? 'menu'
const SHINY_AT = ARM_AT === 'menu' ? 3 : 4

test('RS Wild Encounter shiny: the cheat armed in a battle, Wurmple CD631295 shiny, one alert, Waiting at the menu, disarmed and fled', async () => {
  expect(['menu', 'found'], 'QA_RS_ARM_AT must be menu or found').toContain(ARM_AT)
  test.setTimeout(20 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  const { pg, discordToken, channel } = settings()
  removeCheats()
  installCheat(ROM)
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')
    await assertUnarmed()
    await shotAll('01-launched', pages)
    const runStart = await startWild(card, { mode: 'Spin' })

    // 1. Arm during a battle: the hotkey's Shift is the GBA's Select, which in the field opens a message.
    if (ARM_AT === 'menu') {
      await waitForLine(ENCOUNTER, 2, 4 * MINUTE, '"after the second encounter\'s action menu is logged, arm the cheat"')
      step('arming at encounter 2\'s action menu (the spec\'s moment)')
    } else {
      await waitForLine(FOUND, 3, 5 * MINUTE, '"arm the cheat" (fallback): encounter 3\'s battle line')
      step('arming at encounter 3\'s battle line (the approved fallback)')
    }
    await armCheat()

    // 2. The next encounter is the shiny, and the bot parks at its action menu.
    const next = '"the next encounter is Wurmple with personality value CD631295, shiny"'
    const shiny = parseEncounter(await waitForLine(ENCOUNTER, SHINY_AT, 3 * MINUTE, next))
    expect([shiny.species, shiny.pid, shiny.shinyValue, shiny.shiny], `${next}: encounter ${SHINY_AT}`).toEqual([265, SHINY_PV, 1, true])
    const waiting = '"the bot Waiting at the action menu with no press after the read"'
    await waitForLine(TARGET, 1, MINUTE, waiting)
    await expect.poll(async () => (await dashboardState())?.eventState, { message: `${waiting}: the event is not Waiting`, timeout: MINUTE, intervals: [1000] })
      .toBe('Waiting')
    await expect(card.getByRole('button', { name: 'Resume', exact: true }), `${waiting}: no Resume on the card`).toBeVisible({ timeout: MINUTE })
    await shotAll('02-waiting', pages)

    // 3. At Waiting: still the action menu with the cursor on FIGHT, so nothing was pressed after the read.
    const controller = await readU32(CONTROLLER)
    const cursor = await readU8(CURSOR)
    step(`at Waiting: controller 0x${hex8(controller)}, cursor ${cursor}`)
    expect(hex8(controller), `${waiting}: the controller is not HandleInputChooseAction`).toBe(hex8(MENU_FN))
    expect(cursor, `${waiting}: the cursor moved off FIGHT`).toBe(0)
    await disarmCheat()

    // 4. The scenario flees the shiny's battle itself, pressing only while the bot is Waiting.
    const fled = '"the battle fled by the scenario before shutdown"'
    await runMacro('H.Right.2;H.Down.2')
    expect(await readU8(CURSOR), `${fled}: the cursor is not on RUN`).toBe(3)
    await runMacro('H.A.2')
    await pollWhileWaiting(() => readU16(WINDOW), (v) => BUTTON_WAITS.includes(v), 10_000, `${fled}: "Got away safely!" waiting for A`)
    await runMacro('H.A.2')
    await pollWhileWaiting(readBattleBit, (v) => v === 0, 20_000, `${fled}: the battle bit cleared`)
    await pollWhileWaiting(() => readU8(LOCK), (v) => v === 0, 20_000, `${fled}: the field free`)
    await shotAll('03-fled', pages)

    // 5. The bot logged no press after the target line, then two cancels (event, bot).
    const log = backendLog()
    expect(log.findIndex((l) => TARGET.test(l)), `${waiting}: the target line is not after encounter ${SHINY_AT}'s line`)
      .toBeGreaterThan(log.findIndex((l) => ENCOUNTER.test(l) && parseEncounter(l).pid === SHINY_PV))
    expect(pressesAfterTarget(log), `${waiting}: a press line after the target line`).toEqual([])
    await cancelEventAndBot(card)
    await shotAll('04-cancelled', pages)
    expect(flashUnchanged(ROM), `"the save's first 131,072 bytes unchanged": the ${ROM} SaveRAM changed`).toBe(true)
    await shutdown(frame, admin)

    // 6. "one alert": exactly one Shiny Wurmple message newer than the Run in the shiny channel.
    let alerts: string[] = []
    let status = ''
    const discordDeadline = Date.now() + MINUTE
    while (Date.now() < discordDeadline) {
      const res = await fetch(`https://discord.com/api/v10/channels/${channel}/messages?limit=20`, { headers: { Authorization: `Bot ${discordToken}` } })
      status = `HTTP ${res.status}`
      if (res.ok) {
        const messages = (await res.json()) as { timestamp: string; content?: string; embeds?: { title?: string }[] }[]
        alerts = messages.filter((m) => Date.parse(m.timestamp) > Date.parse(runStart))
          .map((m) => m.embeds?.[0]?.title ?? m.content ?? '').filter((t) => t.includes('Shiny Wurmple'))
        if (alerts.length > 0) break
      }
      await sleep(5000)
    }
    step(`Discord (${status}): ${alerts.length} Wurmple alerts since the Run: ${JSON.stringify(alerts)}`)
    expect(alerts.length, '"one alert": not exactly one Shiny Wurmple alert since the Run').toBe(1)

    // 7. The shiny's row is the only shiny one, with the same PID.
    const shinyRows = psqlRows(pg, runStart, ROM).filter((r) => r[3] === 't')
    expect(shinyRows.map((r) => [hex8(Number(r[0])), r[1]]), `${next}: not exactly one shiny row, Wurmple ${SHINY_PV}`).toEqual([[SHINY_PV, '265']])

    // 8. The battles before the shiny were clean flees.
    const all = battles(log)
    for (let k = 0; k < SHINY_AT - 1; k++) checkFlee(all[k], k + 1)
  } finally {
    await teardown(frame, ROM)
  }
})
