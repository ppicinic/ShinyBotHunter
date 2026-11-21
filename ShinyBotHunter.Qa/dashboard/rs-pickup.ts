// The pieces the rs-pickup scenarios share (#490; docs/specs/rs-pickup.md, QA): every [Pickup] line RsPickupTakeEvent logs,
// in one table; the qa-pickup save and the frame, which is rs-training.ts's trainingRun on that save with Pickup ticked;
// the wait; the Items and Poké Balls pockets; and the check of one visit's presses. The memory port is touched only with
// the bot idle (before Run, between the Cancels, at Complete) or Waiting, one awaited request at a time, each logged.
import * as path from 'path'
import { expect, type Locator } from '@playwright/test'
import { readMemory, step } from './driver'
import { BALL_SLOT, OVER, POS, PRISTINE, snapshotField, startWild, type Save } from './rs-wild'
import {
  failOnStops, MUDKIP, PP, readParty, refusal, trainingRun, writeLogged, writeSlot, type Mon, type Run, type TrainingResult
} from './rs-training'
import { backendLog, hex8 } from './static'

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// --- log patterns: each matches one Console.WriteLine of RsPickupTakeEvent (#488) ---------------------------------------

// TakeAsync's count line at the list, mail holders counted in held: HGSS's text, copied, so either can change alone.
export const PICKUP_HELD = /\[Pickup\] held (\d+) of (\d+) pickup members; (take|nothing to take)\s*$/
// A mail holder, skipped with no press. Groups: slot (1-based), name.
export const MAIL = /\[Pickup\] slot (\d) \((.+?)\) holds mail; not visited\s*$/
// The slot's Pokémon menu after A on it: the entry ids comma-separated, then where ITEM is. Groups: slot, name, entries, position.
export const MENU_ENTRIES = /\[Pickup\] slot (\d) \((.+?)\) menu \[([\d,]*)\]: ITEM at position (\d)\s*$/
// The same menu with no ITEM: B, and the slot left. Groups: slot, name, entries.
export const NO_ITEM = /\[Pickup\] slot (\d) \((.+?)\) menu \[([\d,]*)\]: no ITEM; left\s*$/
// A take, logged once the slot reads empty. Groups: item name, Gen 3 id, slot, name.
export const PICKUP_TOOK = /\[Pickup\] took (.+?) \(Gen 3 item (\d+)\) from slot (\d) \((.+?)\)\s*$/
// The take message dismissed with the item still held (a full pocket). Groups: slot, name, item name, Gen 3 id.
export const STILL_HOLDS_AFTER_TAKE = /\[Pickup\] slot (\d) \((.+?)\) still holds (.+?) \(Gen 3 item (\d+)\) after TAKE; left for the stop\s*$/
// RsBattleEvent.PressConfirmedAsync under the [Pickup] prefix: every press of the visit. Groups: what, ms, by, snapshot, the retry.
export const PICKUP_PRESS = /\[Pickup\] (.+?), (\d+) ms: confirmed by (.+?) \((GameSnapshot \{.*\})\)(; dropped, sent again)?\s*$/
// The visit's end, after B on the start menu, before the post-visit party read. Groups: frames, ms.
export const PICKUP_TRIP = /\[Pickup\] trip took (\d+) frames, (\d+) ms\s*$/
// Park's line: the still-holds stop and the stale stops ("Pickup: Waited for ..."), matched up to "Pickup: ".
export const PICKUP_STOP = /\[Pickup\] Pickup: /
// The still-holds stop; the é of Pokémon is any one or two characters (backend.log is in the console's code page).
// Groups: name, item name.
export const STILL_HOLDS_STOP = /\[Pickup\] Pickup: (.+?) still holds (.+?) after the take; check the bag and the Pok.{1,2}mon menu\.\s*$/

// One regex per press of the visit, on `<what> | <by>`; a line's kind is the row's name and its numbers.
const KIND: [string, RegExp][] = [
  ['START', /^START \| the start menu taking keys$/],
  ['start Down', /^Down on the start menu (\d) -> (\d) \| start cursor (\d)$/],
  ['start Up', /^Up on the start menu (\d) -> (\d) \| start cursor (\d)$/],
  ['A on POKEMON', /^A on POK.{1,2}MON \| the party list taking keys on cursor 0$/],
  ['list Down', /^Down on the party list (\d) -> (\d) \| the party cursor on (\d)$/],
  ['A on cursor', /^A on cursor (\d) \| slot (\d)'s Pok.{1,2}mon menu$/],
  ['B on a menu', /^B on slot (\d)'s menu \| the party list taking keys on cursor (\d)$/],
  ['menu Down', /^Down on slot (\d)'s menu (\d) -> (\d) \| the menu cursor on (\d)$/],
  ['A on ITEM', /^A on ITEM \| slot (\d)'s ITEM submenu$/],
  ['Down to TAKE', /^Down on GIVE \| the submenu cursor on TAKE$/],
  ['A on TAKE', /^A on TAKE \| slot (\d)'s take message waiting for a button$/],
  ['A at the message', /^A at slot (\d)'s take message \| the party list taking keys on cursor (\d)$/],
  ['B on the list', /^B on the party list \| the start menu taking keys$/],
  ['B on the start menu', /^B on the start menu \| the field free$/]
]

/** A press line's kind with its numbers, e.g. `list Down 0 1 1`; null for a press no row of KIND matches. */
function kindOf(what: string, by: string): string | null {
  for (const [name, pattern] of KIND) {
    const m = pattern.exec(`${what} | ${by}`)
    if (m) return [name, ...m.slice(1)].join(' ')
  }
  return null
}

// --- the save and the frame ---------------------------------------------------------------------------------------

// The spike's end state, saved in-game on Route 101 at (14, 4) (research doc, `rs.qa-pickup`).
export const PICKUP_SAVE: Save = {
  file: path.join(path.dirname(PRISTINE), 'qa-pickup.SaveRAM'),
  sha: '55d8f0c866be5ee29480933a25f525a068655133c208281dd60b973635d49848'
}
// Internal species index (include/constants/species.h).
export const ZIGZAGOON = 288
// The Zigzagoon's party slots, 1-based.
export const PICKUP_SLOTS = [2, 3, 4, 5, 6]
// Ruby and Sapphire's Pickup table, by Gen 3 id (FR-13): Super Potion, Full Heal, Ultra Ball, Rare Candy, Full Restore,
// Revive, Nugget, Protein, PP Up, King's Rock.
export const PICKUP_ITEMS = [22, 23, 2, 68, 19, 24, 110, 64, 69, 187]

/** A pocket slot: item id and quantity, both u16; Ruby and Sapphire keep the quantities plain. */
export type BagSlot = { item: number; quantity: number }
export type Pockets = { items: BagSlot[]; balls: BagSlot[] }
// gSaveBlock1 (POS, 0x02025734) + 0x560: 20 slots; + 0x600: 16 slots (rs-wild's BALL_SLOT).
export const ITEMS_POCKET = POS + 0x560
export const BALLS_POCKET = BALL_SLOT

export type PickupResult = TrainingResult & { launchBag: Pockets; idleBag: Pockets }

/**
 * trainingRun on the qa-pickup save, started by startPickup. With the bot idle before `setup`: the party as saved (six,
 * Mudkip Lv 11 leading, five Zigzagoon Lv 2 holding nothing, every checksum valid), the lead's HP and PP written full
 * (the spike's end state is not known, and a heal first would make no first visit), and both pockets read. Both pockets
 * are read again with the bot idle at the end. Then no [Pickup] stop but `expectedStop`.
 */
export async function pickupRun(
  setup: (launchBag: Pockets) => Promise<void>,
  body: (run: Run, launchBag: Pockets) => Promise<'complete' | 'cancel'>,
  { targetLevel = 101, expectedStop }: { targetLevel?: number; expectedStop?: RegExp } = {}
): Promise<PickupResult> {
  let launchBag: Pockets | null = null
  let idleBag: Pockets | null = null
  const result = await trainingRun(
    async () => {
      const party = await readParty()
      step(`party as saved: ${JSON.stringify(party)}`)
      const criterion = 'the qa-pickup save\'s party: Mudkip Lv 11, then five Zigzagoon Lv 2 holding nothing, every checksum valid'
      expect(party.map((m, i) => [m.species, m.level, i === 0 ? 'lead' : m.item, m.checksumOk]), criterion)
        .toEqual([[MUDKIP, 11, 'lead', true], ...PICKUP_SLOTS.map(() => [ZIGZAGOON, 2, 0, true])])
      const lead = party[0]
      const pp = lead.moves.map((id) => (id === 0 ? 0 : PP[id]))
      expect(pp.every((p) => p !== undefined), `the lead's moves ${lead.moves.join(',')} are not all in rs-training.ts's PP table`).toBe(true)
      await writeSlot(1, { hp: lead.maxHp, pp })
      launchBag = await readPockets()
      await setup(launchBag)
    },
    { trainType: 'Standard', targetLevel },
    (run) => body(run, launchBag!),
    {
      save: PICKUP_SAVE,
      start: (card) => startPickup(card, targetLevel),
      whileIdle: async () => {
        idleBag = await readPockets()
      }
    }
  )
  expect(result.log.filter((l) => PICKUP_STOP.test(l) && !expectedStop?.test(l)), 'a [Pickup] stop').toEqual([])
  return { ...result, launchBag: launchBag!, idleBag: idleBag! }
}

/**
 * startWild with Spin, then NoTarget, Standard, the target level and the Pickup box, which must be enabled with the
 * label exactly `Pickup`, ticked. A refused Run fails at once with its sentence, as startTraining does. Returns the ISO
 * time of the Run click.
 */
export async function startPickup(card: Locator, targetLevel = 101): Promise<string> {
  // Set once the race below is settled; read only by `refused`'s loop, on the one test thread.
  let settled = false
  const refused = async (): Promise<never> => {
    while (!settled) {
      if ((await refusal(card).count()) > 0) throw new Error(`the Run was refused: "${(await refusal(card).innerText()).trim()}"`)
      await sleep(250)
    }
    return new Promise<never>(() => undefined)
  }
  const start = startWild(card, { mode: 'Spin' }, async () => {
    await card.getByTestId('param-TargetType').locator('select').selectOption('NoTarget')
    await card.getByTestId('param-TrainType').locator('select').selectOption('Standard')
    await card.getByTestId('param-TargetLevel').locator('input').fill(String(targetLevel))
    const field = card.getByTestId('param-Pickup')
    const box = field.locator('input[type="checkbox"]')
    await expect(box, 'no Pickup checkbox with train Standard').toBeAttached({ timeout: 30_000 })
    const label = ((await field.locator('label').textContent()) ?? '').trim()
    expect(await box.isDisabled(), `the Pickup checkbox is disabled: "${label}"`).toBe(false)
    expect(label, 'the Pickup label').toBe('Pickup')
    await box.check()
    step(`Pickup form: NoTarget, Standard, target level ${targetLevel}, Pickup ticked`)
  })
  try {
    return await Promise.race([start, refused()])
  } finally {
    settled = true
  }
}

/**
 * rs-training.ts's waitLine, which also fails at once on a [Pickup] stop other than `expectedStop`: polls backend.log (a
 * file, never the emulator) every 250 ms until `pattern` has matched `count` lines, and returns the count-th.
 */
export async function waitPickup(pattern: RegExp, count: number, ms: number, criterion: string, expectedStop?: RegExp): Promise<string> {
  const deadline = Date.now() + ms
  while (true) {
    const log = backendLog()
    const found = log.filter((l) => pattern.test(l))
    if (found.length >= count) {
      step(`logged: ${found[count - 1].trim()}`)
      return found[count - 1]
    }
    const stop = log.find((l) => PICKUP_STOP.test(l) && !expectedStop?.test(l))
    if (stop) throw new Error(`${criterion}: the visit stopped: ${stop.trim()}`)
    failOnStops(log)
    if (Date.now() > deadline) {
      const last = log.filter((l) => /\[(Pickup|RsBattle|Navigator|Orchestrator)\]/.test(l)).slice(-10).join('\n')
      throw new Error(`${criterion}: ${found.length} of ${count} lines matching ${pattern} within ${ms / 1000} s; last lines:\n${last}`)
    }
    await sleep(250)
  }
}

// --- the pockets: read and written only with the bot idle or Waiting -------------------------------------------------

/** One read of `slots` pocket slots at `addr`, logged. */
export async function readPocket(addr: number, slots: number): Promise<BagSlot[]> {
  const raw = await readMemory(addr, slots * 4)
  const rows = Array.from({ length: slots }, (_, i) => ({ item: raw.readUInt16LE(i * 4), quantity: raw.readUInt16LE(i * 4 + 2) }))
  step(`pocket at 0x${hex8(addr)}: ${rows.filter((r) => r.item !== 0).map((r) => `${r.item} x${r.quantity}`).join(', ')}`)
  return rows
}

/** The Items pocket, then the Poké Balls pocket. */
export async function readPockets(): Promise<Pockets> {
  return { items: await readPocket(ITEMS_POCKET, 20), balls: await readPocket(BALLS_POCKET, 16) }
}

/** The whole pocket at `addr` written as `rows` in one writeLogged, which reads it back. */
export async function writePocket(addr: number, rows: BagSlot[]): Promise<void> {
  const bytes = Buffer.alloc(rows.length * 4)
  rows.forEach((r, i) => {
    bytes.writeUInt16LE(r.item, i * 4)
    bytes.writeUInt16LE(r.quantity, i * 4 + 2)
  })
  await writeLogged(`pocket of ${rows.length} slots`, addr, bytes)
}

/** Item id to quantity over both pockets. */
export function bagCounts(bag: Pockets): Record<number, number> {
  const counts: Record<number, number> = {}
  for (const { item, quantity } of [...bag.items, ...bag.balls]) if (item !== 0) counts[item] = (counts[item] ?? 0) + quantity
  return counts
}

/** "The bag holding the items": `after` is `before` with one more of each take line's item, and nothing else changed. */
export function checkBag(before: Pockets, after: Pockets, log: string[]): void {
  const expected = bagCounts(before)
  for (const t of takes(log)) expected[t.item] = (expected[t.item] ?? 0) + 1
  step(`bag: ${JSON.stringify(bagCounts(before))} at launch, ${JSON.stringify(bagCounts(after))} with the bot idle`)
  expect(bagCounts(after), '"the bag holding the items": the bag against launch plus one per take line').toEqual(expected)
}

// --- the log: takes, visits and their presses ---------------------------------------------------------------------

export type Take = { name: string; item: number; slot: number; member: string }
export const takes = (log: string[]): Take[] => log.flatMap((l) => {
  const m = PICKUP_TOOK.exec(l)
  return m ? [{ name: m[1], item: Number(m[2]), slot: Number(m[3]), member: m[4] }] : []
})

/** The n-th visit's lines (from 1): from its START press to its trip line. */
export function visit(log: string[], n: number): string[] {
  const starts = log.flatMap((l, i) => (PICKUP_PRESS.exec(l)?.[1] === 'START' ? [i] : []))
  const trips = log.flatMap((l, i) => (PICKUP_TRIP.test(l) ? [i] : []))
  expect(starts.length >= n && trips.length >= n, `visit ${n}: ${starts.length} START presses and ${trips.length} trip lines`).toBe(true)
  return log.slice(starts[n - 1], trips[n - 1] + 1)
}

/** The slots a visit opened, with the position of ITEM its entries line gave. */
export const visitedSlots = (lines: string[]): { slot: number; item: number }[] => lines.flatMap((l) => {
  const m = MENU_ENTRIES.exec(l)
  return m ? [{ slot: Number(m[1]), item: Number(m[4]) }] : []
})

/** The press kinds of a visit that opens `slots` (1-based, in order), ITEM at `item` in each slot's menu. */
function expectedKinds(first: boolean, slots: { slot: number; item: number }[]): string[] {
  const kinds = ['START']
  // Round 2: POKéMON is index 1 of this save's start menu, the cursor is 0 on a fresh boot and remembered after.
  if (first) kinds.push('start Down 0 1 1')
  kinds.push('A on POKEMON')
  let cursor = 0
  for (const { slot, item } of slots) {
    for (; cursor < slot - 1; cursor++) kinds.push(`list Down ${cursor} ${cursor + 1} ${cursor + 1}`)
    kinds.push(`A on cursor ${slot - 1} ${slot}`)
    for (let k = 0; k < item; k++) kinds.push(`menu Down ${slot} ${k} ${k + 1} ${k + 1}`)
    kinds.push(`A on ITEM ${slot}`, 'Down to TAKE', `A on TAKE ${slot}`, `A at the message ${slot} ${slot - 1}`)
  }
  kinds.push('B on the list', 'B on the start menu')
  return kinds
}

/**
 * One visit's presses (`lines`, from `visit`) against the sequence for a run's first visit or a later one and the slots
 * it opens: every press confirmed, each of a KIND row, in order; each slot's entries line right after its A, with ITEM
 * where `slots` says; the last press confirmed with the field free. Retries are counted by kind into steps.log and
 * returned, not failed.
 */
export function checkVisit(lines: string[], { first, slots }: { first: boolean; slots: { slot: number; item: number }[] }, criterion: string): Record<string, number> {
  const quoted = lines.filter((l) => l.includes('[Pickup]')).join('\n')
  const fail = (what: string): never => {
    throw new Error(`${criterion}: ${what}; the visit's lines:\n${quoted}`)
  }
  const presses = lines.flatMap((l, i) => {
    const m = PICKUP_PRESS.exec(l)
    return m ? [{ i, kind: kindOf(m[1], m[3]), snapshot: m[4], retried: m[5] !== undefined, line: l.trim() }] : []
  })
  const unknown = presses.find((p) => p.kind === null)
  if (unknown) fail(`a press of no known kind: ${unknown.line}`)
  expect(presses.map((p) => p.kind), `${criterion}: the visit's presses, each with its confirmation\n${quoted}`).toEqual(expectedKinds(first, slots))
  if (snapshotField(presses.at(-1)!.snapshot, 'Screen') !== 'FieldFree') fail('the last press is not confirmed with Screen = FieldFree')
  for (const { slot, item } of slots) {
    const a = presses.findIndex((p) => p.kind === `A on cursor ${slot - 1} ${slot}`)
    const entries = lines.findIndex((l) => MENU_ENTRIES.exec(l)?.[1] === String(slot))
    if (entries === -1) fail(`no entries line for slot ${slot}`)
    if (!(entries > presses[a].i && entries < presses[a + 1].i)) fail(`slot ${slot}'s entries line is not between its A and the next press`)
    if (MENU_ENTRIES.exec(lines[entries])![4] !== String(item)) fail(`slot ${slot}'s entries line does not give ITEM at position ${item}`)
  }
  const retries: Record<string, number> = {}
  for (const p of presses.filter((x) => x.retried)) {
    const kind = p.kind!.split(' ').filter((w) => !/^\d+$/.test(w)).join(' ')
    retries[kind] = (retries[kind] ?? 0) + 1
  }
  step(`${criterion}: ${presses.length} presses; dropped and sent again: ${JSON.stringify(retries)}`)
  return retries
}

/**
 * "The party empty-handed": with the bot idle, each of `slots` (1-based) holds nothing, or a Pickup item when a battle
 * ended after the last trip line (the Cancel landed a battle late).
 */
export function checkEmptyHanded(log: string[], party: Mon[], slots: number[]): void {
  const lastTrip = log.findLastIndex((l) => PICKUP_TRIP.test(l))
  const battleAfter = log.some((l, i) => i > lastTrip && OVER.test(l))
  for (const slot of slots) {
    const item = party[slot - 1].item
    const allowed = item === 0 || (battleAfter && PICKUP_ITEMS.includes(item))
    expect(allowed, `"the party empty-handed": slot ${slot} holds ${item} with the bot idle${battleAfter ? ', not a Pickup item' : ' and no battle after the last visit'}`).toBe(true)
  }
}
