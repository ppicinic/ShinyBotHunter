import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Locator } from '@playwright/test'
import { readU16, readU32, repoDir, runMacro, sendCheatHotkey, shotAll, step, toS16 } from './driver'
import { Y_ADDR } from './headbutt'
import { backendLog, CRASHED, MINUTE, settings } from './static'
import {
  assertLocalStores, cancelBot, clockConfig, deletePlan, launchHgss, milestoneMessages, putGoal, putPlan, renamePlan, RUN_FROM, saveOf,
  seen, shutdown, stageCopy, startFrame, startWild, teardown, variant, writeLeadExtension, type Frame, type GoalEntry
} from './milestones'

// #525 Q1c: FR-34a with no plan and for a plan's last milestone, FR-13's wanted shiny parking the hunt, the shiny alert,
// C4a's "in use while parked in Waiting".
const VARIANT = variant(['no-plan', 'plan'] as const)
const HANDLE = 'HG-Complete'
const ROUTE32 = '36-land'
const PLAN = 'QA Single'
const CHEAT_SOURCE = path.join(repoDir, 'ShinyBotHunter.Qa', 'Cheats', 'hgss-usa-rev0', 'wild-shiny.cht')
// BizHawk loads <ROM name>.cht from NDS\Cheats, beside NDS\SaveRAM, when the ROM opens.
const CHEAT_TARGET = path.join(path.dirname(path.dirname(saveOf(HANDLE))), 'Cheats', `${HANDLE}.cht`)
// [address, original, patched] for the two halfwords of the wild-shiny cheat, as hgss-route33.spec.ts has them.
const PATCH_SITES: [number, number, number][] = [[0x02247d90, 0x2300, 0x682b], [0x02247d98, 0xfe3f, 0xfdbb]]
// Every HG Route 32 day species (Bellsprout, Rattata, Mareep, Hoppip), so any shiny there is a target.
const GOAL = [69, 19, 179, 187]
// The battle menu's Run on the touch screen (CommandPreprocessor "Run"), as the bot's flee touches it.
const RUN_TOUCH = 'S.125.175.3'
const FOE = /(\S+), isShiny: (True|False)\s*$/
const ALERT = /^attachment:\/\//
const FOUND_FALSE = /^Found pokemon: False\s*$/

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
const count = (pattern: RegExp, from = 0): number => backendLog().slice(from).filter((l) => pattern.test(l)).length
const goal = (amount: number): GoalEntry[] => GOAL.map((species) => ({ species, amount }))
const state = async (): Promise<string> => (await (await fetch('http://localhost:5000/api/bot/getDashboardState')).json())['0']?.eventState

/**
 * With the bot parked in Waiting at the shiny's battle menu: one touch on Run per menu, each confirmed by the field position
 * reading non-zero again (it reads (0, 0) in a battle); a failed escape brings the menu back and is touched again.
 */
async function fleeWhileParked(): Promise<void> {
  for (let attempt = 1; attempt <= 12; attempt++) {
    expect(await state(), 'the bot is not parked while the scenario flees').toBe('Waiting')
    await runMacro(RUN_TOUCH)
    const deadline = Date.now() + 10_000
    while (Date.now() < deadline) {
      if (toS16(await readU32(Y_ADDR)) > 0) {
        step(`fled the parked battle with touch ${attempt}`)
        return
      }
      await sleep(250)
    }
    step(`touch ${attempt} on Run: still in the battle after 10 s (a failed escape, or the touch not taken)`)
  }
  throw new Error('the parked battle did not end after 12 touches on Run')
}

async function discordAlert(name: string, sinceIso: string): Promise<string> {
  const { discordToken, channel } = settings()
  const res = await fetch(`https://discord.com/api/v10/channels/${channel}/messages?limit=10`, { headers: { Authorization: `Bot ${discordToken}` } })
  const messages = res.ok ? ((await res.json()) as { timestamp: string; embeds?: { title?: string }[] }[]) : []
  return messages.filter((m) => Date.parse(m.timestamp) > Date.parse(sinceIso)).flatMap((m) => m.embeds ?? []).map((e) => e.title ?? '')
    .find((t) => t.includes(`Shiny ${name} encountered`)) ?? `none (HTTP ${res.status})`
}

test(`HGSS milestone complete (${VARIANT}): a wanted shiny parks the hunt, the goal emptied, Resume tells once and completes`, async () => {
  test.setTimeout(30 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  assertLocalStores()
  try {
    stageCopy(HANDLE, { rom: 'HG-Route32.nds', save: 'hgss.qa-route32' })
    clockConfig('2026-11-29T12:00:00')
    // Loaded disabled when the ROM opens; armed by the hotkey.
    fs.writeFileSync(CHEAT_TARGET, fs.readFileSync(CHEAT_SOURCE, 'utf8').replace(/\r\n/g, '\n'))
    step(`cheat copied to ${CHEAT_TARGET}`)
    const admin = await startFrame(frame)
    const pages = { admin }
    expect((await putGoal(HANDLE, goal(1))).status, 'the goal').toBeLessThan(300)
    if (VARIANT === 'plan') expect((await putPlan('HGSS', { name: PLAN, default: ROUTE32, rules: [] })).status, 'the plan').toBeLessThan(300)
    const card: Locator = await launchHgss(admin, HANDLE, { clock: true })
    // The patch must not be active before the hotkey; the sites hold either value once the overlay is loaded.
    for (const [addr, original, patched] of PATCH_SITES) {
      await expect.poll(async () => [original, patched].includes(await readU16(addr)), { timeout: MINUTE, intervals: [500] }).toBe(true)
      expect(await readU16(addr), `patch site ${addr.toString(16)} active at launch`).toBe(original)
    }
    await writeLeadExtension({ hp: 30 })

    // 1. The hunt at the generated anchor; two encounters there, then the patch armed.
    const runStart = await startWild(card, VARIANT === 'plan' ? { plan: PLAN, mode: 'Run', yRange: 1 } : { phase: ROUTE32, mode: 'Run', yRange: 1 })
    await seen(RUN_FROM, 1, 15 * MINUTE)
    const runAt = backendLog().findIndex((l) => RUN_FROM.test(l))
    await expect.poll(() => count(FOE, runAt), { timeout: 10 * MINUTE, intervals: [2000] }).toBeGreaterThanOrEqual(2)
    const armAt = new Date().toISOString()
    const foesBefore = count(FOE)
    expect(sendCheatHotkey(), 'EmuHawk was not the foreground window when the hotkey was sent').toBe(true)
    step(`armed at ${armAt}`)

    // 2. The next encounter is shiny and a target: Resume on the card, Waiting, the alert sent.
    const resume = card.getByText('Resume', { exact: true })
    await expect.poll(async () => {
      const crash = backendLog().find((l) => CRASHED.test(l))
      if (crash) throw new Error(`the event crashed: ${crash}`)
      return resume.isVisible()
    }, { timeout: 10 * MINUTE, intervals: [2000] }).toBe(true)
    expect(await state(), 'not parked in Waiting').toBe('Waiting')
    const foe = FOE.exec(backendLog().filter((l) => FOE.test(l)).at(-1)!)!
    step(`the foe that parked the hunt: ${foe[0].trim()}; ${count(FOE) - foesBefore} encounters after the arm`)
    expect(foe[2], 'the last foe is not shiny').toBe('True')
    expect(backendLog().slice(runAt).some((l) => ALERT.test(l)), 'no shiny alert was sent (its attachment line)').toBe(true)
    if (backendLog().some((l) => /Discord client ready\. Logged in as /.test(l))) {
      const title = await discordAlert(foe[1], armAt)
      step(`Discord alert: ${title}`)
      expect(title, 'no shiny alert in the channel').toContain(`Shiny ${foe[1]} encountered`)
    } else {
      step('Discord not logged in: the alert is the attachment line alone')
    }
    await shotAll('01-parked', pages)

    // 3. C4a: the plan is in use while its hunt is parked.
    if (VARIANT === 'plan') {
      const inUse = `${PLAN} is in use by the hunt on bot #0; cancel that hunt first.`
      for (const res of [await deletePlan('HGSS', PLAN), await renamePlan('HGSS', PLAN, `${PLAN} 2`)]) {
        expect([res.status, res.text], 'the plan was not refused while parked').toEqual([400, inUse])
      }
    }

    // 4. The scenario ends the battle while the bot waits; the goal emptied; Resume.
    await fleeWhileParked()
    await sleep(3000)
    expect((await putGoal(HANDLE, goal(0))).status, 'the emptied goal').toBeLessThan(300)
    const resumeStart = new Date().toISOString()
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step('Resume run')
    await seen(FOUND_FALSE, 1, 2 * MINUTE)

    // 5. Exactly one milestone message, then the walk home and Complete.
    const expected = VARIANT === 'plan'
      ? `${PLAN}: Route 32 · grass is complete on ${HANDLE}. The plan is finished.`
      : `Route 32 · grass is complete on ${HANDLE}.`
    await expect.poll(async () => (await milestoneMessages(resumeStart)).length, { timeout: MINUTE, intervals: [5000] }).toBeGreaterThanOrEqual(1)
    await expect.poll(state, { timeout: 5 * MINUTE, intervals: [2000] }).toBe('Complete')
    const messages = await milestoneMessages(runStart)
    expect(messages.length, `not exactly one milestone message: ${JSON.stringify(messages)}`).toBe(1)
    expect(messages[0], 'the milestone message').toContain(expected)
    await shotAll('02-complete', pages)

    // 6. Nothing was saved in game (the shiny was not caught).
    await cancelBot(card)
    expect(fs.readFileSync(saveOf(HANDLE)).equals(fs.readFileSync(saveOf(HANDLE) + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame, () => {
      if (fs.existsSync(CHEAT_TARGET)) {
        fs.rmSync(CHEAT_TARGET)
        step(`cheat removed: ${CHEAT_TARGET}`)
      }
    })
  }
})
