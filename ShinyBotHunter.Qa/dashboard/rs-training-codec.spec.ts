import * as fs from 'fs'
import { expect, test } from '@playwright/test'
import { step } from './driver'
import { liveBlock, partyCount, sections } from './jirachi-seed'
import { decodeRecord, encodeRecord, expAt, movesOf, MUDKIP, oneShort } from './rs-training'
import { PRISTINE } from './rs-wild'

// rs-training.ts's codec proved with no emulator, on the party of the save in QA_RS_SAVE, by default the pristine
// qa-route101 save. Skipped, saying so, when the file is absent.
const SAVE = process.env.QA_RS_SAVE ?? PRISTINE

test('rs-training codec: the save\'s lead decodes as the shiny Lv 7 Mudkip, re-encodes byte for byte, and an edit keeps its checksum', () => {
  test.skip(!fs.existsSync(SAVE), `no save at ${SAVE}: set QA_RS_SAVE to a copy of qa-route101.SaveRAM`)
  const save = fs.readFileSync(SAVE)
  // SaveBlock1 is section 1 of the live block; its party is at + 0x238 (pret include/global.h).
  const at = sections(save, liveBlock(save)).find((s) => s.id === 1)!.start + 0x238
  const raw = save.subarray(at, at + 100)
  const lead = decodeRecord(raw)
  const shinyValue = (lead.otId & 0xffff) ^ (lead.otId >>> 16) ^ (lead.pid >>> 16) ^ (lead.pid & 0xffff)
  step(`${SAVE}: party count ${partyCount(save)}, lead ${JSON.stringify(lead)}, shiny value ${shinyValue}`)
  expect([lead.checksumOk, lead.species, lead.level], 'the lead: checksum, internal species, level').toEqual([true, MUDKIP, 7])
  // Shiny when TID ^ SID ^ PID high ^ PID low is under 8; the message is the failure's.
  expect(shinyValue, 'the lead should be shiny, and its shiny value is 8 or more').toBeLessThan(8)
  expect(lead.moves, 'the lead\'s moves').toEqual([33, 45, 189, 0])
  expect(lead.exp >= expAt(7) && lead.exp < expAt(8), `the lead's experience ${lead.exp} is not in Lv 7`).toBe(true)

  expect(encodeRecord(raw, {}).equals(raw), 'the record re-encoded with no change is not byte for byte the same').toBe(true)

  const changes = { ...oneShort(15, 14), ...movesOf(33, 45, 189, 55), item: 182, hp: 1, maxHp: 1, attack: 1, spAtk: 1 }
  const editedRaw = encodeRecord(raw, changes)
  const edited = decodeRecord(editedRaw)
  step(`edited: ${JSON.stringify(edited)}; bytes ${editedRaw.toString('hex')}`)
  expect(edited.checksumOk, 'the edited record fails its checksum').toBe(true)
  expect(edited.checksum, 'the checksum did not change with the record').not.toBe(lead.checksum)
  expect([edited.pid, edited.otId, edited.species], 'the edit changed the PID, the OTID or the species').toEqual([lead.pid, lead.otId, lead.species])
  for (const [k, v] of Object.entries(changes)) expect(edited[k as keyof typeof changes], `the edited ${k}`).toEqual(v)
  // A record with one byte of the secure block changed fails its checksum.
  const corrupt = Buffer.from(editedRaw)
  corrupt[40] ^= 1
  expect(decodeRecord(corrupt).checksumOk, 'a changed secure byte still passes the checksum').toBe(false)
})
