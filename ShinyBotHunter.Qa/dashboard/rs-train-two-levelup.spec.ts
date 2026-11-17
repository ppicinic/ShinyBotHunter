import { expect, test } from '@playwright/test'
import {
  answerPrompt, ASKING, type Changes, copyLeadToSlot2, expAt, FORGET_CLOSED, MOVE_PROMPT, movesOf, oneShort, PARTY_AFTER, PRESS,
  type Prompt, trainingRun, variant, writeSlot
} from './rs-training'
import { OVER } from './rs-wild'
import { MINUTE } from './static'

// rs-train-two-levelup and rs-train-two-levelup-learn (docs/specs/rs-navigation-and-training.md, QA table; FR-14, FR-15,
// FR-17, FR-19; #433's plan, part Q1d): Switch training with the trainee (slot 1) and a copy of it as the battler (slot 2),
// both one short of a level, so both level up from the one battle, the trainee's box first (research S8, getter 0 then 1).
// QA_RS_VARIANT:
// - `plain` (the default): both one short of Lv 8, target 8;
// - `learn-free`, the first run of -two-levelup-learn: the trainee one short of Lv 10 with three moves (Water Gun into the
//   free slot, text only), the battler one short of Lv 15 with four (Bide: Yes over slot 2, Mud-Slap, two Downs); target 10;
// - `learn-both`, the second run: both one short of Lv 15 with four moves, two prompts in the one battle for two different
//   Pokémon, both named Mudkip: the trainee's Yes over slot 0 (Tackle, no Down), the battler's No; target 15.
// The Train Switch and No Target choices are enabled by T3b (#451): until it merges the form refuses them, and the run
// fails at startTraining.
const VARIANT = variant(['plain', 'learn-free', 'learn-both'])
const BIDE = 117
const FOUR = movesOf(33, 45, 189, 55)
type Expected = { trainee: Changes; battler: Changes; target: number; events: string[]; prompts: Prompt[]; moves: (number[] | null)[] }
const EXPECTED: Record<typeof VARIANT, Expected> = {
  plain: {
    trainee: { exp: expAt(8) - 1 }, battler: { exp: expAt(8) - 1 }, target: 8,
    events: ['box 0 1', 'box 0 2', 'box 1 1', 'box 1 2'], prompts: [], moves: [null, null]
  },
  'learn-free': {
    trainee: { ...oneShort(10), ...movesOf(33, 45, 189) }, battler: { ...oneShort(15), ...FOUR }, target: 10,
    events: ['box 0 1', 'box 0 2', 'box 1 1', 'box 1 2', 'ask 1'],
    prompts: [{ n: 1, learner: 1, move: BIDE, forget: 2 }],
    moves: [[33, 45, 189, 55], [33, 45, BIDE, 55]]
  },
  'learn-both': {
    trainee: { ...oneShort(15), ...FOUR }, battler: { ...oneShort(15), ...FOUR }, target: 15,
    events: ['box 0 1', 'box 0 2', 'ask 0', 'box 1 1', 'box 1 2', 'ask 1'],
    prompts: [{ n: 1, learner: 0, move: BIDE, forget: 0 }, { n: 2, learner: 1, move: BIDE, forget: null }],
    moves: [[BIDE, 45, 189, 55], [33, 45, 189, 55]]
  }
}
const X = EXPECTED[VARIANT]
// The levels after the battle: one up each, from the level byte the writes leave.
const LEVELS = [X.target, VARIANT === 'learn-free' ? 15 : X.target]

const pressOf = (line: string): string | null => {
  const m = PRESS.exec(line)
  return m ? `${m[1]} | ${m[3]}` : null
}
const isBox = (line: string): boolean => /^level-up box page/.test(pressOf(line) ?? '')

/** The level-up boxes (`box <slot> <page>`) and the move prompts (`ask <slot>`), in log order. */
const events = (log: string[]): string[] => log.flatMap((l) => {
  const box = /^level-up box page (\d) for slot (\d): BattleConfirm \|/.exec(pressOf(l) ?? '')
  const ask = ASKING.exec(l)
  return box ? [`box ${box[2]} ${box[1]}`] : ask ? [`ask ${ask[1]}`] : []
})

/** The n-th prompt's answer presses, from its asking line to the next prompt or the battle's end. */
function answerPresses(log: string[], n: number): { presses: string[]; closed: number } {
  const ask = log.flatMap((l, i) => (ASKING.test(l) ? [i] : []))[n - 1]
  const end = log.findIndex((l, i) => i > ask && (MOVE_PROMPT.test(l) || OVER.test(l)))
  const lines = log.slice(ask, end)
  return {
    presses: lines.map(pressOf).filter((p): p is string => p !== null && /Delete a move\?|Stop learning\?|forget/.test(p)),
    closed: lines.filter((l) => FORGET_CLOSED.test(l)).length
  }
}

/** The rule's presses for a Yes over slot k (A, k Downs, A on k) or a No (B, Yes at Stop learning). */
const answerOf = (forget: number | null): string[] => forget === null
  ? ['B at Delete a move? | the prompt leaving Delete a move?', 'BattleConfirm (Yes) at Stop learning? | the prompt leaving Stop learning?']
  : ['BattleConfirm (Yes) at Delete a move? | the prompt leaving Delete a move?',
    ...Array.from({ length: forget }, (_, i) => `Down on the forget-a-move screen ${i} -> ${i + 1} | the cursor`),
    `BattleConfirm on forget slot ${forget} | the summary task closing`]

test(`rs-train-two-levelup ${VARIANT}: the trainee and the battler level up from one switch battle, each box and prompt for its own slot`, async () => {
  test.setTimeout(X.prompts.length * 10 * MINUTE + 15 * MINUTE)
  const { log, idle } = await trainingRun(async () => {
    await copyLeadToSlot2()
    await writeSlot(1, X.trainee)
    await writeSlot(2, X.battler)
  }, { trainType: 'Switch', targetLevel: X.target, switchSlot: 2, discord: X.prompts.some((p) => p.forget !== null) }, async (run) => {
    // In the order the bot asks: each checked for its learner slot and move.
    for (const prompt of X.prompts) await answerPrompt(run, prompt)
    return 'complete'
  })

  // "a level-up box for each, in the order the game gives the experience", and each prompt after its own learner's box.
  expect(events(log), '"two Pokémon level up in the one battle: a box for each, then its prompt"').toEqual(X.events)
  expect(log.filter((l) => /^level-up box page 1/.test(pressOf(l) ?? '')).map(pressOf), '"each box page 1 confirmed by page 2"')
    .toEqual(['0', '1'].map((s) => `level-up box page 1 for slot ${s}: BattleConfirm | page 2`))
  if (VARIANT === 'learn-free') {
    // "the free-slot move learned with text presses only": no gated press between the trainee's box and the battler's.
    const boxes = log.flatMap((l, i) => (isBox(l) ? [i] : []))
    const between = log.slice(boxes[1] + 1, boxes[2]).map(pressOf).filter((p) => p !== null)
    expect(between, '"Water Gun into the free slot with text presses only": presses between the two boxes').toEqual([])
  }
  for (const prompt of X.prompts) {
    const { presses, closed } = answerPresses(log, prompt.n)
    expect(presses, `"answered by the rule": prompt ${prompt.n} (slot ${prompt.learner})`).toEqual(answerOf(prompt.forget))
    expect(closed, `"the forget-a-move screen closed": prompt ${prompt.n}`).toBe(prompt.forget === null ? 0 : 1)
  }

  // "both levels one higher in the party record afterwards" and "the party's moves as decided": the last battle's party
  // lines, then the read with the bot idle.
  const lastOver = log.flatMap((l, i) => (OVER.test(l) ? [i] : [])).at(-1)!
  const after = log.slice(lastOver).map((l) => PARTY_AFTER.exec(l)).filter((m) => m !== null)
  expect(after.map((m) => [Number(m![1]), Number(m![4])]), '"party after the battle": the levels').toEqual([[0, LEVELS[0]], [1, LEVELS[1]]])
  expect(idle.party.map((p) => p.level), '"both levels one higher", read with the bot idle').toEqual(LEVELS)
  X.moves.forEach((moves, slot) => {
    if (moves === null) return
    expect(after[slot]![5], `"the party's moves as decided": party after the battle, slot ${slot}`).toBe(moves.join(','))
    expect(idle.party[slot].moves, `"the party's moves as decided": slot ${slot}, read with the bot idle`).toEqual(moves)
  })
  expect([idle.lock, idle.battleBit], '"the field free", read with the bot idle').toEqual([0, 0])
})
