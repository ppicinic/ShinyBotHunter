import { spawnSync, type ChildProcess } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type ElectronApplication, type Page } from '@playwright/test'
import {
  dashboardDir, emuHawkRunning, killEmuHawk, killTree, launchDashboard, readMemory, readU32, repoDir, runDir, shotAll,
  startBackend, startEmulatorRecorder, step, stopBackend, stopRecorder, toS16, windowByTitle, writeU8
} from './driver'
import { decodeMon, MON_SIZE, rewritePid, shinyPidKeepingLow, type Mon } from './egg-pid'

// A throwaway copy of hgss.qa-primo (ROM and save), made here and deleted in the finally: the bot saves on it.
const SOURCE_ROM = 'SS-Slugma-Room'
const ROM = 'SS-Slugma-Shiny'
const ROMS = 'C:\\ROMs'
const SAVERAM = path.join('C:\\BizHawk-2.11', 'NDS', 'SaveRAM')
const SOURCE_SAVE = path.join(SAVERAM, `${SOURCE_ROM}.SaveRAM`)
const ROM_COPY = path.join(ROMS, `${ROM}.nds`)
const SAVE = path.join(SAVERAM, `${ROM}.SaveRAM`)
const ANCHOR_ADDR = 0x21d4158
const Y_ADDR = 0x21da6fc
// The party, from the anchor: count at -0x23F44, records of 0xEC bytes from -0x23F40 (the spike's egg.py).
const PARTY_COUNT_OFFSET = -0x23f44
const PARTY_OFFSET = -0x23f40
const EGG_SLOT = 5
const TID = 60647
const SID = 55034
const PSQL = 'C:\\Tools\\pgsql\\bin\\psql.exe'
const MONGOSH = path.join(process.env.LOCALAPPDATA!, 'Programs', 'mongosh', 'mongosh.exe')
const API = 'http://localhost:5000/api'
const ACTION = 'HGSS Primo Egg'
const PHASE = 'Primo Slugma'
const MINUTE = 60 * 1000

const sha256 = (file: string): string => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const backendLog = (): string[] => fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
const backendLines = (pattern: RegExp): string[] => backendLog().filter((l) => pattern.test(l))
const crashed = (): string[] => backendLines(/\[BotService\] Event '.*' crashed/)
const hex8 = (pid: number): string => (pid >>> 0).toString(16).toUpperCase().padStart(8, '0')
const show = (m: Mon): string => JSON.stringify({ ...m, pid: hex8(m.pid), ivWord: hex8(m.ivWord) })
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
const caughtFilter = `{ Phase: '${PHASE}', GameFileName: '${ROM}' }`

test('HGSS Primo Egg shiny: the egg\'s PID rewritten in party RAM, the hunt walks and hatches it, alert, catch, Complete, save', async () => {
  test.setTimeout(40 * MINUTE)
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  // Before the try, so the finally block never kills an emulator or deletes a copy this run did not make.
  expect(emuHawkRunning(), 'an EmuHawk is already running; close it first').toBe(false)
  expect(fs.existsSync(path.join(ROMS, `${SOURCE_ROM}.nds`)), `missing ${SOURCE_ROM}.nds in ${ROMS}`).toBe(true)
  expect(fs.existsSync(SOURCE_SAVE), `missing ${SOURCE_SAVE}`).toBe(true)
  const { pg, discordToken, channel, mongoUri } = settings()
  try {
    // 1. The throwaway copy, after any left by an earlier run.
    for (const stale of [ROM_COPY, SAVE, SAVE + '.bak']) {
      if (fs.existsSync(stale)) {
        fs.rmSync(stale)
        step(`deleted stale ${stale}`)
      }
    }
    fs.copyFileSync(path.join(ROMS, `${SOURCE_ROM}.nds`), ROM_COPY)
    fs.copyFileSync(SOURCE_SAVE, SAVE)
    step(`copy made: ${ROM}.nds, ${ROM}.SaveRAM sha256 ${sha256(SAVE)} (source ${sha256(SOURCE_SAVE)})`)

    // 2. The catches already recorded for the copy, since a CaughtPokemon document has no timestamp.
    const caughtBefore = mongo(mongoUri, `print(db.CaughtPokemon.countDocuments(${caughtFilter}))`) as number
    step(`${caughtBefore} matching CaughtPokemon documents before the run`)

    // 3. Build the dashboard, then backend, dashboard, Bot Control.
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), build.stdout + build.stderr)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)
    backend = await startBackend()
    step('backend up on localhost:5000')
    app = await launchDashboard(path.join(runDir, 'videos'))
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const pages: Record<string, Page> = { admin }
    await admin.getByRole('button', { name: 'Bot Control', exact: true }).click()

    // 4. Launch Bot + Continue on the copy, then Run.
    const launch = admin.getByTestId('top-level-action-Launch Bot + Continue')
    await launch.locator('select').selectOption(ROM)
    await launch.getByRole('button', { name: 'Start', exact: true }).click()
    const card = admin.getByTestId('emulator-card-0')
    const picker = card.getByRole('button', { name: ACTION, exact: true })
    await expect(picker).toBeVisible({ timeout: 5 * 60 * 1000 })
    await expect(launch.getByRole('button', { name: 'Start', exact: true })).toBeVisible({ timeout: 5 * 60 * 1000 })
    startEmulatorRecorder()
    await expect.poll(async () => toS16(await readU32(Y_ADDR)), { timeout: 60_000, intervals: [500] }).toBeGreaterThan(0)
    await shotAll('01-launched', pages)
    await picker.click()
    await card.getByTestId('param-PhaseName').locator('select').selectOption(PHASE)
    const runStart = new Date().toISOString()
    await card.getByRole('button', { name: 'Run', exact: true }).click()
    await expect(picker).toHaveCount(0, { timeout: 30_000 })
    step(`HGSS Primo Egg started at ${runStart}`)

    // 5. The rewrite window: after "outcome 5" (message 18, before GiveEgg) and before the event's party read.
    const outcomeDeadline = Date.now() + 3 * MINUTE
    let outcome: string | undefined
    while (!(outcome = backendLines(/\[PrimoEgg\] outcome/)[0])) {
      expect(crashed(), 'the event crashed; see backend.log').toEqual([])
      if (Date.now() > outcomeDeadline) throw new Error('no "[PrimoEgg] outcome" line within 3 min')
      await sleep(100)
    }
    const windowStart = Date.now()
    step(`logged: ${outcome.trim()}`)
    expect(outcome, 'not outcome 5').toContain('[PrimoEgg] outcome 5 (party count)')

    // 5a. GiveEgg has run once the count reads 6 and slot 6 decodes as a Slugma egg.
    const anchor = await readU32(ANCHOR_ADDR)
    const slotAddr = anchor + PARTY_OFFSET + EGG_SLOT * MON_SIZE
    await expect.poll(async () => (await readMemory(anchor + PARTY_COUNT_OFFSET, 1))[0], { timeout: 10_000, intervals: [50] }).toBe(6)
    let old: Buffer = Buffer.alloc(0)
    await expect.poll(async () => {
      old = await readMemory(slotAddr, MON_SIZE)
      const m = decodeMon(old)
      return m.checksumValid && m.species === 218 && m.isEgg
    }, { timeout: 10_000, intervals: [50] }).toBe(true)
    const egg = decodeMon(old)
    step(`slot 6 as given: ${show(egg)}`)
    expect([egg.tid, egg.sid], 'the egg\'s own TID/SID').toEqual([TID, SID])

    // 5b. The shiny record, checked before anything is written.
    const newPid = shinyPidKeepingLow(egg.pid, egg.tid, egg.sid)
    const rewritten = rewritePid(old, newPid)
    const check = decodeMon(rewritten)
    step(`slot 6 rewritten: ${show(check)}`)
    expect([check.checksumValid, check.isShiny, check.species, check.isEgg, check.ivWord], 'the rewritten record')
      .toEqual([true, true, 218, true, egg.ivWord])

    // 5c. Only the differing bytes, all at once: ApiHawk drains its whole queue in one frame.
    const differing = [...rewritten.keys()].filter((i) => rewritten[i] !== old[i])
    await Promise.all(differing.map((i) => writeU8(slotAddr + i, rewritten[i])))
    const after = await readMemory(slotAddr, MON_SIZE)
    step(`PID ${hex8(egg.pid)} -> ${hex8(newPid)}: ${differing.length} bytes written ${Date.now() - windowStart} ms after the outcome line`)
    expect(after.equals(rewritten), 'slot 6 does not read back as written').toBe(true)

    // 5d. The event read the egg after the rewrite.
    const eggDeadline = Date.now() + MINUTE
    let eggLine: string | undefined
    while (!(eggLine = backendLines(/\[PrimoEgg\] egg in slot/)[0])) {
      expect(crashed(), 'the event crashed; see backend.log').toEqual([])
      if (Date.now() > eggDeadline) throw new Error('no "[PrimoEgg] egg in slot" line within 1 min of the rewrite')
      await sleep(250)
    }
    step(`logged: ${eggLine.trim()}`)
    if (eggLine.includes(`pid=${hex8(egg.pid)}`)) throw new Error('the bot read the egg before the rewrite landed; rerun')
    expect(eggLine, 'the event did not read the rewritten egg').toContain(`[PrimoEgg] egg in slot 6, pid=${hex8(newPid)}`)
    await shotAll('02-rewritten', pages)

    // 6. The hunt walks and hatches the shiny egg.
    const hatchDeadline = Date.now() + 25 * MINUTE
    while (backendLines(/\[EggHatch\] slot \d+: /).length === 0) {
      expect(crashed(), 'the event crashed; see backend.log').toEqual([])
      if (Date.now() > hatchDeadline) throw new Error('no hatch line within 25 min of the egg')
      await sleep(2000)
    }
    const hatchLine = backendLines(/\[EggHatch\] slot \d+: /)[0].trim()
    step(`logged: ${hatchLine}`)
    expect(hatchLine, 'not the shiny Slugma').toContain(`[EggHatch] slot 6: Slugma (species 218), pid ${hex8(newPid)}`)
    await shotAll('03-hatched', pages)

    // 7. The event saves and ends Complete.
    await expect.poll(async () => (await (await fetch(`${API}/bot/getDashboardState`)).json())['0']?.eventState,
      { message: 'the event is not Complete', timeout: 2 * MINUTE, intervals: [2000] }).toBe('Complete')
    for (const line of backendLines(/\[PrimoEgg\] (Hatched|Shiny)/)) step(`backend: ${line.trim()}`)
    await shotAll('04-complete', pages)

    // 8. Discord: the shiny channel's latest message is the Slugma alert, sent after the Run.
    let title = 'none'
    let sentAt = ''
    const discordDeadline = Date.now() + 60_000
    while (Date.now() < discordDeadline) {
      const res = await fetch(`https://discord.com/api/v10/channels/${channel}/messages?limit=1`, { headers: { Authorization: `Bot ${discordToken}` } })
      if (res.ok) {
        const [latest] = await res.json()
        title = latest?.embeds?.[0]?.title ?? latest?.content ?? 'none'
        sentAt = latest?.timestamp ?? ''
        if (title.includes('Shiny Slugma') && Date.parse(sentAt) > Date.parse(runStart)) break
      } else {
        title = `HTTP ${res.status}`
      }
      await sleep(5000)
    }
    step(`Discord latest: "${title}" at ${sentAt}`)
    expect(title, 'the latest shiny-channel message is not the Slugma alert').toContain('Shiny Slugma')
    expect(Date.parse(sentAt), 'the Slugma alert is older than the Run').toBeGreaterThan(Date.parse(runStart))

    // 9. Mongo: one new CaughtPokemon for the copy, the shiny. The insert is queued, so poll.
    const findCaught = (): any => mongo(mongoUri,
      `const d = db.CaughtPokemon.find(${caughtFilter}).toArray(); print(EJSON.stringify({ count: d.length, last: d.at(-1)?.Pokemon }, { relaxed: true }))`)
    await expect.poll(() => findCaught().count, { message: 'no new CaughtPokemon document', timeout: 60_000, intervals: [2000] })
      .toBe(caughtBefore + 1)
    const caught = findCaught()
    step(`CaughtPokemon: ${caught.count} matching; newest PID ${hex8(Number(caught.last?.PID))}, species ${caught.last?.Species}, IsShiny ${caught.last?.IsShiny}`)
    expect([hex8(Number(caught.last.PID)), caught.last.Species, caught.last.IsShiny], 'the CaughtPokemon').toEqual([hex8(newPid), 218, true])

    // 10. One Cancel removes the finished bot; its EmuHawk closes and flushes SaveRAM. Then the documented shutdown.
    await card.getByRole('button', { name: 'Cancel', exact: true }).click()
    await card.getByRole('button', { name: 'Yes', exact: true }).click()
    await expect(card).toHaveCount(0, { timeout: 60_000 })
    await stopRecorder()
    await expect.poll(emuHawkRunning, { timeout: 60_000 }).toBe(false)
    step('bot cancelled; no EmuHawk running')
    await shotAll('05-cancelled', pages)
    const video = admin.video()
    expect(video, 'no video for the Admin window').not.toBeNull()
    await app.close()
    app = null
    await video!.saveAs(path.join(runDir, 'admin.webm'))
    step('admin.webm saved')
    const exitCode = await stopBackend(backend)
    expect(exitCode, 'backend did not exit within 60 s of "end"').not.toBeNull()
    backend = null
    step(`backend exited with ${exitCode}`)

    // 11. Postgres: one row since the Run, the shiny on the copy.
    const rows = psql(pg, `SELECT "PID","Species","IsShiny","GameFileName" FROM encounters WHERE "Milestone"='${PHASE}' AND "Timestamp" >= '${runStart}'`)
    step(`encounter rows since the Run: ${rows.join(' ; ')}`)
    expect(rows.length, 'not exactly one encounter row').toBe(1)
    const [pid, species, shiny, file] = rows[0].split('|')
    expect([hex8(Number(pid)), species, shiny, file], 'the encounter row is not the shiny Slugma').toEqual([hex8(newPid), '218', 't', ROM])

    // 12. The in-game save on the copy: a record starting with the new PID, a hatched Slugma, checksum valid.
    const saved = fs.readFileSync(SAVE)
    expect(saved.equals(fs.readFileSync(SOURCE_SAVE)), 'the copy\'s SaveRAM was not written').toBe(false)
    const found: Mon[] = []
    for (let at = 0; at + MON_SIZE <= saved.length; at += 4) {
      if (saved.readUInt32LE(at) !== newPid) continue
      const m = decodeMon(saved.subarray(at, at + MON_SIZE))
      step(`save: record at 0x${at.toString(16)}: ${show(m)}`)
      if (m.checksumValid && m.species === 218 && !m.isEgg) found.push(m)
    }
    expect(found.length, 'no hatched Slugma with the new PID in the copy\'s SaveRAM').toBeGreaterThan(0)
  } finally {
    // The backend first, so it cannot drive the emulator further; then keep the copy's save as evidence and delete the copy.
    if (backend && backend.exitCode === null) killTree(backend.pid!)
    await stopRecorder()
    if (app) await app.close().catch(() => undefined)
    if (emuHawkRunning()) killEmuHawk()
    for (const file of [SAVE, SAVE + '.bak']) {
      if (fs.existsSync(file)) fs.copyFileSync(file, path.join(runDir, path.basename(file)))
    }
    for (const file of [ROM_COPY, SAVE, SAVE + '.bak']) {
      if (fs.existsSync(file)) {
        fs.rmSync(file)
        step(`deleted ${file}`)
      }
    }
    // Never loaded by this run.
    if (fs.existsSync(SOURCE_SAVE + '.bak')) {
      const same = fs.readFileSync(SOURCE_SAVE).equals(fs.readFileSync(SOURCE_SAVE + '.bak'))
      step(`${SOURCE_ROM} SaveRAM ${same ? 'equals' : 'DIFFERS FROM'} .bak (sha256 ${sha256(SOURCE_SAVE)})`)
    }
  }
})
