import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readU16, readU32, repoDir, runDir, sendCheatHotkey,
  shotAll, startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle
} from './driver'
import {
  backendLines, backendLog, CRASHED, encounterRows, HEADBUTT_NAMES, HEADBUTT_SPECIES, MILESTONE, pgEnv, readTile, Y_ADDR
} from './headbutt'

const ROM = 'HG-Headbutt'
const BIZHAWK = 'C:\\BizHawk-2.11'
const SAVE = path.join(BIZHAWK, 'NDS', 'SaveRAM', `${ROM}.SaveRAM`)
const CHEAT_SOURCE = path.join(repoDir, 'ShinyBotHunter.Qa', 'Cheats', 'hgss-usa-rev0', 'wild-shiny.cht')
const CHEAT_TARGET = path.join(BIZHAWK, 'NDS', 'Cheats', `${ROM}.cht`)
// [address, original, patched] for the two halfwords of the wild-shiny cheat, as hgss-route33.spec.ts has them.
const PATCH_SITES: [number, number, number][] = [[0x02247d90, 0x2300, 0x682b], [0x02247d98, 0xfe3f, 0xfdbb]]
const START = '(426,464,Down)'
const API = 'http://localhost:5000/api'
const FOE = /(\S+), isShiny: (True|False)/
const TEN_MINUTES = 10 * 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// The Discord token and the shiny channel: user secrets first, then appsettings.json; the values are never logged.
function setting(key: string): string {
  const secrets = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(repoDir, 'ShinyBotHunter')], { encoding: 'utf8' }).stdout
  const secret = secrets.split(/\r?\n/).find((l) => l.startsWith(`${key} = `))
  if (secret) return secret.slice(key.length + 3).trim()
  // As text: the channel id is above Number.MAX_SAFE_INTEGER.
  const appsettings = fs.readFileSync(path.join(repoDir, 'ShinyBotHunter', 'appsettings.json'), 'utf8')
  const json = appsettings.match(new RegExp(`"${key.split(':').at(-1)}"\\s*:\\s*"?([^",\\r\\n]+)"?`))
  if (!json) throw new Error(`no ${key} in the backend user secrets or appsettings.json`)
  return json[1].trim()
}

test('HGSS Headbutt shiny: wild-shiny armed after two cycles reaches a headbutt encounter; park, alert and row', async () => {
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator this run did not start.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  const pg = pgEnv()
  const discordToken = setting('ApplicationSettings:DiscordToken')
  const channel = setting('ApplicationSettings:ShinyLogChannel')
  try {
    // 1. Build the dashboard so out/ carries the current test ids.
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)

    // 2. Preconditions.
    expect(fs.existsSync(SAVE), `missing ${SAVE}`).toBe(true)
    expect(fs.existsSync(SAVE + '.bak'), `missing ${SAVE}.bak`).toBe(true)
    step(`SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')}`)
    // The cheat file is loaded disabled; BizHawk reads it when the ROM opens.
    fs.writeFileSync(CHEAT_TARGET, fs.readFileSync(CHEAT_SOURCE, 'utf8').replace(/\r\n/g, '\n'))
    step(`cheat copied to ${CHEAT_TARGET}`)

    // 3. Backend.
    backend = await startBackend()
    step('backend up on localhost:5000')

    // 4. Dashboard and its windows. Counts come from backend.log and Postgres, so no milestone window.
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const recent = await windowByTitle(app, 'Pokemon Dashboard — Recent Encounters', 60_000)
    const pages = { admin, recent }
    step('windows attached')

    // 5. Launch the bot through the top-level action.
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const wildPicker = card.getByRole('button', { name: 'Wild Encounter', exact: true })
    const resume = card.getByText('Resume', { exact: true })
    await expect(wildPicker).toBeVisible({ timeout: 5 * 60 * 1000 })
    // The card appears before the Continue macro has run; the form's button reads "Starting..." until the launch request returns.
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await shotAll('01-launched', pages)

    // 6. The patch must not be active before the hotkey. Other overlays share these addresses while
    // the game is still loading, so wait until the site holds either the original or the patched value.
    for (const [addr, original, patched] of PATCH_SITES) {
      await expect.poll(async () => [original, patched].includes(await readU16(addr)), { timeout: 60_000, intervals: [500] }).toBe(true)
      const value = await readU16(addr)
      step(`patch site ${addr.toString(16)} = ${value.toString(16)}`)
      expect(value, 'patch active at launch').toBe(original)
    }

    // 7. The start tile, once the overworld has faded in (the position reads -1 until then).
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    const startTile = await readTile()
    step(`start tile ${startTile}`)
    expect(startTile, `${SAVE} does not stand at the tree`).toBe(START)

    // 8. Wild Encounter, ShinyTarget, Headbutt, TrainType left at None. Headbutt must be offered with no workaround.
    await wildPicker.click()
    const phases = await card.getByTestId('param-PhaseName').locator('option').allTextContents()
    expect(phases, `no "${MILESTONE}" milestone; make it in the Milestone Editor`).toContain(MILESTONE)
    await card.getByTestId('param-PhaseName').locator('select').selectOption(MILESTONE)
    await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
    const headbutt = card.getByTestId('param-EncounterType').locator('option[value="Headbutt"]')
    await expect(headbutt).toBeAttached({ timeout: 30_000 })
    const label = (await headbutt.textContent()) ?? ''
    step(`Headbutt option: "${label}", disabled ${await headbutt.isDisabled()}`)
    expect(await headbutt.isDisabled(), `Headbutt is not offered: "${label}"`).toBe(false)
    await card.getByTestId('param-EncounterType').locator('select').selectOption('Headbutt')
    const runStart = new Date().toISOString()
    const from = backendLog().length
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    step(`Wild Encounter started at ${runStart}: ${MILESTONE}, ShinyTarget, Headbutt`)
    await shotAll('02-running', pages)
    await expect(wildPicker).toHaveCount(0, { timeout: 30_000 })

    // 9. Two cycles' foes through the real loop. A crash or a park before them fails the run.
    const stuck = async (): Promise<void> => {
      for (const line of backendLines(/\[Headbutt\]|\[Orchestrator\] State:|isShiny:/, from).slice(-30)) step(`backend: ${line}`)
      await shotAll('03-stuck', pages)
    }
    const foes = async (): Promise<number> => {
      const crash = backendLines(CRASHED, from)
      if (crash.length > 0) throw new Error(`the event crashed: ${crash[0]}`)
      if (await resume.isVisible()) throw new Error(`the event parked before the cheat: ${backendLines(/\[Headbutt\]/, from).at(-1)}`)
      return backendLines(FOE, from).length
    }
    try {
      // (cycles + 1) × 60 s, as the cycle watch allows.
      await expect.poll(foes, { timeout: 3 * 60_000, intervals: [1000] }).toBeGreaterThanOrEqual(2)
    } catch (e) {
      await stuck()
      throw e
    }
    for (const line of backendLines(FOE, from)) step(`backend: ${line}`)

    // 10. Arm the wild-shiny patch. EmuHawk must hold the foreground for the hotkey to land.
    const armAt = new Date().toISOString()
    expect(sendCheatHotkey(), 'EmuHawk was not the foreground window when the hotkey was sent').toBe(true)
    for (const [addr] of PATCH_SITES) step(`patch site ${addr.toString(16)} after hotkey = ${(await readU16(addr)).toString(16)}`)
    step(`armed at ${armAt}`)
    await shotAll('03-armed', pages)

    // 11. The shiny parks the bot, and the card offers Resume.
    const ended = async (): Promise<boolean> => backendLines(CRASHED, from).length > 0 || (await resume.isVisible())
    try {
      await expect.poll(ended, { timeout: TEN_MINUTES, intervals: [2000] }).toBe(true)
    } catch (e) {
      await stuck()
      throw e
    }
    await shotAll('04-resume', pages)
    expect(backendLines(CRASHED, from), 'the event crashed').toEqual([])

    // 12. The last foe is a shiny headbutt species, and the event is in Waiting.
    const lastFoe = FOE.exec(backendLines(FOE, from).at(-1)!)!
    const [name, shiny] = [lastFoe[1], lastFoe[2]]
    step(`last foe: ${name}, isShiny: ${shiny}; ${backendLines(FOE, from).length} foes`)
    expect(shiny, 'the last foe is not shiny').toBe('True')
    expect(HEADBUTT_NAMES.map((n) => n.toLowerCase()), `${name} is not a headbutt species`).toContain(name.toLowerCase())
    const state = await (await fetch(`${API}/bot/getDashboardState`)).json()
    step(`dashboard state: ${JSON.stringify(state)}`)
    expect(state['0']?.eventState, 'the event is not in Waiting').toBe('Waiting')

    // 13. Discord: the shiny channel's latest message is that foe's alert, sent after the arm.
    let title = 'none'
    let sentAt = ''
    const discordDeadline = Date.now() + 60_000
    while (Date.now() < discordDeadline) {
      const res = await fetch(`https://discord.com/api/v10/channels/${channel}/messages?limit=1`, { headers: { Authorization: `Bot ${discordToken}` } })
      if (res.ok) {
        const [latest] = await res.json()
        title = latest?.embeds?.[0]?.title ?? latest?.content ?? 'none'
        sentAt = latest?.timestamp ?? ''
        if (title.includes(`Shiny ${name} encountered`) && Date.parse(sentAt) > Date.parse(armAt)) break
      } else {
        title = `HTTP ${res.status}`
      }
      await sleep(5000)
    }
    step(`Discord latest: "${title}" at ${sentAt}`)
    expect(title, 'the latest shiny-channel message is not the alert').toContain(`Shiny ${name} encountered`)
    expect(Date.parse(sentAt), 'the alert is older than the arm').toBeGreaterThan(Date.parse(armAt))

    // 14. First cancel clears the event; the position stays and is idle again.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(wildPicker).toBeVisible({ timeout: 60_000 })
    step('event cancelled')
    await shotAll('05-event-cancelled', pages)

    // 15. Second cancel removes the bot and closes the emulator.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('06-bot-cancelled', pages)

    // 16. Nothing was saved in game.
    expect(fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak')), 'SaveRAM differs from its .bak').toBe(true)
    step(`SaveRAM equals .bak (sha256 ${sha256(SAVE)})`)

    // 17. Closing Electron finalises the videos.
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')

    // 18. The documented shutdown, so the encounters are written; the newest row is the shiny.
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)
    const newest = encounterRows(pg, ROM, runStart).at(-1)
    expect(newest, 'no row since the Start').toBeDefined()
    expect(HEADBUTT_SPECIES, `the newest row's species ${newest!.species} is not a headbutt species`).toContain(newest!.species)
    expect(newest!.shiny, 'the newest row is not shiny').toBe(true)
  } finally {
    // Failure path: leave nothing running. A forced kill does not flush SaveRAM.
    await stopRecorder()
    if (app) await app.close().catch(() => undefined)
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    if (emuHawkRunning()) killEmuHawk()
    if (fs.existsSync(CHEAT_TARGET)) {
      fs.rmSync(CHEAT_TARGET)
      step(`cheat removed: ${CHEAT_TARGET}`)
    }
    // On every outcome: a forced kill must not have changed the save either.
    if (fs.existsSync(SAVE) && fs.existsSync(SAVE + '.bak')) {
      const same = fs.readFileSync(SAVE).equals(fs.readFileSync(SAVE + '.bak'))
      step(`after teardown: SaveRAM ${same ? 'equals' : 'DIFFERS FROM'} .bak (SaveRAM sha256 ${sha256(SAVE)}, .bak sha256 ${sha256(SAVE + '.bak')})`)
    }
  }
})
