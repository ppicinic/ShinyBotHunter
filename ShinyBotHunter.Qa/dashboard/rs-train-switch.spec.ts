import { expect, test } from '@playwright/test'
import { step } from './driver'
import {
  AFTER_SHIFT, CHOSEN, copyLeadToSlot2, expAt, FLEEING, MUDKIP, PARTY_AFTER, PRESS, rsRom, SWITCH_CONFIRMED, SWITCH_MISSED,
  SWITCH_TRY, trainingRun, waitLine, writeSlot
} from './rs-training'
import { ADVANCE, ENCOUNTER, FOUND, OVER, parseEncounter, snapshotField } from './rs-wild'
import { MINUTE } from './static'

// rs-train-switch (#433, Q1c; spec QA table, FR-17), and rs-train-switch-ruby with QA_RS_ROM=RU-Wild: the same checks,
// which prove the per-game CB2_PartyMenuMain (FR-12). The battler is built in RAM (approval answer 5): the lead copied
// to slot 2 and raised to Lv 20, which the damage formula makes stronger. Three battles, then a cancel.
// T3b (#451): Train Type Switch, its SwitchSlot option 2 and No Target are chosen by rs-training.ts's startTraining.
const ROM = rsRom()
// Task_HandlePopupMenuInput, the party task's function once SHIFT is up, and HandleBattlePartyMenu, the list's: the same
// in both games (RsPointerService; research S10). The snapshot prints a uint in decimal.
const POPUP_TASK = String(0x08095429)
const LIST_TASK = String(0x08095139)
// The base experience yield of the Route 101 wilds, by national number (pret base_stats.h): Poochyena, Zigzagoon, Wurmple.
const YIELD: Record<number, number> = { 261: 55, 263: 60, 265: 54 }

test(`rs-train-switch${ROM === 'RU-Wild' ? '-ruby' : ''}: three Switch battles to slot 2 on ${ROM}, each switch gated and confirmed, the battler wins`, async () => {
  test.setTimeout(25 * MINUTE)
  const result = await trainingRun(async () => {
    await copyLeadToSlot2()
    await writeSlot(2, { level: 20, exp: expAt(20) })
    await writeSlot(1, { exp: expAt(7) })
  }, { trainType: 'Switch', targetLevel: 101, switchSlot: 2 }, async () => {
    // Two party after lines a battle: the third battle's slot 1 is the sixth.
    await waitLine(PARTY_AFTER, 6, 12 * MINUTE, '"three battles": the third battle\'s party after lines')
    return 'cancel'
  })

  const { log, idle } = result
  const overs = log.flatMap((l, i) => (OVER.test(l) ? [i] : []))
  for (let n = 1; n <= 3; n++) {
    const b = log.slice(n === 1 ? 0 : overs[n - 2] + 1, overs[n - 1] + 1)
    const criterion = `battle ${n}`
    const fail = (what: string): never => {
      throw new Error(`${criterion}: ${what}; its lines:\n${b.filter((l) => l.includes('[RsBattle]')).join('\n')}`)
    }
    // Each press line, by what it pressed: [index in b, what, confirmed by, the confirming snapshot].
    const presses = b.flatMap((l, i) => {
      const m = PRESS.exec(l)
      return m ? [{ i, what: m[1], by: m[3], snapshot: m[4] }] : []
    })
    // The press lines matching what, at least one, each confirmed by `by`.
    const all = (what: RegExp, by: string, item: string): { i: number; what: string; by: string; snapshot: string }[] => {
      const found = presses.filter((p) => what.test(p.what))
      if (found.length === 0) fail(`"${item}": no line matching ${what}`)
      for (const p of found.filter((x) => x.by !== by)) fail(`"${item}": ${p.what} confirmed by "${p.by}", not "${by}"`)
      return found
    }
    const one = (what: RegExp, by: string, item: string): { i: number; what: string; by: string; snapshot: string } => {
      const found = all(what, by, item)
      if (found.length !== 1) fail(`"${item}": ${found.length} lines matching ${what}, not 1`)
      return found[0]
    }
    const at = (pattern: RegExp, item: string): number => {
      const i = b.findIndex((l) => pattern.test(l))
      if (i === -1) fail(`"${item}": no line matching ${pattern}`)
      return i
    }

    // "POKéMON chosen with the cursor confirmed".
    const cursor = one(/^Down on the action menu 0 -> 2$/, 'cursor 2', 'POKéMON chosen with the cursor confirmed')
    const pokemon = one(/^BattleConfirm on POK.{1,2}MON \(switch try 1 of 3\)$/, 'the screen leaving the action menu', 'POKéMON chosen')
    // "The trainee leads" (QA plan L6): the bot tries a switch only while the active slot is not the battler's, so a first
    // try in every battle is the trainee leading.
    const tryAt = at(SWITCH_TRY, 'the trainee leads: switch try 1')
    const [, tryNo, toSlot, , list] = SWITCH_TRY.exec(b[tryAt])!
    if (tryNo !== '1' || toSlot !== '1') fail(`"the trainee leads": switch try ${tryNo} to slot ${toSlot}, not try 1 to slot 1`)
    if (b.filter((l) => SWITCH_TRY.test(l)).length !== 1) fail('more than one switch try')
    // "The party cursor confirmed on slot 2" (slot 1 counted from 0).
    const down = one(/^Down on the party list from 0$/, 'the party cursor leaving 0', 'the party cursor confirmed on slot 2')
    if (snapshotField(down.snapshot, 'PartyMonIndex') !== '1') fail('"the party cursor confirmed on slot 2": the Down\'s snapshot is not on slot 1')
    const slotA = one(/^BattleConfirm on party slot 1$/, 'the SHIFT submenu', 'the party cursor confirmed on slot 2')
    // "SHIFT pressed only once the submenu is up (the party task's function has changed), no fixed wait": the SHIFT line's
    // gate is the snapshot that confirmed the A on the slot, the popup's task function with the fade off, so it was sent on
    // that read and not after a time.
    const shift = one(/^BattleConfirm on SHIFT at GameSnapshot \{.*\}$/, 'the submenu closing', 'SHIFT pressed only once the submenu is up')
    const gate = shift.what.replace(/^BattleConfirm on SHIFT at /, '')
    step(`battle ${n}: SHIFT gated on ${gate}`)
    if (gate !== slotA.snapshot) fail(`"SHIFT ... no fixed wait": its gate ${gate} is not the snapshot that confirmed the slot's A, ${slotA.snapshot}`)
    if (snapshotField(gate, 'Screen') !== 'OtherApp' || snapshotField(gate, 'TaskFunction') !== POPUP_TASK || snapshotField(gate, 'AppState') !== '0'
      || snapshotField(gate, 'PartyMonIndex') !== '1') {
      fail(`"SHIFT pressed only once the submenu is up": its gate is not the popup (task ${POPUP_TASK}) with the fade off on slot 1`)
    }
    if (snapshotField(list, 'TaskFunction') !== LIST_TASK) fail(`"the party task's function has changed": the list read task ${snapshotField(list, 'TaskFunction')}, not ${LIST_TASK}`)
    // "The switch confirmed by 0x02024A6A reading 1", with nothing pressed between SHIFT and that action menu (FR-17).
    const afterShift = at(AFTER_SHIFT, 'the switch confirmed')
    const confirmed = at(SWITCH_CONFIRMED, 'the switch confirmed by 0x02024A6A reading 1')
    const [, reads, confirmedTry] = SWITCH_CONFIRMED.exec(b[confirmed])!
    if (reads !== '1' || confirmedTry !== '1') fail(`"the switch confirmed by 0x02024A6A reading 1": reads ${reads} on try ${confirmedTry}`)
    if (b.some((l) => SWITCH_MISSED.test(l))) fail('a switch try that did not land')
    if (b.slice(shift.i + 1, confirmed).some((l) => PRESS.test(l) || ADVANCE.test(l))) fail('a press between SHIFT and the switch confirmed')
    // "The battler then fights with a move and wins": FIGHT, the picker's move and the move after the switch, no flee, and
    // the battle over with nothing on RUN (QA plan L4). A missed move means another turn: one FIGHT, choice and move each.
    const fights = all(/^BattleConfirm on FIGHT$/, 'the move list', 'the battler then fights with a move')
    const chosen = b.flatMap((l, i) => (CHOSEN.test(l) ? [i] : []))
    const moves = all(/^BattleConfirm on move \d$/, 'the screen leaving the move list', 'the battler then fights with a move')
    if (chosen.length !== fights.length || moves.length !== fights.length) {
      fail(`"fights with a move": ${fights.length} FIGHT, ${chosen.length} chosen and ${moves.length} move lines`)
    }
    for (const p of presses.filter((x) => / on the move list \d -> \d$/.test(x.what))) {
      if (p.by !== `cursor ${p.what.slice(-1)}`) fail(`"fights with a move": ${p.what} confirmed by "${p.by}"`)
    }
    const order = [cursor.i, pokemon.i, tryAt, down.i, slotA.i, shift.i, afterShift, confirmed, fights[0].i, chosen[0], moves[0].i]
    if (order.some((v, k) => k > 0 && v < order[k - 1])) fail('the lines are not in the order POKéMON, list, Down, slot, SHIFT, switch confirmed, FIGHT, move')
    const fleeing = b.find((l) => FLEEING.test(l))
    if (fleeing) fail(`"and wins": ${fleeing.trim()}`)
    const [, , , , onRun] = OVER.exec(b[b.length - 1])!
    if (onRun !== '0') fail(`"and wins": battle over with ${onRun} on RUN`)
    step(`battle ${n}: switched to slot 1 and won`)
  }

  // "The trainee's experience in the party record is higher after each battle": no line carries the experience (QA plan
  // L3, not added by T2), and the bot is never idle between battles, so the read with the bot idle after the third
  // battle is compared with the write before the Run plus each battle's share. Each battle above was won with the trainee
  // in it, so its share is the game's floor(floor(yield × L / 7) / 2): the wild's base experience yield times its level
  // from the battle's encounter line, split between the two that took part (#468). A fourth battle started before the
  // cancel may have given its share too.
  const shareOf = (line: string): number => {
    const { species, level } = parseEncounter(line)
    return Math.floor(Math.floor((YIELD[species] * level) / 7) / 2)
  }
  const shares = overs.slice(0, 3).map((over, k) => shareOf(log.slice(k === 0 ? 0 : overs[k - 1] + 1, over).find((l) => ENCOUNTER.test(l))!))
  const fourthEncounter = log.slice(overs[2] + 1).find((l) => ENCOUNTER.test(l))
  const expected = expAt(7) + shares[0] + shares[1] + shares[2]
  const [trainee, battler] = idle.party
  step(`trainee experience ${expAt(7)} before the Run, shares ${shares.join(', ')}, ${trainee.exp} with the bot idle after three won battles`)
  expect(idle.party.length, '"party count 2"').toBe(2)
  expect(fourthEncounter ? [expected, expected + shareOf(fourthEncounter)] : [expected],
    `"the trainee's experience is higher after each battle": ${trainee.exp} with the bot idle, from ${expAt(7)} and the shares ${shares.join(', ')}`)
    .toContain(trainee.exp)
  expect([battler.species, battler.level], 'the battler, Mudkip at Lv 20').toEqual([MUDKIP, 20])
  // "The field free": every battle over is logged at the free field; with the bot idle the field is free, unless the
  // cancel came after a fourth battle had started.
  const fourth = log.slice(overs[2] + 1).some((l) => FOUND.test(l) || ENCOUNTER.test(l))
  if (fourth) step('a fourth battle had started before the cancel: the field was free at each battle over')
  else expect([idle.lock, idle.battleBit], '"the field free": lock and battle bit with the bot idle').toEqual([0, 0])
})
