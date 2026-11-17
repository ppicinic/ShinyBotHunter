// rs-train (docs/specs/rs-navigation-and-training.md, QA; FR-13, FR-14, FR-19): the lead one experience point short of Lv 9,
// Standard training to 9. One battle fought with a move, one level-up box cleared by two confirmed presses, Complete.
// The plan's run line (#433, Q1b): `npx playwright test rs-train.spec`; QA_RS_ROM=RU-Wild runs it on Ruby.
//
// Depends on T3b (#451), not merged when this was written: Train Type Standard and No Target in the form (rs-training.ts's
// startTraining), and HasOverworldNavigation for RS, without which Complete comes with no walk home and no `already at
// start` line. Check both after T3b merges.
import { expect, test } from '@playwright/test'
import { snapshotField, OVER, START } from './rs-wild'
import { MINUTE } from './static'
import { ASKING, CHOSEN, CLEARING, expAt, movesOf, oneShort, PARTY_AFTER, PRESS, STATE, trainingRun, writeSlot } from './rs-training'

test('RS training rs-train: Standard to Lv 9 from one point short, one battle fought with a move, one level-up box, Complete', async () => {
  test.setTimeout(20 * MINUTE)
  const moves = movesOf(33, 45, 189)
  const { idle, log } = await trainingRun(
    async () => {
      await writeSlot(1, { ...oneShort(9), ...moves })
    },
    { trainType: 'Standard', targetLevel: 9 },
    async () => 'complete'
  )

  // "one battle fought with a move".
  const overs = log.flatMap((l, i) => (OVER.test(l) ? [i] : []))
  expect(overs.length, '"one battle": battle over lines').toBe(1)
  const battle = log.slice(0, overs[0])
  // Each turn: FIGHT confirmed by the move list, the picker's choice, the move cursor to its slot (each step confirmed by
  // the cursor it moved to), then the move, confirmed by the screen leaving the move list.
  const turns = battle.flatMap((l, i) => (CHOSEN.test(l) ? [i] : []))
  expect(turns.length, '"fought with a move": the damage picker\'s lines').toBeGreaterThan(0)
  for (const at of turns) {
    const slot = Number(CHOSEN.exec(battle[at])![1])
    const before = battle.slice(0, at).map((l) => PRESS.exec(l)).filter((m) => m !== null).at(-1)!
    expect([before[1], before[3]], `"fought with a move": the press before "${battle[at].trim()}"`).toEqual(['BattleConfirm on FIGHT', 'the move list'])
    const after = battle.slice(at + 1).map((l) => PRESS.exec(l)).filter((m) => m !== null)
    const last = after.findIndex((m) => m[1].startsWith('BattleConfirm on move'))
    expect(last, `"fought with a move": no BattleConfirm on move after "${battle[at].trim()}"`).toBeGreaterThanOrEqual(0)
    for (const m of after.slice(0, last)) {
      const cursor = /^(Left|Right|Up|Down) on the move list (\d) -> (\d)$/.exec(m[1])
      expect(cursor, `"fought with a move": a press between the choice and the move: ${m[0]}`).not.toBeNull()
      expect(m[3], `"each move-cursor press confirmed by the cursor": ${m[0]}`).toBe(`cursor ${cursor![3]}`)
    }
    expect([after[last][1], after[last][3]], '"fought with a move": the move confirmed').toEqual([`BattleConfirm on move ${slot}`, 'the screen leaving the move list'])
  }

  // "one level-up box cleared by two confirmed presses": page 1 confirmed by page 2 (Prompt = 2), then page 2 by the box
  // leaving it. The counter's 3 is the read with the bot idle below (the plan's L1).
  const boxes = log.map((l) => PRESS.exec(l)).filter((m) => m !== null && m[1].startsWith('level-up box'))
  expect(boxes.map((m) => `${m![1]} / ${m![3]}`), '"one level-up box, two confirmed presses"').toEqual([
    'level-up box page 1 for slot 0: BattleConfirm / page 2',
    'level-up box page 2 for slot 0: BattleConfirm / the box leaving page 2'
  ])
  expect(snapshotField(boxes[0]![4], 'Prompt'), '"page 1 confirmed by page 2": the snapshot\'s Prompt').toBe('2')
  expect(log.filter((l) => ASKING.test(l)), 'no move prompt on the way to Lv 9').toEqual([])

  const party = log.map((l) => PARTY_AFTER.exec(l)).filter((m) => m !== null)
  expect(party.map((m) => [m![1], m![4], m![5]]), '"party after the battle": slot 0 at Lv 9 with its three moves').toEqual([['0', '9', moves.moves!.join(',')]])

  // "Complete": the walk home is `already at start` (Spin never leaves the tile), and the orchestrator's last state
  // before it is ReturnToStart, with none after it (FinishReturn swaps in Complete, which the frame waited for).
  const clearing = log.flatMap((l, i) => (CLEARING.test(l) ? [i] : []))
  expect(clearing.map((i) => CLEARING.exec(log[i])![1]), '"Complete": the walk home').toEqual(['already at start'])
  expect(clearing[0], '"Complete": the walk home after the battle').toBeGreaterThan(overs[0])
  const states = log.flatMap((l, i) => (STATE.test(l) ? [{ i, state: STATE.exec(l)![1] }] : []))
  expect(states.filter((s) => s.i < clearing[0]).at(-1)?.state, '"Complete": the state of the walk home').toBe('ReturnToStart')
  expect(states.filter((s) => s.i > clearing[0]), '"Complete": no orchestrator state after the walk home').toEqual([])

  // With the bot idle: the counter's stale 3 after the box, Lv 9 with its experience, the field free on the start tile.
  expect(idle.boxCounter, '"the box cleared": 0x0201609C with the bot idle').toBe(3)
  expect(idle.party[0].level, 'the lead\'s level with the bot idle').toBe(9)
  expect(idle.party[0].exp, 'the lead\'s experience with the bot idle, at least Lv 9\'s').toBeGreaterThanOrEqual(expAt(9))
  expect([idle.lock, idle.battleBit], '"the field free": the lock and the battle bit with the bot idle').toEqual([0, 0])
  expect(idle.tile, 'the start tile with the bot idle').toEqual(START)
})
