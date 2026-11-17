// rs-train-double-levelup and rs-train-double-levelup-learn (docs/specs/rs-navigation-and-training.md, QA; FR-14, FR-15,
// FR-19), picked by QA_RS_VARIANT:
// - plain (the default): the lead at Lv 8 with experience one point short of Lv 10 and three moves, Standard to 10. Two
//   level-up boxes in the one battle, and Water Gun learned at Lv 10 into the free slot with text presses only.
// - learn: the species and levels the spec asks the scenario to name are Mudkip from Lv 9 to Lv 15, crossing Lv 10 (Water
//   Gun) and Lv 15 (Bide) in one battle: six boxes, and two move prompts, the first learned over slot 2 (Growl), the
//   second refused. The operator answers both in Discord (rs-training.ts's answerPrompt).
// QA_RS_ROM=RU-Wild runs it on Ruby.
//
// Depends on T3b (#451), not merged when this was written: Train Type Standard and No Target in the form (rs-training.ts's
// startTraining), and HasOverworldNavigation for RS, without which Complete comes with no `already at start` line. Check
// both after T3b merges.
import { expect, test } from '@playwright/test'
import { ADVANCE, snapshotField, OVER } from './rs-wild'
import { MINUTE } from './static'
import {
  answerPrompt, ASKING, CLEARING, FORGET_CLOSED, MOVE_PROMPT, movesOf, oneShort, PARTY_AFTER, PRESS, trainingRun, variant, writeSlot
} from './rs-training'

const VARIANT = variant(['plain', 'learn'] as const)
const WATER_GUN = 55
const BIDE = 117
// The writes, the target, the moves after the battle and the boxes, per variant.
const RUNS = {
  plain: { from: 8, to: 10, moves: [33, 45, 189], after: [33, 45, 189, WATER_GUN] },
  learn: { from: 9, to: 15, moves: [33, 45, 189, 193], after: [33, WATER_GUN, 189, 193] }
}
const RUN = RUNS[VARIANT]
const LEVELS = RUN.to - RUN.from

test(`RS training rs-train-double-levelup${VARIANT === 'learn' ? '-learn' : ''}: Mudkip Lv ${RUN.from} to ${RUN.to} in one battle, ${LEVELS} level-up boxes`, async () => {
  test.setTimeout((VARIANT === 'learn' ? 35 : 20) * MINUTE)
  const { idle, log } = await trainingRun(
    async () => {
      await writeSlot(1, { ...oneShort(RUN.to, RUN.from), ...movesOf(...RUN.moves) })
    },
    { trainType: 'Standard', targetLevel: RUN.to, discord: VARIANT === 'learn' },
    async (run) => {
      if (VARIANT === 'learn') {
        await answerPrompt(run, { n: 1, learner: 0, move: WATER_GUN, forget: 1 })
        await answerPrompt(run, { n: 2, learner: 0, move: BIDE, forget: null })
      }
      return 'complete'
    }
  )

  const at = (pattern: RegExp): number[] => log.flatMap((l, i) => (pattern.test(l) ? [i] : []))
  const overs = at(OVER)
  expect(overs.length, '"in the one battle": battle over lines').toBe(1)
  const presses = log.map((l, i) => ({ i, m: PRESS.exec(l) })).filter((p) => p.m !== null)
    .map((p) => ({ i: p.i, what: p.m![1], by: p.m![3], snapshot: p.m![4] }))

  // "a level-up box for every level crossed, each cleared by its own pair of confirmed presses": page 1 confirmed by page 2
  // (Prompt = 2), page 2 by the box leaving it, all for slot 0. The counter reads 1, 2 at each pair's presses; the 3 between
  // two boxes is in no line and the next box's page 1 is its evidence; the last 3 is the read with the bot idle (the
  // plan's L1).
  const boxes = presses.filter((p) => p.what.startsWith('level-up box'))
  const pair = ['level-up box page 1 for slot 0: BattleConfirm / page 2', 'level-up box page 2 for slot 0: BattleConfirm / the box leaving page 2']
  expect(boxes.map((p) => `${p.what} / ${p.by}`), `"${LEVELS} level-up boxes, each by its own pair of confirmed presses"`)
    .toEqual(Array.from({ length: LEVELS }, () => pair).flat())
  for (const p of boxes.filter((b) => b.what.includes('page 1'))) {
    expect(snapshotField(p.snapshot, 'Prompt'), `"page 1 confirmed by page 2": the snapshot's Prompt, line ${p.i}`).toBe('2')
  }
  expect(idle.boxCounter, 'the last box cleared: 0x0201609C reads its stale 3 with the bot idle').toBe(3)

  const answer = presses.filter((p) => /Delete a move\?|Stop learning\?|forget/.test(p.what))
  if (VARIANT === 'plain') {
    // "Water Gun in slot 4 with text presses only": no prompt, and after the first box nothing but the boxes and text.
    expect([at(MOVE_PROMPT).length, at(ASKING).length, answer.length], '"text presses only": move prompt, asking and answer lines').toEqual([0, 0, 0])
    expect(presses.filter((p) => p.i > boxes[0].i && !p.what.startsWith('level-up box')).map((p) => p.what), '"text presses only": gated presses after the first box')
      .toEqual([])
    expect(at(ADVANCE).some((i) => i > boxes.at(-1)!.i && i < overs[0]), '"text presses only": a text press after the last box').toBe(true)
  } else {
    // "two move prompts in the one battle, each seen waiting and answered by the rule": the battle's "Delete a move?" with
    // the tracker 1 for slot 0, Water Gun after the first box pair and Bide after the sixth. answerPrompt checked each
    // asking line (slot 0, the move) and each answer against the click, and read each edited Discord message back.
    const prompts = at(MOVE_PROMPT)
    expect(prompts.map((i) => ['Screen', 'Prompt', 'Question', 'PartyMonIndex'].map((f) => snapshotField(MOVE_PROMPT.exec(log[i])![1], f))),
      '"each seen waiting": the snapshots at the two prompts').toEqual([['BattleMoveLearnPrompt', '1', '5', '0'], ['BattleMoveLearnPrompt', '1', '5', '0']])
    const asks = at(ASKING)
    expect(asks.length, 'two asking lines').toBe(2)
    expect(asks[0] > boxes[1].i && asks[0] < boxes[2].i, `prompt 1 after box pair 1 and before pair 2: lines ${asks[0]}, ${boxes[1].i}, ${boxes[2].i}`).toBe(true)
    expect(asks[1] > boxes.at(-1)!.i && asks[1] < overs[0], `prompt 2 after box pair ${LEVELS}: lines ${asks[1]}, ${boxes.at(-1)!.i}`).toBe(true)
    // "one replacing a slot and one refusing": Yes, one Down, A on slot 1 (Growl) for Water Gun; then B and Stop learning for Bide.
    expect(answer.map((p) => `${p.what} / ${p.by}`), '"answered by the rule": the presses').toEqual([
      'BattleConfirm (Yes) at Delete a move? / the prompt leaving Delete a move?',
      'Down on the forget-a-move screen 0 -> 1 / the cursor',
      'BattleConfirm on forget slot 1 / the summary task closing',
      'B at Delete a move? / the prompt leaving Delete a move?',
      'BattleConfirm (Yes) at Stop learning? / the prompt leaving Stop learning?'
    ])
    expect(answer[0].i > asks[0] && answer[2].i < asks[1] && answer[3].i > asks[1], 'each answer after its own prompt').toBe(true)
    expect(at(FORGET_CLOSED).length, 'the forget-a-move screen closed once').toBe(1)
  }

  // "the party reads Lv N" with the moves as decided: in the log, then in memory with the bot idle.
  const party = log.map((l) => PARTY_AFTER.exec(l)).filter((m) => m !== null)
  expect(party.map((m) => [m![1], m![4], m![5]]), `"party after the battle": slot 0 at Lv ${RUN.to} with its moves`).toEqual([['0', String(RUN.to), RUN.after.join(',')]])
  expect([idle.party[0].level, idle.party[0].moves], `Lv ${RUN.to} and the moves with the bot idle`).toEqual([RUN.to, RUN.after])
  expect([idle.lock, idle.battleBit], '"the field free": the lock and the battle bit with the bot idle').toEqual([0, 0])

  const clearing = at(CLEARING)
  expect(clearing.map((i) => CLEARING.exec(log[i])![1]), 'Complete: the walk home').toEqual(['already at start'])
  expect(clearing[0], 'Complete: the walk home after the battle').toBeGreaterThan(overs[0])
})
