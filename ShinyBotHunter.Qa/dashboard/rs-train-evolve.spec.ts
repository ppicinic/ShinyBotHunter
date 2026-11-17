// rs-train-evolve, rs-train-evolve-learn and rs-train-evolve-free-slot (docs/specs/rs-navigation-and-training.md, QA;
// FR-14, FR-16, FR-19): the lead at Lv 15 one point short of 16, Standard training to 16, so Mudkip evolves into Marshtomp
// after the battle and the scene offers Mud Shot. QA_RS_VARIANT picks the start and the answer:
// - keep (the default): four moves (Tackle, Growl, Mud-Slap, Water Gun); the scene's prompt refused.
// - learn: the same four; Mud Shot learned over slot 2 (Growl). The operator answers in Discord (rs-training.ts's answerPrompt).
// - free: the save's three moves, so the fourth slot is already empty: Mud Shot learned with text presses only, no prompt.
// QA_RS_ROM=RU-Wild runs it on Ruby.
//
// Depends on T3b (#451), not merged when this was written: Train Type Standard and No Target in the form (rs-training.ts's
// startTraining), and HasOverworldNavigation for RS, without which Complete comes with no `already at start` line. Check
// both after T3b merges.
import { expect, test } from '@playwright/test'
import { snapshotField, OVER } from './rs-wild'
import { MINUTE } from './static'
import {
  answerPrompt, ASKING, CLEARING, FORGET_CLOSED, MARSHTOMP, MOVE_PROMPT, movesOf, oneShort, PARTY_AFTER, PRESS, SCENE, trainingRun, variant,
  writeSlot
} from './rs-training'

const VARIANT = variant(['keep', 'learn', 'free'] as const)
const MUD_SHOT = 341
// The party after the battle prints the national number (the plan's L9); the record holds the internal index, MARSHTOMP.
const MARSHTOMP_NATIONAL = 259
const RUNS = {
  keep: { moves: [33, 45, 189, 55], forget: null, after: [33, 45, 189, 55] },
  learn: { moves: [33, 45, 189, 55], forget: 1, after: [33, MUD_SHOT, 189, 55] },
  free: { moves: null, forget: null, after: [33, 45, 189, MUD_SHOT] }
}
const RUN = RUNS[VARIANT]
const NAME = VARIANT === 'keep' ? 'rs-train-evolve' : VARIANT === 'learn' ? 'rs-train-evolve-learn' : 'rs-train-evolve-free-slot'

test(`RS training ${NAME}: Mudkip to Lv 16 evolves into Marshtomp in the scene, Mud Shot ${VARIANT === 'keep' ? 'refused' : 'learned'}`, async () => {
  test.setTimeout(30 * MINUTE)
  const { idle, log } = await trainingRun(
    async () => {
      // free keeps the save's three moves (checked by the frame's launch read and here by the write's read-back).
      await writeSlot(1, { ...oneShort(16), ...(RUN.moves ? movesOf(...RUN.moves) : {}) })
    },
    { trainType: 'Standard', targetLevel: 16, discord: VARIANT === 'learn' },
    async (run) => {
      if (VARIANT !== 'free') await answerPrompt(run, { n: 1, learner: 0, move: MUD_SHOT, forget: RUN.forget })
      return 'complete'
    }
  )

  const at = (pattern: RegExp): number[] => log.flatMap((l, i) => (pattern.test(l) ? [i] : []))
  const overs = at(OVER)
  expect(overs.length, 'one battle: battle over lines').toBe(1)
  const presses = log.map((l, i) => ({ i, m: PRESS.exec(l) })).filter((p) => p.m !== null).map((p) => ({ i: p.i, what: p.m![1], by: p.m![3] }))
  const boxes = presses.filter((p) => p.what.startsWith('level-up box'))
  expect(boxes.map((p) => `${p.what} / ${p.by}`), 'one level-up box, two confirmed presses').toEqual([
    'level-up box page 1 for slot 0: BattleConfirm / page 2',
    'level-up box page 2 for slot 0: BattleConfirm / the box leaving page 2'
  ])

  // "no B while the scene animates": the scene's presses are A at its text waits, each at window 8 (FR-16) for the
  // evolving slot 0, none at data[0] 2 (which does not wait); and the only B of the whole battle is keep's No at "Delete a
  // move?". The frame's press count (every press has its line) leaves no unlogged press for a B to hide in.
  const scene = at(SCENE).map((i) => ({ i, m: SCENE.exec(log[i])! })).map((s) => ({ i: s.i, slot: s.m[1], step: s.m[2], window: s.m[3] }))
  expect(scene.length, 'the scene\'s text presses').toBeGreaterThan(0)
  expect(scene.filter((s) => s.slot !== '0' || s.window !== '8' || s.step === '2'), '"no B while the scene animates": scene presses not at window 8 for slot 0, or at data[0] 2')
    .toEqual([])
  expect(presses.filter((p) => p.what.startsWith('B ')).map((p) => p.what), '"no B while the scene animates": the B presses of the battle')
    .toEqual(VARIANT === 'keep' ? ['B at Delete a move?'] : [])

  const answer = presses.filter((p) => /Delete a move\?|Stop learning\?|forget/.test(p.what))
  if (VARIANT === 'free') {
    // "with text presses only, no prompt": the scene's texts at 14 (evolved), 19 (learned) and 20 only.
    expect([at(MOVE_PROMPT).length, at(ASKING).length, answer.length], '"no prompt": move prompt, asking and answer lines').toEqual([0, 0, 0])
    expect(presses.filter((p) => p.i > boxes[1].i).map((p) => p.what), '"text presses only": gated presses after the box').toEqual([])
    expect([...new Set(scene.map((s) => s.step))], '"text presses only": the scene\'s text waits, by data[0]').toEqual(['14', '19', '20'])
  } else {
    // "the scene's prompt is seen waiting (task state 21, sub-state 4, question 5)": the bot's snapshot at it (the plan's
    // L2, logged by T2). Question is read only at data[8] 4, so its 5 is the sub-state too. answerPrompt checked the
    // asking line (slot 0, Mud Shot) and the answer against the click, and read the edited Discord message back.
    const prompts = at(MOVE_PROMPT)
    expect(prompts.map((i) => ['Screen', 'Prompt', 'Question', 'PartyMonIndex'].map((f) => snapshotField(MOVE_PROMPT.exec(log[i])![1], f))),
      '"the scene\'s prompt seen waiting": its snapshot').toEqual([['Evolution', '21', '5', '0']])
    const expected = VARIANT === 'keep'
      // "answered No, then Stop learning? (question 10) answered Yes"
      ? ['B at Delete a move? / the prompt leaving Delete a move?', 'BattleConfirm (Yes) at Stop learning? / the prompt leaving Stop learning?']
      // "the prompt answered Yes; the forget-a-move screen's cursor confirmed on the slot"
      : ['BattleConfirm (Yes) at Delete a move? / the prompt leaving Delete a move?', 'Down on the forget-a-move screen 0 -> 1 / the cursor',
          'BattleConfirm on forget slot 1 / the summary task closing']
    expect(answer.map((p) => `${p.what} / ${p.by}`), `"answered ${VARIANT === 'keep' ? 'No' : 'Yes'}": the presses`).toEqual(expected)
    expect(at(FORGET_CLOSED).length, VARIANT === 'keep' ? 'no forget-a-move screen' : 'the forget-a-move screen closed').toBe(VARIANT === 'keep' ? 0 : 1)
    const order = [boxes[1].i, prompts[0], answer[0].i, answer.at(-1)!.i, overs[0]]
    expect(order.every((v, k) => k === 0 || v > order[k - 1]), `the box, the prompt, the answer and the battle over in order: lines ${order}`).toBe(true)
    // After the answer, the scene's texts: keep's one "did not learn" at 14; learn's "1, 2 and… Poof!" and "… forgot …" at
    // 21 (data[8] 7 and 8, which no line carries), then 19 and 20 (research R5, R7).
    expect(scene.filter((s) => s.i > answer.at(-1)!.i).map((s) => s.step), 'the scene\'s text waits after the answer, by data[0]')
      .toEqual(VARIANT === 'keep' ? ['14'] : ['21', '21', '19', '20'])
  }

  // "the party reads Marshtomp (species 284) at Lv 16" with the moves as decided: in the log (the national 259), then in
  // memory with the bot idle (the internal 284).
  const party = log.map((l) => PARTY_AFTER.exec(l)).filter((m) => m !== null)
  expect(party.map((m) => [m![1], m![3], m![4], m![5]]), '"party after the battle": slot 0 Marshtomp at Lv 16 with its moves')
    .toEqual([['0', String(MARSHTOMP_NATIONAL), '16', RUN.after.join(',')]])
  expect([idle.party[0].species, idle.party[0].level, idle.party[0].moves], 'Marshtomp at Lv 16 with its moves, with the bot idle')
    .toEqual([MARSHTOMP, 16, RUN.after])
  expect([idle.lock, idle.battleBit], '"the field free": the lock and the battle bit with the bot idle').toEqual([0, 0])

  const clearing = at(CLEARING)
  expect(clearing.map((i) => CLEARING.exec(log[i])![1]), 'Complete: the walk home').toEqual(['already at start'])
  expect(clearing[0], 'Complete: the walk home after the battle').toBeGreaterThan(overs[0])
})
