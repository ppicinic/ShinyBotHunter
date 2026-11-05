// The pieces the hgss-headbutt scenarios share (#228): the FR-11 line, the cycle watch, the tile and facing read, and
// the encounters rows. The FR-11 regex and the watch change together when the line changes, so they live here once;
// the HGSS frame (launch, cancel, teardown) stays copied in each spec, as the other specs do.
import { spawnSync } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import type { Locator } from '@playwright/test'
import { readU16, readU32, repoDir, runDir, step, toS16 } from './driver'

export const X_ADDR = 0x21da6f4
export const Y_ADDR = 0x21da6fc
// HgssOverworldReader.GetFacingAsync: the byte at [Anchor] + FacingOffset, a Direction (Up 0, Down 1, Left 2, Right 3).
const ANCHOR_ADDR = 0x21d4158
const FACING_OFFSET = 0x1dc4
const FACINGS = ['Up', 'Down', 'Left', 'Right']
// Azalea's headbutt trees give Spearow, Heracross and Aipom.
export const HEADBUTT_SPECIES = ['21', '214', '190']
export const HEADBUTT_NAMES = ['Spearow', 'Heracross', 'Aipom']
const PSQL = 'C:\\Tools\\pgsql\\bin\\psql.exe'

/**
 * The offered milestone whose Pokémon include all three of Azalea's headbutt species, found by species, not by name,
 * so any dev milestone for the tree will do. Fails if there is none: make one in the Milestone Editor.
 */
export async function headbuttMilestone(card: Locator): Promise<string> {
  const names = (await card.getByTestId('param-PhaseName').locator('option')
    .evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value))).filter((v) => v !== '')
  for (const name of names) {
    const res = await fetch(`http://localhost:5000/api/phaseinfo/HGSS/${encodeURIComponent(name)}`)
    if (!res.ok) continue
    const ids = ((await res.json()).phaseInfo.pokemon as { speciesId: number }[]).map((p) => String(p.speciesId))
    if (HEADBUTT_SPECIES.every((s) => ids.includes(s))) return name
  }
  throw new Error(`no HGSS milestone with ${HEADBUTT_NAMES.join(', ')} among ${names.join(', ')}; make one in the Milestone Editor`)
}

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))
export const backendLog = (): string[] => fs.readFileSync(path.join(runDir, 'backend.log'), 'utf8').split(/\r?\n/)
export const backendLines = (pattern: RegExp, from = 0): string[] => backendLog().slice(from).filter((l) => pattern.test(l))
export const CRASHED = /\[BotService\] Event '.*' crashed/

/** The player's tile and facing as "(x,y,Facing)", the format of FR-11's built-from tile. */
export async function readTile(): Promise<string> {
  const x = toS16(await readU32(X_ADDR))
  const y = toS16(await readU32(Y_ADDR))
  const facing = (await readU16((await readU32(ANCHOR_ADDR)) + FACING_OFFSET)) & 0xff
  return `(${x},${y},${FACINGS[facing] ?? facing})`
}

// FR-11, one line per cycle once its script has ended. The return is the one made before this cycle's A, so a
// return shows on the next cycle's line; built and rebuilt carry the tile the macro was built from.
const FR11 = /\[Headbutt\] start=script (\d+) presses=(\d+) end=(battle|nothing) return=(none|cached|(?:re)?built from \(-?\d+,-?\d+,\w+\))/
export type Fr11 = { script: number; presses: number; end: string; ret: string }
export function parseFr11(line: string): Fr11 | null {
  const m = FR11.exec(line)
  return m ? { script: Number(m[1]), presses: Number(m[2]), end: m[3], ret: m[4] } : null
}

export type Cycle = { aTile: string; fr11?: Fr11 }

/**
 * Watches backend.log from line `from` every 250 ms until `lines` FR-11 lines have been logged, and returns the cycles.
 * A cycle starts at its first `[GameState] the headbutt script:` line, and the tile and facing are read then: that is
 * the A's tile, since the player does not move until the swap, after the Yes and "used Headbutt.". A cycle whose start
 * and FR-11 line arrive in the same poll keeps "not read", which fails the tile assert. Fails at once on a crash, a
 * cache miss, a park (Resume on the card) or after `lines` × 60 s.
 */
export async function watchCycles(from: number, lines: number, resume: Locator): Promise<Cycle[]> {
  const cycles: Cycle[] = []
  let scanned = from
  const deadline = Date.now() + lines * 60_000
  while (true) {
    // The last element is a partial line, or the empty string after the final newline; it is read on a later poll.
    const log = backendLog()
    const fresh = log.slice(scanned, log.length - 1)
    scanned = Math.max(scanned, log.length - 1)
    let readNow = false
    for (const line of fresh) {
      if (CRASHED.test(line)) throw new Error(`the event crashed: ${line}`)
      if (line.includes('[Headbutt] Cache miss at')) throw new Error(`a cache miss: ${line}`)
      const current = cycles.at(-1)
      if (line.includes('[GameState] the headbutt script:') && (!current || current.fr11)) {
        cycles.push({ aTile: 'not read' })
        readNow = true
      }
      const fr11 = parseFr11(line)
      if (fr11) {
        const cycle = cycles.at(-1)
        if (!cycle || cycle.fr11) throw new Error(`an FR-11 line with no headbutt script line before it: ${line}`)
        cycle.fr11 = fr11
        readNow = false
        step(`cycle ${cycles.length}: A at ${cycle.aTile}, start=script ${fr11.script} presses=${fr11.presses} end=${fr11.end} return=${fr11.ret}`)
      }
    }
    if (readNow) cycles.at(-1)!.aTile = await readTile()
    if (cycles.filter((c) => c.fr11).length >= lines) return cycles
    if (await resume.isVisible()) throw new Error(`the event parked: ${backendLines(/\[Headbutt\]/, from).at(-1) ?? 'no [Headbutt] line'}`)
    if (Date.now() > deadline) throw new Error(`${cycles.filter((c) => c.fr11).length} of ${lines} FR-11 lines within ${lines} min`)
    await sleep(250)
  }
}

// --- Postgres through psql, with the backend's own connection string; its values are never logged ---
export function pgEnv(): NodeJS.ProcessEnv {
  const secrets = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(repoDir, 'ShinyBotHunter')], { encoding: 'utf8' }).stdout
  const prefix = 'ConnectionStrings:DefaultConnection = '
  const line = secrets.split(/\r?\n/).find((l) => l.startsWith(prefix))
  if (!line) throw new Error('no ConnectionStrings:DefaultConnection in the backend user secrets')
  const parts = Object.fromEntries(line.slice(prefix.length).split(';').filter((p) => p.includes('='))
    .map((p) => [p.slice(0, p.indexOf('=')).trim().toLowerCase(), p.slice(p.indexOf('=') + 1).trim()]))
  return {
    ...process.env, PGTZ: 'UTC', PGHOST: parts.host ?? parts.server, PGPORT: parts.port ?? '5432', PGDATABASE: parts.database,
    PGUSER: parts.username ?? parts['user id'], PGPASSWORD: parts.password
  }
}
function psql(env: NodeJS.ProcessEnv, sql: string): string[] {
  const res = spawnSync(PSQL, ['-At', '-F', '|', '-c', sql], { env, encoding: 'utf8' })
  if (res.status !== 0) throw new Error(`psql exited with ${res.status}: ${res.stderr}`)
  return res.stdout.split(/\r?\n/).filter((l) => l !== '')
}
// psql prints timestamptz under PGTZ=UTC as "2026-10-25 13:34:29.123+00".
const toMs = (text: string): number => Date.parse(text.replace(' ', 'T').replace(/([+-]\d\d)$/, '$1:00'))

export type Row = { species: string; level: number; shiny: boolean; at: number }
/** The milestone's encounters rows for the ROM since `since`, oldest first; they are written to steps.log. */
export function encounterRows(env: NodeJS.ProcessEnv, rom: string, milestone: string, since: string): Row[] {
  const rows = psql(env, `SELECT "Species","Level","IsShiny","Timestamp" FROM encounters WHERE "GameFileName"='${rom}' ` +
    `AND "Milestone"='${milestone}' AND "Timestamp" >= '${since}' ORDER BY "Timestamp"`)
  step(`${rows.length} encounters rows since ${since}: ${rows.join(' ; ')}`)
  return rows.map((l) => {
    const [species, level, shiny, at] = l.split('|')
    return { species, level: Number(level), shiny: shiny === 't', at: toMs(at) }
  })
}
