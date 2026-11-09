// The pieces the hgss-pickup scenarios share (#278, #289): the [Pickup] lines PickupTakeEvent.TakeAsync logs on every
// visit, the party read from RAM, the Wild Encounter form and the decision windows. The regexes change together with
// those lines, so they live here once; the HGSS frame (launch, cancel, teardown) stays copied in
// each spec, as the other specs do.
import { expect, type Locator } from '@playwright/test'
import { readMemory, readU32 } from './driver'
import { decodeMon, MON_SIZE } from './egg-pid'

// ABILITY_PICKUP, include/constants/abilities.h.
export const ABILITY_PICKUP = 53
// The party, from the anchor: count at -0x23F44, records of 0xEC bytes from -0x23F40 (the Primo specs' offsets).
const ANCHOR_ADDR = 0x21d4158
const PARTY_COUNT_OFFSET = -0x23f44
const PARTY_OFFSET = -0x23f40

// Every visit writes it at the party list, the first visit included; a heal decision makes no visit, its later return
// decision does.
export const PICKUP_HELD = /\[Pickup\] held (\d+) of (\d+) pickup members; (take|nothing to take)/
// One per take, the slot 1-based.
export const PICKUP_TOOK = /\[Pickup\] took item (\d+) from slot (\d+) \((.*)\)/
// Every visit's end: after the field-free wait that follows CANCEL, before the post-visit party read.
export const PICKUP_TRIP = /\[Pickup\] trip took (\d+) ms/
// The still-holds stop and the stale stops. Matched up to "Pickup: " so the é of the still-holds sentence, which
// backend.log (the redirected console) may not carry, cannot hide it.
export const PICKUP_STOP = /\[Pickup\] Pickup: /

export type Held = { held: number; members: number; take: boolean }
export function parseHeld(line: string): Held | null {
  const m = PICKUP_HELD.exec(line)
  return m ? { held: Number(m[1]), members: Number(m[2]), take: m[3] === 'take' } : null
}

export type Took = { item: number; slot: number; name: string }
export function parseTook(line: string): Took | null {
  const m = PICKUP_TOOK.exec(line)
  return m ? { item: Number(m[1]), slot: Number(m[2]), name: m[3] } : null
}

export function parseTrip(line: string): number | null {
  const m = PICKUP_TRIP.exec(line)
  return m ? Number(m[1]) : null
}

export type PartyMon = { slot: number; species: number; ability: number; heldItem: number }
/** The party from RAM through egg-pid.ts's decodeMon, slots 1-based; fails on a bad checksum, naming the slot. */
export async function readPartyRam(): Promise<PartyMon[]> {
  // A plain read can tear a record the game is updating (the bot's own read is the validated one), so retry the whole read.
  for (let attempt = 1; ; attempt++) {
    const anchor = await readU32(ANCHOR_ADDR)
    const count = (await readMemory(anchor + PARTY_COUNT_OFFSET, 1))[0]
    const party: PartyMon[] = []
    let badSlot = 0
    for (let i = 0; i < count && badSlot === 0; i++) {
      const mon = decodeMon(await readMemory(anchor + PARTY_OFFSET + i * MON_SIZE, MON_SIZE))
      if (!mon.checksumValid) badSlot = i + 1
      else party.push({ slot: i + 1, species: mon.species, ability: mon.ability, heldItem: mon.heldItem })
    }
    if (badSlot === 0) return party
    if (attempt === 10) throw new Error(`party slot ${badSlot} read from RAM has a bad checksum`)
    await new Promise((resolve) => setTimeout(resolve, 100))
  }
}

/** The 1-based slots of the Pickup members holding an item. */
export const pickupHolders = (party: PartyMon[]): number[] =>
  party.filter((m) => m.ability === ABILITY_PICKUP && m.heldItem !== 0).map((m) => m.slot)

/**
 * Fills the open Wild Encounter form: Route 33, ShinyTarget, Run, xRange 3, train Standard, target level 101. Returns
 * the Pickup checkbox's label and whether it is disabled; with `pickup` it fails on a disabled box, naming its label,
 * and ticks it.
 */
export async function fillPickupForm(card: Locator, { pickup }: { pickup: boolean }): Promise<{ label: string; disabled: boolean }> {
  const milestone = card.getByTestId('param-PhaseName').locator('select')
  try {
    await expect(milestone.locator('option[value="Route 33"]')).toBeAttached({ timeout: 30_000 })
  } catch {
    throw new Error('no Route 33 milestone is offered; make it in the Milestone Editor')
  }
  await milestone.selectOption('Route 33')
  await card.getByTestId('param-TargetType').locator('select').selectOption('ShinyTarget')
  await card.getByTestId('param-EncounterType').locator('select').selectOption('Run')
  await card.getByTestId('param-xRange').locator('input').fill('3')
  await card.getByTestId('param-TrainType').locator('select').selectOption('Standard')
  await card.getByTestId('param-TargetLevel').locator('input').fill('101')
  const field = card.getByTestId('param-Pickup')
  const box = field.locator('input[type="checkbox"]')
  await expect(box).toBeAttached({ timeout: 30_000 })
  const label = ((await field.locator('label').textContent()) ?? '').trim()
  const disabled = await box.isDisabled()
  if (pickup) {
    expect(disabled, `the Pickup checkbox is disabled: "${label}"`).toBe(false)
    await box.check()
  }
  return { label, disabled }
}

export type Decision = { held: Held; took: Took[]; tripMs: number; lines: string[] }
/**
 * The decision after each battle in `log` from line `from`: the `[Pickup]` and `[Orchestrator] State:` lines between
 * a `BattleComplete` and the next `Encountering` (a battle during a heal walk stays in the same window), the open last
 * window included. Each window must hold exactly one count line and one `trip took` line; fails with the window's
 * lines otherwise.
 */
export function checkDecisions(log: string[], from: number): Decision[] {
  const windows: string[][] = []
  let current: string[] | null = null
  for (const line of log.slice(from)) {
    if (line.includes('[Orchestrator] State: BattleComplete') && current === null) {
      current = []
      windows.push(current)
    }
    if (current === null) continue
    if (line.includes('[Pickup]') || line.includes('[Orchestrator] State:')) current.push(line)
    if (line.includes('[Orchestrator] State: Encountering')) current = null
  }
  return windows.map((lines, i) => {
    const held = lines.map(parseHeld).filter((h) => h !== null)
    if (held.length !== 1) throw new Error(`decision ${i + 1} has ${held.length} [Pickup] count lines: ${lines.join(' | ')}`)
    const trips = lines.map(parseTrip).filter((ms) => ms !== null)
    if (trips.length !== 1) throw new Error(`decision ${i + 1} has ${trips.length} [Pickup] trip lines: ${lines.join(' | ')}`)
    return { held: held[0], took: lines.map(parseTook).filter((t) => t !== null), tripMs: trips[0], lines }
  })
}
