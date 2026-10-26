import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test } from '@playwright/test'
import {
  dolphinRunning, emuHawkRunning, killDolphin, killEmuHawk, killTree, readU16, readU32, repoDir, runDir, shotAll,
  startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder
} from './driver'
import { craftSeed, firstBlockSeed, GSAVEBLOCK2, liveBlock, partyCount, partySlot, sections, cheatFile, type Frozen } from './jirachi-seed'

const ROM = 'Pokemon - Sapphire Version (USA, Europe) (Rev 2)'
const RESULT_ROM = `${ROM}-WISHMKR`
const BIZHAWK = 'C:\\BizHawk-2.11'
const SAVERAM = path.join(BIZHAWK, 'GBA', 'SaveRAM')
const HUNT_SAVE = path.join(SAVERAM, `${ROM}.SaveRAM`)
const BACKUP = HUNT_SAVE + '.wishmaker.bak'
const RESULT_SAVE = path.join(SAVERAM, `${RESULT_ROM}.SaveRAM`)
const RESULT_SAVE_BAK = RESULT_SAVE + '.bak'
const CHEAT = path.join(BIZHAWK, 'GBA', 'Cheats', `${ROM}.cht`)
// Beside the main checkout, found through git so a worktree resolves it too.
const MAIN_CHECKOUT = path.dirname(spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: repoDir, encoding: 'utf8' }).stdout.trim())
const PRISTINE = path.join(MAIN_CHECKOUT, '..', 'shinybothunterqasaves', 'rs', 'qa-jirachi.SaveRAM')
const PSQL = 'C:\\Tools\\pgsql\\bin\\psql.exe'
const MONGOSH = path.join(process.env.LOCALAPPDATA!, 'Programs', 'mongosh', 'mongosh.exe')
const API = 'http://localhost:5000/api'
const PHASE = 'Jirachi'
const SEED = 0xecdd
const SHINY_PID = '987ED636'
const OT_TID = 20043
const MINUTE = 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const backendLog = (): string[] => fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
const backendLines = (pattern: RegExp): string[] => backendLog().filter((l) => pattern.test(l))
const crashed = (): string[] => backendLines(/\[BotService\] Event '.*' crashed/)
const hex8 = (pid: number): string => (pid >>> 0).toString(16).toUpperCase().padStart(8, '0')
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// --- settings: user secrets first, then appsettings.json; values are never logged ---
function settings(): { pg: NodeJS.ProcessEnv; discordToken: string; channel: string; mongoUri: string } {
  const secrets = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(repoDir, 'ShinyBotHunter')], { encoding: 'utf8' }).stdout
  const appsettings = fs.readFileSync(path.join(repoDir, 'ShinyBotHunter', 'appsettings.json'), 'utf8')
  const value = (key: string): string => {
    const secret = secrets.split(/\r?\n/).find((l) => l.startsWith(`${key} = `))
    if (secret) return secret.slice(key.length + 3).trim()
    // As text: the channel id is above Number.MAX_SAFE_INTEGER.
    const json = appsettings.match(new RegExp(`"${key.split(':').at(-1)}"\\s*:\\s*"?([^",\\r\\n]+)"?`))
    if (!json) throw new Error(`no ${key} in the backend user secrets or appsettings.json`)
    return json[1].trim()
  }
  const conn = value('ConnectionStrings:DefaultConnection')
  const parts = Object.fromEntries(conn.split(';').filter((p) => p.includes('='))
    .map((p) => [p.slice(0, p.indexOf('=')).trim().toLowerCase(), p.slice(p.indexOf('=') + 1).trim()]))
  return {
    pg: {
      ...process.env, PGTZ: 'UTC', PGHOST: parts.host ?? parts.server, PGPORT: parts.port ?? '5432', PGDATABASE: parts.database,
      PGUSER: parts.username ?? parts['user id'], PGPASSWORD: parts.password
    },
    discordToken: value('ApplicationSettings:DiscordToken'),
    channel: value('ApplicationSettings:ShinyLogChannel'),
    mongoUri: `mongodb://${value('ApplicationSettings:MongoDbUrl')}/${value('ApplicationSettings:MongoDbDatabase')}`
  }
}
function psql(env: NodeJS.ProcessEnv, sql: string): string[] {
  const res = spawnSync(PSQL, ['-At', '-F', '|', '-c', sql], { env, encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`psql exited with ${res.status}: ${res.stderr}`)
  return res.stdout.split(/\r?\n/).filter((l) => l !== '')
}
function mongo(uri: string, script: string): any {
  const res = spawnSync(MONGOSH, [uri, '--quiet', '--eval', script], { encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`mongosh exited with ${res.status}: ${res.stderr}`)
  return JSON.parse(res.stdout.trim())
}
const caughtFilter = `{ Phase: '${PHASE}', GameFileName: '${RESULT_ROM}', 'Pokemon.PID': NumberLong(${parseInt(SHINY_PID, 16)}) }`

/** The frozen fields as they are in RAM now. */
async function readFields(): Promise<Frozen> {
  const play = await readU32(GSAVEBLOCK2 + 0x10)
  const berry = await readU32(GSAVEBLOCK2 + 0xa0)
  const day = berry & 0xffff
  return {
    hours: await readU16(GSAVEBLOCK2 + 0x0e), minutes: play & 0xff, seconds: (play >>> 8) & 0xff, frames: (play >>> 16) & 0xff,
    day: day >= 0x8000 ? day - 0x10000 : day, berryHours: (berry >>> 16) & 0xff, berryMinutes: berry >>> 24,
    berrySeconds: (await readU16(GSAVEBLOCK2 + 0xa4)) & 0xff
  }
}
/** Section 0 of the hunt save's first block, as the fields: what the game last saved there. */
function savedFields(save: Buffer): Frozen {
  const s = sections(save, 0).find((x) => x.id === 0)!.start
  return {
    hours: save.readUInt16LE(s + 0x0e), minutes: save[s + 0x10], seconds: save[s + 0x11], frames: save[s + 0x12],
    day: save.readInt16LE(s + 0xa0), berryHours: save[s + 0xa2], berryMinutes: save[s + 0xa3], berrySeconds: save[s + 0xa4]
  }
}
const show = (f: Frozen | null): string => JSON.stringify(f)

test('RS Wishmaker Jirachi shiny: a crafted ECDD save and a QA cheat give a real shiny; the event parks in Waiting', async () => {
  test.setTimeout(30 * MINUTE)
  let backend: ChildProcess | null = null
  // Before the try, so the finally block never kills an emulator or restores over a save this run did not touch.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(dolphinRunning(), 'a Dolphin is already running; close it first').toBe(false)
  expect(fs.existsSync(PRISTINE), `missing ${PRISTINE}`).toBe(true)
  const { pg, discordToken, channel, mongoUri } = settings()
  try {
    // 1. Stale artefacts of an earlier run.
    for (const stale of [BACKUP, RESULT_SAVE, RESULT_SAVE_BAK, CHEAT]) {
      if (fs.existsSync(stale)) {
        fs.rmSync(stale)
        step(`deleted stale ${stale}`)
      }
    }

    // 2. The crafted hunt save and the cheat that keeps its section 0 through the bot's own saves.
    const crafted = craftSeed(fs.readFileSync(PRISTINE), SEED)
    const live = sections(crafted.save, liveBlock(crafted.save))
    expect(live.every((s) => s.valid), 'the crafted live block is not valid').toBe(true)
    expect(live.find((s) => s.id === 0)!.stored, 'the crafted section 0 checksum is not ECDD').toBe(SEED)
    fs.writeFileSync(HUNT_SAVE, crafted.save)
    step(`crafted hunt save installed: fields ${show(crafted.fields)}, sha256 ${sha256(HUNT_SAVE)}`)
    fs.mkdirSync(path.dirname(CHEAT), { recursive: true })
    fs.writeFileSync(CHEAT, cheatFile(crafted.fields))
    fs.copyFileSync(CHEAT, path.join(runDir, path.basename(CHEAT)))
    step(`cheat installed at ${CHEAT}`)

    // 3. The catches already recorded, since a CaughtPokemon document has no timestamp.
    const caughtBefore = mongo(mongoUri, `print(db.CaughtPokemon.countDocuments(${caughtFilter}))`) as number
    step(`${caughtBefore} matching CaughtPokemon documents before the run`)

    // 4. Start the event through REST, as the dashboard's form does.
    backend = await startBackend()
    step('backend up on localhost:5000')
    const runStart = new Date().toISOString()
    const started = await fetch(`${API}/startTopLevelAction`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ EventName: 'Wishmaker Jirachi Hunt', GameName: ROM, PhaseName: PHASE })
    })
    const startBody = await started.text()
    expect(started.status, `Start refused: ${startBody}`).toBe(200)
    expect(Object.keys(JSON.parse(startBody)), 'the event is not at position 0').toEqual(['0'])
    step(`event started at ${runStart}`)

    // 5. The cheat is active: once the save is loaded (trainer "Phil", TID 39541 at gSaveBlock2), the fields
    //    read the crafted values twice, 1.5 s apart, so a ticking play time cannot pass.
    const loadDeadline = Date.now() + 3 * MINUTE
    let loaded = false
    while (!loaded) {
      expect(crashed(), 'the event crashed; see backend.log').toEqual([])
      if (Date.now() > loadDeadline) throw new Error('gSaveBlock2 did not read "Phil" / 39541 within 3 min')
      try {
        loaded = (await readU32(GSAVEBLOCK2)) === 0xe0dddcca && (await readU16(GSAVEBLOCK2 + 0x0a)) === 39541
      } catch {
        // EmuHawk is still coming up.
      }
      if (!loaded) await sleep(1000)
    }
    step(`gSaveBlock2 at ${GSAVEBLOCK2.toString(16)} verified: name Phil, TID 39541`)
    const first = await readFields()
    await sleep(1500)
    const second = await readFields()
    step(`frozen fields read ${show(first)} then ${show(second)}`)
    expect(first, 'the cheat is not active at load').toEqual(crafted.fields)
    expect(second, 'the cheat is not holding the fields').toEqual(crafted.fields)

    // 6. Hold point: the first cycle's seed. Anything but ECDD stops here; the finally tears down, no second cycle.
    let lastRead: Frozen | null = second
    const seedDeadline = Date.now() + 5 * MINUTE
    let seedLine: string | undefined
    while (!(seedLine = backendLines(/\[Wishmaker\] Seed [0-9A-F]{4} predicts/)[0])) {
      expect(crashed(), 'the event crashed; see backend.log').toEqual([])
      if (Date.now() > seedDeadline) throw new Error(`no Seed line within 5 min; last fields read ${show(lastRead)}`)
      try {
        lastRead = await readFields()
      } catch {
        // EmuHawk closed after the save.
      }
      await sleep(2000)
    }
    step(`logged: ${seedLine.trim()}`)
    const seed = seedLine.match(/Seed ([0-9A-F]{4})/)![1]
    if (parseInt(seed, 16) !== SEED) {
      throw new Error(`hold point: seed ${seed}, not ECDD. Crafted ${show(crafted.fields)}; last RAM read ${show(lastRead)}; ` +
        `the hunt save's first-block section 0 ${show(savedFields(fs.readFileSync(HUNT_SAVE)))}`)
    }
    expect(seedLine, 'ECDD does not predict 987ED636').toContain(`predicts PID ${SHINY_PID}`)

    // 7. The shiny and the park, then the log order.
    const parkDeadline = Date.now() + 8 * MINUTE
    while (backendLines(new RegExp(`\\[Wishmaker\\] SHINY Jirachi PID ${SHINY_PID}`)).length === 0) {
      expect(crashed(), 'the event crashed; see backend.log').toEqual([])
      if (Date.now() > parkDeadline) throw new Error('no SHINY park line within 8 min of the seed')
      await sleep(2000)
    }
    const log = backendLog()
    const at = (pattern: RegExp): number => log.findIndex((l) => pattern.test(l))
    const order = [
      at(new RegExp(`Seed ECDD predicts PID ${SHINY_PID}`)), at(/Bonus Disc: power prompt/), at(/\[Wishmaker\] summary on view/),
      at(new RegExp(`predicted ${SHINY_PID} got ${SHINY_PID} SHINY`)), at(new RegExp(`SHINY Jirachi PID ${SHINY_PID}`))
    ]
    step(`log order (line numbers): ${order.join(', ')}`)
    expect(order.every((i) => i >= 0), 'a line of the shiny path is missing').toBe(true)
    expect([...order].sort((a, b) => a - b), 'the shiny path is out of order').toEqual(order)

    // 8. Waiting.
    const state = await (await fetch(`${API}/bot/getDashboardState`)).json()
    step(`dashboard state: ${JSON.stringify(state)}`)
    expect(state['0']?.eventState, 'the event is not in Waiting').toBe('Waiting')

    // 9. One EmuHawk, on the result ROM, and no Dolphin.
    expect(dolphinRunning(), 'Dolphin is running at the park').toBe(false)
    const cim = spawnSync('powershell', ['-NoProfile', '-Command',
      "Get-CimInstance Win32_Process -Filter \"Name='EmuHawk.exe'\" | ForEach-Object { $_.CommandLine }"], { encoding: 'utf8' })
    const emuHawks = cim.stdout.split(/\r?\n/).filter((l) => l.trim() !== '')
    step(`EmuHawk command lines: ${emuHawks.join(' | ')}`)
    expect(emuHawks.length, 'not exactly one EmuHawk at the park').toBe(1)
    expect(emuHawks[0], 'the parked EmuHawk is not on the result ROM').toContain(`${RESULT_ROM}.gba`)

    // 10. Evidence of the park at the summary.
    startEmulatorRecorder()
    await sleep(3000)
    await shotAll('01-parked', {})
    await stopRecorder()

    // 11. The saves: Jirachi in slot 2 of the result, one member in the hunt save, whose first block is ECDD.
    const result = fs.readFileSync(RESULT_SAVE)
    const slot = partySlot(result, 1)
    step(`result save: party ${partyCount(result)}, slot 2 PID ${hex8(slot.pid)} OT ${slot.tid}/${slot.sid}`)
    expect(partyCount(result), 'the result save party is not 2').toBe(2)
    expect(hex8(slot.pid), 'slot 2 is not the shiny Jirachi').toBe(SHINY_PID)
    expect([slot.tid, slot.sid], 'slot 2 OT is not WISHMKR 20043/0').toEqual([OT_TID, 0])
    const hunt = fs.readFileSync(HUNT_SAVE)
    step(`hunt save: party ${partyCount(hunt)}, first-block seed ${firstBlockSeed(hunt).toString(16).toUpperCase()}`)
    expect(partyCount(hunt), 'the hunt save party is not 1').toBe(1)
    expect(firstBlockSeed(hunt), 'the hunt save first block is not ECDD after the bot saves').toBe(SEED)

    // 12. Mongo: one new CaughtPokemon for the shiny.
    const caught = mongo(mongoUri,
      `const d = db.CaughtPokemon.find(${caughtFilter}).toArray(); print(EJSON.stringify({ count: d.length, last: d.at(-1)?.Pokemon }, { relaxed: true }))`)
    step(`CaughtPokemon: ${caught.count} matching; newest species ${caught.last?.Species}, IsShiny ${caught.last?.IsShiny}`)
    expect(caught.count, 'no new CaughtPokemon document').toBe(caughtBefore + 1)
    expect(caught.last.Species, 'the CaughtPokemon is not Jirachi').toBe(385)
    expect(caught.last.IsShiny, 'the CaughtPokemon is not shiny').toBe(true)

    // 13. Discord: the shiny channel's latest message is the Jirachi alert, sent after the Start.
    let title = 'none'
    let sentAt = ''
    const discordDeadline = Date.now() + 60_000
    while (Date.now() < discordDeadline) {
      const res = await fetch(`https://discord.com/api/v10/channels/${channel}/messages?limit=1`, { headers: { Authorization: `Bot ${discordToken}` } })
      if (res.ok) {
        const [latest] = await res.json()
        title = latest?.embeds?.[0]?.title ?? latest?.content ?? 'none'
        sentAt = latest?.timestamp ?? ''
        if (title.includes('Shiny Jirachi') && Date.parse(sentAt) > Date.parse(runStart)) break
      } else {
        title = `HTTP ${res.status}`
      }
      await sleep(5000)
    }
    step(`Discord latest: "${title}" at ${sentAt}`)
    expect(title, 'the latest shiny-channel message is not the Jirachi alert').toContain('Shiny Jirachi')
    expect(Date.parse(sentAt), 'the Jirachi alert is older than the Start').toBeGreaterThan(Date.parse(runStart))

    // 14. Cancel once (a second cancel of a removed position is a 500); nothing may be left running.
    const cancel = await fetch(`${API}/bot/cancelAction`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '0' })
    expect(cancel.status, 'Cancel failed').toBe(200)
    await expect.poll(async () => Object.keys(await (await fetch(`${API}/bot/getDashboardState`)).json()).length, { timeout: 60_000 }).toBe(0)
    await expect.poll(() => emuHawkRunning() || dolphinRunning(), { timeout: 60_000 }).toBe(false)
    step('cancelled; no event, no EmuHawk, no Dolphin')

    // 15. The documented shutdown, so the encounter is written, then Postgres.
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)
    const rows = psql(pg, `SELECT "PID","Species","IsShiny","GameFileName" FROM encounters WHERE "Milestone"='${PHASE}' AND "Timestamp" >= '${runStart}'`)
    step(`encounter rows since the Start: ${rows.join(' ; ')}`)
    expect(rows.length, 'not exactly one encounter row').toBe(1)
    const [pid, species, shiny, file] = rows[0].split('|')
    expect([hex8(Number(pid)), species, shiny, file], 'the encounter row is not the shiny Jirachi').toEqual([SHINY_PID, '385', 't', ROM])
  } finally {
    // The backend first, so it cannot launch another emulator; then the cheat goes, the end state is kept as
    // evidence, the bot's artefacts are deleted and the pristine hunt save goes back.
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    await stopRecorder()
    if (emuHawkRunning()) killEmuHawk()
    if (dolphinRunning()) killDolphin()
    if (fs.existsSync(CHEAT)) {
      fs.rmSync(CHEAT)
      step(`cheat removed: ${CHEAT}`)
    }
    for (const file of [HUNT_SAVE, BACKUP, RESULT_SAVE, RESULT_SAVE_BAK]) {
      if (fs.existsSync(file)) fs.copyFileSync(file, path.join(runDir, path.basename(file)))
    }
    for (const file of [BACKUP, RESULT_SAVE, RESULT_SAVE_BAK]) {
      if (fs.existsSync(file)) fs.rmSync(file)
    }
    fs.copyFileSync(PRISTINE, HUNT_SAVE)
    step(`hunt save restored: sha256 ${sha256(HUNT_SAVE)}, pristine sha256 ${sha256(PRISTINE)}`)
    expect(sha256(HUNT_SAVE), 'the restored hunt save differs from the pristine copy').toBe(sha256(PRISTINE))
  }
})
