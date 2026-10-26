import { spawnSync } from 'child_process'
import { createHash } from 'crypto'
import * as fs from 'fs'
import * as path from 'path'
import { expect, test } from '@playwright/test'
import { firstBlockSeed, liveBlock, partyCount, sections } from './jirachi-seed'

// The Wishmaker section walk on the spike's two real saves: no emulator, backend or database.
// Beside the main checkout, found through git so a worktree resolves it too.
const repoDir = path.resolve(__dirname, '..', '..')
const MAIN_CHECKOUT = path.dirname(spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: repoDir, encoding: 'utf8' }).stdout.trim())
const SAVES = path.join(MAIN_CHECKOUT, '..', 'shinybothunterqasaves', 'rs')
const REV0_BASE = path.join(SAVES, 'rs-rev0-base.SaveRAM')
const SEED_ECDD = path.join(SAVES, 'rs-seed-ecdd.sav')

const sha256 = (save: Buffer): string => createHash('sha256').update(save).digest('hex')
const section = (save: Buffer, block: number, id: number): { start: number; counter: number } => sections(save, block).find((s) => s.id === id)!

test('RS Wishmaker saves: the rev 0 base and the crafted ECDD save read as the spike recorded them', () => {
  for (const file of [REV0_BASE, SEED_ECDD]) expect(fs.existsSync(file), `missing ${file}`).toBe(true)

  const base = fs.readFileSync(REV0_BASE)
  expect(base.length).toBe(131088)
  expect(sha256(base)).toBe('8aebc82d4453815c91ecd6a6667d3f2b1e01521f992431aabb4016b66458cc9e')
  // Every section of both blocks valid, ids 0 to 13 once each.
  for (const block of [0, 0xe000]) {
    const all = sections(base, block)
    expect(all.filter((s) => !s.valid).map((s) => s.id), `invalid sections in block ${block.toString(16)}`).toEqual([])
    expect(all.map((s) => s.id).sort((a, b) => a - b)).toEqual([...Array(14).keys()])
  }
  // Block B is live on section 0's counter, 3 against 2, but the seed is the first block's.
  expect(section(base, 0, 0).counter).toBe(2)
  expect(section(base, 0xe000, 0).counter).toBe(3)
  expect(liveBlock(base)).toBe(0xe000)
  expect(firstBlockSeed(base)).toBe(0x0a64)
  expect(firstBlockSeed(base.subarray(0, 131072)), 'the seed without the mGBA footer').toBe(0x0a64)
  // Complete after counter 2, not after 3: every live section carries counter 3.
  expect(sections(base, 0xe000).every((s) => s.counter > 2)).toBe(true)
  expect(sections(base, 0xe000).some((s) => s.counter > 3)).toBe(false)
  // Live play time 0:15:13 frame 3, the Pokédex flag (0x801), one party member.
  const time = section(base, 0xe000, 0).start + 0x0e
  expect([base.readUInt16LE(time), base[time + 2], base[time + 3], base[time + 4]]).toEqual([0, 15, 13, 3])
  expect(base[section(base, 0xe000, 2).start + 0x3a0] & 0x02).toBe(0x02)
  expect(partyCount(base)).toBe(1)

  const ecdd = fs.readFileSync(SEED_ECDD)
  expect(ecdd.length).toBe(131072)
  expect(sha256(ecdd)).toBe('3d86c956005d064dcf5684486502ddd002a02e245befc8d9f85fa540c7a82dc7')
  expect(firstBlockSeed(ecdd)).toBe(0xecdd)
})
