import { spawnSync } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test, type Page } from '@playwright/test'
import { shotAll, step, writeU8 } from './driver'
import { decodeMon, MON_SIZE, rewritePid, shinyPidKeepingLow, type Mon } from './egg-pid'
import {
  API, ATTEMPT, backendLines, backendLog, cancelBot, checkAttempt, hex8, installPristine, launchBot, MACROS, MINUTE, PALKIA,
  parseResult, parseRunLine, PHASE, pristineSha, psql, readPartyCount, readSlot, readTile, RESULT, ROM, RUN_LINE, runStatic,
  SAVED, saveOf, savedSha, settings, SHINY_SAVED, shotPages, shutdown, SID, slotAddr, START_TILE, startFrame, teardown, TID, waitForLine,
  waitForSummary, YES, type Frame
} from './static'

const MONGOSH = path.join(process.env.LOCALAPPDATA!, 'Programs', 'mongosh', 'mongosh.exe')
const show = (m: Mon): string => JSON.stringify({ ...m, pid: hex8(m.pid), ivWord: hex8(m.ivWord) })
const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

function mongo(uri: string, script: string): any {
  const res = spawnSync(MONGOSH, [uri, '--quiet', '--eval', script], { encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`mongosh exited with ${res.status}: ${res.stderr}`)
  return JSON.parse(res.stdout.trim())
}
const caughtFilter = `{ Phase: '${PHASE}', GameFileName: '${ROM}' }`

test('HGSS Static Encounter Palkia shiny: slot 2 rewritten at the hand-over, the shiny summary, alert, catch, Complete, save', async () => {
  test.setTimeout(25 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  const { pg, discordToken, channel, mongoUri } = settings()
  installPristine()
  try {
    // 1. The catches already recorded for the ROM, since a CaughtPokemon document has no timestamp.
    const caughtBefore = mongo(mongoUri, `print(db.CaughtPokemon.countDocuments(${caughtFilter}))`) as number
    step(`${caughtBefore} matching CaughtPokemon documents before the run`)

    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchBot(admin, ROM, 'emulator')
    expect(await readTile(), 'not the save\'s start tile').toEqual(START_TILE)
    expect(await readPartyCount(), 'the save\'s party is not one').toBe(1)
    await shotAll('01-launched', pages)
    const runStart = await runStatic(card, PALKIA)
    expect(parseRunLine(await waitForLine(RUN_LINE, 1, MINUTE)).macro, 'Palkia\'s walk').toBe(MACROS[PALKIA])
    await waitForLine(SAVED, 1, 90_000)
    await waitForLine(ATTEMPT, 1, MINUTE)
    await waitForLine(YES, 1, 2 * MINUTE)

    // 2. The hand-over: the count reads 2 and slot 2 decodes as Palkia.
    await expect.poll(readPartyCount, { message: 'the party count did not reach 2', timeout: 200_000, intervals: [50] }).toBe(2)
    const countAt = Date.now()
    let old: Buffer = Buffer.alloc(0)
    await expect.poll(async () => {
      old = await readSlot(1)
      const m = decodeMon(old)
      return m.checksumValid && m.species === 484
    }, { message: 'slot 2 did not decode as Palkia', timeout: 10_000, intervals: [50] }).toBe(true)
    const palkia = decodeMon(old)
    step(`slot 2 as given: ${show(palkia)}`)
    expect([palkia.tid, palkia.sid], 'Palkia\'s TID/SID').toEqual([TID, SID])

    // 3. The shiny record, checked before anything is written; then only the differing bytes, all at once.
    const newPid = shinyPidKeepingLow(palkia.pid, palkia.tid, palkia.sid)
    const rewritten = rewritePid(old, newPid)
    const check = decodeMon(rewritten)
    step(`slot 2 rewritten: ${show(check)}`)
    expect([check.checksumValid, check.isShiny, check.species, check.ivWord], 'the rewritten record').toEqual([true, true, 484, palkia.ivWord])
    const slot2Addr = await slotAddr(1)
    const differing = [...rewritten.keys()].filter((i) => rewritten[i] !== old[i])
    await Promise.all(differing.map((i) => writeU8(slot2Addr + i, rewritten[i])))
    const after = await readSlot(1)
    step(`PID ${hex8(palkia.pid)} -> ${hex8(newPid)}: ${differing.length} bytes written ${Date.now() - countAt} ms after count 2`)
    expect(after.equals(rewritten), 'slot 2 does not read back as written').toBe(true)

    // 4. The summary shows the shiny, and the result line carries the new PID.
    await waitForSummary(0, 240_000, '02-shiny-summary')
    await shotPages('02-shiny-summary', pages)
    const result = parseResult(await waitForLine(RESULT, 1, MINUTE))
    if (result.pid === palkia.pid) throw new Error('the bot read slot 2 before the rewrite landed; rerun')
    expect([hex8(result.pid), result.species, result.shiny], 'the result line').toEqual([hex8(newPid), 484, true])
    await waitForLine(SHINY_SAVED, 1, 2 * MINUTE)
    checkAttempt(backendLog(), 1)
    expect(backendLines(ATTEMPT).length, 'a second attempt after the shiny').toBe(1)

    // 5. The event ends Complete.
    await expect.poll(async () => (await (await fetch(`${API}/bot/getDashboardState`)).json())['0']?.eventState,
      { message: 'the event is not Complete', timeout: 2 * MINUTE, intervals: [2000] }).toBe('Complete')
    await shotAll('03-complete', pages)

    // 6. Discord: exactly one Palkia alert newer than the Run in the shiny channel.
    let alerts: string[] = []
    let status = ''
    const discordDeadline = Date.now() + MINUTE
    while (Date.now() < discordDeadline) {
      const res = await fetch(`https://discord.com/api/v10/channels/${channel}/messages?limit=20`, { headers: { Authorization: `Bot ${discordToken}` } })
      status = `HTTP ${res.status}`
      if (res.ok) {
        const messages = (await res.json()) as { timestamp: string; content?: string; embeds?: { title?: string }[] }[]
        alerts = messages.filter((m) => Date.parse(m.timestamp) > Date.parse(runStart))
          .map((m) => m.embeds?.[0]?.title ?? m.content ?? '').filter((t) => t.includes('Shiny Palkia'))
        if (alerts.length > 0) break
      }
      await sleep(5000)
    }
    step(`Discord (${status}): ${alerts.length} Palkia alerts since the Run: ${JSON.stringify(alerts)}`)
    expect(alerts.length, 'not exactly one Palkia alert since the Run').toBe(1)

    // 7. Mongo: one new CaughtPokemon, the shiny. The insert is queued, so poll.
    const findCaught = (): any => mongo(mongoUri,
      `const d = db.CaughtPokemon.find(${caughtFilter}).toArray(); print(EJSON.stringify({ count: d.length, last: d.at(-1)?.Pokemon }, { relaxed: true }))`)
    await expect.poll(() => findCaught().count, { message: 'no new CaughtPokemon document', timeout: MINUTE, intervals: [2000] })
      .toBe(caughtBefore + 1)
    const caught = findCaught()
    step(`CaughtPokemon: ${caught.count} matching; newest PID ${hex8(Number(caught.last?.PID))}, species ${caught.last?.Species}, IsShiny ${caught.last?.IsShiny}`)
    expect([hex8(Number(caught.last.PID)), caught.last.Species, caught.last.IsShiny], 'the CaughtPokemon').toEqual([hex8(newPid), 484, true])

    // 8. One Cancel removes the finished bot; its EmuHawk closes and flushes SaveRAM. Then the documented shutdown.
    await cancelBot(card)
    await shotAll('04-cancelled', pages)
    await shutdown(frame, admin)

    // 9. Postgres: one row since the Run, the shiny.
    const rows = psql(pg, `SELECT "PID","Species","IsShiny","GameFileName" FROM encounters WHERE "Milestone"='${PHASE}' AND "Timestamp" >= '${runStart}'`)
    step(`encounter rows since the Run: ${rows.join(' ; ')}`)
    expect(rows.length, 'not exactly one encounter row').toBe(1)
    const [pid, species, shiny, file] = rows[0].split('|')
    expect([hex8(Number(pid)), species, shiny, file], 'the encounter row is not the shiny Palkia').toEqual([hex8(newPid), '484', 't', ROM])

    // 10. The game saved: a record starting with the new PID, a Palkia, not an egg, checksum valid.
    expect(savedSha(), `${ROM} SaveRAM still equals the pristine copy`).not.toBe(pristineSha())
    const saved = fs.readFileSync(saveOf(ROM))
    const found: Mon[] = []
    for (let at = 0; at + MON_SIZE <= saved.length; at += 4) {
      if (saved.readUInt32LE(at) !== newPid) continue
      const m = decodeMon(saved.subarray(at, at + MON_SIZE))
      step(`save: record at 0x${at.toString(16)}: ${show(m)}`)
      if (m.checksumValid && m.species === 484 && !m.isEgg) found.push(m)
    }
    expect(found.length, 'no Palkia with the new PID in the SaveRAM').toBeGreaterThan(0)
  } finally {
    await teardown(frame)
  }
})
