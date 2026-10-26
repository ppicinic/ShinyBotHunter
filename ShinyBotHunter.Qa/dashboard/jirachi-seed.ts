// Crafts a Sapphire save whose first-block seed (section 0's checksum) is a chosen value, and the BizHawk cheat
// that keeps the moving section-0 fields frozen so the bot's own in-game saves write that seed (#189).

/** gSaveBlock2 in EWRAM on Sapphire rev 2: a fixed struct; section 0 holds its first 0xF2C bytes. */
export const GSAVEBLOCK2 = 0x02024ea4

/** The section-0 fields the game changes on save; the cheat freezes all of them. */
export interface Frozen {
  hours: number // 0x0E u16 play time
  minutes: number // 0x10
  seconds: number // 0x11
  frames: number // 0x12
  day: number // 0xA0 s16, lastBerryTreeUpdate
  berryHours: number // 0xA2
  berryMinutes: number // 0xA3
  berrySeconds: number // 0xA4
}

// --- Gen 3 sections: 14 of 0x1000 per block; footer id 0xFF4, checksum 0xFF6, signature 0xFF8, counter 0xFFC ---
const SECTION_DATA_SIZES = [0xf2c, 0xf80, 0xf80, 0xf80, 0xf08, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0xf80, 0x7d0]
const fold = (sum: number): number => ((sum >>> 16) + (sum & 0xffff)) & 0xffff
function dataSum(save: Buffer, start: number, id: number): number {
  let sum = 0
  for (let w = 0; w < SECTION_DATA_SIZES[id]; w += 4) sum = (sum + save.readUInt32LE(start + w)) >>> 0
  return sum
}
export function sections(save: Buffer, block: number): { start: number; id: number; stored: number; counter: number; signed: boolean; valid: boolean }[] {
  return Array.from({ length: 14 }, (_, i) => {
    const start = block + i * 0x1000
    const id = save.readUInt16LE(start + 0xff4)
    const stored = save.readUInt16LE(start + 0xff6)
    const signed = save.readUInt32LE(start + 0xff8) === 0x08012025
    const valid = signed && id < 14 && stored === fold(dataSum(save, start, id))
    return { start, id, stored, counter: save.readUInt32LE(start + 0xffc), signed, valid }
  })
}
// The live block is the one whose section 0 has the higher counter; on a tie the second, as Gen3SaveFile reads it.
export function liveBlock(save: Buffer): number {
  const zero = (block: number): number | undefined => sections(save, block).find((s) => s.signed && s.id === 0)?.counter
  const [a, b] = [zero(0), zero(0xe000)]
  return b !== undefined && (a === undefined || b >= a) ? 0xe000 : 0
}
export const firstBlockSeed = (save: Buffer): number => sections(save, 0).find((s) => s.id === 0)!.stored
const liveSection = (save: Buffer, id: number): number => sections(save, liveBlock(save)).find((s) => s.id === id)!.start
export const partyCount = (save: Buffer): number => save[liveSection(save, 1) + 0x234]
export function partySlot(save: Buffer, i: number): { pid: number; tid: number; sid: number } {
  const slot = liveSection(save, 1) + 0x238 + i * 100
  return { pid: save.readUInt32LE(slot), tid: save.readUInt16LE(slot + 4), sid: save.readUInt16LE(slot + 6) }
}

/**
 * A copy of the save whose live section 0 has checksum `seed`: play time hours (upward from the save's own),
 * minutes, seconds and frames are searched; the berry stamp keeps its saved values. Throws if nothing fits.
 */
export function craftSeed(save: Buffer, seed: number): { save: Buffer; fields: Frozen } {
  const out = Buffer.from(save)
  const start = liveSection(out, 0)
  const savedHours = out.readUInt16LE(start + 0x0e)
  out.writeUInt16LE(0, start + 0x0e)
  out.fill(0, start + 0x10, start + 0x13)
  const base = dataSum(out, start, 0)
  for (let hours = savedHours; hours <= 999; hours++) {
    for (let minutes = 0; minutes < 60; minutes++) {
      for (let seconds = 0; seconds < 60; seconds++) {
        for (let frames = 0; frames < 60; frames++) {
          // hours is the high half of the word at 0x0C; minutes, seconds and frames are bytes 0 to 2 of the word at 0x10.
          const sum = (base + hours * 0x10000 + minutes + seconds * 0x100 + frames * 0x10000) >>> 0
          if (fold(sum) !== seed) continue
          out.writeUInt16LE(hours, start + 0x0e)
          out.writeUInt8(minutes, start + 0x10)
          out.writeUInt8(seconds, start + 0x11)
          out.writeUInt8(frames, start + 0x12)
          out.writeUInt16LE(fold(dataSum(out, start, 0)), start + 0xff6)
          const fields: Frozen = {
            hours, minutes, seconds, frames, day: out.readInt16LE(start + 0xa0),
            berryHours: out[start + 0xa2], berryMinutes: out[start + 0xa3], berrySeconds: out[start + 0xa4]
          }
          return { save: out, fields }
        }
      }
    }
  }
  throw new Error(`no play time from ${savedHours} h fits seed ${seed.toString(16)}`)
}

/** The BizHawk .cht for the fields: tab-separated, the wild-shiny.cht column order, enabled, no compare. */
export function cheatFile(fields: Frozen): string {
  const line = (offset: number, value: number, size: 'b' | 'w', name: string): string =>
    [(GSAVEBLOCK2 + offset).toString(16).toUpperCase(), (value & 0xffff).toString(16).toUpperCase(), 'N', 'System Bus', '1',
      `jirachi seed: ${name}`, size, 'h', '0', 'None'].join('\t')
  return [
    line(0x0e, fields.hours, 'w', 'play hours'),
    line(0x10, fields.minutes, 'b', 'play minutes'),
    line(0x11, fields.seconds, 'b', 'play seconds'),
    line(0x12, fields.frames, 'b', 'play frames'),
    line(0xa0, fields.day, 'w', 'berry day'),
    line(0xa2, fields.berryHours, 'b', 'berry hours'),
    line(0xa3, fields.berryMinutes, 'b', 'berry minutes'),
    line(0xa4, fields.berrySeconds, 'b', 'berry seconds')
  ].join('\n') + '\n'
}
