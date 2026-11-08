// The Gen 4 party record (0xEC bytes), for the Primo shiny scenario: decode one, and give it a shiny PID by
// re-encrypting it as the game would (ported from the Primo spike's egg.py; docs/research/primo-slugma-egg.md).
// 0x00 PID, 0x06 checksum, 0x08 to 0x87 blocks A to D shuffled by the PID and encrypted with the checksum as
// seed, 0x88 to 0xEB the party extension encrypted with the PID as seed.

export const MON_SIZE = 0xec
const BLOCKS = 0x08
const EXTENSION = 0x88

const PERM = ['ABCD', 'ABDC', 'ACBD', 'ACDB', 'ADBC', 'ADCB', 'BACD', 'BADC', 'BCAD', 'BCDA', 'BDAC', 'BDCA',
  'CABD', 'CADB', 'CBAD', 'CBDA', 'CDAB', 'CDBA', 'DABC', 'DACB', 'DBAC', 'DBCA', 'DCAB', 'DCBA']

const shuffleOf = (pid: number): number => ((pid & 0x3e000) >>> 13) % 24

/** XORs each u16 with the high half of the LCG s = s * 0x41C64E6D + 0x6073; the same call encrypts and decrypts. */
function crypt(data: Buffer, seed: number): Buffer {
  const out = Buffer.alloc(data.length)
  let s = seed >>> 0
  for (let i = 0; i < data.length; i += 2) {
    s = (Math.imul(s, 0x41c64e6d) + 0x6073) >>> 0
    out.writeUInt16LE(data.readUInt16LE(i) ^ (s >>> 16), i)
  }
  return out
}

const sum16 = (data: Buffer): number => {
  let sum = 0
  for (let i = 0; i < data.length; i += 2) sum = (sum + data.readUInt16LE(i)) & 0xffff
  return sum
}

/** Blocks A, B, C, D in that order, decrypted, from a record in storage order. */
function plainBlocks(raw: Buffer): Buffer {
  const pid = raw.readUInt32LE(0)
  const stored = crypt(raw.subarray(BLOCKS, EXTENSION), raw.readUInt16LE(6))
  const order = PERM[shuffleOf(pid)]
  return Buffer.concat([...'ABCD'].map((c) => stored.subarray(order.indexOf(c) * 32, order.indexOf(c) * 32 + 32)))
}

export interface Mon {
  pid: number
  species: number
  heldItem: number
  ability: number
  isEgg: boolean
  tid: number
  sid: number
  ivWord: number
  shuffle: number
  checksumValid: boolean
  isShiny: boolean
}

export function decodeMon(raw: Buffer): Mon {
  const pid = raw.readUInt32LE(0)
  const plain = plainBlocks(raw)
  const tid = plain.readUInt16LE(0x04)
  const sid = plain.readUInt16LE(0x06)
  const ivWord = plain.readUInt32LE(32 + 0x10)
  return {
    pid,
    species: plain.readUInt16LE(0x00),
    heldItem: plain.readUInt16LE(0x02),
    ability: plain.readUInt8(0x0d),
    isEgg: ((ivWord >>> 30) & 1) === 1,
    tid,
    sid,
    ivWord,
    shuffle: shuffleOf(pid),
    checksumValid: sum16(plain) === raw.readUInt16LE(6),
    isShiny: (tid ^ sid ^ (pid >>> 16) ^ (pid & 0xffff)) < 8
  }
}

/**
 * A shiny PID for tid/sid with the old PID's low half (gender and ability unchanged) and the same block order:
 * hi = tid ^ sid ^ lo ^ k for the smallest k in 0 to 7 whose hi & 3 equals the old hi & 3.
 */
export function shinyPidKeepingLow(pid: number, tid: number, sid: number): number {
  const lo = pid & 0xffff
  // The smallest such k is below 4: it only has to fix the two low bits.
  const k = (tid ^ sid ^ lo ^ (pid >>> 16)) & 3
  return (((tid ^ sid ^ lo ^ k) << 16) | lo) >>> 0
}

/** The record with newPid: checksum recomputed, blocks re-shuffled for newPid and re-encrypted, extension re-encrypted. */
export function rewritePid(raw: Buffer, newPid: number): Buffer {
  const plain = plainBlocks(raw)
  const extension = crypt(raw.subarray(EXTENSION, MON_SIZE), raw.readUInt32LE(0))
  const checksum = sum16(plain)
  const order = PERM[shuffleOf(newPid)]
  const stored = Buffer.concat([...order].map((c) => plain.subarray('ABCD'.indexOf(c) * 32, 'ABCD'.indexOf(c) * 32 + 32)))
  const out = Buffer.from(raw)
  out.writeUInt32LE(newPid >>> 0, 0)
  out.writeUInt16LE(checksum, 6)
  crypt(stored, checksum).copy(out, BLOCKS)
  crypt(extension, newPid).copy(out, EXTENSION)
  return out
}
