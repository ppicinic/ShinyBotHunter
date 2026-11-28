import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { readU16, readU32, repoDir, runMacro, sendCheatHotkey, shotAll, step, toS16 } from './driver'
import { Y_ADDR } from './headbutt'
import { backendLog, CRASHED, MINUTE, settings } from './static'
import {
  assertLocalStores, cancelBot, cardWindow, clockConfig, deletePlan, launchHgss, milestoneMessages, putMilestone, putPlan, renamePlan, RUN_FROM,
  saveOf, seed, seen, setAnchorHere, shutdown, stageCopy, startFrame, startPlan, startWild, subscribe, teardown, variant, WILD,
  writeLeadExtension, type Frame, type MilestoneRow, type Plan
} from './milestones'

// #525 Q1c and #621 R8b2, R8c2: FR-34a on a saved milestone with a location; FR-13's wanted shiny parking the hunt, the shiny
// alert. `no-plan` (the default) hunts it as a Wild Encounter on its fixed card; `plan` as the one step of the plan QA Shiny
// (its default, endWhenComplete), started through Bot Control's Plan action, on the file's follows card: the shiny parks the
// step, the plan is in use while parked, and Resume carries on the same step to its milestone's message and the plan's Complete.
const VARIANT = variant(['no-plan', 'plan'] as const)
const HANDLE = 'HG-Complete'
const ROUTE32 = 'QA Route 32'
const PLAN: Plan = {
  name: 'QA Shiny', rules: [], default: { eventName: WILD, phaseName: ROUTE32, targetType: 'PhaseTarget', encounterType: 'Run', yRange: 1 },
  endWhenComplete: true
}
const IN_USE = `${PLAN.name} is in use by the hunt on bot #0; cancel that hunt first.`
const CHEAT_SOURCE = path.join(repoDir, 'ShinyBotHunter.Qa', 'Cheats', 'hgss-usa-rev0', 'wild-shiny.cht')
// BizHawk loads <ROM name>.cht from NDS\Cheats, beside NDS\SaveRAM, when the ROM opens.
const CHEAT_TARGET = path.join(path.dirname(path.dirname(saveOf(HANDLE))), 'Cheats', `${HANDLE}.cht`)
// [address, original, patched] for the two halfwords of the wild-shiny cheat, as hgss-route33.spec.ts has them.
const PATCH_SITES: [number, number, number][] = [[0x02247d90, 0x2300, 0x682b], [0x02247d98, 0xfe3f, 0xfdbb]]
// Every HG Route 32 day species (Bellsprout, Rattata, Mareep, Hoppip; Data/HGSS/milestones.json.gz, 36-land), so any shiny
// there at 12:00 is a target.
const GOAL = [69, 19, 179, 187]
// The battle menu's Run on the touch screen (CommandPreprocessor "Run"), as the bot's flee touches it.
const RUN_TOUCH = 'S.125.175.3'
const FOE = /(\S+), isShiny: (True|False)\s*$/
const ALERT = /^attachment:\/\//
const FOUND_FALSE = /^Found pokemon: False\s*$/

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
const count = (pattern: RegExp, from = 0): number => backendLog().slice(from).filter((l) => pattern.test(l)).length
const state = async (): Promise<string> => (await (await fetch('http://localhost:5000/api/bot/getDashboardState')).json())['0']?.eventState
const entryName = async (): Promise<string> => (await (await fetch('http://localhost:5000/api/bot/getDashboardState')).json())['0']?.name

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

test(`HGSS milestone complete (${VARIANT}): a wanted shiny parks the hunt, the milestone emptied, Resume tells once and completes`, async () => {
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
    const pages: Record<string, Page> = { admin }
    // QA Route 32 at Route 32 · grass, ticked for this file: the four day species at `amount`, from the location's seed.
    const seeded = await seed('HGSS', '36-land', 'None')
    expect(seeded.status, 'the seed of 36-land').toBe(200)
    const rows = (amount: number): MilestoneRow[] => (seeded.json.rows as MilestoneRow[]).filter((r) => GOAL.includes(r.speciesId))
      .map(({ name, speciesId, formCode, requireSpecificForm }) => ({ name, speciesId, amount, formCode, requireSpecificForm }))
    expect(rows(1).map((r) => r.speciesId).sort(), 'one seed row for each day species').toEqual([...GOAL].sort())
    const milestone = (amount: number): ReturnType<typeof putMilestone> =>
      putMilestone({ game: 'HGSS', name: ROUTE32, location: '36-land', gameFiles: [HANDLE], archived: false, pokemon: rows(amount) })
    expect((await milestone(1)).status, `${ROUTE32} at 1`).toBe(200)
    if (VARIANT === 'plan') expect((await putPlan('HGSS', PLAN)).status, 'PUT plan').toBe(204)
    // The card before the launch, so no new window takes the foreground from EmuHawk before the cheat hotkey: the milestone's
    // fixed card, or for the plan the file's follows card.
    const followsHunt = VARIANT === 'plan'
    expect((await subscribe({ game: 'HGSS', phase: ROUTE32, gameName: HANDLE, followsHunt })).status, `subscribe ${ROUTE32} for ${HANDLE}`).toBe(200)
    pages.card = await cardWindow(frame.app!, followsHunt ? `HGSS — ${HANDLE}` : `HGSS — ${ROUTE32} (${HANDLE})`)
    const card: Locator = await launchHgss(admin, HANDLE, { clock: true })
    // The patch must not be active before the hotkey; the sites hold either value once the overlay is loaded.
    for (const [addr, original, patched] of PATCH_SITES) {
      await expect.poll(async () => [original, patched].includes(await readU16(addr)), { timeout: MINUTE, intervals: [500] }).toBe(true)
      expect(await readU16(addr), `patch site ${addr.toString(16)} active at launch`).toBe(original)
    }
    await writeLeadExtension({ hp: 30 })

    // 1. The hunt from where the bot stands (anchor set here on grass: the walk south to the generated anchor meets a
    // story trigger); two encounters there, then the patch armed.
    const set = await setAnchorHere(0, ROUTE32, 0, 1)
    expect(set.status, `Set anchor here: ${set.text}`).toBe(200)
    const runStart = VARIANT === 'plan' ? await startPlan(card, PLAN.name) : await startWild(card, { phase: ROUTE32, mode: 'Run', yRange: 1 })
    await seen(RUN_FROM, 1, 15 * MINUTE)
    const runAt = backendLog().findIndex((l) => RUN_FROM.test(l))
    await expect.poll(() => count(FOE, runAt), { timeout: 10 * MINUTE, intervals: [2000] }).toBeGreaterThanOrEqual(2)
    const armAt = new Date().toISOString()
    const foesBefore = count(FOE)
    const hotkey = sendCheatHotkey()
    expect(hotkey.sent, `EmuHawk was not the foreground window when the hotkey was sent; the foreground was ${hotkey.foreground}`).toBe(true)
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

    // 3. The plan: the parked step named on the card, and the plan in use while parked.
    if (VARIANT === 'plan') {
      expect(await entryName(), 'Bot Control\'s entry while parked').toBe(`Plan ${PLAN.name}: ${ROUTE32}`)
      await expect(card, 'the card while parked').toContainText(`Plan ${PLAN.name}: ${ROUTE32}`)
      for (const res of [await deletePlan('HGSS', PLAN.name), await renamePlan('HGSS', PLAN.name, `${PLAN.name} 2`), await putPlan('HGSS', PLAN)]) {
        expect([res.status, res.text], 'the plan was not refused while parked').toEqual([400, IN_USE])
      }
    }

    // 4. The scenario ends the battle while the bot waits; the milestone emptied; Resume.
    await fleeWhileParked()
    await sleep(3000)
    expect((await milestone(0)).status, `${ROUTE32} emptied`).toBe(200)
    const resumeStart = new Date().toISOString()
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step('Resume run')
    await seen(FOUND_FALSE, 1, 2 * MINUTE)

    // 5. Exactly one milestone message, then the walk home and Complete: the step's hunt, and with it the plan, whose one
    // shiny-hunt step is done.
    const expected = `${ROUTE32} is complete on ${HANDLE}.`
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
